// MD Search to Selection - unit tests on a fake Sets window (no Navisworks)
// Author: MD (Mayank Dholariya)
using System;
using System.Collections.Generic;
using System.Linq;
using SearchToSelection.Core;
using Xunit;

namespace SearchToSelection.Tests
{
    /// <summary>A fake set node. Search sets hold a predicate over the fake model; selection sets hold explicit items.</summary>
    public sealed class F
    {
        public string Name;
        public SetKind Kind;
        public Func<string, bool> Search;           // only for search sets
        public List<string> Items = new List<string>();  // only for selection sets
        public List<F> Kids = new List<F>();
        public bool Broken;                          // ResolveItems throws
        public F Copy()
        {
            var c = new F { Name = Name, Kind = Kind, Search = Search, Items = new List<string>(Items), Broken = Broken };
            c.Kids = Kids.Select(k => k.Copy()).ToList();
            return c;
        }
    }

    /// <summary>
    /// Fake host that behaves like Navisworks DocumentSelectionSets: inserts store a COPY (P10), and Root() is a fresh view.
    /// Node objects handed out before an insert are "stale" (like Navisworks) - the host refuses to use them.
    /// </summary>
    public sealed class FakeHost : ISetHost<F, List<string>>
    {
        public F RootNode = new F { Name = "", Kind = SetKind.Folder };
        public List<string> Model = new List<string>();
        public int Generation;          // bumps on every change
        readonly Dictionary<F, int> _issued = new Dictionary<F, int>();
        public int Searches, Inserts, Removes;
        public Action OnResolve;       // test hook (e.g. click Cancel)

        F Issue(F f) { _issued[f] = Generation; return f; }
        void CheckFresh(F f) { if (!_issued.TryGetValue(f, out int g) || g != Generation) throw new InvalidOperationException("stale object (P10)"); }

        public F Root() { return Issue(RootNode); }
        public IList<F> Children(F folder) { CheckFresh(folder); return folder.Kids.Select(Issue).ToList(); }
        public string Name(F n) { return n.Name; }
        public SetKind Kind(F n) { return n.Kind; }
        public List<string> ResolveItems(F s)
        {
            CheckFresh(s); Searches++; OnResolve?.Invoke();
            if (s.Broken) throw new InvalidOperationException("bad search condition");
            return Model.Where(s.Search).ToList();
        }
        public int Count(List<string> items) { return items.Count; }
        public void InsertFolder(F parent, string name)
        {
            CheckFresh(parent);
            parent.Kids.Add(new F { Name = name, Kind = SetKind.Folder }.Copy()); Generation++; Inserts++;
        }
        public void InsertSelectionSet(F parent, string name, List<string> items)
        {
            CheckFresh(parent);
            parent.Kids.Add(new F { Name = name, Kind = SetKind.SelectionSet, Items = new List<string>(items) }.Copy()); Generation++; Inserts++;
        }
        public bool Remove(F parent, F child)
        {
            CheckFresh(parent); CheckFresh(child);
            bool ok = parent.Kids.Remove(child); Generation++; Removes++; return ok;
        }

        // ---- builders ----
        public static F Folder(string n, params F[] kids) { return new F { Name = n, Kind = SetKind.Folder, Kids = kids.ToList() }; }
        public static F Search(string n, string contains) { return new F { Name = n, Kind = SetKind.SearchSet, Search = s => s.Contains(contains) }; }
        public static F Sel(string n, params string[] items) { return new F { Name = n, Kind = SetKind.SelectionSet, Items = items.ToList() }; }
        public F At(params string[] path)
        {
            F cur = RootNode;
            foreach (var p in path) { cur = cur.Kids.FirstOrDefault(k => k.Name == p); if (cur == null) return null; }
            return cur;
        }
    }

