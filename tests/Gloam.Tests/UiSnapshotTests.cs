using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Gloam.Core;
using Velopack;
using Gloam.Localization;
using Gloam.Localization.Wpf;
using Xunit;

namespace Gloam.Tests
{
    /// <summary>
    /// Renders real application windows to PNG for documentation.
    /// </summary>
    /// <remarks>
    /// Not a test: it asserts nothing and is a no-op unless <c>GLOAM_CAPTURE_UI=1</c>. It exists
    /// because documentation screenshots otherwise rot — every UI change leaves the manual's images
    /// showing a previous version, and nobody notices. Running it produces images straight from the
    /// live visual tree, in both shipped languages.
    ///
    /// These are renders of the real windows, not screen captures: the window is laid out offscreen
    /// and rasterised with RenderTargetBitmap. Same fonts, same layout, same resources.
    /// </remarks>
    [ResetsLocalization]
    public sealed class UiSnapshotTests
    {
        [Fact]
        public void Capture_window_snapshots_for_documentation()
        {
            if (Environment.GetEnvironmentVariable("GLOAM_CAPTURE_UI") != "1")
                return;

            string output = Environment.GetEnvironmentVariable("GLOAM_CAPTURE_DIR")
                ?? Path.Combine(Path.GetTempPath(), "gloam-ui");

            Directory.CreateDirectory(output);

            // One Application per process: creating a second one throws. The language switch is
            // done by republishing LocalizedStrings.Default and opening a fresh window, which is
            // exactly what the running app does.
            RunSta(() =>
            {
                string root = Path.Combine(Path.GetTempPath(), $"gloam-shot-{Guid.NewGuid():N}");
                string originalData = AppPaths.DataDir;
                string originalRoaming = AppPaths.RoamingDataDir;
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

                try
                {
                    AppPaths.UseDataDirectoriesForCurrentProcess(
                        Path.Combine(root, "data"), Path.Combine(root, "roaming"));

                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/Gloam;component/Themes/Tokens.xaml"),
                    });

                    CaptureOne(output, "en-US", "dashboard", app, BuildDashboard);
                    CaptureOne(output, "zh-Hans", "dashboard", app, BuildDashboard);
                    CaptureOne(output, "en-US", "settings", app, BuildSettings);
                    CaptureOne(output, "zh-Hans", "settings", app, BuildSettings);

                    // Pseudo-language pass: ~40% longer strings, which is what the layout has to
                    // survive before a second real language can be promised.
                    CaptureOne(output, "qps-ploc", "dashboard", app, BuildDashboard, pseudo: true);
                    CaptureOne(output, "qps-ploc", "settings", app, BuildSettings, pseudo: true);
                }
                finally
                {
                    app.Shutdown();
                    AppPaths.UseDataDirectoriesForCurrentProcess(originalData, originalRoaming);
                }
            });
        }

        private static Window BuildSettings()
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

        private static void CaptureOne(string folder, string culture, string name, Application app,
            Func<Window> factory, bool pseudo = false)
        {
            Localizer localizer = new(
                StringCatalogSet.Load(new EmbeddedCatalogSource(
                    typeof(Gloam.Shell.App).Assembly, "Gloam.Resources.i18n.")),
                new LocalizationOptions(),
                CultureInfo.GetCultureInfo(pseudo ? "en-US" : culture));

            ILocalizer effective = pseudo ? new PseudoLocalizer(localizer) : localizer;
            LocalizedStrings.Default = new LocalizedStrings(effective);
            CultureFontPolicy.Apply(localizer.Culture);

            Window window = factory();
            try
            {
                window.Show();
                Pump(TimeSpan.FromMilliseconds(600));

                Save(window, Path.Combine(folder, $"{name}-{culture}.png"));

                // Windows are fixed-height and scroll internally, so a viewport-sized render hides
                // everything below the fold. Two things are needed, and the second only because the
                // first cannot finish the job: growing the window helps until the desktop work area
                // caps it (this machine caps at ~1000px), so whatever still overflows is captured as
                // a second frame scrolled to the end.
                foreach (Expander expander in FindAll<Expander>(window))
                    expander.IsExpanded = true;
                Pump(TimeSpan.FromMilliseconds(300));

                ScrollViewer? scroller = null;
                double surplus = 0;
                foreach (ScrollViewer viewer in FindAll<ScrollViewer>(window))
                {
                    if (viewer.ScrollableHeight > surplus)
                    {
                        surplus = viewer.ScrollableHeight;
                        scroller = viewer;
                    }
                }

                if (surplus > 1)
                {
                    window.Height = window.ActualHeight + surplus + 8;
                    Pump(TimeSpan.FromMilliseconds(400));
                }

                Save(window, Path.Combine(folder, $"{name}-{culture}-full.png"));

                if (scroller is not null && scroller.ScrollableHeight > 1)
                {
                    scroller.ScrollToEnd();
                    Pump(TimeSpan.FromMilliseconds(400));
                    Save(window, Path.Combine(folder, $"{name}-{culture}-bottom.png"));
                }
            }
            finally
            {
                window.Close();
            }
        }

        private static void Save(Window window, string path)
        {
            var bitmap = new RenderTargetBitmap(
                (int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using FileStream stream = File.Create(path);
            encoder.Save(stream);
        }

        private static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    yield return match;

                foreach (T nested in FindAll<T>(child))
                    yield return nested;
            }
        }

        private static Window BuildDashboard()
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

        private static void RunSta(Action body)
        {
            ExceptionDispatchInfo? failure = null;
            var thread = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
            })
            {
                IsBackground = true,
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            failure?.Throw();
        }

        private static void Pump(TimeSpan duration)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

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
}
