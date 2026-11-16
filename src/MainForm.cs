// CYSMS - main window.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using static Cysms.Ui;

namespace Cysms
{
    public class Row
    {
        public string Key, Name, Type, Serial, State, Model, Profile, StatusText = "";
        public bool Saved;
        public DeviceRec Dev;
        public string AdbState;   // raw adb state if adb knows the device
    }

    class DevInfo { public int? Battery, Sdk; public bool Charging; public string Android = "", HwSerial = "", Ssid; public DateTime When; }

    public class MainForm : Form
    {
        // From <Version> in CYSMS.csproj, so the number shown always matches the build
        public static readonly string Version = AppVersion();
        static string AppVersion()
        {
            var v = typeof(MainForm).Assembly.GetName().Version;
            return v == null ? "" : v.Build > 0 || v.Revision <= 0 ? v.ToString(3) : v.ToString(4);
        }
        readonly Store store;
        readonly string logDir, shotDir;

        List<Row> rows = new List<Row>();
        readonly List<Session> sessions = new List<Session>();
        readonly Dictionary<string, DevInfo> info = new Dictionary<string, DevInfo>();
        readonly HashSet<string> autoWirelessTried = new HashSet<string>();
        readonly Dictionary<string, Control> ctl = new Dictionary<string, Control>();
        readonly Dictionary<string, string[]> comboVals = new Dictionary<string, string[]>();

        Dictionary<string, object> cur;
        string activeProfile;
        bool dirty, loading, busy, refreshing, reallyExit, trayHinted, editingLabel;
        int tick;
        string lastSelKey;

        // controls
        Label toolLabel, profileTitle, emptyTitle;
        FlowLayoutPanel banner;
        ListView list;
        TableLayoutPanel empty;
        ComboBox profileCombo;
        TabControl tabs;
        TextBox preview;
        Button btnConnect, btnStop, btnSave, btnShot, btnWifi, btnRename, btnRemove;
        FlowLayoutPanel phoneBar;
        ToolStripStatusLabel statusText, sessionText;
        NotifyIcon tray;
        Timer timer;
        ContextMenuStrip rowMenu, addMenu, helpMenu;
        ToolStripItem miConnect, miStop, miSave, miWifi, miShot, miDisc, miRemove, miRename;
        Label appInfo, stickerInfo;
        SplitContainer split;
        PictureBox headSticker, emptySticker, toastPic;
        ToolStripStatusLabel pcNetText;
        string pcSsid, pcNetNote; DateTime pcSsidWhen = DateTime.MinValue;
        Button modeBtn;
        Button btnQuality, btnOptions, btnPhone;
        ContextMenuStrip qualityMenu, optionsMenu, phoneMenu, columnMenu;
        Label headSub, bannerText;
        static readonly (string, int, string)[] PhoneKeys =
        {
            ("Wake", 224, "Turn the screen on"), ("Lock", 223, "Turn the screen off"), ("Home", 3, "Home"), ("Back", 4, "Back"),
            ("Recents", 187, "Recent apps"), ("Vol \u2212", 25, "Volume down"), ("Vol +", 24, "Volume up"),
        };
        int[] colWidths;
        bool SimpleMode => store.AppStr("Mode") != "Advanced";
        Panel toast; Label toastText; Timer toastTimer;
        string mood = "", moodFile;

        public MainForm(Store st, string logDir)
        {
            store = st; this.logDir = logDir;
            shotDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "CYSMS");
            activeProfile = store.HasProfile(store.AppStr("LastProfile")) ? store.AppStr("LastProfile") : store.Profiles[0].Key;
            cur = store.GetProfile(activeProfile);

            Text = "CYSMS " + Version; Icon = AppIcon; StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = new Size(S(400), S(330)); Size = WantSize(SimpleMode);
            BackColor = Bg; Font = FontUI; KeyPreview = true;
            Ui.Owner = this;

            BuildHeader();
            BuildBanner();
            BuildBody();
            var status = new StatusStrip { BackColor = Card, SizingGrip = false };
            statusText = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft, Text = "Ready" };
            sessionText = new ToolStripStatusLabel { Text = "No active sessions", ForeColor = Muted };
            pcNetText = new ToolStripStatusLabel { Text = "", ForeColor = Muted, Margin = new Padding(0, 3, S(14), 2) };
            status.Items.Add(statusText); status.Items.Add(pcNetText); status.Items.Add(sessionText);

            Controls.Add(split); Controls.Add(banner); Controls.Add(headerPanel); Controls.Add(status);
            BuildTray();

            timer = new Timer { Interval = 1000 };
            timer.Tick += async (s, e) => await OnTick();

            KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.S) { SaveCurrentProfile(); e.Handled = true; e.SuppressKeyPress = true; } };
            Resize += (s, e) =>
            {
                UpdateResponsive();
                if (WindowState == FormWindowState.Minimized && store.AppBool("MinimizeToTray"))
                {
                    Hide();
                    if (!trayHinted) { trayHinted = true; tray.ShowBalloonTip(3000, "CYSMS is still running", "Double-click the tray icon to open it, or right-click to connect a device.", ToolTipIcon.Info); }
                }
            };
            FormClosing += (s, e) =>
            {
                if (dirty && !reallyExit && !ConfirmDiscard()) { e.Cancel = true; return; }
                timer.Stop();
                RememberSize(SimpleMode); store.SaveApp();
                tray.Visible = false;
            };
            Shown += async (s, e) => await OnShown();
        }

        public void SetStatus(string t) { statusText.Text = t; }

        // =================================================================== layout
        Panel headerPanel;

        void BuildHeader()
        {
            headerPanel = new Panel { Dock = DockStyle.Top, Height = S(46), BackColor = Head };
            var logo = new PictureBox { Size = new Size(S(28), S(28)), Location = new Point(S(12), S(9)), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            try { logo.Image = new Icon(AppIcon, 48, 48).ToBitmap(); } catch { }
            var t = Lbl("CYSMS", FontTitle, Color.White); t.Location = new Point(S(46), S(7)); t.Margin = Padding.Empty;
            var sub = Lbl("Android screen mirroring & control – powered by scrcpy", null, HeadSub); sub.Margin = Padding.Empty;
            sub.Location = new Point(t.Left + t.PreferredSize.Width + S(10), S(17)); headSub = sub;
            headerPanel.Controls.Add(logo); headerPanel.Controls.Add(t); headerPanel.Controls.Add(sub);

            var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Head, Padding = new Padding(0, S(1), S(10), 0) };
            toolLabel = Lbl("Checking scrcpy...", null, HeadSub); toolLabel.Margin = new Padding(S(8), S(14), S(12), 0);
            var help = Btn("Help");
            help.BackColor = HeadButton; help.ForeColor = Color.White;
            help.FlatAppearance.BorderColor = HeadButtonHover; help.FlatAppearance.MouseOverBackColor = HeadButtonHover;
            headSticker = new PictureBox { Size = new Size(S(42), S(42)), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, S(12), 0), Cursor = Cursors.Hand, Visible = false, BackColor = Color.Transparent };
            headSticker.Click += (s, e) => ShowToast(Stickers.PickAny(moodFile), "Hoot!");
            Tip.SetToolTip(headSticker, "Click me");
            right.Controls.Add(headSticker); right.Controls.Add(toolLabel); right.Controls.Add(help);
            help.Margin = new Padding(0, S(7), 0, 0);
            headerPanel.Controls.Add(right);

            helpMenu = new ContextMenuStrip();
            helpMenu.Items.Add("Quick tour", null, (s, e) => ShowTour());
            helpMenu.Items.Add("Phone setup guide", null, (s, e) => TextWindow("CYSMS – Phone setup guide", SetupGuide));
            helpMenu.Items.Add("What Simple mode decides for you", null, (s, e) => Msg(SimpleHelp, "Simple mode"));
            helpMenu.Items.Add("Compatibility", null, (s, e) => TextWindow("CYSMS \u2013 Compatibility", CompatText, 640, 600));
            helpMenu.Items.Add("Keyboard shortcuts", null, (s, e) => TextWindow("CYSMS – Shortcuts", ShortcutText(Schema.Str(cur, "ShortcutMod")), 600, 640));
            helpMenu.Items.Add("-");
            helpMenu.Items.Add("Open logs folder", null, (s, e) => OpenFolder(logDir));
            helpMenu.Items.Add("Open data folder", null, (s, e) => OpenFolder(store.Dir));
            helpMenu.Items.Add("scrcpy documentation (web)", null, (s, e) => OpenUrl("https://github.com/Genymobile/scrcpy#user-documentation"));
            helpMenu.Items.Add("Third-party licences", null, (s, e) =>
            {
                var p = Path.Combine(Tools.AppDir, "THIRD-PARTY-NOTICES.txt");
                if (File.Exists(p)) TextWindow("CYSMS – Third-party licences", File.ReadAllText(p), 720, 600);
                else Err("The licence file was not found next to CYSMS.");
            });
            helpMenu.Items.Add("-");
            helpMenu.Items.Add("About CYSMS", null, (s, e) => Msg(
                "CYSMS version " + Version + "\nAndroid screen mirroring launcher powered by scrcpy (Genymobile, Apache 2.0).\n\n" +
                "scrcpy: " + (Tools.Scrcpy ?? "not found") + (Tools.IsBundled ? " (bundled)" : "") + "\nadb: " + (Tools.Adb ?? "not found") + "\nData: " + store.Dir, "About CYSMS"));
            help.Click += (s, e) => helpMenu.Show(help, 0, help.Height);
            modeBtn = Btn("Advanced mode");
            modeBtn.BackColor = HeadButton; modeBtn.ForeColor = Color.White;
            modeBtn.FlatAppearance.BorderColor = HeadButtonHover; modeBtn.FlatAppearance.MouseOverBackColor = HeadButtonHover;
            modeBtn.Margin = new Padding(0, S(7), S(8), 0);
            Tip.SetToolTip(modeBtn, "Switch between Simple mode (a few choices, CYSMS decides the rest) and Advanced mode (every scrcpy option)");
            right.Controls.Add(modeBtn); right.Controls.SetChildIndex(modeBtn, right.Controls.GetChildIndex(help));
            modeBtn.Click += (s, e) =>
            {
                bool was = SimpleMode;
                if (!was) { if (dirty && !ConfirmDiscard()) return; store.App["Mode"] = "Simple"; }
                else store.App["Mode"] = "Advanced";
                ApplyMode(was); store.SaveApp();
            };
        }

        void BuildBanner()
        {
            banner = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = Warn, Padding = new Padding(S(14), S(6), S(14), S(6)), Visible = false };
            bannerText = Lbl("scrcpy was not found.", FontBold, WarnText);
            banner.Controls.Add(bannerText);
            var locate = Btn("Locate scrcpy.exe...", small: true);
            var dl = Btn("Download page", small: true);
            var re = Btn("Re-check", small: true);
            banner.Controls.AddRange(new Control[] { locate, dl, re });
            locate.Click += (s, e) => LocateScrcpy();
            dl.Click += (s, e) => OpenUrl("https://github.com/Genymobile/scrcpy/releases");
            re.Click += async (s, e) => { InitTools(); await RefreshDevices(); };
        }

        void BuildBody()
        {
            split = new SplitContainer { Dock = DockStyle.Fill, BackColor = Bg, SplitterWidth = S(4) };
            split.Panel1.Padding = new Padding(S(10), S(8), S(6), S(6));
            split.Panel2.Padding = new Padding(S(6), S(8), S(10), S(6));

            // ---------------- left: devices
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
            left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));   // fixed-width column so button rows wrap instead of widening it
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            split.Panel1.Controls.Add(left);
            left.Controls.Add(Lbl("Devices", FontH2), 0, 0);

            var devBar = Flow();
            var btnAdd = Btn("+ Add device ▾");
            var btnRefresh = Btn("Refresh");
            btnRename = Btn("Rename");
            btnRemove = Btn("Remove");
            devBar.Controls.AddRange(new Control[] { btnAdd, btnRefresh, btnRename, btnRemove });
            left.Controls.Add(devBar, 0, 1);

            var host = new Panel { Dock = DockStyle.Fill, BackColor = Border, Padding = new Padding(1), Margin = new Padding(0, S(6), 0, S(6)) };
            left.Controls.Add(host, 0, 2);
            list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
                LabelEdit = true, BorderStyle = BorderStyle.None, HeaderStyle = ColumnHeaderStyle.Nonclickable, Font = FontUI, BackColor = Card, ForeColor = Ui.Text,
            };
            // the image height sets the row height: taller rows are easier to read and hit
            var imgs = new ImageList { ImageSize = new Size(S(16), S(30)), ColorDepth = ColorDepth.Depth32Bit };
            Bitmap RowDot(Color c)
            {
                var b = new Bitmap(S(16), S(30));
                using (var g = Graphics.FromImage(b)) using (var d = Dot(c)) g.DrawImage(d, 0, (b.Height - d.Height) / 2);
                return b;
            }
            imgs.Images.Add("connected", RowDot(Green)); imgs.Images.Add("online", RowDot(Green)); imgs.Images.Add("mirroring", RowDot(Blue));
            imgs.Images.Add("unauthorized", RowDot(Amber)); imgs.Images.Add("offline", RowDot(Gray));
            list.Font = new Font("Segoe UI", 10f);
            list.SmallImageList = imgs;
            list.Columns.Add("Name", S(160)); list.Columns.Add("Status", S(104)); list.Columns.Add("Via", S(46)); list.Columns.Add("Wi-Fi network", S(150));
            list.Columns.Add("Battery", S(62)); list.Columns.Add("Android", S(58)); list.Columns.Add("Address", S(140)); list.Columns.Add("Profile", S(100));
            colWidths = list.Columns.Cast<ColumnHeader>().Select(c => c.Width).ToArray();
            columnMenu = new ContextMenuStrip { ShowCheckMargin = true, ShowImageMargin = false };
            columnMenu.Items.Add(new ToolStripMenuItem("Name") { Checked = true, Enabled = false });
            for (int i = 1; i < list.Columns.Count; i++)
            {
                var name = list.Columns[i].Text;
                var mi = new ToolStripMenuItem(name) { Tag = name };
                mi.Click += (s, e) =>
                {
                    var vis = VisibleColumns();
                    if (!vis.Remove(name)) vis.Add(name);
                    store.App["Columns"] = string.Join(",", list.Columns.Cast<ColumnHeader>().Select(c => c.Text).Where(vis.Contains));
                    store.SaveApp(); LayoutColumns();
                };
                columnMenu.Items.Add(mi);
            }
            columnMenu.Opening += (s, e) =>
            {
                var vis = VisibleColumns();
                foreach (ToolStripItem it in columnMenu.Items) if (it is ToolStripMenuItem mi && mi.Tag is string n) mi.Checked = vis.Contains(n);
            };
            // lay out now, and once more after the list has settled (scroll bars change its inner width)
            list.Resize += (s, e) => { LayoutColumns(); if (IsHandleCreated) BeginInvoke((Action)LayoutColumns); };
            host.Controls.Add(list);

            empty = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Card, ColumnCount = 1, Padding = new Padding(S(24)), Visible = false };
            empty.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            emptySticker = new PictureBox { Size = new Size(S(150), S(150)), SizeMode = PictureBoxSizeMode.Zoom, Visible = false, Margin = new Padding(0, 0, 0, S(8)) };
            empty.Controls.Add(emptySticker);
            emptyTitle = Lbl("No devices yet", FontH2);
            empty.Controls.Add(emptyTitle);
            empty.Controls.Add(Lbl("Plug a phone in with USB debugging on, or add one over Wi-Fi.\nNew to this? Open Help > Phone setup guide.", null, Muted));
            var eb = Flow();
            var eWifi = Btn("Make wireless (USB)"); var ePair = Btn("Pair with code"); var eScan = Btn("Scan network"); var eGuide = Btn("Setup guide");
            eb.Controls.AddRange(new Control[] { eWifi, ePair, eScan, eGuide });
            empty.Controls.Add(eb);
            host.Controls.Add(empty); empty.BringToFront();
            toast = new Panel { Size = new Size(S(176), S(196)), BackColor = Card, BorderStyle = BorderStyle.FixedSingle, Visible = false, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            toastPic = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Card };
            toastText = new Label { Dock = DockStyle.Bottom, Height = S(26), TextAlign = ContentAlignment.MiddleCenter, Font = FontBold, ForeColor = Ui.Text, UseMnemonic = false, AutoEllipsis = true };
            toast.Controls.Add(toastPic); toast.Controls.Add(toastText);
            toast.Click += (s, e) => HideToast(); toastPic.Click += (s, e) => HideToast();
            host.Controls.Add(toast);
            toastTimer = new Timer { Interval = 3500 };
            toastTimer.Tick += (s, e) => HideToast();

            var act = Flow();
            btnConnect = Btn("Connect", true);
            btnStop = Btn("Stop");
            btnSave = Btn("Save device");
            btnShot = Btn("Screenshot");
            btnWifi = Btn("Make wireless");
            act.Controls.AddRange(new Control[] { btnConnect, btnStop });
            BuildSimpleMenus(act);
            act.Controls.AddRange(new Control[] { btnSave, btnWifi });
            left.Controls.Add(act, 0, 3);
            Tip.SetToolTip(btnConnect, "Start mirroring the selected device (or double-click it / press Enter)");
            Tip.SetToolTip(btnSave, "Remember this device so it's listed even when not connected");
            Tip.SetToolTip(btnWifi, "Switch the selected USB device to wireless mode and save it");

            phoneBar = Flow();
            phoneBar.Controls.Add(Lbl("Phone:", null, Muted));
            var bUnlock = Btn("Unlock", small: true);
            Tip.SetToolTip(bUnlock, "Wake a locked device and open its PIN / password entry, so you can type it from this PC");
            bUnlock.Click += (s, e) => RunAction(UnlockSelected);
            phoneBar.Controls.Add(bUnlock);
            foreach (var kb in PhoneKeys)
            {
                var b = Btn(kb.Item1, small: true); int code = kb.Item2;
                Tip.SetToolTip(b, kb.Item3 + " (on the selected device)");
                b.Click += (s, e) => RunAction(() => SendKey(code));
                phoneBar.Controls.Add(b);
            }
            left.Controls.Add(phoneBar, 0, 4);

            addMenu = new ContextMenuStrip();
            addMenu.Items.Add("Make wireless from a plugged-in USB device", null, (s, e) => RunAction(() => UsbToWifi(null)));
            addMenu.Items.Add("Pair with code (Android 11+, no cable)", null, (s, e) => RunAction(Pair));
            addMenu.Items.Add("Scan network for wireless devices", null, (s, e) => RunAction(Scan));
            addMenu.Items.Add("Add by IP address", null, (s, e) => RunAction(AddByIp));
            btnAdd.Click += (s, e) => addMenu.Show(btnAdd, 0, btnAdd.Height);

            rowMenu = new ContextMenuStrip();
            miConnect = rowMenu.Items.Add("Connect", null, (s, e) => RunAction(ConnectSelected)); miConnect.Font = FontBold;
            miStop = rowMenu.Items.Add("Stop mirroring", null, (s, e) => StopSelected());
            rowMenu.Items.Add("-");
            miSave = rowMenu.Items.Add("Save device", null, (s, e) => RunAction(() => SaveSelected(null)));
            miRename = rowMenu.Items.Add("Rename", null, (s, e) => RenameSelected()); ((ToolStripMenuItem)miRename).ShortcutKeyDisplayString = "F2";
            miWifi = rowMenu.Items.Add("Make wireless (from USB)", null, (s, e) => RunAction(() => UsbToWifi(SelectedRow())));
            miShot = rowMenu.Items.Add("Take screenshot", null, (s, e) => RunAction(Screenshot));
            miDisc = rowMenu.Items.Add("Disconnect adb", null, (s, e) => RunAction(Disconnect));
            rowMenu.Items.Add("-");
            miRemove = rowMenu.Items.Add("Remove", null, (s, e) => RemoveSelected()); ((ToolStripMenuItem)miRemove).ShortcutKeyDisplayString = "Del";
            rowMenu.Items.Add("-");
            var miCols = new ToolStripMenuItem("Columns");
            miCols.DropDownOpening += (s, e) =>
            {
                // share the same items with the header menu
                var vis = VisibleColumns();
                miCols.DropDownItems.Clear();
                for (int i = 1; i < list.Columns.Count; i++)
                {
                    var src = (ToolStripMenuItem)columnMenu.Items[i];
                    miCols.DropDownItems.Add(new ToolStripMenuItem(src.Text, null, (a, b) => src.PerformClick()) { Checked = vis.Contains(src.Text) });
                }
            };
            miCols.DropDownItems.Add("...");
            rowMenu.Items.Add(miCols);
            rowMenu.Opening += (s, e) =>
            {
                UpdateButtons();
                // right-click on the column headers (or on empty space) chooses columns
                var pt = list.PointToClient(Cursor.Position);
                int headerH = list.Items.Count > 0 ? Math.Max(S(18), list.Items[0].Bounds.Top) : S(26);
                if (pt.Y >= 0 && pt.Y < headerH || SelectedRow() == null || list.GetItemAt(pt.X, pt.Y) == null && pt.Y >= 0)
                {
                    e.Cancel = true;
                    BeginInvoke((Action)(() => columnMenu.Show(Cursor.Position)));
                }
            };
            list.ContextMenuStrip = rowMenu;

            // events
            list.SelectedIndexChanged += (s, e) => OnSelectionChanged();
            list.DoubleClick += (s, e) => RunAction(ConnectSelected);
            list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Return) { e.Handled = true; e.SuppressKeyPress = true; RunAction(ConnectSelected); }
                else if (e.KeyCode == Keys.F2) RenameSelected();
                else if (e.KeyCode == Keys.Delete) RemoveSelected();
                else if (e.KeyCode == Keys.F5) RunAction(RefreshDevices);
            };
            list.BeforeLabelEdit += (s, e) => editingLabel = true;
            list.AfterLabelEdit += (s, e) =>
            {
                editingLabel = false;
                e.CancelEdit = true;   // list is redrawn by RefreshDevices
                var name = e.Label?.Trim();
                if (string.IsNullOrEmpty(name)) return;
                var key = list.Items[e.Item].Tag as string;
                var r = rows.FirstOrDefault(x => x.Key == key);
                if (r == null) return;
                BeginInvoke((Action)(() =>
                {
                    if (r.Saved) { r.Dev.Name = name; store.SaveDevices(); SetStatus("Renamed to " + name); RunAction(RefreshDevices); }
                    else RunAction(() => SaveSelected(name));
                }));
            };
            btnConnect.Click += (s, e) => RunAction(ConnectSelected);
            btnStop.Click += (s, e) => StopSelected();
            btnSave.Click += (s, e) => RunAction(() => SaveSelected(null));
            btnShot.Click += (s, e) => RunAction(Screenshot);
            btnWifi.Click += (s, e) => RunAction(() => UsbToWifi(SelectedRow()));
            btnRefresh.Click += (s, e) => RunAction(async () => { SetStatus("Refreshing..."); await RefreshDevices(); SetStatus("Device list refreshed"); });
            btnRename.Click += (s, e) => RenameSelected();
            btnRemove.Click += (s, e) => RemoveSelected();
            eWifi.Click += (s, e) => RunAction(() => UsbToWifi(null));
            ePair.Click += (s, e) => RunAction(Pair);
            eScan.Click += (s, e) => RunAction(Scan);
            eGuide.Click += (s, e) => TextWindow("CYSMS – Phone setup guide", SetupGuide);

            // ---------------- right: settings
            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            split.Panel2.Controls.Add(right);
            profileTitle = Lbl("Settings", FontH2);
            right.Controls.Add(profileTitle, 0, 0);

            var profBar = Flow(false);
            profBar.Controls.Add(Lbl("Profile:", null, Muted));
            profileCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(160), Margin = new Padding(0, S(3), S(8), 0) };
            var pSave = Btn("Save", small: true); var pSaveAs = Btn("Save as...", small: true); var pDel = Btn("Delete", small: true);
            profBar.Controls.AddRange(new Control[] { profileCombo, pSave, pSaveAs, pDel });
            right.Controls.Add(profBar, 0, 1);
            Tip.SetToolTip(profileCombo, "Each device remembers which profile it uses");

            tabs = new TabControl { Dock = DockStyle.Fill, Margin = new Padding(0, S(6), 0, S(6)), Padding = new Point(S(10), S(5)) };
            var settingsHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, S(6), 0, S(6)) };
            tabs.Margin = Padding.Empty;
            settingsHost.Controls.Add(tabs);
            right.Controls.Add(settingsHost, 0, 2);
            foreach (var tn in Schema.TabOrder)
            {
                var page = new TabPage(tn);
                var tbl = NewTabTable(page);
                foreach (var o in Schema.All.Where(x => x.Tab == tn)) AddSettingRow(tbl, o);
                tabs.TabPages.Add(page);
            }
            BuildAppTab();

            var previewLabelRef = Lbl("Command preview", FontBold, Muted);
            right.Controls.Add(previewLabelRef, 0, 3);
            var prow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
            prow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            prow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            preview = new TextBox { Multiline = true, ReadOnly = true, Height = S(58), Dock = DockStyle.Fill, Font = FontMono, BackColor = PreviewBg, ScrollBars = ScrollBars.Vertical };
            var copy = Btn("Copy", small: true); copy.Anchor = AnchorStyles.Top;
            prow.Controls.Add(preview, 0, 0); prow.Controls.Add(copy, 1, 0);
            right.Controls.Add(prow, 0, 4);
            copy.Click += (s, e) => { if (preview.Text != "") { Clipboard.SetText(preview.Text); SetStatus("Command copied to clipboard"); } };

            profileCombo.SelectedIndexChanged += (s, e) => OnProfileComboChanged();
            pSave.Click += (s, e) => SaveCurrentProfile();
            pSaveAs.Click += (s, e) => SaveProfileAs();
            pDel.Click += (s, e) => DeleteProfile();
        }

        TableLayoutPanel NewTabTable(TabPage page)
        {
            page.BackColor = Card; page.AutoScroll = true; page.Padding = new Padding(S(12), S(8), S(12), S(8));
            var tbl = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 0 };
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            page.Controls.Add(tbl);
            return tbl;
        }

        int NewRow(TableLayoutPanel tbl)
        {
            int r = tbl.RowCount; tbl.RowCount = r + 1;
            tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return r;
        }

        void AddSettingRow(TableLayoutPanel tbl, Opt o)
        {
            int row = NewRow(tbl);
            string tip = o.Tip ?? "";
            if (!string.IsNullOrEmpty(o.F)) tip += (tip != "" ? "\n" : "") + "scrcpy option: " + o.F;

            if (o.T == "check")
            {
                var cb = new CheckBox { Text = o.L, AutoSize = true, Name = o.K, Margin = new Padding(0, S(5), 0, S(3)) };
                cb.CheckedChanged += (s, e) => OnSettingChanged();
                tbl.Controls.Add(cb, 0, row); tbl.SetColumnSpan(cb, 2);
                ctl[o.K] = cb;
                if (tip != "") Tip.SetToolTip(cb, tip);
                return;
            }
            var lbl = Lbl(o.L); lbl.Margin = new Padding(0, S(8), S(10), S(3));
            tbl.Controls.Add(lbl, 0, row);
            Control c;
            if (o.T == "num")
            {
                var n = new NumericUpDown { Maximum = o.Max, Minimum = o.Min, Width = S(110) };
                n.ValueChanged += (s, e) => OnSettingChanged();
                c = n;
            }
            else if (o.T == "combo")
            {
                var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(260) };
                var vals = new List<string>();
                for (int i = 0; i < o.O.Length; i += 2) { vals.Add(o.O[i]); cb.Items.Add(o.O[i + 1]); }
                comboVals[o.K] = vals.ToArray();
                cb.SelectedIndexChanged += (s, e) => OnSettingChanged();
                c = cb;
            }
            else if (o.T == "pick" || o.T == "folder")
            {
                var tb = new TextBox { Width = S(200), Margin = new Padding(0, S(2), S(4), 0), Name = o.K };
                tb.TextChanged += (s, e) => OnSettingChanged();
                var fp = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, S(3), 0, 0) };
                var b = Btn(o.T == "pick" ? "Load..." : "Browse...", small: true);
                fp.Controls.Add(tb); fp.Controls.Add(b);
                if (o.T == "folder")
                    b.Click += (s, e) =>
                    {
                        using (var fd = new FolderBrowserDialog())
                        {
                            if (Directory.Exists(tb.Text)) fd.SelectedPath = tb.Text;
                            if (fd.ShowDialog(this) == DialogResult.OK) tb.Text = fd.SelectedPath;
                        }
                    };
                else
                {
                    b.Click += (s, e) => RunAction(() => LoadPick(o, tb));
                    Tip.SetToolTip(b, "Load the list from the selected phone (it must be connected)");
                }
                tbl.Controls.Add(fp, 1, row);
                ctl[o.K] = tb;
                if (tip != "") { Tip.SetToolTip(tb, tip); Tip.SetToolTip(lbl, tip); }
                return;
            }
            else
            {
                var tb = new TextBox { Width = S(260) };
                tb.TextChanged += (s, e) => OnSettingChanged();
                c = tb;
            }
            c.Name = o.K; c.Margin = new Padding(0, S(5), 0, S(3));
            tbl.Controls.Add(c, 1, row);
            ctl[o.K] = c;
            if (tip != "") { Tip.SetToolTip(c, tip); Tip.SetToolTip(lbl, tip); }
        }

        void BuildAppTab()
        {
            var page = new TabPage("App");
            var tbl = NewTabTable(page);
            tabs.TabPages.Add(page);
            void Check(string key, string text, string tip = null)
            {
                int r = NewRow(tbl);
                var cb = new CheckBox { Text = text, AutoSize = true, Checked = store.AppBool(key), Margin = new Padding(0, S(5), 0, S(3)) };
                cb.CheckedChanged += (s, e) => { store.App[key] = cb.Checked; store.SaveApp(); };
                tbl.Controls.Add(cb, 0, r); tbl.SetColumnSpan(cb, 2);
                if (tip != null) Tip.SetToolTip(cb, tip);
            }
            void Num(string key, string text, int min, int max, int def)
            {
                int r = NewRow(tbl);
                var l = Lbl(text); l.Margin = new Padding(0, S(8), S(10), S(3));
                var n = new NumericUpDown { Maximum = max, Minimum = min, Width = S(90), Margin = new Padding(0, S(5), 0, S(3)) };
                n.Value = Math.Min(max, Math.Max(min, store.AppInt(key, def)));
                n.ValueChanged += (s, e) => { store.App[key] = (int)n.Value; store.SaveApp(); };
                tbl.Controls.Add(l, 0, r); tbl.Controls.Add(n, 1, r);
            }
            Check("AutoWireless", "Auto wireless: when a saved phone is plugged in by USB, switch it back to Wi-Fi mode",
                  "Fixes saved phones showing Offline after they restart. Needs the phone on the same Wi-Fi.");
            Check("AutoConnectSingle", "On start, connect automatically if exactly one device is ready");
            Check("CloseAfterConnect", "Close CYSMS once mirroring has started");
            Check("MinimizeToTray", "Minimise to the system tray");
            Check("HideScanNotice", "Skip the explanation before network scans");
            Num("RefreshSeconds", "Device status refresh (seconds)", 3, 120, 6);
            Num("ScanPort", "Wireless adb port", 1024, 65535, 5555);
            // ---- Look and stickers
            int tr = NewRow(tbl);
            var themeLbl = Lbl("Theme"); themeLbl.Margin = new Padding(0, S(14), S(10), S(3));
            var themeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, S(10), 0, 0) };
            var themeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(140), Margin = new Padding(0, S(3), S(8), 0) };
            foreach (var t in Themes) themeCombo.Items.Add(t);
            themeCombo.SelectedItem = Themes.Contains(store.AppStr("Theme")) ? store.AppStr("Theme") : "Classic";
            var themeNote = Lbl("", null, Muted);
            themeRow.Controls.Add(themeCombo); themeRow.Controls.Add(themeNote);
            tbl.Controls.Add(themeLbl, 0, tr); tbl.Controls.Add(themeRow, 1, tr);
            Tip.SetToolTip(themeCombo, "Classic: blue. Forest: cream, warm brown and leaf green.");
            themeCombo.SelectedIndexChanged += (s, e) =>
            {
                var t = (string)themeCombo.SelectedItem;
                if (t == store.AppStr("Theme")) return;
                store.App["Theme"] = t; store.SaveApp();
                if (Ask("Restart CYSMS now to apply the " + t + " theme?")) RestartApp();
                else themeNote.Text = "Applies next time CYSMS starts.";
            };

            int sr = NewRow(tbl);
            var showSt = new CheckBox { Text = "Show stickers from my own folder (mascot in the header and pop-ups)", AutoSize = true, Checked = store.AppBool("ShowStickers"), Margin = new Padding(0, S(10), 0, S(3)) };
            tbl.Controls.Add(showSt, 0, sr); tbl.SetColumnSpan(showSt, 2);
            int fr = NewRow(tbl);
            var folderLbl = Lbl("Sticker folder"); folderLbl.Margin = new Padding(0, S(8), S(10), S(3));
            var folderRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, S(3), 0, 0) };
            var folderBox = new TextBox { Width = S(230), ReadOnly = true, Text = store.AppStr("StickerFolder"), Margin = new Padding(0, S(2), S(4), 0) };
            var pickF = Btn("Browse...", small: true); var clearF = Btn("Clear", small: true); var testF = Btn("Try one", small: true);
            folderRow.Controls.AddRange(new Control[] { folderBox, pickF, clearF, testF });
            tbl.Controls.Add(folderLbl, 0, fr); tbl.Controls.Add(folderRow, 1, fr);
            int ir = NewRow(tbl);
            stickerInfo = Lbl("", null, Muted); stickerInfo.MaximumSize = new Size(S(480), 0); stickerInfo.Margin = new Padding(0, S(2), 0, S(3));
            tbl.Controls.Add(stickerInfo, 0, ir); tbl.SetColumnSpan(stickerInfo, 2);
            Tip.SetToolTip(stickerInfo, "Moments are matched by words in the file names, e.g. 'sparkle' when mirroring starts, 'shocked' or 'sad' when something fails, 'snore' when idle, 'rotating' while busy.");
            void UpdateStickerInfo()
            {
                stickerInfo.Text = Stickers.Describe() + " Pictures are matched to moments by words in their file names: sparkle/happy (mirroring starts), shocked/sad (something fails), snore/tired (idle), rotating (busy), headpat/blush (ready).";
            }
            UpdateStickerInfo();
            showSt.CheckedChanged += (s, e) => { store.App["ShowStickers"] = showSt.Checked; store.SaveApp(); mood = ""; UpdateMood(); };
            pickF.Click += (s, e) =>
            {
                using (var fd = new FolderBrowserDialog { Description = "Choose a folder of sticker images (PNG, GIF, JPG or WebP)" })
                {
                    if (Directory.Exists(folderBox.Text)) fd.SelectedPath = folderBox.Text;
                    if (fd.ShowDialog(this) != DialogResult.OK) return;
                    folderBox.Text = fd.SelectedPath; store.App["StickerFolder"] = fd.SelectedPath; store.SaveApp();
                    Stickers.Load(fd.SelectedPath); UpdateStickerInfo(); mood = ""; UpdateMood();
                    if (Stickers.Count > 0) ShowToast(Stickers.Pick("start", true), "Stickers loaded!");
                }
            };
            clearF.Click += (s, e) => { folderBox.Text = ""; store.App["StickerFolder"] = ""; store.SaveApp(); Stickers.Load(""); UpdateStickerInfo(); mood = ""; UpdateMood(); };
            testF.Click += (s, e) => { if (Stickers.Count == 0) Err("Choose a sticker folder first."); else ShowToast(Stickers.PickAny(), "Hoot!"); };

            int rr = NewRow(tbl);
            appInfo = Lbl("", null, Muted); appInfo.MaximumSize = new Size(S(480), 0); appInfo.Margin = new Padding(0, S(14), 0, 0);
            tbl.Controls.Add(appInfo, 0, rr); tbl.SetColumnSpan(appInfo, 2);
            rr = NewRow(tbl);
            var bar = Flow();
            var b1 = Btn("Use a different scrcpy...", small: true); var b2 = Btn("Use bundled scrcpy", small: true);
            var b3 = Btn("Open data folder", small: true); var b4 = Btn("Open logs", small: true);
            bar.Controls.AddRange(new Control[] { b1, b2, b3, b4 });
            tbl.Controls.Add(bar, 0, rr); tbl.SetColumnSpan(bar, 2);
            b1.Click += (s, e) => LocateScrcpy();
            b2.Click += async (s, e) => { store.App["ScrcpyPath"] = ""; store.App["PreferBundled"] = true; store.SaveApp(); InitTools(); await RefreshDevices(); };
            b3.Click += (s, e) => OpenFolder(store.Dir);
            b4.Click += (s, e) => OpenFolder(logDir);
        }

        void BuildTray()
        {
            tray = new NotifyIcon { Icon = AppIcon, Text = "CYSMS " + Version, Visible = true, ContextMenuStrip = new ContextMenuStrip() };
            var m = tray.ContextMenuStrip;
            m.Opening += (s, e) =>
            {
                m.Items.Clear();
                var open = m.Items.Add("Open CYSMS", null, (a, b) => ShowMain()); open.Font = FontBold;
                m.Items.Add("-");
                foreach (var r in rows)
                {
                    var key = r.Key;
                    var it = m.Items.Add(r.Name + " (" + r.StatusText + ")", null, (a, b) =>
                    {
                        var rw = rows.FirstOrDefault(x => x.Key == key);
                        if (rw != null) RunAction(() => ConnectRow(rw, false));
                    });
                    it.Enabled = r.State != "offline" && r.State != "mirroring";
                }
                if (rows.Count == 0) m.Items.Add("(no devices)").Enabled = false;
                m.Items.Add("-");
                m.Items.Add("Exit", null, (a, b) => { reallyExit = true; Close(); });
                e.Cancel = false;
            };
            tray.DoubleClick += (s, e) => ShowMain();
        }

        // =================================================================== simple mode
        const string SimpleHelp =
            "In Simple mode CYSMS makes these decisions for you:\n\n" +
            "\u2022 Quality 'Auto' picks the picture size and bit rate from the connection (USB, or the Wi-Fi link speed).\n" +
            "\u2022 Saved phones are switched back to Wi-Fi when you plug them in.\n" +
            "\u2022 The phone is kept awake while mirroring.\n" +
            "\u2022 If a phone rejects H.265, CYSMS retries once with H.264 and remembers it.\n\n" +
            "Quality and Options are saved straight away to the selected device's profile.\n" +
            "Advanced mode (top right) shows every scrcpy option.";

        static readonly string[][] SimpleOpts =
        {
            new[] { "Audio", "Play the phone's sound on this PC (Android 11+)" },
            new[] { "TurnScreenOff", "Turn the phone screen off while mirroring" },
            new[] { "AlwaysOnTop", "Keep the mirror window on top" },
            new[] { "ViewOnly", "View only (no mouse or keyboard control)" },
        };

        void BuildSimpleMenus(FlowLayoutPanel act)
        {
            btnQuality = Btn("Quality: Auto \u25BE"); btnOptions = Btn("Options \u25BE"); btnPhone = Btn("Phone \u25BE");
            act.Controls.AddRange(new Control[] { btnQuality, btnOptions, btnPhone });

            qualityMenu = new ContextMenuStrip { ShowCheckMargin = true, ShowImageMargin = false };
            foreach (var q in Schema.Quality)
            {
                var name = q.Name;
                var mi = new ToolStripMenuItem(name + "  \u2013  " + q.Desc) { Tag = name };
                mi.Click += (s, e) => ApplySimple(d => { foreach (var kv in Schema.Quality.First(x => x.Name == name).Set) d[kv.Key] = kv.Value; });
                qualityMenu.Items.Add(mi);
            }
            optionsMenu = new ContextMenuStrip { ShowCheckMargin = true, ShowImageMargin = false };
            foreach (var o in SimpleOpts)
            {
                var key = o[0];
                var mi = new ToolStripMenuItem(o[1]) { Tag = key };
                mi.Click += (s, e) => ApplySimple(d => d[key] = !Schema.B(d, key));
                optionsMenu.Items.Add(mi);
            }
            optionsMenu.Items.Add("-");
            optionsMenu.Items.Add("What Simple mode decides for you...", null, (s, e) => Msg(SimpleHelp, "Simple mode"));

            phoneMenu = new ContextMenuStrip { ShowImageMargin = false };
            foreach (var kb in PhoneKeys)
            {
                int code = kb.Item2;
                phoneMenu.Items.Add(kb.Item3, null, (s, e) => RunAction(() => SendKey(code)));
            }
            phoneMenu.Items.Insert(0, new ToolStripMenuItem("Wake and open PIN entry", null, (s, e) => RunAction(UnlockSelected)) { ToolTipText = "Wakes a locked device and opens its PIN / password entry, so you can type it from this PC" });
            phoneMenu.Items.Insert(1, new ToolStripSeparator());
            phoneMenu.Items.Add("-");
            phoneMenu.Items.Add("Take screenshot", null, (s, e) => RunAction(Screenshot));
            btnQuality.Click += (s, e) => { SyncSimple(); qualityMenu.Show(btnQuality, 0, btnQuality.Height); };
            btnOptions.Click += (s, e) => { SyncSimple(); optionsMenu.Show(btnOptions, 0, btnOptions.Height); };
            btnPhone.Click += (s, e) => phoneMenu.Show(btnPhone, 0, btnPhone.Height);
            Tip.SetToolTip(btnOptions, "Sound, screen off, always on top, view only");
            Tip.SetToolTip(btnPhone, "Press a button on the selected device: wake, lock, home, back, recents, volume");
        }

        void SyncSimple()
        {
            if (btnQuality == null) return;
            var q = Schema.QualityOf(cur);
            btnQuality.Text = "Quality: " + q + " \u25BE";
            foreach (ToolStripItem it in qualityMenu.Items) if (it is ToolStripMenuItem mi) mi.Checked = (string)mi.Tag == q;
            int on = 0;
            foreach (ToolStripItem it in optionsMenu.Items)
                if (it is ToolStripMenuItem mi && mi.Tag is string key) { mi.Checked = Schema.B(cur, key); if (mi.Checked) on++; }
            btnOptions.Text = "Options" + (on > 0 ? " (" + on + ")" : "") + " \u25BE";
            var r = SelectedRow();
            int users = store.Devices.Count(d => d.Profile == activeProfile);
            Tip.SetToolTip(btnQuality, (q == "Custom" ? "Custom settings from Advanced mode are in use. Pick a quality to replace them.\n" : "") +
                (r != null ? "Settings for " + r.Name + ". " : "") + "Saved straight away to the profile '" + activeProfile + "'" +
                (users > 1 ? ", which " + users + " devices use." : "."));
        }

        void ApplySimple(Action<Dictionary<string, object>> change)
        {
            if (loading) return;
            var s = new Dictionary<string, object>(cur);
            change(s);
            cur = s;
            WriteSettingsToControls(cur);   // keeps Advanced mode in step (also calls SyncSimple)
            store.SetProfile(activeProfile, cur); store.SaveProfiles();
            dirty = false; UpdateTitle(); UpdatePreview();
            SetStatus("Saved to profile '" + activeProfile + "'");
        }

        HashSet<string> VisibleColumns() =>
            new HashSet<string>(store.AppStr("Columns").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));

        bool layingColumns;
        // Drop order when the list gets narrow: least important first. Name and Status always stay.
        static readonly string[] DropOrder = { "Profile", "Address", "Android", "Via", "Battery", "Wi-Fi network" };

        // Chosen columns keep their width and Name stretches to fill the rest. When the window is too
        // narrow, the least important columns step aside until it fits (they return when there is room).
        void LayoutColumns()
        {
            if (layingColumns || list == null || colWidths == null) return;
            layingColumns = true;
            try
            {
                var vis = VisibleColumns();
                int avail = list.ClientSize.Width - S(6), nameMin = S(110);
                int Used() => Enumerable.Range(1, list.Columns.Count - 1).Where(i => vis.Contains(list.Columns[i].Text)).Sum(i => colWidths[i]);
                foreach (var drop in DropOrder)
                {
                    if (Used() + nameMin <= avail) break;
                    vis.Remove(drop);
                }
                for (int i = 1; i < list.Columns.Count; i++)
                {
                    int cw = vis.Contains(list.Columns[i].Text) ? colWidths[i] : 0;
                    if (list.Columns[i].Width != cw) list.Columns[i].Width = cw;
                }
                int nameW = Math.Max(S(90), avail - Used());
                if (list.Columns[0].Width != nameW) list.Columns[0].Width = nameW;
            }
            finally { layingColumns = false; }
        }

        // =================================================================== demo mode (sample data for screenshots)
        void DemoRows()
        {
            pcSsid = "Home-5G"; pcNetText.Text = "This PC: Wi-Fi \u201CHome-5G\u201D";
            if (store.Devices.Count == 0)
            {
                store.Devices.Add(new DeviceRec { Name = "Pixel 8", IP = "192.168.1.21", HwSerial = "DEMO0001", LastSsid = "Home-5G", Profile = "Auto quality" });
                store.Devices.Add(new DeviceRec { Name = "Galaxy Tab S10", IP = "192.168.1.34", HwSerial = "DEMO0002", LastSsid = "Home-5G", Profile = "High quality + audio" });
                store.Devices.Add(new DeviceRec { Name = "Lenovo Y700", IP = "192.168.1.47", HwSerial = "DEMO0003", LastSsid = "Home-5G" });
            }
            var d = store.Devices;
            rows = new List<Row>
            {
                new Row { Key = d[0].Id, Name = d[0].Name, Type = "wifi", Serial = d[0].AdbSerial, Saved = true, Dev = d[0], State = "connected", StatusText = "Ready", Profile = d[0].Profile, AdbState = "device" },
                new Row { Key = d[1].Id, Name = d[1].Name, Type = "wifi", Serial = d[1].AdbSerial, Saved = true, Dev = d[1], State = "connected", StatusText = "Ready", Profile = d[1].Profile, AdbState = "device" },
                new Row { Key = "new:DEMO0004", Name = "Galaxy S23", Type = "usb", Serial = "DEMO0004", Saved = false, State = "connected", StatusText = "Ready \u2013 new", Profile = "Default", AdbState = "device" },
                new Row { Key = d[2].Id, Name = d[2].Name, Type = "wifi", Serial = d[2].AdbSerial, Saved = true, Dev = d[2], State = "offline", StatusText = "Offline", Profile = d[2].Profile },
            };
            info[rows[0].Serial] = new DevInfo { Battery = 84, Android = "16", Ssid = "Home-5G", When = DateTime.Now };
            info[rows[1].Serial] = new DevInfo { Battery = 61, Charging = true, Android = "16", Ssid = "Home-5G", When = DateTime.Now };
            info[rows[2].Serial] = new DevInfo { Battery = 47, Charging = true, Android = "15", Ssid = "Home-5G", When = DateTime.Now };
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct RECT { public int L, T, R, B; }

        static void CaptureWindow(Form f, string path)
        {
            f.TopMost = true; f.Activate(); f.Refresh(); Application.DoEvents();
            System.Threading.Thread.Sleep(700); Application.DoEvents();
            var b = f.Bounds;
            if (DwmGetWindowAttribute(f.Handle, 9, out var r, 16) == 0 && r.R > r.L) b = Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
            using (var bmp = new Bitmap(b.Width, b.Height))
            {
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(b.Location, Point.Empty, b.Size);
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            f.TopMost = false;
        }

        async Task DemoScreenshots()
        {
            var dir = Path.Combine(Tools.AppDir, "screenshots");
            Directory.CreateDirectory(dir);
            try
            {
                store.App["Mode"] = "Simple"; store.App["SimpleSize"] = S(520) + "x" + S(430); ApplyMode();
                CenterToScreen(); if (list.Items.Count > 0) list.Items[0].Selected = true;
                await Task.Delay(600);
                CaptureWindow(this, Path.Combine(dir, "simple.png"));

                store.App["Mode"] = "Advanced"; store.App["AdvancedSize"] = S(1080) + "x" + S(680); ApplyMode();
                CenterToScreen();
                await Task.Delay(600);
                CaptureWindow(this, Path.Combine(dir, "advanced.png"));

                var t = new Timer { Interval = 900 };
                t.Tick += (s, e) =>
                {
                    t.Stop();
                    var f = Form.ActiveForm;
                    if (f != null && f != this) { CaptureWindow(f, Path.Combine(dir, "tour.png")); f.Close(); }
                };
                t.Start();
                ShowTour();
            }
            catch (Exception ex) { try { File.WriteAllText(Path.Combine(dir, "error.txt"), ex.ToString()); } catch { } }
            reallyExit = true; Close();
        }

        // =================================================================== Windows compatibility
        void CheckWindows()
        {
            var note = Compat.WindowsNote(Environment.OSVersion.Version, Environment.Is64BitOperatingSystem, Tools.NativeArch());
            if (note == null) return;
            var key = Environment.OSVersion.Version + "/" + Tools.NativeArch() + "/" + Environment.Is64BitOperatingSystem;
            if (store.AppStr("WindowsNoteSeen") == key) return;   // shown once per Windows version
            store.App["WindowsNoteSeen"] = key; store.SaveApp();
            Msg(note + "\n\nHelp > Compatibility lists what CYSMS supports.", "CYSMS \u2013 This PC");
        }

        // =================================================================== quick tour
        void ShowTour()
        {
            var steps = new[]
            {
                new[] { "Welcome to CYSMS",
                    "CYSMS shows your Android phone or tablet in a window on this PC, and lets you control it with your mouse and keyboard.\n\n" +
                    "Everything it needs (scrcpy and adb) is already included. This tour has 4 short steps." },
                new[] { "1. Get the phone ready (once per phone)",
                    "On the phone:\n\n" +
                    "\u2022 Settings > About phone: tap Build number 7 times to unlock Developer options.\n" +
                    "\u2022 Settings > System > Developer options: turn on USB debugging.\n\n" +
                    "Works with Android 5.0 or later. Sound on the PC needs Android 11 or later.\n\n" +
                    "Menu names differ a little between brands. The full guide has the details." },
                new[] { "2. Plug it in and connect",
                    "\u2022 Connect the phone to this PC with a USB cable.\n" +
                    "\u2022 On the phone, tap Allow on \u201CAllow USB debugging?\u201D (tick Always allow).\n" +
                    "\u2022 The phone appears in the Devices list as Ready.\n" +
                    "\u2022 Select it and press Connect, or just double-click it." },
                new[] { "3. Go wireless",
                    "\u2022 Select the plugged-in phone and press Make wireless. CYSMS switches it to Wi-Fi and saves it, so you can unplug the cable.\n" +
                    "\u2022 The phone and this PC must be on the same Wi-Fi. The Wi-Fi network column shows which one each is on.\n" +
                    "\u2022 If the phone restarts, plug it in once and CYSMS switches it back to Wi-Fi by itself.\n\n" +
                    "No cable? + Add device also offers pairing with a code, scanning the network, or adding by IP address." },
                new[] { "4. Make it yours",
                    "\u2022 Quality: leave it on Auto, or pick Smooth, Balanced or Sharp.\n" +
                    "\u2022 Options: phone sound on the PC, screen off, always on top, view only.\n" +
                    "\u2022 Phone: wake, home, back, volume and screenshot.\n" +
                    "\u2022 Right-click the column headers to choose what the list shows.\n" +
                    "\u2022 Advanced mode (top right) opens every scrcpy option.\n\n" +
                    "You can run this tour again from Help > Quick tour." },
            };
            using (var f = new Form { Text = "CYSMS \u2013 Quick tour", StartPosition = FormStartPosition.CenterParent, Icon = AppIcon, ClientSize = new Size(S(520), S(340)),
                                      BackColor = Card, AutoScaleMode = AutoScaleMode.None, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, Font = FontUI })
            {
                var head = new Panel { Dock = DockStyle.Top, Height = S(64), BackColor = Head };
                var logo = new PictureBox { Size = new Size(S(40), S(40)), Location = new Point(S(16), S(12)), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
                try { logo.Image = new Icon(AppIcon, 48, 48).ToBitmap(); } catch { }
                var title = new Label { AutoSize = false, Location = new Point(S(68), S(10)), Size = new Size(S(440), S(28)), Font = FontH2, ForeColor = Color.White, UseMnemonic = false };
                var count = new Label { AutoSize = false, Location = new Point(S(69), S(38)), Size = new Size(S(440), S(20)), ForeColor = HeadSub, UseMnemonic = false };
                head.Controls.Add(logo); head.Controls.Add(title); head.Controls.Add(count);
                var body = new Label { Location = new Point(S(22), S(80)), Size = new Size(S(476), S(196)), ForeColor = Ui.Text, UseMnemonic = false, Font = new Font("Segoe UI", 10f) };
                var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = S(52), FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(S(12), S(8), S(12), 0), BackColor = Bg };
                var next = Btn("Next", true); var back = Btn("Back"); var skip = Btn("Skip tour"); var guide = Btn("Full setup guide");
                bar.Controls.AddRange(new Control[] { next, back, skip, guide });
                f.Controls.Add(body); f.Controls.Add(bar); f.Controls.Add(head);
                int i = 0;
                Action show = () =>
                {
                    title.Text = steps[i][0]; body.Text = steps[i][1];
                    count.Text = i == 0 ? "Quick tour" : "Step " + i + " of " + (steps.Length - 1);
                    back.Enabled = i > 0; guide.Visible = i == 1; skip.Visible = i < steps.Length - 1;
                    next.Text = i == steps.Length - 1 ? "Done" : i == 0 ? "Start" : "Next";
                };
                next.Click += (s, e) => { if (i == steps.Length - 1) f.Close(); else { i++; show(); } };
                back.Click += (s, e) => { if (i > 0) { i--; show(); } };
                skip.Click += (s, e) => f.Close();
                guide.Click += (s, e) => TextWindow("CYSMS \u2013 Phone setup guide", SetupGuide);
                f.AcceptButton = next; f.CancelButton = skip;
                show();
                f.ShowDialog(this);
            }
            if (!store.AppBool("TourSeen")) { store.App["TourSeen"] = true; store.SaveApp(); }
        }

        static Size ParseSize(string v, Size fallback)
        {
            var m = System.Text.RegularExpressions.Regex.Match(v ?? "", @"^(\d+)x(\d+)$");
            return m.Success ? new Size(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) : fallback;
        }

        static Size MinSize(bool simple) => simple ? new Size(S(400), S(330)) : new Size(S(780), S(500));

        Size WantSize(bool simple)
        {
            var min = MinSize(simple);
            var want = simple ? ParseSize(store.AppStr("SimpleSize"), new Size(S(500), S(480))) : ParseSize(store.AppStr("AdvancedSize"), new Size(S(1140), S(760)));
            return new Size(Math.Max(want.Width, min.Width), Math.Max(want.Height, min.Height));
        }

        void RememberSize(bool simple)
        {
            if (WindowState != FormWindowState.Normal) return;
            store.App[simple ? "SimpleSize" : "AdvancedSize"] = Width + "x" + Height;
        }

        void ApplyMode(bool? cameFromSimple = null)
        {
            bool simple = SimpleMode;
            if (cameFromSimple.HasValue) RememberSize(cameFromSimple.Value);
            modeBtn.Text = simple ? "Advanced mode" : "Simple mode";
            btnQuality.Visible = btnOptions.Visible = btnPhone.Visible = simple;
            phoneBar.Visible = !simple; btnRename.Visible = btnRemove.Visible = !simple;
            var min = MinSize(simple);
            var want = WantSize(simple);
            MinimumSize = new Size(1, 1);
            if (simple)
            {
                // the split's minimum sizes must fit the small window, or it stops laying out its panels
                try { split.Panel1MinSize = S(25); split.Panel2MinSize = S(25); } catch { }
                split.Panel2Collapsed = true;
            }
            if (WindowState == FormWindowState.Normal) Size = want;
            MinimumSize = min;
            if (!simple)
            {
                split.Panel2Collapsed = false;
                try
                {
                    split.Panel1MinSize = S(330); split.Panel2MinSize = S(410);
                    split.SplitterDistance = Math.Max(S(330), Math.Min((int)(ClientSize.Width * 0.5), ClientSize.Width - S(414)));
                }
                catch { }
            }
            LayoutColumns();
            EnsureOnScreen();
            UpdateResponsive(); UpdateTitle(); SyncSimple();
            if (IsHandleCreated) BeginInvoke((Action)LayoutColumns);
        }

        // Hide secondary text when the window is narrow
        void UpdateResponsive()
        {
            if (headSub == null || pcNetText == null) return;
            int w = ClientSize.Width;
            headSub.Visible = !SimpleMode && w > S(900);
            toolLabel.Visible = w > S(640);   // a missing scrcpy is already shown by the banner
            pcNetText.Visible = w > S(700);
            sessionText.Visible = w > S(480);
        }

        async Task RefreshPcNetwork()
        {
            if ((DateTime.Now - pcSsidWhen).TotalSeconds < 30) return;
            pcSsidWhen = DateTime.Now;
            var res = await Tools.RunAsync("netsh.exe", new[] { "wlan", "show", "interfaces" }, 8000);
            pcSsid = Parse.PcSsid(res.Output);
            if (pcSsid != null) pcNetNote = "This PC: Wi-Fi \u201C" + pcSsid + "\u201D";
            else if (res.Output.IndexOf("location", StringComparison.OrdinalIgnoreCase) >= 0) pcNetNote = "This PC: Wi-Fi name hidden (turn on Location in Windows Settings to show it)";
            else pcNetNote = "This PC: not on Wi-Fi";
            pcNetText.Text = pcNetNote;
        }

        // =================================================================== stickers
        bool StickersOn => store.AppBool("ShowStickers") && Stickers.Count > 0;

        static void SetImage(PictureBox pb, Image img)
        {
            var old = pb.Image;
            pb.Image = img;
            if (old != null && old != img) { try { old.Dispose(); } catch { } }
        }

        // Header (and empty-state) sticker follows the app's state; only changes when the state changes.
        void UpdateMood(string forced = null)
        {
            string m = forced;
            if (m == null)
            {
                if (busy && Stickers.Matching("busy").Count > 0) m = "busy";
                else if (sessions.Any(x => !x.Exited)) m = "start";
                else if (rows.Count == 0 || rows.All(r => r.State == "offline")) m = "idle";
                else m = "ready";
            }
            if (!StickersOn)
            {
                headSticker.Visible = false; emptySticker.Visible = false;
                SetImage(headSticker, null); SetImage(emptySticker, null); mood = ""; return;
            }
            if (m == mood) return;
            mood = m;
            var f = Stickers.Pick(m, true, moodFile);
            moodFile = f;
            SetImage(headSticker, f != null ? Stickers.LoadImage(f) : null);
            headSticker.Visible = headSticker.Image != null;
            var ef = Stickers.Pick("idle", true);
            SetImage(emptySticker, ef != null ? Stickers.LoadImage(ef) : null);
            emptySticker.Visible = emptySticker.Image != null;
        }

        void ShowToast(string file, string caption)
        {
            if (!StickersOn || file == null) return;
            var img = Stickers.LoadImage(file);
            if (img == null) return;
            SetImage(toastPic, img);
            toastText.Text = caption;
            var host = toast.Parent;
            toast.Location = new Point(host.ClientSize.Width - toast.Width - S(14), host.ClientSize.Height - toast.Height - S(14));
            toast.Visible = true; toast.BringToFront();
            toastTimer.Stop(); toastTimer.Start();
        }

        void ShowMoodToast(string m, string caption) { if (StickersOn) ShowToast(Stickers.Pick(m, true), caption); }

        void HideToast() { toastTimer.Stop(); toast.Visible = false; SetImage(toastPic, null); }

        void RestartApp()
        {
            if (dirty && !ConfirmDiscard()) return;
            Program.RestartRequested = true; reallyExit = true;
            foreach (var x in sessions.Where(x => !x.Exited).ToList()) x.Stop();
            Close();
        }

        // =================================================================== helpers
        public void ShowMain()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            EnsureOnScreen();
            Activate(); BringToFront();
        }

        void EnsureOnScreen()
        {
            if (WindowState != FormWindowState.Normal) return;
            foreach (var scr in Screen.AllScreens)
            {
                var hit = Rectangle.Intersect(scr.WorkingArea, Bounds);
                if (hit.Width >= S(200) && hit.Height >= S(100)) return;
            }
            var wa = Screen.PrimaryScreen.WorkingArea;
            int w = Math.Min(Width, wa.Width), h = Math.Min(Height, wa.Height);
            Bounds = new Rectangle(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2, w, h);
        }

        static void OpenFolder(string p)
        {
            try { Directory.CreateDirectory(p); Process.Start("explorer.exe", "\"" + p + "\""); } catch { }
        }

        static void OpenUrl(string u)
        {
            try { Process.Start(new ProcessStartInfo(u) { UseShellExecute = true }); } catch { }
        }

        // Runs a UI action, one at a time, reporting errors instead of crashing.
        async void RunAction(Func<Task> act)
        {
            if (busy) { SetStatus("Please wait, still working on the previous action..."); return; }
            busy = true; UseWaitCursor = true;
            UpdateMood();
            try { await act(); }
            catch (Exception ex) { Err("Something went wrong:\n\n" + ex.Message); }
            finally { busy = false; UseWaitCursor = false; UpdateButtons(); UpdateMood(); }
        }

        void LocateScrcpy()
        {
            using (var d = new OpenFileDialog { Filter = "scrcpy.exe|scrcpy.exe", Title = "Locate scrcpy.exe" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                store.App["ScrcpyPath"] = d.FileName; store.App["PreferBundled"] = false; store.SaveApp();
                InitTools();
                RunAction(RefreshDevices);
            }
        }

        void InitTools()
        {
            Tools.Locate(store.AppStr("ScrcpyPath"), store.AppBool("PreferBundled"));
            banner.Visible = Tools.Scrcpy == null;
            bannerText.Text = Tools.BundledProblem != null
                ? "The included scrcpy can't run on " + Tools.BundledProblem + ". Download the 32-bit scrcpy (scrcpy-win32) and locate it."
                : "scrcpy was not found.";
            toolLabel.Text = Tools.Scrcpy == null ? "scrcpy not found" : "scrcpy" + (Tools.IsBundled ? " (bundled)" : "") + (Tools.Adb != null ? " – adb ready" : " – adb missing");
            UpdateResponsive();
            toolLabel.ForeColor = Tools.Scrcpy == null ? Col("#FCA5A5") : HeadSub;
            appInfo.Text = "scrcpy: " + (Tools.Scrcpy ?? "not found") + "\nadb: " + (Tools.Adb ?? "not found") + "\nSaved data: " + store.Dir;
            UpdateButtons();
        }

        async Task FillVersion()
        {
            if (Tools.Scrcpy == null) return;
            var r = await Tools.RunAsync(Tools.Scrcpy, new[] { "--version" }, 8000);
            var m = Regex.Match(r.Output, @"scrcpy\s+(\d+(?:\.\d+)+)");
            if (m.Success)
            {
                Tools.ScrcpyVersion = m.Groups[1].Value;
                toolLabel.Text = "scrcpy " + Tools.ScrcpyVersion + (Tools.IsBundled ? " (bundled)" : "") + (Tools.Adb != null ? " – adb ready" : " – adb missing");
            }
        }

        // =================================================================== devices
        Session SessionFor(string key) => sessions.FirstOrDefault(s => s.Key == key && !s.Exited);

        Row SelectedRow()
        {
            if (list.SelectedItems.Count == 0) return null;
            var key = list.SelectedItems[0].Tag as string;
            return rows.FirstOrDefault(r => r.Key == key);
        }

        async Task RefreshDevices()
        {
            if (refreshing) return;
            refreshing = true;
            try
            {
                if (Program.Demo) { DemoRows(); RenderList(); return; }
                await RefreshPcNetwork();
                var adb = Tools.Adb != null ? await Tools.DevicesAsync() : new List<AdbDev>();
                var probes = new Dictionary<string, bool>();
                var wifi = store.Devices.Where(d => d.Type == "wifi").ToList();
                var pt = wifi.Select(async d => new { d.AdbSerial, Ok = await Tools.ProbeAsync(d.IP, d.Port, 400) }).ToList();
                foreach (var p in await Task.WhenAll(pt)) probes[p.AdbSerial] = p.Ok;

                var nrows = new List<Row>();
                var claimed = new HashSet<string>();
                foreach (var d in store.Devices)
                {
                    var serial = d.AdbSerial;
                    if (string.IsNullOrEmpty(serial)) continue;
                    claimed.Add(serial);
                    var a = adb.FirstOrDefault(x => x.Serial == serial);
                    string state = "offline";
                    if (a != null && a.State == "device") state = "connected";
                    else if (a != null && a.State == "unauthorized") state = "unauthorized";
                    else if (d.Type == "wifi" && probes.TryGetValue(serial, out var ok) && ok) state = "online";
                    nrows.Add(new Row { Key = d.Id, Name = d.Name, Type = d.Type, Serial = serial, Saved = true, Dev = d, State = state, Model = a?.Model ?? "", Profile = d.Profile, AdbState = a?.State });
                }
                foreach (var a in adb)
                {
                    if (claimed.Contains(a.Serial) || (a.State != "device" && a.State != "unauthorized")) continue;
                    var type = Parse.IsNetworkSerial(a.Serial) ? "wifi" : "usb";
                    // A USB device we already know over Wi-Fi: show its saved name
                    var known = type == "usb" ? store.Devices.FirstOrDefault(d => d.HwSerial != "" && d.HwSerial == a.Serial) : null;
                    var name = known != null ? known.Name + " (USB)" : (a.Model != "" ? a.Model : a.Serial);
                    nrows.Add(new Row
                    {
                        Key = "new:" + a.Serial, Name = name, Type = type, Serial = a.Serial, Saved = false, State = a.State == "device" ? "connected" : "unauthorized",
                        Model = a.Model, Profile = known?.Profile ?? store.AppStr("LastProfile"), AdbState = a.State,
                    });
                }
                foreach (var r in nrows)
                {
                    if (SessionFor(r.Key) != null) r.State = "mirroring";
                    switch (r.State)
                    {
                        case "mirroring": r.StatusText = "Mirroring"; break;
                        case "connected": r.StatusText = "Ready"; break;
                        case "online": r.StatusText = "Ready (Wi-Fi)"; break;
                        case "unauthorized": r.StatusText = "Allow on phone"; break;
                        default: r.StatusText = "Offline"; break;
                    }
                    if (!r.Saved) r.StatusText += " – new";
                }
                rows = nrows;
                RenderList();

                await AutoWireless(adb);
                await UpdateInfo();
            }
            finally { refreshing = false; }
        }

        // Saved Wi-Fi phone plugged in by USB while its Wi-Fi entry is offline: re-enable wireless mode
        async Task AutoWireless(List<AdbDev> adb)
        {
            if (!store.AppBool("AutoWireless") || Tools.Adb == null) return;
            foreach (var a in adb.Where(x => x.State == "device" && !Parse.IsNetworkSerial(x.Serial)))
            {
                var saved = store.Devices.FirstOrDefault(d => d.Type == "wifi" && d.HwSerial == a.Serial);
                if (saved == null || autoWirelessTried.Contains(a.Serial)) continue;
                var row = rows.FirstOrDefault(r => r.Key == saved.Id);
                if (row == null || row.State != "offline") continue;
                autoWirelessTried.Add(a.Serial);
                SetStatus("Auto wireless: re-enabling Wi-Fi mode for " + saved.Name + "...");
                var ip = await DeviceIp(a.Serial);
                if (ip == null) { SetStatus("Auto wireless: " + saved.Name + " isn't on Wi-Fi"); continue; }
                await Tools.AdbAsync(10000, "-s", a.Serial, "tcpip", saved.Port.ToString(CultureInfo.InvariantCulture));
                await Task.Delay(2500);
                if (await Tools.ConnectWifiAsync(ip + ":" + saved.Port) == "device")
                {
                    if (saved.IP != ip) { saved.IP = ip; store.SaveDevices(); }
                    SetStatus("Auto wireless: " + saved.Name + " is back on Wi-Fi (" + ip + "). You can unplug it.");
                    tray.ShowBalloonTip(3000, "Wireless re-enabled", saved.Name + " is back on Wi-Fi at " + ip + ".", ToolTipIcon.Info);
                }
                else SetStatus("Auto wireless: couldn't reach " + saved.Name + " at " + ip);
            }
        }

        async Task<string> DeviceIp(string serial)
        {
            var ip = Parse.IpFromRoute(await Tools.AdbShellAsync(serial, "ip route"));
            if (ip == null) ip = Parse.IpFromRoute(await Tools.AdbShellAsync(serial, "ip -f inet addr show wlan0"));
            return ip;
        }

        // Battery / Android version for devices adb can talk to (cached for 60 s)
        async Task UpdateInfo()
        {
            bool changed = false;
            foreach (var r in rows.Where(x => x.AdbState == "device").ToList())
            {
                if (info.TryGetValue(r.Serial, out var di) && (DateTime.Now - di.When).TotalSeconds < 60) continue;
                di = new DevInfo { When = DateTime.Now };
                var bat = await Tools.AdbShellAsync(r.Serial, "dumpsys battery", 6000);
                di.Battery = Parse.BatteryLevel(bat); di.Charging = Parse.BatteryCharging(bat);
                di.Android = await Tools.GetPropAsync(r.Serial, "ro.build.version.release");
                di.Sdk = Compat.ParseSdk(await Tools.GetPropAsync(r.Serial, "ro.build.version.sdk"));
                di.HwSerial = await Tools.GetPropAsync(r.Serial, "ro.serialno");
                di.Ssid = Parse.PhoneSsid(await Tools.AdbShellAsync(r.Serial, "cmd wifi status 2>/dev/null | head -20; dumpsys wifi | grep -m 1 mWifiInfo", 8000));
                if (di.Ssid != null)
                {
                    // remember the network for saved Wi-Fi entries (also when seen over USB)
                    var owner = r.Saved && r.Dev.Type == "wifi" ? r.Dev : store.Devices.FirstOrDefault(d => d.Type == "wifi" && d.HwSerial != "" && (d.HwSerial == r.Serial || d.HwSerial == di.HwSerial));
                    if (owner != null && owner.LastSsid != di.Ssid) { owner.LastSsid = di.Ssid; store.SaveDevices(); }
                }
                info[r.Serial] = di; changed = true;
                // remember the phone's hardware serial for Auto wireless
                if (r.Saved && r.Dev.Type == "wifi" && r.Dev.HwSerial == "" && di.HwSerial != "") { r.Dev.HwSerial = di.HwSerial; store.SaveDevices(); }
            }
            if (changed) RenderList();
        }

        void RenderList()
        {
            string selKey = list.SelectedItems.Count > 0 ? list.SelectedItems[0].Tag as string : null;
            bool same = list.Items.Count == rows.Count;
            for (int i = 0; same && i < rows.Count; i++) if ((string)list.Items[i].Tag != rows[i].Key) same = false;
            list.BeginUpdate();
            if (!same) list.Items.Clear();
            for (int i = 0; i < rows.Count; i++)
            {
                var it = same ? list.Items[i] : new ListViewItem();
                SetItem(it, rows[i]);
                if (!same) list.Items.Add(it);
            }
            list.EndUpdate();

            ListViewItem target = null;
            foreach (ListViewItem it in list.Items) if ((string)it.Tag == selKey) target = it;
            if (target == null)
                foreach (ListViewItem it in list.Items)
                {
                    var r = rows.FirstOrDefault(x => x.Key == (string)it.Tag);
                    if (r != null && r.State != "offline") { target = it; break; }
                }
            if (target == null && list.Items.Count > 0) target = list.Items[0];
            if (target != null && !target.Selected) { target.Selected = true; target.Focused = true; }

            empty.Visible = rows.Count == 0;
            UpdatePreview();
            UpdateMood();
            int n = sessions.Count(s => !s.Exited);
            sessionText.Text = n == 0 ? "No active sessions" : n == 1 ? "1 device mirroring" : n + " devices mirroring";
            UpdateButtons();
        }

        void SetItem(ListViewItem it, Row r)
        {
            info.TryGetValue(r.Serial ?? "", out var di);
            bool live = r.AdbState == "device";
            string net = "";
            if (live && di?.Ssid != null) net = di.Ssid + (pcSsid != null && di.Ssid != pcSsid ? " (PC is on " + pcSsid + ")" : "");
            else if (r.Saved && r.Dev.LastSsid != "") net = "last seen: " + r.Dev.LastSsid;
            bool tooOld = live && di?.Sdk < Compat.MinSdk;
            var vals = new[]
            {
                tooOld ? "Not supported (needs Android 5+)" : r.StatusText,
                r.Type == "usb" ? "USB" : "Wi-Fi",
                net,
                live && di?.Battery != null ? di.Battery + "%" + (di.Charging ? " ⚡" : "") : "",
                live && di != null ? di.Android : "",
                r.Serial,
                r.Saved ? r.Profile : "(not saved)",
            };
            if (it.Text != r.Name) it.Text = r.Name;
            it.ImageKey = r.State; it.Tag = r.Key;
            while (it.SubItems.Count < 8) it.SubItems.Add("");
            for (int i = 0; i < vals.Length; i++) if (it.SubItems[i + 1].Text != vals[i]) it.SubItems[i + 1].Text = vals[i];
            it.ForeColor = r.State == "offline" ? Muted : Ui.Text;
            it.Font = r.Saved ? FontUI : FontItalic;
        }

        void UpdateButtons()
        {
            var r = SelectedRow();
            bool has = r != null, running = has && r.State == "mirroring", live = has && r.State != "offline";
            btnConnect.Enabled = has && Tools.Scrcpy != null && !running;
            btnStop.Enabled = running;
            btnSave.Visible = has && !r.Saved;
            btnShot.Enabled = has && live && r.State != "unauthorized";
            btnWifi.Visible = has && r.Type == "usb";
            btnRename.Enabled = has; btnRemove.Enabled = has && r.Saved;
            phoneBar.Enabled = btnPhone.Enabled = has && live && r.State != "unauthorized";
            miSave.Visible = has && !r.Saved; miRemove.Enabled = has && r.Saved;
            miStop.Enabled = running; miConnect.Enabled = has && !running;
            miWifi.Visible = has && r.Type == "usb"; miDisc.Visible = has && r.Type == "wifi";
            miShot.Enabled = btnShot.Enabled;
        }

        void SelectKey(string key)
        {
            foreach (ListViewItem it in list.Items) if ((string)it.Tag == key) { it.Selected = true; it.Focused = true; it.EnsureVisible(); }
        }

        // =================================================================== settings
        void WriteSettingsToControls(Dictionary<string, object> s)
        {
            loading = true;
            try
            {
                foreach (var o in Schema.All)
                {
                    if (!ctl.TryGetValue(o.K, out var c)) continue;
                    s.TryGetValue(o.K, out var v);
                    switch (c)
                    {
                        case CheckBox cb: cb.Checked = v is bool b && b; break;
                        case NumericUpDown n:
                            decimal d = 0; try { d = Convert.ToDecimal(v, CultureInfo.InvariantCulture); } catch { }
                            n.Value = Math.Min(n.Maximum, Math.Max(n.Minimum, d)); break;
                        case ComboBox cmb:
                            int idx = Array.IndexOf(comboVals[o.K], Convert.ToString(v, CultureInfo.InvariantCulture));
                            cmb.SelectedIndex = idx >= 0 ? idx : 0; break;
                        default: c.Text = Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""; break;
                    }
                }
            }
            finally { loading = false; }
            UpdateEnabled();
            SyncSimple();
        }

        Dictionary<string, object> ReadControls()
        {
            var s = new Dictionary<string, object>();
            foreach (var o in Schema.All)
            {
                if (!ctl.TryGetValue(o.K, out var c)) { s[o.K] = o.D; continue; }
                switch (c)
                {
                    case CheckBox cb: s[o.K] = cb.Checked; break;
                    case NumericUpDown n: s[o.K] = (int)n.Value; break;
                    case ComboBox cmb: s[o.K] = comboVals[o.K][Math.Max(0, cmb.SelectedIndex)]; break;
                    default: s[o.K] = c.Text.Trim(); break;
                }
            }
            return s;
        }

        void UpdateEnabled()
        {
            foreach (var o in Schema.All)
            {
                if (string.IsNullOrEmpty(o.Dep) || !ctl.TryGetValue(o.K, out var c)) continue;
                bool en = Schema.TestDep(o.Dep, cur);
                if ((o.T == "pick" || o.T == "folder") && c.Parent != null) c.Parent.Enabled = en; else c.Enabled = en;
            }
            // Auto quality also picks the size (except for camera streams)
            if (ctl.TryGetValue("MaxSize", out var ms))
                ms.Enabled = !(Schema.B(cur, "AutoBitRate") && Schema.Str(cur, "VideoSource") != "camera");
        }

        void UpdateTitle() => profileTitle.Text = "Settings – " + activeProfile + (dirty ? " (unsaved changes, Ctrl+S to save)" : "");

        void UpdatePreview()
        {
            var r = SelectedRow();
            if (r == null) { preview.Text = "Select a device to see the scrcpy command."; return; }
            preview.Text = Schema.JoinArgs(new[] { "scrcpy" }.Concat(Schema.BuildArgs(cur, r.Serial, r.Name, true)));
        }

        void OnSettingChanged()
        {
            if (loading) return;
            cur = ReadControls();
            dirty = true;
            UpdateEnabled(); UpdatePreview(); UpdateTitle();
        }

        void FillProfileCombo(string select)
        {
            loading = true;
            try
            {
                profileCombo.Items.Clear();
                foreach (var kv in store.Profiles) profileCombo.Items.Add(kv.Key);
                int i = profileCombo.Items.IndexOf(select);
                profileCombo.SelectedIndex = i >= 0 ? i : 0;
            }
            finally { loading = false; }
        }

        void LoadProfileIntoUI(string name)
        {
            FillProfileCombo(name);
            activeProfile = (string)profileCombo.SelectedItem;
            cur = store.GetProfile(activeProfile);
            WriteSettingsToControls(cur);
            dirty = false;
            UpdateTitle(); UpdatePreview();
        }

        bool ConfirmDiscard()
        {
            if (!dirty) return true;
            var res = Msg("Save changes to profile '" + activeProfile + "' first?", "Unsaved changes", MessageBoxIcon.Question, MessageBoxButtons.YesNoCancel);
            if (res == DialogResult.Cancel) return false;
            if (res == DialogResult.Yes) SaveCurrentProfile(); else dirty = false;
            return true;
        }

        void SaveCurrentProfile()
        {
            if (string.IsNullOrEmpty(activeProfile)) return;
            store.SetProfile(activeProfile, ReadControls()); store.SaveProfiles();
            dirty = false; UpdateTitle();
            SetStatus("Profile '" + activeProfile + "' saved");
        }

        void SaveProfileAs()
        {
            var r = Input("Save profile as", "Save the current settings as a new profile.", new[] { new Field { Key = "Name", Label = "Profile name" } }, "Save");
            if (r == null || r["Name"] == "") return;
            var n = r["Name"];
            if (store.HasProfile(n) && !Ask("Replace the existing profile '" + n + "'?")) return;
            store.SetProfile(n, ReadControls()); store.SaveProfiles();
            dirty = false;
            FillProfileCombo(n); activeProfile = n;
            var sel = SelectedRow();
            if (sel != null && sel.Saved) { sel.Dev.Profile = n; store.SaveDevices(); RunAction(RefreshDevices); }
            UpdateTitle(); SetStatus("Profile '" + n + "' saved");
        }

        void DeleteProfile()
        {
            var n = activeProfile;
            if (store.Profiles.Count <= 1) { Err("You need at least one profile."); return; }
            if (!Ask("Delete the profile '" + n + "'? Devices using it will switch to another profile.")) return;
            store.RemoveProfile(n); store.SaveProfiles();
            var fb = store.Profiles[0].Key;
            foreach (var d in store.Devices.Where(d => d.Profile == n)) d.Profile = fb;
            store.SaveDevices();
            dirty = false;
            LoadProfileIntoUI(fb);
            RunAction(RefreshDevices);
        }

        void OnProfileComboChanged()
        {
            if (loading) return;
            var nw = (string)profileCombo.SelectedItem;
            if (nw == activeProfile) return;
            if (dirty)
            {
                loading = true; profileCombo.SelectedItem = activeProfile; loading = false;
                if (!ConfirmDiscard()) return;
                loading = true; profileCombo.SelectedItem = nw; loading = false;
            }
            activeProfile = nw;
            cur = store.GetProfile(nw);
            WriteSettingsToControls(cur);
            dirty = false;
            var r = SelectedRow();
            if (r != null && r.Saved && r.Dev.Profile != nw) { r.Dev.Profile = nw; r.Profile = nw; store.SaveDevices(); RenderList(); }
            store.App["LastProfile"] = nw; store.SaveApp();
            UpdateTitle(); UpdatePreview();
        }

        void OnSelectionChanged()
        {
            var r = SelectedRow();
            UpdateButtons();
            if (r == null || r.Key == lastSelKey) { UpdatePreview(); return; }
            lastSelKey = r.Key;
            var want = r.Saved ? r.Profile : store.AppStr("LastProfile");
            if (want != activeProfile && store.HasProfile(want) && ConfirmDiscard()) LoadProfileIntoUI(want);
            else { UpdatePreview(); SyncSimple(); }
        }

        // =================================================================== pickers
        async Task LoadPick(Opt o, TextBox target)
        {
            var r = SelectedRow();
            if (r == null || r.State == "offline" || r.State == "unauthorized") { Err("Select a connected device in the list first, then press Load again."); return; }
            if (Tools.Scrcpy == null) { Err("scrcpy was not found."); return; }
            if (r.Type == "wifi" && Parse.IsNetworkSerial(r.Serial) && r.AdbState != "device")
                if (await Tools.ConnectWifiAsync(r.Serial) != "device") { Err("Couldn't reach " + r.Name + "."); return; }
            string flag, what;
            switch (o.Pick)
            {
                case "encoders": flag = "--list-encoders"; what = "video encoders"; break;
                case "displays": flag = "--list-displays"; what = "displays"; break;
                case "cameras": flag = "--list-cameras"; what = "cameras"; break;
                default: flag = "--list-apps"; what = "apps"; break;
            }
            SetStatus("Reading " + what + " from " + r.Name + "...");
            var res = await Tools.RunAsync(Tools.Scrcpy, new[] { "--serial=" + r.Serial, flag }, 30000);
            List<PickItem> items;
            switch (o.Pick)
            {
                case "encoders": items = Parse.Encoders(res.Output).Where(x => x.Extra == Schema.Str(cur, "VideoCodec")).ToList(); break;
                case "displays": items = Parse.Displays(res.Output); break;
                case "cameras": items = Parse.Cameras(res.Output); break;
                default: items = Parse.Apps(res.Output); break;
            }
            SetStatus("Ready");
            if (items.Count == 0)
            {
                var tail = string.Join("\n", Parse.Lines(res.Output).Where(l => l.Trim() != "").Reverse().Take(6).Reverse());
                Err("No " + what + " were found" + (o.Pick == "encoders" ? " for the " + Schema.Str(cur, "VideoCodec").ToUpperInvariant() + " codec" : "") + ".\n\n" + tail);
                return;
            }
            var pick = PickFromList("Choose " + what.TrimEnd('s'), "Found " + items.Count + " " + what + " on " + r.Name + ":", items);
            if (pick == null) return;
            target.Text = pick.Value;
        }

        // =================================================================== actions
        Task ConnectSelected() { var r = SelectedRow(); return r == null ? Task.CompletedTask : ConnectRow(r, true); }

        async Task ConnectRow(Row r, bool useCurrent, Dictionary<string, object> settings = null, bool retried = false)
        {
            if (Tools.Scrcpy == null) { Err("scrcpy was not found. Use the yellow bar at the top to locate it."); return; }
            if (SessionFor(r.Key) != null) { SetStatus(r.Name + " is already mirroring"); return; }
            var s = settings ?? (useCurrent ? new Dictionary<string, object>(cur) : store.GetProfile(r.Profile));
            var serial = r.Serial;

            if (r.Type == "wifi" && Parse.IsNetworkSerial(serial) && r.AdbState != "device")
            {
                SetStatus("Connecting to " + r.Name + " (" + serial + ")...");
                var res = await Tools.ConnectWifiAsync(serial);
                if (res == "unauthorized")
                {
                    Err(r.Name + " is asking for permission.\n\nUnlock the phone, tap 'Allow' on the debugging prompt (tick 'Always allow'), then press Connect again.");
                    SetStatus("Waiting for permission on the phone"); await RefreshDevices(); return;
                }
                if (res != "device")
                {
                    ShowMoodToast("fail", "Can't reach " + r.Name);
                    Err("Couldn't reach " + r.Name + " at " + serial + ".\n\n" +
                        "• Is the phone on, unlocked and on the same Wi-Fi?\n" +
                        "• If the phone restarted, wireless mode was reset: plug it in by USB" + (store.AppBool("AutoWireless") ? " (Auto wireless will switch it back)." : " and use 'Make wireless'.") + "\n" +
                        "• Guest or office networks may block device-to-device traffic.");
                    SetStatus("Connection failed"); await RefreshDevices(); return;
                }
            }
            else if (r.State == "unauthorized")
            {
                Err("Unlock " + r.Name + " and tap 'Allow' on the USB debugging prompt, then press Connect again."); return;
            }

            // Compatibility: block what this Android version can't do, switch off optional extras it lacks
            var compat = Compat.ForDevice(await SdkOf(serial), s, r.Type == "usb");
            if (compat.Block != null)
            {
                ShowMoodToast("fail", "Not on this device");
                Err(r.Name + " can't be mirrored with these settings.\n\n" + compat.Block, "Not supported on this device");
                SetStatus(r.Name + ": not supported with these settings"); return;
            }
            s = compat.Settings;
            string compatNote = compat.Notes.Count > 0 ? " Note: " + string.Join("; ", compat.Notes) + "." : "";

            // Locked device: mirroring still works and shows the lock screen, but face/fingerprint unlock can't be
            // used from the PC, View only can't unlock at all, and Android 11 needs it unlocked for sound
            var lockOut = await Tools.AdbShellAsync(serial, Parse.LockProbe, 6000);
            bool locked = Parse.Locked(lockOut) == true;
            DiagLog("lock probe " + r.Name + " -> " + (Parse.Locked(lockOut)?.ToString() ?? "unknown") + "\n" + lockOut);
            bool control = !Schema.B(s, "ViewOnly");
            if (locked && !LockWarning(r.Name, control, Schema.B(s, "Audio") && await SdkOf(serial) == 30))
            { SetStatus(r.Name + " is locked: unlock it, then press Connect"); return; }

            int? autoBr = null, autoMs = null;
            if (Schema.B(s, "AutoBitRate"))
            {
                if (r.Type == "usb") { autoBr = Parse.AutoBitRate(true, null); autoMs = Parse.AutoMaxSize(true, null); }
                else
                {
                    var w = await Tools.AdbShellAsync(serial, "cmd wifi status 2>/dev/null | grep -i -m 2 'link speed'; dumpsys wifi | grep -i -m 3 'link speed'", 8000);
                    var link = Parse.WifiLinkSpeed(w);
                    autoBr = Parse.AutoBitRate(false, link); autoMs = Parse.AutoMaxSize(false, link);
                    SetStatus("Wi-Fi link " + (link?.ToString() ?? "unknown") + " Mbps: using " + autoBr + " Mbps, " + (autoMs == 0 ? "full resolution" : autoMs + " px"));
                }
            }

            if (Schema.B(s, "Record"))
            {
                var dir = Schema.Str(s, "RecordFolder"); if (dir == "") dir = Schema.DefaultRecordDir;
                try { Directory.CreateDirectory(dir); } catch { Err("Can't create the recording folder:\n" + dir); return; }
            }

            var args = Schema.BuildArgs(s, serial, r.Name, false, autoBr, null, autoMs);
            var log = Path.Combine(logDir, Schema.SafeName(r.Name) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".log");
            try
            {
                var sess = Session.Launch(r.Key, r.Name, args, log, Schema.B(s, "Record"));
                sess.Settings = s; sess.Retried = retried; sess.Profile = r.Saved ? r.Profile : activeProfile;
                sess.Serial = serial; sess.Locked = locked && control;
                sessions.Add(sess);
            }
            catch (Exception ex) { Err("Couldn't start scrcpy:\n" + ex.Message); return; }
            SetStatus("Starting mirroring for " + r.Name + "..." + compatNote);
            await RefreshDevices();
        }

        // Android API level of a connected device (cached by UpdateInfo; asked directly on a first connect)
        async Task<int?> SdkOf(string serial)
        {
            if (info.TryGetValue(serial, out var di) && di.Sdk.HasValue) return di.Sdk;
            var sdk = Compat.ParseSdk(await Tools.GetPropAsync(serial, "ro.build.version.sdk"));
            if (sdk.HasValue && di != null) di.Sdk = sdk;
            return sdk;
        }

        async Task UnlockSelected()
        {
            var r = SelectedRow(); if (r == null || r.AdbState != "device") return;
            await ShowUnlockScreen(r.Serial);
            SetStatus(r.Name + ": PIN entry is open. Type your PIN or password and press Enter" + (SessionFor(r.Key) != null ? " (it may look black in the mirror)." : ", or connect first to do it from the mirror."));
        }

        // Wakes the device and asks Android to show its PIN / pattern screen (no effect on swipe-only locks except unlocking)
        async Task ShowUnlockScreen(string serial)
        {
            var o = await Tools.AdbShellAsync(serial, "input keyevent 224; wm dismiss-keyguard", 6000);
            DiagLog("dismiss-keyguard " + serial + " -> " + o);
        }

        // Shown before connecting to a locked device. Returns false to cancel.
        bool LockWarning(string name, bool control, bool android11Audio)
        {
            if (control && !android11Audio && store.AppBool("HideLockWarning")) { return true; }
            string text = !control
                ? name + " is locked, and View only is on, so it can't be unlocked from this PC.\n\nUnlock it on the device first, or turn off View only (Options)."
                : name + " is locked. Mirroring still works, but:\n\n" +
                  "\u2022 Face and fingerprint unlock only work on the device itself.\n" +
                  "\u2022 To unlock from this PC: swipe up in the mirror window, type your PIN or password and press Enter. " +
                  "Android hides the PIN screen from screen capture, so it may look black while you type. A pattern can't be drawn blind, so unlock it on the device.\n" +
                  "\u2022 If the mirror goes black later, the screen has timed out: right-click in the mirror to wake it." +
                  (android11Audio ? "\n\nOn Android 11, sound only reaches the PC if the device is unlocked when mirroring starts." : "");
            using (var f = new Form { Text = "Device is locked", StartPosition = FormStartPosition.CenterParent, Icon = AppIcon, BackColor = Card, Font = FontUI,
                                      FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
                                      AutoScaleMode = AutoScaleMode.None, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(S(16)) })
            {
                var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
                var lbl = Lbl(text); lbl.MaximumSize = new Size(S(440), 0); lbl.Margin = new Padding(0, 0, 0, S(12));
                t.Controls.Add(lbl);
                var dont = new CheckBox { Text = "Don't warn me again (the status bar still says when a device is locked)", AutoSize = true, Visible = control && !android11Audio };
                t.Controls.Add(dont);
                var bar = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, S(12), 0, 0) };
                var go = Btn(control ? "Connect" : "Connect anyway", true); var cancel = Btn("Cancel");
                go.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
                bar.Controls.Add(go); bar.Controls.Add(cancel);
                t.Controls.Add(bar);
                f.Controls.Add(t); f.AcceptButton = go; f.CancelButton = cancel;
                var ok = f.ShowDialog(this) == DialogResult.OK;
                if (ok && dont.Checked) { store.App["HideLockWarning"] = true; store.SaveApp(); }
                return ok;
            }
        }

        static void DiagLog(string text)
        {
            if (!Program.Diag) return;
            try { File.AppendAllText(Path.Combine(Tools.AppDir, "diag.txt"), DateTime.Now.ToString("s") + "  " + text + "\r\n\r\n"); } catch { }
        }

        void StopSelected()
        {
            var r = SelectedRow(); if (r == null) return;
            var s = SessionFor(r.Key);
            if (s != null) { s.Stop(); SetStatus("Stopped " + r.Name); }
        }

        async Task CheckSessions()
        {
            bool changed = false;
            foreach (var s in sessions.ToList())
            {
                double age = (DateTime.Now - s.Start).TotalSeconds;
                if (s.Exited)
                {
                    sessions.Remove(s); changed = true;
                    await Task.Delay(150);
                    s.CloseLog();
                    // Only a non-zero exit is a failure: closing the mirror window yourself ends scrcpy with exit code 0,
                    // and pressing Stop is never a failure
                    if (!s.Verified && age < 10 && s.ExitCode != 0 && !s.UserStopped)
                    {
                        var txt = s.ReadLog();
                        var hint = Parse.LogHint(txt);
                        var row = rows.FirstOrDefault(x => x.Key == s.Key);
                        if (SimpleMode && !s.Retried && row != null && s.Settings != null && hint.Contains("codec")
                            && (Schema.Str(s.Settings, "VideoCodec") != "h264" || Schema.Str(s.Settings, "VideoEncoder") != ""))
                        {
                            // Automatic decision: fall back to the most compatible codec once
                            var s2 = new Dictionary<string, object>(s.Settings) { ["VideoCodec"] = "h264", ["VideoEncoder"] = "" };
                            SetStatus(s.Name + " didn't accept " + Schema.Str(s.Settings, "VideoCodec").ToUpperInvariant() + ". Retrying with H.264...");
                            await ConnectRow(row, false, s2, true);
                            continue;
                        }
                        var tail = string.Join("\n", Parse.Lines(txt).Where(l => l.Trim() != "").Skip(1).Reverse().Take(10).Reverse());
                        ShowMoodToast("fail", "Oh no...");
                        var ans = Msg("Mirroring for " + s.Name + " stopped right after starting.\n\nTip: " + Parse.LogHint(txt) + "\n\nLast log lines:\n" + tail + "\n\nOpen the full log?",
                            "Mirroring failed", MessageBoxIcon.Warning, MessageBoxButtons.YesNo);
                        if (ans == DialogResult.Yes) { try { Process.Start("notepad.exe", "\"" + s.LogPath + "\""); } catch { } }
                        SetStatus("Mirroring failed for " + s.Name);
                    }
                    else
                    {
                        SetStatus("Mirroring ended: " + s.Name);
                        if (s.Record) tray.ShowBalloonTip(4000, "Recording saved", s.Name + " was saved in your recordings folder.", ToolTipIcon.Info);
                    }
                }
                else if (!s.Verified && (age >= 5 || age >= 1 && s.ReadLog().Contains("INFO: Texture:")))   // window is up
                {
                    s.Verified = true; changed = true;
                    SetStatus("Mirroring " + s.Name + (s.Retried ? " with H.264 (saved for next time)" : ""));
                    ShowMoodToast("start", s.Name + " is live!");
                    if (s.Locked)
                        SetStatus(s.Name + " is locked: swipe up in the mirror, then type your PIN or password and press Enter (the PIN screen may look black).");
                    if (s.Retried && s.Profile != null && store.HasProfile(s.Profile))
                    {
                        var p = store.GetProfile(s.Profile); p["VideoCodec"] = "h264"; p["VideoEncoder"] = "";
                        store.SetProfile(s.Profile, p); store.SaveProfiles();
                        if (s.Profile == activeProfile && !dirty) LoadProfileIntoUI(activeProfile);
                    }
                    if (store.AppBool("CloseAfterConnect") && sessions.All(x => x.Verified)) { reallyExit = true; Close(); return; }
                }
            }
            if (changed) await RefreshDevices();
        }

        async Task SaveSelected(string name)
        {
            var r = SelectedRow(); if (r == null || r.Saved) return;
            var di = info.TryGetValue(r.Serial, out var x) ? x : null;
            DeviceRec rec;
            var m = Regex.Match(r.Serial, @"^(.+):(\d+)$");
            if (r.Type == "wifi" && m.Success)
                rec = new DeviceRec { Name = name ?? r.Name, Type = "wifi", IP = m.Groups[1].Value, Port = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), Profile = activeProfile, HwSerial = di?.HwSerial ?? "" };
            else
                rec = new DeviceRec { Name = name ?? r.Name, Type = "usb", Serial = r.Serial, Profile = activeProfile, HwSerial = r.Serial };
            DeviceRec saved;
            if (name != null) { saved = store.AddOrUpdate(rec); SetStatus("Saved " + name); }
            else saved = AskSave(rec, rec.Type == "usb" ? "This saves the USB connection. Tip: use 'Make wireless' to also use it over Wi-Fi." : "");
            if (saved != null) { lastSelKey = saved.Id; await RefreshDevices(); SelectKey(saved.Id); }
        }

        DeviceRec AskSave(DeviceRec rec, string message)
        {
            var r = Input("Save device", message, new[] { new Field { Key = "Name", Label = "Device name", Default = rec.Name } }, "Save");
            if (r == null) return null;
            if (r["Name"] != "") rec.Name = r["Name"];
            var saved = store.AddOrUpdate(rec);
            SetStatus("Saved " + rec.Name);
            return saved;
        }

        void RenameSelected() { if (list.SelectedItems.Count > 0) list.SelectedItems[0].BeginEdit(); }

        void RemoveSelected()
        {
            var r = SelectedRow(); if (r == null || !r.Saved) return;
            if (!Ask("Remove '" + r.Name + "' from saved devices?")) return;
            store.Devices.Remove(r.Dev); store.SaveDevices();
            SetStatus("Removed " + r.Name); lastSelKey = null;
            RunAction(RefreshDevices);
        }

        async Task Disconnect()
        {
            var r = SelectedRow(); if (r == null) return;
            await Tools.AdbAsync(5000, "disconnect", r.Serial);
            SetStatus("Disconnected " + r.Name); await RefreshDevices();
        }

        async Task<bool> EnsureReachable(Row r)
        {
            if (r.AdbState == "device") return true;
            if (r.Type == "wifi" && Parse.IsNetworkSerial(r.Serial) && await Tools.ConnectWifiAsync(r.Serial) == "device") return true;
            Err("Couldn't reach " + r.Name + ".");
            return false;
        }

        async Task SendKey(int code)
        {
            var r = SelectedRow(); if (r == null || !await EnsureReachable(r)) return;
            await Tools.AdbShellAsync(r.Serial, "input keyevent " + code.ToString(CultureInfo.InvariantCulture), 6000);
        }

        async Task Screenshot()
        {
            var r = SelectedRow(); if (r == null || Tools.Adb == null || !await EnsureReachable(r)) return;
            Directory.CreateDirectory(shotDir);
            var file = Path.Combine(shotDir, Schema.SafeName(r.Name) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".png");
            SetStatus("Taking screenshot of " + r.Name + "...");
            bool ok = await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo(Tools.Adb, Schema.JoinArgs(new[] { "-s", r.Serial, "exec-out", "screencap", "-p" }))
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    using (var p = Process.Start(psi))
                    using (var fs = File.Create(file))
                    {
                        var copy = p.StandardOutput.BaseStream.CopyToAsync(fs);
                        p.StandardError.ReadToEndAsync();
                        if (!copy.Wait(20000)) { try { p.Kill(); } catch { } return false; }
                        p.WaitForExit(5000);
                    }
                    return new FileInfo(file).Length > 1000;
                }
                catch { return false; }
            });
            if (ok) { SetStatus("Screenshot saved: " + file); try { Process.Start("explorer.exe", "/select,\"" + file + "\""); } catch { } }
            else { try { File.Delete(file); } catch { } Err("The screenshot failed. Some apps (banking, DRM video) block screenshots."); }
        }

        async Task UsbToWifi(Row row)
        {
            if (Tools.Adb == null) { Err("adb was not found."); return; }
            var usb = (await Tools.DevicesAsync()).Where(d => d.State == "device" && !Parse.IsNetworkSerial(d.Serial)).ToList();
            if (usb.Count == 0)
            {
                var un = (await Tools.DevicesAsync()).Any(d => d.State == "unauthorized");
                Err(un ? "A phone is plugged in but hasn't allowed this PC yet. Unlock it and tap Allow on the USB debugging prompt."
                       : "No USB device found.\n\nPlug the phone in with a data cable, turn on USB debugging and unlock it. See Help > Phone setup guide.");
                return;
            }
            string serial = usb[0].Serial;
            if (row != null && row.Type == "usb") serial = row.Serial;
            else if (usb.Count > 1)
            {
                var sel = SelectedRow();
                if (sel != null && sel.Type == "usb") serial = sel.Serial;
                else { Err("Several USB devices are plugged in. Select the one you want in the list, then use 'Make wireless'."); return; }
            }
            var model = await Tools.GetPropAsync(serial, "ro.product.model"); if (model == "") model = "Android device";
            int port = store.AppInt("ScanPort", 5555);
            SetStatus("Reading Wi-Fi address of " + model + "...");
            var ip = await DeviceIp(serial);
            if (ip == null) { Err(model + " doesn't seem to be on Wi-Fi. Connect it to the same Wi-Fi as this PC and try again."); return; }
            SetStatus("Switching " + model + " to wireless mode...");
            await Tools.AdbAsync(10000, "-s", serial, "tcpip", port.ToString(CultureInfo.InvariantCulture));
            await Task.Delay(2500);
            if (await Tools.ConnectWifiAsync(ip + ":" + port) != "device")
            {
                Err("Wireless mode was enabled, but this PC couldn't reach " + model + " at " + ip + ":" + port + ".\n\nCheck both are on the same network (not guest Wi-Fi), then try 'Scan network'.");
                await RefreshDevices(); return;
            }
            autoWirelessTried.Add(serial);
            var existing = store.Devices.FirstOrDefault(d => d.Type == "wifi" && d.HwSerial == serial);
            if (existing != null)
            {
                existing.IP = ip; existing.Port = port; store.SaveDevices();
                SetStatus(existing.Name + " is on Wi-Fi at " + ip + ". You can unplug the cable.");
                await RefreshDevices(); SelectKey(existing.Id); return;
            }
            var rec = new DeviceRec { Name = model, Type = "wifi", IP = ip, Port = port, Profile = store.AppStr("LastProfile"), HwSerial = serial };
            var saved = AskSave(rec, "Connected wirelessly to " + model + " at " + ip + ". You can unplug the cable.\n\n" +
                (store.AppBool("AutoWireless") ? "If the phone restarts, plug it in once and CYSMS switches it back to Wi-Fi automatically." : "Wireless mode resets when the phone restarts."));
            await RefreshDevices();
            if (saved != null) SelectKey(saved.Id);
        }

        async Task Pair()
        {
            if (Tools.Adb == null) { Err("adb was not found."); return; }
            var r = Input("Pair a device over Wi-Fi",
                "On the phone: Settings > Developer options > Wireless debugging (turn on) > 'Pair device with pairing code'. Keep that screen open.",
                new[]
                {
                    new Field { Key = "PairAddr", Label = "Pairing IP address & port", Hint = "Shown in the pairing pop-up, e.g. 192.168.1.23:37105" },
                    new Field { Key = "Code", Label = "Pairing code", Hint = "6 digits, shown in the same pop-up" },
                    new Field { Key = "ConnAddr", Label = "Connect IP address & port (optional)", Hint = "Shown on the main Wireless debugging screen. Leave blank and CYSMS will try to find it." },
                }, "Pair", v =>
                {
                    if (!Regex.IsMatch(v["PairAddr"], @"^\d{1,3}(\.\d{1,3}){3}:\d+$")) return "The pairing address should look like 192.168.1.23:37105";
                    if (!Regex.IsMatch(v["Code"], @"^\d{6}$")) return "The pairing code is the 6 digits shown on the phone.";
                    if (v["ConnAddr"] != "" && !Regex.IsMatch(v["ConnAddr"], @"^\d{1,3}(\.\d{1,3}){3}:\d+$")) return "The connect address should look like 192.168.1.23:41235 (or leave it blank).";
                    return null;
                });
            if (r == null) return;
            var pm = Regex.Match(r["PairAddr"], @"^(\d{1,3}(?:\.\d{1,3}){3}):(\d+)$");
            var ip = pm.Groups[1].Value;
            SetStatus("Pairing...");
            var outp = await Tools.AdbAsync(25000, "pair", r["PairAddr"], r["Code"]);
            if (!outp.Contains("Successfully paired")) { Err("Pairing failed. Check the code hasn't expired (it changes each time the pop-up opens).\n\n" + outp.Trim()); return; }

            var conn = r["ConnAddr"];
            if (conn == "")
            {
                SetStatus("Looking for the connect port...");
                for (int i = 0; i < 4 && conn == ""; i++)
                {
                    var port = Parse.MdnsConnectPort(await Tools.AdbAsync(6000, "mdns", "services"), ip);
                    if (port != null) conn = ip + ":" + port; else await Task.Delay(700);
                }
            }
            if (conn == "")
            {
                var r2 = Input("One more step", "Paired. Enter the IP address & port shown at the top of the Wireless debugging screen (it is different from the pairing port).",
                    new[] { new Field { Key = "ConnAddr", Label = "Connect IP address & port", Default = ip + ":" } }, "Connect");
                if (r2 == null || !Regex.IsMatch(r2["ConnAddr"], @"^\d{1,3}(\.\d{1,3}){3}:\d+$")) return;
                conn = r2["ConnAddr"];
            }
            SetStatus("Connecting to " + conn + "...");
            if (await Tools.ConnectWifiAsync(conn) != "device") { Err("Paired, but couldn't connect to " + conn + ". Check the address on the Wireless debugging screen."); return; }
            var model = await Tools.GetPropAsync(conn, "ro.product.model"); if (model == "") model = "Android device";
            var hw = await Tools.GetPropAsync(conn, "ro.serialno");

            int fixedPort = store.AppInt("ScanPort", 5555), finalPort;
            SetStatus("Setting a fixed wireless port...");
            await Tools.AdbAsync(10000, "-s", conn, "tcpip", fixedPort.ToString(CultureInfo.InvariantCulture));
            await Task.Delay(2500);
            string note = "";
            if (await Tools.ConnectWifiAsync(ip + ":" + fixedPort) == "device") finalPort = fixedPort;
            else
            {
                finalPort = int.Parse(conn.Split(':')[1], CultureInfo.InvariantCulture);
                await Tools.ConnectWifiAsync(conn);
                note = "\n\nNote: this phone kept its changing port (" + finalPort + "). If it shows Offline later, pair again or use 'Make wireless' from USB.";
            }
            var rec = new DeviceRec { Name = model, Type = "wifi", IP = ip, Port = finalPort, Profile = store.AppStr("LastProfile"), HwSerial = hw };
            var saved = AskSave(rec, "Paired and connected to " + model + " at " + ip + ":" + finalPort + "." + note);
            await RefreshDevices();
            if (saved != null) SelectKey(saved.Id);
        }

        async Task AddByIp()
        {
            var r = Input("Add a device by IP address", "For a phone that is already in wireless mode (e.g. after 'adb tcpip 5555').",
                new[]
                {
                    new Field { Key = "Name", Label = "Name", Default = "My phone" },
                    new Field { Key = "IP", Label = "IP address", Hint = "Phone: Settings > About phone > Status > IP address" },
                    new Field { Key = "Port", Label = "Port", Default = store.AppInt("ScanPort", 5555).ToString(CultureInfo.InvariantCulture) },
                }, "Add", v =>
                {
                    if (!Regex.IsMatch(v["IP"], @"^\d{1,3}(\.\d{1,3}){3}$") && !Regex.IsMatch(v["IP"], @"^[A-Za-z0-9][A-Za-z0-9.-]*$")) return "That IP address doesn't look right, e.g. 192.168.1.23";
                    if (!int.TryParse(v["Port"], out int p) || p < 1 || p > 65535) return "Port must be a number (usually 5555).";
                    return null;
                });
            if (r == null) return;
            int port = int.Parse(r["Port"], CultureInfo.InvariantCulture);
            var rec = store.AddOrUpdate(new DeviceRec { Name = r["Name"] != "" ? r["Name"] : r["IP"], Type = "wifi", IP = r["IP"], Port = port, Profile = store.AppStr("LastProfile") });
            SetStatus("Checking " + r["IP"] + "...");
            var res = await Tools.ConnectWifiAsync(rec.AdbSerial);
            await RefreshDevices(); SelectKey(rec.Id);
            if (res == "unauthorized") Err("Saved. The phone is asking for permission - allow it on the phone.");
            else SetStatus(res == "device" ? "Saved and connected: " + rec.Name : "Saved " + rec.Name + " – not reachable right now");
        }

        bool ScanNotice()
        {
            if (store.AppBool("HideScanNotice")) return true;
            using (var f = DialogForm("Before scanning", 460))
            {
                var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 1 }; f.Controls.Add(t);
                t.Controls.Add(Lbl("Scan your network for phones", FontH2));
                var txt = Lbl("CYSMS will check this PC's local network for phones listening on wireless adb port " + store.AppInt("ScanPort", 5555) + ".\n\n" +
                    "It only finds phones that are ALREADY in wireless mode. A brand-new phone needs a one-time setup that CYSMS can't do from here:\n" +
                    "• Developer options and USB debugging turned on, and\n" +
                    "• either 'Make wireless' once over USB, or 'Pair with code' (Android 11+).\n\n" +
                    "Wireless mode resets when a phone restarts. Guest Wi-Fi and some office networks block scanning.");
                txt.MaximumSize = new Size(S(440), 0); t.Controls.Add(txt);
                var chk = new CheckBox { Text = "Don't show this again", AutoSize = true, Margin = new Padding(0, S(10), 0, 0) };
                t.Controls.Add(chk);
                var ok = Btn("Scan now", true); ok.DialogResult = DialogResult.OK;
                var guide = Btn("Setup guide"); guide.Click += (s, e) => TextWindow("CYSMS – Phone setup guide", SetupGuide);
                var cancel = Btn("Cancel"); cancel.DialogResult = DialogResult.Cancel;
                t.Controls.Add(ButtonBar(cancel, ok, guide));
                f.AcceptButton = ok; f.CancelButton = cancel;
                var res = f.ShowDialog(this);
                if (res == DialogResult.OK && chk.Checked) { store.App["HideScanNotice"] = true; store.SaveApp(); }
                return res == DialogResult.OK;
            }
        }

        async Task Scan()
        {
            if (Tools.Adb == null) { Err("adb was not found."); return; }
            if (!ScanNotice()) return;
            var bases = Tools.LocalSubnets();
            if (bases.Count == 0) { Err("Couldn't find an active network on this PC. Connect to Wi-Fi or Ethernet and try again."); return; }
            int port = store.AppInt("ScanPort", 5555);
            var hits = new List<string>();
            foreach (var b in bases.Take(3))
            {
                SetStatus("Scanning " + b + ".0/24 for phones on port " + port + "...");
                hits.AddRange(await Tools.ScanSubnetAsync(b, port));
            }
            var results = new List<(string ip, string model, string state, string hw)>();
            foreach (var ip in hits)
            {
                var serial = ip + ":" + port;
                if (store.Find("wifi", serial) != null) continue;
                SetStatus("Checking " + serial + "...");
                var st = await Tools.ConnectWifiAsync(serial);
                if (st == "failed") continue;
                var model = st == "device" ? await Tools.GetPropAsync(serial, "ro.product.model") : "";
                var hw = st == "device" ? await Tools.GetPropAsync(serial, "ro.serialno") : "";
                results.Add((ip, model == "" ? "Android device" : model, st, hw));
            }
            await RefreshDevices();
            SetStatus("Scan finished");
            if (results.Count == 0)
            {
                Msg(hits.Count > 0 ? "Scan finished. Only phones you have already saved were found."
                    : "No new phones found.\n\nPhones must already be in wireless mode. Use 'Make wireless' (USB) or 'Pair with code' for first-time setup.", "Scan complete");
                return;
            }
            using (var f = DialogForm("Phones found", 420))
            {
                var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 1 }; f.Controls.Add(t);
                t.Controls.Add(Lbl("Found " + results.Count + " new phone(s). Tick the ones to save:", FontBold));
                var cl = new CheckedListBox { Width = S(400), Height = S(140), CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle };
                foreach (var x in results) cl.Items.Add(x.model + " – " + x.ip + (x.state == "unauthorized" ? " (allow on phone)" : ""), true);
                t.Controls.Add(cl);
                var ok = Btn("Save selected", true); ok.DialogResult = DialogResult.OK;
                var cancel = Btn("Close"); cancel.DialogResult = DialogResult.Cancel;
                t.Controls.Add(ButtonBar(cancel, ok));
                f.AcceptButton = ok; f.CancelButton = cancel;
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    foreach (int i in cl.CheckedIndices)
                    {
                        var x = results[i];
                        store.AddOrUpdate(new DeviceRec { Name = x.model, Type = "wifi", IP = x.ip, Port = port, Profile = store.AppStr("LastProfile"), HwSerial = x.hw });
                    }
                    SetStatus("Saved " + cl.CheckedIndices.Count + " device(s). Rename them with F2.");
                }
            }
            await RefreshDevices();
        }

        // =================================================================== lifecycle
        async Task OnShown()
        {
            try
            {
                split.Panel1MinSize = S(25); split.Panel2MinSize = S(25);
            }
            catch { }
            EnsureOnScreen();
            LoadProfileIntoUI(activeProfile);
            ApplyMode();
            SetStatus("Looking for scrcpy and devices...");
            busy = true;
            try
            {
                InitTools();
                await Task.Run(() => Tools.StartAdbServer());
                await FillVersion();
                await RefreshDevices();
            }
            catch (Exception ex) { Err("Startup problem: " + ex.Message); }
            finally { busy = false; }
            lastSelKey = null;
            OnSelectionChanged();
            var ready = rows.Where(r => r.State == "connected" || r.State == "online").ToList();
            SetStatus(rows.Count == 0 ? "No devices yet – plug one in or use Add device"
                : rows.Count + " device(s), " + ready.Count + " ready. Select one and press Connect.");
            if (Program.DemoShots) { await DemoScreenshots(); return; }
            CheckWindows();
            if (!store.AppBool("TourSeen")) ShowTour();
            if (store.AppBool("AutoConnectSingle") && ready.Count == 1 && Tools.Scrcpy != null)
            {
                SelectKey(ready[0].Key);
                RunAction(() => ConnectRow(ready[0], false));
            }
            timer.Start();
        }

        bool inTick;
        async Task OnTick()
        {
            if (inTick) return;
            inTick = true;
            try
            {
                if (Program.ShowRequested) { Program.ShowRequested = false; ShowMain(); }
                if (Visible) EnsureOnScreen();
                if (busy || editingLabel) return;
                await CheckSessions();
                if (++tick >= Math.Max(3, store.AppInt("RefreshSeconds", 6)))
                {
                    tick = 0;
                    await RefreshDevices();
                }
            }
            catch { }
            finally { inTick = false; }
        }
    }
}