    public class CoreTests
    {
        static FakeHost Sample()
        {
            var h = new FakeHost();
            h.Model.AddRange(new[] { "PIPE-100-A", "PIPE-200-A", "PIPE-300-B", "STEEL-1", "STEEL-2", "VALVE-1" });
            h.RootNode.Kids.Add(FakeHost.Search("All pipes", "PIPE"));
            h.RootNode.Kids.Add(FakeHost.Sel("My picks", "STEEL-1"));
            h.RootNode.Kids.Add(FakeHost.Folder("Area A",
                FakeHost.Search("A pipes", "-A"),
                FakeHost.Folder("Steel", FakeHost.Search("Steel", "STEEL")),
                FakeHost.Sel("Hand set", "VALVE-1")));
            h.RootNode.Kids.Add(FakeHost.Folder("Only selections", FakeHost.Sel("x", "VALVE-1")));
            return h;
        }

        static List<SetRef> All(FakeHost h) { return SetTree.List(h).SearchSets().Select(s => s.Ref).ToList(); }

        [Fact]
        public void List_shows_only_search_sets_and_their_folders()
        {
            var h = Sample();
            var root = SetTree.List(h);
            var paths = root.SearchSets().Select(s => s.Ref.DisplayPath).ToList();
            Assert.Equal(new[] { "All pipes", "Area A / A pipes", "Area A / Steel / Steel" }, paths);
            Assert.DoesNotContain(root.Children, c => c.Name == "My picks");          // selection set skipped
            Assert.DoesNotContain(root.Children, c => c.Name == "Only selections");   // folder with no search sets skipped
            Assert.Equal(3, root.SearchSetCount);
            Assert.Equal(2, root.Children.Single(c => c.Name == "Area A").SearchSetCount);
        }

        [Fact]
        public void Converts_search_set_into_explicit_selection_set_in_Converted_folder_keeping_structure()
        {
            var h = Sample();
            var r = Converter.Run(h, All(h), new ConvertOptions());
            Assert.Equal(3, r.Converted);
            Assert.Equal(0, r.Failed);
            var made = h.At("Converted", "All pipes");
            Assert.NotNull(made);
            Assert.Equal(SetKind.SelectionSet, made.Kind);
            Assert.Null(made.Search);                                  // no search attached
            Assert.Equal(new[] { "PIPE-100-A", "PIPE-200-A", "PIPE-300-B" }, made.Items);
            Assert.Equal(new[] { "PIPE-100-A", "PIPE-200-A" }, h.At("Converted", "Area A", "A pipes").Items);
            Assert.Equal(new[] { "STEEL-1", "STEEL-2" }, h.At("Converted", "Area A", "Steel", "Steel").Items);
            Assert.Equal(7, r.Items);
            Assert.Equal(3, h.Searches);                               // each search executed exactly once
        }

        [Fact]
        public void Selection_set_is_a_snapshot_not_live()
        {
            var h = Sample();
            Converter.Run(h, All(h).Take(1).ToList(), new ConvertOptions());
            h.Model.Add("PIPE-999-Z");
            Assert.Equal(3, h.At("Converted", "All pipes").Items.Count);
        }

        [Fact]
        public void Originals_are_never_removed_or_changed()
        {
            var h = Sample();
            Converter.Run(h, All(h), new ConvertOptions { IntoSourceFolder = true });
            Converter.Run(h, All(h), new ConvertOptions { IntoSourceFolder = true });   // second run: overwrite
            Assert.Equal(SetKind.SearchSet, h.At("All pipes").Kind);
            Assert.NotNull(h.At("All pipes").Search);
            Assert.Equal(SetKind.SearchSet, h.At("Area A", "A pipes").Kind);
            Assert.Equal(SetKind.SearchSet, h.At("Area A", "Steel", "Steel").Kind);
            Assert.Equal(new[] { "VALVE-1" }, h.At("Area A", "Hand set").Items);
            Assert.Equal(new[] { "STEEL-1" }, h.At("My picks").Items);
        }

