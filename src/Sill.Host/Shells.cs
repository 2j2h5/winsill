using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Sill.Host
{
    // 셸 표(M4 설계 3.3절). 셸마다 다른 것은 명령줄(셸 통합 포함)과 Sill이 넣는 cd뿐이다.
    // 셸 통합은 모두 OSC 133;A, 9;9(Windows 경로), 133;B를 내므로 Sync는 셸을 모른다.
    sealed class Shell
    {
        public string Name;
        public string CommandLine;
        public Func<string, string> Cd;     // cd 한 줄(Enter 포함). 그 셸이 갈 수 없는 경로면 null. 셸 통합이 없으면 Cd 자체가 null

        // 프로필 적재 뒤 prompt 함수를 감싼다. -EncodedCommand로 넘기므로 실행 정책에 걸리지 않는다.
        const string PowerShellIntegration = @"
$global:__sill_prompt = $function:prompt
function global:prompt {
  $e = [char]27; $p = $executionContext.SessionState.Path.CurrentLocation
  $s = ""$e]133;A$e\""
  if ($p.Provider.Name -eq 'FileSystem') { $s += ""$e]9;9;$($p.ProviderPath)$e\"" }
  $s + (& $global:__sill_prompt) + ""$e]133;B$e\""
}";

        // --rcfile은 로그인 셸 파일을 읽지 않으므로 직접 읽는다(VS Code와 같음). ST 대신 BEL로 끝낸다(설계 표 1).
        const string BashIntegration = @"[ -r /etc/profile ] && . /etc/profile
if [ -r ~/.bash_profile ]; then . ~/.bash_profile; elif [ -r ~/.bash_login ]; then . ~/.bash_login; elif [ -r ~/.profile ]; then . ~/.profile; fi
__sill_prompt() { local s=$?; printf '\e]133;A\a\e]9;9;%s\a' ""$(cygpath -w ""$PWD"")""; return $s; }
PROMPT_COMMAND=""__sill_prompt${PROMPT_COMMAND:+; $PROMPT_COMMAND}""
PS1=""$PS1""'\[\e]133;B\a\]'
";

        // value: sill-host의 두 번째 인수. 없으면 settings.json의 shell. 고른 셸을 띄울 수 없으면 null.
        public static Shell Choose(string value, Action<string> notice)
        {
            if (value == null) value = FromSettings(notice);
            switch ((value ?? "").ToLowerInvariant())
            {
                case "": return PowerShell(OnPath("pwsh.exe") != null ? "pwsh.exe" : "powershell.exe");
                case "powershell": return PowerShell("powershell.exe");
                case "pwsh": return PowerShell("pwsh.exe");
                case "cmd": return Cmd();
                case "gitbash": return GitBash(notice);
                default: return new Shell { Name = value, CommandLine = value };
            }
        }

        static string FromSettings(Action<string> notice)
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Sill\settings.json");
            if (!File.Exists(path)) return null;
            try
            {
                var settings = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                object shell;
                if (settings == null || !settings.TryGetValue("shell", out shell)) return null;
                if (shell is string) return (string)shell;
                notice("settings.json: shell은 문자열이어야 합니다. 기본 셸로 시작합니다.");
            }
            catch (Exception e) { notice("settings.json을 읽을 수 없습니다(" + e.Message + "). 기본 셸로 시작합니다."); }
            return null;
        }

        static Shell PowerShell(string exe)
        {
            return new Shell
            {
                Name = exe,
                CommandLine = exe + " -NoLogo -NoExit -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(PowerShellIntegration)),
                // PowerShell은 유니코드 작은따옴표(‘ ’ ‚ ‛)도 작은따옴표로 읽으므로 모두 두 번 쓴다.
                Cd = p => "cd -LiteralPath '" + Regex.Replace(p, "['‘’‚‛]", "$0$0") + "'\r",
            };
        }

        static Shell Cmd()
        {
            // AutoRun이 /k보다 먼저 돌므로 prompt는 /k로 넣는다. 사용자의 PROMPT 모양은 가운데에 살린다.
            string user = Environment.GetEnvironmentVariable("PROMPT");
            return new Shell
            {
                Name = "cmd",
                CommandLine = @"cmd.exe /k prompt $e]133;A$e\$e]9;9;$P$e\" + (string.IsNullOrEmpty(user) ? "$P$G" : user) + @"$e]133;B$e\",
                // 대화형 cmd는 따옴표 안에서도 %이름%을 푼다. % 뒤에 ""를 넣으면 이름이 달라져 풀리지 않는다. UNC로는 cd할 수 없다.
                Cd = p => p.StartsWith(@"\\") ? null : "cd /d \"" + p.Replace("%", "%\"\"") + "\"\r",
            };
        }

        static Shell GitBash(Action<string> notice)
        {
            // PATH의 git.exe는 보통 Git\cmd에 있다. 없으면 기본 설치 위치.
            string git = OnPath("git.exe");
            string bash = git == null ? null : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(git), @"..\bin\bash.exe"));
            if (bash == null || !File.Exists(bash))
                bash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Git\bin\bash.exe");
            if (!File.Exists(bash)) { notice("Git Bash(bash.exe)를 찾을 수 없습니다."); return null; }

            string rc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Sill\sill.bash");
            File.WriteAllText(rc, BashIntegration.Replace("\r", ""));    // bash는 CR을 명령의 일부로 읽는다
            return new Shell
            {
                Name = "gitbash " + bash,
                CommandLine = "\"" + bash + "\" --rcfile \"" + rc.Replace('\\', '/') + "\" -i",
                Cd = p => "cd -- '" + p.Replace("'", @"'\''") + "'\r",
            };
        }

        static string OnPath(string exe)
        {
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try { if (dir.Length > 0 && File.Exists(Path.Combine(dir, exe))) return Path.Combine(dir, exe); }
                catch (ArgumentException) { }
            }
            return null;
        }
    }
}
