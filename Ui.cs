using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace YtDlpGui
{
    /// <summary>
    /// A control that knows how tall it needs to be for a given width. Implemented by every
    /// container in this file so a parent can size a row without asking the WinForms layout
    /// engine to guess.
    /// </summary>
    internal interface IFlexHeight
    {
        int HeightForWidth(int width);
    }

    // =========================================================================
    //  Painting
    // =========================================================================

    /// <summary>A scroll host that paints without the flicker of a plain Panel.</summary>
    internal class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
    }

    /// <summary>
    /// A TabControl whose pages do not flash grey when the selection changes. Deliberately
    /// not owner-drawn: the native common control still paints the strip, so the tabs look
    /// exactly like every other Windows tab control under any theme.
    /// </summary>
    internal class BufferedTabControl : TabControl
    {
        public BufferedTabControl()
        {
            // No ResizeRedraw: it invalidates the whole control on every size change, which
            // during a window drag means repainting a full-page surface per mouse move.
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
    }

    /// <summary>A Label that repaints in one pass, for text that changes several times a second.</summary>
    internal class SmoothLabel : Label
    {
        public SmoothLabel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint, true);
        }

        /// <summary>Assigns only when the text actually differs, so an unchanged line never repaints.</summary>
        public void SetText(string value)
        {
            value = value ?? "";
            if (Text != value) Text = value;
        }
    }

    // =========================================================================
    //  Progress
    // =========================================================================

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
                if (_state == value) return;
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
            if (value == Value && Style == ProgressBarStyle.Continuous) return;

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

    // =========================================================================
    //  Layout containers
    // =========================================================================

    /// <summary>
    /// A row of small controls that wraps at the available width. Replaces FlowLayoutPanel,
    /// which reports a preferred size that depends on its current size and so has to be
    /// measured repeatedly before it settles.
    /// </summary>
    internal class WrapRow : Panel, IFlexHeight
    {
        internal int HSpacing = 18;
        internal int VSpacing = 5;

        private bool _laying;
        private bool _autoHeight;

        /// <summary>
        /// Sizes the row to its content, for a row docked to the top or bottom of a panel.
        /// Done through AutoSize and GetPreferredSize rather than by assigning Height during
        /// layout: a bottom-docked control that resizes itself mid-pass keeps its old top
        /// edge and simply grows off the bottom of its parent.
        /// </summary>
        internal bool AutoHeight
        {
            get { return _autoHeight; }
            set
            {
                if (_autoHeight == value) return;
                _autoHeight = value;
                if (value) AutoSizeMode = AutoSizeMode.GrowAndShrink;
                AutoSize = value;
            }
        }

        public WrapRow()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public int HeightForWidth(int width)
        {
            return Arrange(width - Padding.Horizontal, false) + Padding.Vertical;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int w = proposedSize.Width;
            if (w <= 1) w = Width;
            if (w <= 1 && Parent != null) w = Parent.ClientSize.Width;
            if (w <= 1) w = 200;
            return new Size(w, HeightForWidth(w));
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (_laying) return;
            _laying = true;
            try { Arrange(ClientSize.Width - Padding.Horizontal, true); }
            finally { _laying = false; }
        }

        private int Arrange(int width, bool apply)
        {
            if (width < 20) width = 20;

            var items = new List<Control>();
            foreach (Control c in Controls) if (c.Visible) items.Add(c);

            // Measure the tallest item first so short controls sit on the same baseline as
            // tall ones instead of clinging to the top of the line.
            int tallest = 0;
            foreach (var c in items)
            {
                int h = Ui.Measure(c).Height;
                if (h > tallest) tallest = h;
            }

            int x = 0, y = 0, lineHeight = 0;

            for (int i = 0; i < items.Count; i++)
            {
                var c = items[i];
                var size = Ui.Measure(c);
                bool isSeparator = c is Separator;
                if (isSeparator) size = new Size(size.Width, tallest);

                // A divider is only meaningful between two things on the same line. If what
                // follows it would wrap anyway, the wrap is the separation - drop the rule
                // rather than leave it dangling at the end of a line.
                if (isSeparator)
                {
                    int next = i + 1 < items.Count ? Ui.Measure(items[i + 1]).Width : 0;
                    if (next == 0 || (x > 0 && x + size.Width + HSpacing + next > width))
                    {
                        if (apply) c.SetBounds(Padding.Left + x, Padding.Top + y, 0, 0);
                        continue;
                    }
                }

                if (x > 0 && x + size.Width > width)
                {
                    x = 0;
                    y += lineHeight + VSpacing;
                    lineHeight = 0;
                }
                if (apply)
                    Ui.Place(c, Padding.Left + x, Padding.Top + y + (tallest - size.Height) / 2, size);

                x += size.Width + HSpacing;
                if (size.Height > lineHeight) lineHeight = size.Height;
            }
            return y + lineHeight;
        }
    }

    /// <summary>A hairline that separates one group of buttons from the next.</summary>
    internal class Separator : Panel
    {
        public Separator()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(9, 24);
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var pen = new Pen(SystemColors.ControlDark))
            {
                int x = Width / 2;
                int inset = Math.Max(2, Height / 5);
                e.Graphics.DrawLine(pen, x, inset, x, Height - inset);
            }
        }
    }

    /// <summary>A caption on the left with a row of controls pinned to the right-hand edge.</summary>
    internal class CaptionRow : Panel, IFlexHeight
    {
        private readonly Label _caption;
        private readonly Control[] _right;
        private bool _laying;

        public CaptionRow(string caption, params Control[] rightHandSide)
        {
            _right = rightHandSide ?? new Control[0];
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;

            _caption = new Label();
            _caption.Text = caption;
            _caption.AutoSize = true;
            _caption.ForeColor = SystemColors.GrayText;
            Controls.Add(_caption);
            foreach (var c in _right) Controls.Add(c);
        }

        public Label Caption { get { return _caption; } }

        public int HeightForWidth(int width)
        {
            int h = Ui.Measure(_caption).Height;
            foreach (var c in _right) h = Math.Max(h, Ui.Measure(c).Height);
            return h;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (_laying) return;
            _laying = true;
            try
            {
                int h = HeightForWidth(ClientSize.Width);
                var cs = Ui.Measure(_caption);
                Ui.Place(_caption, 0, (h - cs.Height) / 2, cs);

                int x = ClientSize.Width;
                for (int i = _right.Length - 1; i >= 0; i--)
                {
                    var size = Ui.Measure(_right[i]);
                    x -= size.Width;
                    Ui.Place(_right[i], x, (h - size.Height) / 2, size);
                    x -= Ui.Gap;
                }
            }
            finally { _laying = false; }
        }
    }

    /// <summary>A row of controls at their natural size, side by side, vertically centred.</summary>
    internal class InlineRow : Panel, IFlexHeight
    {
        internal int HSpacing = 10;
        private bool _laying;

        public InlineRow()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public int HeightForWidth(int width) { return Arrange(width, false); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (_laying) return;
            _laying = true;
            try { Arrange(ClientSize.Width, true); }
            finally { _laying = false; }
        }

        private int Arrange(int width, bool apply)
        {
            int tallest = 0;
            foreach (Control c in Controls)
            {
                if (!c.Visible) continue;
                int h = Ui.Measure(c).Height;
                if (h > tallest) tallest = h;
            }

            if (apply)
            {
                int x = 0;
                foreach (Control c in Controls)
                {
                    if (!c.Visible) continue;
                    var size = Ui.Measure(c);
                    Ui.Place(c, x, (tallest - size.Height) / 2, size);
                    x += size.Width + HSpacing;
                }
            }
            return tallest;
        }
    }

    /// <summary>A text box that stretches, with a fixed Browse button pinned to its right.</summary>
    internal class BrowseRow : Panel, IFlexHeight
    {
        private readonly TextBox _box;
        private readonly Button _button;
        private bool _laying;

        public BrowseRow(TextBox box, Button button)
        {
            _box = box;
            _button = button;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Controls.Add(box);
            Controls.Add(button);
        }

        public int HeightForWidth(int width)
        {
            return Math.Max(Ui.Measure(_box).Height, Ui.Measure(_button).Height);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (_laying) return;
            _laying = true;
            try
            {
                int width = ClientSize.Width;
                var bs = Ui.Measure(_button);
                var xs = Ui.Measure(_box);
                int h = Math.Max(bs.Height, xs.Height);

                int boxWidth = Math.Max(40, width - bs.Width - Ui.Gap);
                _box.SetBounds(0, (h - xs.Height) / 2, boxWidth, xs.Height);
                _button.SetBounds(boxWidth + Ui.Gap, (h - bs.Height) / 2, bs.Width, bs.Height);
            }
            finally { _laying = false; }
        }
    }

    /// <summary>
    /// A labelled group of options. Rows are laid out in one downward pass - a label column
    /// sized once from the widest label, then each row placed at a known y - and the box sizes
    /// itself to the result. Nothing is measured twice and nothing feeds back into the parent
    /// except the single final height.
    /// </summary>
    internal class Section : GroupBox
    {
        private sealed class Row
        {
            public Section Owner;
            public Label Label;
            public Control Body;
            public bool Full;
        }

        private readonly List<Row> _rows = new List<Row>();
        private int _labelWidth = -1;
        private bool _laying;
        private bool _collapsed = true;
        private readonly Button _toggle;

        /// <summary>Set while the tab is being assembled: every layout request is ignored.</summary>
        internal bool Building;

        public Section(string title)
        {
            Text = title;
            DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Padding = new Padding(Ui.Pad, 2, Ui.Pad, Ui.Pad);
            _toggle = Ui.Btn("Expand", delegate { Collapsed = !Collapsed; }, 80);
            _toggle.AccessibleName = "Expand " + title;
            Controls.Add(_toggle);
        }

        internal bool Collapsed
        {
            get { return _collapsed; }
            set
            {
                if (_collapsed == value) return;
                _collapsed = value;
                _toggle.Text = value ? "Expand" : "Collapse";
                _toggle.AccessibleName = _toggle.Text + " " + Text;
                foreach (var row in _rows)
                    if (row.Body.Parent == this) SetRowVisible(row, !value);
                PerformLayout();
                if (Parent != null) Parent.PerformLayout();
            }
        }

        private static void SetRowVisible(Row row, bool visible)
        {
            row.Body.Visible = visible;
            if (row.Label != null) row.Label.Visible = visible;
        }

        // Basic mode borrows the actual rows, so values, events and settings have one owner.
        internal void BorrowRow(Control option)
        {
            var source = option.Parent;
            while (source != null && !(source is Section)) source = source.Parent;
            if (source == null || source == this) return;
            foreach (var row in ((Section)source)._rows)
            {
                if (row.Body != option && !row.Body.Contains(option)) continue;
                _rows.Add(row);
                AttachRow(row);
                return;
            }
        }

        internal void ReturnBorrowedRows()
        {
            foreach (var row in _rows)
                if (row.Owner != this) row.Owner.AttachRow(row);
            _rows.RemoveAll(delegate (Row row) { return row.Owner != this; });
            _labelWidth = -1;
        }

        private void AttachRow(Row row)
        {
            if (row.Label != null) Controls.Add(row.Label);
            Controls.Add(row.Body);
            SetRowVisible(row, !_collapsed);
            _labelWidth = -1;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && e.Y <= _toggle.Bottom)
                Collapsed = !Collapsed;
        }

        internal void AddRow(Label label, Control body, bool full)
        {
            var row = new Row { Owner = this, Label = label, Body = body, Full = full };
            _rows.Add(row);
            _labelWidth = -1;
            if (label != null) Controls.Add(label);
            if (body != null) Controls.Add(body);
            SetRowVisible(row, !_collapsed);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            _labelWidth = -1;
            base.OnFontChanged(e);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (Building || _laying || _toggle == null) return;
            _laying = true;
            try { Arrange(); }
            finally { _laying = false; }
        }

        /// <summary>Lays the rows out and returns the height the box now has.</summary>
        internal int Arrange()
        {
            int headerHeight = Math.Max(24, Font.Height + 10);
            _toggle.SetBounds(Math.Max(Ui.Pad, ClientSize.Width - 80 - Ui.Pad), 0, 80, headerHeight);
            if (_collapsed)
            {
                Height = headerHeight + 8;
                return Height;
            }
            var display = DisplayRectangle;

            int left = display.Left;
            int available = Math.Max(60, display.Width);

            int labelColumn = LabelColumnWidth();
            int y = Math.Max(display.Top, headerHeight + 6);
            bool first = true;

            foreach (var row in _rows)
            {
                if (row.Body.Parent != this || !row.Body.Visible) continue;
                if (!first) y += Ui.RowGap;
                first = false;

                if (row.Full || row.Label == null)
                {
                    var body = row.Body;
                    int h = Ui.HeightFor(body, available);
                    // A caption keeps its natural width; anything else spans the group.
                    if (body is Label)
                    {
                        var natural = Ui.Measure(body);
                        body.SetBounds(left, y, Math.Min(natural.Width, available), h);
                    }
                    else
                    {
                        body.SetBounds(left, y, available, h);
                    }
                    y += h;
                }
                else
                {
                    int bodyLeft = left + labelColumn + Ui.LabelGap;
                    int bodyWidth = Math.Max(60, available - labelColumn - Ui.LabelGap);
                    int bodyHeight = Ui.HeightFor(row.Body, bodyWidth);

                    var ls = Ui.Measure(row.Label);
                    // Centre the label against a single-line control; align it with the first
                    // line of anything taller, which is where the eye expects it.
                    int labelY = bodyHeight <= ls.Height + 10
                        ? y + (bodyHeight - ls.Height) / 2
                        : y + 3;

                    row.Label.SetBounds(left, labelY, labelColumn, ls.Height);
                    row.Body.SetBounds(bodyLeft, y, bodyWidth, bodyHeight);
                    y += Math.Max(bodyHeight, ls.Height);
                }
            }

            int wanted = y + Padding.Bottom + 3;
            if (Height != wanted) Height = wanted;
            return wanted;
        }

        private int LabelColumnWidth()
        {
            if (_labelWidth >= 0) return _labelWidth;
            int w = 0;
            foreach (var row in _rows)
            {
                if (row.Body.Parent != this || row.Full || row.Label == null) continue;
                int lw = Ui.Measure(row.Label).Width;
                if (lw > w) w = lw;
            }
            _labelWidth = w;
            return w;
        }
    }

    /// <summary>
    /// The single column of Sections that makes up a tab. Docked to the top of a scrolling
    /// panel, it stacks its children and then reports one height - the only value the parent
    /// ever has to react to.
    /// </summary>
    internal class SectionStack : Control
    {
        private bool _laying;
        private bool _dirty = true;
        private int _arrangedAt = -1;

        /// <summary>Set while the tab is being assembled: every layout request is ignored.</summary>
        internal bool Building;

        public SectionStack()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Dock = DockStyle.Top;
            Padding = new Padding(0);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (Building || _laying) return;

            // A TabControl resizes every page, not just the one on top. Moving four hundred
            // controls that nobody can see is what makes dragging the window edge stutter, so
            // a hidden tab records that it owes a layout and does it when it is next shown.
            if (!Visible) { _dirty = true; return; }

            _laying = true;
            try { Arrange(); }
            finally { _laying = false; }
        }

        // A tab page is sized only once it is first shown. Both of these are that moment,
        // depending on whether the page was ever selected, and either way the stack has to
        // arrange itself against a real width rather than the default one it was built at.
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (Visible) PerformLayout();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && (_dirty || _arrangedAt != ClientSize.Width)) PerformLayout();
        }

        internal void Arrange()
        {
            _dirty = false;
            _arrangedAt = ClientSize.Width;

            int width = Math.Max(80, ClientSize.Width);
            int y = Padding.Top;
            bool first = true;

            foreach (Control c in Controls)
            {
                if (!c.Visible) continue;
                if (!first) y += Ui.SectionGap;
                first = false;

                int innerWidth = width - Padding.Horizontal;
                var section = c as Section;
                if (section != null)
                {
                    // Width first: the row heights depend on it.
                    if (section.Width != innerWidth || section.Top != y || section.Left != Padding.Left)
                        section.SetBounds(Padding.Left, y, innerWidth, section.Height);
                    section.Arrange();
                }
                else
                {
                    c.SetBounds(Padding.Left, y, innerWidth, Ui.HeightFor(c, innerWidth));
                }
                y = c.Bottom;
            }

            int wanted = y + Padding.Bottom;
            if (Height != wanted) Height = wanted;
        }
    }

    /// <summary>
    /// The two progress bars and the lines underneath them. Fixed geometry, computed once
    /// from the font: the contents change several times a second and must never cause the
    /// window to re-measure anything.
    /// </summary>
    internal class ProgressPanel : Control
    {
        private readonly Label _capOverall, _capFile;
        private Control _barOverall, _lineOverall, _barFile, _lineFile;
        private readonly int _text;
        private bool _laying;

        public ProgressPanel(int textHeight)
        {
            _text = textHeight;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            Padding = new Padding(10, 5, 10, 5);

            _capOverall = new Label { Text = "Overall", AutoSize = true };
            _capFile = new Label { Text = "This file", AutoSize = true, ForeColor = SystemColors.GrayText };
        }

        public void Compose(Control barOverall, Control lineOverall, Control barFile, Control lineFile)
        {
            _barOverall = barOverall;
            _lineOverall = lineOverall;
            _barFile = barFile;
            _lineFile = lineFile;

            Controls.Add(_capOverall);
            Controls.Add(_capFile);
            Controls.Add(_barOverall);
            Controls.Add(_lineOverall);
            Controls.Add(_barFile);
            Controls.Add(_lineFile);

            Height = Padding.Vertical
                   + Math.Max(_barOverall.Height, _text) + 2 + _text + 2 + 6
                   + Math.Max(_barFile.Height, _text) + 2 + _text + 2;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (_laying || _barOverall == null) return;
            _laying = true;
            try
            {
                int labelWidth = Math.Max(Ui.Measure(_capOverall).Width, Ui.Measure(_capFile).Width);
                int left = Padding.Left;
                int barLeft = left + labelWidth + Ui.LabelGap;
                int barWidth = Math.Max(60, ClientSize.Width - barLeft - Padding.Right);
                int y = Padding.Top;

                int h = Math.Max(_barOverall.Height, _text);
                Ui.Place(_capOverall, left, y + (h - _text) / 2, new Size(labelWidth, _text));
                Ui.Place(_barOverall, barLeft, y + (h - _barOverall.Height) / 2,
                         new Size(barWidth, _barOverall.Height));
                y += h + 2;
                Ui.Place(_lineOverall, barLeft, y, new Size(barWidth, _text + 2));
                y += _text + 2 + 6;

                h = Math.Max(_barFile.Height, _text);
                Ui.Place(_capFile, left, y + (h - _text) / 2, new Size(labelWidth, _text));
                Ui.Place(_barFile, barLeft, y + (h - _barFile.Height) / 2,
                         new Size(barWidth, _barFile.Height));
                y += h + 2;
                Ui.Place(_lineFile, barLeft, y, new Size(barWidth, _text + 2));
            }
            finally { _laying = false; }
        }
    }

    // =========================================================================
    //  Builders
    // =========================================================================

    /// <summary>
    /// Small helpers for building plain-WinForms layouts that follow the system font and DPI.
    /// Every tab in the app is assembled through these.
    /// </summary>
    internal static class Ui
    {
        public const int Pad = 8;
        public const int Gap = 6;
        public const int RowGap = 7;
        public const int SectionGap = 9;
        public const int LabelGap = 10;

        // ---- measurement ------------------------------------------------------

        /// <summary>The size a control wants, without going through the auto-size engine.</summary>
        internal static Size Measure(Control c)
        {
            // Fully qualified: inside this class "Size" is the byte-count formatter below.
            if (c == null) return System.Drawing.Size.Empty;

            var box = c as TextBoxBase;
            if (box != null)
            {
                // AutoSize on a text box governs its height and nothing else. Its preferred
                // *width* is the width of the text it contains, which for an empty box is
                // zero - asking for it is how a row of inputs disappears.
                int h = box.Multiline || !box.AutoSize ? box.Height : box.PreferredSize.Height;
                return new System.Drawing.Size(box.Width, Math.Max(1, h));
            }

            if (c.AutoSize)
            {
                var preferred = c.PreferredSize;
                if (preferred.Width > 0 && preferred.Height > 0) return preferred;
            }
            return c.Size;
        }

        /// <summary>The height a control needs at a given width.</summary>
        internal static int HeightFor(Control c, int width)
        {
            if (c == null) return 0;
            var flex = c as IFlexHeight;
            if (flex != null) return flex.HeightForWidth(width);
            return Measure(c).Height;
        }

        internal static void Place(Control c, int x, int y, Size size)
        {
            if (c.Left != x || c.Top != y || c.Width != size.Width || c.Height != size.Height)
                c.SetBounds(x, y, size.Width, size.Height);
        }

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

        // ---- structure ---------------------------------------------------------

        /// <summary>
        /// Creates a scrollable tab page and returns the stack that sections are appended to.
        /// The stack stays in "building" mode until <see cref="FinishTabs"/> is called, so
        /// adding three hundred controls costs three hundred insertions and no layout at all.
        /// </summary>
        public static SectionStack Tab(TabControl tabs, string title)
        {
            var page = new TabPage(title);
            page.UseVisualStyleBackColor = true;
            page.Padding = new Padding(Pad);

            var scroll = new BufferedPanel();
            scroll.BackColor = Color.Transparent;
            scroll.Dock = DockStyle.Fill;
            scroll.AutoScroll = true;

            var stack = new SectionStack();
            stack.Building = true;

            scroll.Controls.Add(stack);
            page.Controls.Add(scroll);
            tabs.TabPages.Add(page);
            return stack;
        }

        /// <summary>
        /// Ends the build. Each stack is left to arrange itself when its page is first sized,
        /// which is the only moment the real width is known - laying out now would be a pass
        /// against a default width that is thrown away.
        /// </summary>
        public static void FinishTabs(TabControl tabs)
        {
            foreach (TabPage page in tabs.TabPages)
            {
                foreach (var stack in FindStacks(page))
                {
                    foreach (Control c in stack.Controls)
                    {
                        var section = c as Section;
                        if (section != null) section.Building = false;
                    }
                    stack.Building = false;
                }
            }
        }

        private static IEnumerable<SectionStack> FindStacks(Control root)
        {
            foreach (Control c in root.Controls)
            {
                var stack = c as SectionStack;
                if (stack != null) { yield return stack; continue; }
                foreach (var nested in FindStacks(c)) yield return nested;
            }
        }

        /// <summary>Adds a group box to a tab and returns it; rows are added to what comes back.</summary>
        public static Section Group(SectionStack host, string title)
        {
            var section = new Section(title);
            section.Building = host.Building;
            host.Controls.Add(section);
            return section;
        }

        /// <summary>Label on the left, control filling the rest of the row.</summary>
        public static T Row<T>(Section group, string label, T control, string tip = null) where T : Control
        {
            var lbl = new Label();
            lbl.BackColor = Color.Transparent;
            lbl.Text = label;
            lbl.AutoSize = true;

            group.AddRow(lbl, control, false);

            if (tip != null)
            {
                Tips.SetToolTip(control, tip);
                Tips.SetToolTip(lbl, tip);
            }
            return control;
        }

        /// <summary>A control spanning the full width of the group.</summary>
        public static T Full<T>(Section group, T control, string tip = null) where T : Control
        {
            group.AddRow(null, control, true);
            if (tip != null) Tips.SetToolTip(control, tip);
            return control;
        }

        /// <summary>A wrapping row of small controls (typically check boxes), full width.</summary>
        public static WrapRow Flow(Section group, params Control[] items)
        {
            var row = new WrapRow();
            foreach (var c in items) row.Controls.Add(c);
            return Full(group, row);
        }

        /// <summary>A label plus a right-hand row of controls kept at their natural size.</summary>
        public static InlineRow RowFlow(Section group, string label, params Control[] items)
        {
            var row = new InlineRow();
            foreach (var c in items) row.Controls.Add(c);
            Row(group, label, row);
            return row;
        }

        // ---- leaf controls -------------------------------------------------------

        public static CheckBox Chk(string name, string text, string tip = null)
        {
            var c = new CheckBox();
            c.BackColor = Color.Transparent;
            c.Name = name;
            c.Text = text;
            c.AutoSize = true;
            if (tip != null) Tips.SetToolTip(c, tip);
            return c;
        }

        public static RadioButton Rad(string name, string text, string tip = null)
        {
            var r = new RadioButton();
            r.BackColor = Color.Transparent;
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
            l.BackColor = Color.Transparent;
            l.Text = text;
            l.AutoSize = true;
            l.ForeColor = SystemColors.GrayText;
            return l;
        }

        /// <summary>A text box with a Browse button for a folder or file path.</summary>
        public static TextBox PathRow(Section group, string label, string name,
                                      bool folder, string tip = null, string filter = null)
        {
            var box = Txt(name);

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

            var row = new BrowseRow(box, browse);
            Row(group, label, row, tip);
            if (tip != null) Tips.SetToolTip(box, tip);
            return box;
        }
    }
}
