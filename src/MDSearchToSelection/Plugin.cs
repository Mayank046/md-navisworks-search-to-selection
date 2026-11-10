// MD Search to Selection - dock pane + Tool Add-ins toggle
// Author: MD (Mayank Dholariya)
using System;
using System.IO;
using System.Windows.Forms;
using Autodesk.Navisworks.Api.Plugins;
using SearchToSelection.Core;
using NavisApp = Autodesk.Navisworks.Api.Application;

namespace MDSearchToSelection
{
    internal static class Ids
    {
        internal const string Developer = "MDST";
        internal const string PaneName = "MDSearchToSelection.Pane";
        internal const string PaneId = PaneName + "." + Developer;
        internal const string DisplayName = Names.Product;
        internal const string ToolTip = Names.Product + " - convert Search Sets to fast Selection Sets | " + Names.Author;
        internal const string Version = Names.Version;
    }

    internal static class Diagnostics
    {
        private static readonly object Gate = new object();
        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Names.LogFolder); }
        }
        public static string LogPath { get { return Path.Combine(Folder, Names.LogFile); } }
        public static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                lock (Gate)
                    File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch { }
        }
    }

    [Plugin(Ids.PaneName, Ids.Developer, DisplayName = Ids.DisplayName, ToolTip = Ids.ToolTip)]
    [DockPanePlugin(420, 640, AutoScroll = false, FixedSize = false, MinimumWidth = 320, MinimumHeight = 420)]
    public sealed class PanePlugin : DockPanePlugin
    {
        public override Control CreateControlPane()
        {
            Diagnostics.Log("CreateControlPane md-searchtoselection-" + Ids.Version + ": " + typeof(PanePlugin).Assembly.Location);
            var control = new SearchToSelectionPane();
            control.Dock = DockStyle.Fill;
            try { control.CreateControl(); } catch (Exception ex) { Diagnostics.Log("CreateControl: " + ex); }
            return control;
        }

        public override void DestroyControlPane(Control pane)
        {
            if (pane != null) pane.Dispose();
        }
    }

    [Plugin("MDSearchToSelection.Show", Ids.Developer, DisplayName = Ids.DisplayName, ToolTip = Ids.ToolTip,
        ExtendedToolTip = "Show or hide MD Search to Selection: tick Search Sets and turn them into Selection Sets in one click. " + Names.Author)]
    [AddInPlugin(AddInLocation.AddIn, Icon = "Images\\MDSearchToSelection_16.png", LargeIcon = "Images\\MDSearchToSelection_32.png",
        CanToggle = true, LoadForCanExecute = true)]
    public sealed class ShowCommand : AddInPlugin
    {
        public override CommandState CanExecute()
        {
            var state = new CommandState(true);
            try
            {
                PluginRecord record = NavisApp.Plugins.FindPlugin(Ids.PaneId);
                var pane = record != null ? record.LoadedPlugin as DockPanePlugin : null;
                state.IsChecked = pane != null && pane.Visible;
            }
            catch { state.IsChecked = false; }
            return state;
        }

        public override int Execute(params string[] parameters)
        {
            if (NavisApp.IsAutomated) return 0;
            PluginRecord record = NavisApp.Plugins.FindPlugin(Ids.PaneId);
            if (record == null)
            {
                MessageBox.Show("MD Search to Selection panel was not found. Please reinstall.", Ids.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 0;
            }
            try
            {
                if (record.LoadedPlugin == null) record.LoadPlugin();
            }
            catch (Exception ex)
            {
                Diagnostics.Log("LoadPlugin failed: " + ex);
                MessageBox.Show("MD Search to Selection panel failed to load.\r\n\r\n" + ex.Message + "\r\n\r\nLog: %APPDATA%\\MDSearchToSelection\\mdsearchtoselection.log",
                    Ids.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 0;
            }
            var pane = record.LoadedPlugin as DockPanePlugin;
            if (pane == null) return 0;
            pane.Visible = !pane.Visible;
            if (pane.Visible) pane.ActivatePane();
            return 0;
        }
    }
}
