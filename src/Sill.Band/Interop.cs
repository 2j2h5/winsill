using System;
using System.Runtime.InteropServices;

// 셸 band 객체가 구현·사용하는 COM 인터페이스 선언 (shobjidl.h, ocidl.h).
// 상속 인터페이스는 ComImport에서 부모 메서드를 앞에 다시 선언해야 vtable 순서가 맞는다.
namespace Sill.Band
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINTL { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINTL pt;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DESKBANDINFO
    {
        public uint dwMask;
        public POINTL ptMinSize;
        public POINTL ptMaxSize;
        public POINTL ptIntegral;
        public POINTL ptActual;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string wszTitle;
        public uint dwModeFlags;
        public uint crBkgnd;
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("00000114-0000-0000-C000-000000000046")]
    public interface IOleWindow
    {
        [PreserveSig] int GetWindow(out IntPtr phwnd);
        [PreserveSig] int ContextSensitiveHelp(bool fEnterMode);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("EB0FE172-1A3A-11D0-89B3-00A0C90A90AC")]
    public interface IDeskBand
    {
        // IOleWindow
        [PreserveSig] int GetWindow(out IntPtr phwnd);
        [PreserveSig] int ContextSensitiveHelp(bool fEnterMode);
        // IDockingWindow
        [PreserveSig] int ShowDW(bool fShow);
        [PreserveSig] int CloseDW(uint dwReserved);
        [PreserveSig] int ResizeBorderDW(IntPtr prcBorder, [MarshalAs(UnmanagedType.IUnknown)] object punkToolbarSite, bool fReserved);
        // IDeskBand
        [PreserveSig] int GetBandInfo(uint dwBandID, uint dwViewMode, ref DESKBANDINFO pdbi);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("FC4801A3-2BA9-11CF-A229-00AA003D7352")]
    public interface IObjectWithSite
    {
        [PreserveSig] int SetSite([MarshalAs(UnmanagedType.IUnknown)] object pUnkSite);
        [PreserveSig] int GetSite(ref Guid riid, out IntPtr ppvSite);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("00000109-0000-0000-C000-000000000046")]
    public interface IPersistStream
    {
        // IPersist
        [PreserveSig] int GetClassID(out Guid pClassID);
        // IPersistStream
        [PreserveSig] int IsDirty();
        [PreserveSig] int Load([MarshalAs(UnmanagedType.IUnknown)] object pStm);
        [PreserveSig] int Save([MarshalAs(UnmanagedType.IUnknown)] object pStm, bool fClearDirty);
        [PreserveSig] int GetSizeMax(out ulong pcbSize);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("68284FAA-6A48-11D0-8C78-00C04FD918B4")]
    public interface IInputObject
    {
        [PreserveSig] int UIActivateIO(bool fActivate, ref MSG pMsg);
        [PreserveSig] int HasFocusIO();
        [PreserveSig] int TranslateAcceleratorIO(ref MSG pMsg);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("F1DB8392-7331-11D0-8C99-00A04FD918B4")]
    public interface IInputObjectSite
    {
        [PreserveSig] int OnFocusChangeIS([MarshalAs(UnmanagedType.IUnknown)] object punkObj, bool fSetFocus);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    public interface IServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
    }

    // 탐색기 창(탭)의 셸 브라우저 (shobjidl_core.h). QueryActiveShellView까지만 순서대로 선언하고 뒤의 메서드는 부르지 않는다.
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214E2-0000-0000-C000-000000000046")]
    public interface IShellBrowser
    {
        // IOleWindow
        [PreserveSig] int GetWindow(out IntPtr phwnd);
        [PreserveSig] int ContextSensitiveHelp(bool fEnterMode);
        // IShellBrowser
        [PreserveSig] int InsertMenusSB(IntPtr hmenuShared, IntPtr lpMenuWidths);
        [PreserveSig] int SetMenuSB(IntPtr hmenuShared, IntPtr holemenuRes, IntPtr hwndActiveObject);
        [PreserveSig] int RemoveMenusSB(IntPtr hmenuShared);
        [PreserveSig] int SetStatusTextSB([MarshalAs(UnmanagedType.LPWStr)] string pszStatusText);
        [PreserveSig] int EnableModelessSB(bool fEnable);
        [PreserveSig] int TranslateAcceleratorSB(ref MSG pmsg, ushort wID);
        [PreserveSig] int BrowseObject(IntPtr pidl, uint wFlags);
        [PreserveSig] int GetViewStateStream(uint grfMode, out IntPtr ppStrm);
        [PreserveSig] int GetControlWindow(uint id, out IntPtr phwnd);
        [PreserveSig] int SendControlMsg(uint id, uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr pret);
        [PreserveSig] int QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object ppshv);
    }

    // 파일 목록 보기 (shobjidl_core.h). 탐색기의 보기는 IOleWindow로의 QueryInterface를 거절하므로 IShellView로 받는다(M6에서 겪음).
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214E3-0000-0000-C000-000000000046")]
    public interface IShellView
    {
        [PreserveSig] int GetWindow(out IntPtr phwnd);
    }

    // 탐색기 창(탭)의 이벤트 (exdisp.h). 쓰는 것만 선언한다. 나머지 DISPID는 CLR이 DISP_E_MEMBERNOTFOUND로 돌려준다.
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIDispatch), Guid("34A715A0-6587-11D0-924A-0020AFC7AC4D")]
    public interface DWebBrowserEvents2
    {
        [DispId(252)] void NavigateComplete2([MarshalAs(UnmanagedType.IDispatch)] object pDisp, [In] ref object URL);
    }

    // band를 담는 사이트(shobjidl_core.h). 스타일만 바꾸지만 vtable 순서대로 모두 선언한다.
    // BSIS_NOCAPTION은 문서에 "제목 글자를 숨김"으로만 있으나, 탐색기의 Explorer Bar에서는 왼쪽 띠 머리와 ×까지 없앤다(M7 설계 2.2절).
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("4CF504B0-DE96-11D0-8B3F-00A0C911E8E5")]
    public interface IBandSite
    {
        [PreserveSig] int AddBand([MarshalAs(UnmanagedType.IUnknown)] object punk);
        [PreserveSig] int EnumBands(uint uBand, out uint pdwBandID);
        [PreserveSig] int QueryBand(uint dwBandID, IntPtr ppstb, out uint pdwState, IntPtr pszName, int cchName);
        [PreserveSig] int SetBandState(uint dwBandID, uint dwMask, uint dwState);
        [PreserveSig] int RemoveBand(uint dwBandID);
        [PreserveSig] int GetBandObject(uint dwBandID, ref Guid riid, IntPtr ppv);
        [PreserveSig] int SetBandSiteInfo(ref BANDSITEINFO pbsinfo);
        [PreserveSig] int GetBandSiteInfo(ref BANDSITEINFO pbsinfo);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BANDSITEINFO { public uint dwMask, dwState, dwStyle; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize, flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }

    internal static class Native
    {
        public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr hWndParent, EnumProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);
        [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO pgui);
        public const int S_OK = 0, S_FALSE = 1, E_NOTIMPL = unchecked((int)0x80004001), E_FAIL = unchecked((int)0x80004005);

        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
        [DllImport("user32.dll")] public static extern IntPtr GetFocus();
        [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string lpString);
        [DllImport("user32.dll")] public static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr ILCreateFromPathW(string pszPath);
        [DllImport("shell32.dll")] public static extern void ILFree(IntPtr pidl);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);

        // 탐색기 창(BaseBar)의 서브클래스. 다른 확장과 순서가 꼬이지 않도록 comctl32의 방식을 쓴다(M7 설계 3.2절).
        public delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData);
        [DllImport("comctl32.dll")] public static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);
        [DllImport("comctl32.dll")] public static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);
        [DllImport("comctl32.dll")] public static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
    }
}
