using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Gloam.Localization;
using Gloam.Localization.Wpf;
using Xunit;

namespace Gloam.Tests;

/// <summary>
/// Measures whether any visible text is actually cut off in the shipped languages.
/// </summary>
/// <remarks>
/// Comparing language screenshots by eye finds overflow only when someone happens to look. This
/// measures it: for every single-line <see cref="TextBlock"/> on a main surface it lays the string
/// out at the element's own font settings and compares the width WPF would need against the width
/// the element was given. A shortfall means the text is clipped (or ellipsised, which is at least
/// visible but still loses information).
///
/// The pseudo language is the important case. Chinese is usually shorter than the English source,
/// so a Chinese screenshot cannot prove the layout survives translation — a language that expands
/// text is what breaks fixed columns, and qps-ploc fakes one at +40%.
/// </remarks>
[ResetsLocalization]
[Collection(UiGlobalState.Name)]
    public class LayoutOverflowTests
{
    /// <summary>Sub-pixel differences are measurement noise, not visible clipping.</summary>
    private const double Tolerance = 1.5;

    [Theory]
    [InlineData("zh-Hans")]
    [InlineData("en-US")]
    [InlineData("qps-ploc")]
    public void No_visible_text_is_clipped_on_a_main_surface(string language)
    {
        var findings = new List<string>();

        WpfTestHost.Run(() =>
        {
            foreach ((string name, Func<Window> factory) in UiFixtures.MainSurfaces)
            {
                Localizer baseLocalizer = new(
                    StringCatalogSet.Load(new EmbeddedCatalogSource(
                        typeof(Gloam.Shell.App).Assembly, "Gloam.Resources.i18n.")),
                    new LocalizationOptions(),
                    CultureInfo.GetCultureInfo("en-US"));

                ILocalizer effective = language == "qps-ploc"
                    ? new PseudoLocalizer(baseLocalizer)
                    : baseLocalizer;
                if (language != "qps-ploc")
                    effective.SetCulture(CultureInfo.GetCultureInfo(language));

                LocalizedStrings.Default = new LocalizedStrings(effective);
                CultureFontPolicy.Apply(effective.Culture);

                Window window = factory();
                try
                {
                    window.Show();
                    WpfTestHost.Pump(TimeSpan.FromMilliseconds(500));

                    // Collapsed panels are still part of the layout contract: a user who expands
                    // one should not find its text cut.
                    foreach (Expander expander in Descendants<Expander>(window))
                        expander.IsExpanded = true;
                    WpfTestHost.Pump(TimeSpan.FromMilliseconds(300));

                    foreach (TextBlock block in Descendants<TextBlock>(window))
                        Check(name, block, findings);
                }
                finally
                {
                    window.Close();
                }
            }
        });

        string? reportPath = Environment.GetEnvironmentVariable("GLOAM_LAYOUT_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
            File.WriteAllText(reportPath, string.Join(Environment.NewLine, findings), Encoding.UTF8);

        Assert.True(findings.Count == 0,
            $"{language}: {findings.Count} clipped text run(s){Environment.NewLine}  " +
            string.Join(Environment.NewLine + "  ", findings));
    }

    private static void Check(string surface, TextBlock block, List<string> findings)
    {
        string text = block.Text;
        if (string.IsNullOrWhiteSpace(text))
            return;

        // Wrapped text reflows; it cannot be cut horizontally, and measuring its unwrapped width
        // would report every paragraph as an overflow.
        if (block.TextWrapping != TextWrapping.NoWrap)
            return;

        if (block.Visibility != Visibility.Visible || block.ActualWidth <= 0)
            return;

        if (block.Parent is not null && block.Parent is TextBlock)
            return;

        double available = block.ActualWidth - block.Padding.Left - block.Padding.Right;

        if (available <= 0)
            return;

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            block.FlowDirection,
            new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch),
            block.FontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(block).PixelsPerDip);

        double needed = formatted.WidthIncludingTrailingWhitespace;
        if (needed <= available + Tolerance)
            return;

        findings.Add(
            $"{surface}: \"{Truncate(text)}\" needs {needed:F0}px, has {available:F0}px " +
            $"(+{needed - available:F0}), trimming={block.TextTrimming}, size={block.FontSize:F0}");
    }

    private static string Truncate(string value)
        => value.Length <= 48 ? value : value[..45] + "…";

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;

            foreach (T nested in Descendants<T>(child))
                yield return nested;
        }
    }
}
