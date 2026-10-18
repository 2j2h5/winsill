using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Sill.Host
{
    // 단축키 상주 프로세스(sill-host.exe keys)와 오른쪽 클릭 메뉴(sill-host.exe open). M6 설계 3.1·3.7절.
    // 탐색기 창이 전경일 때만 Ctrl+`, Ctrl+Shift+`를 등록하므로 다른 앱의 같은 키는 막지 않는다.
    // 활성 탭에 패널이 보이면 band 창에 Sill.Key 메시지를 보내고(포커스는 band가 안다), 안 보이면 COM으로 연다.
    static class Keys
    {
        const int Toggle = 0, Restart = 1, Focus = 2;      // Sill.Key의 wParam
        const string Clsid = "{AE2C3550-FB10-4D93-B6E1-4FBED3C0AD90}";
        static readonly uint KeyMessage = Native.RegisterWindowMessage("Sill.Key");
        static Native.WinEventProc _hook;                   // GC가 거두지 않게 붙잡아 둔다
        static bool _registered;

        static void Log(string format, params object[] args) { Shared.Log("keys.log", format, args); }

        public static int Run()
        {
            bool created;
            using (new Mutex(true, @"Local\Sill.Keys", out created))
            {
                if (!created) { Log("이미 실행 중"); return 0; }
                Log("시작");
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
                        if (msg.message == WM_HOTKEY) Act(Native.GetForegroundWindow(), msg.wParam.ToInt32() == 1 ? Restart : Toggle);
                        else if (msg.message == WM_TIMER) Watch(Native.GetForegroundWindow());
                    }
                });
            }
            return 0;
        }

        // 오른쪽 클릭 메뉴: 메뉴를 연 탐색기 창이 전경이다. 이미 열려 있으면 닫지 않고 포커스만 준다.
        // 바탕 화면처럼 탐색기 창 밖이면 그 폴더로 탐색기를 열고 패널을 켠다(Open in Terminal과 같음, M8 설계 3.6절).
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
                    string path = (string)Get(Get(Get(Get(w, "Document"), "Folder"), "Self"), "Path");
                    if (string.Equals(path.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) found.Add(w);
                }
                catch (Exception) { }   // 아직 문서가 없는 창(여는 중)
            }
            return found;
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
            const uint MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000, VK_OEM_3 = 0xC0;
            bool explorer = ClassOf(hwnd) == "CabinetWClass";
            if (explorer == _registered) return;
            _registered = explorer;
            Log("단축키 {0} (전경 {1})", explorer ? "등록" : "해제", ClassOf(hwnd));
            if (!explorer) { Native.UnregisterHotKey(IntPtr.Zero, 0); Native.UnregisterHotKey(IntPtr.Zero, 1); return; }
            bool toggle = Native.RegisterHotKey(IntPtr.Zero, 0, MOD_CONTROL | MOD_NOREPEAT, VK_OEM_3);
            bool restart = Native.RegisterHotKey(IntPtr.Zero, 1, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_OEM_3);
            if (!toggle || !restart) Log("단축키 등록 실패 toggle={0} restart={1} err={2}", toggle, restart, Marshal.GetLastWin32Error());
        }

        static void Act(IntPtr frame, int action)
        {
            try
            {
                if (ClassOf(frame) != "CabinetWClass") { Log("탐색기 창 아님 {0}", ClassOf(frame)); return; }
                IntPtr band = FindBand(frame);
                if (band == IntPtr.Zero)
                {
                    object tab = ActiveTab(frame);
                    if (tab == null) { Log("활성 탭 없음"); return; }
                    // 열면 band의 UIActivateIO가 패널에 포커스를 준다(M0·M1). 다시 시작이면 연 패널에 이어서 알린다.
                    tab.GetType().InvokeMember("ShowBrowserBar", BindingFlags.InvokeMethod, null, tab, new object[] { Clsid, true, null });
                    Log("패널 열기 action={0}", action);
                    if (action != Restart) return;
                    band = FindBand(frame);
                    if (band == IntPtr.Zero) { Log("연 패널을 찾지 못함"); return; }
                }
                Native.PostMessage(band, KeyMessage, (IntPtr)action, IntPtr.Zero);
                Log("Sill.Key {0} → 0x{1:X}", action, band.ToInt64());
            }
            catch (Exception e) { Log("Act 예외: {0}", e); }
        }

        // 활성 탭은 z-순서에서 가장 위의 탭 창이다. Windows 11은 비활성 탭 창도 숨기지 않아 IsWindowVisible로는 가릴 수 없다(M6 시험).
        static IntPtr ActiveTabWindow(IntPtr frame) { return Native.FindWindowEx(frame, IntPtr.Zero, "ShellTabWindowClass", null); }

        // band 창은 활성 탭 창 안의, 제목이 "Sill"인 WinForms 창이다. 패널이 닫혀 있으면 숨겨져 있다.
        static IntPtr FindBand(IntPtr frame)
        {
            IntPtr found = IntPtr.Zero;
            Native.EnumChildWindows(ActiveTabWindow(frame), (h, l) =>
            {
                var text = new StringBuilder(8);
                Native.GetWindowText(h, text, text.Capacity);
                if (text.ToString() == "Sill" && ClassOf(h).StartsWith("WindowsForms10.") && Native.IsWindowVisible(h)) { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        // 탭마다 IWebBrowser2가 있고 HWND는 모두 같은 창이다. 셸 브라우저 창이 활성 탭 창인 것을 고른다(설계 표 11의 첫 위험).
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
            [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
            [DllImport("user32.dll")] public static extern UIntPtr SetTimer(IntPtr hwnd, IntPtr id, uint ms, IntPtr proc);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
            [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
            [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder s, int n);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder s, int n);
            [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        }
    }
}
