using System; using System.Collections.Generic; using System.IO; using System.Linq; using Cysms;
static class T {
  static int fail = 0, pass = 0;
  static void Eq(string name, object got, object want) { if ($"{got}" == $"{want}") pass++; else { fail++; Console.WriteLine($"FAIL {name}\n  got:  {got}\n  want: {want}"); } }
  static int Main() {
    Schema.DefaultRecordDir = "/rec";
    var def = Schema.Merge(null);
    Eq("default args", string.Join(" ", Schema.BuildArgs(def, "192.168.50.71:5555", "Magic V6", false)),
      "--serial=192.168.50.71:5555 --max-size=1050 --max-fps=60 --video-bit-rate=10M --video-codec=h265 --no-audio --shortcut-mod=lalt --stay-awake --turn-screen-off --disable-screensaver --window-title=Magic V6");
    Eq("quote", Schema.JoinArgs(new[]{@"C:\Users\Tjiong Chern\scrcpy.exe","--window-title=Magic V6","a\"b",@"C:\x y\"}), "\"C:\\Users\\Tjiong Chern\\scrcpy.exe\" \"--window-title=Magic V6\" \"a\\\"b\" \"C:\\x y\\\\\"");
    var auto = Schema.Merge(new Dictionary<string,object>{{"AutoBitRate",true}});
    Eq("auto preview", string.Join(" ", Schema.BuildArgs(auto, "X", "T", true)), "--serial=X --max-size=<auto> --max-fps=60 --video-bit-rate=<auto> --video-codec=h265 --no-audio --shortcut-mod=lalt --stay-awake --turn-screen-off --disable-screensaver --window-title=T");
    Eq("auto usb native", string.Join(" ", Schema.BuildArgs(auto, "X", "T", false, 16, null, 0)).StartsWith("--serial=X --max-fps=60 --video-bit-rate=16M"), true);
    Eq("auto wifi 1600", string.Join(" ", Schema.BuildArgs(auto, "X", "T", false, 8, null, 1600)).StartsWith("--serial=X --max-size=1600 --max-fps=60 --video-bit-rate=8M"), true);
    Eq("automax", $"{Parse.AutoMaxSize(true,null)} {Parse.AutoMaxSize(false,866)} {Parse.AutoMaxSize(false,433)} {Parse.AutoMaxSize(false,null)} {Parse.AutoMaxSize(false,40)}", "0 0 1920 1600 1024");
    Eq("ssid cmd", Parse.PhoneSsid("Wifi is enabled\nWifi is connected to \"Tjiong Home 5G\"\nWifiInfo: SSID: \"Tjiong Home 5G\", BSSID: aa"), "Tjiong Home 5G");
    Eq("ssid dumpsys", Parse.PhoneSsid("mWifiInfo SSID: \"TRUVISOR-LAB\", BSSID: 11:22, MAC: x, Link speed: 866Mbps"), "TRUVISOR-LAB");
    Eq("ssid unknown", Parse.PhoneSsid("mWifiInfo SSID: <unknown ssid>, BSSID: 02:00"), null);
    Eq("pc ssid", Parse.PcSsid("There is 1 interface on the system:\r\n\r\n    Name                   : Wi-Fi\r\n    State                  : connected\r\n    SSID                   : Tjiong Home 5G\r\n    BSSID                  : aa:bb\r\n"), "Tjiong Home 5G");
    Eq("pc no wifi", Parse.PcSsid("The Wireless AutoConfig Service (wlansvc) is not running."), null);
    Eq("quality of default", Schema.QualityOf(Schema.Merge(null)), "Balanced");
    Eq("quality of auto", Schema.QualityOf(Schema.Merge(new Dictionary<string,object>{{"AutoBitRate",true},{"MaxSize",0}})), "Auto");
    Eq("quality custom", Schema.QualityOf(Schema.Merge(new Dictionary<string,object>{{"MaxSize",720}})), "Custom");
    Eq("auto real", string.Join(" ", Schema.BuildArgs(auto, "X", "T", false, 12)).Contains("--video-bit-rate=12M"), true);
    var cam = Schema.Merge(new Dictionary<string,object>{{"VideoSource","camera"},{"CameraSize","1920x1080"},{"MaxSize",0},{"ViewOnly",true}});
    Eq("camera", string.Join(" ", Schema.BuildArgs(cam, "X", "Cam", false)), "--serial=X --video-source=camera --max-fps=60 --video-bit-rate=10M --video-codec=h265 --camera-facing=back --camera-size=1920x1080 --no-audio --no-control --shortcut-mod=lalt --disable-screensaver --window-title=Cam");
    cam["CameraId"] = "2";
    Eq("camera id drops facing", string.Join(" ", Schema.BuildArgs(cam, "X", "Cam", false)).Contains("--camera-facing"), false);
    Eq("camera id present", string.Join(" ", Schema.BuildArgs(cam, "X", "Cam", false)).Contains("--camera-id=2"), true);
    var desk = Schema.Merge(new Dictionary<string,object>{{"NewDisplay",true},{"MaxSize",0},{"Audio",true},{"Keyboard","uhid"},{"TurnScreenOff",false},{"DisplayId","1"},{"StartApp","com.android.chrome"}});
    Eq("desktop", string.Join(" ", Schema.BuildArgs(desk, "X", "T", false)), "--serial=X --max-fps=60 --video-bit-rate=10M --video-codec=h265 --keyboard=uhid --shortcut-mod=lalt --stay-awake --disable-screensaver --start-app=com.android.chrome --no-vd-destroy-content --new-display=1920x1080/160 --window-title=T");
    var rec = Schema.Merge(new Dictionary<string,object>{{"Record",true},{"NoPlayback",true},{"ExtraArgs","--time-limit=60 \"--window-x=10\""}});
    Eq("record only", string.Join(" ", Schema.BuildArgs(rec, "X", "My Tab", false, null, new DateTime(2026,11,16,9,8,7))), "--serial=X --max-size=1050 --max-fps=60 --video-bit-rate=10M --video-codec=h265 --no-audio --shortcut-mod=lalt --stay-awake --turn-screen-off --disable-screensaver --no-playback --record=/rec/My Tab_20261116_090807.mp4 --time-limit=60 --window-x=10");
    var vo = Schema.Merge(new Dictionary<string,object>{{"Audio",true},{"AudioDup",true},{"ViewOnly",true}});
    Eq("view only", string.Join(" ", Schema.BuildArgs(vo, null, null, false)), "--max-size=1050 --max-fps=60 --video-bit-rate=10M --video-codec=h265 --audio-dup --no-control --shortcut-mod=lalt --disable-screensaver");
    var enc = Schema.Merge(new Dictionary<string,object>{{"VideoEncoder","c2.qti.hevc.encoder"}});
    Eq("encoder", string.Join(" ", Schema.BuildArgs(enc, null, null, false)).Contains("--video-codec=h265 --video-encoder=c2.qti.hevc.encoder"), true);

    // Parsers
    var d = Parse.AdbDevices("List of devices attached\r\nHA2KWPVA               device product:TB323FU_PRC model:TB323FU device:TB323FU transport_id:1\r\n192.168.50.71:5555 device product:PNM model:PNM_N49 device:x transport_id:4\r\nabc unauthorized usb:2 transport_id:5\r\n* daemon started successfully\r\n\r\n");
    Eq("adb count", d.Count, 3);
    Eq("adb rows", string.Join(",", d.Select(x => x.Serial+"|"+x.State+"|"+x.Model)), "HA2KWPVA|device|TB323FU,192.168.50.71:5555|device|PNM N49,abc|unauthorized|");
    Eq("adb none", Parse.AdbDevices("List of devices attached\r\n\r\n").Count, 0);
    Eq("netserial", $"{Parse.IsNetworkSerial("1.2.3.4:5555")} {Parse.IsNetworkSerial("adb-R5CW-abc._adb-tls-connect._tcp")} {Parse.IsNetworkSerial("HA2KWPVA")}", "True True False");
    Eq("ip route", Parse.IpFromRoute("10.0.0.0/8 dev rmnet0 proto kernel scope link src 10.1.2.3\n192.168.50.0/24 dev wlan0 proto kernel scope link src 192.168.50.71"), "192.168.50.71");
    Eq("ip addr", Parse.IpFromRoute("3: wlan0: <UP>\n    inet 192.168.1.9/24 brd 192.168.1.255 scope global wlan0"), "192.168.1.9");
    Eq("mdns", Parse.MdnsConnectPort("List of discovered mdns services\nadb-R5CW-abc\t_adb-tls-pairing._tcp\t192.168.50.68:37105\nadb-R5CW-abc\t_adb-tls-connect._tcp\t192.168.50.68:41235", "192.168.50.68"), 41235);
    Eq("battery", Parse.BatteryLevel("Current Battery Service state:\n  AC powered: false\n  status: 2\n  health: 2\n  level: 87\n  scale: 100"), 87);
    Eq("charging", Parse.BatteryCharging("  status: 2\n  level: 87"), true);
    Eq("link", Parse.WifiLinkSpeed("mWifiInfo SSID: \"x\", BSSID: y, MAC: z, Supplicant state: COMPLETED, Wi-Fi standard: 11ax, RSSI: -50, Link speed: 1201Mbps, Tx Link speed: 1201Mbps"), 1201);
    Eq("autobr", $"{Parse.AutoBitRate(true,null)} {Parse.AutoBitRate(false,null)} {Parse.AutoBitRate(false,866)} {Parse.AutoBitRate(false,72)} {Parse.AutoBitRate(false,24)}", "16 8 16 6 4");
    var encs = Parse.Encoders("[server] INFO: List of video encoders:\n    --video-codec=h264 --video-encoder=c2.qti.avc.encoder         (hw) [vendor]\n    --video-codec=h265 --video-encoder=c2.qti.hevc.encoder        (hw) [vendor]\n    --video-codec=h264 --video-encoder=c2.android.avc.encoder     (sw)");
    Eq("encoder label", encs[0].Label, "c2.qti.avc.encoder (h264, hw [vendor])");
    Eq("encoders", string.Join(";", encs.Select(e => e.Value+"/"+e.Extra)), "c2.qti.avc.encoder/h264;c2.qti.hevc.encoder/h265;c2.android.avc.encoder/h264");
    Eq("displays", string.Join(";", Parse.Displays("[server] INFO: List of displays:\n    --display-id=0    (1600x2560)\n    --display-id=2    (1920x1080)").Select(x => x.Value+"="+x.Label)), "0=Display 0 (1600x2560);2=Display 2 (1920x1080)");
    Eq("cameras", string.Join(";", Parse.Cameras("[server] INFO: List of cameras:\n    --camera-id=0    (back, 4000x3000, fps=[15, 30])\n    --camera-id=1    (front, 2592x1944, fps=[15, 30])").Select(x => x.Value+"="+x.Label)), "0=Camera 0 (back, 4000x3000, fps=[15, 30]);1=Camera 1 (front, 2592x1944, fps=[15, 30])");
    var apps = Parse.Apps("[server] INFO: List of apps:\n * Settings                  com.android.settings\n - Chrome                    com.android.chrome\n - YouTube Music             com.google.android.apps.youtube.music");
    Eq("apps", string.Join(";", apps.Select(x => x.Value)), "com.android.chrome;com.google.android.apps.youtube.music;com.android.settings");
    Eq("hint", Parse.LogHint("ERROR: Could not open video stream\nERROR: encoder error"), "This device may not support the chosen video codec or encoder. Try H.264 and a blank encoder in the Video tab, or lower Max size.");

    // JSON round trip
    var j = Json.Parse("{\"a\":[1,2.5,\"x\\\"y\\u0041\",true,null,{}],\"b\":{\"c\":-3}}");
    Eq("json", Json.Write(j).Replace("\n","").Replace(" ",""), "{\"a\":[1,2.5,\"x\\\"yA\",true,null,{}],\"b\":{\"c\":-3}}");

    // Store
    var dir = Path.Combine(Path.GetTempPath(), "cysms-test-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
    var legacy = Path.Combine(dir, "legacy.json");
    File.WriteAllText(legacy, "[{\"Name\":\"Magic V6\",\"IP\":\"192.168.50.71\",\"Port\":5555,\"NeedsTcpip\":false},{\"Name\":\"Y700\",\"IP\":\"192.168.50.67\",\"Port\":5555}]");
    var st = new Store(Path.Combine(dir, "data")); st.Load(legacy);
    Eq("profiles seeded", string.Join(",", st.Profiles.Select(p => p.Key)), "Default,Auto quality,Low bandwidth,High quality + audio,Desktop mode,Camera,Record");
    Eq("migrated", string.Join(",", st.Devices.Select(x => x.Name+"@"+x.AdbSerial)), "Magic V6@192.168.50.71:5555,Y700@192.168.50.67:5555");
    st.AddOrUpdate(new DeviceRec{Name="Renamed", IP="192.168.50.71", Port=5555});
    var st2 = new Store(st.Dir); st2.Load(legacy);
    Eq("reload/dedupe", string.Join(",", st2.Devices.Select(x => x.Name)), "Renamed,Y700");
    st2.Devices.Clear(); st2.SaveDevices(); var st3 = new Store(st.Dir); st3.Load(legacy);
    Eq("empty stays empty", st3.Devices.Count, 0);
    st3.SetProfile("Mine", Schema.Merge(new Dictionary<string,object>{{"MaxSize",720},{"Audio",true}})); st3.SaveProfiles();
    var st4 = new Store(st.Dir); st4.Load();
    Eq("profile rt", $"{st4.GetProfile("Mine")["MaxSize"]} {st4.GetProfile("Mine")["Audio"]} {st4.Profiles.Last().Key}", "720 True Mine");
    Eq("unknown profile", st4.GetProfile("nope")["MaxSize"], 1050);
    // Data from the PowerShell CYSMS version (same folder/format)
    var psDir = Path.Combine(dir, "ps"); Directory.CreateDirectory(psDir);
    File.WriteAllText(Path.Combine(psDir, "devices.json"), "[\n  {\n    \"Profile\":  \"Default\",\n    \"Name\":  \"Y700-LABS\",\n    \"Port\":  5555,\n    \"Type\":  \"wifi\",\n    \"IP\":  \"192.168.50.67\",\n    \"Id\":  \"abc\",\n    \"Serial\":  \"\"\n  }\n]");
    File.WriteAllText(Path.Combine(psDir, "profiles.json"), "{\n  \"Default\": {\"MaxSize\": 1050, \"Audio\": false, \"VideoCodec\": \"h265\"},\n  \"Mine\": {\"MaxSize\": 800}\n}");
    var st5 = new Store(psDir); st5.Load();
    Eq("ps data", $"{st5.Devices[0].Name} {st5.Devices[0].AdbSerial} {string.Join(",", st5.Profiles.Select(p=>p.Key))} {st5.GetProfile("Mine")["MaxSize"]}", "Y700-LABS 192.168.50.67:5555 Default,Auto quality,Mine 800");
    var st6 = new Store(psDir); st6.Load(); Eq("seed once", string.Join(",", st6.Profiles.Select(p=>p.Key)), "Default,Auto quality,Mine");
    Directory.Delete(dir, true);
    Console.WriteLine($"\npassed {pass}, failed {fail}");
    return fail == 0 ? 0 : 1;
  }
}
