using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using ComTypes = System.Runtime.InteropServices.ComTypes;

[assembly: ComVisible(false)]

namespace Sill.Band
{
    // 가로형 Explorer Bar(CATID_CommBand). M1: WebView2 + xterm.js 화면을 띄우고, 셸은 sill-host 프로세스에 맡긴다.
    // 탐색기 안에서 예외가 새면 탐색기가 죽으므로, 모든 COM 메서드는 Guard로, 이벤트와 비동기 코드는 try/catch로 감싼다.
    // UserControl을 직접 COM에 노출하지 않는 이유: Control이 내부적으로 같은 IID(IOleWindow 등)를 구현해 충돌한다.
    [ComVisible(true), Guid(Clsid), ClassInterface(ClassInterfaceType.None)]
    public class SillBand : IDeskBand, IObjectWithSite, IPersistStream, IInputObject
    {
        public const string Clsid = "AE2C3550-FB10-4D93-B6E1-4FBED3C0AD90";

        static readonly string Dir = Path.GetDirectoryName(new Uri(typeof(SillBand).Assembly.CodeBase).LocalPath);

        // WebView2 환경은 만든 스레드에서만 쓸 수 있고, 탐색기는 창마다 스레드가 다르다.
        [ThreadStatic] static Task<CoreWebView2Environment> _env;

        object _site;
        Control _view;
        CoreWebView2Controller _web;
        Session _session;
        object _browser;                    // 이 탭의 IWebBrowser2
        ComTypes.IConnectionPoint _events;
        int _cookie;

        static void Log(string format, params object[] args) { Shared.Log("band.log", format, args); }

        static int Guard(string name, Func<int> body)
        {
            try { return body(); }
            catch (Exception e) { Log("{0} 예외: {1}", name, e); return Native.E_FAIL; }
        }

        static void Safe(string name, Action body)
        {
            try { body(); }
            catch (Exception e) { Log("{0} 예외: {1}", name, e); }
        }

        public SillBand() { Log("생성"); }

        // --- IObjectWithSite ---

        public int SetSite(object pUnkSite)
        {
            return Guard("SetSite", () =>
            {
                Log("SetSite({0})", pUnkSite == null ? "null" : "site");
                DestroyView();
                _site = pUnkSite;
                if (pUnkSite == null) return Native.S_OK;

                IntPtr parent;
                int hr = ((IOleWindow)pUnkSite).GetWindow(out parent);
                if (hr != Native.S_OK) { Log("site.GetWindow 실패 hr=0x{0:X8}", hr); return hr; }

                var cls = new StringBuilder(256);
                Native.GetClassName(parent, cls, cls.Capacity);
                Log("부모 창 0x{0:X} class={1}", parent.ToInt64(), cls);

                // 문서 요구: 보이지 않는 자식 창으로 만든다. 표시는 ShowDW에서 한다.
                // 이 창은 자리만 차지하고, 화면은 그 위에 붙는 WebView2(msedgewebview2.exe)가 그린다.
                var view = new Control { BackColor = Color.FromArgb(30, 30, 30), Visible = false };
                view.GotFocus += (s, e) => Safe("GotFocus", () => { if (_web != null) _web.MoveFocus(CoreWebView2MoveFocusReason.Programmatic); });
                view.Resize += (s, e) => Safe("Resize", () => { if (_web != null) _web.Bounds = view.ClientRectangle; });
                IntPtr old = Native.SetParent(view.Handle, parent);
                Log("SetParent 결과 old=0x{0:X} err={1}", old.ToInt64(), Marshal.GetLastWin32Error());
                _view = view;

                // 세션은 패널 여닫기가 아니라 band 수명(SetSite → CloseDW)에 묶는다(M0 결과 표 2).
                _session = new Session(view, ToPage, Path.Combine(Dir, "sill-host.exe"));
                Safe("탐색기 이벤트 연결", () => WatchBrowser(pUnkSite));
                Safe("시작 폴더", () => SendFolder("시작"));
                InitWebView(view);
                return Native.S_OK;
            });
        }