        [Fact]
        public void Same_folder_name_collision_gets_Sel_suffix()
        {
            var h = Sample();
            var r = Converter.Run(h, All(h), new ConvertOptions { IntoSourceFolder = true });
            Assert.Equal(3, r.Converted);
            Assert.Equal(SetKind.SelectionSet, h.At("All pipes (Sel)").Kind);
            Assert.Equal(SetKind.SelectionSet, h.At("Area A", "A pipes (Sel)").Kind);
            Assert.Equal(SetKind.SelectionSet, h.At("Area A", "Steel", "Steel (Sel)").Kind);
            Assert.Null(h.At("Converted"));
            Assert.Contains("renamed", r.Outcomes[0].Reason);
        }

        [Fact]
        public void User_suffix_avoids_collision_without_Sel()
        {
            var h = Sample();
            Converter.Run(h, All(h).Take(1).ToList(), new ConvertOptions { IntoSourceFolder = true, Suffix = " - fixed" });
            Assert.NotNull(h.At("All pipes - fixed"));
            Assert.Null(h.At("All pipes (Sel)"));
        }

        [Fact]
        public void Overwrite_on_replaces_existing_selection_set()
        {
            var h = Sample();
            h.RootNode.Kids.Add(FakeHost.Folder("Converted", FakeHost.Sel("All pipes", "OLD")));
            var r = Converter.Run(h, All(h).Take(1).ToList(), new ConvertOptions { Overwrite = true });
            Assert.Equal(1, r.Converted);
            var sets = h.At("Converted").Kids.Where(k => k.Name == "All pipes").ToList();
            Assert.Single(sets);
            Assert.Equal(3, sets[0].Items.Count);
            Assert.Contains("replaced", r.Outcomes[0].Reason);
        }

        [Fact]
        public void Overwrite_off_skips_existing_selection_set()
        {
            var h = Sample();
            h.RootNode.Kids.Add(FakeHost.Folder("Converted", FakeHost.Sel("All pipes", "OLD")));
            var r = Converter.Run(h, All(h).Take(1).ToList(), new ConvertOptions { Overwrite = false });
            Assert.Equal(0, r.Converted);
            Assert.Equal(1, r.Skipped);
            Assert.Equal(new[] { "OLD" }, h.At("Converted", "All pipes").Items);
            Assert.Contains("overwrite is off", r.Summary());
        }

        [Fact]
        public void Creates_output_folder_once_and_reuses_existing()
        {
            var h = Sample();
            Converter.Run(h, All(h), new ConvertOptions { OutputFolder = "  " });   // blank -> "Converted"
            Converter.Run(h, All(h), new ConvertOptions());
            Assert.Single(h.RootNode.Kids, k => k.Name == "Converted");
            Assert.Single(h.At("Converted").Kids, k => k.Name == "Area A");
            Assert.Single(h.At("Converted").Kids, k => k.Name == "All pipes");
        }

        [Fact]
        public void EnsurePath_creates_nested_folders_and_refinds_from_root()
        {
            var h = new FakeHost();
            Assert.Null(Converter.EnsurePath(h, new[] { "Out", "A", "B" }));
            Assert.NotNull(h.At("Out", "A", "B"));
            int inserts = h.Inserts;
            Assert.Null(Converter.EnsurePath(h, new[] { "Out", "A", "B" }));
            Assert.Equal(inserts, h.Inserts);                   // nothing created twice
        }

        [Fact]
        public void Empty_result_is_skipped_by_default_or_created_when_asked()
        {
            var h = Sample();
            h.RootNode.Kids.Add(FakeHost.Search("Nothing", "ZZZ"));
            var refs = All(h).Where(r => r.Name == "Nothing").ToList();
            var r1 = Converter.Run(h, refs, new ConvertOptions());
            Assert.Equal(1, r1.Skipped);
            Assert.Contains("0 items", r1.Outcomes[0].Reason);
            Assert.Null(h.At("Converted", "Nothing"));
            var r2 = Converter.Run(h, refs, new ConvertOptions { SkipEmpty = false });
            Assert.Equal(1, r2.Converted);
            Assert.Empty(h.At("Converted", "Nothing").Items);
        }

