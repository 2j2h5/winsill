using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Sill.Host
{
    // band 하나에 붙는 셸 세션(M1 설계 3.3절). band가 준 이름으로 파이프 서버를 열고, 첫 r 메시지에서 ConPTY로 셸을 시작한다.
    // 자기 자신을 KILL_ON_JOB_CLOSE 작업 개체에 넣으므로, 이 프로세스가 어떻게 끝나든 셸과 그 자식이 함께 종료된다.
    static class Program
    {
        static void Log(string format, params object[] args) { Shared.Log("host.log", format, args); }

        // 셸 통합이 Sill의 cd 경로를 읽는 파일. 키 처리기를 등록한 셸 통합이 만든다(M6 설계 3.4절).
        static readonly string KeyFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            string.Format(@"Sill\cd-{0}.txt", Process.GetCurrentProcess().Id));

        static int Main(string[] args)
        {
            // keys: 단축키 상주 프로세스, open: 오른쪽 클릭 메뉴(M6 설계 3.1·3.7절). 그 밖에는 band가 준 파이프 이름이다.
            // 인수가 없으면(bin에서 더블클릭) 할 일이 없다(M8).
            if (args.Length == 0) return 0;
            if (args[0] == "keys") return Keys.Run();
            if (args[0] == "open") return Keys.Open(args.Length > 1 ? args[1] : "");
            try { Run(args[0], args.Length > 1 ? args[1] : null); }
            catch (Exception e) { Log("예외: {0}", e); }
            Exit();
            return 0;
        }

        static void Exit()
        {
            try { File.Delete(KeyFile); } catch { }
            Log("종료");
            Environment.Exit(0);
        }

        // shellArg: 셸 값(host-check용). 없으면 settings.json을 따른다(M4 설계 3.4절).
        static void Run(string pipeName, string shellArg)
        {
            Log("시작 pipe={0}", pipeName);
            KillChildrenOnExit();
            File.Delete(KeyFile);
            Environment.SetEnvironmentVariable("SILL_CD", KeyFile);   // 셸이 물려받는다
            // 파이프는 현재 사용자만 연다. 기본 DACL은 Everyone에게 읽기를 준다(M8 설계 3.5절).
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
            // Asynchronous: 읽기(이 스레드)와 쓰기(출력 스레드)가 같은 핸들에서 서로 막지 않게 한다.
            using (var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous, 0, 0, security))
            {
                var wait = pipe.BeginWaitForConnection(null, null);
                if (!wait.AsyncWaitHandle.WaitOne(10000)) { Log("연결 시간 초과"); return; }
                pipe.EndWaitForConnection(wait);

                Pty pty = null;
                Shell shell = null;
                short cols = 0, rows = 0;
                var sync = new Sync();
                // 출력 보내기 스레드: 16 ms에 한 번까지. 쉬는 동안 온 출력은 다음 메시지에 함께 간다(M8 설계 3.1절).
                new Thread(() =>
                {
                    try { while (true) { OutReady.WaitOne(); Flush(pipe); Thread.Sleep(16); } }
                    catch (Exception e) { Log("보내기 중단: {0}", e.Message); }
                }) { IsBackground = true }.Start();
                // 셸이 시작된 뒤의 c는 작업 스레드가 받은 순서대로 처리한다. 끊긴 네트워크 경로의 폴더 확인(약 21 s)이 입력을 막지 않게 한다(M8 설계 3.3절).
                var folders = new BlockingCollection<string>();
                new Thread(() =>
                {
                    foreach (var f in folders.GetConsumingEnumerable())
                        try { sync.Folder(f); }
                        catch (Exception e) { Log("c 처리 예외: {0}", e.Message); }
                }) { IsBackground = true }.Start();
                string m;
                while ((m = Shared.Read(pipe)) != null)
                {
                    if (m.Length == 0) continue;
                    if (m[0] == 'i' && pty != null) { sync.Input(m.Substring(1)); pty.Write(m.Substring(1)); }
                    else if (m[0] == 'c' && pty == null) sync.StartDir = m.Substring(1);   // 확인은 셸을 띄울 때 한 번
                    else if (m[0] == 'c') folders.Add(m.Substring(1));
                    else if (m[0] == 's') OpenSettings(pipe);
                    else if (m[0] == 'r')
                    {
                        var size = m.Substring(1).Split(',');
                        short c = short.Parse(size[0]), r = short.Parse(size[1]);
                        if (pty != null)
                        {
                            // 같은 크기는 새 페이지(WebView2 복구)뿐이다. ConPTY는 크기가 바뀌면 화면을 다시 보내므로 한 칸 바꿨다 되돌린다(M6 설계 3.3절).
                            // 화면과 달리 창 제목은 다시 오지 않으므로 마지막 제목을 먼저 보낸다(M7).
                            if (c == cols && r == rows)
                            {
                                Log("다시 그리기");
                                if (sync.Title != null) Out("\x1b]0;" + sync.Title + "\x07");
                                pty.Resize((short)(c - 1), r);
                            }
                            pty.Resize(c, r);
                            cols = c; rows = r;
                            continue;
                        }
                        cols = c; rows = r;

                        // 설정 문제는 셸 출력보다 먼저 패널에 한 줄로 알린다. 셸을 띄우지 못하면 끝내고, band가 다시 시작을 안내한다.
                        Action<string> notice = s => { Log("안내: {0}", s); Out("\x1b[90m[Sill] " + s + "\x1b[0m\r\n"); };
                        shell = Shell.Choose(shellArg, notice);
                        if (shell == null) { Flush(pipe); pipe.WaitForPipeDrain(); return; }   // 안내 줄을 band가 읽은 뒤에 닫는다
                        // zip 안처럼 탐색기에서만 열리는 폴더, 그 셸이 갈 수 없는 폴더(cmd의 UNC), 답이 없는 네트워크 폴더면 홈에서 시작한다.
                        string dir = sync.StartDir;
                        if (!DirectoryExists(dir, 2000) || (shell.Cd != null && shell.Cd(dir) == null))
                        {
                            Log("시작 폴더 아님 {0}", dir);
                            dir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        }
                        try { pty = Pty.Start(shell.CommandLine, shell.Title, cols, rows, dir); }
                        catch (Win32Exception e) { notice("Cannot start the shell: " + shell.Name + " (" + e.Message + ")"); Flush(pipe); pipe.WaitForPipeDrain(); return; }
                        Log("셸 시작 {0} pid={1} {2}x{3} {4}", shell.Name, pty.Process.Id, cols, rows, dir);
                        sync.Started(pty, shell.Cd, KeyFile);
                        var started = pty;
                        new Thread(() => Pump(started, pipe, sync)) { IsBackground = true }.Start();
                        new Thread(() =>
                        {
                            started.Process.WaitForExit();
                            int code = started.ExitCode;
                            Log("셸 끝남 code={0}", code);
                            // Windows Terminal의 종료 줄 모양(M6 설계 표 8). 셸의 마지막 출력이 먼저 가도록 잠깐 기다린다.
                            Thread.Sleep(100);
                            try
                            {
                                Out(string.Format("\r\n\x1b[90m[process exited with code {0} (0x{0:X8})]\x1b[0m\r\n", code));
                                Flush(pipe);
                                pipe.WaitForPipeDrain();
                            }
                            catch (Exception e) { Log("종료 줄 실패: {0}", e.Message); }
                            Exit();
                        }) { IsBackground = true }.Start();
                    }
                }
                Log("파이프 끊김");
            }
        }

        // 끊긴 네트워크 경로의 확인은 20초 넘게 걸린다. 파이프 읽기 스레드에서 부르므로 ms 안에 답이 없으면 없는 것으로 본다.
        // 확인 작업은 뒤에서 끝나도록 둔다(M8 후속).
        static bool DirectoryExists(string path, int ms)
        {
            var check = Task.Run(() => Directory.Exists(path));
            return check.Wait(ms) && check.Result;
        }

        // 보낼 출력. 출력 조각마다 메시지를 보내면 탐색기의 band가 메시지마다 UI 스레드를 깨운다(대량 출력 중 탐색기 CPU 57 %, M8 설계 2.2절).
        static readonly StringBuilder Pending = new StringBuilder();
        static readonly AutoResetEvent OutReady = new AutoResetEvent(false);

        static void Out(string s)
        {
            lock (Pending) Pending.Append(s);
            OutReady.Set();
        }

        // 모인 출력을 o 하나로 보낸다. 꺼내기와 쓰기를 한 잠금 안에서 하므로, 뒤에 보내는 p·종료 줄이 앞의 출력을 앞지르지 않는다.
        static void Flush(PipeStream pipe)
        {
            lock (Pending)
            {
                if (Pending.Length == 0) return;
                Shared.Write(pipe, "o" + Pending);
                Pending.Clear();
            }
        }

        // ConPTY 출력(UTF-8)을 글자 경계에서 끊어 보낼 출력에 붙인다. 셸이 사용자의 명령으로 폴더를 옮겼으면 p도 보낸다.
        static void Pump(Pty pty, PipeStream pipe, Sync sync)
        {
            try
            {
                var decoder = Encoding.UTF8.GetDecoder();
                var buf = new byte[65536];
                var chars = new char[Encoding.UTF8.GetMaxCharCount(buf.Length)];
                int n;
                while ((n = pty.Output.Read(buf, 0, buf.Length)) > 0)
                {
                    int c = decoder.GetChars(buf, 0, n, chars, 0);
                    if (c == 0) continue;
                    var text = new string(chars, 0, c);
                    string moved = sync.Output(text);
                    Out(text);
                    if (moved == null) continue;
                    // 없는 경로로 이동하면 탐색기가 오류 창을 띄운다(M2 결과 3.3절).
                    if (!Directory.Exists(moved)) { Log("p 건너뜀(폴더 아님) {0}", moved); continue; }
                    Flush(pipe);
                    Shared.Write(pipe, "p" + moved);
                    Log("p 보냄 {0}", moved);
                }
            }
            catch (Exception e) { Log("출력 중단: {0}", e.Message); }
        }

        // Ctrl+,(M6 설계 3.6절). 파일이 없으면 빈 shell(자동 선택)로 만든다. 지금 셸 값을 쓰면 자동 선택이 꺼진다(M8).
        // 메모장은 band가 띄운다. 이 프로세스의 자식은 작업 개체와 함께 끝나기 때문이다.
        static void OpenSettings(PipeStream pipe)
        {
            if (!File.Exists(Shell.SettingsPath))
            {
                File.WriteAllText(Shell.SettingsPath, "{\"shell\": \"\"}\r\n");
                Log("설정 파일 만듦");
            }
            Shared.Write(pipe, "s" + Shell.SettingsPath);
        }

        static void KillChildrenOnExit()
        {
            const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
            const int JobObjectExtendedLimitInformation = 9;
            IntPtr job = Native.CreateJobObject(IntPtr.Zero, null);
            var info = new Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            int size = Marshal.SizeOf(info);
            IntPtr p = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(info, p, false);
            if (!Native.SetInformationJobObject(job, JobObjectExtendedLimitInformation, p, (uint)size)
                || !Native.AssignProcessToJobObject(job, Native.GetCurrentProcess()))
                Log("작업 개체 실패 err={0}", Marshal.GetLastWin32Error());
            Marshal.FreeHGlobal(p);
            // job 핸들은 닫지 않는다. 프로세스가 끝나며 닫힐 때 안의 프로세스(셸, conhost, 그 자식)가 모두 종료된다.
        }
    }

    sealed class Pty
    {
        public FileStream Output;
        public Process Process;
        FileStream _input;
        IntPtr _hpc;
        IntPtr _process;

        public int ExitCode { get { uint code; Native.GetExitCodeProcess(_process, out code); return (int)code; } }

        public static Pty Start(string commandLine, string title, short cols, short rows, string dir)
        {
            SafeFileHandle inRead, inWrite, outRead, outWrite;
            if (!Native.CreatePipe(out inRead, out inWrite, IntPtr.Zero, 0)) throw new Win32Exception();
            if (!Native.CreatePipe(out outRead, out outWrite, IntPtr.Zero, 0)) throw new Win32Exception();

            var pty = new Pty();
            int hr = Native.CreatePseudoConsole(new Native.COORD { X = cols, Y = rows }, inRead, outWrite, 0, out pty._hpc);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);

            const int EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
            var PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = (IntPtr)0x00020016;
            IntPtr listSize = IntPtr.Zero;
            Native.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref listSize);
            var si = new Native.STARTUPINFOEX();
            si.StartupInfo.cb = Marshal.SizeOf(si);
            si.lpAttributeList = Marshal.AllocHGlobal(listSize);
            // ConPTY는 셸이 제목을 정하기 전에 시작 제목을 OSC 0으로 보낸다. 없으면 실행 파일의 전체 경로다(M7 설계 표 6).
            si.StartupInfo.lpTitle = Marshal.StringToHGlobalUni(title);
            try
            {
                if (!Native.InitializeProcThreadAttributeList(si.lpAttributeList, 1, 0, ref listSize)
                    || !Native.UpdateProcThreadAttribute(si.lpAttributeList, 0, PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                        pty._hpc, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                    throw new Win32Exception();
                Native.PROCESS_INFORMATION pi;
                if (!Native.CreateProcess(null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false,
                        EXTENDED_STARTUPINFO_PRESENT, IntPtr.Zero, dir, ref si, out pi))
                    throw new Win32Exception();
                pty.Process = Process.GetProcessById(pi.dwProcessId);
                pty._process = pi.hProcess;     // GetProcessById로 얻은 Process의 ExitCode는 예외를 던지므로 이 핸들로 읽는다(M6에서 찾음)
                Native.CloseHandle(pi.hThread);
            }
            finally
            {
                Native.DeleteProcThreadAttributeList(si.lpAttributeList);
                Marshal.FreeHGlobal(si.lpAttributeList);
                Marshal.FreeHGlobal(si.StartupInfo.lpTitle);
            }
            // 의사 콘솔이 복제해 가졌으므로 이쪽 끝은 닫는다.
            inRead.Dispose();
            outWrite.Dispose();
            pty._input = new FileStream(inWrite, FileAccess.Write, 1);
            pty.Output = new FileStream(outRead, FileAccess.Read, 1);
            return pty;
        }

        // 파이프 읽기 스레드(사용자 입력), 폴더 작업 스레드와 Pump(Sill의 cd)가 함께 부른다. FileStream은 스레드에 안전하지 않다(M8 후속).
        public void Write(string s)
        {
            var b = Encoding.UTF8.GetBytes(s);
            lock (_input)
            {
                _input.Write(b, 0, b.Length);
                _input.Flush();
            }
        }

        public void Resize(short cols, short rows)
        {
            int hr = Native.ResizePseudoConsole(_hpc, new Native.COORD { X = cols, Y = rows });
            Shared.Log("host.log", "크기 {0}x{1} hr=0x{2:X8}", cols, rows, hr);
        }
    }

    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct COORD { public short X; public short Y; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct STARTUPINFO
        {
            public int cb;
            public IntPtr lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr lpAttributeList; }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, int nSize);
        [DllImport("kernel32.dll")]
        public static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC);
        [DllImport("kernel32.dll")]
        public static extern int ResizePseudoConsole(IntPtr hPC, COORD size);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);
        [DllImport("kernel32.dll")]
        public static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CreateProcess(string lpApplicationName, StringBuilder lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
            bool bInheritHandles, int dwCreationFlags, IntPtr lpEnvironment, string lpCurrentDirectory, ref STARTUPINFOEX lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);
        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr hObject);
        [DllImport("kernel32.dll")]
        public static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetInformationJobObject(IntPtr hJob, int infoClass, IntPtr lpInfo, uint cbInfoLength);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);
        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();
    }
}
