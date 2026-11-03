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
    [Collection(UiGlobalState.Name)]
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
            WpfTestHost.Run(() =>
            {
                string root = Path.Combine(Path.GetTempPath(), $"gloam-shot-{Guid.NewGuid():N}");
                string originalData = AppPaths.DataDir;
                string originalRoaming = AppPaths.RoamingDataDir;

                try
                {
                    AppPaths.UseDataDirectoriesForCurrentProcess(
                        Path.Combine(root, "data"), Path.Combine(root, "roaming"));

                    CaptureOne(output, "en-US", "report", UiFixtures.BuildReport);
                    CaptureOne(output, "zh-Hans", "report", UiFixtures.BuildReport);
                    CaptureOne(output, "en-US", "dashboard", UiFixtures.BuildDashboard);
                    CaptureOne(output, "zh-Hans", "dashboard", UiFixtures.BuildDashboard);
                    CaptureOne(output, "en-US", "settings", UiFixtures.BuildSettings);
                    CaptureOne(output, "zh-Hans", "settings", UiFixtures.BuildSettings);

                    // Pseudo-language pass: ~40% longer strings, which is what the layout has to
                    // survive before a second real language can be promised.
                    CaptureOne(output, "qps-ploc", "dashboard", UiFixtures.BuildDashboard, pseudo: true);
                    CaptureOne(output, "qps-ploc", "settings", UiFixtures.BuildSettings, pseudo: true);

                    // The Game Lab window is the second main surface and is reached straight from the
                    // tray, so it belongs in the manual alongside the dashboard.
                    CaptureOne(output, "en-US", "gamelab", UiFixtures.BuildGameLab);
                    CaptureOne(output, "zh-Hans", "gamelab", UiFixtures.BuildGameLab);
                    CaptureOne(output, "qps-ploc", "gamelab", UiFixtures.BuildGameLab, pseudo: true);
                }
                finally
                {
                    AppPaths.UseDataDirectoriesForCurrentProcess(originalData, originalRoaming);
                }
            });
        }

        private static void CaptureOne(string folder, string culture, string name,
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

            // A fresh data directory per capture. The windows persist what they are given, so a
            // shared directory let one capture's sample profile show up in the next one's list -
            // the Chinese screenshot ended up with two entries where English had one.
            string captureRoot = Path.Combine(Path.GetTempPath(), $"gloam-shot-data-{Guid.NewGuid():N}");
            AppPaths.UseDataDirectoriesForCurrentProcess(
                Path.Combine(captureRoot, "data"), Path.Combine(captureRoot, "roaming"));

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

        private static void Pump(TimeSpan duration)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
    }
}
