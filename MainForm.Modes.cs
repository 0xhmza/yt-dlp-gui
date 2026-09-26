using System.Collections.Generic;
using System.Windows.Forms;

namespace YtDlpGui
{
    internal partial class MainForm
    {
        private TabControl _basicTabs;
        private ComboBox _uiMode;
        private Section[] _basicGroups;
        private Control[][] _basicOptions;
        private Control[] _advancedActions;
        private bool _advancedMode = true;

        private Control BuildModeBar()
        {
            _basicTabs = new BufferedTabControl { Dock = DockStyle.Fill };
            var host = Ui.Tab(_basicTabs, "Quick download");
            _basicGroups = new[]
            {
                Ui.Group(host, "Save to"),
                Ui.Group(host, "Video and audio"),
                Ui.Group(host, "Subtitles and playlists")
            };
            _basicOptions = new[]
            {
                new Control[] { txtOutDir, cmbTemplate },
                new Control[] { radFmtBest, cmbMaxHeight, cmbMergeContainer, txtCustomFormat, cmbAudioFormat },
                new Control[] { chkWriteSubs, txtSubLangs, cmbPlaylistMode, txtPlaylistItems }
            };
            Ui.FinishTabs(_basicTabs);

            _uiMode = Ui.Cmb("", new[] { "Basic", "Advanced" });
            _uiMode.AccessibleName = "Interface mode";
            _uiMode.SelectedIndexChanged += delegate { SetAdvancedMode(_uiMode.SelectedIndex == 1); };
            Ui.Tips.SetToolTip(_uiMode, "Basic shows everyday options. Advanced shows every option. Your settings are kept in both views.");

            var bar = new WrapRow { Dock = DockStyle.Top, AutoHeight = true,
                Padding = new Padding(10, 6, 10, 6), HSpacing = 10 };
            bar.Controls.Add(Ui.Note("Options"));
            bar.Controls.Add(_uiMode);
            bar.Controls.Add(Ui.Btn("Expand all", delegate { SetGroupsCollapsed(false); }));
            bar.Controls.Add(Ui.Btn("Collapse all", delegate { SetGroupsCollapsed(true); }));
            bar.Controls.Add(Ui.Note("Your settings apply in both views."));
            return bar;
        }

        private void SetAdvancedMode(bool advanced)
        {
            if (_advancedMode == advanced) return;
            _advancedMode = advanced;
            _split.Panel1.SuspendLayout();
            try
            {
                // Keep both trees attached for settings capture, even when one is hidden.
                _tabs.Visible = false;
                _basicTabs.Visible = false;
                for (int i = 0; i < _basicGroups.Length; i++)
                {
                    if (advanced) _basicGroups[i].ReturnBorrowedRows();
                    else foreach (var option in _basicOptions[i]) _basicGroups[i].BorrowRow(option);
                }
                // Quality caps and audio formats cover everyday use; these refinements
                // stay available on the full Format tab.
                cmbMaxFps.Visible = advanced;
                cmbVideoCodec.Visible = advanced;
                cmbAudioQuality.Visible = advanced;
                foreach (var control in _advancedActions) control.Visible = advanced;
                _tabs.Visible = advanced;
                _basicTabs.Visible = !advanced;
                _uiMode.SelectedIndex = advanced ? 1 : 0;
            }
            finally { _split.Panel1.ResumeLayout(true); }
        }

        private static IEnumerable<Section> OptionGroups(Control root)
        {
            foreach (Control child in root.Controls)
            {
                var section = child as Section;
                if (section != null) yield return section;
                else foreach (var nested in OptionGroups(child)) yield return nested;
            }
        }

        private void SetGroupsCollapsed(bool collapsed)
        {
            var tabs = _advancedMode ? _tabs : _basicTabs;
            tabs.SuspendLayout();
            try
            {
                foreach (var section in OptionGroups(tabs)) section.Collapsed = collapsed;
            }
            finally { tabs.ResumeLayout(true); }
        }
    }
}
