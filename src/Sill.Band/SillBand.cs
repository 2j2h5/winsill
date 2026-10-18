using System;
using System.Diagnostics;
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
        View _view;
        CoreWebView2Controller _web;
        Task<CoreWebView2Environment> _envUsed;    // _web을 만든 환경. 브라우저 프로세스가 죽으면 이 환경을 버린다
        DateTime _recovered;                        // 마지막으로 WebView2를 다시 만든 시각
        Session _session;
        object _browser;                    // 이 탭의 IWebBrowser2
        ComTypes.IConnectionPoint _events;
        int _cookie;
        IntPtr _bar;                        // 서브클래싱한 탐색기의 BaseBar 창
        Label _notice;                      // WebView2를 만들지 못했을 때의 안내

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
                Theme.Read();
                var view = new View { BackColor = Theme.Content, Visible = false, Text = "Sill" };
                view.Key += action => Safe("Sill.Key", () => OnKey(action));
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
                var envTask = _env;
                var env = await envTask;
                var web = await env.CreateCoreWebView2ControllerAsync(view.Handle);
                if (view != _view) { web.Close(); return; }   // 기다리는 사이 band가 닫혔다

                _web = web;
                _envUsed = envTask;
                web.DefaultBackgroundColor = view.BackColor;
                web.Bounds = view.ClientRectangle;
                web.IsVisible = view.Visible;
                web.GotFocus += (s, e) => Safe("web.GotFocus", () => NotifyFocus(true));
                web.LostFocus += (s, e) => Safe("web.LostFocus", () => NotifyFocus(false));

                var core = web.CoreWebView2;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                // .local은 멀티캐스트 DNS 이름이라 첫 적재가 2초 늦었다. .example은 예약된 이름이다(M6 설계 2.2절).
                core.SetVirtualHostNameToFolderMapping("sill.example", Path.Combine(Dir, "web"), CoreWebView2HostResourceAccessKind.Deny);
                core.WebMessageReceived += OnWebMessage;
                core.ProcessFailed += (s, e) => Safe("ProcessFailed", () => Recover(e.ProcessFailedKind));
                core.Navigate("https://sill.example/index.html");
                // 패널을 열 때 UIActivateIO가 컨트롤러보다 먼저 와서 포커스가 band 창에 남는다. 그대로 두면 키가 사라진다(M6 시험).
                if (view.Focused) web.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
                Log("WebView2 준비 (스레드 {0})", System.Threading.Thread.CurrentThread.ManagedThreadId);
            }
            catch (Exception e)
            {
                Log("WebView2 예외: {0}", e);
                // 실패한 환경을 남기면 이 창의 band는 모두 실패한다. 버리고, 안내를 보인 뒤 패널을 다시 열 때(ShowDW) 다시 만든다(M8 설계 3.4절).
                if (_env != null && _env.IsFaulted) _env = null;
                if (view == _view && _web == null) ShowNotice(e.Message);
            }
        }

        // WebView2 대신 band 창에 보이는 안내 한 줄. 런타임이 없는 Windows 10이 이 경우다. band 창의 제목("Sill")은 상주 프로세스가 찾는 데 쓰므로 그대로 둔다.
        void ShowNotice(string error)
        {
            if (_notice == null)
            {
                _notice = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
                _view.Controls.Add(_notice);
            }
            _notice.ForeColor = Theme.Light ? Color.FromArgb(0x1A, 0x1A, 0x1A) : Color.FromArgb(0xCC, 0xCC, 0xCC);
            _notice.Text = "Sill could not start WebView2 (" + error + ").\nInstall the Microsoft Edge WebView2 Runtime, then reopen the panel.";
            Log("WebView2 안내");
        }

        // M6 설계 3.3절: 브라우저나 렌더러 프로세스가 죽으면 컨트롤러를 다시 만든다. sill-host와 셸은 그대로이고,
        // 새 페이지가 보내는 같은 크기의 r로 sill-host가 화면을 다시 그린다. 런타임이 깨졌을 때 되풀이하지 않도록 10초 안의 재실패는 두고 본다.
        void Recover(CoreWebView2ProcessFailedKind kind)
        {
            Log("WebView2 ProcessFailed {0}", kind);
            if (kind != CoreWebView2ProcessFailedKind.BrowserProcessExited && kind != CoreWebView2ProcessFailedKind.RenderProcessExited
                && kind != CoreWebView2ProcessFailedKind.FrameRenderProcessExited) return;
            if (DateTime.Now - _recovered < TimeSpan.FromSeconds(10)) { Log("WebView2 다시 만들지 않음(10초 안에 또 실패)"); return; }
            _recovered = DateTime.Now;
            if (kind == CoreWebView2ProcessFailedKind.BrowserProcessExited && _env == _envUsed) _env = null;
            var web = _web;
            _web = null;
            Safe("web.Close", () => web.Close());
            Log("WebView2 다시 만들기");
            InitWebView(_view);
        }

        void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            Safe("OnWebMessage", () =>
            {
                string m = e.TryGetWebMessageAsString();
                // 세션이 끝난 뒤 페이지에서 키를 누르면 새 sill-host로 다시 시작한다.
                if (m == "n") Restart("다시 시작");
                else if (m == "h") { Log("닫기 버튼"); ClosePanel(); }
                else if (m == "t") ApplyTheme();
                else _session.Send(m);
            });
        }

        void Restart(string why)
        {
            _session.Dispose();
            _session = new Session(_view, ToPage, Path.Combine(Dir, "sill-host.exe"));
            SendFolder(why);
        }

        void ToPage(string m)
        {
            if (m[0] == 'p') Safe("셸 폴더", () => Navigate(m.Substring(1)));
            // Ctrl+,: sill-host의 자식은 작업 개체와 함께 끝나므로 메모장은 band가 띄운다(M6 설계 3.6절).
            else if (m[0] == 's') Safe("설정 파일", () => Process.Start("notepad.exe", "\"" + m.Substring(1) + "\""));
            else if (_web != null) _web.CoreWebView2.PostWebMessageAsString(m);
        }

        // --- 단축키와 오른쪽 클릭 메뉴(M6 설계 표 4): 상주 프로세스가 활성 탭의 band 창에 보낸다 ---

        void OnKey(int action)
        {
            const int KeyToggle = 0, KeyRestart = 1;
            bool focused = PanelFocused();
            Log("Sill.Key {0} 포커스={1}", action, focused);
            if (action == KeyRestart)
            {
                Restart("다시 시작 키");
                if (_web != null) _web.CoreWebView2.PostWebMessageAsString("n");   // 페이지가 화면을 지우고 크기를 보낸다
            }
            else if (action == KeyToggle && focused)
            {
                ClosePanel();
                return;
            }
            _view.Focus();
        }

        // Ctrl+`와 탭 줄의 ×(M7 설계 3.3절). 포커스를 파일 목록에 두고 닫는다. 세션은 band 수명이라 그대로다.
        void ClosePanel()
        {
            FocusFileList();
            // 창 메시지 처리 중에 바를 닫지 않고 메시지 루프로 미룬다.
            _view.BeginInvoke(new Action(() => Safe("패널 닫기", () =>
                _browser.GetType().InvokeMember("ShowBrowserBar", BindingFlags.InvokeMethod, null, _browser, new object[] { "{" + Clsid + "}", false, null }))));
        }

        // --- 탐색기가 그리는 모양(M7 설계 3.1·3.2·3.5절) ---

        // 탐색기의 왼쪽 띠 머리(제목, ×)를 없애고, 위쪽 분할선을 칠하도록 BaseBar를 서브클래싱한다. 보일 때마다 부른다.
        void Restyle()
        {
            const uint BSIM_STYLE = 0x2, BSIS_NOCAPTION = 0x40;
            var site = _site as IBandSite;
            var info = new BANDSITEINFO { dwMask = BSIM_STYLE };
            if (site != null && site.GetBandSiteInfo(ref info) == Native.S_OK && (info.dwStyle & BSIS_NOCAPTION) == 0)
            {
                info.dwStyle |= BSIS_NOCAPTION;
                Log("사이트 스타일 0x{0:X} hr=0x{1:X8}", info.dwStyle, site.SetBandSiteInfo(ref info));
            }
            _view.CaptionOff = true;
            IntPtr bar = Native.GetParent(Native.GetParent(_view.Handle));
            if (bar == _bar) return;
            if (_bar != IntPtr.Zero) Native.RemoveWindowSubclass(_bar, BarProc, UIntPtr.Zero);
            _bar = Native.SetWindowSubclass(bar, BarProc, UIntPtr.Zero, UIntPtr.Zero) ? bar : IntPtr.Zero;
            Log("BaseBar 0x{0:X} 서브클래스 {1}", bar.ToInt64(), _bar != IntPtr.Zero);
            if (_bar != IntPtr.Zero) Native.InvalidateRect(_bar, IntPtr.Zero, true);
        }

        void ApplyTheme()
        {
            Theme.Read();
            Log("앱 모드 {0}", Theme.Light ? "라이트" : "다크");
            _view.BackColor = Theme.Content;
            if (_web != null) _web.DefaultBackgroundColor = Theme.Content;
            if (_bar != IntPtr.Zero) Native.InvalidateRect(_bar, IntPtr.Zero, true);
        }

        // 분할선은 BaseBar 창 클래스의 바탕 브러시(COLOR_3DFACE, 다크 모드에서도 #F0F0F0)가 칠한다(M7 설계 2.2절).
        // 바탕을 지울 때 rebar가 덮지 않은 위쪽을 탐색기의 세로 분할선처럼 칠한다. 마우스 처리(끌어서 높이 바꾸기)는 그대로 둔다.
        static readonly Native.SubclassProc BarProc = OnBaseBar;    // 대리자가 수집되지 않게 붙들어 둔다

        static IntPtr OnBaseBar(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
        {
            const uint WM_ERASEBKGND = 0x14, WM_NCDESTROY = 0x82;
            try
            {
                if (msg == WM_ERASEBKGND)
                {
                    RECT bar, rebar;
                    Native.GetWindowRect(hwnd, out bar);
                    Native.GetWindowRect(Native.FindWindowEx(hwnd, IntPtr.Zero, "ReBarWindow32", null), out rebar);
                    int width = bar.right - bar.left, top = rebar.top - bar.top;
                    using (var g = Graphics.FromHdc(wParam))
                    using (var frame = new SolidBrush(Theme.Frame))
                    using (var line = new SolidBrush(Theme.Line))
                    {
                        g.FillRectangle(frame, 0, 0, width, top);
                        g.FillRectangle(line, 0, 1, width, 1);
                    }
                    return (IntPtr)1;
                }
                if (msg == WM_NCDESTROY) Native.RemoveWindowSubclass(hwnd, BarProc, id);
            }
            catch (Exception e)
            {
                Log("BaseBar 예외, 서브클래스 뗌: {0}", e);
                Native.RemoveWindowSubclass(hwnd, BarProc, id);
            }
            return Native.DefSubclassProc(hwnd, msg, wParam, lParam);
        }

        // WebView2의 GotFocus는 페이지 안에서 준 포커스에는 오지 않는다. 포커스 창은 msedgewebview2 스레드에 있으므로
        // 패널과 그 자손 창의 스레드마다 포커스 창을 본다(M1 시험과 같은 방법).
        bool PanelFocused()
        {
            IntPtr panel = _view.Handle;
            var threads = new System.Collections.Generic.HashSet<uint> { Native.GetWindowThreadProcessId(panel, IntPtr.Zero) };
            Native.EnumChildWindows(panel, (h, l) => { threads.Add(Native.GetWindowThreadProcessId(h, IntPtr.Zero)); return true; }, IntPtr.Zero);
            foreach (uint tid in threads)
            {
                var info = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
                // band 창 자체의 포커스는 키를 WebView2로 넘기지 못하므로 패널 포커스로 치지 않는다.
                if (Native.GetGUIThreadInfo(tid, ref info) && Native.IsChild(panel, info.hwndFocus)) return true;
            }
            return false;
        }

        void FocusFileList()
        {
            object view;
            IntPtr hwnd;
            var sb = ShellBrowser();
            if (sb == null || sb.QueryActiveShellView(out view) != Native.S_OK || ((IShellView)view).GetWindow(out hwnd) != Native.S_OK) { Log("파일 목록 없음"); return; }
            Native.SetFocus(hwnd);
        }

        IShellBrowser ShellBrowser()
        {
            var sidTopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
            var iid = typeof(IShellBrowser).GUID;
            object sb;
            int hr = ((IServiceProvider)_site).QueryService(ref sidTopLevelBrowser, ref iid, out sb);
            if (hr != Native.S_OK) { Log("IShellBrowser 없음 hr=0x{0:X8}", hr); return null; }
            return (IShellBrowser)sb;
        }

        // --- 터미널 → 탐색기 (M3 설계 3.4절): sill-host가 정한 폴더로 이동만 한다. 돌아오는 c는 sill-host가 버린다. ---

        // Navigate2는 새 보기에 포커스를 줘서 사용자가 패널에 이어 치는 키가 파일 목록으로 간다(M3 결과).
        // BrowseObject는 SBSP_ACTIVATE_NOFOCUS로 포커스를 패널에 그대로 둔다(M3 설계 표 4의 대안).
        void Navigate(string path)
        {
            const uint SBSP_SAMEBROWSER = 0x1, SBSP_ABSOLUTE = 0x0, SBSP_ACTIVATE_NOFOCUS = 0x80000;
            Log("셸 폴더 {0}", path);
            var sb = ShellBrowser();
            if (sb == null) return;
            IntPtr pidl = Native.ILCreateFromPathW(path);
            if (pidl == IntPtr.Zero) { Log("PIDL 실패 {0}", path); return; }
            int hr;
            try { hr = sb.BrowseObject(pidl, SBSP_SAMEBROWSER | SBSP_ABSOLUTE | SBSP_ACTIVATE_NOFOCUS); }
            finally { Native.ILFree(pidl); }
            if (hr != Native.S_OK) Log("BrowseObject hr=0x{0:X8}", hr);
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
                // 탐색기는 패널을 닫았다 열 때마다 사이트 스타일을 되돌리므로 보일 때마다 다시 적용한다. 그 전의 배치에는 띠 머리가 있다.
                if (_view != null && !fShow) _view.CaptionOff = false;
                if (_view != null && fShow) Safe("탐색기 모양", Restyle);
                // WebView2를 만들지 못해 안내를 보이던 패널을 다시 열면 다시 시도한다(M8 설계 3.4절).
                if (_view != null && fShow && _notice != null)
                {
                    _notice.Dispose();
                    _notice = null;
                    Log("WebView2 다시 시도");
                    InitWebView(_view);
                }
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

        // 키는 WebView2 프로세스의 창이 받으므로 여기로 오지 않는다(M1에서 키 24개로 확인). 키 로그는 M8에서 지웠다.
        public int TranslateAcceleratorIO(ref MSG pMsg) { return Native.S_FALSE; }

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
            // 닫는 동안 band 창이 포커스를 받으면(GotFocus) 닫힌 컨트롤러를 부르지 않도록 먼저 비운다.
            if (_web != null) { var web = _web; _web = null; web.Close(); }
            if (_bar != IntPtr.Zero) { Native.RemoveWindowSubclass(_bar, BarProc, UIntPtr.Zero); _bar = IntPtr.Zero; }
            if (_view == null) return;
            _view.Dispose();
            _view = null;
            _notice = null;
        }
    }

    // band 창. 상주 프로세스가 제목("Sill")으로 찾아 Sill.Key 메시지를 보낸다(M6 설계 3.1절). wParam: 0 토글, 1 다시 시작, 2 포커스.
    sealed class View : Control
    {
        static readonly uint KeyMessage = Native.RegisterWindowMessage("Sill.Key");
        public event Action<int> Key;

        public bool CaptionOff;             // 이번에 보일 때 BSIS_NOCAPTION을 적용한 뒤. 그 전의 배치에는 띠 머리가 있다
        bool _captionLogged;

        protected override void WndProc(ref Message m)
        {
            const int WM_WINDOWPOSCHANGING = 0x46;
            if (m.Msg == (int)KeyMessage && Key != null) { Key(m.WParam.ToInt32()); return; }
            if (m.Msg == WM_WINDOWPOSCHANGING) Place(m.LParam);
            base.WndProc(ref m);
        }

        // M7 설계 3.1절: rebar는 띠 머리가 없어도 band 창을 x = 2에 두고, BaseBar보다 4 px 넓게 잡는다.
        // band 창이 rebar 왼쪽 끝부터 BaseBar 오른쪽 끝까지 덮게 고친다. 띠 머리가 남았으면(BSIS_NOCAPTION이 안 들음) × 도구 모음을 숨기고 덮는다(QTTabBar의 방법).
        void Place(IntPtr lParam)
        {
            const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2;
            var p = (WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(WINDOWPOS));
            IntPtr rebar = Native.GetParent(Handle);
            RECT bar;
            if (!Native.GetClientRect(Native.GetParent(rebar), out bar)) return;
            if ((p.flags & SWP_NOMOVE) == 0 && p.x > 0)
            {
                if (p.x > 4 && CaptionOff && !_captionLogged)
                {
                    _captionLogged = true;
                    Shared.Log("band.log", "띠 머리 남음 x={0}, × 숨김", p.x);
                    Native.ShowWindow(Native.FindWindowEx(rebar, IntPtr.Zero, "ToolbarWindow32", null), 0);
                }
                if ((p.flags & SWP_NOSIZE) != 0) { p.cx = Width; p.flags &= ~SWP_NOSIZE; }
                p.cx += p.x;
                p.x = 0;
            }
            if ((p.flags & SWP_NOSIZE) == 0) p.cx = Math.Min(p.cx, bar.right - ((p.flags & SWP_NOMOVE) == 0 ? p.x : Left));
            Marshal.StructureToPtr(p, lParam, false);
        }
    }

    // 탐색기와 같은 색(M7 설계 표 4). 탐색기는 Windows의 앱 모드(AppsUseLightTheme)를 따른다.
    // band 창은 자식 창이라 모드 변경(WM_SETTINGCHANGE)을 받지 않으므로, 시작할 때와 페이지의 t를 받았을 때 읽는다.
    static class Theme
    {
        public static bool Light;
        public static Color Content { get { return Light ? Color.FromArgb(0xFF, 0xFF, 0xFF) : Color.FromArgb(0x19, 0x19, 0x19); } }   // 파일 목록, 터미널
        public static Color Frame { get { return Light ? Color.FromArgb(0xE8, 0xE8, 0xE8) : Color.FromArgb(0x20, 0x20, 0x20); } }     // 창 틀, 탭 줄
        public static Color Line { get { return Light ? Color.FromArgb(0xF7, 0xF7, 0xF7) : Color.FromArgb(0x2B, 0x2B, 0x2B); } }      // 세로 분할선 가운데

        public static void Read()
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                Light = k != null && k.GetValue("AppsUseLightTheme") is int && (int)k.GetValue("AppsUseLightTheme") != 0;
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
