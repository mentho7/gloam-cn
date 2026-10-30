using System.Globalization;

namespace Gloam.Localization;

/// <summary>What a lookup returns when no catalog defines the key.</summary>
public enum MissingKeyBehavior
{
    /// <summary>Return the key. Default: the UI shows <c>dashboard.title</c>, which is unmistakably a bug.</summary>
    ReturnKey,

    /// <summary>Return an empty string. For layouts where a stray key would be worse than a gap.</summary>
    ReturnEmpty,
}

/// <summary>Configuration for a <see cref="Localizer"/>.</summary>
public sealed class LocalizationOptions
{
    /// <summary>
    /// Language every catalog is authored in. Lookups fall back to it last, and the validator
    /// treats its keys as the reference set.
    /// </summary>
    public CultureInfo SourceCulture { get; set; } = CultureChain.SourceCulture;

    /// <summary>
    /// Optional folder scanned for additional catalogs, e.g. <c>%LOCALAPPDATA%\Gloam\i18n</c>.
    /// Files here override the ones embedded in the binary, so a translation can be corrected
    /// or a new language added without cutting a release. A malformed file in this folder is
    /// skipped and reported, never fatal — user-controlled input does not get to crash the app
    /// on startup.
    /// </summary>
    public string? ExternalCatalogDirectory { get; set; }

    /// <summary>Lookup result when a key is absent everywhere.</summary>
    public MissingKeyBehavior MissingKeyBehavior { get; set; } = MissingKeyBehavior.ReturnKey;

    /// <summary>
    /// Cap on distinct missing keys tracked. Diagnostics only; a runaway loop must not turn into
    /// unbounded memory growth.
    /// </summary>
    public int MaxTrackedMissingKeys { get; set; } = 500;
}
