// CYSMS - locating and running scrcpy / adb without blocking the UI.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cysms
{
    public static class Tools
    {
        public static string AppDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        public static string Scrcpy, Adb, ScrcpyVersion = "";
        public static bool IsBundled;
        public static string BundledProblem;   // why the included scrcpy can't run on this PC (null = it can)

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);

        // The PC's real CPU architecture, also when CYSMS itself runs under emulation: x64, x86, ARM64
        public static string NativeArch()
        {
            try
            {
                if (IsWow64Process2(Process.GetCurrentProcess().Handle, out _, out var native))
                {
                    if (native == 0xAA64) return "ARM64";
                    if (native == 0x8664) return "x64";
                    if (native == 0x014C) return "x86";
                }
            }
            catch { }   // IsWow64Process2 needs Windows 10 1709+
            var a = (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ?? Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "").ToUpperInvariant();
            return a == "ARM64" ? "ARM64" : a == "AMD64" ? "x64" : a == "X86" ? "x86" : a;
        }

        // ---------------------------------------------------------------- find
        public static void Locate(string userPath, bool preferBundled)
        {
            var bundled = Path.Combine(AppDir, "scrcpy", "scrcpy.exe");
            var cands = new List<string>();
            if (!string.IsNullOrEmpty(userPath)) cands.Add(userPath);
            // The bundled scrcpy is the 64-bit (x64) build: skip it where Windows can't run it
            BundledProblem = Compat.BundledRuns(Environment.OSVersion.Version, Environment.Is64BitOperatingSystem, NativeArch()) ? null
                : !Environment.Is64BitOperatingSystem ? "32-bit Windows" : "Windows 10 on ARM";
            if (BundledProblem == null) { if (preferBundled) cands.Insert(0, bundled); else cands.Add(bundled); }
            cands.Add(Path.Combine(AppDir, "scrcpy.exe"));
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try { if (dir.Trim() != "") cands.Add(Path.Combine(dir.Trim(), "scrcpy.exe")); } catch { }
            }
            var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (!string.IsNullOrEmpty(local))
            {
                var pk = Path.Combine(local, "Microsoft", "WinGet", "Packages");
                try
                {
                    if (Directory.Exists(pk))
                        foreach (var d in Directory.GetDirectories(pk, "Genymobile.scrcpy*"))
                            cands.AddRange(Directory.GetFiles(d, "scrcpy.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTime));
                }
                catch { }
            }
            Scrcpy = null; Adb = null; IsBundled = false;
            foreach (var c in cands)
            {
                try
                {
                    if (!File.Exists(c)) continue;
                    // WinGet "Links" are symlinks with no adb next to them: prefer real install folders
                    var fi = new FileInfo(c);
                    if ((fi.Attributes & FileAttributes.ReparsePoint) != 0 && !File.Exists(Path.Combine(fi.DirectoryName, "adb.exe"))) continue;
                    Scrcpy = fi.FullName;
                    IsBundled = string.Equals(Scrcpy, bundled, StringComparison.OrdinalIgnoreCase);
                    break;
                }
                catch { }
            }
            if (Scrcpy != null)
            {
                var a = Path.Combine(Path.GetDirectoryName(Scrcpy), "adb.exe");
                if (File.Exists(a)) Adb = a;
            }
            if (Adb == null)
                foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                {
                    try { var a = Path.Combine(dir.Trim(), "adb.exe"); if (dir.Trim() != "" && File.Exists(a)) { Adb = a; break; } } catch { }
                }
        }

        // ---------------------------------------------------------------- run
        public class RunResult { public string Output = ""; public int ExitCode = -1; public bool TimedOut; }

        public static Task<RunResult> RunAsync(string exe, IEnumerable<string> args, int timeoutMs = 15000)
        {
            return Task.Run(() =>
            {
                var r = new RunResult();
                if (string.IsNullOrEmpty(exe)) { r.Output = "(tool not found)"; return r; }
                var psi = new ProcessStartInfo(exe, Schema.JoinArgs(args))
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                };
                if (Adb != null) psi.EnvironmentVariables["ADB"] = Adb;
                var sb = new StringBuilder();
                var gate = new object();
                try
                {
                    using (var p = new Process { StartInfo = psi })
                    {
                        p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (gate) sb.AppendLine(e.Data); };
                        p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (gate) sb.AppendLine(e.Data); };
                        p.Start();
                        try { p.StandardInput.Close(); } catch { }
                        p.BeginOutputReadLine(); p.BeginErrorReadLine();
                        if (!p.WaitForExit(timeoutMs))
                        {
                            r.TimedOut = true;
                            try { p.Kill(); } catch { }
                        }
                        else p.WaitForExit();   // flush async readers
                        r.ExitCode = r.TimedOut ? -1 : p.ExitCode;
                    }
                }
                catch (Exception ex) { lock (gate) sb.AppendLine("(could not run: " + ex.Message + ")"); }
                lock (gate) r.Output = sb.ToString();
                if (r.TimedOut) r.Output += "\n(timed out)";
                return r;
            });
        }

        public static async Task<string> AdbAsync(int timeoutMs, params string[] args) => (await RunAsync(Adb, args, timeoutMs)).Output;

        public static Task<string> AdbShellAsync(string serial, string cmd, int timeoutMs = 8000) =>
            AdbAsync(timeoutMs, "-s", serial, "shell", cmd);

        public static async Task<List<AdbDev>> DevicesAsync()
        {
            if (Adb == null) return new List<AdbDev>();
            return Parse.AdbDevices(await AdbAsync(8000, "devices", "-l"));
        }

        public static async Task<string> StateAsync(string serial)
        {
            foreach (var d in await DevicesAsync()) if (d.Serial == serial) return d.State;
            return null;
        }

        public static async Task<string> GetPropAsync(string serial, string prop)
        {
            var o = (await AdbShellAsync(serial, "getprop " + prop)).Trim();
            if (o.Contains("error") || o.Contains("not found") || o.Contains("offline") || o.Contains("(timed out)")) return "";
            return Parse.Lines(o).FirstOrDefault()?.Trim() ?? "";
        }

        // Returns: device | unauthorized | failed
        public static async Task<string> ConnectWifiAsync(string serial)
        {
            var st = await StateAsync(serial);
            if (st == "device") return "device";
            if (st == "offline") await AdbAsync(5000, "disconnect", serial);
            var outp = await AdbAsync(12000, "connect", serial);
            for (int i = 0; i < 6; i++)
            {
                st = await StateAsync(serial);
                if (st == "device") return "device";
                if (st == "unauthorized") return "unauthorized";
                if (outp.Contains("failed") || outp.Contains("cannot") || outp.Contains("refused") || outp.Contains("timed out")) break;
                await Task.Delay(300);
            }
            if (outp.Contains("authenticate")) return "unauthorized";
            return "failed";
        }

        public static void StartAdbServer()
        {
            if (Adb == null) return;
            try
            {
                var psi = new ProcessStartInfo(Adb, "start-server") { UseShellExecute = false, CreateNoWindow = true };
                using (var p = Process.Start(psi)) p.WaitForExit(10000);
            }
            catch { }
        }

        // ---------------------------------------------------------------- network
        public static List<string> LocalSubnets()
        {
            var res = new List<string>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var props = nic.GetIPProperties();
                    if (!props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && g.Address.ToString() != "0.0.0.0")) continue;
                    foreach (var u in props.UnicastAddresses)
                    {
                        if (u.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var ip = u.Address.ToString();
                        if (ip.StartsWith("169.254.") || ip.StartsWith("127.")) continue;
                        var o = ip.Split('.');
                        var b = o[0] + "." + o[1] + "." + o[2];
                        if (!res.Contains(b)) res.Add(b);
                    }
                }
            }
            catch { }
            return res;
        }

        public static async Task<bool> ProbeAsync(string host, int port, int timeoutMs)
        {
            using (var c = new TcpClient())
            {
                try
                {
                    var t = c.ConnectAsync(host, port);
                    var done = await Task.WhenAny(t, Task.Delay(timeoutMs));
                    return done == t && !t.IsFaulted && c.Connected;
                }
                catch { return false; }
            }
        }

        public static async Task<List<string>> ScanSubnetAsync(string baseIp, int port)
        {
            var tasks = Enumerable.Range(1, 254).Select(async i =>
            {
                var ip = baseIp + "." + i;
                return await ProbeAsync(ip, port, 400) ? ip : null;
            }).ToList();
            var res = await Task.WhenAll(tasks);
            return res.Where(x => x != null).ToList();
        }
    }

    // A running scrcpy mirroring session
    public class Session
    {
        public string Key, Name, LogPath;
        public Process Proc;
        public DateTime Start = DateTime.Now;
        public bool Verified, Record, Retried, UserStopped, Locked;
        public string Serial;
        public Dictionary<string, object> Settings;
        public string Profile;
        StreamWriter log;
        readonly object gate = new object();

        public static Session Launch(string key, string name, List<string> args, string logPath, bool record)
        {
            var s = new Session { Key = key, Name = name, LogPath = logPath, Record = record };
            Directory.CreateDirectory(Path.GetDirectoryName(logPath));
            s.log = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true };
            s.log.WriteLine("> " + Schema.JoinArgs(new[] { Tools.Scrcpy }.Concat(args)));
            var psi = new ProcessStartInfo(Tools.Scrcpy, Schema.JoinArgs(args))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                WorkingDirectory = Path.GetDirectoryName(Tools.Scrcpy),
            };
            if (Tools.Adb != null) psi.EnvironmentVariables["ADB"] = Tools.Adb;
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (o, e) => s.Write(e.Data);
            p.ErrorDataReceived += (o, e) => s.Write(e.Data);
            p.Start();
            try { p.StandardInput.Close(); } catch { }
            p.BeginOutputReadLine(); p.BeginErrorReadLine();
            s.Proc = p;
            return s;
        }

        void Write(string line)
        {
            if (line == null) return;
            lock (gate) { try { log?.WriteLine(line); } catch { } }
        }

        public bool Exited { get { try { return Proc.HasExited; } catch { return true; } } }

        public int ExitCode { get { try { return Proc.HasExited ? Proc.ExitCode : 0; } catch { return -1; } } }

        public void Stop()
        {
            UserStopped = true;
            try { if (!Proc.HasExited) Proc.Kill(); } catch { }
        }

        public void CloseLog() { lock (gate) { try { log?.Dispose(); } catch { } log = null; } }

        public string ReadLog()
        {
            try
            {
                using (var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs)) return sr.ReadToEnd();
            }
            catch { return ""; }
        }
    }
}
