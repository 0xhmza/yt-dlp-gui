using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace YtDlpGui
{
    internal enum LineKind { Normal, Error, Warning, Info, Progress, Gui }

    internal enum ProgressKind
    {
        /// <summary>Byte progress for the file currently being transferred.</summary>
        File,
        /// <summary>"Downloading item N of M" - a new playlist entry has started.</summary>
        Item,
        /// <summary>A post-processor (merge, extract audio, embed) is working.</summary>
        Post
    }

    internal class ProgressInfo
    {
        public ProgressKind Kind = ProgressKind.File;

        public string Status;              // "downloading" / "finished"
        public bool Finished;

        public long Downloaded = -1;
        public long Total = -1;            // exact size, or the estimate when that is all there is
        public bool TotalIsEstimate;
        public double Speed = -1;          // bytes per second
        public double Eta = -1;            // seconds
        public double Elapsed = -1;        // seconds

        public int FragIndex = -1, FragCount = -1;
        public int PlaylistIndex = -1, PlaylistCount = -1;

        public string Filename;            // the file this progress refers to
        public string PostProcessor;

        /// <summary>0..100, or -1 when nothing usable could be worked out.</summary>
        public double Percent
        {
            get
            {
                if (Finished) return 100;
                if (Total > 0 && Downloaded >= 0) return Math.Min(100.0, Downloaded * 100.0 / Total);
                if (FragCount > 0 && FragIndex >= 0) return Math.Min(100.0, FragIndex * 100.0 / FragCount);
                return -1;
            }
        }
    }

    /// <summary>
    /// Wraps a single yt-dlp invocation. Output is read asynchronously; events are raised on
    /// background threads and marshalled by the caller.
    /// </summary>
    internal class Runner
    {
        /// <summary>
        /// Marker that starts every machine-readable progress line. Chosen so it cannot be
        /// confused with anything yt-dlp, ffmpeg or an external downloader prints.
        /// </summary>
        public const string Sentinel = "~~ytg~~";

        /// <summary>
        /// The --progress-template that produces those lines. Fields are pipe-separated and
        /// fixed in position; missing values arrive as "NA". The filename is last because it
        /// is the only field that can itself contain a pipe.
        /// </summary>
        public const string ProgressTemplate =
            "download:" + Sentinel +
            "|%(progress.status)s" +
            "|%(progress.downloaded_bytes)s" +
            "|%(progress.total_bytes)s" +
            "|%(progress.total_bytes_estimate)s" +
            "|%(progress.speed)s" +
            "|%(progress.eta)s" +
            "|%(progress.fragment_index)s" +
            "|%(progress.fragment_count)s" +
            "|%(info.playlist_index)s" +
            "|%(info.n_entries)s" +
            "|%(info.playlist_count)s" +
            "|%(progress.elapsed)s" +
            "|%(progress.filename)s";

        private Process _proc;
        private readonly object _gate = new object();
        private volatile bool _running;
        private volatile bool _stopping;

        public event Action<string, LineKind> LineReceived;
        public event Action<ProgressInfo> ProgressChanged;
        public event Action<int> Finished;

        public bool IsRunning { get { return _running; } }
        public bool IsStopping { get { return _stopping; } }
        public string LastCommandLine { get; private set; }

        // ---- output patterns --------------------------------------------------

        // Fallback for yt-dlp builds without --progress-template, and for any downloader that
        // prints its own progress:  [download]  23.4% of ~  12.34MiB at 1.23MiB/s ETA 00:42
        private static readonly Regex RxProgress = new Regex(
            @"^\[download\]\s+(?<pct>\d{1,3}(?:\.\d+)?)%\s+of\s+~?\s*(?<size>[\d.]+\s*[KMGTP]?i?B)?" +
            @"(?:\s+(?:at|in)\s+(?<speed>\S+))?(?:\s+ETA\s+(?<eta>\S+))?(?:.*?\(frag\s+(?<frag>\d+)/(?<fragtot>\d+)\))?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex RxItem = new Regex(
            @"^\[download\]\s+Downloading item\s+(?<i>\d+)\s+of\s+(?<n>\d+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex RxPost = new Regex(
            @"^\[(?<pp>Merger|ExtractAudio|VideoRemuxer|VideoConvertor|EmbedSubtitle|EmbedThumbnail|Metadata|SponsorBlock|ModifyChapters|FixupM4a|FixupM3u8|FixupTimestamp|SplitChapters|ThumbnailsConvertor)\]",
            RegexOptions.Compiled);

        // ANSI colour and cursor sequences: yt-dlp emits them whenever --color is not "never",
        // and a RichTextBox would otherwise show them as literal garbage.
        private static readonly Regex RxAnsi = new Regex(
            @"\x1B\[[0-9;?]*[ -/]*[@-~]|\x1B\][^\x07\x1B]*(?:\x07|\x1B\\)|\x1B[@-Z\\-_]",
            RegexOptions.Compiled);

        public static string StripAnsi(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\x1B') < 0) return s;
            return RxAnsi.Replace(s, "");
        }

        // =====================================================================
        public void Start(string exePath, IList<string> args, string workingDir)
        {
            if (_running) throw new InvalidOperationException("A download is already running.");

            var argLine = BuildArgumentLine(args);
            LastCommandLine = Quote(exePath) + " " + argLine;

            var psi = NewStartInfo(exePath, argLine);
            psi.WorkingDirectory = string.IsNullOrEmpty(workingDir) ? App.BaseDir : workingDir;

            var p = new Process();
            p.StartInfo = psi;
            p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e) { HandleLine(e.Data, false); };
            p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { HandleLine(e.Data, true); };

            lock (_gate) { _proc = p; }
            _stopping = false;
            _running = true;

            try
            {
                p.Start();
            }
            catch
            {
                _running = false;
                lock (_gate) { _proc = null; }
                try { p.Dispose(); }
                catch { }
                throw;
            }

            // Close stdin at once: any child that reads it (ffmpeg for HLS) gets EOF rather
            // than blocking forever on a handle a windowed process cannot supply.
            try { p.StandardInput.Close(); }
            catch { }
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            // Process.Exited fires before the asynchronous readers have drained, which loses
            // the last lines of every run - including the error that explains a failure.
            // The blocking WaitForExit() overload waits for those readers, so completion is
            // announced from a thread of our own instead.
            var monitor = new Thread(delegate ()
            {
                int code = -1;
                try { p.WaitForExit(); }
                catch { }
                try { code = p.ExitCode; }
                catch { }

                lock (_gate) { _proc = null; }
                _running = false;

                var h = Finished;
                if (h != null)
                {
                    try { h(code); }
                    catch { }
                }
                try { p.Dispose(); }
                catch { }
            });
            monitor.IsBackground = true;
            monitor.Name = "yt-dlp monitor";
            monitor.Start();
        }

        private static ProcessStartInfo NewStartInfo(string exePath, string argLine)
        {
            var psi = new ProcessStartInfo(exePath, argLine);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true;
            psi.StandardOutputEncoding = new UTF8Encoding(false);
            psi.StandardErrorEncoding = new UTF8Encoding(false);
            psi.WorkingDirectory = App.BaseDir;

            // Force UTF-8 so unicode titles survive the pipe intact.
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            psi.EnvironmentVariables["PYTHONUTF8"] = "1";
            return psi;
        }

        // =====================================================================
        private void HandleLine(string raw, bool stderr)
        {
            if (raw == null) return;

            var line = StripAnsi(raw);

            // Machine-readable progress: consumed here, never shown verbatim.
            if (line.StartsWith(Sentinel, StringComparison.Ordinal))
            {
                var pi = ParseTemplateLine(line);
                if (pi != null)
                {
                    Raise(pi);
                    var ph = LineReceived;
                    if (ph != null) ph(line, LineKind.Progress);
                    return;
                }
            }

            var kind = LineKind.Normal;
            if (line.IndexOf("ERROR:", StringComparison.OrdinalIgnoreCase) >= 0) kind = LineKind.Error;
            else if (line.IndexOf("WARNING:", StringComparison.OrdinalIgnoreCase) >= 0) kind = LineKind.Warning;
            else if (stderr) kind = LineKind.Warning;
            else if (line.StartsWith("[info]") || line.StartsWith("[download] Destination:")) kind = LineKind.Info;

            var item = RxItem.Match(line);
            if (item.Success)
            {
                var pi = new ProgressInfo { Kind = ProgressKind.Item };
                pi.PlaylistIndex = ParseInt(item.Groups["i"].Value, -1);
                pi.PlaylistCount = ParseInt(item.Groups["n"].Value, -1);
                Raise(pi);
            }
            else
            {
                var pp = RxPost.Match(line);
                if (pp.Success)
                {
                    Raise(new ProgressInfo { Kind = ProgressKind.Post, PostProcessor = pp.Groups["pp"].Value });
                }
                else
                {
                    var m = RxProgress.Match(line);
                    if (m.Success)
                    {
                        Raise(ParseHumanLine(m));
                        // A percentage line drives the bar, not the log: writing every one of
                        // them is what made long playlists crawl. The "100% ... in ..." summary
                        // is still worth keeping.
                        if (kind == LineKind.Normal && line.IndexOf("100%", StringComparison.Ordinal) < 0)
                            kind = LineKind.Progress;
                    }
                }
            }

            var lh = LineReceived;
            if (lh != null) lh(line, kind);
        }

        private void Raise(ProgressInfo pi)
        {
            var ph = ProgressChanged;
            if (ph != null && pi != null) ph(pi);
        }

        /// <summary>
        /// Renders one machine progress line the way yt-dlp would have written it, for the
        /// benefit of "Log progress lines". Returns null when the line is not one of ours.
        /// </summary>
        public static string DescribeProgressLine(string line)
        {
            if (line == null || !line.StartsWith(Sentinel, StringComparison.Ordinal)) return null;

            var p = ParseTemplateLine(line);
            if (p == null) return null;

            var sb = new StringBuilder("[download] ");
            sb.Append(p.Percent >= 0 ? p.Percent.ToString("0.0", CultureInfo.InvariantCulture) + "%" : "?");
            if (p.Total > 0) sb.Append(" of ").Append(p.TotalIsEstimate ? "~" : "").Append(Ui.Size(p.Total));
            if (p.Speed > 0) sb.Append(" at ").Append(Ui.Rate(p.Speed));
            if (p.Eta >= 0) sb.Append(" ETA ").Append(Ui.Duration(p.Eta));
            if (p.FragCount > 0) sb.Append(" (frag ").Append(p.FragIndex).Append('/').Append(p.FragCount).Append(')');
            if (!string.IsNullOrEmpty(p.Filename))
            {
                string name;
                try { name = System.IO.Path.GetFileName(p.Filename); }
                catch { name = p.Filename; }
                sb.Append("  ").Append(name);
            }
            return sb.ToString();
        }

        /// <summary>Parses one Sentinel line. Returns null if the shape is not what we emitted.</summary>
        private static ProgressInfo ParseTemplateLine(string line)
        {
            // Split into the fixed fields; the filename tail keeps any pipes it contains.
            var f = line.Split('|');
            if (f.Length < 13) return null;

            var pi = new ProgressInfo { Kind = ProgressKind.File };
            pi.Status = Val(f[1]);
            pi.Finished = pi.Status == "finished";

            pi.Downloaded = ParseLong(f[2], -1);

            long total = ParseLong(f[3], -1);
            if (total < 0)
            {
                total = ParseLong(f[4], -1);
                pi.TotalIsEstimate = total >= 0;
            }
            pi.Total = total;

            pi.Speed = ParseDouble(f[5], -1);
            pi.Eta = ParseDouble(f[6], -1);
            pi.FragIndex = ParseInt(f[7], -1);
            pi.FragCount = ParseInt(f[8], -1);
            pi.PlaylistIndex = ParseInt(f[9], -1);
            pi.PlaylistCount = ParseInt(f[10], -1);
            if (pi.PlaylistCount < 0) pi.PlaylistCount = ParseInt(f[11], -1);
            pi.Elapsed = ParseDouble(f[12], -1);

            if (f.Length > 13)
            {
                var name = string.Join("|", f, 13, f.Length - 13);
                pi.Filename = Val(name);
            }

            // "finished" reports the final size; make the two agree so the bar lands on 100%.
            if (pi.Finished && pi.Downloaded > 0 && pi.Total <= 0) pi.Total = pi.Downloaded;
            return pi;
        }

        private static ProgressInfo ParseHumanLine(Match m)
        {
            var pi = new ProgressInfo { Kind = ProgressKind.File };

            double pct;
            if (double.TryParse(m.Groups["pct"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out pct))
            {
                // Model the human line as bytes so both paths feed the same arithmetic.
                pi.Total = 10000;
                pi.Downloaded = (long)Math.Round(pct * 100);
                pi.TotalIsEstimate = true;
                pi.Finished = pct >= 100;
            }
            if (m.Groups["frag"].Success)
            {
                pi.FragIndex = ParseInt(m.Groups["frag"].Value, -1);
                pi.FragCount = ParseInt(m.Groups["fragtot"].Value, -1);
            }
            pi.Speed = m.Groups["speed"].Success ? ParseRate(m.Groups["speed"].Value) : -1;
            pi.Eta = m.Groups["eta"].Success ? ParseClock(m.Groups["eta"].Value) : -1;
            return pi;
        }

        // ---- value helpers ----------------------------------------------------
        private static string Val(string s)
        {
            if (s == null) return null;
            s = s.Trim();
            return (s.Length == 0 || s == "NA" || s == "None") ? null : s;
        }

        private static long ParseLong(string s, long fallback)
        {
            var v = Val(s);
            double d;
            if (v != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) &&
                d >= 0 && d < long.MaxValue)
                return (long)d;
            return fallback;
        }

        private static int ParseInt(string s, int fallback)
        {
            long v = ParseLong(s, fallback);
            return v > int.MaxValue ? fallback : (int)v;
        }

        private static double ParseDouble(string s, double fallback)
        {
            var v = Val(s);
            double d;
            if (v != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                return d;
            return fallback;
        }

        /// <summary>"1.23MiB/s" -&gt; bytes per second, or -1.</summary>
        private static double ParseRate(string s)
        {
            if (string.IsNullOrEmpty(s)) return -1;
            var t = s.Trim();
            int slash = t.IndexOf('/');
            if (slash > 0) t = t.Substring(0, slash);
            return ParseSize(t);
        }

        private static double ParseSize(string s)
        {
            if (string.IsNullOrEmpty(s)) return -1;
            var t = s.Trim();
            int i = 0;
            while (i < t.Length && (char.IsDigit(t[i]) || t[i] == '.')) i++;
            if (i == 0) return -1;

            double n;
            if (!double.TryParse(t.Substring(0, i), NumberStyles.Float, CultureInfo.InvariantCulture, out n))
                return -1;

            var unit = t.Substring(i).Trim().ToUpperInvariant();
            if (unit.StartsWith("K")) n *= 1024;
            else if (unit.StartsWith("M")) n *= 1024 * 1024;
            else if (unit.StartsWith("G")) n *= 1024L * 1024 * 1024;
            else if (unit.StartsWith("T")) n *= 1024L * 1024 * 1024 * 1024;
            return n;
        }

        /// <summary>"00:42" or "01:02:03" -&gt; seconds, or -1.</summary>
        private static double ParseClock(string s)
        {
            if (string.IsNullOrEmpty(s)) return -1;
            var parts = s.Trim().Split(':');
            double total = 0;
            foreach (var p in parts)
            {
                double v;
                if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return -1;
                total = total * 60 + v;
            }
            return total;
        }

        // =====================================================================
        /// <summary>Terminates the process together with any children (ffmpeg, aria2c...).</summary>
        public void Stop()
        {
            Process p;
            lock (_gate) { p = _proc; }
            if (p == null) return;

            _stopping = true;
            KillTree(p);
        }

        internal static void KillTree(Process p)
        {
            try
            {
                if (p == null || p.HasExited) return;

                // taskkill /T reaches the ffmpeg children that Kill() would orphan.
                var psi = new ProcessStartInfo("taskkill.exe", "/PID " + p.Id + " /T /F");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (var k = Process.Start(psi))
                {
                    if (k != null) k.WaitForExit(5000);
                }
            }
            catch { }

            try { if (!p.HasExited) p.Kill(); }
            catch { }
        }

        // =====================================================================
        public static string BuildArgumentLine(IList<string> args)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < args.Count; i++)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(Quote(args[i]));
            }
            return sb.ToString();
        }

        // Whitespace and quotes are what CommandLineToArgvW cares about. The rest are what
        // cmd.exe cares about: the command line is also shown on the Command tab and saved as
        // a .bat file, and an unquoted "|" there would be read as a pipe. Quoting more than
        // strictly necessary changes nothing for the child process.
        private static readonly char[] NeedsQuoting = { ' ', '\t', '\n', '\v', '"', '|', '&', '<', '>', '^' };

        /// <summary>Quotes one argument according to the Win32 CommandLineToArgvW rules.</summary>
        public static string Quote(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return "\"\"";
            if (arg.IndexOfAny(NeedsQuoting) < 0) return arg;

            var sb = new StringBuilder();
            sb.Append('"');
            for (int i = 0; i < arg.Length; i++)
            {
                int slashes = 0;
                while (i < arg.Length && arg[i] == '\\') { slashes++; i++; }

                if (i == arg.Length)
                {
                    sb.Append('\\', slashes * 2);
                    break;
                }

                if (arg[i] == '"')
                {
                    sb.Append('\\', slashes * 2 + 1);
                    sb.Append('"');
                }
                else
                {
                    sb.Append('\\', slashes);
                    sb.Append(arg[i]);
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>
        /// Runs a tool to completion and returns its combined output. Kept for the short,
        /// blocking queries where the caller has nothing else to do.
        /// </summary>
        public static string RunCapture(string exePath, IList<string> args, int timeoutMs, out int exitCode)
        {
            var run = ToolRun.RunSync(exePath, args, timeoutMs);
            exitCode = run.ExitCode;
            return run.Combined;
        }
    }

    /// <summary>
    /// One short-lived helper invocation (-F, --version, -U, --list-extractors). Unlike a
    /// download these are run for their output, so stdout and stderr are kept apart, and the
    /// caller can cancel instead of watching a frozen window.
    /// </summary>
    internal sealed class ToolRun
    {
        public string StdOut = "";
        public string StdErr = "";
        public int ExitCode = -1;
        public bool TimedOut;
        public bool Cancelled;
        public Exception Failure;

        public string Combined
        {
            get
            {
                if (StdErr.Length == 0) return StdOut;
                if (StdOut.Length == 0) return StdErr;
                return StdOut + Environment.NewLine + StdErr;
            }
        }

        public bool Ok { get { return ExitCode == 0 && !TimedOut && !Cancelled && Failure == null; } }

        private Process _proc;
        private readonly object _gate = new object();
        private volatile bool _cancelled;

        /// <summary>Starts the tool on a background thread; <paramref name="completed"/> is
        /// raised there too, so callers must marshal to the UI thread themselves.</summary>
        public static ToolRun Begin(string exePath, IList<string> args, int timeoutMs, Action<ToolRun> completed)
        {
            var run = new ToolRun();
            var t = new Thread(delegate ()
            {
                run.Execute(exePath, args, timeoutMs);
                if (completed != null)
                {
                    try { completed(run); }
                    catch { }
                }
            });
            t.IsBackground = true;
            t.Name = "yt-dlp tool";
            t.Start();
            return run;
        }

        public static ToolRun RunSync(string exePath, IList<string> args, int timeoutMs)
        {
            var run = new ToolRun();
            run.Execute(exePath, args, timeoutMs);
            return run;
        }

        public void Cancel()
        {
            _cancelled = true;
            Process p;
            lock (_gate) { p = _proc; }
            if (p != null) Runner.KillTree(p);
        }

        private void Execute(string exePath, IList<string> args, int timeoutMs)
        {
            var outSb = new StringBuilder();
            var errSb = new StringBuilder();

            try
            {
                var psi = new ProcessStartInfo(exePath, Runner.BuildArgumentLine(args));
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.RedirectStandardInput = true;   // see Runner.Start(): stops children blocking
                psi.StandardOutputEncoding = new UTF8Encoding(false);
                psi.StandardErrorEncoding = new UTF8Encoding(false);
                psi.WorkingDirectory = App.BaseDir;
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                psi.EnvironmentVariables["PYTHONUTF8"] = "1";

                using (var p = new Process())
                {
                    p.StartInfo = psi;
                    p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) lock (outSb) { outSb.AppendLine(Runner.StripAnsi(e.Data)); }
                    };
                    p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) lock (errSb) { errSb.AppendLine(Runner.StripAnsi(e.Data)); }
                    };

                    lock (_gate) { _proc = p; }
                    if (_cancelled) { Cancelled = true; return; }

                    p.Start();
                    try { p.StandardInput.Close(); }
                    catch { }
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();

                    if (!p.WaitForExit(timeoutMs))
                    {
                        Runner.KillTree(p);
                        TimedOut = !_cancelled;
                        Cancelled = _cancelled;
                        try { p.WaitForExit(3000); }
                        catch { }
                    }
                    else
                    {
                        p.WaitForExit();          // let the asynchronous readers flush
                        ExitCode = p.ExitCode;
                    }

                    if (_cancelled) Cancelled = true;
                    lock (_gate) { _proc = null; }
                }
            }
            catch (Exception ex)
            {
                Failure = ex;
            }
            finally
            {
                lock (outSb) { StdOut = outSb.ToString(); }
                lock (errSb) { StdErr = errSb.ToString(); }
                if (TimedOut) StdErr += "ERROR: the operation timed out." + Environment.NewLine;
                if (Failure != null) StdErr += "ERROR: " + Failure.Message + Environment.NewLine;
            }
        }
    }
}
