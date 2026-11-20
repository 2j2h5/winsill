using System;
using System.Drawing;
using System.Windows.Forms;
using Sill.Band;

namespace Sill.Band { sealed class View : Control { public bool RightMode; } }

static class RightDockTest
{
    static int checks;
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }

    [STAThread] static int Main()
    {
        try
        {
            using (var parent = new Form { ClientSize = new Size(1000, 700), ShowInTaskbar = false })
            using (var home = new Panel())
            using (var shell = new Panel())
            using (var view = new Sill.Band.View())
            {
                parent.Controls.Add(home);
                parent.Controls.Add(shell);
                shell.Bounds = new Rectangle(30, 40, 900, 600);
                Native.SetParent(view.Handle, home.Handle);
                IntPtr parentHandle = parent.Handle;
                using (var dock = new RightDock(view))
                {
                    dock.Attach(shell.Handle);
                    dock.Show(true);
                    Check(shell.Width == 450 && view.Left == 480 && view.Width == 450, "Reserve right space without overlap");
                    Check(view.Top == 40 && view.Height == 600, "Preserve content origin and height");
                    dock.ResizeBy(-200);
                    Check(view.Width == 280 && shell.Width == 620, "Drag resize reserves matching space");
                    dock.ResizeBy(200);
                    Native.SetWindowPos(shell.Handle, IntPtr.Zero, 30, 40, 600, 450, 0x14);
                    Check(shell.Width == 300 && view.Width == 300 && view.Height == 450, "Reflow on native Explorer layout");
                    dock.Show(false);
                    Check(shell.Width == 600, "Restore width when panel hidden");
                    dock.Show(true);
                    Check(shell.Width == 300, "Reopen right panel");
                    for (int i = 0; i < 10; i++)
                    {
                        dock.Show(false); dock.Show(true);
                    }
                    Check(shell.Width == 300, "Repeated visibility changes do not shrink content");
                    using (var replacement = new Panel())
                    {
                        parent.Controls.Add(replacement);
                        replacement.Bounds = new Rectangle(30, 40, 900, 600);
                        dock.Attach(replacement.Handle);
                        Check(shell.Width == 600 && replacement.Width == 450, "Navigation restores old view and docks new view");
                        dock.Attach(shell.Handle);
                        Check(replacement.Width == 900 && shell.Width == 300, "Navigation back restores replacement view");
                    }
                    // 탐색기가 셸 뷰를 다른 부모로 옮기면(내 PC) View도 따라가 바로 옆에 선다.
                    using (var sink = new Panel())
                    {
                        parent.Controls.Add(sink);
                        sink.Bounds = new Rectangle(100, 20, 800, 650);
                        Native.SetParent(shell.Handle, sink.Handle);
                        Native.SetWindowPos(shell.Handle, IntPtr.Zero, 0, 0, 600, 450, 0x14);
                        Check(Native.GetParent(view.Handle) == sink.Handle && view.Left == shell.Right && view.Top == shell.Top, "Follow the shell view to its new parent");
                        Native.SetParent(shell.Handle, parent.Handle);
                        Native.SetWindowPos(shell.Handle, IntPtr.Zero, 30, 40, 600, 450, 0x14);
                    }
                }
                Check(shell.Width == 600 && Native.GetParent(view.Handle) == home.Handle && !view.RightMode, "Restore layout and parent on bottom/dispose");
                Check(RightDock.PanelWidth(200, 480) == 100, "Clamp narrow window");
            }
            Console.WriteLine("PASS: " + checks + " RightDock checks");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("FAIL: " + e); return 1; }
    }
}
