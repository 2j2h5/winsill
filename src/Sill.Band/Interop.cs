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

    internal static class Native
    {
        public const int S_OK = 0, S_FALSE = 1, E_NOTIMPL = unchecked((int)0x80004001), E_FAIL = unchecked((int)0x80004005);

        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
        [DllImport("user32.dll")] public static extern IntPtr GetFocus();
        [DllImport("user32.dll")] public static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
    }
}
