using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace YtDlpGui
{
    /// <summary>A scroll host that paints without the flicker of a plain Panel.</summary>
    internal class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
    }

    internal enum BarState { Normal = 1, Error = 2, Paused = 3 }

    /// <summary>
    /// A ProgressBar that can turn red or amber, and that moves to a new value at once.
    /// The stock control animates towards whatever it is given, so on a fast download it
    /// permanently lags the real figure, and on completion it is still crawling forward
    /// after the job has finished.
    /// </summary>
    internal class ProgressBarEx : ProgressBar
    {
        private const int PBM_SETSTATE = 0x0410;
        private BarState _state = BarState.Normal;

        public ProgressBarEx()
        {
            Maximum = 1000;
            Style = ProgressBarStyle.Continuous;
        }

        public BarState State
        {
            get { return _state; }
            set
            {
                _state = value;
                Apply();
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Apply();
        }

        private void Apply()
        {
            if (!IsHandleCreated) return;
            try { Native.SendMessage(Handle, PBM_SETSTATE, (IntPtr)(int)_state, IntPtr.Zero); }
            catch { }
        }

        /// <summary>Jumps straight to a value with no sliding animation.</summary>
        public void SetValueInstant(int value)
        {
            if (value < Minimum) value = Minimum;
            if (value > Maximum) value = Maximum;

            // Going backwards is instant; going forwards animates. Overshoot by one and step
            // back, which lands on the requested value immediately.
            if (value == Maximum)
            {
                Maximum = value + 1;
                Value = value + 1;
                Value = value;
                Maximum = value;
            }
            else
            {
                Value = value + 1;
                Value = value;
            }
        }

        /// <summary>Sets the bar from a 0..1 fraction; a negative fraction means "unknown".</summary>
        public void SetFraction(double fraction)
        {
            if (fraction < 0 || double.IsNaN(fraction))
            {
                if (Style != ProgressBarStyle.Marquee) { MarqueeAnimationSpeed = 30; Style = ProgressBarStyle.Marquee; }
                return;
            }
            if (Style != ProgressBarStyle.Continuous) { Style = ProgressBarStyle.Continuous; Apply(); }
            SetValueInstant((int)Math.Round(Math.Min(1.0, fraction) * Maximum));
        }
    }

    /// <summary>
    /// Small helpers for building plain-WinForms layouts that auto-size and follow the
    /// system font/DPI. Every tab in the app is assembled through these.
    /// </summary>
    internal static class Ui
    {
        // ---- human-readable quantities ---------------------------------------
        private static readonly string[] SizeUnits = { "B", "KiB", "MiB", "GiB", "TiB", "PiB" };

        /// <summary>"1.23 MiB". Negative or zero returns an empty string.</summary>
        public static string Size(double bytes)
        {
            if (bytes <= 0 || double.IsNaN(bytes) || double.IsInfinity(bytes)) return "";
            int u = 0;
            while (bytes >= 1024 && u < SizeUnits.Length - 1) { bytes /= 1024; u++; }
            var format = bytes >= 100 || u == 0 ? "0" : bytes >= 10 ? "0.0" : "0.00";
            return bytes.ToString(format, CultureInfo.InvariantCulture) + " " + SizeUnits[u];
        }

        /// <summary>"1.23 MiB/s".</summary>
        public static string Rate(double bytesPerSecond)
        {
            var s = Size(bytesPerSecond);
            return s.Length == 0 ? "" : s + "/s";
        }

        /// <summary>"04:31" or "1:02:03". Negative returns an empty string.</summary>
        public static string Duration(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds > 359999) return "";
            // Away-from-zero, not the default banker's rounding: "12.5 seconds left" showing
            // as 00:12 while 13.5 shows as 00:14 is the kind of thing people notice.
            var t = TimeSpan.FromSeconds(Math.Round(seconds, MidpointRounding.AwayFromZero));
            return t.TotalHours >= 1
                ? ((int)t.TotalHours).ToString(CultureInfo.InvariantCulture) + t.ToString("\\:mm\\:ss")
                : t.ToString("mm\\:ss");
        }

        /// <summary>Joins the non-empty parts with a separator, so absent figures leave no gap.</summary>
        public static string Join(string separator, params string[] parts)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var p in parts)
            {
                if (string.IsNullOrEmpty(p)) continue;
                if (sb.Length > 0) sb.Append(separator);
                sb.Append(p);
            }
            return sb.ToString();
        }

        public static readonly ToolTip Tips = new ToolTip
        {
            AutoPopDelay = 20000,
            InitialDelay = 400,
            ReshowDelay = 150,
            ShowAlways = true
        };

        public const int Pad = 6;

        /// <summary>Creates a scrollable tab page and returns the single-column host that
        /// group boxes are appended to.</summary>
        public static TableLayoutPanel Tab(TabControl tabs, string title)
        {
            var page = new TabPage(title);
            page.UseVisualStyleBackColor = true;
            page.Padding = new Padding(Pad);

            var scroll = new BufferedPanel();
            scroll.Dock = DockStyle.Fill;
            scroll.AutoScroll = true;

            var host = new TableLayoutPanel();
            host.Dock = DockStyle.Top;
            host.AutoSize = true;
            host.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            host.GrowStyle = TableLayoutPanelGrowStyle.AddRows;
            host.ColumnCount = 1;
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            scroll.Controls.Add(host);
            page.Controls.Add(scroll);
            tabs.TabPages.Add(page);
            return host;
        }

        /// <summary>Adds a group box to a tab host and returns its two-column inner grid.</summary>
        public static TableLayoutPanel Group(TableLayoutPanel host, string title)
        {
            var box = new GroupBox();
            box.Text = title;
            box.Dock = DockStyle.Fill;
            // Sized from the grid's real height below. GroupBox.AutoSize would use the grid's
            // *preferred* height, which TableLayoutPanel over-reports whenever a control spans
            // both columns, leaving a band of dead space in every group.
            box.AutoSize = false;
            box.Padding = new Padding(Pad, Pad, Pad, Pad);
            box.Margin = new Padding(0, 0, 0, Pad + 2);

            var grid = new TableLayoutPanel();
            // Top, not Fill: a filled grid hands its leftover height to the last auto-size
            // row, which pushes that row's label out of line with its control.
            grid.Dock = DockStyle.Top;
            grid.AutoSize = true;
            grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            grid.GrowStyle = TableLayoutPanelGrowStyle.AddRows;
            grid.ColumnCount = 2;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            box.Controls.Add(grid);

            // Keep the box exactly as tall as its contents, at any font size or DPI.
            // Driven by SizeChanged only: hooking Layout re-entered the layout engine on every
            // pass, which is what made switching tabs visibly slow.
            grid.SizeChanged += delegate
            {
                int wanted = grid.Bottom + box.Padding.Bottom;
                if (box.Height != wanted) box.Height = wanted;
            };

            host.Controls.Add(box, 0, NextRow(host));
            return grid;
        }

        /// <summary>
        /// Reserves the next row of a grid. The row index is tracked on the panel itself:
        /// relying on RowCount is unreliable once controls are placed explicitly.
        /// </summary>
        private static int NextRow(TableLayoutPanel grid)
        {
            int r = grid.Tag == null ? 0 : (int)grid.Tag;
            grid.Tag = r + 1;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if (grid.RowCount < r + 1) grid.RowCount = r + 1;
            return r;
        }

        /// <summary>Label on the left, control filling the rest of the row.</summary>
        public static T Row<T>(TableLayoutPanel grid, string label, T control, string tip = null) where T : Control
        {
            var lbl = new Label();
            lbl.Text = label;
            lbl.AutoSize = true;
            lbl.Anchor = AnchorStyles.Left;
            lbl.Margin = new Padding(3, 6, 8, 3);

            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(3, 3, 3, 3);

            int r = NextRow(grid);
            grid.Controls.Add(lbl, 0, r);
            grid.Controls.Add(control, 1, r);

            if (tip != null)
            {
                Tips.SetToolTip(control, tip);
                Tips.SetToolTip(lbl, tip);
            }
            return control;
        }

        /// <summary>A control spanning both columns.</summary>
        public static T Full<T>(TableLayoutPanel grid, T control, string tip = null) where T : Control
        {
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(3, 3, 3, 3);

            int r = NextRow(grid);
            grid.Controls.Add(control, 0, r);
            grid.SetColumnSpan(control, 2);

            if (tip != null) Tips.SetToolTip(control, tip);
            return control;
        }

        /// <summary>A wrapping row of small controls (typically check boxes) spanning both columns.</summary>
        public static FlowLayoutPanel Flow(TableLayoutPanel grid, params Control[] items)
        {
            var flow = new FlowLayoutPanel();
            flow.AutoSize = true;
            flow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flow.FlowDirection = FlowDirection.LeftToRight;
            flow.WrapContents = true;
            flow.Margin = new Padding(0);
            foreach (var c in items)
            {
                c.Margin = new Padding(3, 3, 14, 3);
                flow.Controls.Add(c);
            }
            return Full(grid, flow);
        }

        /// <summary>A label plus a right-hand row of controls kept at their natural size.</summary>
        public static FlowLayoutPanel RowFlow(TableLayoutPanel grid, string label, params Control[] items)
        {
            var flow = new FlowLayoutPanel();
            flow.AutoSize = true;
            flow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flow.FlowDirection = FlowDirection.LeftToRight;
            // Must not wrap: a wrapping panel that is docked into a table cell reports a tall
            // preferred size, which pushes the row's label out of alignment.
            flow.WrapContents = false;
            flow.Margin = new Padding(0);
            foreach (var c in items)
            {
                c.Margin = new Padding(3, 3, 10, 3);
                flow.Controls.Add(c);
            }
            Row(grid, label, flow);

            // Let the panel size itself instead of filling the cell: a docked auto-size flow
            // panel reports an inflated preferred height and throws the row out of alignment.
            flow.Dock = DockStyle.None;
            flow.Anchor = AnchorStyles.Left;
            return flow;
        }

        public static CheckBox Chk(string name, string text, string tip = null)
        {
            var c = new CheckBox();
            c.Name = name;
            c.Text = text;
            c.AutoSize = true;
            if (tip != null) Tips.SetToolTip(c, tip);
            return c;
        }

        public static RadioButton Rad(string name, string text, string tip = null)
        {
            var r = new RadioButton();
            r.Name = name;
            r.Text = text;
            r.AutoSize = true;
            if (tip != null) Tips.SetToolTip(r, tip);
            return r;
        }

        public static TextBox Txt(string name, string text = "")
        {
            var t = new TextBox();
            t.Name = name;
            t.Text = text;
            return t;
        }

        public static TextBox Multi(string name, int rows = 3)
        {
            var t = new TextBox();
            t.Name = name;
            t.Multiline = true;
            t.ScrollBars = ScrollBars.Vertical;
            t.WordWrap = false;
            t.Height = TextRenderer.MeasureText("Wg", SystemFonts.MessageBoxFont).Height * rows + 8;
            return t;
        }

        public static ComboBox Cmb(string name, string[] items, bool editable = false, int selected = 0)
        {
            var c = new ComboBox();
            c.Name = name;
            c.DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList;
            if (items != null && items.Length > 0)
            {
                c.Items.AddRange(items);
                if (selected >= 0 && selected < items.Length) c.SelectedIndex = selected;
            }
            return c;
        }

        public static NumericUpDown Num(string name, decimal min, decimal max, decimal value, int decimals = 0)
        {
            var n = new NumericUpDown();
            n.Name = name;
            n.Minimum = min;
            n.Maximum = max;
            n.Value = value;
            n.DecimalPlaces = decimals;
            n.Width = 90;
            n.AutoSize = false;
            return n;
        }

        public static Button Btn(string text, EventHandler onClick, int width = 0)
        {
            var b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            if (width > 0) { b.AutoSize = false; b.Width = width; }
            if (onClick != null) b.Click += onClick;
            return b;
        }

        public static Label Note(string text)
        {
            var l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Margin = new Padding(3, 2, 3, 6);
            l.ForeColor = SystemColors.GrayText;
            return l;
        }

        /// <summary>A text box with a Browse button for a folder or file path.</summary>
        public static TextBox PathRow(TableLayoutPanel grid, string label, string name,
                                      bool folder, string tip = null, string filter = null)
        {
            var panel = new TableLayoutPanel();
            panel.AutoSize = true;
            panel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panel.ColumnCount = 2;
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.Margin = new Padding(0);
            panel.RowCount = 1;

            var box = Txt(name);
            box.Dock = DockStyle.Fill;
            box.Margin = new Padding(3, 3, 3, 3);

            var browse = Btn("Browse...", delegate
            {
                if (folder)
                {
                    using (var d = new FolderBrowserDialog())
                    {
                        d.Description = label;
                        try { if (System.IO.Directory.Exists(box.Text)) d.SelectedPath = box.Text; }
                        catch { }
                        if (d.ShowDialog() == DialogResult.OK) box.Text = d.SelectedPath;
                    }
                }
                else
                {
                    using (var d = new OpenFileDialog())
                    {
                        d.Title = label;
                        d.Filter = filter ?? "All files (*.*)|*.*";
                        d.CheckFileExists = false;
                        if (d.ShowDialog() == DialogResult.OK) box.Text = d.FileName;
                    }
                }
            });
            browse.Margin = new Padding(0, 3, 0, 3);

            panel.Controls.Add(box, 0, 0);
            panel.Controls.Add(browse, 1, 0);

            Row(grid, label, panel, tip);
            if (tip != null) Tips.SetToolTip(box, tip);
            return box;
        }
    }
}
