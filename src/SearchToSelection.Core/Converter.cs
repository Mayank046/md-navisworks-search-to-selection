// MD Search to Selection - the conversion (no Navisworks; tested on a fake host)
// Author: MD (Mayank Dholariya)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace SearchToSelection.Core
{
    public sealed class ConvertOptions
    {
        /// <summary>Top-level folder for the new selection sets. The source folder structure is kept below it.</summary>
        public string OutputFolder { get; set; } = Names.DefaultOutputFolder;
        /// <summary>Write each selection set into the same folder as its search set (off by default).</summary>
        public bool IntoSourceFolder { get; set; }
        /// <summary>Added to every new name (default empty).</summary>
        public string Suffix { get; set; } = "";
        /// <summary>Replace a selection set that already has the target name (on by default). Search sets are never replaced.</summary>
        public bool Overwrite { get; set; } = true;
        /// <summary>Do not create a selection set when the search finds nothing (on by default).</summary>
        public bool SkipEmpty { get; set; } = true;

        public string EffectiveOutputFolder
        {
            get { var s = (OutputFolder ?? "").Trim(); return s.Length == 0 ? Names.DefaultOutputFolder : s; }
        }
    }

    public enum OutcomeStatus { Converted, Skipped, Failed, NotRun }

    public sealed class Outcome
    {
        public SetRef Source { get; set; }
        public OutcomeStatus Status { get; set; }
        public string TargetPath { get; set; } = "";
        public int Items { get; set; }
        public string Reason { get; set; } = "";

        public override string ToString()
        {
            switch (Status)
            {
                case OutcomeStatus.Converted:
                    return "OK      " + Source.DisplayPath + "  ->  " + TargetPath + "  (" + Items.ToString("N0", CultureInfo.InvariantCulture) + " items)" +
                           (Reason.Length > 0 ? "  " + Reason : "");
                case OutcomeStatus.Skipped: return "SKIPPED " + Source.DisplayPath + "  - " + Reason;
                case OutcomeStatus.Failed: return "FAILED  " + Source.DisplayPath + "  - " + Reason;
                default: return "NOT RUN " + Source.DisplayPath + "  - " + Reason;
            }
        }
    }

    public sealed class ConvertResult
    {
        public List<Outcome> Outcomes { get; } = new List<Outcome>();
        public bool Cancelled { get; set; }
        public double Seconds { get; set; }
        public int Requested { get { return Outcomes.Count; } }
        public int Converted { get { return Outcomes.Count(o => o.Status == OutcomeStatus.Converted); } }
        public int Skipped { get { return Outcomes.Count(o => o.Status == OutcomeStatus.Skipped); } }
        public int Failed { get { return Outcomes.Count(o => o.Status == OutcomeStatus.Failed); } }
        public int NotRun { get { return Outcomes.Count(o => o.Status == OutcomeStatus.NotRun); } }
        public long Items { get { return Outcomes.Where(o => o.Status == OutcomeStatus.Converted).Sum(o => (long)o.Items); } }

        /// <summary>e.g. "Converted 12 of 40 search sets (8,431 items) in 14.2 s. 1 failed: ..."</summary>
        public string Summary()
        {
            var c = CultureInfo.InvariantCulture;
            string s = (Cancelled ? "CANCELLED - partial. " : "") +
                       "Converted " + Converted.ToString("N0", c) + " of " + Requested.ToString("N0", c) + " search set" + (Requested == 1 ? "" : "s") +
                       " (" + Items.ToString("N0", c) + " items) in " + Seconds.ToString("0.0", c) + " s.";
            if (Skipped > 0) s += " " + Skipped.ToString("N0", c) + " skipped" + FirstReasons(OutcomeStatus.Skipped) + ".";
            if (Failed > 0) s += " " + Failed.ToString("N0", c) + " failed" + FirstReasons(OutcomeStatus.Failed) + ".";
            if (NotRun > 0) s += " " + NotRun.ToString("N0", c) + " not run.";
            return s;
        }

        string FirstReasons(OutcomeStatus st)
        {
            var groups = Outcomes.Where(o => o.Status == st).GroupBy(o => o.Reason).OrderByDescending(g => g.Count()).Take(2)
                .Select(g => g.Key + (g.Count() > 1 ? " x" + g.Count().ToString(CultureInfo.InvariantCulture) : "")).ToList();
            return groups.Count == 0 ? "" : " (" + string.Join("; ", groups) + ")";
        }
    }

    public static class Converter
    {
        static bool Same(string a, string b) { return string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase); }

        /// <summary>
        /// Converts each search set in <paramref name="sources"/> into an explicit selection set.
        /// Never removes or changes a search set or a folder. Each set is guarded: a failure is recorded and the run continues.
        /// progress(doneSoFar, total, currentPath, itemsSoFar) is called before each set; cancel() is checked before each set.
        /// </summary>
        public static ConvertResult Run<T, TItems>(ISetHost<T, TItems> host, IList<SetRef> sources, ConvertOptions options,
            Action<int, int, string, long> progress = null, Func<bool> cancel = null)
        {
            options = options ?? new ConvertOptions();
            var result = new ConvertResult();
            var sw = Stopwatch.StartNew();
            var createdThisRun = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = (sources ?? new List<SetRef>()).Where(s => s != null).ToList();
            long items = 0;

            for (int i = 0; i < list.Count; i++)
            {
                SetRef src = list[i];
                if (cancel != null && SafeBool(cancel))
                {
                    result.Cancelled = true;
                    for (int j = i; j < list.Count; j++)
                        result.Outcomes.Add(new Outcome { Source = list[j], Status = OutcomeStatus.NotRun, Reason = "cancelled" });
                    break;
                }
                try { progress?.Invoke(i, list.Count, src.DisplayPath, items); } catch { }
                Outcome o = ConvertOne(host, src, options, createdThisRun);
                result.Outcomes.Add(o);
                if (o.Status == OutcomeStatus.Converted) items += o.Items;
            }
            try { progress?.Invoke(result.Outcomes.Count(o => o.Status != OutcomeStatus.NotRun), list.Count, "", items); } catch { }
            result.Seconds = sw.Elapsed.TotalSeconds;
            return result;
        }

        static bool SafeBool(Func<bool> f) { try { return f(); } catch { return false; } }

        static Outcome ConvertOne<T, TItems>(ISetHost<T, TItems> host, SetRef src, ConvertOptions options, HashSet<string> createdThisRun)
        {
            var o = new Outcome { Source = src };
            try
            {
                // 1. find the search set again from the root (P10) and run its search
                T set;
                if (!SetTree.TryFind(host, src, out set))
                    return Fail(o, "search set not found (renamed or deleted? press Refresh)");
                if (host.Kind(set) != SetKind.SearchSet)
                    return Skip(o, "not a search set (already a selection set)");
                TItems found;
                try { found = host.ResolveItems(set); }
                catch (Exception ex) { return Fail(o, "search failed: " + ex.Message); }
                if (found == null) return Fail(o, "search returned nothing (null)");
                int n = host.Count(found);
                o.Items = n;
                if (n == 0 && options.SkipEmpty) return Skip(o, "search found 0 items");

                // 2. target folder (created if missing, re-found from root after every insert)
                Func<T> locate;
                IList<string> targetNames;
                if (options.IntoSourceFolder)
                {
                    SetRef parentRef = src.Parent;
                    targetNames = parentRef.Steps.Select(s => s.Name).ToList();
                    locate = () => { T p; return SetTree.TryFind(host, parentRef, out p) ? p : default(T); };
                }
                else
                {
                    targetNames = new List<string> { options.EffectiveOutputFolder };
                    foreach (var f in src.FolderNames) targetNames.Add(f);
                    string err = EnsurePath(host, targetNames);
                    if (err != null) return Fail(o, err);
                    var names = targetNames;
                    locate = () => WalkPath(host, names);
                }
                T target = locate();
                if (target == null) return Fail(o, "target folder not found");
                string folderKey = (options.IntoSourceFolder ? "S|" : "C|") + string.Join("\u001f", targetNames);

                // 3. pick the name: never collide with a search set, nor with a set made earlier in this run
                string name = PickName(host, target, src.Name + (options.Suffix ?? ""), folderKey, createdThisRun, out string note);

                // 4. existing selection set with that name: replace (default) or skip
                var existing = host.Children(target).Where(k => k != null && host.Kind(k) == SetKind.SelectionSet && Same(host.Name(k), name)).ToList();
                if (existing.Count > 0)
                {
                    if (!options.Overwrite) return Skip(o, "selection set '" + name + "' already exists (overwrite is off)");
                    foreach (var _ in existing)
                    {
                        target = locate();   // re-find before each change (P10)
                        if (target == null) return Fail(o, "target folder lost while replacing");
                        T victim = host.Children(target).FirstOrDefault(k => k != null && host.Kind(k) == SetKind.SelectionSet && Same(host.Name(k), name));
                        if (victim == null) break;
                        if (!host.Remove(target, victim)) return Fail(o, "could not replace old selection set '" + name + "'");
                    }
                    note = (note.Length > 0 ? note + ", " : "") + "replaced old";
                }

                // 5. insert the explicit selection set, then confirm it is in the document
                target = locate();
                if (target == null) return Fail(o, "target folder lost before insert");
                host.InsertSelectionSet(target, name, found);
                target = locate();
                bool ok = target != null && host.Children(target).Any(k => k != null && host.Kind(k) == SetKind.SelectionSet && Same(host.Name(k), name));
                if (!ok) return Fail(o, "insert not confirmed in the Sets window");

                createdThisRun.Add(folderKey + "\u001e" + name);
                o.Status = OutcomeStatus.Converted;
                o.TargetPath = string.Join(" / ", targetNames.Concat(new[] { name }));
                o.Reason = note.Length > 0 ? "(" + note + ")" : "";
                return o;
            }
            catch (Exception ex) { return Fail(o, ex.Message); }
        }

        /// <summary>Base name; if a search set already has it in the target folder, add " (Sel)" (then " (Sel 2)" ...).
        /// If a set of that name was already made in this run, add " (2)", " (3)" ... so nothing made now is overwritten.</summary>
        public static string PickName<T, TItems>(ISetHost<T, TItems> host, T target, string baseName, string folderKey,
            ICollection<string> createdThisRun, out string note)
        {
            note = "";
            var kids = host.Children(target) ?? new List<T>();
            Func<string, bool> searchHas = nm => kids.Any(k => k != null && host.Kind(k) == SetKind.SearchSet && Same(host.Name(k), nm));
            Func<string, bool> madeNow = nm => createdThisRun != null && createdThisRun.Contains(folderKey + "\u001e" + nm);
            string name = baseName ?? "";
            if (searchHas(name))
            {
                name = baseName + Names.CollisionSuffix;
                for (int k = 2; searchHas(name) && k < 1000; k++) name = baseName + " (Sel " + k.ToString(CultureInfo.InvariantCulture) + ")";
                note = "renamed: a search set has the same name";
            }
            if (madeNow(name))
            {
                string stem = name;
                for (int k = 2; (madeNow(name) || searchHas(name)) && k < 10000; k++) name = stem + " (" + k.ToString(CultureInfo.InvariantCulture) + ")";
                note = (note.Length > 0 ? note + ", " : "") + "renamed: same name twice in this run";
            }
            return name;
        }

        static T WalkPath<T, TItems>(ISetHost<T, TItems> host, IList<string> names)
        {
            T cur = host.Root();
            foreach (var n in names)
            {
                bool found;
                cur = SetTree.FindFolder(host, cur, n, out found);
                if (!found) return default(T);
            }
            return cur;
        }

        /// <summary>Find-or-create every folder on the path. After each insert the whole path is walked again from Root() (P10).
        /// Returns null when the path exists, or an error text.</summary>
        public static string EnsurePath<T, TItems>(ISetHost<T, TItems> host, IList<string> names)
        {
            for (int depth = 1; depth <= names.Count; depth++)
            {
                var prefix = names.Take(depth).ToList();
                if (WalkPath(host, prefix) != null) continue;
                T parent = WalkPath(host, names.Take(depth - 1).ToList());
                if (parent == null) return "could not find folder '" + string.Join(" / ", names.Take(depth - 1)) + "'";
                try { host.InsertFolder(parent, names[depth - 1]); }
                catch (Exception ex) { return "could not create folder '" + names[depth - 1] + "': " + ex.Message; }
                if (WalkPath(host, prefix) == null) return "folder '" + string.Join(" / ", prefix) + "' not found after create";
            }
            return null;
        }

        static Outcome Fail(Outcome o, string why) { o.Status = OutcomeStatus.Failed; o.Reason = why; return o; }
        static Outcome Skip(Outcome o, string why) { o.Status = OutcomeStatus.Skipped; o.Reason = why; return o; }
    }
}
