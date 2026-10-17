using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Sill.Host
{
    // band 하나에 붙는 셸 세션(M1 설계 3.3절). band가 준 이름으로 파이프 서버를 열고, 첫 r 메시지에서 ConPTY로 셸을 시작한다.
    // 자기 자신을 KILL_ON_JOB_CLOSE 작업 개체에 넣으므로, 이 프로세스가 어떻게 끝나든 셸과 그 자식이 함께 종료된다.
    static class Program
    {
        static void Log(string format, params object[] args) { Shared.Log("host.log", format, args); }

        static int Main(string[] args)
        {
            try { Run(args[0], args.Length > 1 ? args[1] : null); }
            catch (Exception e) { Log("예외: {0}", e); }
            Log("종료");
            return 0;
        }

        // shellArg: 셸 값(host-check용). 없으면 settings.json을 따른다(M4 설계 3.4절).
        static void Run(string pipeName, string shellArg)
        {
            Log("시작 pipe={0}", pipeName);
            KillChildrenOnExit();
            // Asynchronous: 읽기(이 스레드)와 쓰기(출력 스레드)가 같은 핸들에서 서로 막지 않게 한다.
            using (var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous))
            {
                var wait = pipe.BeginWaitForConnection(null, null);
                if (!wait.AsyncWaitHandle.WaitOne(10000)) { Log("연결 시간 초과"); return; }
                pipe.EndWaitForConnection(wait);

                Pty pty = null;
                var sync = new Sync();
                string m;
                while ((m = Shared.Read(pipe)) != null)
                {
                    if (m.Length == 0) continue;
                    if (m[0] == 'i' && pty != null) { sync.Input(m.Substring(1)); pty.Write(m.Substring(1)); }
                    else if (m[0] == 'c') sync.Folder(m.Substring(1));
                    else if (m[0] == 'r')
                    {
                        var size = m.Substring(1).Split(',');
                        short cols = short.Parse(size[0]), rows = short.Parse(size[1]);
                        if (pty != null) { pty.Resize(cols, rows); continue; }

                        // 설정 문제는 셸 출력보다 먼저 패널에 한 줄로 알린다. 셸을 띄우지 못하면 끝내고, band가 다시 시작을 안내한다.
                        Action<string> notice = s => { Log("안내: {0}", s); Shared.Write(pipe, "o\x1b[90m[Sill] " + s + "\x1b[0m\r\n"); };
                        var shell = Shell.Choose(shellArg, notice);
                        if (shell == null) { pipe.WaitForPipeDrain(); return; }   // 안내 줄을 band가 읽은 뒤에 닫는다
                        string dir = sync.StartDir;
                        if (shell.Cd != null && shell.Cd(dir) == null) dir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        try { pty = Pty.Start(shell.CommandLine, cols, rows, dir); }
                        catch (Win32Exception e) { notice("셸을 시작할 수 없습니다: " + shell.Name + " (" + e.Message + ")"); pipe.WaitForPipeDrain(); return; }
                        Log("셸 시작 {0} pid={1} {2}x{3} {4}", shell.Name, pty.Process.Id, cols, rows, dir);
                        sync.Started(pty, shell.Cd);
                        var started = pty;
                        new Thread(() => Pump(started, pipe, sync)) { IsBackground = true }.Start();
                        new Thread(() =>
                        {
                            started.Process.WaitForExit();
                            Log("셸 끝남 code={0}", started.Process.ExitCode);
                            Environment.Exit(0);
                        }) { IsBackground = true }.Start();
                    }
                }
                Log("파이프 끊김");
            }
        }

        // ConPTY 출력(UTF-8)을 글자 경계에서 끊어 o 메시지로 보낸다. 셸이 사용자의 명령으로 폴더를 옮겼으면 p도 보낸다.
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
                    Shared.Write(pipe, "o" + text);
                    if (moved == null) continue;
                    // 없는 경로로 이동하면 탐색기가 오류 창을 띄운다(M2 결과 3.3절).
                    if (!Directory.Exists(moved)) { Log("p 건너뜀(폴더 아님) {0}", moved); continue; }
                    Shared.Write(pipe, "p" + moved);
                    Log("p 보냄 {0}", moved);
                }
            }
            catch (Exception e) { Log("출력 중단: {0}", e.Message); }
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

        public static Pty Start(string commandLine, short cols, short rows, string dir)
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
                Native.CloseHandle(pi.hThread);
                Native.CloseHandle(pi.hProcess);
            }
            finally
            {
                Native.DeleteProcThreadAttributeList(si.lpAttributeList);
                Marshal.FreeHGlobal(si.lpAttributeList);
            }
            // 의사 콘솔이 복제해 가졌으므로 이쪽 끝은 닫는다.
            inRead.Dispose();
            outWrite.Dispose();
            pty._input = new FileStream(inWrite, FileAccess.Write, 1);
            pty.Output = new FileStream(outRead, FileAccess.Read, 1);
            return pty;
        }

        public void Write(string s)
        {
            var b = Encoding.UTF8.GetBytes(s);
            _input.Write(b, 0, b.Length);
            _input.Flush();
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
