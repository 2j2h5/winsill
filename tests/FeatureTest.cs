using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Sill;
using Sill.Band;
using Sill.Host;

namespace Sill.Band { sealed class View : Control { public bool RightMode; } }

static class FeatureTest
{
    static int checks;
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }

    [STAThread] static int Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sill-feature-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, "{\"shell\":\"cmd\",\"alertOpensPanel\":false,\"custom\":{\"x\":3}}");
            Preferences.Save(path, "panelPosition", "right");
            var data = Preferences.Read(path);
            Check((string)data["shell"] == "cmd" && !(bool)data["alertOpensPanel"] && data.ContainsKey("custom"), "Preserve existing settings");
            Check((string)data["panelPosition"] == "right", "Save right position");
            Preferences.Save(path, "panelPosition", "bottom");
            Check((string)Preferences.Read(path)["panelPosition"] == "bottom", "Replace atomically");
            File.WriteAllText(path, "{broken");
            bool failed = false;
            try { Preferences.Save(path, "panelPosition", "right"); } catch { failed = true; }
            Check(failed && File.ReadAllText(path) == "{broken", "Do not overwrite malformed settings");
            Check(TerminalLauncher.Arguments(@"C:\") == "-w new new-tab -d " + (char)34 + @"C:\\" + (char)34, "Quote drive root");
            Check(TerminalLauncher.Arguments(@"C:\한글 folder") == "-w new new-tab -d " + (char)34 + @"C:\한글 folder" + (char)34, "Quote Unicode and spaces");
            Check(TerminalLauncher.Arguments(@"\\server\share\") == "-w new new-tab -d " + (char)34 + @"\\server\share\\" + (char)34, "Quote UNC root");
            Check(TerminalLauncher.Arguments(@"C:\a;b") == "-w new new-tab -d .", "Do not interpret semicolon as wt command");
            failed = false;
            try { TerminalLauncher.Arguments("::{virtual}"); } catch (ArgumentException) { failed = true; }
            Check(failed, "Reject virtual folders");

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
                }
                Check(shell.Width == 600 && Native.GetParent(view.Handle) == home.Handle && !view.RightMode, "Restore layout and parent on bottom/dispose");
                Check(RightDock.PanelWidth(200, 480) == 100, "Clamp narrow window");
            }
            Console.WriteLine("PASS: " + checks + " feature checks");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("FAIL: " + e); return 1; }
        finally { Directory.Delete(dir, true); }
    }
}
