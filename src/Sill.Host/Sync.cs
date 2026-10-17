using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Sill.Host
{
    // 탐색기 → 터미널 동기화(M2 설계 3.3–3.4절). 셸 출력의 OSC 133/9;9와 사용자 입력으로 셸 상태를 보고,
    // 프롬프트에서 기다리는 중(Idle)일 때만 cd를 넣는다. 출력 스레드와 파이프 스레드가 함께 부르므로 lock 안에서만 상태를 바꾼다.
    sealed class Sync
    {
        // 프로필 적재 뒤 prompt 함수를 감싼다. -EncodedCommand로 넘기므로 실행 정책에 걸리지 않는다.
        public const string Integration = @"
$global:__sill_prompt = $function:prompt
function global:prompt {
  $e = [char]27; $p = $executionContext.SessionState.Path.CurrentLocation
  $s = ""$e]133;A$e\""
  if ($p.Provider.Name -eq 'FileSystem') { $s += ""$e]9;9;$($p.ProviderPath)$e\"" }
  $s + (& $global:__sill_prompt) + ""$e]133;B$e\""
}";

        const int Busy = 0, Idle = 1, Typing = 2;
        // xterm.js가 사용자 입력 없이 보내는 응답: 커서 위치, 장치 속성, 포커스 보고
        static readonly Regex Report = new Regex(@"^\x1b\[(\d+;\d+R|\?[\d;]*c|[IO])$");

        readonly object _lock = new object();
        public string StartDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Pty _pty;
        int _state = Busy;      // 프로필 적재 중에는 실행 중으로 본다
        bool _ownCd;            // 실행 중인 것이 Sill이 넣은 cd
        string _pending;        // _ownCd 동안 받은 마지막 폴더
        string _cwd = "";       // 마지막 OSC 9;9
        StringBuilder _osc;     // ESC ] 뒤를 모으는 중
        bool _esc;

        static void Log(string format, params object[] args) { Shared.Log("host.log", format, args); }

        public void Started(Pty pty) { lock (_lock) _pty = pty; }

        public void Folder(string path)
        {
            // zip 안처럼 탐색기에서만 열리는 경로를 거른다. 네트워크 경로는 여기서 늦어질 수 있지만 탐색기와는 상관없다.
            if (!Directory.Exists(path)) { Log("c 건너뜀(폴더 아님) {0}", path); return; }
            lock (_lock)
            {
                if (_pty == null) { StartDir = path; return; }
                // 같은 폴더 비교는 대기 중에만 한다. Sill의 cd가 도는 동안 _cwd는 아직 이전 폴더다.
                if (_state == Idle && SameDir(path, _cwd)) Log("c 건너뜀(같은 폴더) {0}", path);
                else if (_state == Idle) Cd(path);
                else if (_state == Typing) Log("c 건너뜀(입력 중) {0}", path);
                else if (_ownCd) { _pending = path; Log("c 보류 {0}", path); }
                else Log("c 건너뜀(실행 중) {0}", path);
            }
        }

        public void Input(string data)
        {
            lock (_lock)
            {
                if (data.IndexOf('\r') >= 0) { _state = Busy; _ownCd = false; _pending = null; }
                else if (_state == Idle && !Report.IsMatch(data)) _state = Typing;
            }
        }

        // ESC ] … (BEL | ESC \). 시퀀스가 o 메시지 경계에서 나뉠 수 있으므로 상태를 호출 사이에 유지한다.
        public void Output(string text)
        {
            lock (_lock)
            {
                foreach (char ch in text)
                {
                    bool afterEsc = _esc;
                    _esc = ch == '\x1b';
                    if (afterEsc && ch == ']') { _osc = new StringBuilder(); continue; }
                    if (_osc == null) continue;
                    if (ch == '\a' || (afterEsc && ch == '\\')) { Osc(_osc.ToString()); _osc = null; }
                    else if (afterEsc || _osc.Length > 4096) _osc = null;
                    else if (!_esc) _osc.Append(ch);
                }
            }
        }

        void Osc(string s)
        {
            if (s.StartsWith("9;9;")) { _cwd = s.Substring(4).Trim('"'); Log("OSC 9;9 {0}", _cwd); }
            else if (s.StartsWith("133;C")) _state = Busy;
            else if (s == "133;B")
            {
                _state = Idle;
                string next = _ownCd ? _pending : null;
                _ownCd = false;
                _pending = null;
                if (next != null && !SameDir(next, _cwd)) Cd(next);
            }
        }

        static bool SameDir(string a, string b)
        {
            return string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        void Cd(string path)
        {
            // PowerShell은 유니코드 작은따옴표(‘ ’ ‚ ‛)도 작은따옴표로 읽으므로 모두 두 번 쓴다.
            _pty.Write("cd -LiteralPath '" + Regex.Replace(path, "['‘’‚‛]", "$0$0") + "'\r");
            _state = Busy;
            _ownCd = true;
            Log("cd 보냄 {0}", path);
        }
    }
}
