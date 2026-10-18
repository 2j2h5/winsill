using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Sill.Band
{
    // Session 단위 시험(0.1.1). build.ps1이 Session.cs, Shared.cs(SILL_TEST: 로그 안 씀)와 함께 빌드해 돌린다.
    // 이 exe가 자기 자신을 가짜 sill-host로 띄운다. 인수가 파이프 이름 하나면 서버이고, 하는 일은 환경 변수 SILL_TEST_MODE가 정한다.
    static class SessionTest
    {
        static int _items, _failed;
        static readonly List<string> Got = new List<string>();

        static void Check(string name, bool ok)
        {
            _items++;
            if (!ok) { _failed++; Console.WriteLine("FAIL " + name); }
        }

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 1) { Serve(args[0]); return 0; }
            try { Run(); }
            finally
            {
                // 아직 자는 가짜 서버를 정리한다.
                var self = Process.GetCurrentProcess();
                foreach (var p in Process.GetProcessesByName(self.ProcessName))
                    if (p.Id != self.Id) try { p.Kill(); } catch { }
            }
            Console.WriteLine("Session 단위 시험 {0}/{1} {2}", _items - _failed, _items, _failed == 0 ? "PASS" : "FAIL");
            return _failed == 0 ? 0 : 1;
        }

        // late: 1 s 동안 읽지 않다가 읽고, 번호가 순서대로인지 "o<개수> ok|bad"로 돌려준다. stuck: 읽지 않고 10 s 잔다.
        static void Serve(string name)
        {
            using (var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous, 65536, 65536))
            {
                pipe.WaitForConnection();
                if (Environment.GetEnvironmentVariable("SILL_TEST_MODE") == "stuck") { Thread.Sleep(10000); return; }
                Thread.Sleep(1000);
                bool ok = true;
                for (int i = 0; i < 300; i++)
                {
                    string m = Shared.Read(pipe);
                    if (m == null || !m.StartsWith("i" + i.ToString("D4"))) ok = false;
                }
                Shared.Write(pipe, "o300 " + (ok ? "ok" : "bad"));
                Thread.Sleep(1000);
            }
        }

        static Session Make(Control ui, string mode, string hostPath)
        {
            Environment.SetEnvironmentVariable("SILL_TEST_MODE", mode);
            Got.Clear();
            return new Session(ui, m => Got.Add(m), hostPath);
        }

        // 페이지로 가는 메시지는 UI 스레드로 오므로 메시지 루프를 돌리며 기다린다.
        static bool WaitFor(Func<bool> cond, int ms)
        {
            var sw = Stopwatch.StartNew();
            while (!cond() && sw.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(5); }
            return cond();
        }

        // 메시지마다 Send에 걸린 가장 긴 시간
        static double SendAll(Session s, int count, Func<int, string> message)
        {
            double worst = 0;
            for (int i = 0; i < count; i++)
            {
                var m = message(i);
                var sw = Stopwatch.StartNew();
                s.Send(m);
                worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds);
            }
            return worst;
        }

        static void Run()
        {
            string self = Process.GetCurrentProcess().MainModule.FileName;
            var ui = new Control();
            ui.CreateControl();
            string pad = new string('x', 1019);

            // 1, 2: 0.1.0은 서버가 읽지 않으면 65번째 1 KB에서 막혔다(M2).
            // 연결 전의 것은 0.1.0도 큐(_pending)에 넣었으므로, 첫 메시지로 연결을 기다린 뒤 보낸다. 서버는 연결 뒤 1 s 동안 읽지 않는다.
            var s = Make(ui, "late", self);
            s.Send("i0000" + pad);
            Thread.Sleep(300);
            double worst = SendAll(s, 299, i => "i" + (i + 1).ToString("D4") + pad);
            Check("1 읽지 않는 동안 Send가 막히지 않음 (가장 긴 것 " + worst.ToString("F1") + " ms)", worst < 50);
            Check("2 보낸 순서대로 도착", WaitFor(() => Got.Contains("o300 ok"), 10000));
            s.Dispose();

            // 3: 하나만 큰 것은 통과하고, 쌓인 것이 상한을 넘으면 끊고 x. 연결된 뒤(쓰기 스레드가 막힌 뒤)의 일이다.
            s = Make(ui, "stuck", self);
            s.Send("i");
            Thread.Sleep(500);
            string big = "i" + new string('x', 999999);
            worst = SendAll(s, 20, i => big);
            Check("3 상한을 넘으면 x (Send 가장 긴 것 " + worst.ToString("F1") + " ms)", worst < 50 && WaitFor(() => Got.Contains("x"), 1000));
            s.Dispose();

            // 4: 쓰기가 막힌 동안 닫기
            s = Make(ui, "stuck", self);
            s.Send("i");
            Thread.Sleep(300);
            SendAll(s, 200, i => "i" + pad);
            Thread.Sleep(500);
            var sw = Stopwatch.StartNew();
            s.Dispose();
            long ms = sw.ElapsedMilliseconds;
            Check("4 쓰기가 막힌 동안 Dispose (" + ms + " ms)", ms < 100);

            // 5: sill-host를 띄우지 못함
            Session bad = null;
            try { bad = Make(ui, "late", @"C:\sill-test-missing\sill-host.exe"); } catch (Exception e) { Console.WriteLine(e.Message); }
            Check("5 생성자가 예외를 내지 않음", bad != null);
            if (bad != null)
            {
                bad.Send("r80,24");
                Check("5 첫 r에 안내와 x", WaitFor(() => Got.Count == 2, 1000) && Got[0].StartsWith("o") && Got[0].Contains("Cannot start sill-host.exe") && Got[1] == "x");
                bad.Dispose();
            }
            Application.DoEvents();
        }
    }
}
