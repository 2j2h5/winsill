using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace Sill.Host
{
    static class TerminalLauncher
    {
        internal static string Arguments(string folder)
        {
            if (string.IsNullOrEmpty(folder) || folder.StartsWith("::") || !Path.IsPathRooted(folder))
                throw new ArgumentException("Open a file-system folder in Explorer first.");
            if (folder.IndexOf('"') >= 0 || folder.IndexOf(';') >= 0)
                // wt treats semicolons as command separators even in quoted
                // arguments. Pass such paths via WorkingDirectory and -d . instead.
                return "-w new new-tab -d .";
            int trailing = folder.Length - folder.TrimEnd('\\').Length;
            return "-w new new-tab -d \"" + folder + new string('\\', trailing) + "\"";
        }

        public static int Open(string folder)
        {
            try
            {
                var start = new ProcessStartInfo("wt.exe", Arguments(folder));
                start.WorkingDirectory = folder;
                start.UseShellExecute = false;
                using (Process.Start(start)) { }
                Shared.Log("keys.log", "Windows Terminal 열기 {0}", folder);
                return 0;
            }
            catch (Exception e)
            {
                Shared.Log("keys.log", "Windows Terminal 실행 실패: {0}", e.Message);
                MessageBox.Show("Could not open Windows Terminal. Install Windows Terminal and enable its wt.exe app execution alias.\n\n" + e.Message,
                    "Sill", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }
        }
    }
}