        [Fact]
        public void Cancel_stops_cleanly_and_marks_rest_not_run()
        {
            var h = Sample();
            bool cancel = false;
            h.OnResolve = () => cancel = true;     // user clicks Cancel while the first search runs
            var r = Converter.Run(h, All(h), new ConvertOptions(), null, () => cancel);
            Assert.True(r.Cancelled);
            Assert.Equal(1, r.Converted);
            Assert.Equal(2, r.NotRun);
            Assert.StartsWith("CANCELLED - partial. Converted 1 of 3 search sets", r.Summary());
            Assert.Equal(1, h.Searches);
        }

        [Fact]
        public void Failure_is_recorded_and_run_continues()
        {
            var h = Sample();
            h.At("Area A", "A pipes").Broken = true;
            var refs = All(h);
            refs.Insert(1, new SetRef(new[] { new RefStep("Gone", SetKind.SearchSet, 0) }));
            var r = Converter.Run(h, refs, new ConvertOptions());
            Assert.Equal(2, r.Converted);
            Assert.Equal(2, r.Failed);
            string s = r.Summary();
            Assert.Contains("2 failed", s);
            Assert.Contains("bad search condition", s);
            Assert.Contains("not found", s);
        }

        [Fact]
        public void Selection_set_in_source_list_is_skipped_not_converted()
        {
            var h = Sample();
            var r = Converter.Run(h, new List<SetRef> { new SetRef(new[] { new RefStep("My picks", SetKind.SelectionSet, 0) }) }, new ConvertOptions());
            Assert.Equal(1, r.Skipped);
            Assert.Equal(0, h.Searches);
        }

        [Fact]
        public void Duplicate_names_in_one_run_do_not_overwrite_each_other()
        {
            var h = new FakeHost();
            h.Model.AddRange(new[] { "A1", "B1" });
            h.RootNode.Kids.Add(FakeHost.Search("Dup", "A"));
            h.RootNode.Kids.Add(FakeHost.Search("Dup", "B"));
            var refs = All(h);
            Assert.Equal(1, refs[1].Steps[0].Occurrence);
            var r = Converter.Run(h, refs, new ConvertOptions());
            Assert.Equal(2, r.Converted);
            Assert.Equal(new[] { "A1" }, h.At("Converted", "Dup").Items);
            Assert.Equal(new[] { "B1" }, h.At("Converted", "Dup (2)").Items);
        }

        [Fact]
        public void Second_same_folder_run_replaces_its_own_Sel_set_and_keeps_counts_right()
        {
            var h = Sample();
            Converter.Run(h, All(h), new ConvertOptions { IntoSourceFolder = true });
            h.Model.Add("PIPE-400-A");
            var r = Converter.Run(h, All(h), new ConvertOptions { IntoSourceFolder = true });
            Assert.Equal(3, r.Converted);
            Assert.Single(h.RootNode.Kids, k => k.Name == "All pipes (Sel)");
            Assert.Equal(4, h.At("All pipes (Sel)").Items.Count);
        }

        [Fact]
        public void Summary_has_counts_and_seconds()
        {
            var h = Sample();
            var r = Converter.Run(h, All(h), new ConvertOptions());
            Assert.Matches(@"^Converted 3 of 3 search sets \(7 items\) in \d+\.\d s\.$", r.Summary());
        }

        [Fact]
        public void Progress_reports_every_set()
        {
            var h = Sample();
            var seen = new List<string>();
            Converter.Run(h, All(h), new ConvertOptions(), (i, n, path, items) => { if (path.Length > 0) seen.Add(path); });
            Assert.Equal(3, seen.Count);
        }
    }
}
