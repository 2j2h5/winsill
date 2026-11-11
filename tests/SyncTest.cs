using System;
using System.Collections.Generic;
using System.IO;

namespace Sill
{
    // Sync.cs만 함께 빌드한다. 사용자의 host.log에 쓰지 않는다.
    static class Shared { public static void Log(string file, string format, params object[] args) { } }
}

namespace Sill.Host
{
    // Sync 단위 시험. build.ps1이 bin을 만들기 전에 빌드해 돌린다. 셸과 ConPTY 없이 출력 문자열과 입력만으로 판정을 본다.
    static class SyncTest
    {
        const string N = "0123456789abcdef0123456789abcdef";
        static readonly List<string> Writes = new List<string>();
        static string _a, _b, _key;
        static int _items, _failed;

        static string Osc(string s) { return "\x1b]" + s + "\x1b\\"; }
        // PowerShell 셸 통합이 내는 꼴: A, 9;9, 사용자 프롬프트, B
        static string Prompt(string dir) { return Osc("133;A;sill=" + N) + Osc("9;9;" + dir) + "PS " + dir + "> " + Osc("133;B;sill=" + N); }

        // 첫 프롬프트(폴더 a)를 본 대기 상태. 키 파일이 있으므로 cd는 키로 간다(PowerShell·Git Bash).
        static Sync Ready()
        {
            var s = new Sync(N);
            Writes.Clear();
            s.Started(w => Writes.Add(w), p => "cd '" + p + "'\r", _key);
            s.Output(Prompt(_a));
            return s;
        }

        static void Check(string name, bool ok)
        {
            _items++;
            if (!ok) { _failed++; Console.WriteLine("FAIL " + name); }
        }

        static int Main()
        {
            string dir = Path.Combine(Path.GetTempPath(), "sill-sync-test-" + Guid.NewGuid().ToString("N"));
            _a = Directory.CreateDirectory(Path.Combine(dir, "a")).FullName;
            _b = Directory.CreateDirectory(Path.Combine(dir, "b")).FullName;
            _key = Path.Combine(dir, "cd.txt");
            File.WriteAllText(_key, "");
            try { Run(); }
            finally { Directory.Delete(dir, true); }
            Console.WriteLine("Sync 단위 시험 {0}/{1} {2}", _items - _failed, _items, _failed == 0 ? "PASS" : "FAIL");
            return _failed == 0 ? 0 : 1;
        }

