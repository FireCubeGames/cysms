// CYSMS - entry point: single instance, data folder, error handling.
using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Cysms
{
    static class Program
    {
        public static volatile bool ShowRequested;
        public static bool RestartRequested;

        // Demo mode (--demo, or a file named CYSMS.demo next to the exe) shows sample devices from a throw-away
        // data folder, for screenshots. With the file, it also saves screenshots to a "screenshots" folder and exits.
        public static bool Demo, DemoShots;

        [STAThread]
        static void Main(string[] args)
        {
            var flag = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CYSMS.demo");
            DemoShots = File.Exists(flag);
            Demo = DemoShots || Array.IndexOf(args, "--demo") >= 0;
            bool created;
            using (var mutex = new Mutex(true, Demo ? @"Local\CYSMS_Native_Demo" : @"Local\CYSMS_Native_SingleInstance", out created))
            using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Demo ? @"Local\CYSMS_Native_DemoShow" : @"Local\CYSMS_Native_Show"))
            {
                if (!created) { showEvent.Set(); return; }   // already running: bring it forward

                var waiter = new Thread(() => { while (true) { showEvent.WaitOne(); ShowRequested = true; } }) { IsBackground = true };
                waiter.Start();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var dataDir = Path.Combine(local, "CYSMS");
                if (Demo)
                {
                    dataDir = Path.Combine(Path.GetTempPath(), "CYSMS-demo");
                    try { if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true); } catch { }
                }
                var logDir = Path.Combine(dataDir, "logs");
                Directory.CreateDirectory(logDir);
                CleanLogs(logDir);
                Schema.DefaultRecordDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "CYSMS");

                Application.ThreadException += (s, e) => Report(logDir, e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Report(logDir, e.ExceptionObject as Exception);

                Ui.Init();
                var store = new Store(dataDir);
                store.Load(Demo ? null : Path.Combine(local, "ScrcpyApp", "devices.json"));
                if (Demo) { store.App["TourSeen"] = true; store.App["AutoWireless"] = false; store.App["MinimizeToTray"] = false; }
                Ui.ApplyTheme(store.AppStr("Theme"));
                Stickers.Load(Demo ? "" : store.AppStr("StickerFolder"));
                Application.Run(new MainForm(store, logDir));
            }
            // Restart after the single-instance lock is released (e.g. theme change)
            if (RestartRequested) { try { System.Diagnostics.Process.Start(Application.ExecutablePath); } catch { } }
        }

        static void Report(string logDir, Exception ex)
        {
            if (ex == null) return;
            try { File.AppendAllText(Path.Combine(logDir, "cysms-errors.log"), DateTime.Now.ToString("s") + "  " + ex + "\r\n\r\n"); } catch { }
            try { MessageBox.Show("Something went wrong:\n\n" + ex.Message + "\n\nDetails were saved to cysms-errors.log in the logs folder.", "CYSMS", MessageBoxButtons.OK, MessageBoxIcon.Warning); } catch { }
        }

        static void CleanLogs(string dir)
        {
            try
            {
                var files = new DirectoryInfo(dir).GetFiles("*.log");
                Array.Sort(files, (a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
                for (int i = 40; i < files.Length; i++) if (files[i].Name != "cysms-errors.log") files[i].Delete();
            }
            catch { }
        }
    }
}
