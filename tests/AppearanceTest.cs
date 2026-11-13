using System;
using System.Collections.Generic;
using System.IO;
using Sill.Band;

static class AppearanceTest
{
    static int checks;
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
    static Dictionary<string, object> Obj(object value) { return (Dictionary<string, object>)value; }
    static int Main(string[] args)
    {
        string dir = Path.Combine(Path.GetTempPath(), "sill-appearance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string schemes = File.ReadAllText(args[0]);
            string json = @"{
                // Windows Terminal default profile, not simply the first profile
                ""defaultProfile"": ""{b}"",
                ""profiles"": {
                    ""defaults"": { ""colorScheme"": ""Campbell"", ""font"": { ""face"": ""JetBrainsMono Nerd Font"", ""size"": 12 }, ""cursorColor"": ""#16C60C"", },
                    ""list"": [
                        { ""guid"": ""{a}"", ""name"": ""Other"", ""font"": { ""face"": ""Other Font"" } },
                        { ""guid"": ""{b}"", ""name"": ""PowerShell"", ""font"": { ""size"": 14 }, },
                    ],
                },
            }";
            var result = TerminalAppearance.Resolve(json, schemes, true, dir);
            var options = Obj(result["options"]); var theme = Obj(options["theme"]);
            Check((string)result["profile"] == "PowerShell", "Select actual default profile");
            Check(((string)options["fontFamily"]).StartsWith("\"JetBrainsMono Nerd Font\""), "Inherit default font face when profile overrides size");
            Check(Math.Abs(Convert.ToDouble(options["fontSize"]) - 14 * 96.0 / 72) < .001, "Convert Terminal font points to CSS pixels");
            Check((string)theme["background"] == "#0C0C0C" && (string)theme["blue"] == "#0037DA", "Use real Campbell colors even when Windows is light");
            Check((string)theme["cursor"] == "#16C60C", "Apply profile cursor override");
            Check((string)theme["magenta"] == "#881798" && theme.ContainsKey("brightMagenta"), "Map Windows Terminal purple to xterm magenta");
            Check(Convert.ToInt32(options["minimumContrastRatio"]) == 1, "Do not alter Terminal palette through contrast correction");
            Check(!(bool)result["light"], "Terminal app theme defaults to dark independently of Explorer");
            string paired = @"{""theme"":""system"",""profiles"":{""defaults"":{""colorScheme"":{""light"":""One Half Light"",""dark"":""One Half Dark""}}}}";
            Check((string)TerminalAppearance.Resolve(paired, schemes, true, dir)["scheme"] == "One Half Light", "Resolve light/dark pair using Terminal app theme");
            Check((string)TerminalAppearance.Resolve(paired, schemes, false, dir)["scheme"] == "One Half Dark", "Resolve dark scheme pair");
            string custom = @"{""profiles"":{""defaults"":{""colorScheme"":""Mine"",""background"":""#123456"",""cursorShape"":""underscore"",""font"":{""weight"":""bold""}}},""schemes"":[{""name"":""Mine"",""background"":""#111111"",""foreground"":""#abc"",""purple"":""#ff00ff""}]}";
            result = TerminalAppearance.Resolve(custom, schemes, false, dir); options = Obj(result["options"]); theme = Obj(options["theme"]);
            Check((string)theme["background"] == "#123456" && (string)theme["foreground"] == "#abc", "Profile colors override custom scheme");
            Check((string)options["cursorStyle"] == "underline" && Convert.ToInt32(options["fontWeight"]) == 700, "Match cursor style and font weight");
            // URLs and comment-looking strings must survive JSONC normalization.
            string normalized = TerminalAppearance.Json(@"{/* block */ ""url"":""https://example.test/a//b"",""text"":""/* keep */ ,}"",}" );
            Check(normalized.Contains("https://example.test/a//b") && normalized.Contains("/* keep */ ,}"), "Do not strip comment markers or commas inside strings");
            bool invalid = false;
            try { TerminalAppearance.Resolve("{/* not closed", schemes, false, dir); } catch { invalid = true; }
            Check(invalid, "Reject unfinished comments so callers retain previous theme");
            File.WriteAllBytes(Path.Combine(dir, "bg.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aEikAAAAASUVORK5CYII="));
            string image = @"{""profiles"":{""defaults"":{""backgroundImage"":""bg.png"",""backgroundImageOpacity"":0.3,""backgroundImageStretchMode"":""fill"",""backgroundImageAlignment"":""bottomRight""}}}";
            result = TerminalAppearance.Resolve(image, schemes, false, dir);
            Check(((string)result["backgroundImage"]).StartsWith("data:image/png;base64,"), "Read only the configured local background image");
            Check((string)result["imageSize"] == "100% 100%" && (string)result["imagePosition"] == "bottom right" && Convert.ToDouble(result["imageOpacity"]) == .3, "Preserve image placement and opacity");
            result = TerminalAppearance.Resolve(image.Replace("bg.png", "missing.png"), schemes, false, dir);
            Check((string)result["backgroundImage"] == "", "Missing background image leaves terminal colors intact");
            result = TerminalAppearance.Resolve("{}", schemes, true, dir);
            Check(Convert.ToDouble(Obj(result["options"])["fontSize"]) == 16, "Use Terminal's default 12pt font, not Sill's old 13px");
            Console.WriteLine("PASS: " + checks + " appearance checks");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("FAIL: " + e); return 1; }
        finally { Directory.Delete(dir, true); }
    }
}
