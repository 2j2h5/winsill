using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Sill.Host
{
    // 탐색기 → 터미널 동기화(M2). 셸 출력의 OSC 133/9;9와 사용자 입력으로 셸 상태를 보고,
    // 프롬프트에서 기다리는 중(Idle)일 때만 cd를 넣는다. 출력 스레드와 파이프 스레드가 함께 부르므로 lock 안에서만 상태를 바꾼다.
    // 터미널 → 탐색기(M3): 사용자의 명령으로 바뀐 폴더를 Output이 돌려주고, 그 경로를 되먹임 목록에 넣는다.
    // 셸마다 다른 cd 형식은 Shells.cs의 셸 표가 준다(M4).
    // 셸에서 돈 프로그램도 출력에 같은 시퀀스를 섞을 수 있다(악성 파일을 cat, ssh 안의 셸). 그래서 이 세션의 난수가 붙은
    // 133;A·B만 믿고, 9;9는 진짜 A 바로 다음 OSC일 때만 믿는다. 난수는 셸 통합만 안다(VS Code의 633 nonce와 같은 방식).
    sealed class Sync
    {
        const int Busy = 0, Idle = 1, Typing = 2;
        // xterm.js가 사용자 입력 없이 보내는 응답: 커서 위치, 장치 속성, 포커스 보고
        static readonly Regex Report = new Regex(@"^\x1b\[(\d+;\d+R|\?[\d;]*c|[IO])$");

        readonly object _lock = new object();
        public string StartDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // 셸 통합의 키 처리기가 받는 키(Ctrl+Alt+Shift+F12). 경로는 _keyFile로 건넨다(M6).
        const string CdKey = "\x1b[24;8~";
        readonly string _a, _b;  // 진짜 133;A·B. 셸 통합이 내는 꼴 그대로
        Action<string> _write;  // 셸 입력(ConPTY)
        Func<string, string> _cd;
        string _keyFile;
        int _state = Busy;      // 프로필 적재 중에는 실행 중으로 본다
        bool _ownCd;            // 실행 중인 것이 Sill이 넣은 cd
        bool _prompt;           // 실행 중이 된 뒤 새 프롬프트의 시작(133;A)을 봤다
        string _pending;        // _ownCd 동안 받은 마지막 폴더
        string _cwd = "";       // 마지막 OSC 9;9
        string _moved;          // 이번 Output에서 탐색기에 알릴 폴더
        // p로 보낸 폴더와 보낸 시각. 탐색기가 따라온 뒤 돌려주는 c를 버린다. 따라오지 못한(BrowseObject 실패) 항목이
        // 나중의 같은 폴더 이동을 삼키지 않도록 2초 지난 항목은 맞춰 보지 않는다. 따라오는 c는 M3에서 중앙값 56.5 ms였다(M8).
        readonly Queue<Tuple<string, DateTime>> _echo = new Queue<Tuple<string, DateTime>>();
        static readonly TimeSpan EchoLife = TimeSpan.FromSeconds(2);
        StringBuilder _osc;     // ESC ] 뒤를 모으는 중
        bool _esc;
        bool _afterA;           // 바로 앞 OSC(제목 빼고)가 진짜 133;A
        bool _logged133, _logged99;   // 버린 시퀀스는 세션마다 처음 한 번만 적는다. 시퀀스 수만 개를 담은 파일도 있다

        public Sync(string nonce)
        {
            _a = "133;A;sill=" + nonce;
            _b = "133;B;sill=" + nonce;
        }

        static void Log(string format, params object[] args) { Shared.Log("host.log", format, args); }

        public void Started(Action<string> write, Func<string, string> cd, string keyFile) { lock (_lock) { _write = write; _cd = cd; _keyFile = keyFile; } }

        // 셸이 시작된 뒤의 c. sill-host의 폴더 작업 스레드가 받은 순서대로 부른다(M8). 시작 전의 c는 StartDir에 바로 들어간다.
        public void Folder(string path)
        {
            // 셸의 이동(p)을 탐색기가 따라온 것이면 버린다. Dolphin의 m_sendCdToTerminalHistory와 같이 맞는 항목 앞의 것도 함께 버린다.
            lock (_lock)
                while (_echo.Count > 0)
                {
                    var e = _echo.Dequeue();
                    if (SameDir(e.Item1, path) && DateTime.UtcNow - e.Item2 < EchoLife) { Log("c 건너뜀(셸이 옮김) {0}", path); return; }
                }
            // zip 안처럼 탐색기에서만 열리는 경로를 거른다. 끊긴 네트워크 경로면 여기서 20초 넘게 걸리므로 작업 스레드에서 부른다.
            if (!Directory.Exists(path)) { Log("c 건너뜀(폴더 아님) {0}", path); return; }
            lock (_lock)
            {
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
                if (data.IndexOf('\r') >= 0 || data.IndexOf('\x03') >= 0) { _state = Busy; _prompt = false; _ownCd = false; _pending = null; }
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
                // 한 번에 읽은 출력에 프롬프트가 여럿이면(cd 두 줄을 붙여 넣음) 마지막 폴더만 알린다. 되먹임 목록에도 알린 것만 넣는다.
                // 전에는 중간 폴더도 넣어 탐색기가 돌려주지 않는 항목이 남았다(M8 시험에서 찾음).
                if (_moved != null) _echo.Enqueue(Tuple.Create(_moved, DateTime.UtcNow));
                return _moved;
            }
        }

        // 마지막 창 제목(OSC 0·2). ConPTY는 제목을 바뀔 때만 보내므로, 새 페이지(WebView2 복구)에는 sill-host가 다시 보낸다(M7).
        public volatile string Title;

        void Osc(string s)
        {
            // 제목은 ConPTY가 따로 끼워 보낼 수 있으므로 _afterA를 건드리지 않는다.
            if (s.StartsWith("0;") || s.StartsWith("2;")) { Title = s.Substring(2); return; }
            bool afterA = _afterA;
            _afterA = false;
            if (s.StartsWith("9;9;"))
            {
                // 셸 통합은 A와 9;9를 한 번에 쓴다. A와 B 사이에는 사용자 프롬프트(남이 정한 저장소 이름 등)가 그려지므로 그 안의 9;9는 믿지 않는다.
                // 버린 경로는 존재 확인도 하지 않는다. \서버\공유면 확인만으로 그 서버에 접속한다.
                if (!afterA) { if (!_logged99) { _logged99 = true; Log("9;9 무시(프롬프트 밖)"); } return; }
                string prev = _cwd;
                _cwd = s.Substring(4).Trim('"');
                Log("OSC 9;9 {0}", _cwd);
                // 첫 프롬프트와 Sill이 넣은 cd의 결과는 알리지 않는다. OSC 9;9는 133;B보다 먼저 오므로 _ownCd가 아직 켜져 있다.
                if (prev.Length > 0 && !SameDir(prev, _cwd) && !_ownCd) _moved = _cwd;
            }
            else if (s == _a) { _prompt = true; _afterA = true; }
            else if (s == _b)
            {
                // Ctrl+L은 입력 줄을 둔 채 프롬프트를 다시 그린다(PSReadLine은 prompt를 다시 부르고, bash는 PS1만 다시 낸다). 입력 중이면 그대로 둔다(M4).
                if (_state == Typing) { Log("입력 중 유지(프롬프트 다시 그림)"); return; }
                // bash는 bind -x 함수(Sill의 cd 키) 뒤에 줄을 다시 그리며 133;A 없이 133;B만 낸다. 실행이 끝난 것은 새 프롬프트뿐이다(M6).
                if (_state == Busy && !_prompt) { Log("133;A 없는 133;B 무시"); return; }
                _state = Idle;
                string next = _ownCd ? _pending : null;
                _ownCd = false;
                _pending = null;
                if (next != null && !SameDir(next, _cwd)) Cd(next);
            }
            else if (s.StartsWith("133;") && !_logged133) { _logged133 = true; Log("133 무시(난수 없음) {0}", s.Length > 40 ? s.Substring(0, 40) : s); }
        }

        static bool SameDir(string a, string b)
        {
            return string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        void Cd(string path)
        {
            string line = _cd == null ? null : _cd(path);
            if (line == null) { Log("c 건너뜀(셸이 못 감) {0}", path); return; }
            // 셸 통합이 키 처리기를 등록했으면(파일이 있으면) 키로 보내 화면과 명령 기록에 cd가 남지 않게 한다. 없으면(cmd) 한 줄을 입력한다.
            if (File.Exists(_keyFile)) { File.WriteAllText(_keyFile, path); _write(CdKey); Log("cd 보냄(키) {0}", path); }
            else { _write(line); Log("cd 보냄 {0}", path); }
            _state = Busy;
            _prompt = false;
            _ownCd = true;
        }
    }
}
