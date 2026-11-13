using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Sill
{
    // Shared by Explorer and the shortcut host. Preserve settings we do not own.
    static class Preferences
    {
        public static readonly string PathName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Sill\settings.json");

        public static Dictionary<string, object> Read(string path)
        {
            return File.Exists(path) ? new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path)) ?? new Dictionary<string, object>() : new Dictionary<string, object>();
        }

        public static string Text(string key, string fallback)
        {
            try { object v; return Read(PathName).TryGetValue(key, out v) && v is string ? (string)v : fallback; }
            catch (Exception e) { Shared.Log("keys.log", "설정 읽기 실패: {0}", e.Message); return fallback; }
        }

        public static bool Right { get { return Text("panelPosition", "bottom").Equals("right", StringComparison.OrdinalIgnoreCase); } }
        public static bool WindowsTerminal { get { return Text("terminal", "panel").Equals("windows-terminal", StringComparison.OrdinalIgnoreCase); } }

        public static void SavePosition(bool right)
        {
            using (var mutex = new Mutex(false, @"Local\Sill.Preferences"))
            {
                bool held = false;
                try
                {
                    try { held = mutex.WaitOne(2000); } catch (AbandonedMutexException) { held = true; }
                    if (!held) throw new IOException("Settings are busy. Try again.");
                    Save(PathName, "panelPosition", right ? "right" : "bottom");
                }
                finally { if (held) mutex.ReleaseMutex(); }
            }
        }

        internal static void Save(string path, string key, object value)
        {
            var data = Read(path); // Malformed JSON must never be overwritten.
            data[key] = value;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, new JavaScriptSerializer().Serialize(data), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
