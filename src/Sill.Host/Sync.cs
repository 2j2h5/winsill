using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Sill.Host
{
    // 탐색기 → 터미널 동기화(M2 설계 3.3–3.4절). 셸 출력의 OSC 133/9;9와 사용자 입력으로 셸 상태를 보고,
    // 프롬프트에서 기다리는 중(Idle)일 때만 cd를 넣는다. 출력 스레드와 파이프 스레드가 함께 부르므로 lock 안에서만 상태를 바꾼다.
    // 터미널 → 탐색기(M3 설계 3.2–3.3절): 사용자의 명령으로 바뀐 폴더를 Output이 돌려주고, 그 경로를 되먹임 목록에 넣는다.
    // 셸마다 다른 cd 형식은 Shells.cs의 셸 표가 준다(M4 설계 3.3절).
    sealed class Sync
    {
        const int Busy = 0, Idle = 1, Typing = 2;
        // xterm.js가 사용자 입력 없이 보내는 응답: 커서 위치, 장치 속성, 포커스 보고
        static readonly Regex Report = new Regex(@"^\x1b\[(\d+;\d+R|\?[\d;]*c|[IO])$");

        readonly object _lock = new object();
        public string StartDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Pty _pty;
        Func<string, string> _cd;
        int _state = Busy;      // 프로필 적재 중에는 실행 중으로 본다
        bool _ownCd;            // 실행 중인 것이 Sill이 넣은 cd
        string _pending;        // _ownCd 동안 받은 마지막 폴더
        string _cwd = "";       // 마지막 OSC 9;9
        string _moved;          // 이번 Output에서 탐색기에 알릴 폴더
        readonly Queue<string> _echo = new Queue<string>();   // p로 보낸 폴더. 탐색기가 따라온 뒤 돌려주는 c를 버린다
        StringBuilder _osc;     // ESC ] 뒤를 모으는 중
        bool _esc;

        static void Log(string format, params object[] args) { Shared.Log("host.log", format, args); }

        public void Started(Pty pty, Func<string, string> cd) { lock (_lock) { _pty = pty; _cd = cd; } }

        public void Folder(string path)
        {
            // 셸의 이동(p)을 탐색기가 따라온 것이면 버린다. Dolphin의 m_sendCdToTerminalHistory와 같이 맞는 항목 앞의 것도 함께 버린다.
            lock (_lock)
                while (_echo.Count > 0)
                    if (SameDir(_echo.Dequeue(), path)) { Log("c 건너뜀(셸이 옮김) {0}", path); return; }
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
                // Ctrl+C도 줄을 끝낸다. 입력 중에 누르면 셸이 줄을 버리고 새 프롬프트를 낸다.
                if (data.IndexOf('\r') >= 0 || data.IndexOf('\x03') >= 0) { _state = Busy; _ownCd = false; _pending = null; }
                else if (_state == Idle && !Report.IsMatch(data)) _state = Typing;
            }
        }

        // ESC ] … (BEL | ESC \). 시퀀스가 o 메시지 경계에서 나뉠 수 있으므로 상태를 호출 사이에 유지한다.
        // 탐색기에 알릴 폴더가 있으면 돌려준다. 존재 확인은 lock 밖에서 부르는 쪽이 한다.
        public string Output(string text)
        {
            lock (_lock)
            {
                _moved = null;
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
                return _moved;
            }
        }

        void Osc(string s)
        {
            if (s.StartsWith("9;9;"))
            {
                string prev = _cwd;
                _cwd = s.Substring(4).Trim('"');
                Log("OSC 9;9 {0}", _cwd);
                // 첫 프롬프트와 Sill이 넣은 cd의 결과는 알리지 않는다. OSC 9;9는 133;B보다 먼저 오므로 _ownCd가 아직 켜져 있다.
                if (prev.Length > 0 && !SameDir(prev, _cwd) && !_ownCd) { _moved = _cwd; _echo.Enqueue(_cwd); }
            }
            else if (s.StartsWith("133;C")) _state = Busy;
            else if (s == "133;B")
            {
                // Ctrl+L은 입력 줄을 둔 채 프롬프트를 다시 그린다(PSReadLine은 prompt를 다시 부르고, bash는 PS1만 다시 낸다). 입력 중이면 그대로 둔다(M4).
                if (_state == Typing) { Log("입력 중 유지(프롬프트 다시 그림)"); return; }
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
            string line = _cd == null ? null : _cd(path);
            if (line == null) { Log("c 건너뜀(셸이 못 감) {0}", path); return; }
            _pty.Write(line);
            _state = Busy;
            _ownCd = true;
            Log("cd 보냄 {0}", path);
        }
    }
}
