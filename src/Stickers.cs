// CYSMS - sticker pack: images from a folder the user chooses, shown at matching moments.
// Nothing is bundled; files are read from the user's own folder at run time.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace Cysms
{
    public static class Stickers
    {
        // Moments and the filename words that match them (case-insensitive, any word matches)
        public static readonly Dictionary<string, string[]> Moods = new Dictionary<string, string[]>
        {
            { "start", new[] { "sparkle", "enthusiastic", "excited", "happy", "hoot", "cape", "yay", "cheer" } },
            { "fail",  new[] { "shock", "scream", "rage", "angry", "sad", "stress", "disgust", "cry" } },
            { "idle",  new[] { "snore", "sleep", "tired", "flat", "bored", "zzz" } },
            { "busy",  new[] { "rotat", "spin", "loading", "think", "eat" } },
            { "ready", new[] { "headpat", "blush", "glance", "backpack", "wave", "unimpressed", "crossed" } },
        };

        public static readonly string[] Extensions = { ".png", ".gif", ".jpg", ".jpeg", ".bmp", ".webp" };

        static readonly Random rnd = new Random();
        static List<string> files = new List<string>();
        public static string Folder = "";
        public static int Count => files.Count;

        public static void Load(string folder)
        {
            Folder = folder ?? "";
            files = new List<string>();
            try
            {
                if (Folder != "" && Directory.Exists(Folder))
                    files = Directory.GetFiles(Folder).Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f).ToList();
            }
            catch { files = new List<string>(); }
        }

        static string NameOf(string f) => Path.GetFileNameWithoutExtension(f).ToLowerInvariant();

        public static List<string> Matching(string mood)
        {
            if (!Moods.TryGetValue(mood, out var words)) return new List<string>();
            return files.Where(f => words.Any(w => NameOf(f).Contains(w))).ToList();
        }

        // A random file for the mood, or any file when allowAny and nothing matches.
        public static string Pick(string mood, bool allowAny = false, string avoid = null)
        {
            var m = Matching(mood);
            if (m.Count == 0 && allowAny) m = files.ToList();
            if (m.Count > 1 && avoid != null) m.Remove(avoid);
            return m.Count == 0 ? null : m[rnd.Next(m.Count)];
        }

        public static string PickAny(string avoid = null) => Pick("", true, avoid);

        // Loads an image without locking the file. GIFs keep their animation.
        // WebP goes through Windows Imaging Component (built into Windows 11; Windows 10 needs the WebP Image Extension).
        public static Image LoadImage(string path)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".webp") return DecodeWithWic(bytes);
                var ms = new MemoryStream(bytes);   // must stay open for GDI+ (animated GIF)
                return Image.FromStream(ms);
            }
            catch { return null; }
        }

        static Image DecodeWithWic(byte[] bytes)
        {
            using (var input = new MemoryStream(bytes))
            {
                var dec = System.Windows.Media.Imaging.BitmapDecoder.Create(input,
                    System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(dec.Frames[0]));
                var png = new MemoryStream();
                enc.Save(png);
                png.Position = 0;
                return Image.FromStream(png);
            }
        }

        public static string Describe()
        {
            if (Folder == "") return "No sticker folder chosen.";
            if (!Directory.Exists(Folder)) return "The sticker folder was not found.";
            var parts = Moods.Keys.Select(k => k + " " + Matching(k).Count);
            return Count + " images found (" + string.Join(", ", parts) + ").";
        }
    }
}
