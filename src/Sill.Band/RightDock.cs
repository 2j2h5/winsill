using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Sill.Band
{
    // The Explorer Bar remains the session owner. In right mode its view is a
    // sibling of the active shell view, so no bottom space is reserved. Intercept
    // Explorer's requested shell-view bounds before layout (never poll or overlay
    // the file list). Keep the original bounds to restore on hide/dispose.
    sealed class RightDock : IDisposable
    {
        readonly View _view;
        readonly IntPtr _home;
        readonly Native.SubclassProc _proc;
        IntPtr _shell;
        Rectangle _full;
        bool _placing;
        int _width = 480;
        bool _shown;

        public RightDock(View view)
        {
            _view = view;
            _home = Native.GetParent(view.Handle);
            _proc = WindowProc;
            view.RightMode = true;
        }

        public void Attach(IntPtr shell)
        {
            if (shell == _shell) return;
            Restore();
            _shell = shell;
            if (shell == IntPtr.Zero) return;
            _full = BoundsOf(shell);
            if (!Native.SetWindowSubclass(shell, _proc, UIntPtr.Zero, UIntPtr.Zero))
            { _shell = IntPtr.Zero; throw new InvalidOperationException("Cannot attach the right panel to this Explorer view."); }
            Native.SetParent(_view.Handle, Native.GetParent(shell));
            Layout();
        }

        static Rectangle BoundsOf(IntPtr hwnd)
        {
            RECT r;
            Native.GetWindowRect(hwnd, out r);
            Native.MapWindowPoints(IntPtr.Zero, Native.GetParent(hwnd), ref r, 2);
            return Rectangle.FromLTRB(r.left, r.top, r.right, r.bottom);
        }

        // Leave at least half the available width for Explorer, even in small windows.
        internal static int PanelWidth(int available, int requested)
        { return Math.Max(0, Math.Min(Math.Max(180, requested), available / 2)); }

        public void Show(bool shown)
        {
            _shown = shown;
            Layout();
            _view.Visible = shown;
        }

        public void ResizeBy(int delta)
        {
            _width = PanelWidth(_full.Width, _width + delta);
            Layout();
        }

        void Layout()
        {
            if (_shell == IntPtr.Zero || _placing) return;
            _placing = true;
            try
            {
                int width = _shown ? PanelWidth(_full.Width, _width) : 0;
                Native.SetWindowPos(_shell, IntPtr.Zero, _full.X, _full.Y, _full.Width - width, _full.Height, 0x14);
                _view.Bounds = new Rectangle(_full.Right - width, _full.Y, width, _full.Height);
                if (_shown) _view.BringToFront();
            }
            finally { _placing = false; }
        }

        IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l, UIntPtr id, UIntPtr data)
        {
            bool dispatched = false;
            IntPtr result = IntPtr.Zero;
            try
            {
                if (msg == 0x46 && !_placing) // WM_WINDOWPOSCHANGING
                {
                    var p = (WINDOWPOS)Marshal.PtrToStructure(l, typeof(WINDOWPOS));
                    if ((p.flags & 2) == 0) { _full.X = p.x; _full.Y = p.y; }
                    if ((p.flags & 1) == 0) { _full.Width = p.cx; _full.Height = p.cy; }
                    if (_shown)
                    {
                        p.flags &= ~1u;
                        p.cx = _full.Width - PanelWidth(_full.Width, _width);
                        p.cy = _full.Height;
                        Marshal.StructureToPtr(p, l, false);
                    }
                }
                result = Native.DefSubclassProc(hwnd, msg, w, l);
                dispatched = true;
                if (msg == 0x47 && !_placing) Layout(); // WM_WINDOWPOSCHANGED
                if (msg == 0x82) // A navigation can replace the shell view.
                {
                    Native.RemoveWindowSubclass(hwnd, _proc, id);
                    // Move out before Explorer destroys the old shell view's parent.
                    Native.SetParent(_view.Handle, _home);
                    _shell = IntPtr.Zero;
                }
                return result;
            }
            catch (Exception e)
            {
                Shared.Log("band.log", "오른쪽 배치 실패: {0}", e);
                return dispatched ? result : Native.DefSubclassProc(hwnd, msg, w, l);
            }
        }

        void Restore()
        {
            if (_shell == IntPtr.Zero) return;
            Native.RemoveWindowSubclass(_shell, _proc, UIntPtr.Zero);
            Native.SetWindowPos(_shell, IntPtr.Zero, _full.X, _full.Y, _full.Width, _full.Height, 0x14);
            _shell = IntPtr.Zero;
        }

        public void Dispose()
        {
            _shown = false;
            Restore();
            Native.SetParent(_view.Handle, _home);
            _view.RightMode = false;
        }
    }
}
