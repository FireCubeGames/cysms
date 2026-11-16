// CYSMS - theme, control factories and dialogs.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Cysms
{
    public static class Ui
    {
        public static float Scale = 1f;
        public static int S(double n) => (int)Math.Round(n * Scale);
        public static Color Col(string hex) => ColorTranslator.FromHtml(hex);

        // Colours are set by ApplyTheme() before any window is built.
        public static Color Bg, Card, Head, HeadSub, HeadButton, HeadButtonHover, Accent, AccentDark, Text, Muted, Border,
            Green, Amber, Red, Gray, Blue, Warn, WarnText, Disabled, Hover, PreviewBg;

        public static readonly string[] Themes = { "Classic", "Forest" };

        public static void ApplyTheme(string name)
        {
            // Shared status colours
            Green = Col("#16A34A"); Amber = Col("#D97706"); Red = Col("#DC2626"); Gray = Col("#A3ACB9");
            if (name == "Forest")
            {
                // Cream, warm brown and leaf green
                Bg = Col("#F6EFDF"); Card = Col("#FFFCF5"); Head = Col("#4A3423"); HeadSub = Col("#D8C6A6");
                HeadButton = Col("#5E4430"); HeadButtonHover = Col("#735538");
                Accent = Col("#4F8A3B"); AccentDark = Col("#3D6E2D"); Text = Col("#2F2418"); Muted = Col("#7B6A55");
                Border = Col("#E2D4B8"); Blue = Col("#2F6FB5"); Warn = Col("#F3E2B8"); WarnText = Col("#6B4A1E");
                Disabled = Col("#D6CCB8"); Hover = Col("#F3EAD7"); PreviewBg = Col("#FBF6EA");
            }
            else
            {
                Bg = Col("#F3F5F9"); Card = Col("#FFFFFF"); Head = Col("#0F172A"); HeadSub = Col("#94A3B8");
                HeadButton = Col("#1E293B"); HeadButtonHover = Col("#334155");
                Accent = Col("#2563EB"); AccentDark = Col("#1D4ED8"); Text = Col("#0F172A"); Muted = Col("#64748B");
                Border = Col("#D9DEE7"); Blue = Col("#2563EB"); Warn = Col("#FEF3C7"); WarnText = Col("#92400E");
                Disabled = Col("#CBD5E1"); Hover = Col("#EEF2F8"); PreviewBg = Col("#F8FAFC");
            }
        }

        public static Font FontUI, FontBold, FontH2, FontTitle, FontMono, FontItalic, FontSmall;
        public static ToolTip Tip;
        public static Icon AppIcon;

        public static void Init()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f;
            FontUI = new Font("Segoe UI", 9f);
            FontBold = new Font("Segoe UI Semibold", 9.5f);
            FontH2 = new Font("Segoe UI Semibold", 11f);
            FontTitle = new Font("Segoe UI Semibold", 16f);
            FontMono = new Font("Consolas", 9f);
            FontItalic = new Font(FontUI, FontStyle.Italic);
            FontSmall = new Font("Segoe UI", 8.25f);
            Tip = new ToolTip { AutoPopDelay = 12000, InitialDelay = 400 };
            try { AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { AppIcon = SystemIcons.Application; }
        }

        public static Button Btn(string text, bool primary = false, bool small = false)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat, UseMnemonic = false,
                Cursor = Cursors.Hand, UseVisualStyleBackColor = false,
                Margin = new Padding(0, S(2), S(6), S(2)),
                Padding = small ? new Padding(S(6), S(1), S(6), S(1)) : new Padding(S(10), S(4), S(10), S(4)),
            };
            if (primary)
            {
                b.BackColor = Accent; b.ForeColor = Color.White; b.Font = FontBold;
                b.FlatAppearance.BorderSize = 0; b.FlatAppearance.MouseOverBackColor = AccentDark;
                b.EnabledChanged += (s, e) =>
                {
                    b.BackColor = b.Enabled ? Accent : Disabled;
                    b.ForeColor = b.Enabled ? Color.White : Card;
                };
            }
            else
            {
                b.BackColor = Card; b.ForeColor = Text; b.Font = FontUI;
                b.FlatAppearance.BorderColor = Border; b.FlatAppearance.MouseOverBackColor = Hover;
            }
            return b;
        }

        public static Label Lbl(string text, Font font = null, Color? color = null)
        {
            return new Label
            {
                Text = text, AutoSize = true, Font = font ?? FontUI, ForeColor = color ?? Text, UseMnemonic = false,
                Margin = new Padding(0, S(6), S(6), S(4)),
            };
        }

        public static FlowLayoutPanel Flow(bool wrap = true)
        {
            return new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = wrap, Dock = DockStyle.Fill, Margin = new Padding(0) };
        }

        public static Bitmap Dot(Color c)
        {
            int n = S(16);
            var bmp = new Bitmap(n, n);
            using (var g = Graphics.FromImage(bmp))
            using (var br = new SolidBrush(c))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int d = (int)(n * 0.5), o = (n - d) / 2;
                g.FillEllipse(br, o, o, d, d);
            }
            return bmp;
        }

        // ------------------------------------------------------------- dialogs
        public static IWin32Window Owner;

        public static DialogResult Msg(string text, string title = "CYSMS", MessageBoxIcon icon = MessageBoxIcon.Information, MessageBoxButtons buttons = MessageBoxButtons.OK)
            => MessageBox.Show(Owner, text, title, buttons, icon);

        public static void Err(string text, string title = "CYSMS") => Msg(text, title, MessageBoxIcon.Warning);

        public static bool Ask(string text, string title = "CYSMS") => Msg(text, title, MessageBoxIcon.Question, MessageBoxButtons.YesNo) == DialogResult.Yes;

        public static Form DialogForm(string title, int minWidth)
        {
            return new Form
            {
                Text = title, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, BackColor = Card, Font = FontUI,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(S(16)), Icon = AppIcon,
                MinimumSize = new Size(S(minWidth), 0), AutoScaleMode = AutoScaleMode.None,
            };
        }

        public static FlowLayoutPanel ButtonBar(params Button[] buttons)
        {
            var bar = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, S(12), 0, 0) };
            foreach (var b in buttons) bar.Controls.Add(b);
            return bar;
        }

        public class Field { public string Key, Label, Default, Hint; }

        // Multi-field input dialog. Returns null if cancelled.
        // validate: returns an error message to keep the dialog open, or null when the input is fine.
        public static Dictionary<string, string> Input(string title, string intro, IEnumerable<Field> fields, string okText = "OK", Func<Dictionary<string, string>, string> validate = null)
        {
            using (var f = DialogForm(title, 440))
            {
                var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
                f.Controls.Add(t);
                if (!string.IsNullOrEmpty(intro))
                {
                    var l = Lbl(intro, null, Muted); l.MaximumSize = new Size(S(420), 0); t.Controls.Add(l);
                }
                var boxes = new Dictionary<string, TextBox>();
                foreach (var fd in fields)
                {
                    t.Controls.Add(Lbl(fd.Label, FontBold));
                    var tb = new TextBox { Width = S(420), Text = fd.Default ?? "" };
                    t.Controls.Add(tb);
                    if (!string.IsNullOrEmpty(fd.Hint))
                    {
                        var h = Lbl(fd.Hint, null, Muted); h.MaximumSize = new Size(S(420), 0); h.Margin = new Padding(0, 0, 0, S(4));
                        t.Controls.Add(h);
                    }
                    boxes[fd.Key] = tb;
                }
                var ok = Btn(okText, true);
                var cancel = Btn("Cancel"); cancel.DialogResult = DialogResult.Cancel;
                t.Controls.Add(ButtonBar(cancel, ok));
                f.AcceptButton = ok; f.CancelButton = cancel;
                ok.Click += (s, e) =>
                {
                    var vals = boxes.ToDictionary(kv => kv.Key, kv => kv.Value.Text.Trim());
                    var problem = validate?.Invoke(vals);
                    if (problem != null) { MessageBox.Show(f, problem, title, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    f.DialogResult = DialogResult.OK;
                };
                if (f.ShowDialog(Owner) != DialogResult.OK) return null;
                return boxes.ToDictionary(kv => kv.Key, kv => kv.Value.Text.Trim());
            }
        }

        public static void TextWindow(string title, string text, int w = 640, int h = 540)
        {
            using (var f = new Form { Text = title, StartPosition = FormStartPosition.CenterParent, Icon = AppIcon, Size = new Size(S(w), S(h)), BackColor = Card, AutoScaleMode = AutoScaleMode.None })
            {
                var tb = new TextBox
                {
                    Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
                    BackColor = Card, Font = FontUI, Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n"),
                };
                var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(14)) };
                p.Controls.Add(tb); f.Controls.Add(p);
                f.Shown += (s, e) => tb.Select(0, 0);
                f.ShowDialog(Owner);
            }
        }

        // Pick one item from a list (used for device pickers). Returns null if cancelled.
        public static PickItem PickFromList(string title, string intro, List<PickItem> items)
        {
            using (var f = DialogForm(title, 480))
            {
                var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
                f.Controls.Add(t);
                t.Controls.Add(Lbl(intro, FontBold));
                var filter = new TextBox { Width = S(460) };
                if (items.Count > 12) { t.Controls.Add(Lbl("Filter", null, Muted)); t.Controls.Add(filter); }
                var lb = new ListBox { Width = S(460), Height = S(260), IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
                void Fill()
                {
                    var q = filter.Text.Trim();
                    lb.BeginUpdate(); lb.Items.Clear();
                    foreach (var it in items) if (q == "" || it.Label.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) lb.Items.Add(it);
                    lb.EndUpdate();
                    if (lb.Items.Count > 0) lb.SelectedIndex = 0;
                }
                filter.TextChanged += (s, e) => Fill();
                Fill();
                t.Controls.Add(lb);
                var ok = Btn("Use selected", true); ok.DialogResult = DialogResult.OK;
                var cancel = Btn("Cancel"); cancel.DialogResult = DialogResult.Cancel;
                var clear = Btn("Clear (use default)"); clear.DialogResult = DialogResult.Ignore;
                t.Controls.Add(ButtonBar(cancel, ok, clear));
                lb.DoubleClick += (s, e) => { if (lb.SelectedItem != null) { f.DialogResult = DialogResult.OK; } };
                f.AcceptButton = ok; f.CancelButton = cancel;
                var res = f.ShowDialog(Owner);
                if (res == DialogResult.Ignore) return new PickItem { Value = "", Label = "" };
                if (res != DialogResult.OK) return null;
                return lb.SelectedItem as PickItem;
            }
        }

        public const string CompatText =
@"WINDOWS (this PC)

Supported
  - Windows 11 (64-bit), including Windows 11 on ARM.
  - Windows 10 version 1903 or later, 64-bit.

Not supported
  - Windows 10 on ARM: the included scrcpy is 64-bit (x64), which Windows 10
    on ARM can't run. You can try the 32-bit scrcpy (scrcpy-win32) with
    ""Locate scrcpy.exe"".
  - 32-bit Windows: same fix, use the 32-bit scrcpy.
  - Windows 10 before 1903, Windows 8.1 and Windows 7.

ANDROID (your phone or tablet)

  - Android 5.0 or later: mirroring and control, over USB or Wi-Fi.
  - Android 10 or later: separate virtual display (Desktop tab).
  - Android 11 or later: sound on the PC, and Pair with code (no cable).
    On Android 11 itself, keep the phone unlocked while it connects.
  - Android 12 or later: the phone camera as the source.
  - Android 13 or later: app playback audio, and keeping the sound on the
    phone at the same time.
  - USB only: AOA keyboard, mouse and gamepad modes.

What CYSMS does about it
  - Devices older than Android 5.0 show ""Not supported"" in the list.
  - Settings a device can't use are switched off for that session
    (for example sound on Android 10), and the status bar says so.
  - Settings that are the whole point of a profile (camera, virtual
    display) stop with an explanation instead of failing.

Brands
  Some manufacturers add extra switches (Samsung Auto Blocker, Xiaomi
  ""USB debugging (Security settings)""). See Help > Phone setup guide.

Limits come from scrcpy, which CYSMS uses:
https://github.com/Genymobile/scrcpy";

        public const string SetupGuide =
@"GETTING A PHONE READY  (one time per phone)

1. Turn on Developer options
   Settings > About phone > tap ""Build number"" 7 times.
   (Samsung: About phone > Software information.  Xiaomi: tap ""OS version"".)

2. Turn on USB debugging
   Settings > System > Developer options > USB debugging.

3. Brand-specific switches
   - Samsung (One UI 6+): turn OFF Auto Blocker
     (Settings > Security and privacy > Auto Blocker), or USB debugging is blocked.
   - Xiaomi / Redmi / POCO: also turn on ""USB debugging (Security settings)"",
     otherwise mouse and keyboard control will not work.
   - Honor / Huawei: if the phone does not show up over USB, turn on
     ""Allow ADB debugging in charge only mode"".

4. First USB connection
   Plug the phone in, unlock it and tap ""Allow"" on the USB debugging prompt
   (tick ""Always allow from this computer"").

GOING WIRELESS  (choose one)

A. ""Make wireless""  (any Android version)
   With the phone plugged in and on Wi-Fi, CYSMS switches it to wireless
   mode on port 5555 and saves it. Unplug the cable afterwards.
   Wireless mode resets when the phone REBOOTS. With ""Auto wireless"" on
   (App tab), plugging a saved phone in by USB re-enables it automatically.

B. ""Pair with code""  (Android 11+, no cable needed)
   Phone: Developer options > Wireless debugging > Pair device with pairing code.
   Enter the pairing IP:port and 6-digit code shown there.

NETWORK REQUIREMENTS
- PC and phone on the same network. Guest Wi-Fi / ""client isolation"" and
  some office networks block device-to-device traffic.
- Scanning only finds phones already in wireless mode (step A or B).

FEATURE REQUIREMENTS
- Audio forwarding: Android 11+.   App-playback / audio duplication: Android 13+.
- Camera mirroring: Android 12+.
- Virtual display (""Desktop"" tab): newer Android versions; behaviour varies by brand.";

        public static string ShortcutText(string mod)
        {
            string m;
            switch (mod)
            {
                case "lalt": m = "Left Alt"; break;
                case "lctrl": m = "Left Ctrl"; break;
                case "lsuper": m = "Left Win"; break;
                case "ralt": m = "Right Alt"; break;
                case "rctrl": m = "Right Ctrl"; break;
                default: m = "Left Alt or Left Win"; break;
            }
            return "scrcpy keyboard shortcuts (MOD = " + m + " for the current profile)" + @"

Window
  MOD + F              Toggle fullscreen
  MOD + G              Resize window to 1:1 (pixel-perfect)
  MOD + W              Resize window to remove black borders
  MOD + Left / Right   Rotate the window
  MOD + Z              Pause the display    MOD + Shift + Z  Resume
  MOD + Shift + R      Reset video capture / encoding
  MOD + I              Show / hide FPS counter (in the log)

Phone buttons
  MOD + H  or middle-click        HOME
  MOD + B  or right-click         BACK
  MOD + S                         Recent apps
  MOD + M                         MENU (unlock screen)
  MOD + Up / Down                 Volume up / down
  MOD + P                         POWER
  MOD + R                         Rotate the phone

Screen
  MOD + O              Turn the phone screen off (keep mirroring)
  MOD + Shift + O      Turn the phone screen back on
  MOD + N              Open notifications   (MOD + N + N: quick settings)
  MOD + Shift + N      Close notifications

Clipboard
  MOD + C / X / V      Copy / cut / paste via the phone clipboard
  MOD + Shift + V      Type the PC clipboard as keystrokes

Other
  MOD + K              Keyboard settings (UHID keyboard mode)
  Ctrl + drag          Pinch-to-zoom / rotate
  Drag an .apk file onto the window   Install it
  Drag any other file                 Copy it to /sdcard/Download

Full list: https://github.com/Genymobile/scrcpy/blob/master/doc/shortcuts.md";
        }
    }
}