        async void InitWebView(Control view)
        {
            try
            {
                if (_env == null)
                {
                    // 탐색기 안에서는 WebView2Loader.dll을 C:\Windows에서 찾으므로 band 폴더를 알려 준다. 프로세스에서 한 번만 허용된다.
                    try { CoreWebView2Environment.SetLoaderDllFolderPath(Dir); }
                    catch (InvalidOperationException) { }
                    string udf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Sill\WebView2");
                    _env = CoreWebView2Environment.CreateAsync(null, udf);
                }
                var env = await _env;
                var web = await env.CreateCoreWebView2ControllerAsync(view.Handle);
                if (view != _view) { web.Close(); return; }   // 기다리는 사이 band가 닫혔다

                _web = web;
                web.DefaultBackgroundColor = view.BackColor;
                web.Bounds = view.ClientRectangle;
                web.IsVisible = view.Visible;
                web.GotFocus += (s, e) => Safe("web.GotFocus", () => NotifyFocus(true));
                web.LostFocus += (s, e) => Safe("web.LostFocus", () => NotifyFocus(false));
                web.AcceleratorKeyPressed += (s, e) =>
                {
                    if (e.KeyEventKind == CoreWebView2KeyEventKind.KeyDown || e.KeyEventKind == CoreWebView2KeyEventKind.SystemKeyDown)
                        Log("AcceleratorKeyPressed vk=0x{0:X2} {1}", e.VirtualKey, e.KeyEventKind);
                };

                var core = web.CoreWebView2;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.SetVirtualHostNameToFolderMapping("sill.local", Path.Combine(Dir, "web"), CoreWebView2HostResourceAccessKind.Deny);
                core.WebMessageReceived += OnWebMessage;
                core.ProcessFailed += (s, e) => Log("WebView2 ProcessFailed {0}", e.ProcessFailedKind);
                core.Navigate("https://sill.local/index.html");
                Log("WebView2 준비 (스레드 {0})", System.Threading.Thread.CurrentThread.ManagedThreadId);
            }
            catch (Exception e) { Log("WebView2 예외: {0}", e); }
        }

        void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            Safe("OnWebMessage", () =>
            {
                string m = e.TryGetWebMessageAsString();
                if (m == "n")
                {
                    // 세션이 끝난 뒤 페이지에서 키를 누르면 새 sill-host로 다시 시작한다.
                    _session.Dispose();
                    _session = new Session(_view, ToPage, Path.Combine(Dir, "sill-host.exe"));
                    SendFolder("다시 시작");
                }
                else _session.Send(m);
            });
        }

        void ToPage(string m)
        {
            if (_web != null) _web.CoreWebView2.PostWebMessageAsString(m);
        }

        // --- 탐색기 → 터미널 (M2 설계 3.2절): 폴더가 바뀌면 경로만 sill-host에 보낸다. cd를 넣을지는 sill-host가 정한다. ---

        void WatchBrowser(object site)
        {
            var sidWebBrowserApp = new Guid("0002DF05-0000-0000-C000-000000000046");
            var iidWebBrowser2 = new Guid("D30C1661-CDAF-11D0-8A3E-00C04FC9E26E");
            int hr = ((IServiceProvider)site).QueryService(ref sidWebBrowserApp, ref iidWebBrowser2, out _browser);
            if (hr != Native.S_OK) { Log("IWebBrowser2 없음 hr=0x{0:X8}", hr); return; }
            var iid = typeof(DWebBrowserEvents2).GUID;
            ((ComTypes.IConnectionPointContainer)_browser).FindConnectionPoint(ref iid, out _events);
            _events.Advise(new BrowserEvents(() => Safe("NavigateComplete2", () => SendFolder("이동"))), out _cookie);
        }

        void SendFolder(string why)
        {
            if (_browser == null || _session == null) return;
            // 표 1: Document.Folder.Self.Path는 파일 시스템 경로를 그대로, 가상 폴더는 ::{GUID}로 준다.
            object self = Get(Get(Get(_browser, "Document"), "Folder"), "Self");
            string path = Get(self, "Path") as string;
            Log("{0} 폴더 {1}", why, path);
            if (string.IsNullOrEmpty(path) || path.StartsWith("::")) return;
            _session.Send("c" + path);
        }

