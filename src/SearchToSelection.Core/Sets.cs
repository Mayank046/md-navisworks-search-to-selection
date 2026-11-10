// MD Search to Selection - set tree model (no Navisworks)
// Author: MD (Mayank Dholariya)
using System;
using System.Collections.Generic;
using System.Linq;

namespace SearchToSelection.Core
{
    /// <summary>What a node in the Sets window is.</summary>
    public enum SetKind { Folder, SearchSet, SelectionSet, Other }

    /// <summary>
    /// The Sets window seen through a thin adapter. In Navisworks: T = SavedItem, TItems = ModelItemCollection.
    /// Root() must re-read the document's RootItem every call (pitfall P10: inserts are copies).
    /// </summary>
    public interface ISetHost<T, TItems>
    {
        T Root();
        IList<T> Children(T folder);
        string Name(T node);
        SetKind Kind(T node);
        /// <summary>Executes the saved search of a Search Set and returns the items it finds now. Throws on failure.</summary>
        TItems ResolveItems(T searchSet);
        int Count(TItems items);
        /// <summary>Inserts an empty folder as the last child of parent (the document stores a copy).</summary>
        void InsertFolder(T parent, string name);
        /// <summary>Inserts an explicit Selection Set (items only, no search) as the last child of parent.</summary>
        void InsertSelectionSet(T parent, string name, TItems items);
        bool Remove(T parent, T child);
    }

    /// <summary>One step of a path: a child name and kind, plus which same-named, same-kind sibling it is (0 = first).</summary>
    public sealed class RefStep
    {
        public string Name { get; }
        public SetKind Kind { get; }
        public int Occurrence { get; }
        public RefStep(string name, SetKind kind, int occurrence) { Name = name ?? ""; Kind = kind; Occurrence = occurrence; }
        public override string ToString() { return Name + (Occurrence > 0 ? "#" + Occurrence : ""); }
    }

    /// <summary>
    /// Stable address of a set: names from the root. It survives inserts and removals of other items,
    /// because the tool never removes search sets or folders (only same-named selection sets it replaces).
    /// </summary>
    public sealed class SetRef
    {
        public IReadOnlyList<RefStep> Steps { get; }
        public SetRef(IEnumerable<RefStep> steps) { Steps = (steps ?? Enumerable.Empty<RefStep>()).ToList(); }
        public string Name { get { return Steps.Count == 0 ? "" : Steps[Steps.Count - 1].Name; } }
        public IList<string> FolderNames { get { return Steps.Take(Math.Max(0, Steps.Count - 1)).Select(s => s.Name).ToList(); } }
        public SetRef Parent { get { return new SetRef(Steps.Take(Math.Max(0, Steps.Count - 1))); } }
        /// <summary>"Folder / Sub / Set" as shown in the pane and status lines.</summary>
        public string DisplayPath { get { return string.Join(" / ", Steps.Select(s => s.Name)); } }
        public string Key { get { return string.Join("\u001f", Steps.Select(s => s.Kind + ":" + s.Name + "#" + s.Occurrence)); } }
        public override string ToString() { return DisplayPath; }
    }

    /// <summary>One row in the pane: a folder that contains search sets somewhere below, or a search set.</summary>
    public sealed class SetNode
    {
        public string Name { get; set; }
        public SetKind Kind { get; set; }
        public SetRef Ref { get; set; }
        public List<SetNode> Children { get; } = new List<SetNode>();
        public int SearchSetCount { get { return Kind == SetKind.SearchSet ? 1 : Children.Sum(c => c.SearchSetCount); } }

        public IEnumerable<SetNode> SearchSets()
        {
            if (Kind == SetKind.SearchSet) { yield return this; yield break; }
            foreach (var c in Children) foreach (var s in c.SearchSets()) yield return s;
        }
    }

    public static class SetTree
    {
        static bool Same(string a, string b) { return string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase); }

        /// <summary>
        /// Lists the Search Sets (HasSearch) under the root, keeping folders. Plain Selection Sets are left out.
        /// Folders with no search set anywhere below are left out. onError gets (path, message) for skipped nodes.
        /// </summary>
        public static SetNode List<T, TItems>(ISetHost<T, TItems> host, Action<string, string> onError = null)
        {
            var root = new SetNode { Name = "", Kind = SetKind.Folder, Ref = new SetRef(null) };
            Fill(host, host.Root(), root, new List<RefStep>(), onError, 0);
            return root;
        }

        static void Fill<T, TItems>(ISetHost<T, TItems> host, T folder, SetNode into, List<RefStep> path, Action<string, string> onError, int depth)
        {
            if (depth > 64) return;
            IList<T> kids;
            try { kids = host.Children(folder) ?? new List<T>(); }
            catch (Exception ex) { onError?.Invoke(string.Join(" / ", path.Select(p => p.Name)), ex.Message); return; }
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (T k in kids)
            {
                try
                {
                    if (k == null) continue;
                    SetKind kind = host.Kind(k);
                    string name = host.Name(k) ?? "";
                    string occKey = kind + ":" + name;
                    int occ; seen.TryGetValue(occKey, out occ); seen[occKey] = occ + 1;
                    var step = new RefStep(name, kind, occ);
                    if (kind == SetKind.Folder)
                    {
                        var node = new SetNode { Name = name, Kind = SetKind.Folder, Ref = new SetRef(path.Concat(new[] { step })) };
                        path.Add(step);
                        Fill(host, k, node, path, onError, depth + 1);
                        path.RemoveAt(path.Count - 1);
                        if (node.SearchSetCount > 0) into.Children.Add(node);
                    }
                    else if (kind == SetKind.SearchSet)
                    {
                        into.Children.Add(new SetNode { Name = name, Kind = SetKind.SearchSet, Ref = new SetRef(path.Concat(new[] { step })) });
                    }
                }
                catch (Exception ex) { onError?.Invoke(string.Join(" / ", path.Select(p => p.Name)), ex.Message); }
            }
        }

        /// <summary>Finds a node again from a fresh Root() (P10). Returns false when it is gone.</summary>
        public static bool TryFind<T, TItems>(ISetHost<T, TItems> host, SetRef r, out T node)
        {
            node = host.Root();
            foreach (var step in r.Steps)
            {
                int occ = 0; bool found = false;
                foreach (T k in host.Children(node) ?? new List<T>())
                {
                    if (k == null) continue;
                    if (host.Kind(k) != step.Kind || !Same(host.Name(k), step.Name)) continue;
                    if (occ++ == step.Occurrence) { node = k; found = true; break; }
                }
                if (!found) { node = default(T); return false; }
            }
            return true;
        }

        /// <summary>First child folder with this name (case-insensitive), or default.</summary>
        public static T FindFolder<T, TItems>(ISetHost<T, TItems> host, T parent, string name, out bool found)
        {
            found = false;
            foreach (T k in host.Children(parent) ?? new List<T>())
                if (k != null && host.Kind(k) == SetKind.Folder && Same(host.Name(k), name)) { found = true; return k; }
            return default(T);
        }
    }
}
