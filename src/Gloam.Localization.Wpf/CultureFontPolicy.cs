using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Gloam.Localization.Wpf;

/// <summary>
/// Swaps the app's font tokens for a face that can actually draw the active language.
/// </summary>
/// <remarks>
/// The app bundles Plus Jakarta Sans, Space Grotesk and Cascadia Mono — all Latin-only. Chinese
/// text set in them falls through to a system face per glyph, which produces mixed metrics and a
/// visibly inconsistent weight rather than tofu, so the bug is easy to miss in review and obvious
/// to a user. Selecting a CJK-capable family up front costs one resource swap.
///
/// The token entries are consumed with <c>{DynamicResource BodyFont}</c> across the UI, so
/// replacing the entry re-resolves every window, including ones already open.
///
/// This class never hard-codes the app's font resources: it snapshots whatever
/// <c>Tokens.xaml</c> published on first use and restores it for non-CJK cultures, so the theme
/// stays the single source of truth for the brand faces.
/// </remarks>
public static class CultureFontPolicy
{
    private const string BodyFontKey = "BodyFont";
    private const string DisplayFontKey = "DisplayFont";

    // Families tried in order; WPF uses the first installed one for the whole run.
    private const string CjkBodyFamily = "Microsoft YaHei UI, Microsoft YaHei, Segoe UI, Noto Sans CJK SC";
    private const string CjkDisplayFamily = "Microsoft YaHei UI, Microsoft YaHei, Segoe UI Semibold, Noto Sans CJK SC";

    private static readonly object Gate = new();
    private static FontFamily? _originalBody;
    private static FontFamily? _originalDisplay;

    /// <summary>True when <paramref name="culture"/> needs a CJK-capable family.</summary>
    public static bool RequiresCjk(CultureInfo culture) => CultureChain.RequiresCjkFonts(culture);

    /// <summary>
    /// Publishes the fonts for <paramref name="culture"/> into the application resource dictionary.
    /// No-op outside a running application (unit tests, CLI), so it is always safe to call.
    /// </summary>
    public static void Apply(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        ResourceDictionary? resources = Application.Current?.Resources;
        if (resources is null)
            return;

        lock (Gate)
        {
            // Snapshot before the first overwrite so the Latin faces can be restored later.
            if (_originalBody is null && resources[BodyFontKey] is FontFamily body)
                _originalBody = body;
            if (_originalDisplay is null && resources[DisplayFontKey] is FontFamily display)
                _originalDisplay = display;

            if (RequiresCjk(culture))
            {
                resources[BodyFontKey] = new FontFamily(CjkBodyFamily);
                resources[DisplayFontKey] = new FontFamily(CjkDisplayFamily);
            }
            else
            {
                if (_originalBody is not null) resources[BodyFontKey] = _originalBody;
                if (_originalDisplay is not null) resources[DisplayFontKey] = _originalDisplay;
            }
        }
    }

    /// <summary>
    /// The family a given culture would get, without touching application resources. Useful for
    /// design-time previews and tests.
    /// </summary>
    public static FontFamily Preview(CultureInfo culture, bool display)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (RequiresCjk(culture))
            return new FontFamily(display ? CjkDisplayFamily : CjkBodyFamily);

        ResourceDictionary? resources = Application.Current?.Resources;
        string key = display ? DisplayFontKey : BodyFontKey;
        return resources?[key] as FontFamily ?? new FontFamily("Segoe UI");
    }
}
