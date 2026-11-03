using System.Globalization;

namespace Gloam.Localization;

/// <summary>
/// CLDR cardinal plural rules for the languages this app can ship, plus a conservative default.
/// </summary>
/// <remarks>
/// Hand-written rather than data-file driven: the rule set needed by an EU/EA language pair is
/// three lines each, and a wrong-but-plausible table generated from CLDR data is worse than an
/// explicit one that reads correctly. Languages not listed here get the English rule (one when
/// the integer part is 1), which is the correct rule for most of the languages an app adds
/// before it thinks about plurals at all. Adding a language means adding a case, and the
/// validator will catch the catalog that needs it.
/// </remarks>
public static class PluralRules
{
    /// <summary>Selects the CLDR category for <paramref name="count"/> in <paramref name="culture"/>.</summary>
    public static PluralCategory Select(CultureInfo culture, double count)
    {
        ArgumentNullException.ThrowIfNull(culture);

        // CLDR operates on the absolute value; -1 display should pluralise like 1.
        double n = Math.Abs(count);
        bool integer = Math.Abs(n - Math.Truncate(n)) < double.Epsilon;
        long i = (long)Math.Truncate(n);

        switch (culture.TwoLetterISOLanguageName)
        {
            // No grammatical plural: one form for every count.
            case "zh":
            case "ja":
            case "ko":
            case "vi":
            case "th":
            case "id":
            case "ms":
                return PluralCategory.Other;

            // Romance languages put 0 and 1 in the singular.
            case "fr":
            case "pt":
                return i is 0 or 1 ? PluralCategory.One : PluralCategory.Other;

            // Slavic: one / few (2-4, not 12-14) / many.
            case "ru":
            case "uk":
                if (integer && i % 10 == 1 && i % 100 != 11)
                    return PluralCategory.One;
                if (integer && i % 10 is >= 2 and <= 4 && i % 100 is < 12 or > 14)
                    return PluralCategory.Few;
                return PluralCategory.Many;

            case "pl":
                if (integer && i == 1)
                    return PluralCategory.One;
                if (integer && i % 10 is >= 2 and <= 4 && i % 100 is < 12 or > 14)
                    return PluralCategory.Few;
                return PluralCategory.Many;

            case "ar":
                if (n == 0) return PluralCategory.Zero;
                if (n == 1) return PluralCategory.One;
                if (n == 2) return PluralCategory.Two;
                if (integer && i % 100 is >= 3 and <= 10) return PluralCategory.Few;
                return PluralCategory.Many;

            // English, German, Spanish, Italian, Dutch, and the default for anything unlisted.
            default:
                return integer && i == 1 ? PluralCategory.One : PluralCategory.Other;
        }
    }

    /// <summary>
    /// The categories a catalog for <paramref name="culture"/> is expected to define. The
    /// validator uses this to flag a translation that silently loses a form.
    /// </summary>
    public static IReadOnlyList<PluralCategory> RequiredCategories(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return culture.TwoLetterISOLanguageName switch
        {
            "zh" or "ja" or "ko" or "vi" or "th" or "id" or "ms" => new[] { PluralCategory.Other },
            "fr" or "pt" => new[] { PluralCategory.One, PluralCategory.Other },
            "ru" or "uk" or "pl" => new[] { PluralCategory.One, PluralCategory.Few, PluralCategory.Many },
            "ar" => new[]
            {
                PluralCategory.Zero, PluralCategory.One, PluralCategory.Two,
                PluralCategory.Few, PluralCategory.Many, PluralCategory.Other,
            },
            _ => new[] { PluralCategory.One, PluralCategory.Other },
        };
    }
}
