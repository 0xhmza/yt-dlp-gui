using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace YtDlpGui
{
    /// <summary>One row of yt-dlp's format table, read from its JSON rather than its text output.</summary>
    internal class Fmt
    {
        public string Id, Ext, Note, Protocol, VCodec, ACodec, Language, DynamicRange, Container;
        public int Width, Height, Channels;
        public double Fps, Tbr, Abr, Asr;
        public long Size = -1;
        public bool SizeApprox, Drm;

        public bool HasVideo { get { return !IsNone(VCodec); } }
        public bool HasAudio { get { return !IsNone(ACodec); } }

        /// <summary>Storyboards and other things nobody means to download.</summary>
        public bool IsExtra
        {
            get
            {
                if (HasVideo || HasAudio) return false;
                return true;
            }
        }

        private static bool IsNone(string codec)
        {
            return string.IsNullOrEmpty(codec) || codec == "none";
        }

        public string ResolutionText
        {
            get
            {
                if (!HasVideo) return "audio only";
                if (Height > 0) return (Width > 0 ? Width + "x" + Height : Height + "p");
                return "video";
            }
        }

        public string QualityLabel
        {
            get
            {
                if (!HasVideo) return Abr > 0 ? Math.Round(Abr) + "k audio" : "audio";
                return Height > 0 ? Height + "p" + (Fps >= 50 ? Math.Round(Fps).ToString(CultureInfo.InvariantCulture) : "") : "video";
            }
        }
    }

    /// <summary>A details ListView that does not flicker while it is being filled.</summary>
    internal class BufferedListView : ListView
    {
        public BufferedListView()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
    }

    /// <summary>
    /// Fetches the real format table for a URL and lets the user pick one, or a video row and
    /// an audio row to be merged. The list is built from "--print %(formats)j", so the columns
    /// carry real numbers and can be sorted; nothing depends on the width of yt-dlp's own
    /// text table.
    /// </summary>
    internal class FormatsForm : Form
    {
        private readonly string _url;
        private readonly List<string> _queryArgs;

        private BufferedListView _list;
        private Label _info;
        private TextBox _selection;
        private TextBox _raw;
        private Button _ok, _cancel, _refresh, _bestVa;
        private CheckBox _showExtras;
        private Label _summary;
        private SplitContainer _split;

        private ToolRun _run;
        private readonly List<Fmt> _formats = new List<Fmt>();
        private string _title, _videoId;
        private int _sortColumn = -1;
        private bool _sortAscending;
        private readonly Timer _tick = new Timer();
        private DateTime _startedAt;
        private bool _updatingSelection;

        public string SelectedFormat { get; private set; }

        public FormatsForm(string url, List<string> queryArgs)
        {
            _url = url;
            _queryArgs = queryArgs ?? new List<string>();

            Text = "Available formats";
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            ClientSize = new Size(1020, 620);
            MinimumSize = new Size(700, 420);

            Build();

            _tick.Interval = 250;
            _tick.Tick += delegate { TickWhileFetching(); };

            Shown += delegate { StartFetch(); };
            FormClosing += delegate
            {
                _tick.Stop();
                var r = _run;
                if (r != null) r.Cancel();
            };
        }

        // =====================================================================
        private void Build()
        {
            _info = new Label();
            _info.Dock = DockStyle.Top;
            _info.AutoSize = false;
            _info.Height = TextRenderer.MeasureText("Wg", Font).Height + 10;
            _info.Text = "Fetching formats...";
            _info.Padding = new Padding(8, 5, 8, 0);

            _list = new BufferedListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.GridLines = false;
            _list.HideSelection = false;
            _list.MultiSelect = true;
            _list.HeaderStyle = ColumnHeaderStyle.Clickable;
            _list.ShowItemToolTips = true;

            _list.Columns.Add("ID", 80);
            _list.Columns.Add("Quality", 85);
            _list.Columns.Add("Resolution", 95);
            _list.Columns.Add("FPS", 45, HorizontalAlignment.Right);
            _list.Columns.Add("Ext", 50);
            _list.Columns.Add("Size", 85, HorizontalAlignment.Right);
            _list.Columns.Add("Bitrate", 70, HorizontalAlignment.Right);
            _list.Columns.Add("Video codec", 125);
            _list.Columns.Add("Audio codec", 110);
            _list.Columns.Add("Proto", 85);
            _list.Columns.Add("Notes", 170);

            _list.SelectedIndexChanged += delegate { UpdateSelectionBox(); };
            _list.ColumnClick += OnColumnClick;
            _list.DoubleClick += delegate
            {
                UpdateSelectionBox();
                if (_selection.TextLength > 0) Accept();
            };
            _list.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && _selection.TextLength > 0) { Accept(); e.Handled = true; }
            };

            // The raw output pane is only interesting when something went wrong, so it starts
            // collapsed and opens itself if the fetch fails.
            _raw = new TextBox();
            _raw.Dock = DockStyle.Fill;
            _raw.Multiline = true;
            _raw.ReadOnly = true;
            _raw.ScrollBars = ScrollBars.Both;
            _raw.WordWrap = false;
            _raw.BorderStyle = BorderStyle.None;
            _raw.BackColor = SystemColors.Control;
            _raw.Font = new Font(FontFamily.GenericMonospace, Font.SizeInPoints);

            _split = new SplitContainer();
            _split.Dock = DockStyle.Fill;
            _split.Orientation = Orientation.Horizontal;
            _split.SplitterWidth = 6;
            _split.Panel1.Controls.Add(_list);
            _split.Panel2.Controls.Add(_raw);
            _split.Panel2Collapsed = true;

            // ---- bottom ------------------------------------------------------
            var bottom = new TableLayoutPanel();
            bottom.Dock = DockStyle.Bottom;
            bottom.AutoSize = true;
            bottom.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            bottom.ColumnCount = 2;
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.Padding = new Padding(8, 4, 8, 8);
            bottom.RowCount = 4;
            for (int i = 0; i < 4; i++) bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var topRow = new FlowLayoutPanel();
            topRow.AutoSize = true;
            topRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            topRow.WrapContents = false;
            topRow.Margin = new Padding(0);
            _showExtras = Ui.Chk("showExtras", "Show storyboards and images");
            _showExtras.CheckedChanged += delegate { Populate(); };
            _bestVa = Ui.Btn("Best video + audio", delegate { PickBest(); });
            _refresh = Ui.Btn("Refresh", delegate { StartFetch(); });
            topRow.Controls.Add(_showExtras);
            topRow.Controls.Add(_bestVa);
            topRow.Controls.Add(_refresh);

            _summary = new Label();
            _summary.AutoSize = true;
            _summary.ForeColor = SystemColors.GrayText;
            _summary.Margin = new Padding(3, 8, 3, 3);

            var selLabel = new Label();
            selLabel.Text = "Format selector (-f)";
            selLabel.AutoSize = true;
            selLabel.Anchor = AnchorStyles.Left;
            selLabel.Margin = new Padding(3, 8, 8, 3);

            _selection = new TextBox();
            _selection.Dock = DockStyle.Fill;
            _selection.Font = new Font(FontFamily.GenericMonospace, Font.SizeInPoints);
            _selection.TextChanged += delegate { if (!_updatingSelection) _summary.Text = ""; };

            var buttons = new FlowLayoutPanel();
            buttons.AutoSize = true;
            buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttons.WrapContents = false;
            buttons.Margin = new Padding(0);
            _ok = Ui.Btn("Use this format", delegate { Accept(); }, 130);
            _cancel = Ui.Btn("Cancel", delegate { DialogResult = DialogResult.Cancel; Close(); }, 90);
            buttons.Controls.Add(_ok);
            buttons.Controls.Add(_cancel);

            var selRow = new TableLayoutPanel();
            selRow.Dock = DockStyle.Fill;
            selRow.AutoSize = true;
            selRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            selRow.ColumnCount = 3;
            selRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            selRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            selRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            selRow.Margin = new Padding(0);
            selRow.Controls.Add(selLabel, 0, 0);
            selRow.Controls.Add(_selection, 1, 0);
            selRow.Controls.Add(buttons, 2, 0);

            var hint = Ui.Note("Pick one row, or a video row and an audio row together to merge them. " +
                               "Click a column heading to sort. You can also type a selector by hand.");

            bottom.Controls.Add(topRow, 0, 0);
            bottom.SetColumnSpan(topRow, 2);
            bottom.Controls.Add(_summary, 0, 1);
            bottom.SetColumnSpan(_summary, 2);
            bottom.Controls.Add(selRow, 0, 2);
            bottom.SetColumnSpan(selRow, 2);
            bottom.Controls.Add(hint, 0, 3);
            bottom.SetColumnSpan(hint, 2);

            Controls.Add(_split);
            Controls.Add(bottom);
            Controls.Add(_info);

            AcceptButton = _ok;
            CancelButton = _cancel;
            _ok.Enabled = false;
            _bestVa.Enabled = false;
        }

        // =====================================================================
        private void StartFetch()
        {
            var previous = _run;
            if (previous != null) previous.Cancel();

            _formats.Clear();
            _list.Items.Clear();
            _list.Groups.Clear();
            _ok.Enabled = false;
            _bestVa.Enabled = false;
            _refresh.Enabled = false;
            _split.Panel2Collapsed = true;
            _raw.Clear();
            _summary.Text = "";
            _startedAt = DateTime.UtcNow;
            _info.Text = "Fetching formats for " + _url + " ...";
            _tick.Start();

            var args = new List<string>(_queryArgs);
            args.Add("--skip-download");
            args.Add("--no-warnings");
            // One video is enough to choose a format, and it keeps a 500-entry playlist from
            // being walked just to fill this list.
            args.Add("--playlist-items");
            args.Add("1");
            args.Add("--print");
            args.Add("%(id)s");
            args.Add("--print");
            args.Add("%(title)s");
            args.Add("--print");
            args.Add("%(formats)j");
            args.Add(_url);

            _run = ToolRun.Begin(App.YtDlpPath, args, 180000, delegate (ToolRun r)
            {
                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke((MethodInvoker)delegate { FetchDone(r); });
                }
                catch { }
            });
        }

        private void TickWhileFetching()
        {
            var secs = (int)(DateTime.UtcNow - _startedAt).TotalSeconds;
            _info.Text = "Fetching formats for " + _url + " ...  (" + secs + "s - Cancel closes this window)";
        }

        private void FetchDone(ToolRun r)
        {
            _tick.Stop();
            _refresh.Enabled = true;
            if (_run != r) return;          // a Refresh overtook this one
            if (r.Cancelled) { _info.Text = "Cancelled."; return; }

            ParseFormats(r.StdOut);

            if (_formats.Count == 0)
            {
                _info.Text = "No formats were returned for " + _url;
                _info.ForeColor = Color.Firebrick;
                _raw.Text = Describe(r);
                _split.Panel2Collapsed = false;
                try { _split.SplitterDistance = Math.Max(80, _split.Height / 2); }
                catch { }
                return;
            }

            _info.ForeColor = SystemColors.ControlText;
            _info.Text = _formats.Count + " formats" +
                         (string.IsNullOrEmpty(_title) ? "" : "  -  " + _title) +
                         (string.IsNullOrEmpty(_videoId) ? "" : "  [" + _videoId + "]");

            if (r.StdErr.Trim().Length > 0) _raw.Text = r.StdErr;

            Populate();
            _ok.Enabled = true;
            _bestVa.Enabled = true;
        }

        private static string Describe(ToolRun r)
        {
            var sb = new StringBuilder();
            if (r.StdErr.Trim().Length > 0) sb.AppendLine(r.StdErr.TrimEnd());
            if (r.StdOut.Trim().Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("--- standard output ---");
                sb.AppendLine(r.StdOut.TrimEnd());
            }
            if (sb.Length == 0) sb.AppendLine("yt-dlp produced no output at all (exit code " + r.ExitCode + ").");

            sb.AppendLine();
            sb.AppendLine("Things worth checking:");
            sb.AppendLine("  - the URL opens in a browser and the video is not private or region-locked;");
            sb.AppendLine("  - a JavaScript runtime is present (Tools > Check dependencies) - YouTube needs one;");
            sb.AppendLine("  - cookies, if the site requires you to be signed in (Authentication tab);");
            sb.AppendLine("  - yt-dlp itself is current (Tools > Update yt-dlp).");
            return sb.ToString();
        }

        /// <summary>Reads the three printed lines: id, title, and the formats array as JSON.</summary>
        private void ParseFormats(string stdout)
        {
            _formats.Clear();
            _title = null;
            _videoId = null;
            if (string.IsNullOrEmpty(stdout)) return;

            var lines = stdout.Replace("\r", "").Split('\n');
            string jsonLine = null;

            for (int i = 0; i < lines.Length; i++)
            {
                var l = lines[i].Trim();
                if (l.Length == 0) continue;
                if (l[0] == '[' && jsonLine == null && l.Length > 2) { jsonLine = lines[i]; continue; }
                if (_videoId == null) { _videoId = l; continue; }
                if (_title == null) { _title = l; }
            }
            if (jsonLine == null) return;

            object parsed;
            if (!Json.TryParse(jsonLine, out parsed)) return;

            var arr = Json.Arr(parsed);
            if (arr == null) return;

            foreach (var o in arr)
            {
                var f = ReadFormat(o);
                if (f != null) _formats.Add(f);
            }
        }

        private static Fmt ReadFormat(object o)
        {
            var id = Json.Str(o, "format_id");
            if (id == null) return null;

            var f = new Fmt();
            f.Id = id;
            f.Ext = Json.Str(o, "ext") ?? "";
            f.Note = Json.Str(o, "format_note") ?? "";
            f.Protocol = Json.Str(o, "protocol") ?? "";
            f.VCodec = Json.Str(o, "vcodec") ?? "none";
            f.ACodec = Json.Str(o, "acodec") ?? "none";
            f.Language = Json.Str(o, "language");
            f.DynamicRange = Json.Str(o, "dynamic_range");
            f.Container = Json.Str(o, "container");
            f.Drm = Json.Flag(o, "has_drm");

            f.Width = (int)(Json.Num(o, "width") ?? 0);
            f.Height = (int)(Json.Num(o, "height") ?? 0);
            f.Channels = (int)(Json.Num(o, "audio_channels") ?? 0);
            f.Fps = Json.Num(o, "fps") ?? 0;
            f.Tbr = Json.Num(o, "tbr") ?? 0;
            f.Abr = Json.Num(o, "abr") ?? 0;
            f.Asr = Json.Num(o, "asr") ?? 0;

            var size = Json.Num(o, "filesize");
            if (size.HasValue && size.Value > 0) { f.Size = (long)size.Value; }
            else
            {
                var approx = Json.Num(o, "filesize_approx");
                if (approx.HasValue && approx.Value > 0) { f.Size = (long)approx.Value; f.SizeApprox = true; }
            }

            // A resolution string is the only clue for formats that report no width/height.
            if (f.Height == 0)
            {
                var res = Json.Str(o, "resolution");
                if (res != null && res.IndexOf('x') > 0)
                {
                    var parts = res.Split('x');
                    int w, h;
                    if (parts.Length == 2 &&
                        int.TryParse(parts[0], out w) && int.TryParse(parts[1], out h))
                    { f.Width = w; f.Height = h; }
                }
            }
            return f;
        }

        // =====================================================================
        private void Populate()
        {
            var gCombined = new ListViewGroup("Video + audio (ready to play)");
            var gVideo = new ListViewGroup("Video only (merge with an audio row)");
            var gAudio = new ListViewGroup("Audio only");
            var gExtra = new ListViewGroup("Storyboards and images");

            var rows = new List<Fmt>();
            foreach (var f in _formats)
            {
                if (f.IsExtra && !_showExtras.Checked) continue;
                rows.Add(f);
            }
            rows.Sort(DefaultOrder);

            _list.BeginUpdate();
            try
            {
                // Filling a sorted ListView re-sorts on every insert; put the comparer back
                // once the rows are in.
                _list.ListViewItemSorter = null;
                _list.Items.Clear();
                _list.Groups.Clear();
                _list.Groups.AddRange(new[] { gCombined, gVideo, gAudio, gExtra });

                foreach (var f in rows)
                {
                    var it = new ListViewItem(new[]
                    {
                        f.Id,
                        f.QualityLabel,
                        f.ResolutionText,
                        f.Fps > 0 ? Math.Round(f.Fps).ToString(CultureInfo.InvariantCulture) : "",
                        f.Ext,
                        f.Size > 0 ? (f.SizeApprox ? "~" : "") + Ui.Size(f.Size) : "",
                        f.Tbr > 0 ? Math.Round(f.Tbr) + "k" : "",
                        Short(f.VCodec),
                        Short(f.ACodec),
                        ShortProtocol(f.Protocol),
                        BuildNote(f)
                    });
                    it.Tag = f;

                    if (f.IsExtra) it.Group = gExtra;
                    else if (f.HasVideo && f.HasAudio) it.Group = gCombined;
                    else if (f.HasVideo) it.Group = gVideo;
                    else it.Group = gAudio;

                    if (f.Drm) it.ForeColor = Color.Firebrick;
                    else if (f.IsExtra) it.ForeColor = SystemColors.GrayText;

                    it.ToolTipText = f.Id + "  " + f.ResolutionText +
                                     (f.Size > 0 ? "  " + Ui.Size(f.Size) : "") +
                                     "\r\nvideo: " + f.VCodec + "   audio: " + f.ACodec;
                    _list.Items.Add(it);
                }

                // Empty groups still draw their heading, which looks like a bug.
                for (int i = _list.Groups.Count - 1; i >= 0; i--)
                    if (_list.Groups[i].Items.Count == 0) _list.Groups.RemoveAt(i);

                if (_sortColumn >= 0)
                {
                    _list.ListViewItemSorter = _sorter;
                    _list.Sort();
                }
            }
            finally { _list.EndUpdate(); }

            UpdateSelectionBox();
        }

        private static string BuildNote(Fmt f)
        {
            var parts = new List<string>();
            if (f.Drm) parts.Add("DRM");

            // The note is worth a column of its own only when it says something the Quality
            // column has not already said - on YouTube it is usually just "1080p60" again.
            if (!string.IsNullOrEmpty(f.Note) &&
                !string.Equals(f.Note, f.QualityLabel, StringComparison.OrdinalIgnoreCase))
                parts.Add(f.Note);

            if (!string.IsNullOrEmpty(f.DynamicRange) && f.DynamicRange != "SDR") parts.Add(f.DynamicRange);
            if (f.Channels > 0) parts.Add(f.Channels + "ch");
            if (f.Asr > 0) parts.Add(Math.Round(f.Asr / 1000.0) + "kHz");
            if (!string.IsNullOrEmpty(f.Language)) parts.Add(f.Language);
            return string.Join(", ", parts.ToArray());
        }

        private static string Short(string codec)
        {
            if (string.IsNullOrEmpty(codec) || codec == "none") return "-";
            return codec;
        }

        /// <summary>yt-dlp's protocol names are long and the distinctions rarely matter here.</summary>
        private static string ShortProtocol(string protocol)
        {
            if (string.IsNullOrEmpty(protocol)) return "";
            switch (protocol)
            {
                case "m3u8_native": return "m3u8";
                case "http_dash_segments": return "dash";
                case "https": return "https";
                default: return protocol;
            }
        }

        /// <summary>Best first inside each group: that is what people are looking for.</summary>
        private static int DefaultOrder(Fmt a, Fmt b)
        {
            int c = Rank(b).CompareTo(Rank(a));
            if (c != 0) return c;
            c = b.Height.CompareTo(a.Height);
            if (c != 0) return c;
            c = b.Fps.CompareTo(a.Fps);
            if (c != 0) return c;
            c = b.Tbr.CompareTo(a.Tbr);
            if (c != 0) return c;
            return string.CompareOrdinal(a.Id, b.Id);
        }

        private static int Rank(Fmt f)
        {
            if (f.IsExtra) return 0;
            if (f.HasVideo && f.HasAudio) return 3;
            if (f.HasVideo) return 2;
            return 1;
        }

        // ---- column sorting ---------------------------------------------------
        /// <summary>
        /// Sorting is handed to the ListView itself rather than done by rebuilding the list:
        /// that keeps every row inside its group, and keeps the selection intact.
        /// </summary>
        private class RowSorter : System.Collections.IComparer
        {
            public int Column;
            public bool Ascending;

            public int Compare(object x, object y)
            {
                var a = ((ListViewItem)x).Tag as Fmt;
                var b = ((ListViewItem)y).Tag as Fmt;
                if (a == null || b == null) return 0;
                int r = CompareBy(Column, a, b);
                if (r == 0) r = string.CompareOrdinal(a.Id, b.Id);
                return Ascending ? r : -r;
            }
        }

        private readonly RowSorter _sorter = new RowSorter();

        private void OnColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (e.Column == _sortColumn) _sortAscending = !_sortAscending;
            else { _sortColumn = e.Column; _sortAscending = false; }

            _sorter.Column = _sortColumn;
            _sorter.Ascending = _sortAscending;
            _list.ListViewItemSorter = _sorter;
            _list.Sort();
        }

        private static int CompareBy(int column, Fmt a, Fmt b)
        {
            switch (column)
            {
                case 0: return string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
                case 1:
                case 2: return a.Height != b.Height ? a.Height.CompareTo(b.Height) : a.Tbr.CompareTo(b.Tbr);
                case 3: return a.Fps.CompareTo(b.Fps);
                case 4: return string.Compare(a.Ext, b.Ext, StringComparison.OrdinalIgnoreCase);
                case 5: return a.Size.CompareTo(b.Size);
                case 6: return a.Tbr.CompareTo(b.Tbr);
                case 7: return string.Compare(a.VCodec, b.VCodec, StringComparison.OrdinalIgnoreCase);
                case 8: return string.Compare(a.ACodec, b.ACodec, StringComparison.OrdinalIgnoreCase);
                case 9: return string.Compare(a.Protocol, b.Protocol, StringComparison.OrdinalIgnoreCase);
                default: return string.Compare(a.Note, b.Note, StringComparison.OrdinalIgnoreCase);
            }
        }

        // ---- selection ---------------------------------------------------------
        private void UpdateSelectionBox()
        {
            var videos = new List<Fmt>();
            var audios = new List<Fmt>();
            var both = new List<Fmt>();

            foreach (ListViewItem it in _list.SelectedItems)
            {
                var f = it.Tag as Fmt;
                if (f == null) continue;
                if (f.HasVideo && f.HasAudio) both.Add(f);
                else if (f.HasVideo) videos.Add(f);
                else if (f.HasAudio) audios.Add(f);
                else both.Add(f);                       // storyboard: treat as a plain pick
            }

            if (videos.Count == 0 && audios.Count == 0 && both.Count == 0) { _summary.Text = ""; return; }

            string selector;
            long size = 0;
            bool approx = false, sizeKnown = true;

            if (videos.Count > 0 && audios.Count > 0)
            {
                selector = Chain(videos) + "+" + Chain(audios);
                Accumulate(videos[0], ref size, ref approx, ref sizeKnown);
                Accumulate(audios[0], ref size, ref approx, ref sizeKnown);
            }
            else
            {
                var all = new List<Fmt>();
                all.AddRange(both);
                all.AddRange(videos);
                all.AddRange(audios);
                selector = Chain(all);
                Accumulate(all[0], ref size, ref approx, ref sizeKnown);
            }

            _updatingSelection = true;
            try { _selection.Text = selector; }
            finally { _updatingSelection = false; }

            _summary.Text = sizeKnown && size > 0
                ? "Estimated download: " + (approx ? "~" : "") + Ui.Size(size)
                : "Estimated download: unknown (the site did not report a size)";
        }

        private static void Accumulate(Fmt f, ref long size, ref bool approx, ref bool known)
        {
            if (f.Size <= 0) { known = false; return; }
            size += f.Size;
            if (f.SizeApprox) approx = true;
        }

        /// <summary>ids joined as a yt-dlp fallback chain, parenthesised when it needs to be.</summary>
        private static string Chain(List<Fmt> items)
        {
            if (items.Count == 1) return items[0].Id;
            var ids = new List<string>();
            foreach (var f in items) ids.Add(f.Id);
            return "(" + string.Join("/", ids.ToArray()) + ")";
        }

        private void PickBest()
        {
            Fmt bestCombined = null, bestVideo = null, bestAudio = null;
            foreach (var f in _formats)
            {
                if (f.IsExtra || f.Drm) continue;
                if (f.HasVideo && f.HasAudio) { if (Better(f, bestCombined)) bestCombined = f; }
                else if (f.HasVideo) { if (Better(f, bestVideo)) bestVideo = f; }
                else if (f.HasAudio) { if (BetterAudio(f, bestAudio)) bestAudio = f; }
            }

            _list.SelectedItems.Clear();
            if (bestVideo != null && bestAudio != null) { Select(bestVideo); Select(bestAudio); }
            else if (bestCombined != null) Select(bestCombined);
            else if (bestVideo != null) Select(bestVideo);
            else if (bestAudio != null) Select(bestAudio);

            UpdateSelectionBox();
            _list.Focus();
        }

        private static bool Better(Fmt candidate, Fmt current)
        {
            if (current == null) return true;
            if (candidate.Height != current.Height) return candidate.Height > current.Height;
            if (candidate.Fps != current.Fps) return candidate.Fps > current.Fps;
            return candidate.Tbr > current.Tbr;
        }

        private static bool BetterAudio(Fmt candidate, Fmt current)
        {
            if (current == null) return true;
            if (candidate.Abr != current.Abr) return candidate.Abr > current.Abr;
            return candidate.Tbr > current.Tbr;
        }

        private void Select(Fmt f)
        {
            foreach (ListViewItem it in _list.Items)
            {
                if (ReferenceEquals(it.Tag, f))
                {
                    it.Selected = true;
                    it.EnsureVisible();
                    return;
                }
            }
        }

        private void Accept()
        {
            var sel = _selection.Text.Trim();
            if (sel.Length == 0)
            {
                MessageBox.Show(this, "Select a format row first, or type a selector.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SelectedFormat = sel;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tick.Dispose();
                var r = _run;
                if (r != null) r.Cancel();
            }
            base.Dispose(disposing);
        }
    }
}
