// MD Search to Selection - the ONLY place that touches the Navisworks API.
// Author: MD (Mayank Dholariya)
// API used (all present in the 2017-2027 reference DLLs, see README "Known limits"):
//   doc.SelectionSets.RootItem / InsertCopy(GroupItem,int,SavedItem) / Remove(GroupItem,SavedItem)
//   SelectionSet.HasSearch, SelectionSet.GetSelectedItems()  (shipped in MD Route Walk)
//   SelectionSet.Search.FindAll(doc, false)                  (fallback only)
//   new SelectionSet(ModelItemCollection) { DisplayName }    (handbook 3.9)
using System;
using System.Collections.Generic;
using SearchToSelection.Core;
using Nw = Autodesk.Navisworks.Api;

namespace MDSearchToSelection
{
    /// <summary>Adapter: Navisworks Sets window -> SearchToSelection.Core.ISetHost.</summary>
    internal sealed class NwSetHost : ISetHost<Nw.SavedItem, Nw.ModelItemCollection>
    {
        private readonly Nw.Document _doc;
        public NwSetHost(Nw.Document doc) { _doc = doc; }

        // P10: always read RootItem again; inserted items are copies.
        public Nw.SavedItem Root() { return _doc.SelectionSets.RootItem; }

        public IList<Nw.SavedItem> Children(Nw.SavedItem folder)
        {
            var l = new List<Nw.SavedItem>();
            var g = folder as Nw.GroupItem;
            if (g == null) return l;
            try { foreach (Nw.SavedItem c in g.Children) if (c != null) l.Add(c); }
            catch (Exception ex) { Diagnostics.Log("Children: " + ex.Message); }
            return l;
        }

        public string Name(Nw.SavedItem n) { try { return n.DisplayName ?? ""; } catch { return ""; } }

        public SetKind Kind(Nw.SavedItem n)
        {
            try
            {
                if (n is Nw.FolderItem) return SetKind.Folder;
                var s = n as Nw.SelectionSet;
                if (s != null) return s.HasSearch ? SetKind.SearchSet : SetKind.SelectionSet;
            }
            catch (Exception ex) { Diagnostics.Log("Kind: " + ex.Message); }
            return SetKind.Other;
        }

        public Nw.ModelItemCollection ResolveItems(Nw.SavedItem searchSet)
        {
            var s = searchSet as Nw.SelectionSet;
            if (s == null) throw new InvalidOperationException("not a selection/search set");
            Exception first = null;
            try { var c = s.GetSelectedItems(); if (c != null) return c; }
            catch (Exception ex) { first = ex; Diagnostics.Log("GetSelectedItems '" + Name(s) + "': " + ex.Message); }
            try
            {
                Nw.Search q = s.Search;
                if (q != null) { var c = q.FindAll(_doc, false); if (c != null) return c; }
            }
            catch (Exception ex) { Diagnostics.Log("Search.FindAll '" + Name(s) + "': " + ex.Message); if (first == null) first = ex; }
            throw new InvalidOperationException(first != null ? first.Message : "the search returned no result");
        }

        public int Count(Nw.ModelItemCollection items) { try { return items == null ? 0 : items.Count; } catch { return 0; } }

        public void InsertFolder(Nw.SavedItem parent, string name)
        {
            var g = parent as Nw.GroupItem;
            if (g == null) throw new InvalidOperationException("parent is not a folder");
            var f = new Nw.FolderItem { DisplayName = name };
            _doc.SelectionSets.InsertCopy(g, g.Children.Count, f);
        }

        public void InsertSelectionSet(Nw.SavedItem parent, string name, Nw.ModelItemCollection items)
        {
            var g = parent as Nw.GroupItem;
            if (g == null) throw new InvalidOperationException("parent is not a folder");
            var set = new Nw.SelectionSet(items) { DisplayName = name };   // explicit items, no Search attached
            _doc.SelectionSets.InsertCopy(g, g.Children.Count, set);
        }

        public bool Remove(Nw.SavedItem parent, Nw.SavedItem child)
        {
            var g = parent as Nw.GroupItem;
            if (g == null || child == null) return false;
            return _doc.SelectionSets.Remove(g, child);
        }
    }

    internal static class Bridge
    {
        /// <summary>Active document, or null with a reason the user can act on.</summary>
        public static Nw.Document ActiveDocument(out string why)
        {
            why = null;
            Nw.Document doc = null;
            try { doc = Nw.Application.ActiveDocument; } catch (Exception ex) { Diagnostics.Log("ActiveDocument: " + ex.Message); }
            if (doc == null) { why = "Open a model first."; return null; }
            try { var r = doc.SelectionSets.RootItem; if (r == null) { why = "This document has no Sets."; return null; } }
            catch (Exception ex) { why = "Sets are not available: " + ex.Message; return null; }
            return doc;
        }

        public static int ModelCount(Nw.Document doc) { try { return doc.Models.Count; } catch { return 0; } }
    }
}
