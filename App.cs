using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace YtDlpGui
{
    /// <summary>
    /// Global paths and dependency discovery. Everything is resolved relative to the
    /// executable folder first so the whole thing stays portable.
    /// </summary>
    internal static class App
    {
        public const string Title = "yt-dlp GUI";

        public static string BaseDir { get; private set; }
        public static string DataDir { get; private set; }
        public static string YtDlpPath { get; private set; }
        public static string FfmpegPath { get; private set; }
        public static string FfprobePath { get; private set; }
        public static string DenoPath { get; private set; }

        private static readonly string[] YtDlpNames =
        {
            "yt-dlp.exe", "yt-dlp_x86.exe", "yt-dlp_x64.exe", "yt-dlp_min.exe", "youtube-dl.exe"
        };

        public static void Init()
        {
            BaseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            // Portable when possible: keep settings beside the exe, fall back to %APPDATA%.
            DataDir = BaseDir;
            if (!IsWritable(BaseDir))
            {
                DataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "yt-dlp-gui");
                Directory.CreateDirectory(DataDir);
            }

            Rescan();
        }

        public static void Rescan()
        {
            YtDlpPath = FindTool(YtDlpNames);
            FfmpegPath = FindTool(new[] { "ffmpeg.exe" });
            FfprobePath = FindTool(new[] { "ffprobe.exe" });
            DenoPath = FindTool(new[] { "deno.exe", "node.exe", "bun.exe" });
        }

        /// <summary>Looks in the app folder, then any bin/ subfolder, then PATH.</summary>
        private static string FindTool(string[] names)
        {
            foreach (var name in names)
            {
                var local = Path.Combine(BaseDir, name);
                if (File.Exists(local)) return local;

                var bin = Path.Combine(Path.Combine(BaseDir, "bin"), name);
                if (File.Exists(bin)) return bin;
            }

            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathVar.Split(Path.PathSeparator))
            {
                if (string.IsNullOrEmpty(dir)) continue;
                foreach (var name in names)
                {
                    try
                    {
                        var candidate = Path.Combine(dir.Trim(), name);
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch { /* malformed PATH entry */ }
                }
            }
            return null;
        }

        public static bool HasYtDlp { get { return !string.IsNullOrEmpty(YtDlpPath); } }
        public static bool HasFfmpeg { get { return !string.IsNullOrEmpty(FfmpegPath); } }
        public static bool HasJsRuntime { get { return !string.IsNullOrEmpty(DenoPath); } }

        // ---- downloader capabilities ------------------------------------------
        public static string YtDlpVersion { get; private set; }

        /// <summary>
        /// Arguments the GUI adds for its own benefit rather than because the user asked for
        /// them. If a yt-dlp too old to know one of these is in use, it is switched off and
        /// the run is repeated - the alternative is an unexplained failure on every download.
        /// </summary>
        private static readonly HashSet<string> OptionalArgs =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "--progress-template", "--progress-delta" };

        private static readonly HashSet<string> DisabledArgs =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static bool SupportsProgressTemplate
        {
            get { return !DisabledArgs.Contains("--progress-template"); }
        }

        public static bool SupportsProgressDelta
        {
            get { return !DisabledArgs.Contains("--progress-delta"); }
        }

        /// <summary>True when the option was one of ours and was not already switched off.</summary>
        public static bool DisableOptionalArg(string option)
        {
            if (string.IsNullOrEmpty(option) || !OptionalArgs.Contains(option)) return false;
            return DisabledArgs.Add(option);
        }

        public static bool IsDisabledOptionalArg(string arg)
        {
            return !string.IsNullOrEmpty(arg) && DisabledArgs.Contains(arg);
        }

        /// <summary>
        /// Records the reported version and, when it is older than the release that introduced
        /// them, switches off the options that would fail.
        /// </summary>
        public static void SetVersion(string version)
        {
            if (string.IsNullOrEmpty(version)) return;
            YtDlpVersion = version.Trim();

            // yt-dlp versions are YYYY.MM.DD. Anything that does not parse is left alone: a
            // fork or a nightly is far more likely to be new than ancient.
            var parts = YtDlpVersion.Split('.');
            int y, m, d;
            if (parts.Length < 3 ||
                !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out y) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out m) ||
                !int.TryParse(new string(TakeDigits(parts[2])), NumberStyles.Integer, CultureInfo.InvariantCulture, out d))
                return;

            var released = new DateTime(Math.Max(2000, Math.Min(9999, y)),
                                        Math.Max(1, Math.Min(12, m)),
                                        Math.Max(1, Math.Min(28, d)));

            if (released < new DateTime(2021, 4, 11)) DisabledArgs.Add("--progress-template");
            if (released < new DateTime(2024, 5, 1)) DisabledArgs.Add("--progress-delta");
        }

        private static char[] TakeDigits(string s)
        {
            var chars = new List<char>();
            foreach (var c in s)
            {
                if (!char.IsDigit(c)) break;
                chars.Add(c);
            }
            if (chars.Count == 0) chars.Add('1');
            return chars.ToArray();
        }

        /// <summary>The --js-runtimes value for the runtime found next to the app, or null.</summary>
        public static string JsRuntimeArg
        {
            get
            {
                if (string.IsNullOrEmpty(DenoPath)) return null;
                var name = Path.GetFileNameWithoutExtension(DenoPath).ToLowerInvariant();
                return name + ":" + DenoPath;
            }
        }

        public static string SettingsFile { get { return Path.Combine(DataDir, "ytdlp-gui.settings"); } }
        public static string ProfilesDir
        {
            get
            {
                var d = Path.Combine(DataDir, "profiles");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        public static string DefaultDownloadDir
        {
            get
            {
                // Prefer the real Downloads folder; fall back to Desktop, then the app folder.
                try
                {
                    var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    var dl = Path.Combine(profile, "Downloads");
                    if (Directory.Exists(dl)) return dl;
                }
                catch { }
                try { return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); }
                catch { return BaseDir; }
            }
        }

        private static bool IsWritable(string dir)
        {
            try
            {
                var probe = Path.Combine(dir, ".write-probe-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        public static void OpenFolder(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) path = BaseDir;
                if (Directory.Exists(path)) Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch { }
        }

        /// <summary>Opens a file with its associated program, selecting it in Explorer if it is absent.</summary>
        public static void OpenFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                if (File.Exists(path)) Process.Start("notepad.exe", "\"" + path + "\"");
                else OpenFolder(Path.GetDirectoryName(path));
            }
            catch { }
        }

        public static void OpenUrl(string url)
        {
            // Only ever hand the shell something that is unmistakably a web address.
            try
            {
                if (string.IsNullOrEmpty(url)) return;
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
                Process.Start(url);
            }
            catch { }
        }
    }
}
