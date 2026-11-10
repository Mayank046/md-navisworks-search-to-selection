// MD Search to Selection - dock pane UI (WinForms; works in every Navisworks 2017-2027)
// Author: MD (Mayank Dholariya)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using SearchToSelection.Core;
using Nw = Autodesk.Navisworks.Api;

namespace MDSearchToSelection
{
    public sealed class SearchToSelectionPane : UserControl
    {
        private bool _busy, _cancelRequested, _cascading;
        private string _listedDocKey;
        private TreeView _tree;
        private Button _refresh, _all, _none, _convert, _cancel;
        private TextBox _outFolder, _suffix, _results;
        private CheckBox _sameFolder, _overwrite, _skipEmpty;
        private Label _status;

        public SearchToSelectionPane()
        {
            Dock = DockStyle.Fill;
            Font = new Font("Segoe UI", 9f);
            Build();
            VisibleChanged += (s, e) => { if (Visible && !_busy) SafeRefresh(); };
        }

        private void Build()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(6) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // header
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // list buttons
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65f));      // tree
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // options
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // convert / cancel
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // status
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35f));      // results
            Controls.Add(layout);

            layout.Controls.Add(new Label { Text = Names.Product + " v" + Names.Version + "   " + Names.Author, AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Margin = new Padding(3, 3, 3, 6) });

            var top = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
            _refresh = Btn("Refresh", (s, e) => SafeRefresh());
            _all = Btn("Select all", (s, e) => SetAll(true));
            _none = Btn("Select none", (s, e) => SetAll(false));
            top.Controls.AddRange(new Control[] { _refresh, _all, _none });
            layout.Controls.Add(top);

            _tree = new TreeView { Dock = DockStyle.Fill, CheckBoxes = true, HideSelection = false, ShowNodeToolTips = true };
            _tree.AfterCheck += OnAfterCheck;
            layout.Controls.Add(_tree);

            var opt = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2 };
            opt.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            opt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _outFolder = new TextBox { Text = Names.DefaultOutputFolder, Dock = DockStyle.Fill };
            _suffix = new TextBox { Text = "", Dock = DockStyle.Fill };
            _sameFolder = new CheckBox { Text = "Write into the same folder as each search set", AutoSize = true, Checked = false };
            _sameFolder.CheckedChanged += (s, e) => _outFolder.Enabled = !_sameFolder.Checked;
            _overwrite = new CheckBox { Text = "Overwrite selection sets with the same name", AutoSize = true, Checked = true };
            _skipEmpty = new CheckBox { Text = "Skip search sets that find nothing", AutoSize = true, Checked = true };
            opt.Controls.Add(new Label { Text = "Output folder:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            opt.Controls.Add(_outFolder, 1, 0);
            opt.Controls.Add(_sameFolder, 0, 1); opt.SetColumnSpan(_sameFolder, 2);
            opt.Controls.Add(new Label { Text = "Name suffix:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
            opt.Controls.Add(_suffix, 1, 2);
            opt.Controls.Add(_overwrite, 0, 3); opt.SetColumnSpan(_overwrite, 2);
            opt.Controls.Add(_skipEmpty, 0, 4); opt.SetColumnSpan(_skipEmpty, 2);
            var tip = new ToolTip();
            tip.SetToolTip(_outFolder, "Top-level folder in the Sets window. The source folder structure is kept below it.");
            tip.SetToolTip(_suffix, "Added to every new name. If a search set already has the name, \" (Sel)\" is added so both can stay.");
            tip.SetToolTip(_overwrite, "Replace an older selection set with the same name. Search sets are never changed or deleted.");
            layout.Controls.Add(opt);

            var run = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            _convert = Btn("Convert checked", (s, e) => Convert());
            _convert.Font = new Font(Font, FontStyle.Bold);
            _cancel = Btn("Cancel", (s, e) => { _cancelRequested = true; SetStatus("Cancelling after the current set..."); });
            _cancel.Enabled = false;
            run.Controls.AddRange(new Control[] { _convert, _cancel });
            layout.Controls.Add(run);

            _status = new Label { AutoSize = true, MaximumSize = new Size(2000, 0), Text = "Press Refresh to list the search sets.", Dock = DockStyle.Fill, Margin = new Padding(3, 4, 3, 4) };
            layout.Controls.Add(_status);
            layout.Resize += (s, e) => { try { _status.MaximumSize = new Size(Math.Max(100, layout.ClientSize.Width - 20), 0); } catch { } };

            _results = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 8.5f) };
            layout.Controls.Add(_results);
        }

        private static Button Btn(string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(4, 1, 4, 1) };
            b.Click += click;
            return b;
        }

        private void SetStatus(string s) { _status.Text = s; _status.Refresh(); }

        private static string DocKey(Nw.Document doc)
        {
            try { if (!string.IsNullOrEmpty(doc.FileName)) return doc.FileName; } catch { }
            try { if (!string.IsNullOrEmpty(doc.CurrentFileName)) return doc.CurrentFileName; } catch { }
            try { return doc.Title ?? ""; } catch { return ""; }
        }

        // ---------------- list ----------------

        private void SafeRefresh()
        {
            if (_busy) return;
            try { RefreshList(); }
            catch (Exception ex) { Diagnostics.Log("Refresh: " + ex); SetStatus("Refresh failed: " + ex.Message + "  Log: " + Diagnostics.LogPath); }
        }

        private void RefreshList()
        {
            var keep = new HashSet<string>(CheckedNodes().Select(n => n.Ref.Key));
            var expanded = new HashSet<string>();
            foreach (TreeNode t in AllTreeNodes()) if (t.IsExpanded && t.Tag is SetNode sn) expanded.Add(sn.Ref.Key);
            bool first = _tree.Nodes.Count == 0;

            _tree.BeginUpdate();
            try
            {
                _tree.Nodes.Clear();
                string why;
                Nw.Document doc = Bridge.ActiveDocument(out why);
                if (doc == null) { _listedDocKey = null; SetStatus(why); return; }
                int errors = 0;
                SetNode root = SetTree.List(new NwSetHost(doc), (p, m) => { errors++; Diagnostics.Log("List '" + p + "': " + m); });
                _cascading = true;
                try { foreach (var c in root.Children) _tree.Nodes.Add(ToTree(c, keep, expanded, first)); }
                finally { _cascading = false; }
                _listedDocKey = DocKey(doc);
                int n = root.SearchSetCount;
                SetStatus(n == 0
                    ? "No search sets in this model. Save one in the Sets window (Save Search), then press Refresh."
                    : n.ToString("N0", CultureInfo.InvariantCulture) + " search set" + (n == 1 ? "" : "s") + " found. Tick the ones to convert, then press Convert checked." +
                      (errors > 0 ? "  " + errors + " item(s) could not be read (see log)." : ""));
            }
            finally { _tree.EndUpdate(); }
        }

        private TreeNode ToTree(SetNode n, HashSet<string> keep, HashSet<string> expanded, bool expandAll)
        {
            bool folder = n.Kind == SetKind.Folder;
            var t = new TreeNode(folder ? n.Name + "   (" + n.SearchSetCount.ToString(CultureInfo.InvariantCulture) + ")" : n.Name)
            {
                Tag = n,
                ToolTipText = folder ? "Folder: ticking it ticks every search set inside" : "Search set: " + n.Ref.DisplayPath,
                Checked = !folder && keep.Contains(n.Ref.Key)
            };
            foreach (var c in n.Children) t.Nodes.Add(ToTree(c, keep, expanded, expandAll));
            if (folder && (expandAll || expanded.Contains(n.Ref.Key))) t.Expand();
            return t;
        }

        private IEnumerable<TreeNode> AllTreeNodes()
        {
            var stack = new Stack<TreeNode>();
            for (int i = _tree.Nodes.Count - 1; i >= 0; i--) stack.Push(_tree.Nodes[i]);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                yield return t;
                for (int i = t.Nodes.Count - 1; i >= 0; i--) stack.Push(t.Nodes[i]);
            }
        }

        private List<SetNode> CheckedNodes()
        {
            var l = new List<SetNode>();
            var seen = new HashSet<string>();
            foreach (TreeNode t in AllTreeNodes())
                if (t.Checked && t.Tag is SetNode n && n.Kind == SetKind.SearchSet && seen.Add(n.Ref.Key)) l.Add(n);
            return l;
        }

        private void OnAfterCheck(object sender, TreeViewEventArgs e)
        {
            if (_cascading) return;
            _cascading = true;
            try
            {
                var stack = new Stack<TreeNode>();
                foreach (TreeNode c in e.Node.Nodes) stack.Push(c);
                while (stack.Count > 0)
                {
                    var t = stack.Pop();
                    t.Checked = e.Node.Checked;
                    foreach (TreeNode c in t.Nodes) stack.Push(c);
                }
            }
            finally { _cascading = false; }
            if (!_busy) SetStatus(CheckedNodes().Count.ToString("N0", CultureInfo.InvariantCulture) + " search set(s) ticked.");
        }

        private void SetAll(bool on)
        {
            _cascading = true;
            try { foreach (TreeNode t in AllTreeNodes()) t.Checked = on; }
            finally { _cascading = false; }
            SetStatus(CheckedNodes().Count.ToString("N0", CultureInfo.InvariantCulture) + " search set(s) ticked.");
        }

        // ---------------- convert ----------------

        private void Convert()
        {
            if (_busy) return;
            var picked = CheckedNodes();
            if (picked.Count == 0) { SetStatus("Tick at least one search set (or a folder), then press Convert checked."); return; }
            string why;
            Nw.Document doc = Bridge.ActiveDocument(out why);
            if (doc == null) { SetStatus(why); return; }
            if (_listedDocKey != null && !string.Equals(_listedDocKey, DocKey(doc), StringComparison.OrdinalIgnoreCase))
            { SetStatus("A different model is open now. Press Refresh, tick the sets again, then Convert."); return; }
            if (Bridge.ModelCount(doc) == 0) { SetStatus("No model is loaded, so the searches cannot find anything. Open or append a model first."); return; }

            var options = new ConvertOptions
            {
                OutputFolder = _outFolder.Text,
                IntoSourceFolder = _sameFolder.Checked,
                Suffix = _suffix.Text ?? "",
                Overwrite = _overwrite.Checked,
                SkipEmpty = _skipEmpty.Checked
            };

            _busy = true; _cancelRequested = false;
            SetControls(false);
            Cursor = Cursors.WaitCursor;
            _results.Text = "";
            var sw = Stopwatch.StartNew();
            long lastUi = -1000;
            ConvertResult result = null;
            try
            {
                Diagnostics.Log("Convert " + picked.Count + " set(s); out='" + options.EffectiveOutputFolder + "' same=" + options.IntoSourceFolder +
                                " suffix='" + options.Suffix + "' overwrite=" + options.Overwrite + " skipEmpty=" + options.SkipEmpty);
                result = Converter.Run(new NwSetHost(doc), picked.Select(p => p.Ref).ToList(), options,
                    (i, total, path, items) =>
                    {
                        if (sw.ElapsedMilliseconds - lastUi < 250) return;
                        lastUi = sw.ElapsedMilliseconds;
                        SetStatus("Converting " + (i + 1).ToString("N0", CultureInfo.InvariantCulture) + " of " + total.ToString("N0", CultureInfo.InvariantCulture) +
                                  (path.Length > 0 ? ": " + path : "") + "   (" + items.ToString("N0", CultureInfo.InvariantCulture) + " items so far, " +
                                  sw.Elapsed.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s)");
                        Application.DoEvents();
                    },
                    () => _cancelRequested);
                foreach (var o in result.Outcomes) if (o.Status != OutcomeStatus.Converted) Diagnostics.Log(o.ToString());
                string summary = result.Summary();
                Diagnostics.Log(summary);
                _results.Text = string.Join("\r\n", result.Outcomes.Select(o => o.ToString()));
                SetStatus(summary);
            }
            catch (Exception ex)
            {
                Diagnostics.Log("Convert: " + ex);
                SetStatus("Convert stopped: " + ex.Message + "  Log: " + Diagnostics.LogPath);
            }
            finally
            {
                _busy = false; Cursor = Cursors.Default;
                SetControls(true);
            }
            if (result != null && result.Converted > 0 && !options.IntoSourceFolder)
                _results.AppendText("\r\nNew selection sets are in the Sets window under '" + options.EffectiveOutputFolder + "'.");
        }

        private void SetControls(bool idle)
        {
            _convert.Enabled = idle; _refresh.Enabled = idle; _all.Enabled = idle; _none.Enabled = idle;
            _tree.Enabled = idle; _outFolder.Enabled = idle && !_sameFolder.Checked; _sameFolder.Enabled = idle;
            _suffix.Enabled = idle; _overwrite.Enabled = idle; _skipEmpty.Enabled = idle;
            _cancel.Enabled = !idle;
        }
    }
}
