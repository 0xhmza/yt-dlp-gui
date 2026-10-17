using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace YtDlpGui
{
    internal enum JobState { Queued, Running, Done, Failed, Stopped }

    internal class Job
    {
        public string Url;
        public List<string> Args;
        public JobState State = JobState.Queued;
        public double Fraction;
        public string Detail = "";
        public string Position = "";
        public ListViewItem Item;
        public DateTime Started, Ended;
        public bool Simulate;

        public string StateText
        {
            get
            {
                switch (State)
                {
                    case JobState.Queued: return "Queued";
                    case JobState.Running: return "Running";
                    case JobState.Done: return "Done";
                    case JobState.Failed: return "Failed";
                    default: return "Stopped";
                }
            }
        }
    }

    internal partial class MainForm : Form
    {
        // ---- shell controls -------------------------------------------------
        private TextBox _urls;
        private TabControl _tabs;
        private TabControl _outTabs;
        private RichTextBox _log;
        private ListView _queueView;
        private TextBox _cmdPreview;
        private TextBox _findBox;
        private ProgressBarEx _barOverall, _barFile;
        private Label _lblOverall, _lblFile;
        private Button _btnDownload, _btnStop, _btnQueueAdd, _btnSimulate, _btnFormats, _btnOpenFolder;
        private CheckBox _chkAutoScroll, _chkLogProgress, _chkOpenWhenDone, _chkAlertWhenDone;
        private StatusStrip _status;
        private SplitContainer _split;
        private ToolStripStatusLabel _statusText, _statusStats, _statusDeps;
        private ToolStripMenuItem _miAlwaysOnTop, _miTools;
        private ContextMenuStrip _queueMenu;
        private Dictionary<string, string> _savedSettings;

        // ---- run state ------------------------------------------------------
        private readonly Runner _runner = new Runner();
        private readonly ProgressTracker _progress = new ProgressTracker();
        private readonly List<Job> _queue = new List<Job>();
        private readonly List<Job> _run = new List<Job>();
        private int _runPos = -1;
        private bool _stopRequested;
        private bool _loading;
        private bool _closing;
        private readonly List<KeyValuePair<string, LineKind>> _pending = new List<KeyValuePair<string, LineKind>>();
        private readonly Timer _uiTimer = new Timer();
        private int _okCount, _failCount;
        private DateTime _runStarted;
        private ToolRun _toolRun;
        private Dictionary<string, string> _defaults;
        private string _unsupportedOption;
        private bool _retriedCurrentJob;
        private Rectangle _restoredBounds;

        private const int TabLog = 0, TabQueue = 1, TabCommand = 2;
        private const int LogMaxChars = 1500000;
        private const int LogTrimTo = 900000;

        private static readonly Regex RxNoSuchOption =
            new Regex(@"no such option:?\s*(?<opt>--[a-z0-9-]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public MainForm()
        {
            Text = App.Title;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(820, 600);
            ClientSize = new Size(1040, 880);
            KeyPreview = true;
            AllowDrop = true;

            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { }

            BuildUi();
            HookChangeEvents(this);

            _uiTimer.Interval = 400;
            _uiTimer.Tick += UiTimerTick;

            _runner.LineReceived += OnRunnerLine;
            _runner.ProgressChanged += OnRunnerProgress;
            _runner.Finished += OnRunnerFinished;

            DragEnter += OnFormDragEnter;
            DragDrop += OnFormDragDrop;

            Load += delegate
            {
                // Defaults are captured before anything is restored, so "Reset all options"
                // never has to build a second copy of this form to find out what they were.
                _defaults = Settings.Capture(this);
                _defaults.Remove("urls");

                LoadSettings();
                RefreshDependencyStatus();
                UpdatePreview();
                PrewarmTabs();
                _uiTimer.Start();
            };
            Shown += delegate
            {
                ApplyRestoredGeometry();
                ShowWelcomeAsync();
            };
            FormClosing += OnFormClosing;
        }

        // =====================================================================
        //  Layout
        // =====================================================================
        private void BuildUi()
        {
            var menu = BuildMenu();

            // ---- top: URLs + actions ----------------------------------------
            var top = new TableLayoutPanel();
            top.Dock = DockStyle.Top;
            top.AutoSize = true;
            top.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            top.ColumnCount = 2;
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.Padding = new Padding(8, 6, 8, 4);

            var urlBox = new GroupBox();
            urlBox.Text = "URLs  (one per line)";
            urlBox.Dock = DockStyle.Fill;
            urlBox.AutoSize = true;
            urlBox.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            urlBox.Padding = new Padding(6);

            _urls = new TextBox();
            _urls.Name = "urls";
            _urls.Multiline = true;
            _urls.ScrollBars = ScrollBars.Vertical;
            _urls.Dock = DockStyle.Fill;
            _urls.Height = TextRenderer.MeasureText("Wg", Font).Height * 3 + 10;
            _urls.AllowDrop = true;
            _urls.DragEnter += OnFormDragEnter;
            _urls.DragDrop += OnFormDragDrop;
            Ui.Tips.SetToolTip(_urls, "One URL per line. Lines starting with # are ignored. " +
                                      "Drop links or a text file of links anywhere on the window.");
            urlBox.Controls.Add(_urls);

            var side = new FlowLayoutPanel();
            side.FlowDirection = FlowDirection.TopDown;
            side.AutoSize = true;
            side.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            side.WrapContents = false;
            side.Margin = new Padding(6, 14, 0, 0);
            side.Controls.Add(Ui.Btn("Paste", delegate { PasteUrls(); }, 96));
            side.Controls.Add(Ui.Btn("Clear", delegate { _urls.Clear(); _urls.Focus(); }, 96));
            side.Controls.Add(Ui.Btn("Clean up", delegate { CleanUpUrls(); }, 96));
            Ui.Tips.SetToolTip(side, "Clean up removes blank lines, duplicates and stray spaces.");

            top.Controls.Add(urlBox, 0, 0);
            top.Controls.Add(side, 1, 0);

            // ---- action bar --------------------------------------------------
            var actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Top;
            actions.AutoSize = true;
            actions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            actions.Padding = new Padding(8, 0, 8, 6);
            actions.WrapContents = true;

            _btnDownload = Ui.Btn("Download", delegate { StartDownload(false); }, 120);
            _btnDownload.Font = new Font(Font, FontStyle.Bold);
            _btnQueueAdd = Ui.Btn("Add to queue", delegate { EnqueueCurrentUrls(); }, 110);
            _btnStop = Ui.Btn("Stop", delegate { StopAll(); }, 80);
            _btnStop.Enabled = false;
            _btnFormats = Ui.Btn("List formats...", delegate { ShowFormats(); }, 110);
            _btnSimulate = Ui.Btn("Simulate", delegate { StartDownload(true); }, 90);
            _btnOpenFolder = Ui.Btn("Open folder", delegate { App.OpenFolder(txtOutDir.Text); }, 95);

            Ui.Tips.SetToolTip(_btnDownload, "Start the queue, adding anything typed above first.  (F5)");
            Ui.Tips.SetToolTip(_btnQueueAdd, "Capture the options as they are now and queue the URLs above.  (Ctrl+D)");
            Ui.Tips.SetToolTip(_btnStop, "Terminate yt-dlp and every child process it started.  (Esc)");
            Ui.Tips.SetToolTip(_btnSimulate, "Run with --simulate: resolves everything and reports what would happen, without downloading.");
            Ui.Tips.SetToolTip(_btnFormats, "Fetch the available formats for the first URL and pick one.  (Ctrl+F)");
            Ui.Tips.SetToolTip(_btnOpenFolder, "Open the download folder in Explorer.  (Ctrl+O)");

            _chkOpenWhenDone = Ui.Chk("openWhenDone", "Open folder when finished");
            _chkAlertWhenDone = Ui.Chk("alertWhenDone", "Alert when finished",
                "Flashes the taskbar button and plays the system notification sound once the queue is empty.");
            _chkAlertWhenDone.Checked = true;
            _chkOpenWhenDone.Margin = new Padding(14, 8, 6, 3);
            _chkAlertWhenDone.Margin = new Padding(6, 8, 3, 3);

            actions.Controls.Add(_btnDownload);
            actions.Controls.Add(_btnQueueAdd);
            actions.Controls.Add(_btnStop);
            actions.Controls.Add(_btnFormats);
            actions.Controls.Add(_btnSimulate);
            actions.Controls.Add(_btnOpenFolder);
            actions.Controls.Add(_chkOpenWhenDone);
            actions.Controls.Add(_chkAlertWhenDone);

            // ---- options tabs ------------------------------------------------
            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;
            BuildTabs();

            // ---- output area -------------------------------------------------
            _outTabs = new TabControl();
            _outTabs.Dock = DockStyle.Fill;
            _outTabs.TabPages.Add(BuildLogPage());
            _outTabs.TabPages.Add(BuildQueuePage());
            _outTabs.TabPages.Add(BuildCommandPage());

            // ---- progress ----------------------------------------------------
            var progPanel = BuildProgressPanel();

            // ---- split -------------------------------------------------------
            _split = new SplitContainer();
            _split.Dock = DockStyle.Fill;
            _split.Orientation = Orientation.Horizontal;
            _split.SplitterWidth = 6;
            _split.Panel1MinSize = 200;
            _split.Panel2MinSize = 140;
            _split.Panel1.Controls.Add(_tabs);
            _split.Panel2.Controls.Add(_outTabs);

            _status = new StatusStrip();
            _statusText = new ToolStripStatusLabel("Ready");
            _statusText.Spring = true;
            _statusText.TextAlign = ContentAlignment.MiddleLeft;
            _statusStats = new ToolStripStatusLabel("");
            _statusDeps = new ToolStripStatusLabel("");
            _status.Items.Add(_statusText);
            _status.Items.Add(_statusStats);
            _status.Items.Add(_statusDeps);

            Controls.Add(_split);
            Controls.Add(progPanel);
            Controls.Add(_status);
            Controls.Add(actions);
            Controls.Add(top);
            Controls.Add(menu);
            MainMenuStrip = menu;
        }

        private TabPage BuildLogPage()
        {
            var page = new TabPage("Log");
            page.UseVisualStyleBackColor = true;

            _log = new RichTextBox();
            _log.Dock = DockStyle.Fill;
            _log.ReadOnly = true;
            _log.WordWrap = false;
            _log.BorderStyle = BorderStyle.None;
            _log.Font = new Font(FontFamily.GenericMonospace, Font.SizeInPoints);
            _log.HideSelection = false;
            _log.DetectUrls = false;          // the log is full of URLs; link-ifying them is slow
            _log.ContextMenuStrip = BuildLogMenu();

            var bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Bottom;
            bar.AutoSize = true;
            bar.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            _chkAutoScroll = Ui.Chk("logAutoScroll", "Auto-scroll");
            _chkAutoScroll.Checked = true;
            _chkAutoScroll.Margin = new Padding(3, 7, 8, 3);
            _chkLogProgress = Ui.Chk("logProgress", "Log progress lines",
                "Off by default: progress is shown on the bars above instead of filling the log.");
            _chkLogProgress.Margin = new Padding(3, 7, 8, 3);

            _findBox = new TextBox();
            _findBox.Name = "logFind";
            _findBox.Width = 150;
            _findBox.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { FindInLog(); e.Handled = e.SuppressKeyPress = true; }
            };
            Ui.Tips.SetToolTip(_findBox, "Type here and press Enter (or F3) to find the next match in the log.");

            bar.Controls.Add(_chkAutoScroll);
            bar.Controls.Add(_chkLogProgress);
            bar.Controls.Add(new Label { Text = "Find:", AutoSize = true, Margin = new Padding(10, 8, 3, 3) });
            bar.Controls.Add(_findBox);
            bar.Controls.Add(Ui.Btn("Next", delegate { FindInLog(); }));
            bar.Controls.Add(Ui.Btn("Clear log", delegate { ClearLog(); }));
            bar.Controls.Add(Ui.Btn("Save log...", delegate { SaveLog(); }));

            page.Controls.Add(_log);
            page.Controls.Add(bar);
            return page;
        }

        private TabPage BuildQueuePage()
        {
            var page = new TabPage("Queue");
            page.UseVisualStyleBackColor = true;

            _queueView = new BufferedListView();
            _queueView.Dock = DockStyle.Fill;
            _queueView.View = View.Details;
            _queueView.FullRowSelect = true;
            _queueView.GridLines = true;
            _queueView.HideSelection = false;
            _queueView.Columns.Add("#", 40);
            _queueView.Columns.Add("Status", 80);
            _queueView.Columns.Add("Progress", 70, HorizontalAlignment.Right);
            _queueView.Columns.Add("Item", 70);
            _queueView.Columns.Add("URL", 380);
            _queueView.Columns.Add("Detail", 280);
            _queueView.DoubleClick += delegate { OpenSelectedJobUrl(); };

            _queueMenu = new ContextMenuStrip();
            _queueMenu.Items.Add("Move &up", null, delegate { MoveSelectedJobs(-1); });
            _queueMenu.Items.Add("Move &down", null, delegate { MoveSelectedJobs(1); });
            _queueMenu.Items.Add(new ToolStripSeparator());
            _queueMenu.Items.Add("&Copy URL", null, delegate { CopySelectedJobUrl(); });
            _queueMenu.Items.Add("&Open in browser", null, delegate { OpenSelectedJobUrl(); });
            _queueMenu.Items.Add("Copy &command", null, delegate { CopySelectedJobCommand(); });
            _queueMenu.Items.Add(new ToolStripSeparator());
            _queueMenu.Items.Add("&Requeue", null, delegate { RequeueSelected(); });
            _queueMenu.Items.Add("Re&move", null, delegate { RemoveSelectedJobs(); });
            _queueView.ContextMenuStrip = _queueMenu;

            var bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Bottom;
            bar.AutoSize = true;
            bar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            bar.Controls.Add(Ui.Btn("Move up", delegate { MoveSelectedJobs(-1); }));
            bar.Controls.Add(Ui.Btn("Move down", delegate { MoveSelectedJobs(1); }));
            bar.Controls.Add(Ui.Btn("Remove selected", delegate { RemoveSelectedJobs(); }));
            bar.Controls.Add(Ui.Btn("Clear finished", delegate { ClearFinishedJobs(); }));
            bar.Controls.Add(Ui.Btn("Clear all", delegate { ClearQueue(); }));
            bar.Controls.Add(Ui.Btn("Retry failed", delegate { RetryFailed(); }));

            page.Controls.Add(_queueView);
            page.Controls.Add(bar);
            return page;
        }

        private TabPage BuildCommandPage()
        {
            var page = new TabPage("Command");
            page.UseVisualStyleBackColor = true;

            _cmdPreview = new TextBox();
            _cmdPreview.Dock = DockStyle.Fill;
            _cmdPreview.Multiline = true;
            _cmdPreview.ReadOnly = true;
            _cmdPreview.ScrollBars = ScrollBars.Both;
            _cmdPreview.WordWrap = true;
            _cmdPreview.Font = new Font(FontFamily.GenericMonospace, Font.SizeInPoints);

            var bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Bottom;
            bar.AutoSize = true;
            bar.Controls.Add(Ui.Btn("Copy to clipboard", delegate { CopyPreview(); }));
            bar.Controls.Add(Ui.Btn("Save as .bat...", delegate { SaveBatch(); }));
            bar.Controls.Add(Ui.Note("The exact command that will run for the first URL."));

            page.Controls.Add(_cmdPreview);
            page.Controls.Add(bar);
            return page;
        }

        private Control BuildProgressPanel()
        {
            var panel = new TableLayoutPanel();
            panel.Dock = DockStyle.Bottom;
            panel.AutoSize = true;
            panel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panel.ColumnCount = 2;
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            panel.Padding = new Padding(8, 4, 8, 4);

            int textHeight = TextRenderer.MeasureText("Wg", Font).Height;

            _barOverall = new ProgressBarEx();
            _barOverall.Dock = DockStyle.Fill;
            _barOverall.Height = Math.Max(16, textHeight);
            _barOverall.Margin = new Padding(3, 2, 3, 1);

            _barFile = new ProgressBarEx();
            _barFile.Dock = DockStyle.Fill;
            _barFile.Height = Math.Max(12, textHeight - 4);
            _barFile.Margin = new Padding(3, 2, 3, 1);

            _lblOverall = MakeStatusLine("Idle");
            _lblFile = MakeStatusLine("");
            _lblFile.ForeColor = SystemColors.GrayText;

            var lblO = new Label
            {
                Text = "Overall",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(3, 3, 8, 3)
            };
            var lblF = new Label
            {
                Text = "This file",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(3, 3, 8, 3)
            };

            Ui.Tips.SetToolTip(_barOverall,
                "Progress across everything in this run: every queue entry, and every video inside a playlist.");
            Ui.Tips.SetToolTip(_barFile, "Progress for the single file being transferred right now.");

            panel.RowCount = 4;
            for (int i = 0; i < 4; i++) panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            panel.Controls.Add(lblO, 0, 0);
            panel.Controls.Add(_barOverall, 1, 0);
            panel.Controls.Add(_lblOverall, 1, 1);
            panel.Controls.Add(lblF, 0, 2);
            panel.Controls.Add(_barFile, 1, 2);
            panel.Controls.Add(_lblFile, 1, 3);
            return panel;
        }

        private Label MakeStatusLine(string text)
        {
            var l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.Dock = DockStyle.Fill;
            l.Height = TextRenderer.MeasureText("Wg", Font).Height + 2;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.AutoEllipsis = true;
            l.Margin = new Padding(3, 0, 3, 2);
            return l;
        }

        /// <summary>
        /// A TabControl only creates a page's window handles the first time it is shown, so the
        /// first visit to each tab pays for hundreds of controls. Do it once up front, before
        /// the form is visible, so switching later is instant.
        /// </summary>
        private void PrewarmTabs()
        {
            if (_tabs.TabPages.Count == 0) return;

            _tabs.SuspendLayout();
            try
            {
                foreach (TabPage page in _tabs.TabPages)
                {
                    _tabs.SelectedTab = page;
                    var unused = page.Handle;      // forces the page and its children to exist
                }
                _tabs.SelectedIndex = 0;
            }
            catch { }
            finally { _tabs.ResumeLayout(true); }
        }

        // =====================================================================
        //  Menu and shortcuts
        // =====================================================================
        private MenuStrip BuildMenu()
        {
            var menu = new MenuStrip();

            var file = new ToolStripMenuItem("&File");
            Add(file, "&Save profile...", delegate { SaveProfile(); }, Keys.Control | Keys.S);
            Add(file, "&Load profile...", delegate { LoadProfile(); }, Keys.Control | Keys.P);
            file.DropDownItems.Add(new ToolStripSeparator());
            Add(file, "&Reset all options", delegate { ResetOptions(); }, Keys.None);
            file.DropDownItems.Add(new ToolStripSeparator());
            Add(file, "E&xit", delegate { Close(); }, Keys.Alt | Keys.F4);

            var run = new ToolStripMenuItem("&Run");
            Add(run, "&Download", delegate { StartDownload(false); }, Keys.F5);
            Add(run, "&Add to queue", delegate { EnqueueCurrentUrls(); }, Keys.Control | Keys.D);
            Add(run, "&Simulate", delegate { StartDownload(true); }, Keys.Control | Keys.Shift | Keys.S);
            Add(run, "List &formats...", delegate { ShowFormats(); }, Keys.Control | Keys.F);
            run.DropDownItems.Add(new ToolStripSeparator());
            Add(run, "S&top", delegate { StopAll(); }, Keys.None);

            var view = new ToolStripMenuItem("&View");
            Add(view, "&Log", delegate { _outTabs.SelectedIndex = TabLog; }, Keys.Control | Keys.D1);
            Add(view, "&Queue", delegate { _outTabs.SelectedIndex = TabQueue; }, Keys.Control | Keys.D2);
            Add(view, "&Command", delegate { _outTabs.SelectedIndex = TabCommand; }, Keys.Control | Keys.D3);
            view.DropDownItems.Add(new ToolStripSeparator());
            Add(view, "C&lear log", delegate { ClearLog(); }, Keys.Control | Keys.L);
            Add(view, "Copy c&ommand", delegate { CopyPreview(); }, Keys.Control | Keys.Shift | Keys.C);
            view.DropDownItems.Add(new ToolStripSeparator());
            _miAlwaysOnTop = new ToolStripMenuItem("Always on &top", null, delegate
            {
                _miAlwaysOnTop.Checked = !_miAlwaysOnTop.Checked;
                TopMost = _miAlwaysOnTop.Checked;
            });
            view.DropDownItems.Add(_miAlwaysOnTop);

            var tools = _miTools = new ToolStripMenuItem("&Tools");
            Add(tools, "&Update yt-dlp", delegate { RunTool(new List<string> { "-U" }, "Updating yt-dlp"); }, Keys.None);
            Add(tools, "Show &version info", delegate { RunTool(new List<string> { "--version" }, "yt-dlp version"); }, Keys.None);
            Add(tools, "List &extractors", delegate { RunTool(new List<string> { "--list-extractors" }, "Supported extractors"); }, Keys.None);
            Add(tools, "List &subtitles for first URL", delegate { ListSubtitles(); }, Keys.None);
            tools.DropDownItems.Add(new ToolStripSeparator());
            Add(tools, "&Open download folder", delegate { App.OpenFolder(txtOutDir.Text); }, Keys.Control | Keys.O);
            Add(tools, "Open &app folder", delegate { App.OpenFolder(App.BaseDir); }, Keys.None);
            Add(tools, "Open s&ettings file", delegate { App.OpenFile(App.SettingsFile); }, Keys.None);
            Add(tools, "Open error &log", delegate { App.OpenFile(Path.Combine(App.DataDir, "error.log")); }, Keys.None);
            tools.DropDownItems.Add(new ToolStripSeparator());
            Add(tools, "&Check dependencies", delegate { CheckDependencies(); }, Keys.F2);

            var help = new ToolStripMenuItem("&Help");
            Add(help, "&Keyboard shortcuts", delegate { ShowShortcuts(); }, Keys.F1);
            help.DropDownItems.Add(new ToolStripSeparator());
            Add(help, "yt-dlp &documentation", delegate { App.OpenUrl("https://github.com/yt-dlp/yt-dlp#readme"); }, Keys.None);
            Add(help, "Output &template reference", delegate { App.OpenUrl("https://github.com/yt-dlp/yt-dlp#output-template"); }, Keys.None);
            Add(help, "Format &selection reference", delegate { App.OpenUrl("https://github.com/yt-dlp/yt-dlp#format-selection"); }, Keys.None);
            help.DropDownItems.Add(new ToolStripSeparator());
            Add(help, "&About", delegate { ShowAbout(); }, Keys.None);

            menu.Items.Add(file);
            menu.Items.Add(run);
            menu.Items.Add(view);
            menu.Items.Add(tools);
            menu.Items.Add(help);
            return menu;
        }

        private static void Add(ToolStripMenuItem parent, string text, EventHandler handler, Keys shortcut)
        {
            var item = new ToolStripMenuItem(text, null, handler);
            if (shortcut != Keys.None)
            {
                item.ShortcutKeys = shortcut;
                item.ShowShortcutKeys = true;
            }
            parent.DropDownItems.Add(item);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && _runner.IsRunning) { StopAll(); return true; }
            if (keyData == Keys.F3) { FindInLog(); return true; }
            if (keyData == (Keys.Control | Keys.Enter)) { StartDownload(false); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private ContextMenuStrip BuildLogMenu()
        {
            var cm = new ContextMenuStrip();
            cm.Items.Add("Copy", null, delegate
            {
                try { if (_log.SelectionLength > 0) Clipboard.SetText(_log.SelectedText); }
                catch { }
            });
            cm.Items.Add("Select all", null, delegate { _log.SelectAll(); });
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add("Save...", null, delegate { SaveLog(); });
            cm.Items.Add("Clear", null, delegate { ClearLog(); });
            return cm;
        }

        // =====================================================================
        //  Live command preview
        // =====================================================================
        private void HookChangeEvents(Control root)
        {
            foreach (Control c in root.Controls)
            {
                var chk = c as CheckBox;
                if (chk != null) chk.CheckedChanged += AnyChanged;

                var rad = c as RadioButton;
                if (rad != null) rad.CheckedChanged += AnyChanged;

                var num = c as NumericUpDown;
                if (num != null) num.ValueChanged += AnyChanged;

                var cmb = c as ComboBox;
                if (cmb != null)
                {
                    cmb.SelectedIndexChanged += AnyChanged;
                    cmb.TextChanged += AnyChanged;
                }
                else
                {
                    var txt = c as TextBox;
                    if (txt != null) txt.TextChanged += AnyChanged;
                }

                HookChangeEvents(c);
            }
        }

        // Rebuilding the whole argument list on every keystroke is wasted work when someone is
        // typing a long template; coalesce into one rebuild shortly after they stop.
        private bool _previewDirty;

        private void AnyChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            _previewDirty = true;
        }

        private void UpdatePreview()
        {
            _previewDirty = false;
            try
            {
                var url = FirstUrl();
                var args = BuildArgs(new List<string> { string.IsNullOrEmpty(url) ? "<URL>" : url }, false);
                var exe = App.HasYtDlp ? App.YtDlpPath : "yt-dlp.exe";
                _cmdPreview.Text = Runner.Quote(exe) + " " + Runner.BuildArgumentLine(args);
                UpdateFormatEnablement();
            }
            catch (Exception ex)
            {
                _cmdPreview.Text = "Could not build the command: " + ex.Message;
            }
        }

        private void CopyPreview()
        {
            if (_previewDirty) UpdatePreview();
            try
            {
                if (_cmdPreview.TextLength > 0)
                {
                    Clipboard.SetText(_cmdPreview.Text);
                    SetStatus("Command copied to the clipboard.");
                }
            }
            catch (Exception ex) { SetStatus("Could not copy: " + ex.Message); }
        }

        private void SaveBatch()
        {
            if (_previewDirty) UpdatePreview();
            using (var d = new SaveFileDialog())
            {
                d.Filter = "Batch file (*.bat)|*.bat|All files (*.*)|*.*";
                d.FileName = "download.bat";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    // A batch file eats a single % as the start of a variable, and yt-dlp
                    // output templates are nothing but percent signs.
                    var command = _cmdPreview.Text.Replace("%", "%%");
                    var text = "@echo off\r\nchcp 65001 >nul\r\n" + command + "\r\npause\r\n";
                    File.WriteAllText(d.FileName, text, Encoding.UTF8);
                    SetStatus("Saved " + d.FileName);
                }
                catch (Exception ex) { Warn("Could not save the batch file: " + ex.Message); }
            }
        }

        // =====================================================================
        //  URLs
        // =====================================================================
        private List<string> GetUrls()
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in _urls.Text.Split('\n'))
            {
                var s = CleanUrl(raw);
                if (s == null || !seen.Add(s)) continue;
                list.Add(s);
            }
            return list;
        }

        /// <summary>Trims a pasted line down to the URL, or returns null if it is not one.</summary>
        private static string CleanUrl(string raw)
        {
            if (raw == null) return null;
            var s = raw.Trim().Trim('\r', '\uFEFF');
            if (s.Length == 0 || s.StartsWith("#")) return null;

            // Mail clients and chat apps wrap links in angle brackets or quotes.
            s = s.Trim('<', '>', '"', '\'');
            return s.Length == 0 ? null : s;
        }

        private string FirstUrl()
        {
            var u = GetUrls();
            return u.Count > 0 ? u[0] : null;
        }

        private void PasteUrls()
        {
            try
            {
                if (!Clipboard.ContainsText()) { SetStatus("There is no text on the clipboard."); return; }
                AppendUrls(Clipboard.GetText());
            }
            catch (Exception ex) { SetStatus("Could not read the clipboard: " + ex.Message); }
        }

        private void CleanUpUrls()
        {
            var urls = GetUrls();
            _urls.Text = string.Join(Environment.NewLine, urls.ToArray());
            _urls.SelectionStart = _urls.TextLength;
            SetStatus(urls.Count + " URL(s).");
        }

        private void OnFormDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.Text) || e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnFormDragDrop(object sender, DragEventArgs e)
        {
            try
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    foreach (var f in files)
                    {
                        if (!File.Exists(f)) continue;

                        // A dropped text file is a list of links. Anything else is not, and
                        // reading a 4 GB video into the URL box helps nobody.
                        var ext = Path.GetExtension(f).ToLowerInvariant();
                        if (ext != ".txt" && ext != ".csv" && ext != ".url" && ext != ".log" && ext != "")
                        {
                            SetStatus("Ignored " + Path.GetFileName(f) + " - only text files are read as link lists.");
                            continue;
                        }
                        var info = new FileInfo(f);
                        if (info.Length > 4 * 1024 * 1024)
                        {
                            SetStatus("Ignored " + Path.GetFileName(f) + " - too large to be a link list.");
                            continue;
                        }
                        AppendUrls(File.ReadAllText(f));
                    }
                }
                else if (e.Data.GetDataPresent(DataFormats.Text))
                {
                    AppendUrls((string)e.Data.GetData(DataFormats.Text));
                }
            }
            catch (Exception ex) { SetStatus("Could not read the dropped data: " + ex.Message); }
        }

        private void AppendUrls(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var trimmed = text.Trim();
            if (trimmed.Length == 0) return;

            if (_urls.TextLength > 0 && !_urls.Text.EndsWith("\n")) _urls.AppendText(Environment.NewLine);
            _urls.AppendText(trimmed);
            _urls.SelectionStart = _urls.TextLength;
            _urls.ScrollToCaret();
        }

        // =====================================================================
        //  Queue
        // =====================================================================
        private void EnqueueCurrentUrls()
        {
            var urls = GetUrls();
            if (urls.Count == 0) { Warn("Enter at least one URL first."); return; }

            foreach (var u in urls) AddJob(u, BuildArgs(new List<string> { u }, false), false);

            _urls.Clear();
            _outTabs.SelectedIndex = TabQueue;
            SetStatus(urls.Count + " item(s) added to the queue. Press Download to start.");
        }

        private Job AddJob(string url, List<string> args, bool simulate)
        {
            var job = new Job { Url = url, Args = args, Simulate = simulate };
            var item = new ListViewItem(new[] { (_queue.Count + 1).ToString(CultureInfo.InvariantCulture), job.StateText, "", "", url, "" });
            item.Tag = job;
            job.Item = item;
            _queue.Add(job);
            _queueView.Items.Add(item);
            return job;
        }

        private void UpdateJobRow(Job j)
        {
            if (j.Item == null) return;
            j.Item.SubItems[1].Text = j.StateText;
            j.Item.SubItems[2].Text = j.Fraction > 0 ? (j.Fraction * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "";
            j.Item.SubItems[3].Text = j.Position ?? "";
            j.Item.SubItems[5].Text = j.Detail ?? "";

            switch (j.State)
            {
                case JobState.Failed: j.Item.ForeColor = Color.Firebrick; break;
                case JobState.Done: j.Item.ForeColor = Color.ForestGreen; break;
                case JobState.Stopped: j.Item.ForeColor = SystemColors.GrayText; break;
                default: j.Item.ForeColor = SystemColors.WindowText; break;
            }
        }

        private List<Job> SelectedJobs()
        {
            var list = new List<Job>();
            foreach (ListViewItem it in _queueView.SelectedItems)
            {
                var j = it.Tag as Job;
                if (j != null) list.Add(j);
            }
            return list;
        }

        private void RemoveSelectedJobs()
        {
            var jobs = SelectedJobs();
            if (jobs.Count == 0) { SetStatus("Select a queue row first."); return; }

            int skipped = 0;
            foreach (var j in jobs)
            {
                if (j.State == JobState.Running) { skipped++; continue; }

                // A run in progress walks its own list by index, so the entry is marked
                // rather than pulled out of it: removing it would shift the index of the
                // job being downloaded. RunNext only picks up entries still marked Queued.
                j.State = JobState.Stopped;
                _queueView.Items.Remove(j.Item);
                _queue.Remove(j);
            }
            Renumber();
            SetStatus(skipped > 0
                ? "Removed " + (jobs.Count - skipped) + " item(s); the running one was left alone."
                : "Removed " + jobs.Count + " item(s).");
        }

        private void MoveSelectedJobs(int delta)
        {
            var jobs = SelectedJobs();
            if (jobs.Count == 0) return;
            if (_runner.IsRunning) { SetStatus("The queue cannot be reordered while a download is running."); return; }

            // Walk in the direction of travel so a block of rows keeps its own order.
            var indices = new List<int>();
            foreach (var j in jobs) indices.Add(_queue.IndexOf(j));
            indices.Sort();
            if (delta > 0) indices.Reverse();

            foreach (var i in indices)
            {
                int to = i + delta;
                if (to < 0 || to >= _queue.Count) continue;
                var job = _queue[i];
                _queue.RemoveAt(i);
                _queue.Insert(to, job);
            }
            RebuildQueueView(jobs);
        }

        private void RebuildQueueView(List<Job> keepSelected)
        {
            _queueView.BeginUpdate();
            try
            {
                _queueView.Items.Clear();
                foreach (var j in _queue)
                {
                    var item = new ListViewItem(new[] { "", j.StateText, "", "", j.Url, "" });
                    item.Tag = j;
                    j.Item = item;
                    _queueView.Items.Add(item);
                    UpdateJobRow(j);
                }
                Renumber();
                if (keepSelected != null)
                    foreach (var j in keepSelected)
                        if (j.Item != null) j.Item.Selected = true;
            }
            finally { _queueView.EndUpdate(); }
        }

        private void ClearFinishedJobs()
        {
            int n = 0;
            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                var j = _queue[i];
                if (j.State == JobState.Done || j.State == JobState.Failed || j.State == JobState.Stopped)
                {
                    _queueView.Items.Remove(j.Item);
                    _queue.RemoveAt(i);
                    n++;
                }
            }
            Renumber();
            SetStatus(n + " finished item(s) removed.");
        }

        private void ClearQueue()
        {
            if (_runner.IsRunning) { Warn("Stop the current download first."); return; }
            _queue.Clear();
            _queueView.Items.Clear();
            SetStatus("Queue cleared.");
        }

        private void RetryFailed()
        {
            if (_runner.IsRunning) { Warn("Stop the current download first."); return; }
            int n = 0;
            foreach (var j in _queue)
            {
                if (j.State == JobState.Failed || j.State == JobState.Stopped)
                {
                    Requeue(j);
                    n++;
                }
            }
            SetStatus(n == 0 ? "Nothing to retry." : n + " item(s) reset. Press Download to run them.");
        }

        private void RequeueSelected()
        {
            if (_runner.IsRunning) { Warn("Stop the current download first."); return; }
            int n = 0;
            foreach (var j in SelectedJobs())
            {
                if (j.State == JobState.Running) continue;
                Requeue(j);
                n++;
            }
            SetStatus(n + " item(s) queued again.");
        }

        private void Requeue(Job j)
        {
            j.State = JobState.Queued;
            j.Fraction = 0;
            j.Detail = "";
            j.Position = "";
            UpdateJobRow(j);
        }

        private void Renumber()
        {
            for (int i = 0; i < _queue.Count; i++)
                if (_queue[i].Item != null)
                    _queue[i].Item.SubItems[0].Text = (i + 1).ToString(CultureInfo.InvariantCulture);
        }

        private void CopySelectedJobUrl()
        {
            var jobs = SelectedJobs();
            if (jobs.Count == 0) return;
            var sb = new StringBuilder();
            foreach (var j in jobs) sb.AppendLine(j.Url);
            try { Clipboard.SetText(sb.ToString().TrimEnd()); SetStatus("URL copied."); }
            catch { }
        }

        private void CopySelectedJobCommand()
        {
            var jobs = SelectedJobs();
            if (jobs.Count == 0) return;
            var exe = App.HasYtDlp ? App.YtDlpPath : "yt-dlp.exe";
            try
            {
                Clipboard.SetText(Runner.Quote(exe) + " " + Runner.BuildArgumentLine(jobs[0].Args));
                SetStatus("Command for that queue entry copied.");
            }
            catch { }
        }

        private void OpenSelectedJobUrl()
        {
            var jobs = SelectedJobs();
            if (jobs.Count == 0) return;
            App.OpenUrl(jobs[0].Url);
        }

        // =====================================================================
        //  Running
        // =====================================================================
        private void StartDownload(bool simulate)
        {
            if (_runner.IsRunning) { Warn("A download is already running."); return; }
            if (!EnsureYtDlp()) return;
            if (_previewDirty) UpdatePreview();

            // Anything typed in the URL box joins the queue first.
            var typed = GetUrls();
            foreach (var u in typed)
            {
                var args = BuildArgs(new List<string> { u }, false);
                if (simulate) args.Insert(0, "--simulate");
                AddJob(u, args, simulate);
            }
            if (typed.Count > 0) _urls.Clear();

            _run.Clear();
            foreach (var j in _queue) if (j.State == JobState.Queued) _run.Add(j);

            if (_run.Count == 0)
            {
                Warn("There is nothing to download.\r\n\r\nEnter a URL above, or use Retry failed on the Queue tab.");
                return;
            }

            if (!simulate && !ValidateOutputDir()) return;

            _stopRequested = false;
            _okCount = 0;
            _failCount = 0;
            _runPos = -1;
            _runStarted = DateTime.UtcNow;
            _retriedCurrentJob = false;
            _progress.BeginRun(_run.Count);

            _barOverall.State = BarState.Normal;
            _barFile.State = BarState.Normal;
            // Indeterminate until the first byte: yt-dlp is talking to the site, and a bar
            // sitting at whatever the last run left behind would be a lie.
            _barOverall.SetFraction(-1);
            _barFile.SetFraction(0);
            Native.KeepAwake(true);
            Native.SetTaskbarState(SafeHandle(), TaskbarState.Normal);

            SetRunningUi(true);
            // Show the log, unless the user is watching the queue - that is the other
            // reasonable place to be looking when a run starts.
            if (_outTabs.SelectedIndex != TabQueue) _outTabs.SelectedIndex = TabLog;
            AppendLog(Environment.NewLine + "[gui] Starting " + _run.Count + " queue item(s)" +
                      (simulate ? " in simulate mode." : "."), LineKind.Gui);
            RunNext();
        }

        private bool ValidateOutputDir()
        {
            var dir = txtOutDir.Text.Trim();
            if (dir.Length == 0) return true;          // yt-dlp will use the working directory
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // Prove it is actually writable now, rather than after a 2 GB download.
                var probe = Path.Combine(dir, ".ytdlp-gui-probe");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch (Exception ex)
            {
                Warn("The download folder cannot be written to:\r\n\r\n" + dir + "\r\n\r\n" + ex.Message);
                _tabs.SelectedIndex = 0;
                return false;
            }
        }

        private void RunNext()
        {
            if (_stopRequested) { FinishRun("Stopped."); return; }

            int next = -1;
            for (int i = _runPos + 1; i < _run.Count; i++)
                if (_run[i].State == JobState.Queued) { next = i; break; }

            if (next < 0)
            {
                FinishRun(string.Format(CultureInfo.InvariantCulture,
                    "Finished: {0} succeeded, {1} failed, in {2}.",
                    _okCount, _failCount, Ui.Duration((DateTime.UtcNow - _runStarted).TotalSeconds)));
                return;
            }

            _runPos = next;
            _retriedCurrentJob = false;
            StartJob(_run[next]);
        }

        private void StartJob(Job job)
        {
            job.State = JobState.Running;
            job.Fraction = 0;
            job.Started = DateTime.UtcNow;
            UpdateJobRow(job);
            if (job.Item != null) { job.Item.EnsureVisible(); }

            _progress.BeginJob(_runPos);
            _barFile.SetFraction(0);

            AppendLog(Environment.NewLine + "==> " + job.Url, LineKind.Info);
            AppendLog("    " + Runner.Quote(App.YtDlpPath) + " " + Runner.BuildArgumentLine(job.Args), LineKind.Normal);

            try
            {
                _runner.Start(App.YtDlpPath, job.Args, WorkingDirForRun());
            }
            catch (Exception ex)
            {
                AppendLog("ERROR: could not start yt-dlp: " + ex.Message, LineKind.Error);
                job.State = JobState.Failed;
                job.Detail = ex.Message;
                UpdateJobRow(job);
                _failCount++;
                _progress.EndJob();
                RunNext();
            }
        }

        private string WorkingDirForRun()
        {
            var d = txtOutDir.Text.Trim();
            try { if (d.Length > 0 && Directory.Exists(d)) return d; }
            catch { }
            return App.BaseDir;
        }

        private void OnRunnerLine(string line, LineKind kind)
        {
            if (kind == LineKind.Error && _unsupportedOption == null)
            {
                var m = RxNoSuchOption.Match(line);
                if (m.Success) _unsupportedOption = m.Groups["opt"].Value;
            }
            lock (_pending) _pending.Add(new KeyValuePair<string, LineKind>(line, kind));
        }

        private void OnRunnerProgress(ProgressInfo p)
        {
            _progress.Feed(p);
        }

        private void OnRunnerFinished(int exitCode)
        {
            // Raised on a background thread - hop to the UI thread.
            try
            {
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke((MethodInvoker)delegate { JobFinished(exitCode); });
            }
            catch { }
        }

        private void JobFinished(int exitCode)
        {
            FlushPending();

            Job job = _runPos >= 0 && _runPos < _run.Count ? _run[_runPos] : null;

            // An old yt-dlp may not know an option this GUI adds for its own benefit. Drop it
            // and run the entry again rather than reporting a failure the user cannot act on.
            if (job != null && exitCode != 0 && !_stopRequested && !_retriedCurrentJob &&
                _unsupportedOption != null && App.DisableOptionalArg(_unsupportedOption))
            {
                AppendLog("[gui] This yt-dlp does not support " + _unsupportedOption +
                          "; retrying without it.", LineKind.Gui);
                _unsupportedOption = null;
                _retriedCurrentJob = true;

                // Every entry still to come was built with the same option in it.
                foreach (var q in _queue)
                    if (q.State == JobState.Queued || ReferenceEquals(q, job))
                        q.Args = StripOptionalArgs(q.Args);
                job.Args = StripOptionalArgs(job.Args);
                UpdatePreview();

                StartJob(job);
                return;
            }
            _unsupportedOption = null;

            if (job != null)
            {
                job.Ended = DateTime.UtcNow;
                if (_stopRequested) job.State = JobState.Stopped;
                else if (exitCode == 0)
                {
                    job.State = JobState.Done;
                    job.Fraction = 1;
                    _okCount++;
                }
                else
                {
                    job.State = JobState.Failed;
                    if (string.IsNullOrEmpty(job.Detail)) job.Detail = "exit code " + exitCode;
                    _failCount++;
                }
                UpdateJobRow(job);
            }

            _progress.EndJob();
            if (_stopRequested) { FinishRun("Stopped."); return; }
            RunNext();
        }

        private void StopAll()
        {
            if (!_runner.IsRunning)
            {
                SetRunningUi(false);
                return;
            }
            _stopRequested = true;
            _btnStop.Enabled = false;
            _barOverall.State = BarState.Paused;
            _barFile.State = BarState.Paused;
            Native.SetTaskbarState(SafeHandle(), TaskbarState.Paused);
            SetStatus("Stopping...");
            AppendLog("[gui] Stop requested - terminating yt-dlp and any child processes.", LineKind.Warning);
            _runner.Stop();
        }

        private void FinishRun(string message)
        {
            SetRunningUi(false);
            Native.KeepAwake(false);

            bool clean = _failCount == 0 && _okCount > 0 && !_stopRequested;
            _barOverall.State = _stopRequested ? BarState.Paused : (_failCount > 0 ? BarState.Error : BarState.Normal);
            _barFile.SetFraction(clean ? 1 : 0);
            // Never leave a marquee running on a run that has ended.
            _barOverall.SetFraction(clean ? 1 : Math.Max(0, _progress.Read().Overall));

            var hwnd = SafeHandle();
            if (clean) { Native.SetTaskbarProgress(hwnd, 1); Native.SetTaskbarState(hwnd, TaskbarState.NoProgress); }
            else if (_failCount > 0) Native.SetTaskbarState(hwnd, TaskbarState.Error);
            else Native.SetTaskbarState(hwnd, TaskbarState.NoProgress);

            _lblOverall.Text = message;
            _lblFile.Text = "";
            SetStatus(message);
            SetStats("");
            AppendLog("[gui] " + message, LineKind.Gui);

            if (!_closing && !_stopRequested)
            {
                if (_chkAlertWhenDone.Checked)
                {
                    Native.FlashWindow(hwnd);
                    try { System.Media.SystemSounds.Asterisk.Play(); }
                    catch { }
                }
                if (_chkOpenWhenDone.Checked && _okCount > 0) App.OpenFolder(txtOutDir.Text);
            }

            _runPos = -1;
            _run.Clear();
        }

        private void SetRunningUi(bool running)
        {
            _btnDownload.Enabled = !running;
            _btnQueueAdd.Enabled = !running;
            _btnSimulate.Enabled = !running;
            _btnStop.Enabled = running;
            _uiTimer.Interval = running ? 120 : 400;

            // Updating or replacing yt-dlp underneath a running download is not something to
            // let happen by accident.
            if (_miTools != null) _miTools.Enabled = !running;
        }

        private IntPtr SafeHandle()
        {
            try { return IsHandleCreated && !IsDisposed ? Handle : IntPtr.Zero; }
            catch { return IntPtr.Zero; }
        }

        /// <summary>Removes the arguments this GUI adds for its own benefit, keeping the user's.</summary>
        private static List<string> StripOptionalArgs(List<string> args)
        {
            var result = new List<string>();
            for (int i = 0; i < args.Count; i++)
            {
                if (App.IsDisabledOptionalArg(args[i]))
                {
                    i++;                                // skip its value too
                    continue;
                }
                result.Add(args[i]);
            }
            return result;
        }

        // =====================================================================
        //  Short helper invocations (version, update, extractor list, subtitles)
        // =====================================================================
        private void RunTool(List<string> args, string title)
        {
            if (!EnsureYtDlp()) return;
            if (_toolRun != null) { SetStatus("Another tool is still running."); return; }
            if (_runner.IsRunning) { Warn("Wait for the download to finish first."); return; }

            _outTabs.SelectedIndex = TabLog;
            AppendLog(Environment.NewLine + "==> " + title, LineKind.Gui);
            SetStatus(title + "...");
            _barOverall.SetFraction(-1);                // marquee while we wait

            _toolRun = ToolRun.Begin(App.YtDlpPath, args, 180000, delegate (ToolRun r)
            {
                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke((MethodInvoker)delegate { ToolFinished(r, title); });
                }
                catch { }
            });
        }

        private void ToolFinished(ToolRun r, string title)
        {
            _toolRun = null;
            _barOverall.SetFraction(0);

            foreach (var l in r.Combined.Replace("\r", "").Split('\n'))
            {
                if (l.Length == 0) continue;
                AppendLog(l, l.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0
                    ? LineKind.Error
                    : l.IndexOf("WARNING", StringComparison.OrdinalIgnoreCase) >= 0
                        ? LineKind.Warning
                        : LineKind.Normal);
            }
            SetStatus(title + " - " + (r.Ok ? "done." : "exit code " + r.ExitCode + "."));

            // The downloader may have just replaced itself.
            App.Rescan();
            RefreshDependencyStatus();
        }

        private void ListSubtitles()
        {
            var url = FirstUrl();
            if (string.IsNullOrEmpty(url)) { Warn("Enter a URL first."); return; }

            var args = NetworkArgsForQuery();
            args.Add("--list-subs");
            args.Add("--skip-download");
            args.Add(url);
            RunTool(args, "Subtitles for " + url);
        }

        private void ShowFormats()
        {
            var url = FirstUrl();
            if (string.IsNullOrEmpty(url)) { Warn("Enter a URL first."); return; }
            if (!EnsureYtDlp()) return;

            using (var dlg = new FormatsForm(url, NetworkArgsForQuery()))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dlg.SelectedFormat))
                {
                    radFmtCustom.Checked = true;
                    txtCustomFormat.Text = dlg.SelectedFormat;
                    _tabs.SelectedIndex = 1;
                    UpdatePreview();
                    SetStatus("Format selector set to: " + dlg.SelectedFormat);
                }
            }
        }

        // =====================================================================
        //  UI pump
        // =====================================================================
        private void UiTimerTick(object sender, EventArgs e)
        {
            FlushPending();
            if (_previewDirty) UpdatePreview();
            if (_runner.IsRunning) RenderProgress();
        }

        private void RenderProgress()
        {
            var s = _progress.Read();

            _barOverall.SetFraction(s.Overall);
            // Only one indeterminate indicator at a time: the overall bar carries the "still
            // working it out" state, so this one stays a real figure or nothing.
            _barFile.SetFraction(s.File < 0 ? 0 : s.File);
            Native.SetTaskbarProgress(SafeHandle(), s.Overall);

            var where = new List<string>();
            if (s.Overall >= 0) where.Add((s.Overall * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%");
            if (s.JobCount > 1) where.Add("queue " + s.JobIndex + " of " + s.JobCount);
            if (s.ItemCount > 1 && s.ItemIndex > 0) where.Add("video " + s.ItemIndex + " of " + s.ItemCount);

            if (s.RunElapsed >= 1) where.Add(Ui.Duration(s.RunElapsed) + " elapsed");

            var left = Ui.Duration(s.OverallEta);
            if (left.Length > 0) where.Add("about " + left + " left");

            if (!string.IsNullOrEmpty(s.PostProcessor)) where.Add(Describe(s.PostProcessor));
            _lblOverall.Text = _stopRequested ? "Stopping..." : Ui.Join("   -   ", where.ToArray());

            var detail = new List<string>();
            if (!string.IsNullOrEmpty(s.FileName)) detail.Add(Path.GetFileName(s.FileName));
            if (s.File >= 0) detail.Add((s.File * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%");
            if (s.Total > 0) detail.Add(Ui.Size(s.Downloaded) + " of " + Ui.Size(s.Total));
            else if (s.Downloaded > 0) detail.Add(Ui.Size(s.Downloaded));
            if (s.Speed > 0) detail.Add(Ui.Rate(s.Speed));
            if (s.Eta >= 0) detail.Add("ETA " + Ui.Duration(s.Eta));
            if (s.FragCount > 0) detail.Add("fragment " + s.FragIndex + " of " + s.FragCount);
            _lblFile.Text = Ui.Join("   -   ", detail.ToArray());

            SetStats(Ui.Join("   ",
                s.RunBytes > 0 ? Ui.Size(s.RunBytes) + " downloaded" : "",
                s.Speed > 0 ? Ui.Rate(s.Speed) : ""));

            // Mirror onto the running queue row.
            if (_runPos >= 0 && _runPos < _run.Count)
            {
                var job = _run[_runPos];
                double f = s.Job < 0 ? 0 : s.Job;
                string pos = s.ItemCount > 1 && s.ItemIndex > 0 ? s.ItemIndex + " / " + s.ItemCount : "";
                if (Math.Abs(f - job.Fraction) > 0.004 || pos != job.Position)
                {
                    job.Fraction = f;
                    job.Position = pos;
                    UpdateJobRow(job);
                }
            }
        }

        private static string Describe(string postProcessor)
        {
            switch (postProcessor)
            {
                case "Merger": return "merging video and audio";
                case "ExtractAudio": return "extracting audio";
                case "VideoRemuxer": return "remuxing";
                case "VideoConvertor": return "re-encoding";
                case "EmbedSubtitle": return "embedding subtitles";
                case "EmbedThumbnail": return "embedding thumbnail";
                case "Metadata": return "writing metadata";
                case "SponsorBlock": return "querying SponsorBlock";
                case "ModifyChapters": return "editing chapters";
                case "SplitChapters": return "splitting chapters";
                case "ThumbnailsConvertor": return "converting thumbnail";
                default: return "post-processing (" + postProcessor + ")";
            }
        }

        private void FlushPending()
        {
            List<KeyValuePair<string, LineKind>> batch = null;
            lock (_pending)
            {
                if (_pending.Count > 0)
                {
                    batch = new List<KeyValuePair<string, LineKind>>(_pending);
                    _pending.Clear();
                }
            }
            if (batch == null) return;

            bool showProgress = _chkLogProgress != null && _chkLogProgress.Checked;

            // Append one coloured run at a time: touching the RichTextBox per line is what made
            // long playlists crawl.
            var run = new StringBuilder();
            var runKind = LineKind.Normal;
            bool runOpen = false;

            foreach (var kv in batch)
            {
                TrackJobDetail(kv.Key, kv.Value);

                if (kv.Value == LineKind.Progress && !showProgress) continue;

                // A machine progress line is rendered back into something readable rather
                // than shown raw - it is the GUI's own wiring, not yt-dlp's output.
                var text = kv.Value == LineKind.Progress
                    ? (Runner.DescribeProgressLine(kv.Key) ?? kv.Key)
                    : kv.Key;
                if (text == null) continue;

                if (runOpen && kv.Value != runKind)
                {
                    AppendLog(run.ToString(), runKind, false);
                    run.Length = 0;
                    runOpen = false;
                }
                if (!runOpen) { runKind = kv.Value; runOpen = true; }
                run.Append(text).Append(Environment.NewLine);
            }
            if (runOpen) AppendLog(run.ToString(), runKind, false);

            ScrollLogToEnd();
        }

        /// <summary>Reflects interesting output lines on the running queue row.</summary>
        private void TrackJobDetail(string line, LineKind kind)
        {
            if (_runPos < 0 || _runPos >= _run.Count) return;

            var job = _run[_runPos];
            var before = job.Detail;

            if (line.StartsWith("[download] Destination:"))
                job.Detail = Path.GetFileName(line.Substring("[download] Destination:".Length).Trim());
            else if (kind == LineKind.Error)
                job.Detail = line.Length > 240 ? line.Substring(0, 240) : line;
            else if (line.StartsWith("[Merger]") || line.StartsWith("[ExtractAudio]"))
                job.Detail = line;
            else if (line.StartsWith("[download] Downloading item "))
                job.Detail = line.Substring("[download] ".Length);
            else return;

            if (job.Detail != before) UpdateJobRow(job);
        }

        private void AppendLog(string text, LineKind kind)
        {
            AppendLog(text, kind, true);
        }

        /// <summary>
        /// Appends a block of text in one colour. <paramref name="scroll"/> is false while a
        /// batch is being written so the caret is only moved once at the end.
        /// </summary>
        private void AppendLog(string text, LineKind kind, bool scroll)
        {
            if (string.IsNullOrEmpty(text) || _log == null) return;

            TrimLogIfHuge();

            var color = SystemColors.WindowText;
            switch (kind)
            {
                case LineKind.Error: color = Color.Firebrick; break;
                case LineKind.Warning: color = Color.DarkGoldenrod; break;
                case LineKind.Info: color = Color.RoyalBlue; break;
                case LineKind.Gui: color = Color.DarkSlateBlue; break;
                case LineKind.Progress: color = SystemColors.GrayText; break;
            }

            _log.SelectionStart = _log.TextLength;
            _log.SelectionLength = 0;
            _log.SelectionColor = color;
            _log.AppendText(text.EndsWith(Environment.NewLine) ? text : text + Environment.NewLine);
            _log.SelectionColor = SystemColors.WindowText;

            if (scroll) ScrollLogToEnd();
        }

        /// <summary>
        /// Drops the oldest part of the log instead of clearing it: on a long playlist the
        /// interesting lines are the recent ones, and losing all of them is worse than losing
        /// the start.
        /// </summary>
        private void TrimLogIfHuge()
        {
            if (_log.TextLength <= LogMaxChars) return;

            Native.SuspendDrawing(_log);
            try
            {
                int cut = _log.TextLength - LogTrimTo;
                _log.SelectionStart = 0;
                _log.SelectionLength = cut;
                _log.SelectedText = "";
                _log.SelectionStart = 0;
                _log.SelectionLength = 0;
                _log.SelectionColor = SystemColors.GrayText;
                _log.SelectedText = "[gui] ... earlier output trimmed to keep memory use low ..." + Environment.NewLine;
                _log.SelectionStart = _log.TextLength;
                _log.SelectionColor = SystemColors.WindowText;
            }
            catch { }
            finally { Native.ResumeDrawing(_log); }
        }

        private void ScrollLogToEnd()
        {
            if (_chkAutoScroll == null || !_chkAutoScroll.Checked) return;
            _log.SelectionStart = _log.TextLength;
            _log.SelectionLength = 0;
            _log.ScrollToCaret();
        }

        private void ClearLog()
        {
            _log.Clear();
            SetStatus("Log cleared.");
        }

        private void FindInLog()
        {
            var needle = _findBox.Text;
            if (string.IsNullOrEmpty(needle)) { _findBox.Focus(); return; }

            int from = _log.SelectionStart + Math.Max(1, _log.SelectionLength);
            if (from >= _log.TextLength) from = 0;

            int at = _log.Find(needle, from, RichTextBoxFinds.None);
            if (at < 0 && from > 0) at = _log.Find(needle, 0, RichTextBoxFinds.None);

            if (at < 0) { SetStatus("\"" + needle + "\" is not in the log."); return; }

            _log.Select(at, needle.Length);
            _log.ScrollToCaret();
            _log.Focus();
            SetStatus("Found \"" + needle + "\".");
        }

        private void SaveLog()
        {
            using (var d = new SaveFileDialog())
            {
                d.Filter = "Text file (*.txt)|*.txt|All files (*.*)|*.*";
                d.FileName = "yt-dlp-log-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".txt";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(d.FileName, _log.Text, new UTF8Encoding(false));
                    SetStatus("Log saved to " + d.FileName);
                }
                catch (Exception ex) { Warn("Could not save the log: " + ex.Message); }
            }
        }

        // =====================================================================
        //  Settings / profiles / geometry
        // =====================================================================
        private void LoadSettings()
        {
            _loading = true;
            try
            {
                var map = Settings.Load(App.SettingsFile);
                _savedSettings = map;
                if (map.Count > 0) Settings.Apply(this, map);
                if (string.IsNullOrEmpty(txtOutDir.Text)) txtOutDir.Text = App.DefaultDownloadDir;
                _urls.Clear();                       // never restore stale URLs
                _findBox.Clear();
                ReadGeometry(map);
            }
            catch { }
            finally { _loading = false; }
        }

        private void ReadGeometry(Dictionary<string, string> map)
        {
            int w = GetInt(map, "winWidth", 0), h = GetInt(map, "winHeight", 0);
            int x = GetInt(map, "winLeft", int.MinValue), y = GetInt(map, "winTop", int.MinValue);

            if (w >= MinimumSize.Width && h >= MinimumSize.Height && x != int.MinValue && y != int.MinValue)
            {
                var bounds = new Rectangle(x, y, w, h);

                // Only restore a position that is still on a screen: a monitor may have gone.
                foreach (var screen in Screen.AllScreens)
                {
                    if (screen.WorkingArea.IntersectsWith(bounds))
                    {
                        _restoredBounds = bounds;
                        StartPosition = FormStartPosition.Manual;
                        Bounds = bounds;
                        break;
                    }
                }
            }
            if (GetInt(map, "winMaximized", 0) == 1) WindowState = FormWindowState.Maximized;

            _miAlwaysOnTop.Checked = GetInt(map, "alwaysOnTop", 0) == 1;
            TopMost = _miAlwaysOnTop.Checked;
        }

        private void ApplyRestoredGeometry()
        {
            // The splitter can only be positioned once the form has a real height, which is
            // after Shown - hence the second step, reading the map loaded during Load.
            int stored = GetInt(_savedSettings, "splitter", 0);
            try
            {
                int wanted = stored > 0 ? stored : (int)(_split.Height * 0.70);
                _split.SplitterDistance = Math.Max(_split.Panel1MinSize,
                    Math.Min(_split.Height - _split.Panel2MinSize - _split.SplitterWidth, wanted));
            }
            catch { }
        }

        private static int GetInt(Dictionary<string, string> map, string key, int fallback)
        {
            string v;
            int n;
            if (map != null && map.TryGetValue(key, out v) &&
                int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                return n;
            return fallback;
        }

        private void SaveSettings()
        {
            try
            {
                var map = Settings.Capture(this);
                map.Remove("urls");
                map.Remove("logFind");

                var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                if (bounds.Width <= 0 || bounds.Height <= 0) bounds = _restoredBounds;
                if (bounds.Width > 0 && bounds.Height > 0)
                {
                    map["winLeft"] = bounds.Left.ToString(CultureInfo.InvariantCulture);
                    map["winTop"] = bounds.Top.ToString(CultureInfo.InvariantCulture);
                    map["winWidth"] = bounds.Width.ToString(CultureInfo.InvariantCulture);
                    map["winHeight"] = bounds.Height.ToString(CultureInfo.InvariantCulture);
                }
                map["winMaximized"] = WindowState == FormWindowState.Maximized ? "1" : "0";
                map["alwaysOnTop"] = _miAlwaysOnTop.Checked ? "1" : "0";
                try { map["splitter"] = _split.SplitterDistance.ToString(CultureInfo.InvariantCulture); }
                catch { }

                Settings.Save(App.SettingsFile, map);
            }
            catch { }
        }

        private void SaveProfile()
        {
            using (var d = new SaveFileDialog())
            {
                d.InitialDirectory = App.ProfilesDir;
                d.Filter = "yt-dlp GUI profile (*.ytprofile)|*.ytprofile|All files (*.*)|*.*";
                d.FileName = "profile.ytprofile";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var map = Settings.Capture(this);
                    map.Remove("urls");
                    map.Remove("logFind");
                    Settings.Save(d.FileName, map);
                    SetStatus("Profile saved to " + d.FileName);
                }
                catch (Exception ex) { Warn("Could not save the profile: " + ex.Message); }
            }
        }

        private void LoadProfile()
        {
            using (var d = new OpenFileDialog())
            {
                d.InitialDirectory = App.ProfilesDir;
                d.Filter = "yt-dlp GUI profile (*.ytprofile)|*.ytprofile|All files (*.*)|*.*";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    _loading = true;
                    Settings.Apply(this, Settings.Load(d.FileName));
                    SetStatus("Profile loaded from " + d.FileName);
                }
                catch (Exception ex) { Warn("Could not load the profile: " + ex.Message); }
                finally { _loading = false; UpdatePreview(); }
            }
        }

        private void ResetOptions()
        {
            if (MessageBox.Show(this, "Reset every option back to its default?", App.Title,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            ApplyDefaults();
            SetStatus("Options reset to defaults.");
        }

        /// <summary>
        /// Puts every control back to the value it had before any settings were restored.
        /// Those values were captured once at startup; the previous version built a whole
        /// second MainForm to rediscover them, and left that form's timer running.
        /// </summary>
        private void ApplyDefaults()
        {
            _loading = true;
            try
            {
                if (_defaults != null) Settings.Apply(this, _defaults);
                txtOutDir.Text = App.DefaultDownloadDir;
            }
            finally { _loading = false; }

            RefreshDependencyStatus();
            UpdatePreview();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_runner.IsRunning)
            {
                var r = MessageBox.Show(this,
                    "A download is still running.\r\n\r\nStop it and exit?", App.Title,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) { e.Cancel = true; return; }
                _stopRequested = true;
                _closing = true;
                _runner.Stop();
            }

            _closing = true;
            var tool = _toolRun;
            if (tool != null) tool.Cancel();

            _uiTimer.Stop();
            Native.KeepAwake(false);
            Native.SetTaskbarState(SafeHandle(), TaskbarState.NoProgress);
            SaveSettings();
        }

        // =====================================================================
        //  Dependencies and about
        // =====================================================================
        private bool EnsureYtDlp()
        {
            if (App.HasYtDlp && File.Exists(App.YtDlpPath)) return true;

            App.Rescan();
            RefreshDependencyStatus();
            if (App.HasYtDlp) return true;

            Warn("yt-dlp was not found.\r\n\r\nPut yt-dlp.exe (or yt-dlp_x86.exe) next to this program, " +
                 "or somewhere on your PATH, then use Tools > Check dependencies.");
            return false;
        }

        private void CheckDependencies()
        {
            App.Rescan();
            RefreshDependencyStatus();

            var sb = new StringBuilder();
            sb.AppendLine("yt-dlp:      " + (App.HasYtDlp ? App.YtDlpPath : "NOT FOUND"));
            sb.AppendLine("ffmpeg:      " + (App.HasFfmpeg ? App.FfmpegPath : "NOT FOUND"));
            sb.AppendLine("ffprobe:     " + (App.FfprobePath ?? "NOT FOUND"));
            sb.AppendLine("JS runtime:  " + (App.DenoPath ?? "NOT FOUND"));
            sb.AppendLine();
            sb.AppendLine("yt-dlp version: " + (App.YtDlpVersion ?? "unknown"));
            sb.AppendLine();

            if (!App.HasFfmpeg)
                sb.AppendLine("Without ffmpeg, merging video+audio, audio extraction and remuxing cannot work.");
            if (!App.HasJsRuntime)
                sb.AppendLine("Without a JavaScript runtime (deno.exe next to this program), YouTube " +
                              "extraction can fail or return only some of the formats.");
            if (App.HasYtDlp && App.HasFfmpeg && App.HasJsRuntime)
                sb.AppendLine("Everything needed is present.");

            MessageBox.Show(this, sb.ToString(), "Dependencies", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void RefreshDependencyStatus()
        {
            _statusDeps.Text = "yt-dlp: " + (App.HasYtDlp ? "OK" : "missing") +
                               "   |   ffmpeg: " + (App.HasFfmpeg ? "OK" : "missing") +
                               "   |   JS: " + (App.HasJsRuntime ? "OK" : "missing");
            _statusDeps.ForeColor = App.HasYtDlp && App.HasFfmpeg && App.HasJsRuntime
                ? SystemColors.ControlText : Color.Firebrick;

            bool wasLoading = _loading;
            _loading = true;
            try
            {
                // Fill these in, and repair them when the folder has been moved since last run.
                if (App.HasFfmpeg && !DirectoryLooksRight(txtFfmpegLocation.Text))
                    txtFfmpegLocation.Text = Path.GetDirectoryName(App.FfmpegPath);

                if (App.HasJsRuntime && !JsRuntimeLooksRight(txtJsRuntimes.Text))
                    txtJsRuntimes.Text = App.JsRuntimeArg;
            }
            catch { }
            finally { _loading = wasLoading; }

            // Those two feed the command line, and the change was made with _loading set.
            _previewDirty = true;
        }

        private static bool DirectoryLooksRight(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try { return Directory.Exists(path); }
            catch { return false; }
        }

        /// <summary>A stored "deno:C:\..." is worthless once that file has moved away.</summary>
        private static bool JsRuntimeLooksRight(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            int colon = value.IndexOf(':');
            if (colon < 0) return true;                       // a bare runtime name, e.g. "deno"
            var path = value.Substring(colon + 1);
            if (path.Length < 2) return false;
            try { return File.Exists(path); }
            catch { return false; }
        }

        private void ShowAbout()
        {
            MessageBox.Show(this,
                App.Title + "\r\n\r\n" +
                "A plain WinForms front-end for yt-dlp. Every option maps directly to a yt-dlp\r\n" +
                "command-line switch, and the Command tab always shows exactly what will run.\r\n\r\n" +
                "yt-dlp:     " + (App.YtDlpVersion ?? "unknown") + "\r\n" +
                "App folder: " + App.BaseDir + "\r\n" +
                "Settings:   " + App.SettingsFile,
                "About " + App.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowShortcuts()
        {
            MessageBox.Show(this,
                "F5  or  Ctrl+Enter    Download\r\n" +
                "Ctrl+D                Add the URLs above to the queue\r\n" +
                "Ctrl+Shift+S          Simulate\r\n" +
                "Ctrl+F                List formats for the first URL\r\n" +
                "Esc                   Stop\r\n\r\n" +
                "Ctrl+1 / 2 / 3        Log / Queue / Command\r\n" +
                "Ctrl+L                Clear the log\r\n" +
                "F3                    Find the next match in the log\r\n" +
                "Ctrl+Shift+C          Copy the command line\r\n\r\n" +
                "Ctrl+O                Open the download folder\r\n" +
                "Ctrl+S / Ctrl+P       Save / load a profile\r\n" +
                "F2                    Check dependencies\r\n" +
                "F1                    This list",
                "Keyboard shortcuts", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// First thing the user sees in the log: what was found and what to do. The version
        /// probe runs off the UI thread so the window is usable the moment it appears.
        /// </summary>
        private void ShowWelcomeAsync()
        {
            AppendLog(App.Title + " - paste a URL above and press Download.", LineKind.Gui);

            if (!App.HasYtDlp)
            {
                AppendLog("ERROR: yt-dlp.exe was not found next to this program or on PATH.", LineKind.Error);
            }
            AppendLog(App.HasFfmpeg
                ? "ffmpeg  found    (" + App.FfmpegPath + ")"
                : "WARNING: ffmpeg was not found - merging, audio extraction and remuxing will fail.",
                App.HasFfmpeg ? LineKind.Normal : LineKind.Warning);

            AppendLog(App.HasJsRuntime
                ? "JS      " + Path.GetFileNameWithoutExtension(App.DenoPath) + "   (" + App.DenoPath + ")"
                : "WARNING: no JavaScript runtime found - YouTube may return no formats at all.",
                App.HasJsRuntime ? LineKind.Normal : LineKind.Warning);

            AppendLog("Settings file: " + App.SettingsFile, LineKind.Normal);

            if (!App.HasYtDlp) return;

            ToolRun.Begin(App.YtDlpPath, new List<string> { "--version" }, 30000, delegate (ToolRun r)
            {
                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        App.SetVersion(r.StdOut.Trim());
                        AppendLog("yt-dlp  " + (App.YtDlpVersion ?? "?") + "   (" + App.YtDlpPath + ")", LineKind.Normal);
                        SetStatus("Ready.");
                    });
                }
                catch { }
            });
        }

        private void SetStatus(string text) { _statusText.Text = text ?? ""; }
        private void SetStats(string text) { _statusStats.Text = text ?? ""; }

        private void Warn(string text)
        {
            MessageBox.Show(this, text, App.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _uiTimer.Dispose();
                Native.KeepAwake(false);
            }
            base.Dispose(disposing);
        }
    }
}
