using System.Globalization;

namespace Gloam.Localization;

/// <summary>
/// Resolves a requested culture tag to the chain of catalogs that may serve it.
/// </summary>
/// <remarks>
/// This is the piece that makes "zh-TW" work when only "zh-Hans" and "en-US" ship: the UI asks
/// for what Windows reports, and the chain degrades through parent cultures to the source
/// culture instead of showing raw keys. Normalisation also absorbs the tag variants users and
/// translators actually type — <c>zh_CN</c>, <c>zh-cn</c>, <c>zh</c> — which
/// <see cref="CultureInfo.GetCultureInfo(string)"/> alone does not agree on.
/// </remarks>
public static class CultureChain
{
    /// <summary>The language every catalog is authored in. Never falls back any further.</summary>
    public static readonly CultureInfo SourceCulture = CultureInfo.GetCultureInfo("en-US");

    /// <summary>
    /// Parses a tag leniently. Returns <c>null</c> for blank or unparseable input rather than
    /// throwing — persisted settings and command-line values are untrusted.
    /// </summary>
    public static CultureInfo? TryParse(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        string normalized = tag.Trim().Replace('_', '-');

        // "zh-cn" and "zh-CN" must resolve identically; a hand-edited settings file may hold either.
        //
        // CultureNotFoundException derives from ArgumentException, and an outright malformed tag
        // (bad characters, absurd length) throws the base type. Catching only the derived one
        // would let a hand-edited settings.json crash startup, which is precisely the scenario
        // this method exists to absorb.
        try
        {
            return CultureInfo.GetCultureInfo(normalized);
        }
        catch (ArgumentException)
        {
            // A well-formed tag this OS does not know (e.g. a future locale). Try the neutral
            // parent before giving up: "xx-YY" -> "xx".
            int dash = normalized.IndexOf('-', StringComparison.Ordinal);
            if (dash <= 0)
                return null;

            try
            {
                return CultureInfo.GetCultureInfo(normalized[..dash]);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Builds the ordered lookup chain for <paramref name="requested"/>: the culture itself,
    /// then each parent, then <paramref name="source"/>. Duplicates are removed so a catalog is
    /// never consulted twice.
    /// </summary>
    public static IReadOnlyList<CultureInfo> Build(CultureInfo requested, CultureInfo? source = null)
    {
        ArgumentNullException.ThrowIfNull(requested);
        source ??= SourceCulture;

        var chain = new List<CultureInfo>(4);

        void Add(CultureInfo candidate)
        {
            // Compare by name: CultureInfo instances are not reference-equal and its Equals is
            // stricter than the tag comparison we want here.
            if (!chain.Any(c => string.Equals(c.Name, candidate.Name, StringComparison.OrdinalIgnoreCase)))
                chain.Add(candidate);
        }

        for (CultureInfo? c = requested; c is not null && !string.IsNullOrEmpty(c.Name); c = c.Parent)
            Add(c);

        Add(source);
        return chain;
    }

    /// <summary>
    /// True when text in <paramref name="culture"/> should be set in a CJK-capable face rather
    /// than the bundled Latin display fonts. Drives the font policy: Plus Jakarta Sans and Space
    /// Grotesk have no CJK glyphs, so falling through to them would either tofu or silently
    /// substitute a system face at a mismatched weight.
    /// </summary>
    public static bool RequiresCjkFonts(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        string twoLetter = culture.TwoLetterISOLanguageName;
        return twoLetter is "zh" or "ja" or "ko";
    }
}
