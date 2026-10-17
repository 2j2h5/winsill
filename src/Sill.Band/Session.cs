using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipes;
using System.Threading;
using System.Windows.Forms;

namespace Sill.Band
{
    // band 하나의 sill-host 세션(M1 설계 3.2절). sill-host를 띄우고, 페이지와 sill-host 사이의 메시지를 내용을 보지 않고 넘긴다.
    // 파이프 읽기는 별도 스레드에서 하고 받은 메시지는 UI 스레드로 넘긴다. 이 스레드에서 예외가 새면 탐색기가 죽으므로 모두 잡는다.
    sealed class Session : IDisposable
    {
        readonly Control _ui;
        readonly Action<string> _toPage;
        readonly NamedPipeClientStream _pipe;
        readonly List<string> _pending = new List<string>();   // 연결 전에 페이지가 보낸 메시지(첫 크기)
        bool _connected;
        volatile bool _closed;

        static void Log(string format, params object[] args) { Shared.Log("band.log", format, args); }

        public Session(Control ui, Action<string> toPage, string hostPath)
        {
            _ui = ui;
            _toPage = toPage;
            string name = string.Format("sill-{0}-{1:N}", Process.GetCurrentProcess().Id, Guid.NewGuid());
            // Asynchronous: UI 스레드의 쓰기가 읽기 스레드의 대기 중인 읽기에 막히지 않게 한다.
            _pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            using (var p = Process.Start(new ProcessStartInfo(hostPath, name) { UseShellExecute = false }))
                Log("sill-host 실행 pid={0}", p.Id);
            new Thread(ReadLoop) { IsBackground = true }.Start();
        }

        void ReadLoop()
        {
            try
            {
                _pipe.Connect(10000);
                _pipe.ReadMode = PipeTransmissionMode.Message;
                lock (_pending)
                {
                    foreach (var m in _pending) Shared.Write(_pipe, m);
                    _pending.Clear();
                    _connected = true;
                }
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
                lock (_pending)
                {
                    if (!_connected) { _pending.Add(m); return; }
                }
                Shared.Write(_pipe, m);
            }
            catch (Exception e) { Log("보내기 예외: {0}", e.Message); }
        }

        public void Dispose()
        {
            _closed = true;
            _pipe.Dispose();
        }
    }
}
