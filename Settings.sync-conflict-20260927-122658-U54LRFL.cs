using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace YtDlpGui
{
    /// <summary>
    /// Saves and restores the state of every named control by walking the control tree.
    /// New options are picked up automatically as long as they are given a Name.
    /// </summary>
    internal static class Settings
    {
        public static Dictionary<string, string> Capture(Control root)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Walk(root, delegate (Control c)
            {
                if (string.IsNullOrEmpty(c.Name)) return;

                var chk = c as CheckBox;
                if (chk != null) { map[c.Name] = chk.Checked ? "1" : "0"; return; }

                var rad = c as RadioButton;
                if (rad != null) { map[c.Name] = rad.Checked ? "1" : "0"; return; }

                var num = c as NumericUpDown;
                if (num != null) { map[c.Name] = num.Value.ToString(System.Globalization.CultureInfo.InvariantCulture); return; }

                var cmb = c as ComboBox;
                if (cmb != null) { map[c.Name] = cmb.Text; return; }

                var txt = c as TextBox;
                if (txt != null) { map[c.Name] = txt.Text; return; }
            });
            return map;
        }

        public static void Apply(Control root, Dictionary<string, string> map)
        {
            if (map == null) return;
            Walk(root, delegate (Control c)
            {
                string v;
                if (string.IsNullOrEmpty(c.Name) || !map.TryGetValue(c.Name, out v)) return;

                var chk = c as CheckBox;
                if (chk != null) { chk.Checked = v == "1"; return; }

                var rad = c as RadioButton;
                if (rad != null) { rad.Checked = v == "1"; return; }

                var num = c as NumericUpDown;
                if (num != null)
                {
                    decimal d;
                    if (decimal.TryParse(v, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out d))
                        num.Value = Math.Max(num.Minimum, Math.Min(num.Maximum, d));
                    return;
                }

                var cmb = c as ComboBox;
                if (cmb != null)
                {
                    if (cmb.DropDownStyle == ComboBoxStyle.DropDownList)
                    {
                        int idx = cmb.FindStringExact(v);
                        if (idx >= 0) cmb.SelectedIndex = idx;
                    }
                    else cmb.Text = v;
                    return;
                }

                var txt = c as TextBox;
                if (txt != null) { txt.Text = v; return; }
            });
        }

        private static void Walk(Control c, Action<Control> visit)
        {
            visit(c);
            foreach (Control child in c.Controls) Walk(child, visit);
        }

        public static void Save(string file, Dictionary<string, string> map)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# yt-dlp GUI settings");
            foreach (var kv in map)
                sb.AppendLine(kv.Key + "=" + Escape(kv.Value));

            var dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
        }

        public static Dictionary<string, string> Load(string file)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(file)) return map;

            foreach (var raw in File.ReadAllLines(file, Encoding.UTF8))
            {
                if (raw.Length == 0 || raw[0] == '#') continue;
                int eq = raw.IndexOf('=');
                if (eq <= 0) continue;
                map[raw.Substring(0, eq)] = Unescape(raw.Substring(eq + 1));
            }
            return map;
        }

        private static string Escape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static string Unescape(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    if (n == 'n') sb.Append('\n');
                    else if (n == 'r') sb.Append('\r');
                    else if (n == '\\') sb.Append('\\');
                    else { sb.Append('\\'); sb.Append(n); }
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }
    }
}
