using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace Sill.Host
{
    // 단축키 상주 프로세스(sill-host.exe keys)와 오른쪽 클릭 메뉴(sill-host.exe open). M6.
    // 탐색기 창이 전경일 때만 Ctrl+`, Ctrl+Shift+`를 등록하므로 다른 앱의 같은 키는 막지 않는다.
    // 활성 탭에 band 창이 있으면(패널이 닫혀 숨어 있어도) Sill.Key 메시지를 보내고, band가 포커스·열기·새 터미널을 정한다(M9).
    // band 창이 없으면(이 탭에서 처음 여는 패널) COM으로 열기만 한다. 그러면 band가 첫 터미널을 만든다.
    // 탭 떼기(0.1.2 M1): 떼낸 탭의 band가 남긴 기록을 보고, 새 창에 패널을 열어 그 탭의 sill-host들을 넘긴다(TakeOver).
    static class Keys
    {
        const int Toggle = 0, New = 1, Focus = 2, Dock = 3, Terminal = 4;
        const string Clsid = "{AE2C3550-FB10-4D93-B6E1-4FBED3C0AD90}";
        static readonly uint KeyMessage = Native.RegisterWindowMessage("Sill.Key");
        static Native.WinEventProc _hook;                   // GC가 거두지 않게 붙잡아 둔다
        static bool _registered;
        static DateTime _handled;                           // 이미 본 넘김 기록의 수정 시각

        static void Log(string format, params object[] args) { Shared.Log("keys.log", format, args); }

        public static int Run(bool moved)
        {
            if (!moved && LeaveClosingJob()) return 0;      // 뮤텍스 전에: 다시 뜬 쪽이 뮤텍스를 잡는다
            bool created;
            using (new Mutex(true, @"Local\Sill.Keys", out created))
            {
                if (!created) { Log("이미 실행 중"); return 0; }
                Log("시작 {0}", Shared.Version);
                ListenTabs();
                InSta(() =>
                {
                    const uint EVENT_SYSTEM_FOREGROUND = 3, WINEVENT_OUTOFCONTEXT = 0, WM_HOTKEY = 0x312, WM_TIMER = 0x113;
                    // 이벤트의 창이 아니라 실제 전경 창을 본다. 이미 열린 메모장에 새 탭이 열릴 때처럼 전경이 바뀌지 않았는데 이벤트만 오는 일이 있었다(M6 시험).
                    // ponytail: 놓친 이벤트에 대비해 1초마다 다시 본다. 전경 창 클래스 하나를 읽는 일이라 비용은 무시할 만하다.
                    _hook = (h, e, hwnd, o, c, t, ms) => Watch(Native.GetForegroundWindow());
                    Native.SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _hook, 0, 0, WINEVENT_OUTOFCONTEXT);
                    Native.SetTimer(IntPtr.Zero, IntPtr.Zero, 1000, IntPtr.Zero);
                    Watch(Native.GetForegroundWindow());
                    Native.MSG msg;
                    while (Native.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                    {
                        if (msg.message == WM_HOTKEY) Act(Native.GetForegroundWindow(), msg.wParam.ToInt32());
                        else if (msg.message == WM_TIMER) Watch(Native.GetForegroundWindow());
                    }
                });
            }
            return 0;
        }

        // 오른쪽 클릭 메뉴: 메뉴를 연 탐색기 창이 전경이다. 이미 열려 있으면 닫지 않고 포커스만 준다.
        // 바탕 화면처럼 탐색기 창 밖이면 그 폴더로 탐색기를 열고 패널을 켠다(Open in Terminal과 같음, M8).
        public static int Open(string folder)
        {
            Log("메뉴 {0}", folder);
            InSta(() =>
            {
                IntPtr frame = Native.GetForegroundWindow();
                if (ClassOf(frame) == "CabinetWClass") Act(frame, Focus);
                else OpenWindow(folder);
            });
            return 0;
        }

        // Windows 11은 설정에 따라 새 창 대신 기존 창의 새 탭으로 연다. 그 폴더를 보는 항목이 늘면 마지막 것을 고르므로 둘 다 맞는다.
        static void OpenWindow(string folder)
        {
            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
            int before = Showing(shell, folder).Count;
            Process.Start("explorer.exe", "\"" + folder + "\"").Dispose();
            for (int i = 0; i < 100; i++)
            {
                Thread.Sleep(100);
                var tabs = Showing(shell, folder);
                if (tabs.Count <= before) continue;
                object tab = tabs[tabs.Count - 1];
                tab.GetType().InvokeMember("ShowBrowserBar", BindingFlags.InvokeMethod, null, tab, new object[] { Clsid, true, null });
                Log("새 창에 패널 열기 {0}", folder);
                return;
            }
            Log("새 창을 찾지 못함 {0}", folder);
        }

        static List<object> Showing(object shell, string folder)
        {
            var found = new List<object>();
            object windows = Call(shell, "Windows");
            int count = (int)Get(windows, "Count");
            for (int i = 0; i < count; i++)
            {
                try
                {
                    object w = Call(windows, "Item", i);
                    if (SamePath(PathOf(w), folder)) found.Add(w);
                }
                catch (Exception) { }   // 아직 문서가 없는 창(여는 중)
            }
            return found;
        }

        static string PathOf(object tab)
        {
            object doc = null, folder = null, self = null;
            try
            {
                doc = Get(tab, "Document"); folder = Get(doc, "Folder"); self = Get(folder, "Self");
                return (string)Get(self, "Path");
            }
            finally { Release(self); Release(folder); Release(doc); }
        }

        static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }

        static bool SamePath(string a, string b) { return string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }

        // 탐색기는 탭을 떼면 탭을 옮기지 않고 그 폴더로 새 창을 연 뒤 원래 탭을 닫는다. 닫히는 band는 새 창이 생기기 전에 닫히므로(놓은 뒤 0.33 s,
        // 새 창은 0.45 s에 앞에 나옴) 기록만 남긴다. 2 s 안에 다른 탐색기 창이 같은 폴더로 앞에 나오면 그 창의 활성 탭에 패널을 연다.
        // 다른 창의 탭 줄에 놓아도 같다(그 창에 새 탭을 열고 원래 탭을 닫음). 그 창은 지금 탭이 활성인 채 먼저 앞에 나오므로 놓은 탭이 활성이 될 때까지 본다.
        // 떼기·다른 창에 놓기·다시 붙이기는 모두 탐색기가 새 탭을 ShellWindows에 등록하고, 탭·창 닫기는 등록이 없다. 그래서 기록 무렵 등록이 있었을 때만 넘긴다.
        // 탭을 닫고 2 s 안에 같은 폴더의 다른 창으로 가도, 창의 마지막 탭을 옮겨 원래 창이 사라져도 이것으로 가린다(0.1.2).
        // 등록 이벤트를 받지 못하는 동안(연결 전, 끊긴 것을 알아챈 뒤 다시 붙기 전)은 원래 창이 남아 있으면 넘긴다.
        // 작업 표시줄 explorer가 다시 떠 끊긴 뒤 알아채기까지(최대 10 s)는 등록이 오지 않으므로 넘기지 않는다.
        // 열기 직전에 이름을 바꿔 새 band만 기록을 가져가게 한다.
        // ponytail: 탭을 닫고 1 s 안에 같은 폴더로 새 창(Ctrl+N)을 열면 등록이 있어 열린다. 새 탭의 등록 쿠키까지 맞추면 막을 수 있다.
        static void TakeOver(IntPtr frame)
        {
            var f = new FileInfo(Shared.HandoffPath);
            if (!f.Exists || f.LastWriteTimeUtc == _handled || DateTime.UtcNow - f.LastWriteTimeUtc > TimeSpan.FromSeconds(2)) return;
            string[] rec = File.ReadAllLines(f.FullName);
            if (rec.Length < 5) return;
            var from = (IntPtr)long.Parse(rec[0]);
            if (from == frame) return;      // 떼는 동안, 또는 탭을 닫은 뒤의 원래 창. 다음 전경 변화에서 다시 본다
            _handled = f.LastWriteTimeUtc;
            bool gone = !Native.IsWindowVisible(from);
            long since = f.LastWriteTimeUtc.AddSeconds(-1).Ticks;      // 새 탭 등록이 원래 탭을 닫기 조금 앞설 수도 있다
            // 새 창은 앞에 나온 뒤 약 0.2 s 지나 ShellWindows에 보인다. 다른 창의 탭 줄에 놓으면 그 창의 지금 탭이 잠시 활성으로 남는다.
            string last = null;
            bool matched = false;
            for (int i = 0; i < 15; i++)
            {
                object tab = ActiveTab(frame);
                string path = null;
                if (tab != null) try { path = PathOf(tab); } catch (Exception) { }     // 아직 문서가 없는 창
                if (path != null) last = path;
                bool opened = Interlocked.Read(ref _tabOpened) >= since;
                if (path != null && SamePath(path, rec[1]))
                {
                    if (FindBand(frame) != IntPtr.Zero) { Log("넘김 안 함(band 있음)"); return; }
                    matched = true;
                    if (opened || (!_listening && !gone))
                    {
                        File.Delete(Shared.HandoffOpenPath);
                        File.Move(f.FullName, Shared.HandoffOpenPath);
                        tab.GetType().InvokeMember("ShowBrowserBar", BindingFlags.InvokeMethod, null, tab, new object[] { Clsid, true, null });
                        Log("넘김 패널 열기 {0} ({1}개, 원래 창 {2}, 등록={3})", path, rec.Length - 4, gone ? "없음" : "있음", opened ? 1 : _listening ? 0 : -1);
                        return;
                    }
                }
                Thread.Sleep(100);
            }
            if (matched) Log(gone ? "넘김 안 함(창을 닫음)" : "넘김 안 함(새 탭 없음)");
            else if (last != null) Log("넘김 안 함(다른 폴더) {0}", last);
            else Log("넘김 안 함(탭을 찾지 못함)");
        }

        // 탐색기가 탭(창)을 ShellWindows에 등록한 마지막 시각과, 그 이벤트를 받고 있는지. 이벤트는 따로 STA 스레드에서 받는다.
        // 주 스레드의 루프는 메시지를 배달하지 않고(DispatchMessage 없음), TakeOver가 그 스레드를 기다리게 한다.
        // ShellWindows는 작업 표시줄 explorer에 있으므로 그 프로세스가 다시 뜨면 연결이 끊긴다. 10 s마다 살아 있는지 보고, 끊겼으면
        // 바탕 화면 창이 다시 생긴 뒤 다음 확인 때 다시 붙는다(바로 붙으면 explorer가 등록하기 전의 다른 ShellWindows에 붙을 수 있다).
        static long _tabOpened;
        static volatile bool _listening;

        public static void TabOpened() { Interlocked.Exchange(ref _tabOpened, DateTime.UtcNow.Ticks); }

        static void ListenTabs()
        {
            var t = new Thread(() =>
            {
                const uint WM_TIMER = 0x113;
                object windows = null;
                bool waited = true, warned = false;
                Native.SetTimer(IntPtr.Zero, IntPtr.Zero, 10000, IntPtr.Zero);
                while (true)
                {
                    if (windows == null && waited && Native.GetShellWindow() != IntPtr.Zero)
                    {
                        try
                        {
                            windows = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")));    // ShellWindows
                            var iid = typeof(DShellWindowsEvents).GUID;
                            ComTypes.IConnectionPoint cp;
                            ((ComTypes.IConnectionPointContainer)windows).FindConnectionPoint(ref iid, out cp);
                            int cookie;
                            cp.Advise(new ShellWindowsEvents(), out cookie);
                            _listening = true;
                            warned = false;
                            Log("탭 등록 이벤트 받음");
                        }
                        catch (Exception e)
                        {
                            windows = null;
                            if (!warned) Log("탭 등록 이벤트 못 받음: {0}", e.Message);
                            warned = true;
                        }
                    }
                    Native.MSG msg;
                    if (Native.GetMessage(out msg, IntPtr.Zero, 0, 0) <= 0) return;
                    if (msg.message != WM_TIMER) { Native.DispatchMessage(ref msg); continue; }
                    waited = true;
                    if (windows == null) continue;
                    try { Get(windows, "Count"); }
                    catch (Exception)
                    {
                        windows = null;
                        _listening = false;
                        waited = false;
                        Log("탭 등록 이벤트 끊김");
                    }
                }
            }) { IsBackground = true };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        // Sill 터미널 안에서 띄우면(register.ps1, 설치 프로그램, winget) sill-host의 KILL_ON_JOB_CLOSE 작업에 묶여 그 탭을 닫을 때 함께 끝난다.
        // 그런 작업 안이면 WMI(Win32_Process.Create)로 작업 밖에서 다시 띄우고 끝낸다. 다시 뜬 쪽은 "keys moved"라 또 띄우지 않는다(0.1.2).
        static bool LeaveClosingJob()
        {
            const uint KILL_ON_JOB_CLOSE = 0x2000;
            var info = new Sill.Host.Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            int size = Marshal.SizeOf(info);
            IntPtr p = Marshal.AllocHGlobal(size);
            try
            {
                // 작업 핸들이 없으면 이 프로세스의 작업을 묻는다. 작업 밖이면 실패한다.
                if (!Sill.Host.Native.QueryInformationJobObject(IntPtr.Zero, 9, p, (uint)size, IntPtr.Zero)) return false;
                info = (Sill.Host.Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION)Marshal.PtrToStructure(p, typeof(Sill.Host.Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            }
            finally { Marshal.FreeHGlobal(p); }
            if ((info.BasicLimitInformation.LimitFlags & KILL_ON_JOB_CLOSE) == 0) return false;
            try
            {
                string cmd = "\"" + Process.GetCurrentProcess().MainModule.FileName + "\" keys moved";
                using (var wmi = new System.Management.ManagementClass("Win32_Process"))
                {
                    object code = wmi.InvokeMethod("Create", new object[] { cmd, null, null, null });
                    if (Convert.ToUInt32(code) == 0) { Log("작업 개체 밖에서 다시 띄움"); return true; }
                    Log("작업 개체 밖에서 다시 띄우지 못함 code={0}", code);
                }
            }
            catch (Exception e) { Log("작업 개체 밖에서 다시 띄우지 못함: {0}", e.Message); }
            return false;
        }

        // Shell.Application은 STA 개체이고, 단축키 메시지는 등록한 스레드로 온다.
        static void InSta(Action body)
        {
            var t = new Thread(() =>
            {
                try { body(); }
                catch (Exception e) { Log("예외: {0}", e); }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
        }

        static void Watch(IntPtr hwnd)
        {
            const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000, VK_OEM_3 = 0xC0;
            bool explorer = ClassOf(hwnd) == "CabinetWClass";
            if (explorer)
                try { TakeOver(hwnd); }
                catch (Exception e) { Log("넘김 예외: {0}", e.Message); }
            if (explorer == _registered) return;
            _registered = explorer;
            Log("단축키 {0} (전경 {1})", explorer ? "등록" : "해제", ClassOf(hwnd));
            if (!explorer) { foreach (int key in new[] { Toggle, New, Dock, Terminal }) Native.UnregisterHotKey(IntPtr.Zero, key); return; }
            bool toggle = Native.RegisterHotKey(IntPtr.Zero, 0, MOD_CONTROL | MOD_NOREPEAT, VK_OEM_3);
            bool restart = Native.RegisterHotKey(IntPtr.Zero, 1, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_OEM_3);
            bool dock = Native.RegisterHotKey(IntPtr.Zero, Dock, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_OEM_3);
            bool terminal = Native.RegisterHotKey(IntPtr.Zero, Terminal, MOD_CONTROL | MOD_ALT | MOD_SHIFT | MOD_NOREPEAT, VK_OEM_3);
            if (!toggle || !restart || !dock || !terminal) Log("단축키 등록 실패 toggle={0} new={1} dock={2} terminal={3} err={4}", toggle, restart, dock, terminal, Marshal.GetLastWin32Error());
        }

        static void Act(IntPtr frame, int action)
        {
            try
            {
                if (ClassOf(frame) != "CabinetWClass") { Log("탐색기 창 아님 {0}", ClassOf(frame)); return; }
                if (action == Terminal || (action == Toggle && Preferences.WindowsTerminal))
                {
                    object tab = ActiveTab(frame);
                    try { if (tab != null) TerminalLauncher.Open(PathOf(tab)); }
                    finally { Release(tab); }
                    return;
                }
                IntPtr band = FindBand(frame);
                if (band == IntPtr.Zero)
                {
                    object tab = ActiveTab(frame);
                    if (tab == null) { Log("활성 탭 없음"); return; }
                    // 열면 band의 UIActivateIO가 패널에 포커스를 준다(M0·M1). 새 band는 터미널 하나로 시작하므로 새 터미널 키도 여기서 끝난다.
                    tab.GetType().InvokeMember("ShowBrowserBar", BindingFlags.InvokeMethod, null, tab, new object[] { Clsid, true, null });
                    Log("패널 열기 action={0}", action);
                    // ShowBrowserBar constructs the band synchronously. Apply the
                    // dock action on the first use as well as on existing panels.
                    if (action == Dock)
                    {
                        band = FindBand(frame);
                        if (band != IntPtr.Zero) Native.PostMessage(band, KeyMessage, (IntPtr)Dock, IntPtr.Zero);
                    }
                    return;
                }
                Native.PostMessage(band, KeyMessage, (IntPtr)action, IntPtr.Zero);
                Log("Sill.Key {0} → 0x{1:X}", action, band.ToInt64());
            }
            catch (Exception e) { Log("Act 예외: {0}", e); }
        }

        // 활성 탭은 z-순서에서 가장 위의 탭 창이다. Windows 11은 비활성 탭 창도 숨기지 않아 IsWindowVisible로는 가릴 수 없다(M6 시험).
        static IntPtr ActiveTabWindow(IntPtr frame) { return Native.FindWindowEx(frame, IntPtr.Zero, "ShellTabWindowClass", null); }

        // band 창은 활성 탭 창 안의, 제목이 "Sill"인 WinForms 창이다. 패널이 닫혀 있으면 숨은 채 남는다(M9).
        static IntPtr FindBand(IntPtr frame)
        {
            IntPtr found = IntPtr.Zero;
            Native.EnumChildWindows(ActiveTabWindow(frame), (h, l) =>
            {
                var text = new StringBuilder(8);
                Native.GetWindowText(h, text, text.Capacity);
                if (text.ToString() == "Sill" && ClassOf(h).StartsWith("WindowsForms10.")) { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        // 탭마다 IWebBrowser2가 있고 HWND는 모두 같은 창이다. 셸 브라우저 창이 활성 탭 창인 것을 고른다(M6).
        static object ActiveTab(IntPtr frame)
        {
            IntPtr active = ActiveTabWindow(frame);
            var sidTopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
            var iidShellBrowser = new Guid("000214E2-0000-0000-C000-000000000046");
            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
            object windows = Call(shell, "Windows");
            int count = (int)Get(windows, "Count");
            object only = null;
            int tabs = 0;
            for (int i = 0; i < count; i++)
            {
                object w = Call(windows, "Item", i);
                if (w == null || Convert.ToInt64(Get(w, "HWND")) != frame.ToInt64()) continue;
                tabs++;
                only = w;
                object sb;
                IntPtr tabWindow;
                if (((Native.IServiceProvider)w).QueryService(ref sidTopLevelBrowser, ref iidShellBrowser, out sb) == 0
                    && ((Native.IOleWindow)sb).GetWindow(out tabWindow) == 0 && tabWindow == active)
                    return w;
            }
            return tabs == 1 ? only : null;
        }

        static object Call(object o, string name, params object[] args) { return o.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, o, args); }
        static object Get(object o, string name) { return o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, null); }

        static string ClassOf(IntPtr hwnd)
        {
            var s = new StringBuilder(64);
            Native.GetClassName(hwnd, s, s.Capacity);
            return s.ToString();
        }

        static class Native
        {
            public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
            public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

            [StructLayout(LayoutKind.Sequential)]
            public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

            [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
            public interface IServiceProvider
            {
                [PreserveSig] int QueryService(ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
            }

            // IShellBrowser는 IOleWindow를 이으므로 GetWindow는 같은 자리다.
            [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214E2-0000-0000-C000-000000000046")]
            public interface IOleWindow
            {
                [PreserveSig] int GetWindow(out IntPtr phwnd);
            }

            [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventProc proc, uint pid, uint tid, uint flags);
            [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mod, uint vk);
            [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
            [DllImport("user32.dll")] public static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
            [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref MSG msg);
            [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
            [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
            [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
            [DllImport("user32.dll")] public static extern UIntPtr SetTimer(IntPtr hwnd, IntPtr id, uint ms, IntPtr proc);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
            [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
            [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder s, int n);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder s, int n);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        }
    }

    // ShellWindows의 이벤트(exdisp.h). 쓰는 것만 선언한다. 나머지 DISPID는 CLR이 DISP_E_MEMBERNOTFOUND로 돌려준다(0.1.2 M1).
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIDispatch), Guid("FE4106E0-399A-11D0-A48C-00A0C90A8F39")]
    public interface DShellWindowsEvents
    {
        [DispId(200)] void WindowRegistered(int cookie);
    }

    // 탐색기가 IDispatch::Invoke로 부르는 수신 객체. 등록하지 않고 연결점에 직접 넘긴다.
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class ShellWindowsEvents : DShellWindowsEvents
    {
        public void WindowRegistered(int cookie) { Keys.TabOpened(); }
    }
}
