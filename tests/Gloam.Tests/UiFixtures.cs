using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using Gloam.Core;
using Velopack;

namespace Gloam.Tests;

/// <summary>
/// The windows rendered by the documentation snapshots and the layout regression tests.
/// </summary>
/// <remarks>
/// Shared so that "what the manual shows" and "what the layout gate measures" cannot drift apart.
/// Both construct the window the same way the composition root does, minus the services that need a
/// real desktop session — a stand-in update manager, an empty monitor manager, and a no-op apply
/// callback, none of which affect layout.
/// </remarks>
internal static class UiFixtures
{
    internal static Window BuildDashboard()
    {
        var settings = new SettingsManager();
        var nightMode = new NightModeService(settings.NightMode);
        var update = new UpdateService(new NoUpdateManager());

        return new DashboardWindow(
            new MonitorManager(), settings, nightMode, update, (_, _, _, _, _) => { })
        {
            ShowInTaskbar = false,
            Left = -20000,
            Top = -20000,
            Width = 1080,
            Height = 760,
        };
    }

    internal static Window BuildGameLab()
    {
        var settings = new SettingsManager();
        var nightMode = new NightModeService(settings.NightMode);
        var update = new UpdateService(new NoUpdateManager());

        var window = new GameLabWindow(
            new MonitorManager(), settings, nightMode, update, (_, _, _, _, _) => { })
        {
            ShowInTaskbar = false,
            Left = -20000,
            Top = -20000,
            Width = 1080,
            Height = 720,
        };

        // The editor panel only renders its controls once a profile is selected, and an empty panel
        // makes a useless screenshot: every combo box reads as blank and the reader cannot tell a
        // layout problem from a missing fixture. One sample profile gives the panel real content.
        if (window.DataContext is DashboardViewModel viewModel)
        {
            var editor = new GamerProfileEditorItem(new GamerProfileRule
            {
                AppName = "DOOM Eternal",
                DisplayName = "DOOM Eternal",
            });
            viewModel.GamerMode.Profiles.Add(editor);
            viewModel.GamerMode.SelectedProfile = editor;
        }

        return window;
    }

    internal static Window BuildSettings()
    {
        var settings = new SettingsManager();
        var monitor = new MonitorInfo { FriendlyName = "TL156MDMP31-0" };

        return new SettingsWindow(monitor, new List<MonitorInfo> { monitor }, settings)
        {
            ShowInTaskbar = false,
            Left = -20000,
            Top = -20000,
        };
    }

    /// <summary>Every surface a user can reach without opening a modal dialog.</summary>
    internal static IReadOnlyList<(string Name, Func<Window> Factory)> MainSurfaces { get; } =
        new (string, Func<Window>)[]
        {
            ("dashboard", BuildDashboard),
            ("gamelab", BuildGameLab),
            ("settings", BuildSettings),
        };

    private sealed class NoUpdateManager : IUpdateManagerAdapter
    {
        public bool IsInstalled => false;
        public bool IsPortable => true;
        public SemanticVersion? CurrentVersion => null;
        public VelopackAsset? UpdatePendingRestart => null;
        public Task<UpdateInfo?> CheckForUpdatesAsync() => Task.FromResult<UpdateInfo?>(null);
        public Task DownloadUpdatesAsync(UpdateInfo info) => Task.CompletedTask;
        public void ApplyUpdatesAndRestart(UpdateInfo info) { }
        public void WaitExitThenApplyUpdates(VelopackAsset asset, bool silent, bool restart) { }
        public string? GetLocalPackagePath(VelopackAsset asset) => null;
    }
}