        static void Run()
        {
            var s = Ready();
            s.Folder(_b);
            Check("1 진짜 프롬프트 뒤 c → cd", Writes.Count == 1 && File.ReadAllText(_key) == _b);

            // 셸의 cd 뒤 새 프롬프트(폴더 b)를 모든 글자 위치에서 둘로 잘라 보낸다
            string next = Prompt(_b);
            int bad = 0;
            for (int i = 0; i <= next.Length; i++)
            {
                s = Ready();
                s.Input("cd b\r");
                string m1 = s.Output(next.Substring(0, i)), m2 = s.Output(next.Substring(i));
                s.Folder(_a);
                if ((m2 ?? m1) != _b || Writes.Count != 1) bad++;
            }
            Check("2 메시지 경계에서 잘린 프롬프트", bad == 0);

            s = Ready();
            s.Input("cat f\r");
            Check("3 실행 중 출력의 9;9 → p 없음", s.Output(Osc("9;9;" + _b)) == null);

            s = Ready();
            s.Input("x\r");
            string moved = s.Output(Osc("133;A;sill=" + N) + Osc("9;9;" + _a) + "PS " + Osc("9;9;" + _b) + "> " + Osc("133;B;sill=" + N));
            s.Folder(_a);
            Check("4 A 바로 뒤가 아닌 9;9 → p 없음, 셸 폴더 그대로", moved == null && Writes.Count == 0);

            s = Ready();
            s.Input("cat f\r");
            s.Output(Osc("133;A") + "$ \x1b]133;B\a");     // 9;9를 넣으면 되먹임 목록 때문에 다른 이유로 cd가 빠진다
            s.Folder(_b);
            Check("5 난수 없는 A·B → cd 없음", Writes.Count == 0);

            s = Ready();
            s.Input("cat f\r");
            string other = "f" + N.Substring(1);
            moved = s.Output(Osc("133;A;sill=" + other) + Osc("9;9;" + _b) + Osc("133;B;sill=" + other));
            s.Folder(_b);
            Check("6 다른 난수의 A·B → p·cd 없음", moved == null && Writes.Count == 0);

            s = Ready();
            s.Input("ec");
            s.Output(Prompt(_a));
            s.Folder(_b);
            Check("7 입력 중 다시 그린 프롬프트 → cd 없음", Writes.Count == 0);

            s = Ready();
            s.Input("cd b\r");
            moved = s.Output(Prompt(_b));
            s.Folder(_b);
            Check("8 셸의 이동 → p, 돌아온 c → cd 없음", moved == _b && Writes.Count == 0);

            // Sill의 cd 뒤 bash는 A 없이 B만 다시 그린다. 이것으로 대기가 되면 다음 c가 바로 들어간다.
            s = Ready();
            s.Folder(_b);
            s.Output("\x1b]133;B;sill=" + N + "\a");
            s.Folder(_a);
            bool held = Writes.Count == 1;
            s.Output(Prompt(_b));
            Check("9 A 없는 B로는 대기 안 됨", held && Writes.Count == 2);
            Check("10 Sill의 cd가 도는 중 c → 보류 뒤 cd", File.ReadAllText(_key) == _a);

            // 프롬프트를 그리는 중(진짜 A 뒤) 사용자 프롬프트에 섞인 B. 5·6은 A에서 먼저 걸러져 B의 판정을 보지 못한다.
            s = Ready();
            s.Input("x\r");
            s.Output(Osc("133;A;sill=" + N) + Osc("9;9;" + _a) + "PS " + Osc("133;B"));
            s.Folder(_b);
            Check("11 진짜 A 뒤 난수 없는 B → cd 없음", Writes.Count == 0);

            // 0.1.2: 새 페이지에 다시 보낼 모드. ConPTY가 시작할 때 내는 꼴과 앱이 켜는 꼴.
            s = new Sync(N);
            s.Output("\x1b[?9001h\x1b[?1004h\x1b[?25l\x1b[2J\x1b[m\x1b[H" + Prompt(_a) + "\x1b[?25h");
            s.Output("\x1b[?2004h\x1b[?10");
            s.Output("49h\x1b[?1h");
            Check("12 모드의 마지막 값을 정한 순서로(경계에서 나뉨 포함)", s.Modes() == "\x1b[?1049h\x1b[?2004h\x1b[?1004h\x1b[?25h");
            s.Output("\x1b[?1049;2004l");
            Check("13 여러 값을 한 시퀀스로 끔", s.Modes() == "\x1b[?1049l\x1b[?2004l\x1b[?1004h\x1b[?25h");
            s.Output("\x1b[1049h\x1b[?2004r\x1b]0;[?25l\a");
            Check("14 ? 없는 CSI, h·l이 아닌 끝, OSC 안의 글은 모드가 아님", s.Modes() == "\x1b[?1049l\x1b[?2004l\x1b[?1004h\x1b[?25h");

            // 0.1.2: 보관(Scrollback). 크기별로 묶여 나오고, 상한을 넘으면 앞을 버린다.
            var b = new Scrollback(100, 4);
            b.Size("80,24"); b.Add("a\r\n"); b.Add("b"); b.Size("100,30"); b.Add("c");
            var r = b.Replay();
            Check("15 크기별로 묶음", string.Join("|", r) == "z80,24|oa\r\nb|z100,30|oc");
            b = new Scrollback(20, 5);
            b.Size("80,24");
            for (int i = 0; i < 6; i++) b.Add("aaa\r\n");
            Check("16 줄 끝에서 버리면 그대로, 첫 항목은 크기", string.Join("|", b.Replay()) == "z80,24|oaaa\r\naaa\r\naaa\r\naaa\r\n");
            b = new Scrollback(8, 4);
            b.Size("1,1"); b.Add("xxxx"); b.Add("yy\r\n"); b.Add("zzzz");
            Check("17 줄 중간에서 버리면 첫 줄바꿈 뒤부터", string.Join("|", b.Replay()) == "z1,1|ozzzz");

            // 0.2.0 M2: 알람. OSC 밖의 BEL과 알림 OSC만 알람이고, Rang은 Output마다 새로 정해진다.
            s = Ready();
            s.Output("done\a");
            bool rang = s.Rang;
            s.Output("next");
            Check("18 OSC 밖 BEL → 알람, 다음 출력은 새로", rang && !s.Rang);
            s.Output(Prompt(_a) + "\x1b]0;title\a" + "\x1b]133;A;sill=" + N + "\a");
            Check("19 BEL로 끝나는 OSC(제목, 133) → 알람 아님", !s.Rang);
            bool ok = true;
            foreach (var n in new[] { "9;hello", "777;notify;Title;Body", "99;;body", "99;i=1:d=1;body" }) { s.Output(Osc(n)); ok &= s.Rang; }
            foreach (var n in new[] { "9;9;" + _a, "9;4;3;0", "9;", "777;other;x", "99;i=1:d=0;part", "99;i=1:p=?;", "8;;https://x" }) { s.Output(Osc(n)); ok &= !s.Rang; }
            Check("20 알림 OSC(9;글, 777;notify, 99)만 알람", ok);
            // 제목 OSC와 BEL을 모든 글자 위치에서 둘로 잘라 보낸다. 끝의 BEL만 알람이다.
            string mixed = "\x1b]0;a\ab\x1b]2;c\a\a";
            bad = 0;
            for (int i = 0; i < mixed.Length; i++)
            {
                s = Ready();
                s.Output(mixed.Substring(0, i)); bool r1 = s.Rang;
                s.Output(mixed.Substring(i)); bool r2 = s.Rang;
                if (r1 || !r2) bad++;
            }
            Check("21 메시지 경계에서 잘린 OSC의 끝 BEL → 알람 아님", bad == 0);
            s = Ready();
            s.Output("\x1b]52;c;" + new string('A', 6000) + "\a");
            rang = s.Rang;
            s.Output("\a");
            Check("22 4096자를 넘는 OSC의 끝 BEL → 알람 아님, 뒤 BEL은 알람", !rang && s.Rang);
        }
    }
}
