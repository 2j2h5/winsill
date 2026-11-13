using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Sill.Band
{
    // Import appearance only: never forward Terminal commands, bindings or other
    // settings to the page, and never write to Windows Terminal's settings.
    static class TerminalAppearance
    {
        public static string FindSettings()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (string relative in new[] {
                @"Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe\LocalState\settings.json",
                @"Microsoft\Windows Terminal\settings.json",
                @"Packages\Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe\LocalState\settings.json" })
            {
                string path = Path.Combine(local, relative);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        public static string Stamp(string path)
        {
            if (path == null) return "none";
            var f = new FileInfo(path);
            return path + ":" + f.LastWriteTimeUtc.Ticks + ":" + f.Length;
        }

        public static string Serialize(object value)
        { return new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.Serialize(value); }

        static Dictionary<string, object> Object(object value)
        { return value as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        static object Get(Dictionary<string, object> data, string key)
        { object value; return data.TryGetValue(key, out value) ? value : null; }
        static string Text(Dictionary<string, object> data, string key, string fallback)
        { return Get(data, key) as string ?? fallback; }
        static double Number(Dictionary<string, object> data, string key, double fallback, double min, double max)
        {
            object value = Get(data, key);
            if (!(value is int) && !(value is long) && !(value is double) && !(value is decimal)) return fallback;
            double n = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return double.IsNaN(n) || double.IsInfinity(n) ? fallback : Math.Max(min, Math.Min(max, n));
        }
        static IEnumerable Items(object value) { return value as IList ?? new object[0]; }

        // Windows Terminal uses JSONC. Preserve quoted URLs, escaped quotes,
        // comment-like font names, and commas inside strings.
        internal static string Json(string source)
        {
            var clean = new StringBuilder();
            bool quoted = false, escape = false;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (quoted)
                {
                    clean.Append(c);
                    if (escape) escape = false;
                    else if (c == '\\') escape = true;
                    else if (c == '"') quoted = false;
                }
                else if (c == '"') { quoted = true; clean.Append(c); }
                else if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
                { while (i < source.Length && source[i] != '\n') i++; clean.Append('\n'); }
                else if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/')) i++;
                    if (i + 1 >= source.Length) throw new FormatException("Unclosed settings comment.");
                    i++; clean.Append(' ');
                }
                else clean.Append(c);
            }
            source = clean.ToString(); clean.Length = 0; quoted = false; escape = false;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (!quoted && c == ',')
                {
                    int next = i + 1;
                    while (next < source.Length && char.IsWhiteSpace(source[next])) next++;
                    if (next < source.Length && (source[next] == '}' || source[next] == ']')) continue;
                }
                clean.Append(c);
                if (escape) escape = false;
                else if (quoted && c == '\\') escape = true;
                else if (c == '"') quoted = !quoted;
            }
            return clean.ToString();
        }

        internal static Dictionary<string, object> Resolve(string settings, string schemes, bool systemLight, string directory)
        {
            var parser = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
            var root = Object(parser.DeserializeObject(Json(settings)));
            var profiles = Object(Get(root, "profiles"));
            var profile = new Dictionary<string, object>(Object(Get(profiles, "defaults")));
            var font = new Dictionary<string, object>(Object(Get(profile, "font")));
            string selected = Text(root, "defaultProfile", "");
            Dictionary<string, object> chosen = null;
            foreach (object item in Items(Get(profiles, "list") ?? Get(root, "profiles")))
            {
                var p = Object(item);
                if (chosen == null && !Equals(Get(p, "hidden"), true)) chosen = p;
                if (string.Equals(Text(p, "guid", ""), selected, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Text(p, "name", ""), selected, StringComparison.OrdinalIgnoreCase))
                { chosen = p; break; }
            }
            if (chosen != null)
            {
                foreach (var pair in chosen) profile[pair.Key] = pair.Value;
                foreach (var pair in Object(Get(chosen, "font"))) font[pair.Key] = pair.Value;
            }
            string appTheme = Text(root, "theme", "dark");
            bool light = appTheme == "light" || (appTheme == "system" && systemLight);
            object schemeValue = Get(profile, "colorScheme");
            string schemeName = schemeValue as string ?? Text(Object(schemeValue), light ? "light" : "dark", "Campbell");
            var palette = new Dictionary<string, object>();
            foreach (object item in Items(parser.DeserializeObject(schemes)))
            {
                var s = Object(item);
                if (Text(s, "name", "") == "Campbell") foreach (var pair in s) palette[pair.Key] = pair.Value;
            }
            foreach (IEnumerable list in new[] { Items(parser.DeserializeObject(schemes)), Items(Get(root, "schemes")) })
                foreach (object item in list)
                {
                    var s = Object(item);
                    if (string.Equals(Text(s, "name", ""), schemeName, StringComparison.OrdinalIgnoreCase))
                        foreach (var pair in s) palette[pair.Key] = pair.Value;
                }
            foreach (string key in new[] { "background", "foreground", "cursorColor", "selectionBackground" })
                if (Get(profile, key) is string) palette[key] = profile[key];
            var colors = new Dictionary<string, object>();
            foreach (string key in new[] { "background", "foreground", "cursorColor", "selectionBackground", "black", "red", "green", "yellow", "blue", "purple", "cyan", "white", "brightBlack", "brightRed", "brightGreen", "brightYellow", "brightBlue", "brightPurple", "brightCyan", "brightWhite" })
            {
                string color = Get(palette, key) as string;
                if (color != null && Regex.IsMatch(color, @"^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$"))
                    colors[key == "cursorColor" ? "cursor" : key == "purple" ? "magenta" : key == "brightPurple" ? "brightMagenta" : key] = color;
            }
            string face = Text(font, "face", Text(profile, "fontFace", "Cascadia Mono"));
            string shape = Text(profile, "cursorShape", "bar");
            object weight = Get(font, "weight") ?? Get(profile, "fontWeight") ?? 400;
            string weightText = Convert.ToString(weight, CultureInfo.InvariantCulture);
            int weightNumber;
            if (!int.TryParse(weightText, out weightNumber)) weightNumber = weightText == "bold" ? 700 : weightText == "semi-bold" ? 600 : weightText == "light" ? 300 : 400;
            var options = new Dictionary<string, object> {
                { "theme", colors },
                { "fontFamily", "\"" + face.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\", \"Cascadia Mono\", Consolas, monospace" },
                { "fontSize", Number(font, "size", Number(profile, "fontSize", 12, 4, 96), 4, 96) * 96.0 / 72.0 },
                { "fontWeight", Math.Max(100, Math.Min(900, weightNumber)) },
                { "cursorStyle", shape == "bar" ? "bar" : shape == "underscore" || shape == "vintage" ? "underline" : "block" },
                { "cursorWidth", 1 }, { "minimumContrastRatio", 1 }
            };
            string stretch = Text(profile, "backgroundImageStretchMode", "uniformToFill");
            string align = Text(profile, "backgroundImageAlignment", "center");
            var result = new Dictionary<string, object> {
                { "options", options }, { "backgroundImage", Image(Text(profile, "backgroundImage", ""), directory) },
                { "imageOpacity", Number(profile, "backgroundImageOpacity", 1, 0, 1) },
                { "imageSize", stretch == "fill" ? "100% 100%" : stretch == "none" ? "auto" : stretch == "uniform" ? "contain" : "cover" },
                { "imagePosition", Regex.Replace(align, "([a-z])([A-Z])", "$1 $2").ToLowerInvariant() },
                { "padding", Text(profile, "padding", "8") }, { "profile", Text(chosen ?? profile, "name", "Default") },
                { "scheme", schemeName }, { "light", light }
            };
            return result;
        }

        static string Image(string value, string directory)
        {
            try { return ReadImage(value, directory); }
            catch (IOException) { return ""; }
            catch (UnauthorizedAccessException) { return ""; }
            catch (ArgumentException) { return ""; }
        }

        static string ReadImage(string value, string directory)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value == "desktopWallpaper")
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                    value = key == null ? "" : key.GetValue("WallPaper", "") as string ?? "";
            value = Environment.ExpandEnvironmentVariables(value);
            Uri uri;
            if (Uri.TryCreate(value, UriKind.Absolute, out uri))
            {
                if (!uri.IsFile) return "";
                value = uri.LocalPath;
            }
            if (!Path.IsPathRooted(value)) value = Path.Combine(directory, value);
            var file = new FileInfo(value);
            string ext = file.Extension.ToLowerInvariant();
            string mime = ext == ".png" ? "image/png" : ext == ".jpg" || ext == ".jpeg" ? "image/jpeg" : ext == ".gif" ? "image/gif" : ext == ".webp" ? "image/webp" : ext == ".bmp" ? "image/bmp" : null;
            if (mime == null || !file.Exists || file.Length > 8 * 1024 * 1024) return "";
            return "data:" + mime + ";base64," + Convert.ToBase64String(File.ReadAllBytes(value));
        }
    }
}
