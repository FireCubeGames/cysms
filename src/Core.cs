// CYSMS - core logic with no Windows UI dependency (unit-testable on any OS).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Cysms
{
    // =========================================================================
    // Minimal JSON reader/writer (objects -> Dictionary, arrays -> List)
    // =========================================================================
    public static class Json
    {
        public static object Parse(string s)
        {
            int i = 0;
            var v = ParseValue(s, ref i);
            SkipWs(s, ref i);
            if (i != s.Length) throw new FormatException("Trailing characters in JSON at " + i);
            return v;
        }

        static void SkipWs(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON");
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(StringComparer.Ordinal);
                i++; SkipWs(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    SkipWs(s, ref i);
                    string k = ParseString(s, ref i);
                    SkipWs(s, ref i);
                    if (s[i] != ':') throw new FormatException("Expected ':' at " + i);
                    i++;
                    d[k] = ParseValue(s, ref i);
                    SkipWs(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("Expected ',' or '}' at " + i);
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++; SkipWs(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(ParseValue(s, ref i));
                    SkipWs(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("Expected ',' or ']' at " + i);
                }
            }
            if (c == '"') return ParseString(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (start == i) throw new FormatException("Unexpected character '" + c + "' at " + i);
            var num = s.Substring(start, i - start);
            if (long.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out long lv))
            {
                if (lv >= int.MinValue && lv <= int.MaxValue) return (int)lv;
                return lv;
            }
            return double.Parse(num, CultureInfo.InvariantCulture);
        }

        static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("Expected string at " + i);
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: throw new FormatException("Bad escape at " + i);
                }
            }
            throw new FormatException("Unterminated string");
        }

        public static string Write(object v)
        {
            var sb = new StringBuilder();
            WriteValue(sb, v, 0);
            return sb.ToString();
        }

        static void Indent(StringBuilder sb, int n) { sb.Append('\n').Append(' ', n * 2); }

        static void WriteValue(StringBuilder sb, object v, int lvl)
        {
            switch (v)
            {
                case null: sb.Append("null"); return;
                case string str: WriteString(sb, str); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case int _:
                case long _:
                case short _:
                case decimal _:
                    sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); return;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); return;
                case System.Collections.IDictionary dict:
                    {
                        if (dict.Count == 0) { sb.Append("{}"); return; }
                        sb.Append('{');
                        bool first = true;
                        foreach (System.Collections.DictionaryEntry kv in dict)
                        {
                            if (!first) sb.Append(',');
                            first = false;
                            Indent(sb, lvl + 1);
                            WriteString(sb, Convert.ToString(kv.Key, CultureInfo.InvariantCulture));
                            sb.Append(": ");
                            WriteValue(sb, kv.Value, lvl + 1);
                        }
                        Indent(sb, lvl); sb.Append('}');
                        return;
                    }
                case System.Collections.IEnumerable list:
                    {
                        sb.Append('[');
                        bool first = true;
                        foreach (var item in list)
                        {
                            if (!first) sb.Append(',');
                            first = false;
                            Indent(sb, lvl + 1);
                            WriteValue(sb, item, lvl + 1);
                        }
                        if (!first) Indent(sb, lvl);
                        sb.Append(']');
                        return;
                    }
                default: WriteString(sb, Convert.ToString(v, CultureInfo.InvariantCulture)); return;
            }
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    // =========================================================================
    // Settings schema: every scrcpy option shown in the UI is described once
    // =========================================================================
    public class Opt
    {
        public string K, Tab, T, L, F, Skip, Dep, Tip, Suffix, Pick;
        public object D;
        public bool Neg;
        public int Min, Max;
        public string[] O;      // value,label pairs for combos
        public bool HasSkip => Skip != null;
    }

    public static class Schema
    {
        public const string CtlDep = "!ViewOnly&VideoSource=display";

        public static readonly string[] TabOrder = { "Video", "Audio", "Control", "Device", "Window", "Desktop", "Record", "Advanced" };

        public static string DefaultRecordDir = "";

        public static readonly List<Opt> All = new List<Opt>
        {
            // ---- Video
            new Opt { K="VideoSource", Tab="Video", T="combo", L="Source", D="display", F="--video-source", Skip="display",
                O=new[]{"display","Device screen","camera","Device camera (Android 12+)"}, Tip="Mirror the screen, or stream a phone camera instead." },
            new Opt { K="MaxSize", Tab="Video", T="num", L="Max size (px, longest side, 0 = native)", D=1050, Min=0, Max=8192, F="--max-size", Skip="0",
                Tip="Lower values are smoother and use less bandwidth." },
            new Opt { K="MaxFps", Tab="Video", T="num", L="Max frame rate (0 = unlimited)", D=60, Min=0, Max=240, F="--max-fps", Skip="0" },
            new Opt { K="AutoBitRate", Tab="Video", T="check", L="Choose quality automatically (bit rate and size from USB / Wi-Fi speed)", D=false,
                Tip="On connect: USB gets full resolution and a high bit rate; Wi-Fi gets settings based on the link speed the phone reports." },
            new Opt { K="VideoBitRate", Tab="Video", T="num", L="Video bit rate (Mbps)", D=10, Min=1, Max=200, F="--video-bit-rate", Suffix="M", Skip="8", Dep="!AutoBitRate",
                Tip="scrcpy default is 8 Mbps. Lower it on weak Wi-Fi, raise it over USB." },
            new Opt { K="VideoCodec", Tab="Video", T="combo", L="Video codec", D="h265", F="--video-codec", Skip="h264",
                O=new[]{"h264","H.264 (most compatible)","h265","H.265 / HEVC (sharper, less bandwidth)","av1","AV1 (newest devices only)"},
                Tip="If mirroring fails to start, try H.264." },
            new Opt { K="VideoEncoder", Tab="Video", T="pick", Pick="encoders", L="Video encoder", D="", F="--video-encoder",
                Tip="Blank = the phone's default encoder. Press Load to list this phone's encoders." },
            new Opt { K="CaptureOrientation", Tab="Video", T="combo", L="Capture orientation", D="", F="--capture-orientation", Skip="",
                O=new[]{"","Follow device","@","Lock to current","0","0 deg","90","90 deg","180","180 deg","270","270 deg","@0","Lock 0 deg","@90","Lock 90 deg","@180","Lock 180 deg","@270","Lock 270 deg"},
                Tip="Lock stops the window rotating when the phone rotates." },
            new Opt { K="Crop", Tab="Video", T="text", L="Crop (width:height:x:y)", D="", F="--crop", Tip="Mirror only part of the screen, e.g. 1224:1440:0:0" },
            new Opt { K="DisplayId", Tab="Video", T="pick", Pick="displays", L="Display", D="", F="--display-id", Dep="VideoSource=display&!NewDisplay",
                Tip="Blank = main display. Press Load to list this phone's displays." },
            new Opt { K="CameraId", Tab="Video", T="pick", Pick="cameras", L="Camera", D="", F="--camera-id", Dep="VideoSource=camera",
                Tip="Blank = choose by facing below. Press Load to list this phone's cameras." },
            new Opt { K="CameraFacing", Tab="Video", T="combo", L="Camera facing", D="back", F="--camera-facing", Skip="", Dep="VideoSource=camera",
                O=new[]{"back","Back","front","Front","external","External"} },
            new Opt { K="CameraSize", Tab="Video", T="text", L="Camera resolution", D="", F="--camera-size", Dep="VideoSource=camera",
                Tip="e.g. 1920x1080. Blank = automatic (uses Max size)." },

            // ---- Audio
            new Opt { K="Audio", Tab="Audio", T="check", L="Forward device audio to this PC (Android 11+)", D=false, F="--no-audio", Neg=true },
            new Opt { K="AudioSource", Tab="Audio", T="combo", L="Audio source", D="output", F="--audio-source", Skip="output", Dep="Audio",
                O=new[]{"output","Device output (default)","playback","App playback (Android 13+)","mic","Microphone"} },
            new Opt { K="AudioCodec", Tab="Audio", T="combo", L="Audio codec", D="opus", F="--audio-codec", Skip="opus", Dep="Audio",
                O=new[]{"opus","Opus (default)","aac","AAC","flac","FLAC (lossless)","raw","Raw PCM"} },
            new Opt { K="AudioBitRate", Tab="Audio", T="num", L="Audio bit rate (kbps)", D=128, Min=32, Max=512, F="--audio-bit-rate", Suffix="K", Skip="128", Dep="Audio" },
            new Opt { K="AudioDup", Tab="Audio", T="check", L="Keep sound playing on the device too (Android 13+)", D=false, F="--audio-dup", Dep="Audio",
                Tip="Plays on both phone and PC. Uses app-playback capture." },
            new Opt { K="AudioBuffer", Tab="Audio", T="num", L="Audio buffer (ms)", D=50, Min=0, Max=1000, F="--audio-buffer", Skip="50", Dep="Audio",
                Tip="Raise this if audio crackles over Wi-Fi." },

            // ---- Control
            new Opt { K="ViewOnly", Tab="Control", T="check", L="View only (no mouse / keyboard control)", D=false, F="--no-control" },
            new Opt { K="Keyboard", Tab="Control", T="combo", L="Keyboard", D="sdk", F="--keyboard", Skip="sdk", Dep=CtlDep,
                O=new[]{"sdk","Standard (default)","uhid","Physical keyboard simulation (UHID)","aoa","AOA (USB only)","disabled","Disabled"},
                Tip="UHID behaves like a real keyboard and uses the phone keyboard layout." },
            new Opt { K="Mouse", Tab="Control", T="combo", L="Mouse", D="sdk", F="--mouse", Skip="sdk", Dep=CtlDep,
                O=new[]{"sdk","Standard (default)","uhid","Physical mouse simulation (UHID)","aoa","AOA (USB only)","disabled","Disabled"} },
            new Opt { K="Gamepad", Tab="Control", T="combo", L="Gamepad", D="disabled", F="--gamepad", Skip="disabled", Dep=CtlDep,
                O=new[]{"disabled","Off","uhid","Gamepad simulation (UHID)","aoa","AOA (USB only)"} },
            new Opt { K="ShortcutMod", Tab="Control", T="combo", L="Shortcut key", D="lalt", F="--shortcut-mod", Skip="lalt,lsuper",
                O=new[]{"lalt,lsuper","Left Alt or Left Win (scrcpy default)","lalt","Left Alt","lctrl","Left Ctrl","lsuper","Left Win","ralt","Right Alt","rctrl","Right Ctrl"},
                Tip="The MOD key used for scrcpy shortcuts (see Help > Shortcuts)." },
            new Opt { K="ClipboardSync", Tab="Control", T="check", L="Sync clipboard automatically", D=true, F="--no-clipboard-autosync", Neg=true, Dep=CtlDep },
            new Opt { K="LegacyPaste", Tab="Control", T="check", L="Legacy paste (type clipboard as keystrokes)", D=false, F="--legacy-paste", Dep=CtlDep,
                Tip="Use this if Ctrl+V does not paste on some devices." },

            // ---- Device
            new Opt { K="StayAwake", Tab="Device", T="check", L="Keep device awake while mirroring (while charging)", D=true, F="--stay-awake", Dep=CtlDep },
            new Opt { K="TurnScreenOff", Tab="Device", T="check", L="Turn device screen off while mirroring", D=true, F="--turn-screen-off", Dep=CtlDep },
            new Opt { K="ShowTouches", Tab="Device", T="check", L="Show touches on the device", D=false, F="--show-touches" },
            new Opt { K="NoPowerOn", Tab="Device", T="check", L="Do not wake the device on connect", D=false, F="--no-power-on" },
            new Opt { K="PowerOffOnClose", Tab="Device", T="check", L="Turn device screen off when mirroring ends", D=false, F="--power-off-on-close", Dep=CtlDep },

            // ---- Window
            new Opt { K="WindowTitle", Tab="Window", T="text", L="Window title", D="", Tip="Blank = the device name." },
            new Opt { K="AlwaysOnTop", Tab="Window", T="check", L="Always on top", D=false, F="--always-on-top" },
            new Opt { K="Fullscreen", Tab="Window", T="check", L="Start in fullscreen", D=false, F="--fullscreen" },
            new Opt { K="Borderless", Tab="Window", T="check", L="Borderless window", D=false, F="--window-borderless" },
            new Opt { K="DisplayOrientation", Tab="Window", T="combo", L="Window rotation", D="", F="--display-orientation", Skip="",
                O=new[]{"","Same as capture","90","Rotate 90 deg","180","Rotate 180 deg","270","Rotate 270 deg","flip0","Mirror","flip180","Mirror + 180 deg"} },
            new Opt { K="DisableScreensaver", Tab="Window", T="check", L="Prevent the PC screensaver while mirroring", D=true, F="--disable-screensaver" },

            // ---- Desktop (virtual display)
            new Opt { K="NewDisplay", Tab="Desktop", T="check", L="Use a separate virtual display (desktop-style)", D=false,
                Tip="Opens a new display on the phone, separate from its own screen. Behaviour varies by Android version and brand." },
            new Opt { K="NewDisplaySize", Tab="Desktop", T="text", L="Virtual display size", D="1920x1080/160", Dep="NewDisplay",
                Tip="width x height / dpi, e.g. 1920x1080/160. Blank = same as the phone." },
            new Opt { K="StartApp", Tab="Desktop", T="pick", Pick="apps", L="Start app on connect", D="", F="--start-app",
                Tip="Press Load to pick from the phone's apps, or type a package name (prefix + to force-restart it)." },
            new Opt { K="NoVdDestroyContent", Tab="Desktop", T="check", L="Move apps to the main screen when the window closes", D=true, F="--no-vd-destroy-content", Dep="NewDisplay" },
            new Opt { K="NoVdSystemDecorations", Tab="Desktop", T="check", L="Hide system decorations on the virtual display", D=false, F="--no-vd-system-decorations", Dep="NewDisplay" },

            // ---- Record
            new Opt { K="Record", Tab="Record", T="check", L="Record the session to a video file", D=false },
            new Opt { K="RecordFolder", Tab="Record", T="folder", L="Save recordings to", D="", Dep="Record" },
            new Opt { K="RecordFormat", Tab="Record", T="combo", L="File format", D="mp4", Dep="Record", O=new[]{"mp4","MP4","mkv","MKV"} },
            new Opt { K="NoPlayback", Tab="Record", T="check", L="Record only (no mirroring window)", D=false, F="--no-playback", Dep="Record",
                Tip="Stop it with the Stop button in CYSMS." },

            // ---- Advanced
            new Opt { K="ExtraArgs", Tab="Advanced", T="text", L="Extra scrcpy arguments", D="",
                Tip="Anything else from \"scrcpy --help\", e.g. --time-limit=60" },
        };

        public static Opt Get(string key) => All.FirstOrDefault(o => o.K == key);

        // Built-in starter profiles: only the differences from the schema defaults.
        public static readonly List<KeyValuePair<string, Dictionary<string, object>>> BuiltInProfiles = new List<KeyValuePair<string, Dictionary<string, object>>>
        {
            P("Default", new Dictionary<string, object>()),
            P("Auto quality", new Dictionary<string, object>{ {"AutoBitRate", true}, {"MaxSize", 0} }),
            P("Low bandwidth", new Dictionary<string, object>{ {"VideoCodec","h264"}, {"MaxSize",1024}, {"MaxFps",30}, {"VideoBitRate",4} }),
            P("High quality + audio", new Dictionary<string, object>{ {"MaxSize",0}, {"VideoBitRate",20}, {"Audio",true}, {"TurnScreenOff",false} }),
            P("Desktop mode", new Dictionary<string, object>{ {"NewDisplay",true}, {"MaxSize",0}, {"Audio",true}, {"Keyboard","uhid"}, {"TurnScreenOff",false} }),
            P("Camera", new Dictionary<string, object>{ {"VideoSource","camera"}, {"CameraSize","1920x1080"}, {"MaxSize",0}, {"ViewOnly",true} }),
            P("Record", new Dictionary<string, object>{ {"Record",true}, {"Audio",true} }),
        };
        static KeyValuePair<string, Dictionary<string, object>> P(string n, Dictionary<string, object> d) => new KeyValuePair<string, Dictionary<string, object>>(n, d);

        // Simple mode quality choices: name, description, the settings they set
        public static readonly List<(string Name, string Desc, Dictionary<string, object> Set)> Quality = new List<(string, string, Dictionary<string, object>)>
        {
            ("Auto", "Recommended. CYSMS picks size and bit rate from the connection (USB or Wi-Fi speed).",
                new Dictionary<string, object>{ {"AutoBitRate", true}, {"MaxFps", 60}, {"VideoCodec", "h265"}, {"VideoEncoder", ""} }),
            ("Smooth", "For slow or busy Wi-Fi: lower resolution, 30 fps, most compatible codec.",
                new Dictionary<string, object>{ {"AutoBitRate", false}, {"MaxSize", 1024}, {"MaxFps", 30}, {"VideoBitRate", 4}, {"VideoCodec", "h264"}, {"VideoEncoder", ""} }),
            ("Balanced", "Your original setup: 1050 px, 60 fps, 10 Mbps, H.265.",
                new Dictionary<string, object>{ {"AutoBitRate", false}, {"MaxSize", 1050}, {"MaxFps", 60}, {"VideoBitRate", 10}, {"VideoCodec", "h265"}, {"VideoEncoder", ""} }),
            ("Sharp", "Full resolution, 20 Mbps. Best over USB or fast Wi-Fi.",
                new Dictionary<string, object>{ {"AutoBitRate", false}, {"MaxSize", 0}, {"MaxFps", 60}, {"VideoBitRate", 20}, {"VideoCodec", "h265"}, {"VideoEncoder", ""} }),
        };

        // Which quality choice the settings match, or "Custom"
        public static string QualityOf(Dictionary<string, object> s)
        {
            foreach (var q in Quality)
            {
                bool all = true;
                foreach (var kv in q.Set)
                {
                    if (q.Name == "Auto" && kv.Key != "AutoBitRate") continue;   // Auto ignores the fixed values
                    if (Convert.ToString(s.TryGetValue(kv.Key, out var v) ? v : null, CultureInfo.InvariantCulture) != Convert.ToString(kv.Value, CultureInfo.InvariantCulture)) { all = false; break; }
                }
                if (all) return q.Name;
            }
            return "Custom";
        }

        public static Dictionary<string, object> Defaults()
        {
            var s = new Dictionary<string, object>();
            foreach (var o in All) s[o.K] = o.K == "RecordFolder" ? DefaultRecordDir : o.D;
            return s;
        }

        // Merge an overlay onto defaults, coercing types to what the schema expects.
        public static Dictionary<string, object> Merge(Dictionary<string, object> overlay)
        {
            var s = Defaults();
            if (overlay == null) return s;
            foreach (var kv in overlay)
            {
                var o = Get(kv.Key);
                if (o == null) continue;
                s[kv.Key] = Coerce(o, kv.Value);
            }
            return s;
        }

        public static object Coerce(Opt o, object v)
        {
            if (v == null) return o.D;
            try
            {
                switch (o.T)
                {
                    case "check": return v is bool b ? b : Convert.ToBoolean(v, CultureInfo.InvariantCulture);
                    case "num": return Convert.ToInt32(v, CultureInfo.InvariantCulture);
                    default: return Convert.ToString(v, CultureInfo.InvariantCulture);
                }
            }
            catch { return o.D; }
        }

        public static bool B(Dictionary<string, object> s, string k) => s.TryGetValue(k, out var v) && v is bool b && b;
        public static string Str(Dictionary<string, object> s, string k) => s.TryGetValue(k, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture).Trim() : "";

        public static bool TestDep(string dep, Dictionary<string, object> s)
        {
            if (string.IsNullOrEmpty(dep)) return true;
            foreach (var cond in dep.Split('&'))
            {
                if (cond.StartsWith("!")) { if (B(s, cond.Substring(1))) return false; }
                else if (cond.Contains("="))
                {
                    var kv = cond.Split(new[] { '=' }, 2);
                    if (Str(s, kv[0]) != kv[1]) return false;
                }
                else if (!B(s, cond)) return false;
            }
            return true;
        }

        public static string SafeName(string n)
        {
            n = Regex.Replace(n ?? "", "[\\\\/:*?\"<>|%]", "_").Trim();
            return n.Length == 0 ? "device" : n;
        }

        // Builds the scrcpy argument list. bitRateM: the bit rate (Mbps) chosen by Auto, or null.
        public static List<string> BuildArgs(Dictionary<string, object> s, string serial, string title, bool preview, int? autoBitRateM = null, DateTime? now = null, int? autoMaxSize = null)
        {
            var a = new List<string>();
            if (!string.IsNullOrEmpty(serial)) a.Add("--serial=" + serial);
            bool camera = Str(s, "VideoSource") == "camera";

            foreach (var o in All)
            {
                if (string.IsNullOrEmpty(o.F)) continue;
                if (!TestDep(o.Dep, s)) continue;
                if (o.T == "check")
                {
                    bool on = B(s, o.K);
                    if (o.Neg) { if (!on) a.Add(o.F); }
                    else if (on) a.Add(o.F);
                    continue;
                }
                string sv = Str(s, o.K);
                if (sv == "") continue;
                if (o.HasSkip && sv == o.Skip) continue;
                if (o.K == "CameraFacing" && Str(s, "CameraId") != "") continue; // camera id wins
                a.Add(o.F + "=" + sv + (o.Suffix ?? ""));
            }

            if (B(s, "AutoBitRate"))
            {
                // Insert where the fixed bit rate would have been (right after max fps)
                string val = preview && autoBitRateM == null ? "<auto>" : (autoBitRateM ?? 8) + "M";
                int idx = a.FindIndex(x => x.StartsWith("--video-codec=") || x.StartsWith("--video-encoder="));
                var item = "--video-bit-rate=" + val;
                if (idx < 0) idx = a.FindIndex(x => !x.StartsWith("--serial=") && !x.StartsWith("--video-source=") && !x.StartsWith("--max-size=") && !x.StartsWith("--max-fps="));
                if (idx < 0) a.Add(item); else a.Insert(idx, item);
            }

            // Auto quality also chooses the size (0 = native, so no --max-size)
            if (B(s, "AutoBitRate") && !camera)
            {
                a.RemoveAll(x => x.StartsWith("--max-size="));
                string ms = preview && autoMaxSize == null ? "<auto>" : (autoMaxSize ?? 0) > 0 ? autoMaxSize.Value.ToString(CultureInfo.InvariantCulture) : null;
                if (ms != null)
                {
                    int at = a.FindIndex(x => !x.StartsWith("--serial=") && !x.StartsWith("--video-source="));
                    if (at < 0) a.Add("--max-size=" + ms); else a.Insert(at, "--max-size=" + ms);
                }
            }

            // --camera-size and --max-size cannot be combined
            if (camera && Str(s, "CameraSize") != "") a.RemoveAll(x => x.StartsWith("--max-size="));
            // The camera cannot be controlled
            if (camera && !B(s, "ViewOnly")) a.Add("--no-control");

            if (B(s, "NewDisplay"))
            {
                var sz = Str(s, "NewDisplaySize");
                a.Add(sz != "" ? "--new-display=" + sz : "--new-display");
            }

            var t = Str(s, "WindowTitle");
            if (t == "") t = title ?? "";
            bool recordOnly = B(s, "Record") && B(s, "NoPlayback");
            if (t != "" && !recordOnly) a.Add("--window-title=" + t.Replace("%", "").Replace("\"", ""));

            if (B(s, "Record"))
            {
                var dir = Str(s, "RecordFolder");
                if (dir == "") dir = DefaultRecordDir;
                var ext = Str(s, "RecordFormat");
                if (ext != "mp4" && ext != "mkv") ext = "mp4";
                string stamp = preview ? "<date-time>" : (now ?? DateTime.Now).ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                a.Add("--record=" + Path.Combine(dir, SafeName(title) + "_" + stamp + "." + ext));
            }

            foreach (var x in SplitArgs(Str(s, "ExtraArgs"))) if (x != "") a.Add(x);
            return a;
        }

        public static List<string> SplitArgs(string text)
        {
            var res = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return res;
            foreach (Match m in Regex.Matches(text, "(?:[^\\s\"]+|\"[^\"]*\")+")) res.Add(m.Value.Replace("\"", ""));
            return res;
        }

        // Windows command-line quoting (CommandLineToArgvW rules)
        public static string Quote(string a)
        {
            if (a == null) return "\"\"";
            if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return a;
            var sb = new StringBuilder("\"");
            int bs = 0;
            foreach (char c in a)
            {
                if (c == '\\') { bs++; continue; }
                if (c == '"') { sb.Append('\\', bs * 2 + 1).Append('"'); bs = 0; continue; }
                sb.Append('\\', bs).Append(c); bs = 0;
            }
            sb.Append('\\', bs * 2).Append('"');
            return sb.ToString();
        }

        public static string JoinArgs(IEnumerable<string> args) => string.Join(" ", args.Select(Quote));
    }

    // =========================================================================
    // Parsers for adb / scrcpy output
    // =========================================================================
    public class AdbDev { public string Serial, State, Model; }
    public class PickItem
    {
        public string Value, Label, Extra;   // Extra: e.g. codec for an encoder
        public override string ToString() => Label;
    }

    public static class Parse
    {
        static readonly Regex DevLine = new Regex(@"^(?<serial>\S+)\s+(?<state>device|unauthorized|offline|authorizing|connecting|no permissions|recovery|sideload|bootloader)\b(?<rest>.*)$");

        public static List<AdbDev> AdbDevices(string text)
        {
            var res = new List<AdbDev>();
            foreach (var raw in Lines(text))
            {
                var m = DevLine.Match(raw);
                if (!m.Success) continue;
                string serial = m.Groups["serial"].Value, state = m.Groups["state"].Value, rest = m.Groups["rest"].Value;
                if (serial == "List" || serial == "*") continue;
                var mm = Regex.Match(rest, @"model:(\S+)");
                res.Add(new AdbDev { Serial = serial, State = state, Model = mm.Success ? mm.Groups[1].Value.Replace('_', ' ') : "" });
            }
            return res;
        }

        public static bool IsNetworkSerial(string serial) =>
            Regex.IsMatch(serial ?? "", @":\d+$") || (serial ?? "").Contains("_adb-tls-connect");

        public static string IpFromRoute(string text)
        {
            string fallback = null;
            foreach (var line in Lines(text))
            {
                var m = Regex.Match(line, @"src\s+(\d{1,3}(?:\.\d{1,3}){3})");
                if (m.Success)
                {
                    if (line.Contains("wlan")) return m.Groups[1].Value;
                    if (fallback == null) fallback = m.Groups[1].Value;
                    continue;
                }
                m = Regex.Match(line, @"inet\s+(\d{1,3}(?:\.\d{1,3}){3})/");
                if (m.Success && fallback == null) fallback = m.Groups[1].Value;
            }
            return fallback;
        }

        public static int? MdnsConnectPort(string text, string ip)
        {
            foreach (var line in Lines(text))
            {
                if (!line.Contains("_adb-tls-connect")) continue;
                var m = Regex.Match(line, Regex.Escape(ip) + @":(\d+)");
                if (m.Success) return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            }
            return null;
        }

        public static int? BatteryLevel(string dumpsys)
        {
            var m = Regex.Match(dumpsys ?? "", @"^\s*level:\s*(\d+)", RegexOptions.Multiline);
            return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : (int?)null;
        }

        public static bool BatteryCharging(string dumpsys)
        {
            // status: 2 = charging, 5 = full
            var m = Regex.Match(dumpsys ?? "", @"^\s*status:\s*(\d+)", RegexOptions.Multiline);
            return m.Success && (m.Groups[1].Value == "2" || m.Groups[1].Value == "5");
        }

        // Wi-Fi link speed (Mbps) from "dumpsys wifi" / "cmd wifi status" output
        public static int? WifiLinkSpeed(string text)
        {
            int best = 0;
            foreach (Match m in Regex.Matches(text ?? "", @"(?:Tx\s+)?Link speed:\s*(\d+)\s*Mbps", RegexOptions.IgnoreCase))
            {
                int v = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                if (v > best) best = v;
                break;  // the first reported value is the current connection
            }
            return best > 0 ? best : (int?)null;
        }

        // Phone's Wi-Fi network name from "cmd wifi status" / "dumpsys wifi"; null if unknown
        public static string PhoneSsid(string text)
        {
            text = text ?? "";
            var m = Regex.Match(text, @"Wifi is connected to ""(.*?)""");
            if (!m.Success) m = Regex.Match(text, @"SSID:\s*""(.*?)""\s*,");
            if (!m.Success) m = Regex.Match(text, @"(?<!B)SSID:\s*([^,""\r\n]+?)\s*,");
            if (!m.Success) return null;
            var v = m.Groups[1].Value.Trim();
            if (v == "" || v.Contains("<unknown ssid>") || v == "<none>") return null;
            return v;
        }

        // This PC's Wi-Fi network name from "netsh wlan show interfaces"; null when not on Wi-Fi
        public static string PcSsid(string text)
        {
            foreach (var line in Lines(text))
            {
                var m = Regex.Match(line, @"^\s*SSID\s*:\s*(.+?)\s*$");
                if (m.Success && m.Groups[1].Value != "") return m.Groups[1].Value;
            }
            return null;
        }

        // Auto quality: size limit for the connection (0 = native resolution)
        public static int AutoMaxSize(bool usb, int? linkMbps)
        {
            if (usb) return 0;
            if (linkMbps == null) return 1600;
            if (linkMbps >= 600) return 0;
            if (linkMbps >= 300) return 1920;
            if (linkMbps >= 150) return 1600;
            if (linkMbps >= 70) return 1280;
            return 1024;
        }

        // Auto bit rate (Mbps): USB is fast; Wi-Fi scales with the reported link speed
        public static int AutoBitRate(bool usb, int? linkMbps)
        {
            if (usb) return 16;
            if (linkMbps == null) return 8;
            if (linkMbps >= 600) return 16;
            if (linkMbps >= 300) return 12;
            if (linkMbps >= 150) return 8;
            if (linkMbps >= 70) return 6;
            return 4;
        }

        // scrcpy --list-encoders:   --video-codec=h264 --video-encoder=c2.qti.avc.encoder  (hw) [vendor]
        public static List<PickItem> Encoders(string text)
        {
            var res = new List<PickItem>();
            foreach (var line in Lines(text))
            {
                var m = Regex.Match(line, @"--video-codec=(\S+)\s+--video-encoder=(\S+)(.*)$");
                if (!m.Success) continue;
                string codec = m.Groups[1].Value, enc = m.Groups[2].Value, rest = m.Groups[3].Value.Trim();
                rest = Regex.Replace(rest, @"\s+", " ").Replace("(", "").Replace(")", "").Trim();
                res.Add(new PickItem { Value = enc, Extra = codec, Label = enc + " (" + codec + (rest != "" ? ", " + rest : "") + ")" });
            }
            return res;
        }

        // scrcpy --list-displays:   --display-id=0    (1080x2400)
        public static List<PickItem> Displays(string text)
        {
            var res = new List<PickItem>();
            foreach (var line in Lines(text))
            {
                var m = Regex.Match(line, @"--display-id=(\d+)\s*(\(([^)]*)\))?");
                if (!m.Success) continue;
                res.Add(new PickItem { Value = m.Groups[1].Value, Label = "Display " + m.Groups[1].Value + (m.Groups[3].Success ? " (" + m.Groups[3].Value + ")" : "") });
            }
            return res;
        }

        // scrcpy --list-cameras:    --camera-id=0    (back, 4000x3000, fps=[15, 30])
        public static List<PickItem> Cameras(string text)
        {
            var res = new List<PickItem>();
            foreach (var line in Lines(text))
            {
                var m = Regex.Match(line, @"--camera-id=(\S+)\s*(\(([^)]*(?:\)[^)]*)?)\))?");
                if (!m.Success) continue;
                string desc = m.Groups[3].Success ? m.Groups[3].Value : "";
                res.Add(new PickItem { Value = m.Groups[1].Value, Label = "Camera " + m.Groups[1].Value + (desc != "" ? " (" + desc + ")" : "") });
            }
            return res;
        }

        // scrcpy --list-apps:   * Chrome      com.android.chrome      (* = system, - = user)
        public static List<PickItem> Apps(string text)
        {
            var res = new List<PickItem>();
            foreach (var line in Lines(text))
            {
                var m = Regex.Match(line, @"^\s*([*-])\s+(.+?)\s+([A-Za-z][\w]*(?:\.[\w]+)+)\s*$");
                if (!m.Success) continue;
                res.Add(new PickItem { Value = m.Groups[3].Value, Label = m.Groups[2].Value.Trim() + " – " + m.Groups[3].Value, Extra = m.Groups[1].Value == "*" ? "system" : "user" });
            }
            return res.OrderBy(p => p.Extra == "system").ThenBy(p => p.Label, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string LogHint(string log)
        {
            log = log ?? "";
            var m = Regex.Match(log, @"([A-Za-z ]+?) (?:is|are) not supported before Android (\d+)");
            if (m.Success) return m.Groups[1].Value.Trim() + " needs Android " + m.Groups[2].Value + " or later. Turn that option off for this device.";
            if (Regex.IsMatch(log, "not a valid Win32 application|0x800700C1|This version of .* is not compatible", RegexOptions.IgnoreCase)) return "The included scrcpy can't run on this PC (it needs 64-bit Windows 10/11, or Windows 11 on ARM).";
            if (log.Contains("INJECT_EVENT")) return "Xiaomi/Redmi/POCO: turn on \"USB debugging (Security settings)\" in Developer options, or enable View only.";
            if (Regex.IsMatch(log, "encoder|codec", RegexOptions.IgnoreCase) && Regex.IsMatch(log, "ERROR")) return "This device may not support the chosen video codec or encoder. Try H.264 and a blank encoder in the Video tab, or lower Max size.";
            if (Regex.IsMatch(log, "camera", RegexOptions.IgnoreCase) && log.Contains("ERROR")) return "Camera mirroring needs Android 12+ and a supported camera size. Try leaving Camera resolution blank.";
            if (Regex.IsMatch(log, "audio", RegexOptions.IgnoreCase) && log.Contains("ERROR")) return "Audio forwarding needs Android 11+ (app playback / audio dup needs 13+). Try turning audio off.";
            if (Regex.IsMatch(log, "unauthorized", RegexOptions.IgnoreCase)) return "Allow the debugging prompt on the phone, then Connect again.";
            if (Regex.IsMatch(log, "not found|no devices|Could not find|device offline", RegexOptions.IgnoreCase)) return "The device disconnected. Press Refresh, then Connect again.";
            if (Regex.IsMatch(log, "Unrecognized|invalid option|unknown option|Unexpected argument", RegexOptions.IgnoreCase)) return "An option isn't supported. Check the Advanced tab's extra arguments.";
            return "See the full log for details.";
        }

        public static IEnumerable<string> Lines(string text) => (text ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
    }

    // =========================================================================
    // Compatibility: what the PC and each Android device can do (from scrcpy's documented limits)
    // =========================================================================
    public static class Compat
    {
        public const int MinSdk = 21;          // scrcpy: "The Android device requires at least API 21 (Android 5.0)"
        public const int MinWindowsBuild = 18362;   // Windows 10 1903: first release with .NET Framework 4.8 built in

        static readonly Dictionary<int, string> Names = new Dictionary<int, string>
        {
            {16,"4.1"},{17,"4.2"},{18,"4.3"},{19,"4.4"},{20,"4.4W"},{21,"5.0"},{22,"5.1"},{23,"6"},{24,"7.0"},{25,"7.1"},
            {26,"8.0"},{27,"8.1"},{28,"9"},{29,"10"},{30,"11"},{31,"12"},{32,"12L"},{33,"13"},{34,"14"},{35,"15"},{36,"16"},
        };

        public static string AndroidName(int sdk) => Names.TryGetValue(sdk, out var n) ? "Android " + n : "API level " + sdk;

        public static int? ParseSdk(string v) => int.TryParse((v ?? "").Trim(), out var n) && n > 0 ? n : (int?)null;

        public class Result
        {
            public string Block;                                 // can't mirror with these settings: explain why
            public readonly List<string> Notes = new List<string>();   // settings CYSMS changed for this session
            public Dictionary<string, object> Settings;
        }

        // Checks settings against the device's Android version (sdk = ro.build.version.sdk; null = unknown).
        // Hard limits block with an explanation; optional extras are switched off for this session with a note.
        public static Result ForDevice(int? sdk, Dictionary<string, object> s, bool usb)
        {
            var r = new Result { Settings = new Dictionary<string, object>(s) };
            var d = r.Settings;
            if (sdk.HasValue)
            {
                int v = sdk.Value;
                string has = "This device has " + AndroidName(v) + ".";
                if (v < MinSdk) { r.Block = "Mirroring needs Android 5.0 or later. " + has + " Older devices aren't supported by scrcpy, which CYSMS uses."; return r; }
                if (Schema.Str(d, "VideoSource") == "camera" && v < 31)
                { r.Block = "Camera mirroring needs Android 12 or later. " + has + " In Advanced mode, set Video > Source back to Device screen."; return r; }
                if (Schema.B(d, "NewDisplay") && v < 29)
                { r.Block = "A separate virtual display needs Android 10 or later. " + has + " In Advanced mode, turn off Desktop > Use a separate virtual display."; return r; }
                if (Schema.B(d, "Audio"))
                {
                    if (v < 30) { d["Audio"] = false; r.Notes.Add("no sound on the PC (needs Android 11 or later)"); }
                    else
                    {
                        if (v < 33 && (Schema.Str(d, "AudioSource") == "playback" || Schema.B(d, "AudioDup")))
                        { d["AudioSource"] = "output"; d["AudioDup"] = false; r.Notes.Add("normal device sound used (app playback needs Android 13)"); }
                        if (v == 30) r.Notes.Add("Android 11: keep the phone unlocked while it connects, or there will be no sound");
                    }
                }
            }
            if (!usb)
            {
                var aoa = new List<string>();
                foreach (var k in new[] { "Keyboard", "Mouse", "Gamepad" })
                    if (Schema.Str(d, k) == "aoa") { d[k] = k == "Gamepad" ? "disabled" : "sdk"; aoa.Add(k.ToLowerInvariant()); }
                if (aoa.Count > 0) r.Notes.Add("standard " + string.Join(" and ", aoa) + " used (AOA mode only works over USB)");
            }
            return r;
        }

        // Returns a warning when this PC is outside what CYSMS supports, or null when everything is fine.
        public static string WindowsNote(Version os, bool is64BitOs, string nativeArch)
        {
            if (!is64BitOs)
                return "This PC runs 32-bit Windows. The scrcpy included with CYSMS is 64-bit and can't run here.\n\n" +
                       "Download scrcpy-win32 from the scrcpy releases page, extract it, and choose \u201CLocate scrcpy.exe\u201D in the yellow bar.";
            if (nativeArch == "ARM64" && os.Build < 22000)
                return "This PC runs Windows 10 on ARM, which can't run the 64-bit scrcpy included with CYSMS.\n\n" +
                       "Windows 11 on ARM is supported. On Windows 10 on ARM you can try the 32-bit scrcpy (scrcpy-win32) with \u201CLocate scrcpy.exe\u201D.";
            if (os.Major < 10)
                return "CYSMS is made for Windows 10 (version 1903 or later) and Windows 11. This version of Windows isn't supported, so some things may not work.";
            if (os.Build < MinWindowsBuild)
                return "This Windows 10 version is older than 1903, which CYSMS doesn't support. Please run Windows Update; some things may not work until then.";
            return null;
        }

        // Can the bundled 64-bit (x64) scrcpy run on this PC?
        public static bool BundledRuns(Version os, bool is64BitOs, string nativeArch) =>
            is64BitOs && !(nativeArch == "ARM64" && os.Build < 22000);
    }

    // =========================================================================
    // Saved devices and per-user storage (%LOCALAPPDATA%\CYSMS)
    // =========================================================================
    public class DeviceRec
    {
        public string Id = Guid.NewGuid().ToString(), Name = "", Type = "wifi", IP = "", Serial = "", Profile = "Default", HwSerial = "", LastSsid = "";
        public int Port = 5555;
        public string AdbSerial => Type == "usb" ? Serial : IP + ":" + Port;

        public Dictionary<string, object> ToDict() => new Dictionary<string, object>
        {
            {"Id", Id}, {"Name", Name}, {"Type", Type}, {"IP", IP}, {"Port", Port}, {"Serial", Serial}, {"Profile", Profile}, {"HwSerial", HwSerial}, {"LastSsid", LastSsid}
        };

        public static DeviceRec FromDict(Dictionary<string, object> d)
        {
            string S(string k) => d.TryGetValue(k, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : "";
            var r = new DeviceRec();
            if (S("Id") != "") r.Id = S("Id");
            r.Name = S("Name"); r.Type = S("Type") == "usb" ? "usb" : "wifi"; r.IP = S("IP"); r.Serial = S("Serial");
            r.Profile = S("Profile") == "" ? "Default" : S("Profile"); r.HwSerial = S("HwSerial"); r.LastSsid = S("LastSsid");
            int.TryParse(S("Port"), NumberStyles.Integer, CultureInfo.InvariantCulture, out r.Port);
            if (r.Port <= 0) r.Port = 5555;
            return r;
        }
    }

    public class Store
    {
        public string Dir;
        public Dictionary<string, object> App;
        public List<KeyValuePair<string, Dictionary<string, object>>> Profiles = new List<KeyValuePair<string, Dictionary<string, object>>>();
        public List<DeviceRec> Devices = new List<DeviceRec>();

        public static readonly Dictionary<string, object> AppDefaults = new Dictionary<string, object>
        {
            {"ScrcpyPath", ""}, {"AutoConnectSingle", false}, {"CloseAfterConnect", false}, {"MinimizeToTray", true},
            {"RefreshSeconds", 6}, {"ScanPort", 5555}, {"HideScanNotice", false}, {"LastProfile", "Default"},
            {"AutoWireless", true}, {"PreferBundled", true}, {"SeededV5", false},
            {"Theme", "Forest"}, {"StickerFolder", ""}, {"ShowStickers", true}, {"Mode", "Simple"},
            {"ForestDefault", false}, {"SimpleSize", ""}, {"AdvancedSize", ""},
            {"Columns", "Status,Via,Wi-Fi network,Battery"}, {"TourSeen", false}, {"WindowsNoteSeen", ""},
        };

        public Store(string dir) { Dir = dir; }

        string PathOf(string name) => System.IO.Path.Combine(Dir, name);

        object ReadJson(string name)
        {
            var p = PathOf(name);
            if (!File.Exists(p)) return null;
            try { return Json.Parse(File.ReadAllText(p, Encoding.UTF8)); } catch { return null; }
        }

        void WriteJson(string name, object v)
        {
            Directory.CreateDirectory(Dir);
            var p = PathOf(name);
            var tmp = p + ".tmp";
            File.WriteAllText(tmp, Json.Write(v), new UTF8Encoding(false));
            if (File.Exists(p)) File.Replace(tmp, p, null); else File.Move(tmp, p);
        }

        public void Load(string legacyScrcpyAppDevices = null)
        {
            // App options
            App = new Dictionary<string, object>(AppDefaults);
            if (ReadJson("app.json") is Dictionary<string, object> a)
                foreach (var kv in a) if (App.ContainsKey(kv.Key) && kv.Value != null) App[kv.Key] = kv.Value;

            // 5.3: the Forest colours became the default look (Classic blue stays available in the App tab)
            if (!(App["ForestDefault"] is bool fd && fd)) { App["Theme"] = "Forest"; App["ForestDefault"] = true; SaveApp(); }

            // Profiles (keep file order)
            Profiles.Clear();
            if (ReadJson("profiles.json") is Dictionary<string, object> p)
                foreach (var kv in p) if (kv.Value is Dictionary<string, object> pd) Profiles.Add(new KeyValuePair<string, Dictionary<string, object>>(kv.Key, Schema.Merge(pd)));
            if (Profiles.Count == 0)
            {
                foreach (var kv in Schema.BuiltInProfiles) Profiles.Add(new KeyValuePair<string, Dictionary<string, object>>(kv.Key, Schema.Merge(kv.Value)));
                SaveProfiles();
                App["SeededV5"] = true; SaveApp();
            }
            else if (!(App.TryGetValue("SeededV5", out var sv) && sv is bool sb && sb))
            {
                // Profiles from the PowerShell version: add the profiles new in v5 once
                var auto = Schema.BuiltInProfiles.First(k => k.Key == "Auto quality");
                if (!HasProfile(auto.Key)) { Profiles.Insert(Math.Min(1, Profiles.Count), new KeyValuePair<string, Dictionary<string, object>>(auto.Key, Schema.Merge(auto.Value))); SaveProfiles(); }
                App["SeededV5"] = true; SaveApp();
            }

            // Devices (+ one-time import from the old ScrcpyApp store)
            Devices.Clear();
            bool exists = File.Exists(PathOf("devices.json"));
            if (ReadJson("devices.json") is List<object> dl)
                foreach (var o in dl) if (o is Dictionary<string, object> dd) Devices.Add(DeviceRec.FromDict(dd));
            if (!exists && legacyScrcpyAppDevices != null && File.Exists(legacyScrcpyAppDevices))
            {
                try
                {
                    var old = Json.Parse(File.ReadAllText(legacyScrcpyAppDevices));
                    IEnumerable<object> items = old is List<object> l ? l : new List<object> { old };
                    foreach (var o in items)
                        if (o is Dictionary<string, object> od && od.TryGetValue("IP", out var ip) && ip != null)
                        {
                            var r = DeviceRec.FromDict(od); r.Type = "wifi"; r.Profile = "Default";
                            Devices.Add(r);
                        }
                    if (Devices.Count > 0) SaveDevices();
                }
                catch { }
            }
        }

        public void SaveApp() => WriteJson("app.json", App);
        public void SaveDevices() => WriteJson("devices.json", Devices.Select(d => (object)d.ToDict()).ToList());
        public void SaveProfiles()
        {
            var d = new System.Collections.Specialized.OrderedDictionary();
            foreach (var kv in Profiles) d[kv.Key] = kv.Value;
            WriteJson("profiles.json", d);
        }

        public Dictionary<string, object> GetProfile(string name)
        {
            foreach (var kv in Profiles) if (kv.Key == name) return Schema.Merge(kv.Value);
            return Schema.Merge(Profiles.Count > 0 ? Profiles[0].Value : null);
        }

        public bool HasProfile(string name) => Profiles.Any(kv => kv.Key == name);

        public void SetProfile(string name, Dictionary<string, object> s)
        {
            int i = Profiles.FindIndex(kv => kv.Key == name);
            var v = new KeyValuePair<string, Dictionary<string, object>>(name, new Dictionary<string, object>(s));
            if (i >= 0) Profiles[i] = v; else Profiles.Add(v);
        }

        public void RemoveProfile(string name) => Profiles.RemoveAll(kv => kv.Key == name);

        public DeviceRec Find(string type, string adbSerial) => Devices.FirstOrDefault(d => d.Type == type && d.AdbSerial == adbSerial);

        public DeviceRec AddOrUpdate(DeviceRec rec)
        {
            var ex = Find(rec.Type, rec.AdbSerial);
            if (ex != null)
            {
                ex.Name = rec.Name;
                if (rec.HwSerial != "") ex.HwSerial = rec.HwSerial;
                SaveDevices();
                return ex;
            }
            Devices.Add(rec);
            SaveDevices();
            return rec;
        }

        public bool AppBool(string k) => App.TryGetValue(k, out var v) && v is bool b && b;
        public int AppInt(string k, int def)
        {
            if (App.TryGetValue(k, out var v) && v != null && int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out int i)) return i;
            return def;
        }
        public string AppStr(string k) => App.TryGetValue(k, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : "";
    }
}
