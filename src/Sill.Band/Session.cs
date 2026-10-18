using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Threading;
using System.Windows.Forms;

namespace Sill.Band
{
    // band 하나의 sill-host 세션(M1). sill-host를 띄우고, 페이지와 sill-host 사이의 메시지를 내용을 보지 않고 넘긴다.
    // 파이프 읽기와 쓰기는 각각 별도 스레드에서 하고 받은 메시지는 UI 스레드로 넘긴다. 이 스레드들에서 예외가 새면 탐색기가 죽으므로 모두 잡는다.
    sealed class Session : IDisposable
    {
        // 쓰지 못하고 쌓인 글자가 이만큼을 넘으면 sill-host가 멈춘 것으로 보고 끊는다. 탐색기 메모리를 묶어 둔다(0.1.1).
        const long Limit = 16000000;

        readonly Control _ui;
        readonly Action<string> _toPage;
        readonly NamedPipeClientStream _pipe;
        // UI 스레드는 큐에 넣고 바로 돌아온다. sill-host가 앞의 큰 입력을 셸에 넣는 동안 파이프 쓰기는 수 초 막힌다(M2).
        // 쓰는 스레드가 하나라 순서가 지켜진다. 연결 전에 페이지가 보낸 것(첫 크기)도 여기서 기다린다.
        readonly BlockingCollection<string> _out = new BlockingCollection<string>();
        long _queued;                       // 큐에 있는 글자 수
        readonly string _error;             // sill-host를 띄우지 못한 이유
        volatile bool _closed;

        static void Log(string format, params object[] args) { Shared.Log("band.log", format, args); }

        public Session(Control ui, Action<string> toPage, string hostPath)
        {
            _ui = ui;
            _toPage = toPage;
            string name = string.Format("sill-{0}-{1:N}", Process.GetCurrentProcess().Id, Guid.NewGuid());
            // Asynchronous: 쓰기 스레드의 쓰기가 읽기 스레드의 대기 중인 읽기에 막히지 않게 한다.
            _pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            // 터미널을 만드는 곳(SetSite, ShowDW, +, 단축키, 다시 시작)이 모두 여기를 지나므로 실패는 여기서 안내로 바꾼다(M2).
            try
            {
                using (var p = Process.Start(new ProcessStartInfo(hostPath, name) { UseShellExecute = false }))
                    Log("sill-host 실행 pid={0}", p.Id);
            }
            catch (Exception e)
            {
                _error = e.Message;
                Log("sill-host 실행 실패: {0}", e.Message);
                return;
            }
            new Thread(ReadLoop) { IsBackground = true }.Start();
        }

        void ReadLoop()
        {
            try
            {
                _pipe.Connect(10000);
                // 연결을 기다리는 동안 닫혔으면(새 터미널 직후 탭 ×, 창 닫기) 여기서 끝낸다. 읽기로 가면 영원히 기다리고 sill-host도 남는다(M10).
                if (_closed) { _pipe.Dispose(); return; }
                _pipe.ReadMode = PipeTransmissionMode.Message;
                new Thread(WriteLoop) { IsBackground = true }.Start();
                Log("파이프 연결");
                bool first = true;
                string msg;
                while ((msg = Shared.Read(_pipe)) != null)
                {
                    if (first) { first = false; Log("첫 출력"); }
                    Post(msg);
                }
            }
            catch (Exception e)
            {
                if (!_closed) Log("파이프 예외: {0}", e.Message);
            }
            if (!_closed) Log("sill-host 끊김");
            Post("x");
        }

        void WriteLoop()
        {
            try
            {
                foreach (var m in _out.GetConsumingEnumerable())
                {
                    Interlocked.Add(ref _queued, -m.Length);
                    Shared.Write(_pipe, m);
                }
            }
            catch (Exception e)
            {
                if (!_closed) Log("보내기 예외: {0}", e.Message);
            }
        }

        void Post(string m)
        {
            if (_closed) return;
            try
            {
                _ui.BeginInvoke(new Action(() =>
                {
                    try { if (!_closed) _toPage(m); }
                    catch (Exception e) { Log("페이지 전달 예외: {0}", e.Message); }
                }));
            }
            catch (Exception e) { Log("BeginInvoke 예외: {0}", e.Message); }
        }

        public void Send(string m)
        {
            try
            {
                if (_error != null)
                {
                    // 페이지가 그 터미널을 만든 뒤(첫 크기) 알린다. SetSite에서는 아직 페이지가 없다. 키를 누르면 페이지가 다시 시작(n)을 보낸다.
                    if (m[0] == 'r') { Post("o\x1b[90m[Sill] Cannot start sill-host.exe (" + _error + ")\x1b[0m\r\n"); Post("x"); }
                    return;
                }
                if (_out.IsAddingCompleted) return;     // 닫았거나 끊은 세션
                // 하나만 큰 붙여넣기는 걸리지 않도록 넣기 전에 본다. 파이프를 닫으면 읽기 스레드가 x를 보내 다시 시작을 안내한다.
                if (Interlocked.Read(ref _queued) > Limit) { Log("보내기 큐 넘침, 세션 끊음"); _out.CompleteAdding(); _pipe.Dispose(); return; }
                Interlocked.Add(ref _queued, m.Length);
                _out.Add(m);
            }
            catch (Exception e) { Log("보내기 예외: {0}", e.Message); }
        }

        public void Dispose()
        {
            _closed = true;
            _out.CompleteAdding();
            _pipe.Dispose();
        }
    }
}
