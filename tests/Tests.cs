using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace YtDlpGui.Tests
{
    internal static class TestProgram
    {
        private static int _pass, _fail;

        private static void Check(bool ok, string name, string detail = null)
        {
            if (ok) { _pass++; return; }
            _fail++;
            Console.WriteLine("FAIL  " + name + (detail == null ? "" : "   [" + detail + "]"));
        }

        private static void Eq(object actual, object expected, string name)
        {
            bool ok = Equals(actual, expected);
            Check(ok, name, ok ? null : "expected <" + expected + "> got <" + actual + ">");
        }

        private static void Near(double actual, double expected, double tol, string name)
        {
            bool ok = Math.Abs(actual - expected) <= tol;
            Check(ok, name, ok ? null : "expected ~" + expected + " got " + actual);
        }

        [STAThread]
        private static int Main(string[] args)
        {
            App.Init();

            JsonTests();
            QuoteTests();
            AnsiTests();
            FormatTests();
            TemplateParseTests();
            HumanParseTests();
            TrackerTests();
            SettingsTests();
            VersionTests();

            bool net = false, ui = false;
            foreach (var a in args)
            {
                if (a == "--net") net = true;
                if (a == "--ui") ui = true;
            }

            if (net) LiveTests();

            if (ui)
            {
                Console.WriteLine();
                Console.WriteLine("--- window tests ---");
                System.Windows.Forms.Application.EnableVisualStyles();
                System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                UiTests.ShotDir = Environment.GetEnvironmentVariable("YTG_SHOTS") ??
                                  System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ytg-shots");
                UiTests.Run(delegate (bool ok, string name, string detail) { Check(ok, name, detail); }, net);
            }

            Console.WriteLine();
            Console.WriteLine(_fail == 0
                ? "ALL PASS  (" + _pass + " checks)"
                : _fail + " FAILED of " + (_pass + _fail));
            return _fail == 0 ? 0 : 1;
        }

        // =====================================================================
        private static void JsonTests()
        {
            object v;

            Check(Json.TryParse("[]", out v) && Json.Arr(v).Count == 0, "json empty array");
            Check(Json.TryParse("{}", out v) && Json.Obj(v).Count == 0, "json empty object");

            Check(Json.TryParse("[1, 2.5, -3, 1e3, 1.2e-2]", out v), "json numbers parse");
            var nums = Json.Arr(v);
            Eq(nums.Count, 5, "json number count");
            Eq((double)nums[1], 2.5, "json fractional");
            Eq((double)nums[2], -3.0, "json negative");
            Eq((double)nums[3], 1000.0, "json exponent");
            Near((double)nums[4], 0.012, 1e-12, "json negative exponent");

            Check(Json.TryParse("{\"a\":\"x\\ny\",\"b\":\"\\u00e9\",\"c\":\"a\\\\b\",\"d\":\"q\\\"q\"}", out v),
                "json escapes parse");
            Eq(Json.Str(v, "a"), "x\ny", "json \\n escape");
            Eq(Json.Str(v, "b"), "\u00e9", "json \\u escape");
            Eq(Json.Str(v, "c"), "a\\b", "json backslash escape");
            Eq(Json.Str(v, "d"), "q\"q", "json quote escape");

            Check(Json.TryParse("{\"n\":null,\"t\":true,\"f\":false}", out v), "json literals parse");
            Eq(Json.Str(v, "n"), null, "json null is null");
            Eq(Json.Flag(v, "t"), true, "json true");
            Eq(Json.Flag(v, "f"), false, "json false");
            Eq(Json.Flag(v, "missing"), false, "json missing flag");
            Eq(Json.Num(v, "missing"), null, "json missing number");
            Eq(Json.Str(null, "x"), null, "json accessor tolerates null");

            // A number given as a string still reads as a number, and vice versa: yt-dlp is
            // not consistent about this across extractors.
            Check(Json.TryParse("{\"a\":\"12.5\",\"b\":7}", out v), "json mixed types parse");
            Eq(Json.Num(v, "a"), 12.5, "json numeric string");
            Eq(Json.Str(v, "b"), "7", "json number as string");

            Check(Json.TryParse("[{\"x\":[1,{\"y\":\"z\"}]}]", out v), "json nested parse");
            Eq(Json.Str(Json.Arr(Json.Obj(Json.Arr(v)[0])["x"])[1], "y"), "z", "json deep access");

            Check(!Json.TryParse("[1,", out v), "json truncated rejected");
            Check(!Json.TryParse("{\"a\" 1}", out v), "json missing colon rejected");
            Check(!Json.TryParse("", out v), "json empty rejected");
            Check(!Json.TryParse("[1,2] garbage {", out v) || true, "json trailing tolerated or rejected");

            // Depth guard: a hostile or corrupt line must not take the process down.
            var deep = new string('[', 500) + new string(']', 500);
            Check(!Json.TryParse(deep, out v), "json depth guard");

            // The real shape: a formats array as yt-dlp prints it.
            const string real =
                "[{\"format_id\": \"140\", \"ext\": \"m4a\", \"acodec\": \"mp4a.40.2\", \"vcodec\": \"none\", " +
                "\"abr\": 129.0, \"filesize\": 10276000, \"audio_channels\": 2, \"asr\": 44100, " +
                "\"format_note\": \"medium\", \"protocol\": \"https\"}, " +
                "{\"format_id\": \"299\", \"ext\": \"mp4\", \"acodec\": \"none\", \"vcodec\": \"avc1.64002a\", " +
                "\"width\": 1920, \"height\": 1080, \"fps\": 60, \"tbr\": 3248.0, " +
                "\"filesize_approx\": 257000000, \"has_drm\": false, \"protocol\": \"https\"}]";
            Check(Json.TryParse(real, out v), "json real formats parse");
            var arr = Json.Arr(v);
            Eq(arr.Count, 2, "json real format count");
            Eq(Json.Str(arr[0], "format_id"), "140", "json real id");
            Eq(Json.Num(arr[1], "height"), 1080.0, "json real height");
            Eq(Json.Flag(arr[1], "has_drm"), false, "json real drm flag");
        }

        // =====================================================================
        private static void QuoteTests()
        {
            Eq(Runner.Quote("plain"), "plain", "quote plain");
            Eq(Runner.Quote(""), "\"\"", "quote empty");
            Eq(Runner.Quote("has space"), "\"has space\"", "quote space");
            Eq(Runner.Quote("C:\\dir\\file"), "C:\\dir\\file", "quote path no space");
            Eq(Runner.Quote("C:\\my dir\\"), "\"C:\\my dir\\\\\"", "quote trailing backslash");
            Eq(Runner.Quote("say \"hi\""), "\"say \\\"hi\\\"\"", "quote embedded quotes");
            Eq(Runner.Quote("a\\\"b"), "\"a\\\\\\\"b\"", "quote backslash before quote");

            // The progress template must survive quoting intact.
            var q = Runner.Quote(Runner.ProgressTemplate);
            Check(q.StartsWith("\"") && q.EndsWith("\"") && q.IndexOf("%(progress.status)s", StringComparison.Ordinal) > 0,
                "quote progress template");
            Check(Runner.ProgressTemplate.IndexOf('"') < 0, "progress template has no quotes to escape");

            var line = Runner.BuildArgumentLine(new List<string> { "-o", "a b.mp4", "-f", "bv+ba" });
            Eq(line, "-o \"a b.mp4\" -f bv+ba", "argument line");
        }

        private static void AnsiTests()
        {
            Eq(Runner.StripAnsi("plain"), "plain", "ansi passthrough");
            Eq(Runner.StripAnsi("\x1B[0;31mred\x1B[0m"), "red", "ansi colour");
            Eq(Runner.StripAnsi("\x1B[2K\x1B[0G[download] 5%"), "[download] 5%", "ansi cursor codes");
            Eq(Runner.StripAnsi("\x1B]0;title\x07x"), "x", "ansi osc");
            Eq(Runner.StripAnsi(null), null, "ansi null");
        }

        private static void FormatTests()
        {
            Eq(Ui.Size(0), "", "size zero");
            Eq(Ui.Size(-5), "", "size negative");
            Eq(Ui.Size(512), "512 B", "size bytes");
            Eq(Ui.Size(1024), "1.00 KiB", "size 1k");
            Eq(Ui.Size(1536), "1.50 KiB", "size 1.5k");
            Eq(Ui.Size(10 * 1024 * 1024), "10.0 MiB", "size 10M");
            Eq(Ui.Size(1024L * 1024 * 1024 * 3), "3.00 GiB", "size 3G");
            Eq(Ui.Rate(1024 * 1024), "1.00 MiB/s", "rate");
            Eq(Ui.Rate(0), "", "rate zero");

            Eq(Ui.Duration(-1), "", "duration negative");
            Eq(Ui.Duration(0), "00:00", "duration zero");
            Eq(Ui.Duration(65), "01:05", "duration minutes");
            Eq(Ui.Duration(3725), "1:02:05", "duration hours");
            Eq(Ui.Duration(double.NaN), "", "duration NaN");

            Eq(Ui.Join(" - ", "a", "", null, "b"), "a - b", "join skips blanks");
            Eq(Ui.Join(" - "), "", "join empty");
        }

        // =====================================================================
        private static ProgressInfo ParseTemplate(string line)
        {
            var m = typeof(Runner).GetMethod("ParseTemplateLine",
                BindingFlags.NonPublic | BindingFlags.Static);
            return (ProgressInfo)m.Invoke(null, new object[] { line });
        }

        private static void TemplateParseTests()
        {
            // Exactly the shape captured from a real run.
            const string l1 = "~~ytg~~|downloading|720859|NA|3541588.2|808400.82|12.5|24|123|1|2|2|0.78|p1.mp4";
            var p = ParseTemplate(l1);
            Check(p != null, "template parses");
            Eq(p.Status, "downloading", "template status");
            Eq(p.Downloaded, 720859L, "template downloaded");
            Eq(p.Total, 3541588L, "template estimated total");
            Eq(p.TotalIsEstimate, true, "template total is estimate");
            Near(p.Speed, 808400.82, 0.01, "template speed");
            Near(p.Eta, 12.5, 0.001, "template eta");
            Eq(p.FragIndex, 24, "template frag index");
            Eq(p.FragCount, 123, "template frag count");
            Eq(p.PlaylistIndex, 1, "template playlist index");
            Eq(p.PlaylistCount, 2, "template playlist count");
            Eq(p.Filename, "p1.mp4", "template filename");
            Eq(p.Finished, false, "template not finished");
            Near(p.Percent, 720859.0 * 100 / 3541588, 0.01, "template percent");

            const string l2 = "~~ytg~~|finished|3912242|3912242|NA|1023745.04|NA|NA|NA|2|2|2|3.82|p2.mp4";
            var f = ParseTemplate(l2);
            Eq(f.Finished, true, "template finished flag");
            Eq(f.Total, 3912242L, "template exact total");
            Eq(f.TotalIsEstimate, false, "template exact not estimate");
            Eq(f.Eta, -1.0, "template NA eta");
            Eq(f.FragIndex, -1, "template NA frag");
            Eq(f.Percent, 100.0, "template finished percent");

            // Single video: no playlist fields at all.
            const string l3 = "~~ytg~~|downloading|1024|3871021|NA|175670.46|22|NA|NA|NA|NA|NA|1.05|t.m4a";
            var s = ParseTemplate(l3);
            Eq(s.PlaylistIndex, -1, "template no playlist index");
            Eq(s.PlaylistCount, -1, "template no playlist count");
            Eq(s.Total, 3871021L, "template single total");

            // A filename containing a pipe must not lose its tail.
            const string l4 = "~~ytg~~|downloading|1|2|NA|NA|NA|NA|NA|NA|NA|NA|NA|C:\\v\\a|b.mp4";
            Eq(ParseTemplate(l4).Filename, "C:\\v\\a|b.mp4", "template filename with pipe");

            // Anything malformed must be rejected rather than half-read.
            Check(ParseTemplate("~~ytg~~|downloading|1") == null, "template short line rejected");

            // playlist_count is used when n_entries is missing.
            const string l5 = "~~ytg~~|downloading|1|2|NA|NA|NA|NA|NA|3|NA|9|NA|x.mp4";
            Eq(ParseTemplate(l5).PlaylistCount, 9, "template falls back to playlist_count");

            // Rendering one back for the log must describe that line, not the newest one.
            var text = Runner.DescribeProgressLine(l1);
            Check(text != null && text.StartsWith("[download] "), "describe: looks like yt-dlp output", text);
            Check(text.IndexOf("20.4%", StringComparison.Ordinal) >= 0, "describe: percent of that line", text);
            Check(text.IndexOf("~3.38 MiB", StringComparison.Ordinal) >= 0, "describe: marks an estimate", text);
            Check(text.IndexOf("789 KiB/s", StringComparison.Ordinal) >= 0, "describe: speed", text);
            Check(text.IndexOf("ETA 00:13", StringComparison.Ordinal) >= 0, "describe: eta", text);
            Check(text.IndexOf("(frag 24/123)", StringComparison.Ordinal) >= 0, "describe: fragments", text);
            Check(text.EndsWith("p1.mp4"), "describe: file name", text);

            Eq(Runner.DescribeProgressLine("[download] 5% of 1MiB"), null, "describe: ignores ordinary output");
            Eq(Runner.DescribeProgressLine(null), null, "describe: ignores null");

            var full = Runner.DescribeProgressLine(l2);
            Check(full.IndexOf("100.0%", StringComparison.Ordinal) >= 0, "describe: a finished line reads 100%", full);
            Check(full.IndexOf('~') < 0, "describe: an exact size is not marked approximate", full);
        }

        private static void HumanParseTests()
        {
            // The fallback path, for a yt-dlp without --progress-template.
            var runner = new Runner();
            var seen = new List<ProgressInfo>();
            runner.ProgressChanged += delegate (ProgressInfo p) { seen.Add(p); };

            var handle = typeof(Runner).GetMethod("HandleLine", BindingFlags.NonPublic | BindingFlags.Instance);
            Action<string> feed = delegate (string s) { handle.Invoke(runner, new object[] { s, false }); };

            feed("[download]  23.4% of ~  12.34MiB at    1.23MiB/s ETA 00:42");
            Check(seen.Count == 1, "human line raises progress");
            Near(seen[0].Percent, 23.4, 0.2, "human percent");
            Near(seen[0].Speed, 1.23 * 1024 * 1024, 1024, "human speed");
            Near(seen[0].Eta, 42, 0.01, "human eta");

            seen.Clear();
            feed("[download] 100% of    3.69MiB in 00:00:03 at 1.10MiB/s");
            Eq(seen[0].Finished, true, "human 100% finished");

            seen.Clear();
            feed("[download]  50.0% of ~ 10.00MiB at 500.00KiB/s ETA 00:10 (frag 12/345)");
            Eq(seen[0].FragIndex, 12, "human frag index");
            Eq(seen[0].FragCount, 345, "human frag count");

            seen.Clear();
            feed("[download] Downloading item 7 of 42");
            Eq(seen[0].Kind, ProgressKind.Item, "item line kind");
            Eq(seen[0].PlaylistIndex, 7, "item line index");
            Eq(seen[0].PlaylistCount, 42, "item line count");

            seen.Clear();
            feed("[Merger] Merging formats into \"x.mkv\"");
            Eq(seen[0].Kind, ProgressKind.Post, "merger line kind");
            Eq(seen[0].PostProcessor, "Merger", "merger name");

            seen.Clear();
            feed("[youtube] Extracting URL: https://example.com");
            Eq(seen.Count, 0, "plain line raises no progress");
        }

        // =====================================================================
        private static ProgressInfo File(long got, long total, string name, int plIndex = -1, int plCount = -1)
        {
            return new ProgressInfo
            {
                Kind = ProgressKind.File,
                Downloaded = got,
                Total = total,
                Filename = name,
                PlaylistIndex = plIndex,
                PlaylistCount = plCount,
                Speed = 1000
            };
        }

        private static void TrackerTests()
        {
            // ---- a single video, one stream ----
            var t = new ProgressTracker();
            t.BeginRun(1);
            t.BeginJob(0);

            t.Feed(File(0, 1000, "a.mp4"));
            Near(t.Read().Overall, 0, 0.001, "single: starts at zero");

            t.Feed(File(500, 1000, "a.mp4"));
            Near(t.Read().Overall, 0.5, 0.001, "single: halfway");
            Near(t.Read().File, 0.5, 0.001, "single: file fraction");

            t.Feed(File(1000, 1000, "a.mp4"));
            Near(t.Read().Overall, 0.995, 0.001, "single: held just short of full while in progress");

            t.EndJob();
            Near(t.Read().Overall, 1.0, 0.001, "single: full once the entry ends");

            // ---- one video, video stream then audio stream ----
            t = new ProgressTracker();
            t.BeginRun(1);
            t.BeginJob(0);
            t.Feed(File(0, 100000, "v.f303.webm"));
            t.Feed(File(100000, 100000, "v.f303.webm"));
            double afterVideo = t.Read().Overall;

            t.Feed(File(0, 5000, "v.f251.webm"));
            double atAudioStart = t.Read().Overall;
            Check(atAudioStart >= afterVideo - 1e-9,
                "two streams: never goes backwards when the audio stream starts",
                afterVideo + " -> " + atAudioStart);

            t.Feed(File(5000, 5000, "v.f251.webm"));
            Check(t.Read().Overall >= atAudioStart, "two streams: still rising at the end");
            t.EndJob();
            Near(t.Read().Overall, 1.0, 0.001, "two streams: full at the end");

            // ---- a playlist of 10, inside one queue entry ----
            t = new ProgressTracker();
            t.BeginRun(1);
            t.BeginJob(0);

            t.Feed(File(0, 1000, "1.mp4", 1, 10));
            Near(t.Read().Overall, 0.0, 0.001, "playlist: first item start");

            t.Feed(File(1000, 1000, "1.mp4", 1, 10));
            Near(t.Read().Overall, 0.0995, 0.002, "playlist: one of ten nearly done");

            t.Feed(File(500, 1000, "5.mp4", 5, 10));
            Near(t.Read().Overall, 0.45, 0.002, "playlist: halfway through item five");
            Eq(t.Read().ItemIndex, 5, "playlist: item index reported");
            Eq(t.Read().ItemCount, 10, "playlist: item count reported");

            t.Feed(File(1000, 1000, "10.mp4", 10, 10));
            Near(t.Read().Overall, 0.9995, 0.002, "playlist: last item nearly done");
            t.EndJob();
            Near(t.Read().Overall, 1.0, 0.001, "playlist: full at the end");

            // ---- three queue entries, the middle one a playlist ----
            t = new ProgressTracker();
            t.BeginRun(3);

            t.BeginJob(0);
            t.Feed(File(1000, 1000, "a.mp4"));
            t.EndJob();
            Near(t.Read().Overall, 1.0 / 3, 0.002, "queue: one of three done");

            t.BeginJob(1);
            t.Feed(File(500, 1000, "b2.mp4", 2, 4));
            Near(t.Read().Overall, (1.0 / 3) + (1.0 / 3) * ((1 + 0.5) / 4), 0.003, "queue: mid playlist");
            t.EndJob();
            Near(t.Read().Overall, 2.0 / 3, 0.002, "queue: two of three done");

            t.BeginJob(2);
            t.Feed(File(250, 1000, "c.mp4"));
            Near(t.Read().Overall, 2.0 / 3 + (1.0 / 3) * 0.25, 0.003, "queue: quarter through the last");
            t.EndJob();
            Near(t.Read().Overall, 1.0, 0.002, "queue: all done");

            // ---- monotonic under a hostile stream of reports ----
            t = new ProgressTracker();
            t.BeginRun(2);
            t.BeginJob(0);
            double last = -1;
            var rng = new Random(1234);
            for (int item = 1; item <= 5; item++)
            {
                for (int stream = 0; stream < 2; stream++)
                {
                    long total = 1000 + rng.Next(50000);
                    var name = "i" + item + "s" + stream + ".mp4";
                    for (long got = 0; got <= total; got += Math.Max(1, total / 7))
                    {
                        t.Feed(File(Math.Min(got, total), total, name, item, 5));
                        var now = t.Read().Overall;
                        Check(now >= last - 1e-9, "monotonic overall progress",
                            "item " + item + " stream " + stream + ": " + last + " -> " + now);
                        last = now;
                    }
                }
            }
            t.EndJob();
            Check(t.Read().Overall >= last, "monotonic across job end");

            // ---- unknown sizes fall back to fragments ----
            t = new ProgressTracker();
            t.BeginRun(1);
            t.BeginJob(0);
            t.Feed(new ProgressInfo { Kind = ProgressKind.File, Filename = "hls.mp4", FragIndex = 30, FragCount = 120, Downloaded = -1, Total = -1 });
            Near(t.Read().File, 0.25, 0.001, "fragments drive the file bar when bytes are unknown");

            // ---- nothing known at all ----
            t = new ProgressTracker();
            t.BeginRun(1);
            t.BeginJob(0);
            Eq(t.Read().File, -1.0, "unknown file fraction stays unknown");

            // ---- post-processing is reported and then cleared ----
            t = new ProgressTracker();
            t.BeginRun(1);
            t.BeginJob(0);
            t.Feed(new ProgressInfo { Kind = ProgressKind.Post, PostProcessor = "Merger" });
            Eq(t.Read().PostProcessor, "Merger", "post-processor reported");
            t.Feed(File(1, 10, "x.mp4"));
            Eq(t.Read().PostProcessor, null, "post-processor cleared when bytes move again");

            // ---- item announcements alone still advance the bar ----
            t = new ProgressTracker();
            t.BeginRun(1);
            t.BeginJob(0);
            t.Feed(new ProgressInfo { Kind = ProgressKind.Item, PlaylistIndex = 3, PlaylistCount = 4 });
            Near(t.Read().Overall, 0.5, 0.001, "item announcement advances the overall bar");

            // ---- overall ETA ----
            var snap = new ProgressSnapshot { Overall = 0.25, RunElapsed = 10 };
            Near(snap.OverallEta, 30, 0.001, "overall eta extrapolates");
            Eq(new ProgressSnapshot { Overall = 0, RunElapsed = 10 }.OverallEta, -1.0, "no eta at zero");
            Eq(new ProgressSnapshot { Overall = 0.5, RunElapsed = 1 }.OverallEta, -1.0, "no eta too early");
        }

        // =====================================================================
        private static void SettingsTests()
        {
            var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ytg-settings-test.txt");
            var map = new Dictionary<string, string>
            {
                { "plain", "value" },
                { "path", "C:\\a\\b" },
                { "multi", "one\r\ntwo\nthree" },
                { "empty", "" },
                { "equals", "a=b=c" },
                { "unicode", "\u00e9\u4e2d\u6587" }
            };
            Settings.Save(file, map);
            var back = Settings.Load(file);

            foreach (var kv in map)
                Eq(back.ContainsKey(kv.Key) ? back[kv.Key] : "<missing>", kv.Value, "settings round-trip " + kv.Key);

            try { System.IO.File.Delete(file); }
            catch { }

            Eq(Settings.Load(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "no-such-file-xyz")).Count, 0,
                "settings missing file is empty");
        }

        private static void VersionTests()
        {
            // A current build keeps everything switched on.
            App.SetVersion("2026.08.19");
            Eq(App.YtDlpVersion, "2026.08.19", "version recorded");
            Eq(App.SupportsProgressTemplate, true, "current build supports progress template");
            Eq(App.SupportsProgressDelta, true, "current build supports progress delta");

            Eq(App.DisableOptionalArg("--progress-delta"), true, "optional arg can be disabled");
            Eq(App.DisableOptionalArg("--progress-delta"), false, "disabling twice reports no change");
            Eq(App.SupportsProgressDelta, false, "disabled arg is no longer offered");
            Eq(App.IsDisabledOptionalArg("--progress-delta"), true, "disabled arg is recognised");
            Eq(App.DisableOptionalArg("--proxy"), false, "a user option is never silently dropped");
            Eq(App.IsDisabledOptionalArg("--proxy"), false, "a user option is never marked disabled");
        }

        // =====================================================================
        //  Live: drives the real yt-dlp exactly as the GUI does and watches the
        //  progress model that the bars are drawn from.
        // =====================================================================
        private static void LiveTests()
        {
            Console.WriteLine();
            Console.WriteLine("--- live run against yt-dlp (network) ---");

            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ytg-live-test");
            try { System.IO.Directory.CreateDirectory(dir); }
            catch { }

            var args = new List<string>
            {
                "--newline", "--progress",
                "--progress-template", Runner.ProgressTemplate,
                "--progress-delta", "0.1",
                "--ignore-config", "--color", "never",
                "--js-runtimes", App.JsRuntimeArg ?? "deno",
                "-f", "worstaudio",
                "-o", "%(playlist_index)s.%(ext)s",
                "--no-part",
                "ytsearch2:blender open movie trailer"
            };

            var tracker = new ProgressTracker();
            tracker.BeginRun(1);
            tracker.BeginJob(0);

            var runner = new Runner();
            double last = -1;
            bool wentBackwards = false, sawSentinelAsNormal = false, sawItem2 = false;
            double backFrom = 0, backTo = 0;
            int samples = 0, maxItemCount = 0;
            var done = new System.Threading.ManualResetEvent(false);
            int exit = -999;

            runner.LineReceived += delegate (string line, LineKind kind)
            {
                if (line.StartsWith(Runner.Sentinel, StringComparison.Ordinal) && kind != LineKind.Progress)
                    sawSentinelAsNormal = true;
            };
            runner.ProgressChanged += delegate (ProgressInfo p)
            {
                tracker.Feed(p);
                var s = tracker.Read();
                samples++;
                if (s.ItemCount > maxItemCount) maxItemCount = s.ItemCount;
                if (s.ItemIndex >= 2) sawItem2 = true;
                if (s.Overall >= 0)
                {
                    if (s.Overall < last - 1e-9 && !wentBackwards)
                    {
                        wentBackwards = true;
                        backFrom = last;
                        backTo = s.Overall;
                    }
                    last = s.Overall;
                }
            };
            runner.Finished += delegate (int code) { exit = code; done.Set(); };

            runner.Start(App.YtDlpPath, args, dir);
            bool finished = done.WaitOne(TimeSpan.FromMinutes(5));
            if (!finished) runner.Stop();
            System.Threading.Thread.Sleep(300);

            Check(finished, "live: the run completed");
            Eq(exit, 0, "live: yt-dlp exited cleanly");
            Check(samples > 5, "live: progress samples arrived", "samples=" + samples);
            Check(!sawSentinelAsNormal, "live: no machine progress line leaked into the log");
            Eq(maxItemCount, 2, "live: playlist size detected from the progress stream");
            Check(sawItem2, "live: reached the second playlist entry");
            Check(!wentBackwards, "live: overall progress never went backwards",
                wentBackwards ? backFrom + " -> " + backTo : null);
            Check(last > 0.9, "live: overall reached the end", "last=" + last);

            tracker.EndJob();
            Near(tracker.Read().Overall, 1.0, 0.001, "live: full once the entry ends");
            Check(tracker.Read().RunBytes > 100000, "live: byte total accumulated",
                "bytes=" + tracker.Read().RunBytes);
            Check(!runner.IsRunning, "live: runner reports itself stopped");

            Console.WriteLine("    " + samples + " samples, final overall " +
                              last.ToString("0.000", CultureInfo.InvariantCulture) +
                              ", " + Ui.Size(tracker.Read().RunBytes) + " transferred");

            try { System.IO.Directory.Delete(dir, true); }
            catch { }

            LiveFormatsTest();
        }

        /// <summary>The path that was reported broken: fetch a real format list.</summary>
        private static void LiveFormatsTest()
        {
            var args = new List<string>
            {
                "--ignore-config", "--color", "never",
                "--js-runtimes", App.JsRuntimeArg ?? "deno",
                "--skip-download", "--no-warnings",
                "--playlist-items", "1",
                "--print", "%(id)s",
                "--print", "%(title)s",
                "--print", "%(formats)j",
                "https://www.youtube.com/watch?v=aqz-KE-bpKQ"
            };

            var r = ToolRun.RunSync(App.YtDlpPath, args, 180000);
            Check(r.Ok, "live formats: query succeeded", "exit=" + r.ExitCode + " err=" + r.StdErr.Trim());

            var lines = r.StdOut.Replace("\r", "").Split('\n');
            string json = null;
            foreach (var l in lines)
                if (l.StartsWith("[") && l.Length > 2) { json = l; break; }

            Check(json != null, "live formats: a JSON array was printed");
            if (json == null) return;

            object parsed;
            Check(Json.TryParse(json, out parsed), "live formats: the JSON parses");
            var arr = Json.Arr(parsed);
            Check(arr != null && arr.Count > 10, "live formats: a full table came back",
                "count=" + (arr == null ? 0 : arr.Count));

            int video = 0, audio = 0, sized = 0;
            foreach (var o in arr)
            {
                var vc = Json.Str(o, "vcodec");
                var ac = Json.Str(o, "acodec");
                if (vc != null && vc != "none") video++;
                if (ac != null && ac != "none") audio++;
                var s = Json.Num(o, "filesize") ?? Json.Num(o, "filesize_approx");
                if (s.HasValue && s.Value > 0) sized++;
            }
            Check(video > 5, "live formats: video formats present", "video=" + video);
            Check(audio > 2, "live formats: audio formats present", "audio=" + audio);
            Check(sized > 5, "live formats: sizes reported", "sized=" + sized);

            Console.WriteLine("    " + arr.Count + " formats (" + video + " video, " + audio +
                              " audio, " + sized + " with a size)");
        }
    }
}
