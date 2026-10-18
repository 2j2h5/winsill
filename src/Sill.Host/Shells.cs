using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Sill.Host
{
    // 셸 표(M4). 셸마다 다른 것은 명령줄(셸 통합 포함)과 Sill이 넣는 cd뿐이다.
    // 셸 통합은 모두 OSC 133;A, 9;9(Windows 경로), 133;B를 내므로 Sync는 셸을 모른다.
    // A·B에는 sill-host가 환경 변수 SILL_NONCE로 준 난수를 붙인다. 셸 통합은 값을 셸 안으로 옮기고 환경 변수를 지워,
    // 셸이 띄우는 프로그램은 난수를 모른다.
    sealed class Shell
    {
        public string Name;
        public string Title;                // 셸의 시작 제목. 패널의 탭 제목이 된다(M7)
        public string CommandLine;
        public Func<string, string> Cd;     // cd 한 줄(Enter 포함). 그 셸이 갈 수 없는 경로면 null. 셸 통합이 없으면 Cd 자체가 null

        public static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Sill\settings.json");

        // 프로필 적재 뒤 prompt 함수를 감싼다. -EncodedCommand로 넘기므로 실행 정책에 걸리지 않는다.
        // Sill의 cd는 키 처리기가 받아 프롬프트를 제자리에 다시 그린다. 등록했다는 표시로 $env:SILL_CD 파일을 만든다(M6).
        // InvokePrompt는 PSReadLine이 콘솔 코드 페이지로 쓰므로, 영어 Windows(437)에서는 한글 폴더의 프롬프트와 9;9가 ??가 되었다(0.1.1).
        // 다시 그리는 동안만 UTF-8로 쓴다.
        // 프로필은 이보다 먼저 돌므로 프로필이 띄운 프로세스는 난수를 물려받는다. 프로필은 사용자의 코드다.
        const string PowerShellIntegration = @"
$global:__sill_n = $env:SILL_NONCE; Remove-Item Env:SILL_NONCE -ErrorAction Ignore
$global:__sill_prompt = $function:prompt
function global:prompt {
  $e = [char]27; $p = $executionContext.SessionState.Path.CurrentLocation
  $s = ""$e]133;A;sill=$($global:__sill_n)$e\""
  if ($p.Provider.Name -eq 'FileSystem') { $s += ""$e]9;9;$($p.ProviderPath)$e\"" }
  $s + (& $global:__sill_prompt) + ""$e]133;B;sill=$($global:__sill_n)$e\""
}
try {
  Set-PSReadLineKeyHandler -Chord 'Ctrl+Alt+Shift+F12' -ScriptBlock {
    Set-Location -LiteralPath ([IO.File]::ReadAllText($env:SILL_CD))
    $o = [Console]::OutputEncoding; [Console]::OutputEncoding = [Text.Encoding]::UTF8
    try { [Microsoft.PowerShell.PSConsoleReadLine]::InvokePrompt() } finally { [Console]::OutputEncoding = $o }
  }
  [IO.File]::WriteAllText($env:SILL_CD, '')
} catch { }";

        // --rcfile은 로그인 셸 파일을 읽지 않으므로 직접 읽는다(VS Code와 같음). ST 대신 BEL로 끝낸다.
        // Sill의 cd 키는 cd만 하는 함수(\e[99~)를 부르고 빈 줄로 Enter한다. bind -x 안에서는 프롬프트가 다시 계산되지 않아서다(M6).
        // starship처럼 프롬프트마다 PS1을 새로 쓰는 설정은 133;B를 지운다. PROMPT_COMMAND 배열의 끝(bash 5.1부터 모두 실행)에서
        // B가 없으면 다시 붙인다. 그보다 오래된 bash는 첫 원소만 실행하므로 __sill_b가 돌지 않고 전과 같다(M8).
        // 난수는 사용자 프로필을 읽기 전에 셸 변수로 옮기고 지운다(export하지 않음).
        const string BashIntegration = @"__sill_n=$SILL_NONCE; unset SILL_NONCE
