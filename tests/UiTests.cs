using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace YtDlpGui.Tests
{
    /// <summary>
    /// Drives the real MainForm the way a person would - typing, clicking, running an actual
    /// download - and screenshots it along the way. Nothing here is mocked.
    /// </summary>
    internal static class UiTests
    {
        private const BindingFlags Any =
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

        public static string ShotDir;
        private static int _shot;

        private static T Field<T>(object o, string name)
        {
            var f = o.GetType().GetField(name, Any);
            if (f == null) throw new Exception("no field " + name + " on " + o.GetType().Name);
            return (T)f.GetValue(o);
        }

        private static object Call(object o, string name, params object[] args)
        {
            var types = new Type[args.Length];
            for (int i = 0; i < args.Length; i++) types[i] = args[i].GetType();

            var m = o.GetType().GetMethod(name, Any, null, types, null);
            if (m == null)
            {
                // Fall back to matching on arity: a parameter typed "object" never matches
                // the runtime type of the argument being passed.
                foreach (var candidate in o.GetType().GetMethods(Any))
                    if (candidate.Name == name && candidate.GetParameters().Length == args.Length)
                    { m = candidate; break; }
            }
            if (m == null) throw new Exception("no method " + name);
            return m.Invoke(o, args);
        }

        private static void Pump(int ms)
        {
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < until)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(10);
            }
        }

        private static void PumpUntil(Func<bool> done, int timeoutMs)
        {
            var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < until && !done())
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(20);
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        private const uint PW_RENDERFULLCONTENT = 2;

        /// <summary>
        /// Captures the window itself, not the screen behind it. CopyFromScreen photographs
        /// whatever happens to be in front, so a shot taken while the test window has lost
        /// the foreground records someone else's desktop instead of the thing under test.
        /// </summary>
        private static void Shot(Form f, string name)
        {
            try
            {
                f.Activate();
                Application.DoEvents();
                System.Threading.Thread.Sleep(120);
                Application.DoEvents();

                var b = f.Bounds;
                using (var bmp = new Bitmap(Math.Max(1, b.Width), Math.Max(1, b.Height)))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        IntPtr hdc = g.GetHdc();
                        try { PrintWindow(f.Handle, hdc, PW_RENDERFULLCONTENT); }
                        finally { g.ReleaseHdc(hdc); }
                    }

                    var path = Path.Combine(ShotDir, (++_shot).ToString("00") + "-" + name + ".png");
                    bmp.Save(path, ImageFormat.Png);
                    Console.WriteLine("    shot: " + path);
                }
            }
            catch (Exception ex) { Console.WriteLine("    shot failed: " + ex.Message); }
        }

        // =====================================================================
        public static void Run(Action<bool, string, string> check, bool live)
        {
            Directory.CreateDirectory(ShotDir);

            // The form writes the real settings file when it closes; put it back afterwards.
            var settings = App.SettingsFile;
            var backup = settings + ".uitest-backup";
            bool hadSettings = File.Exists(settings);
            if (hadSettings) File.Copy(settings, backup, true);

            var outDir = Path.Combine(Path.GetTempPath(), "ytg-ui-test");
            try { Directory.CreateDirectory(outDir); }
            catch { }

            MainForm form = null;
            try
            {
                Settings.Save(settings, new Dictionary<string, string>());
                form = new MainForm();
                var tabs = Field<TabControl>(form, "_tabs");
                int startupTabChanges = 0;
                tabs.SelectedIndexChanged += delegate { startupTabChanges++; };
                form.Show();
                form.TopMost = true;
                Pump(1500);                       // let Load and the version probe run

                check(!form.IsDisposed, "ui: the window stayed up", null);
                check(startupTabChanges == 0, "ui: startup never cycles through tabs", "changes=" + startupTabChanges);
                check(!Field<bool>(form, "_advancedMode"), "ui: Basic is the default mode", null);
                var basicGroups = Field<Section[]>(form, "_basicGroups");
                foreach (var group in basicGroups)
                {
                    check(group.Collapsed, "ui: basic group starts collapsed - " + group.Text, null);
                    check(group.BackColor == Color.Transparent, "ui: transparent group - " + group.Text, null);
                }
                Shot(form, "startup");

                Call(form, "SetGroupsCollapsed", false);
                Pump(80);
                check(form.txtOutDir.Visible && form.cmbMaxHeight.Visible && form.txtSubLangs.Visible,
                    "ui: expand all reveals everyday options", null);
                check(!form.cmbVideoCodec.Visible && !form.cmbMaxFps.Visible,
                    "ui: Basic hides format refinements", null);
                foreach (var group in basicGroups)
                    check(!group.Collapsed && group.Height > 40, "ui: basic group expands - " + group.Text, null);

                form.txtOutDir.Text = @"C:\Downloads";
                form.cmbMaxHeight.SelectedIndex = 4;
                form.txtProxy.Text = "http://127.0.0.1:8080";
                Field<TextBox>(form, "_urls").Text = "https://www.youtube.com/watch?v=aqz-KE-bpKQ";
                var beforeModeSwitch = Settings.Capture(form);
                var beforeArgs = Runner.BuildArgumentLine(form.BuildArgs(new List<string> { "https://example.com/video" }));
                basicGroups[2].Collapsed = true;
                Field<RichTextBox>(form, "_log").Clear();
                Field<Button>(form, "_btnDownload").Focus();
                Shot(form, "basic-ui");

                Field<ComboBox>(form, "_uiMode").SelectedIndex = 1;
                Pump(80);
                check(Field<bool>(form, "_advancedMode"), "ui: mode selector opens Advanced", null);
                var afterModeSwitch = Settings.Capture(form);
                foreach (var entry in beforeModeSwitch)
                    check(afterModeSwitch.ContainsKey(entry.Key) && afterModeSwitch[entry.Key] == entry.Value,
                        "ui: mode switch preserves " + entry.Key, null);
                check(beforeArgs == Runner.BuildArgumentLine(form.BuildArgs(new List<string> { "https://example.com/video" })),
                    "ui: mode switch preserves generated arguments including hidden options", null);
                foreach (var group in (IEnumerable<Section>)typeof(MainForm).GetMethod("OptionGroups", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { tabs }))
                    check(group.Collapsed, "ui: advanced group starts collapsed - " + group.Text, null);
                Shot(form, "advanced-ui");
                Call(form, "SetGroupsCollapsed", false);
                Pump(80);
                check(form.txtOutDir.Visible && form.txtTempDir.Visible, "ui: Advanced exposes full destination group", null);
                Call(form, "SetAdvancedMode", false);
                Call(form, "SetGroupsCollapsed", true);
                Pump(50);
                check(!form.txtOutDir.Visible, "ui: collapse all hides row controls", null);
                foreach (Control control in basicGroups[0].Controls)
                    if (control is Button) ((Button)control).PerformClick();
                Pump(50);
                check(form.txtOutDir.Visible && !basicGroups[0].Collapsed, "ui: native header button expands a group", null);
                Call(form, "SetAdvancedMode", true);
                check(beforeArgs == Runner.BuildArgumentLine(form.BuildArgs(new List<string> { "https://example.com/video" })),
                    "ui: repeated view changes preserve all options", null);
                Call(form, "ApplyDefaults");

                // ---- every options tab renders ----
                check(tabs.TabPages.Count == 8, "ui: eight option tabs", "count=" + tabs.TabPages.Count);
                for (int i = 0; i < tabs.TabPages.Count; i++)
                {
                    tabs.SelectedIndex = i;
                    Pump(90);
                    check(!form.IsDisposed, "ui: tab renders - " + tabs.TabPages[i].Text, null);
                }
                tabs.SelectedIndex = 1;
                Pump(150);
                Shot(form, "format-tab");
                tabs.SelectedIndex = 0;

                // ---- the command preview reflects the controls ----
                var urls = Field<TextBox>(form, "_urls");
                urls.Text = "https://www.youtube.com/watch?v=aqz-KE-bpKQ";
                Call(form, "UpdatePreview");
                Pump(60);

                var preview = Field<TextBox>(form, "_cmdPreview").Text;
                check(preview.IndexOf("--progress-template", StringComparison.Ordinal) >= 0,
                    "ui: preview carries the progress template", null);
                check(preview.IndexOf("aqz-KE-bpKQ", StringComparison.Ordinal) >= 0,
                    "ui: preview carries the URL", null);
                check(preview.IndexOf("\"download:" + Runner.Sentinel, StringComparison.Ordinal) >= 0,
                    "ui: the template is quoted so the line can be pasted into a shell", null);

                form.radFmtAudio.Checked = true;
                Call(form, "UpdatePreview");
                check(Field<TextBox>(form, "_cmdPreview").Text.IndexOf(" -x ", StringComparison.Ordinal) >= 0,
                    "ui: audio-only adds -x", null);
                check(!form.cmbMergeContainer.Enabled, "ui: merge container greys out for audio-only", null);

                form.radFmtBest.Checked = true;
                form.cmbMaxHeight.SelectedIndex = 4;               // 1080
                Call(form, "UpdatePreview");
                check(Field<TextBox>(form, "_cmdPreview").Text.IndexOf("height<=1080", StringComparison.Ordinal) >= 0,
                    "ui: the height cap reaches the format selector", null);
                form.cmbMaxHeight.SelectedIndex = 0;

                // ---- URL hygiene ----
                urls.Text = "  <https://example.com/a>  \r\n\r\n# a comment\r\nhttps://example.com/a\r\nhttps://example.com/b\r\n";
                var got = (List<string>)Call(form, "GetUrls");
                check(got.Count == 2, "ui: blank lines, comments and duplicates are dropped", "count=" + got.Count);
                check(got[0] == "https://example.com/a", "ui: angle brackets stripped", got.Count > 0 ? got[0] : "");

                // ---- the queue ----
                Call(form, "EnqueueCurrentUrls");
                Pump(100);
                var queue = Field<List<Job>>(form, "_queue");
                var queueView = Field<ListView>(form, "_queueView");
                check(queue.Count == 2, "ui: both URLs queued", "count=" + queue.Count);
                check(queueView.Items.Count == 2, "ui: both rows shown", null);
                check(urls.TextLength == 0, "ui: the URL box is cleared after queueing", null);

                queueView.Items[1].Selected = true;
                Call(form, "MoveSelectedJobs", -1);
                Pump(60);
                check(queue[0].Url == "https://example.com/b", "ui: queue reordering works", queue[0].Url);
                check(queueView.Items[0].SubItems[0].Text == "1", "ui: rows renumber after a move", null);

                Shot(form, "queue");

                Call(form, "ClearQueue");
                Pump(60);
                check(queue.Count == 0, "ui: the queue clears", null);

                // ---- progress rendering, driven through the real tracker ----
                var tracker = Field<ProgressTracker>(form, "_progress");
                var barOverall = Field<ProgressBarEx>(form, "_barOverall");
                var barFile = Field<ProgressBarEx>(form, "_barFile");

                tracker.BeginRun(3);
                tracker.BeginJob(1);
                tracker.Feed(new ProgressInfo
                {
                    Kind = ProgressKind.File,
                    Downloaded = 40 * 1024 * 1024,
                    Total = 100 * 1024 * 1024,
                    Filename = "Some Long Video Title [abc123].f303.webm",
                    PlaylistIndex = 4,
                    PlaylistCount = 10,
                    Speed = 3.4 * 1024 * 1024,
                    Eta = 78,
                    FragIndex = 44,
                    FragCount = 110
                });
                Call(form, "RenderProgress");
                Pump(120);

                check(barOverall.Value > 0 && barOverall.Value < barOverall.Maximum,
                    "ui: the overall bar is part-filled", "value=" + barOverall.Value);
                check(Math.Abs(barFile.Value / (double)barFile.Maximum - 0.4) < 0.01,
                    "ui: the file bar shows 40%", "value=" + barFile.Value);

                var overallText = Field<Label>(form, "_lblOverall").Text;
                var fileText = Field<Label>(form, "_lblFile").Text;
                check(overallText.IndexOf("queue 2 of 3", StringComparison.Ordinal) >= 0,
                    "ui: the overall line names the queue position", overallText);
                check(overallText.IndexOf("video 4 of 10", StringComparison.Ordinal) >= 0,
                    "ui: the overall line names the playlist position", overallText);
                check(fileText.IndexOf("MiB/s", StringComparison.Ordinal) >= 0,
                    "ui: the file line shows a speed", fileText);
                check(fileText.IndexOf("fragment 44 of 110", StringComparison.Ordinal) >= 0,
                    "ui: the file line shows fragments", fileText);

                Shot(form, "progress");

                barOverall.State = BarState.Error;
                Pump(80);
                Shot(form, "progress-error");
                barOverall.State = BarState.Normal;

                // ---- log plumbing ----
                Call(form, "AppendLog", "hello from the test", LineKind.Info);
                Call(form, "AppendLog", "ERROR: a red line", LineKind.Error);
                Call(form, "AppendLog", "WARNING: an amber line", LineKind.Warning);
                Pump(60);
                var log = Field<RichTextBox>(form, "_log");
                check(log.Text.IndexOf("hello from the test", StringComparison.Ordinal) >= 0,
                    "ui: the log receives text", null);

                Field<TextBox>(form, "_findBox").Text = "amber";
                Call(form, "FindInLog");
                Pump(60);
                check(log.SelectedText == "amber", "ui: find selects the match", "got=" + log.SelectedText);

                // ---- reset restores defaults without building a second form ----
                form.txtProxy.Text = "socks5://127.0.0.1:9";
                form.chkEmbedThumbnail.Checked = true;
                var defaults = Field<Dictionary<string, string>>(form, "_defaults");
                check(defaults != null && defaults.Count > 50, "ui: defaults were captured at startup",
                    defaults == null ? "null" : "count=" + defaults.Count);

                Call(form, "ApplyDefaults");
                Pump(80);
                check(form.txtProxy.Text.Length == 0, "ui: reset clears a changed text box", form.txtProxy.Text);
                check(!form.chkEmbedThumbnail.Checked, "ui: reset clears a changed check box", null);

                // The old implementation built a whole second MainForm to find its defaults,
                // leaving that form's 100 ms timer running against a disposed window.
                check(Application.OpenForms.Count == 1, "ui: reset leaves no stray form behind",
                    "open=" + Application.OpenForms.Count);

                // ---- a real download, end to end ----
                if (live) LiveDownload(form, outDir, check);

                Shot(form, "final");

                form.TopMost = false;
                form.Close();
                Pump(400);
                check(form.IsDisposed, "ui: the window closed cleanly", null);
                check(File.Exists(settings), "ui: settings were written on exit", null);

                var written = Settings.Load(settings);
                check(written.ContainsKey("winWidth") && written.ContainsKey("splitter"),
                    "ui: window geometry was saved", null);
                check(written.ContainsKey("advancedUi") && written["advancedUi"] == "1",
                    "ui: interface preference is saved", null);
                using (var restored = new MainForm())
                {
                    restored.Show();
                    Pump(100);
                    check(Field<bool>(restored, "_advancedMode"), "ui: interface preference is restored", null);
                    restored.Close();
                }
            }
            finally
            {
                try { if (form != null && !form.IsDisposed) { form.TopMost = false; form.Dispose(); } }
                catch { }
                try
                {
                    if (hadSettings) File.Copy(backup, settings, true);
                    else if (File.Exists(settings)) File.Delete(settings);
                    if (File.Exists(backup)) File.Delete(backup);
                }
                catch { }
                try { Directory.Delete(outDir, true); }
                catch { }
            }
        }

        // =====================================================================
        private static void LiveDownload(MainForm form, string outDir, Action<bool, string, string> check)
        {
            Console.WriteLine("    running a real download through the window...");

            form.txtOutDir.Text = outDir;
            form.radFmtCustom.Checked = true;
            form.txtCustomFormat.Text = "worstaudio";
            form.chkEmbedMetadata.Checked = false;
            Field<CheckBox>(form, "_chkOpenWhenDone").Checked = false;
            Field<CheckBox>(form, "_chkAlertWhenDone").Checked = false;

            Field<TextBox>(form, "_urls").Text = "ytsearch2:blender open movie trailer";
            Call(form, "StartDownload", false);
            Pump(200);

            var runner = Field<Runner>(form, "_runner");
            check(runner.IsRunning, "live ui: the download started", null);

            var barOverall = Field<ProgressBarEx>(form, "_barOverall");
            bool shotMidway = false;
            double peak = 0;
            bool regressed = false;
            double prev = -1;

            var deadline = DateTime.UtcNow.AddMinutes(5);
            while (DateTime.UtcNow < deadline)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(25);

                var s = Field<ProgressTracker>(form, "_progress").Read();
                if (s.Overall >= 0)
                {
                    if (prev >= 0 && s.Overall < prev - 1e-9) regressed = true;
                    prev = s.Overall;
                    if (s.Overall > peak) peak = s.Overall;
                }

                if (!shotMidway && s.Overall > 0.25)
                {
                    shotMidway = true;
                    Shot(form, "downloading");
                }
                if (!runner.IsRunning && Field<int>(form, "_runPos") < 0) break;
            }
            Pump(600);

            check(!runner.IsRunning, "live ui: the run finished", null);
            check(!regressed, "live ui: the overall bar never went backwards", null);
            check(peak > 0.5, "live ui: the overall bar advanced", "peak=" + peak.ToString("0.000"));
            check(Field<int>(form, "_okCount") == 1, "live ui: one queue entry succeeded",
                "ok=" + Field<int>(form, "_okCount") + " fail=" + Field<int>(form, "_failCount"));
            check(Field<int>(form, "_failCount") == 0, "live ui: nothing failed", null);
            check(barOverall.Value == barOverall.Maximum, "live ui: the bar finishes full",
                barOverall.Value + "/" + barOverall.Maximum);

            var queue = Field<List<Job>>(form, "_queue");
            check(queue.Count == 1 && queue[0].State == JobState.Done, "live ui: the queue row reads Done",
                queue.Count > 0 ? queue[0].StateText : "empty");
            check(queue.Count > 0 && queue[0].Position == "2 / 2",
                "live ui: the row shows the playlist position",
                queue.Count > 0 ? "'" + queue[0].Position + "'" : "");

            var files = Directory.GetFiles(outDir);
            check(files.Length >= 2, "live ui: both files landed in the output folder", "files=" + files.Length);

            // The command echo legitimately contains the template; a *line* that starts with
            // the sentinel would mean a raw progress report reached the log.
            var log = Field<RichTextBox>(form, "_log").Text;
            foreach (var l in log.Replace("\r", "").Split('\n'))
                if (l.TrimStart().StartsWith(Runner.Sentinel, StringComparison.Ordinal))
                {
                    check(false, "live ui: no machine progress line leaked into the log", l);
                    break;
                }
            check(true, "live ui: no machine progress line leaked into the log", null);
            check(log.IndexOf("\x1B", StringComparison.Ordinal) < 0,
                "live ui: no escape sequences leaked into the log", null);
            check(log.IndexOf("Downloading item 2 of 2", StringComparison.Ordinal) >= 0,
                "live ui: the log kept yt-dlp's own output", null);

            Shot(form, "finished");

            // ---- Stop, mid-flight ----
            // Rate-limited so the transfer is certain to still be going when Stop is pressed;
            // otherwise this races the download and passes for the wrong reason.
            Call(form, "ClearQueue");
            form.txtLimitRate.Text = "60K";
            form.txtCustomFormat.Text = "251";
            Field<TextBox>(form, "_urls").Text = "https://www.youtube.com/watch?v=aqz-KE-bpKQ";
            Call(form, "StartDownload", false);

            PumpUntil(delegate { return Field<ProgressTracker>(form, "_progress").Read().Downloaded > 0; }, 90000);
            check(runner.IsRunning, "live ui: the rate-limited download is under way", null);
            Shot(form, "stopping");

            Call(form, "StopAll");
            PumpUntil(delegate { return !runner.IsRunning; }, 30000);
            Pump(800);

            check(!runner.IsRunning, "live ui: Stop terminated the download", null);
            check(Field<Button>(form, "_btnDownload").Enabled, "live ui: Download is usable again after Stop", null);
            check(!Field<Button>(form, "_btnStop").Enabled, "live ui: Stop is disabled once stopped", null);
            check(queue.Count == 1 && queue[0].State == JobState.Stopped, "live ui: the row reads Stopped",
                queue.Count > 0 ? queue[0].StateText : "empty");

            // Nothing of yt-dlp's must survive a Stop.
            int strays = 0;
            foreach (var p in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    if (p.ProcessName.StartsWith("yt-dlp_", StringComparison.OrdinalIgnoreCase)) strays++;
                }
                catch { }
            }
            check(strays == 0, "live ui: no yt-dlp process survived Stop", "strays=" + strays);

            form.txtLimitRate.Text = "";

            FormatsDialog(form, check);
        }

        /// <summary>
        /// The dialog that was reported as coming back empty. Shown non-modally so the test
        /// can pump it, but built and filled by exactly the same code path as the real one.
        /// </summary>
        private static void FormatsDialog(MainForm form, Action<bool, string, string> check)
        {
            Console.WriteLine("    opening the formats dialog...");

            var query = form.NetworkArgsForQuery();
            check(query.Contains("--js-runtimes"),
                "formats: the query passes a JavaScript runtime", string.Join(" ", query.ToArray()));

            var dlg = new FormatsForm("https://www.youtube.com/watch?v=aqz-KE-bpKQ", query);
            try
            {
                dlg.Show(form);
                dlg.TopMost = true;

                var list = Field<ListView>(dlg, "_list");
                PumpUntil(delegate { return list.Items.Count > 0; }, 180000);
                Pump(400);

                check(list.Items.Count > 10, "formats: the table filled", "rows=" + list.Items.Count);
                check(list.Groups.Count >= 2, "formats: rows are grouped", "groups=" + list.Groups.Count);
                check(Field<Button>(dlg, "_ok").Enabled, "formats: Use is enabled once rows arrive", null);

                var info = Field<Label>(dlg, "_info").Text;
                check(info.IndexOf("formats", StringComparison.OrdinalIgnoreCase) >= 0,
                    "formats: the caption reports the count", info);

                // Storyboards are hidden until asked for.
                int hidden = list.Items.Count;
                Field<CheckBox>(dlg, "_showExtras").Checked = true;
                Pump(250);
                check(list.Items.Count > hidden, "formats: storyboards appear when asked for",
                    hidden + " -> " + list.Items.Count);
                Field<CheckBox>(dlg, "_showExtras").Checked = false;
                Pump(250);

                Shot(dlg, "formats-dialog");

                // "Best video + audio" must produce a mergeable pair.
                Call(dlg, "PickBest");
                Pump(200);
                var selector = Field<TextBox>(dlg, "_selection").Text;
                check(selector.IndexOf('+') > 0, "formats: best picks a video+audio pair", selector);
                check(Field<Label>(dlg, "_summary").Text.IndexOf("Estimated", StringComparison.Ordinal) >= 0,
                    "formats: an estimated size is shown", Field<Label>(dlg, "_summary").Text);

                // Sorting by a column must not lose or duplicate rows.
                int before = list.Items.Count;
                Call(dlg, "OnColumnClick", dlg, new ColumnClickEventArgs(5));
                Pump(150);
                check(list.Items.Count == before, "formats: sorting keeps every row",
                    before + " -> " + list.Items.Count);

                // A single row yields a bare id.
                list.SelectedItems.Clear();
                foreach (ListViewItem it in list.Items)
                {
                    var f = it.Tag as Fmt;
                    if (f != null && f.HasVideo && f.HasAudio) { it.Selected = true; break; }
                }
                if (list.SelectedItems.Count == 0) list.Items[0].Selected = true;
                Pump(150);
                check(Field<TextBox>(dlg, "_selection").Text.IndexOf('+') < 0,
                    "formats: one row yields a bare id", Field<TextBox>(dlg, "_selection").Text);

                dlg.TopMost = false;
                dlg.Close();
                Pump(300);
                check(dlg.IsDisposed, "formats: the dialog closed cleanly", null);
            }
            finally
            {
                try { if (!dlg.IsDisposed) { dlg.TopMost = false; dlg.Dispose(); } }
                catch { }
            }
        }
    }
}
