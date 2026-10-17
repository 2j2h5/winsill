using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

[assembly: ComVisible(false)]

namespace Sill.Band
{
    // 가로형 Explorer Bar(CATID_CommBand). M0: 회색 패널만 그리고 모든 호출을 로그로 남긴다.
    // 탐색기 안에서 예외가 새면 탐색기가 죽으므로, 모든 COM 메서드는 Guard로 감싸 HRESULT로 돌려준다.
    // UserControl을 직접 COM에 노출하지 않는 이유: Control이 내부적으로 같은 IID(IOleWindow 등)를 구현해 충돌한다.
    [ComVisible(true), Guid(Clsid), ClassInterface(ClassInterfaceType.None)]
    public class SillBand : IDeskBand, IObjectWithSite, IPersistStream, IInputObject
    {
        public const string Clsid = "AE2C3550-FB10-4D93-B6E1-4FBED3C0AD90";

        static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Sill\band.log");

        object _site;
        Control _view;

        static void Log(string format, params object[] args)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                File.AppendAllText(LogPath, string.Format("{0:HH:mm:ss.fff} [{1}] {2}\r\n",
                    DateTime.Now, Process.GetCurrentProcess().Id, string.Format(format, args)));
            }
            catch { }
        }

        static int Guard(string name, Func<int> body)
        {
            try { return body(); }
            catch (Exception e) { Log("{0} 예외: {1}", name, e); return Native.E_FAIL; }
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
                _view = new Label
                {
                    Text = "Sill M0",
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = Color.FromArgb(200, 200, 200),
                    Font = new Font("Segoe UI", 14f),
                    Visible = false,
                };
                _view.GotFocus += (s, e) => NotifyFocus(true);
                _view.LostFocus += (s, e) => NotifyFocus(false);
                IntPtr old = Native.SetParent(_view.Handle, parent);
                Log("SetParent 결과 old=0x{0:X} err={1}", old.ToInt64(), Marshal.GetLastWin32Error());
                return Native.S_OK;
            });
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

        // --- IInputObject: M0은 포커스만 보고한다. 키 처리는 M1 ---

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

        public int TranslateAcceleratorIO(ref MSG pMsg) { return Native.S_FALSE; }

        void NotifyFocus(bool focused)
        {
            var s = _site as IInputObjectSite;
            if (s != null) s.OnFocusChangeIS(this, focused);
        }

        void DestroyView()
        {
            if (_view == null) return;
            _view.Dispose();
            _view = null;
        }
    }
}
