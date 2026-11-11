using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Sill.Host
{
    // 에이전트 CLI 알람(0.2.0 M2). Sill은 어떤 프로그램이든 BEL과 알림 OSC를 받는다. 기본으로 알리지 않는 에이전트는 agents가 각자의 방법으로 켠다.
    // Claude Code: Sill 플러그인(claude-plugin\sill, 훅 Stop·PermissionRequest·StopFailure, 에이전트 보기의 Notification agent_completed·agent_needs_input)을 Claude Code의 플러그인 명령으로 설치(Warp와 같은 방식).
    // GitHub Copilot CLI: 사용자 훅 폴더에 sill.json(agentStop·notification). opencode: 전역 플러그인 폴더에 sill.js(session.idle 등).
    // Codex: tui.notifications 기본값이 true라 할 일이 없다. 넣은 훅은 모두 Sill 안(TERM_PROGRAM=Sill)에서만 BEL을 낸다.
    static class Agents
    {
        static void Log(string format, params object[] args) { Shared.Log("host.log", format, args); }

        // Claude Code 플러그인 훅이 셸 없이(exec form) 부른다. Sill 안일 때만 Claude Code에 BEL을 대신 써 달라고 한다.
        // 결정 필드가 없으므로 권한 요청의 흐름은 바뀌지 않는다.
        // 백그라운드 세션(CLAUDE_JOB_DIR)은 Claude Code 데몬 아래에서 돌아 TERM_PROGRAM이 없다. 세션의 BEL은 붙어 있는 클라이언트의 터미널로 가므로,
        // Sill 터미널 안에 claude 클라이언트가 있으면 울린다.
        // ponytail: 어느 클라이언트가 이 세션에 붙었는지는 모름. 다른 터미널에서 붙어 있으면 그 터미널이 울린다. Claude Code가 알려 주면 그것으로 바꾼다.
        public static int Hook()
        {
            if (StillWorking()) return 0;
            bool ring = InSill;
            if (!ring && Environment.GetEnvironmentVariable("CLAUDE_JOB_DIR") != null)
            {
                ring = ClaudeInSill();
                Log("hook 백그라운드 세션: {0}", ring ? "울림" : "Sill 안의 claude 없음");
            }
            if (ring) Console.Out.Write("{\"terminalSequence\":\"\\u0007\"}");
            return 0;
        }

        // Stop은 Claude가 백그라운드 작업(셸, 하위 에이전트, Monitor 등)이나 예약된 깨우기(/loop 등)를 남긴 채 턴을 끝낼 때도 오고,
        // 작업이 끝나면 Claude가 다시 불려 진짜 끝에 Stop이 또 온다. 앞의 것에 울리면 알람이 일하는 중간에 나가고, Sill은 볼 때까지
        // 한 번만 알리므로 진짜 끝은 묻힌다. Stop 입력의 background_tasks·session_crons가 비어 있을 때만 울린다(Claude Code hooks 문서).
        static bool StillWorking()
        {
            if (!Console.IsInputRedirected) return false;   // 콘솔에서 직접 부르면 입력을 기다리지 않음
            try
            {
                string json;
                using (var r = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false))) json = r.ReadToEnd();   // 콘솔 코드 페이지가 아니라 UTF-8, BOM은 뗌
                var input = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(json)
                    as System.Collections.Generic.Dictionary<string, object>;
                object ev;
                if (input == null || !input.TryGetValue("hook_event_name", out ev) || (ev as string) != "Stop") return false;
                var pending = new System.Collections.Generic.List<string>();
                foreach (string key in new[] { "background_tasks", "session_crons" })
                {
                    object list;
                    if (!input.TryGetValue(key, out list) || !(list is object[])) continue;
                    foreach (object item in (object[])list)
                    {
                        var d = item as System.Collections.Generic.Dictionary<string, object>;
                        object type = null, status = null;
                        if (d != null) { d.TryGetValue("type", out type); d.TryGetValue("status", out status); }
                        pending.Add(key == "session_crons" ? "cron" : type + ":" + status);
                    }
                }
                if (pending.Count == 0) return false;
                Log("hook Stop 건너뜀(아직 일하는 중: {0})", string.Join(", ", pending));
                return true;
            }
            catch (Exception e) { Log("hook 입력 못 읽음: {0}", e.Message); return false; }
        }

        static bool ClaudeInSill()
        {
            const uint TH32CS_SNAPPROCESS = 2;
            var parent = new System.Collections.Generic.Dictionary<uint, uint>();
            var name = new System.Collections.Generic.Dictionary<uint, string>();
            IntPtr snap = Native.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snap == new IntPtr(-1)) return false;
            try
            {
                var e = new Native.PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(Native.PROCESSENTRY32)) };
                for (bool ok = Native.Process32First(snap, ref e); ok; ok = Native.Process32Next(snap, ref e))
                {
                    parent[e.th32ProcessID] = e.th32ParentProcessID;
                    name[e.th32ProcessID] = e.szExeFile.ToLowerInvariant();
                }
            }
            finally { Native.CloseHandle(snap); }
            foreach (var p in name)
            {
                if (p.Value != "claude.exe" && p.Value != "node.exe") continue;
                uint id = p.Key;
                for (int depth = 0; depth < 8 && parent.TryGetValue(id, out id) && name.ContainsKey(id); depth++)   // pid 재사용으로 고리가 생길 수 있어 깊이를 묶음
                    if (name[id] == "sill-host.exe") return true;
            }
            return false;
        }

        // BEL을 대신 써 주지 않는 에이전트(Copilot CLI, opencode)의 훅이 부른다. 부모 프로세스(에이전트나 셸)의 콘솔에 붙어 BEL을 쓰면
        // ConPTY를 지나 Sill에 닿는다. 훅의 표준 출력은 에이전트가 가져가므로 콘솔에 직접 쓴다. 콘솔이 없으면 아무것도 하지 않는다.
        public static int Bell()
        {
            const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF, GENERIC_WRITE = 0x40000000, FILE_SHARE_READ_WRITE = 3, OPEN_EXISTING = 3;
            if (!InSill || !Native.AttachConsole(ATTACH_PARENT_PROCESS)) return 0;
            IntPtr h = Native.CreateFile("CONOUT$", GENERIC_WRITE, FILE_SHARE_READ_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h != new IntPtr(-1))
            {
                uint n;
                Native.WriteFile(h, new byte[] { 7 }, 1, out n, IntPtr.Zero);
                Native.CloseHandle(h);
            }
            Native.FreeConsole();
            return 0;
        }

        static bool InSill { get { return Environment.GetEnvironmentVariable("TERM_PROGRAM") == "Sill"; } }

        // sill-host.exe agents [remove] [quiet]. 깔린 에이전트만 설정한다. 사용자가 돌리거나 설치 프로그램에서 고를 때만 바꾼다.
        // quiet는 설치 프로그램이 쓴다(창 없음).
        public static int Run(bool remove, bool quiet)
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            string exe = Path.Combine(dir, "sill-host.exe");
            var report = new StringBuilder();
            bool ok = SetupClaude(remove, dir, exe, report);
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            report.AppendLine(Shell.OnPath("codex.exe") != null || Shell.OnPath("codex.cmd") != null || Directory.Exists(Path.Combine(home, ".codex"))
                ? "Codex: notifications are on by default. Nothing to do." : "Codex: not found.");
            // Copilot CLI는 사용자 훅 폴더의 *.json을 모두 읽는다. COPILOT_HOME이 있으면 그 아래.
            string copilot = Environment.GetEnvironmentVariable("COPILOT_HOME") ?? Path.Combine(home, ".copilot");
            ok &= DropFile("GitHub Copilot CLI", remove, Found("copilot", copilot), Path.Combine(copilot, @"hooks\sill.json"), CopilotHooks(exe), report);
            // opencode는 전역 설정 폴더의 plugins\에 있는 .js를 읽는다. XDG_CONFIG_HOME이 있으면 그 아래.
            string opencode = Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(home, ".config"), "opencode");
            ok &= DropFile("opencode", remove, Found("opencode", opencode), Path.Combine(opencode, @"plugins\sill.js"), OpencodePlugin(exe), report);
            Log("agents{0} {1}: {2}", remove ? " remove" : "", ok ? "완료" : "실패", report.ToString().Replace("\r\n", " | "));
            if (!quiet)
                Native.MessageBox(IntPtr.Zero, (remove ? "Removed Sill alerts from AI coding agents.\n\n" : "Set up Sill alerts for AI coding agents.\n\n") + report,
                    "Sill", ok ? 0x40u : 0x30u);
            return ok ? 0 : 1;
        }

        static bool Found(string command, string configDir)
        {
            return Shell.OnPath(command + ".exe") != null || Shell.OnPath(command + ".cmd") != null || Directory.Exists(configDir);
        }

        // Claude Code는 설치한 플러그인을 자기 캐시로 복사하므로, 훅의 상대 경로(${CLAUDE_PLUGIN_ROOT}/../../sill-host.exe, --plugin-dir용)는
        // 설치본에서 깨진다. sill-host의 절대 경로를 넣은 사본을 %LOCALAPPDATA%\Sill에 만들어 그것을 마켓플레이스로 쓴다(M2 3단계에서 확인).
        static bool SetupClaude(bool remove, string dir, string exe, StringBuilder report)
        {
            string market = Path.Combine(Path.GetDirectoryName(Shell.SettingsPath), "claude-plugin");
            string claude = Shell.OnPath("claude.exe") ?? Shell.OnPath("claude.cmd");
            if (claude == null) { report.AppendLine("Claude Code: not found."); return true; }
            if (remove)
            {
                // 지울 때는 실패해도 계속한다(이미 지웠거나 Claude Code가 없음).
                bool ok = Claude(claude, "plugin uninstall sill@sill", report, true);
                ok &= Claude(claude, "plugin marketplace remove sill", report, true);
                try { if (Directory.Exists(market)) Directory.Delete(market, true); } catch (Exception e) { Log("플러그인 사본 지우기 실패: {0}", e.Message); }
                return ok;
            }
            try { CopyPlugin(Path.Combine(dir, "claude-plugin"), market, exe); }
            catch (Exception e) { report.AppendLine("Claude Code: cannot prepare the plugin: " + e.Message); return false; }
            return Claude(claude, "plugin marketplace add \"" + market + "\"", report) && Claude(claude, "plugin install sill@sill", report);
        }

        // 에이전트가 읽는 폴더에 Sill의 파일 하나를 두거나 지운다. 사용자의 설정 파일은 고치지 않는다.
        static bool DropFile(string name, bool remove, bool found, string path, string content, StringBuilder report)
        {
            try
            {
                if (remove)
                {
                    if (File.Exists(path)) { File.Delete(path); report.AppendLine(name + ": removed " + path); }
                    return true;
                }
                if (!found) { report.AppendLine(name + ": not found."); return true; }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, content);
                report.AppendLine(name + ": wrote " + path);
                return true;
            }
            catch (Exception e) { report.AppendLine(name + ": " + e.Message); return false; }
        }

        static string Json(string s) { return s.Replace("\\", "\\\\").Replace("\"", "\\\""); }

        // agentStop: 응답이 끝남. notification: 권한 요청, 입력 대기 등. 셸 없이(exec) sill-host.exe bell을 부른다.
        static string CopilotHooks(string exe)
        {
            string entry = "{ \"type\": \"command\", \"exec\": \"" + Json(exe) + "\", \"args\": [\"bell\"], \"timeoutSec\": 5 }";
            return "{\n  \"version\": 1,\n  \"hooks\": {\n    \"agentStop\": [" + entry + "],\n    \"notification\": [" + entry + "]\n  }\n}\n";
        }

        // session.idle: 응답이 끝남. permission.*: 권한 요청. Bun 셸($)이 sill-host.exe bell을 부른다. 실패해도 opencode를 방해하지 않는다.
        static string OpencodePlugin(string exe)
        {
            return "// Sill: rings the terminal bell in Sill when opencode finishes or asks for permission. Does nothing outside Sill.\n" +
                   "// Written by \"sill-host.exe agents\"; \"sill-host.exe agents remove\" deletes it.\n" +
                   "const exe = \"" + Json(exe) + "\";\n" +
                   "export const SillAlerts = async ({ $ }) => ({\n" +
                   "  event: async ({ event }) => {\n" +
                   "    if (process.env.TERM_PROGRAM !== \"Sill\") return;\n" +
                   "    if (event.type === \"session.idle\" || event.type.startsWith(\"permission.\")) {\n" +
                   "      try { await $`${exe} bell`.quiet().nothrow(); } catch {}\n" +
                   "    }\n" +
                   "  },\n" +
                   "});\n";
        }

        // claude 명령 하나. 10 s 안에 끝나지 않으면 끝내고 실패로 본다. npm으로 깐 claude.cmd는 cmd가 돌린다.
        // missingOk: 지울 때 이미 없으면("not found") 성공으로 본다.
        static bool Claude(string claude, string args, StringBuilder report, bool missingOk = false)
        {
            var psi = claude.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo("cmd.exe", "/d /s /c \"\"" + claude + "\" " + args + "\"")
                : new ProcessStartInfo(claude, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            try
            {
                using (var p = Process.Start(psi))
                {
                    var output = p.StandardOutput.ReadToEndAsync();
                    var error = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(10000)) { try { p.Kill(); } catch { } report.AppendLine("Claude Code: claude " + args + ": timed out"); return false; }
                    string text = (output.Result + error.Result).Trim();
                    bool ok = p.ExitCode == 0 || (missingOk && text.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0);
                    report.AppendLine("Claude Code: claude " + args + ": " + (ok ? "ok" : "exit " + p.ExitCode) + (text.Length > 0 ? " (" + Short(text) + ")" : ""));
                    return ok;
                }
            }
            catch (Exception e) { report.AppendLine("Claude Code: claude " + args + ": " + e.Message); return false; }
        }

        // 설치 폴더의 플러그인을 복사하며 hooks.json의 상대 경로를 sill-host의 절대 경로(JSON 문자열)로 바꾼다.
        static void CopyPlugin(string from, string to, string exe)
        {
            const string Relative = "${CLAUDE_PLUGIN_ROOT}/../../sill-host.exe";
            foreach (var src in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string dst = Path.Combine(to, src.Substring(from.Length).TrimStart('\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                string text = File.ReadAllText(src);
                if (Path.GetFileName(src) == "hooks.json") text = text.Replace(Relative, Json(exe));
                File.WriteAllText(dst, text);
            }
        }

        static string Short(string s)
        {
            s = s.Replace("\r", "").Replace("\n", " ");
            return s.Length > 200 ? s.Substring(0, 200) + "…" : s;
        }

        static class Native
        {
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int MessageBox(IntPtr hwnd, string text, string caption, uint type);
            [DllImport("kernel32.dll", SetLastError = true)] public static extern bool AttachConsole(uint pid);
            [DllImport("kernel32.dll")] public static extern bool FreeConsole();
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
            [DllImport("kernel32.dll", SetLastError = true)] public static extern bool WriteFile(IntPtr h, byte[] buf, uint count, out uint written, IntPtr overlapped);
            [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
            [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
            [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode)] public static extern bool Process32First(IntPtr snap, ref PROCESSENTRY32 e);
            [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode)] public static extern bool Process32Next(IntPtr snap, ref PROCESSENTRY32 e);
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public struct PROCESSENTRY32
            {
                public uint dwSize, cntUsage, th32ProcessID;
                public IntPtr th32DefaultHeapID;
                public uint th32ModuleID, cntThreads, th32ParentProcessID;
                public int pcPriClassBase;
                public uint dwFlags;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
            }
        }
    }
}