        static object Get(object o, string name)
        {
            return o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, null);
        }

        public int GetSite(ref Guid riid, out IntPtr ppvSite)
        {
            ppvSite = IntPtr.Zero;
            if (_site == null) return Native.E_FAIL;
            IntPtr unk = Marshal.GetIUnknownForObject(_site);
            try { return Marshal.QueryInterface(unk, ref riid, out ppvSite); }
            finally { Marshal.Release(unk); }
        }

        // --- IOleWindow / IDockingWindow / IDeskBand ---

        public int GetWindow(out IntPtr phwnd)
        {
            phwnd = _view == null ? IntPtr.Zero : _view.Handle;
            Log("GetWindow -> 0x{0:X}", phwnd.ToInt64());
            return _view == null ? Native.E_FAIL : Native.S_OK;
        }

        public int ContextSensitiveHelp(bool fEnterMode) { return Native.E_NOTIMPL; }

        public int ShowDW(bool fShow)
        {
            return Guard("ShowDW", () =>
            {
                Log("ShowDW({0})", fShow);
                if (_view != null) _view.Visible = fShow;
                if (_web != null) _web.IsVisible = fShow;
                return Native.S_OK;
            });
        }

        public int CloseDW(uint dwReserved)
        {
            return Guard("CloseDW", () =>
            {
                Log("CloseDW");
                DestroyView();
                return Native.S_OK;
            });
        }

        public int ResizeBorderDW(IntPtr prcBorder, object punkToolbarSite, bool fReserved) { return Native.E_NOTIMPL; }

        public int GetBandInfo(uint dwBandID, uint dwViewMode, ref DESKBANDINFO pdbi)
        {
            const uint MINSIZE = 0x1, MAXSIZE = 0x2, INTEGRAL = 0x4, ACTUAL = 0x8, TITLE = 0x10, MODEFLAGS = 0x20;
            const uint DBIMF_VARIABLEHEIGHT = 0x8;
            Log("GetBandInfo(id={0}, mode={1}, mask=0x{2:X})", dwBandID, dwViewMode, pdbi.dwMask);
            if ((pdbi.dwMask & MINSIZE) != 0) { pdbi.ptMinSize.x = 0; pdbi.ptMinSize.y = 120; }
            if ((pdbi.dwMask & MAXSIZE) != 0) { pdbi.ptMaxSize.x = -1; pdbi.ptMaxSize.y = -1; }
            if ((pdbi.dwMask & INTEGRAL) != 0) { pdbi.ptIntegral.x = 1; pdbi.ptIntegral.y = 1; }
            if ((pdbi.dwMask & ACTUAL) != 0) { pdbi.ptActual.x = 0; pdbi.ptActual.y = 240; }
            if ((pdbi.dwMask & TITLE) != 0) pdbi.wszTitle = "Sill";
            if ((pdbi.dwMask & MODEFLAGS) != 0) pdbi.dwModeFlags = DBIMF_VARIABLEHEIGHT;
            return Native.S_OK;
        }

        // --- IPersistStream: 저장할 상태가 없어도 성공을 돌려줘야 한다 ---

        public int GetClassID(out Guid pClassID) { pClassID = new Guid(Clsid); return Native.S_OK; }
        public int IsDirty() { return Native.S_FALSE; }
        public int Load(object pStm) { return Native.S_OK; }
        public int Save(object pStm, bool fClearDirty) { return Native.S_OK; }
        public int GetSizeMax(out ulong pcbSize) { pcbSize = 0; return Native.S_OK; }

        // --- IInputObject ---

        public int UIActivateIO(bool fActivate, ref MSG pMsg)
        {
            Log("UIActivateIO({0})", fActivate);
            if (fActivate && _view != null) _view.Focus();
            return Native.S_OK;
        }

        public int HasFocusIO()
        {
            if (_view == null) return Native.S_FALSE;
            IntPtr f = Native.GetFocus();
            return f == _view.Handle || Native.IsChild(_view.Handle, f) ? Native.S_OK : Native.S_FALSE;
        }

        // 키는 WebView2 프로세스의 창이 받으므로 여기로 오지 않을 것으로 본다(M1 설계 3.4절). 오는지 확인하려고 로그만 남긴다.
        public int TranslateAcceleratorIO(ref MSG pMsg)
        {
            const uint WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
            if (pMsg.message == WM_KEYDOWN || pMsg.message == WM_SYSKEYDOWN)
                Log("TranslateAcceleratorIO msg=0x{0:X} vk=0x{1:X2}", pMsg.message, pMsg.wParam.ToInt64());
            return Native.S_FALSE;
        }

        void NotifyFocus(bool focused)
        {
            var s = _site as IInputObjectSite;
            if (s != null) s.OnFocusChangeIS(this, focused);
        }

        void DestroyView()
        {
            if (_events != null) { Safe("Unadvise", () => _events.Unadvise(_cookie)); _events = null; }
            _browser = null;
            if (_session != null) { _session.Dispose(); _session = null; }
            if (_web != null) { _web.Close(); _web = null; }
            if (_view == null) return;
            _view.Dispose();
            _view = null;
        }
    }

    // 탐색기가 IDispatch::Invoke로 부르는 이벤트 수신 객체. 등록하지 않고 연결점에 직접 넘기므로 CLSID가 없다.
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class BrowserEvents : DWebBrowserEvents2
    {
        readonly Action _navigated;
        public BrowserEvents(Action navigated) { _navigated = navigated; }
        public void NavigateComplete2(object pDisp, ref object URL) { _navigated(); }
    }
}