[ -r /etc/profile ] && . /etc/profile
if [ -r ~/.bash_profile ]; then . ~/.bash_profile; elif [ -r ~/.bash_login ]; then . ~/.bash_login; elif [ -r ~/.profile ]; then . ~/.profile; fi
__sill_prompt() { local s=$?; printf '\e]133;A;sill=%s\a\e]9;9;%s\a' ""$__sill_n"" ""$(cygpath -w ""$PWD"")""; return $s; }
PROMPT_COMMAND=""__sill_prompt${PROMPT_COMMAND:+; $PROMPT_COMMAND}""
PS1=""$PS1""'\[\e]133;B;sill='""$__sill_n""'\a\]'
__sill_b() { [[ $PS1 == *'133;B'* ]] || PS1=""$PS1""'\[\e]133;B;sill='""$__sill_n""'\a\]'; }
PROMPT_COMMAND+=(__sill_b)
__sill_cd() { cd -- ""$(cat -- ""$SILL_CD"")""; }
bind -x '""\e[99~"": __sill_cd'
bind '""\e[24;8~"": ""\e[99~\C-m""'
: > ""$SILL_CD""
";

        // value: sill-host의 두 번째 인수. 없으면 settings.json의 shell. 고른 셸을 띄울 수 없으면 null.
        public static Shell Choose(string value, Action<string> notice)
        {
            if (value == null) value = FromSettings(notice);
            string key = (value ?? "").ToLowerInvariant();
            if (key == "") key = OnPath("pwsh.exe") != null ? "pwsh" : "powershell";
            switch (key)
            {
                case "powershell": return PowerShell("powershell.exe");
                case "pwsh": return PowerShell("pwsh.exe");
                case "cmd": return Cmd();
                case "gitbash": return GitBash(notice);
                default: return new Shell { Name = value, CommandLine = value, Title = TitleOf(value) };
            }
        }

        static string FromSettings(Action<string> notice)
        {
            if (!File.Exists(SettingsPath)) return null;
            try
            {
                var settings = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(SettingsPath));
                object shell;
                if (settings == null || !settings.TryGetValue("shell", out shell)) return null;
                if (shell is string) return (string)shell;
                notice("settings.json: \"shell\" must be a string. Starting the default shell.");
            }
            catch (Exception e) { notice("Cannot read settings.json (" + e.Message + "). Starting the default shell."); }
            return null;
        }

        static Shell PowerShell(string exe)
        {
            return new Shell
            {
                Name = exe,
                Title = Path.GetFileNameWithoutExtension(exe),
                CommandLine = exe + " -NoLogo -NoExit -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(PowerShellIntegration)),
                // PowerShell은 유니코드 작은따옴표(‘ ’ ‚ ‛)도 작은따옴표로 읽으므로 모두 두 번 쓴다.
                Cd = p => "cd -LiteralPath '" + Regex.Replace(p, "['‘’‚‛]", "$0$0") + "'\r",
            };
        }

        static Shell Cmd()
        {
            // AutoRun이 /k보다 먼저 돌므로 prompt는 /k로 넣는다. 사용자의 PROMPT 모양은 가운데에 살린다.
            // %SILL_NONCE%는 줄을 읽을 때 풀리므로 prompt에 값이 들어간 뒤 지운다. 난수는 PROMPT 환경 변수에 남아 자식에게 가지만
            // ESC가 $e 두 글자로 들어 있어, 환경을 출력해도 시퀀스가 되지 않는다. $e를 푸는 것은 cmd의 프롬프트뿐이다.
            string user = Environment.GetEnvironmentVariable("PROMPT");
            return new Shell
            {
                Name = "cmd",
                Title = "cmd",
                CommandLine = @"cmd.exe /k prompt $e]133;A;sill=%SILL_NONCE%$e\$e]9;9;$P$e\" + (string.IsNullOrEmpty(user) ? "$P$G" : user) + @"$e]133;B;sill=%SILL_NONCE%$e\& set ""SILL_NONCE=""",
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
            if (!File.Exists(bash)) { notice("Cannot find Git Bash (bash.exe)."); return null; }

            string rc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Sill\sill.bash");
            File.WriteAllText(rc, BashIntegration.Replace("\r", ""));    // bash는 CR을 명령의 일부로 읽는다
            return new Shell
            {
                Name = "gitbash " + bash,
                Title = "bash",
                CommandLine = "\"" + bash + "\" --rcfile \"" + rc.Replace('\\', '/') + "\" -i",
                Cd = p => "cd -- '" + p.Replace("'", @"'\''") + "'\r",
            };
        }

        // 명령줄 첫 낱말(따옴표로 묶였으면 그 안)의 파일 이름. "C:\x\wsl.exe" -d Ubuntu → wsl
        static string TitleOf(string commandLine)
        {
            string s = commandLine.Trim();
            string first = s.StartsWith("\"") ? s.Substring(1).Split('"')[0] : s.Split(' ')[0];
            try { return Path.GetFileNameWithoutExtension(first); }
            catch (ArgumentException) { return first; }
        }

        // 네트워크 폴더(UNC, 연결된 드라이브)는 건너뛴다. 끊겨 있으면 폴더 하나에 20초 넘게 걸리고, 셸 시작 전의 파이프 읽기 스레드를 막는다(M8 후속).
        static string OnPath(string exe)
        {
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try { if (dir.Length > 0 && !OnNetwork(dir) && File.Exists(Path.Combine(dir, exe))) return Path.Combine(dir, exe); }
                catch (ArgumentException) { }
            }
            return null;
        }

        static bool OnNetwork(string dir)
        {
            if (dir.StartsWith(@"\\")) return true;
            string root = Path.GetPathRoot(dir);
            return root.Length > 0 && new DriveInfo(root).DriveType == DriveType.Network;
        }
    }
}
