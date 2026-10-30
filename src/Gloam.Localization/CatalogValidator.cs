using System.Globalization;

namespace Gloam.Localization;

/// <summary>What kind of problem a catalog entry has.</summary>
public enum CatalogIssueKind
{
    /// <summary>The source catalog does not define a key another catalog translates.</summary>
    OrphanTranslation,

    /// <summary>A key exists in the source catalog but is missing from this one.</summary>
    MissingKey,

    /// <summary>A translation dropped, renamed or invented a placeholder the source has.</summary>
    PlaceholderMismatch,

    /// <summary>A locale whose plural rules need more forms than the entry provides.</summary>
    MissingPluralForm,

    /// <summary>The entry renders as empty text.</summary>
    EmptyValue,
}

/// <summary>One validation finding.</summary>
public sealed record CatalogIssue(CatalogIssueKind Kind, string Culture, string Key, string Message)
{
    public override string ToString() => $"[{Kind}] {Culture}: {Key} — {Message}";
}

/// <summary>
/// Cross-checks catalogs against the source language.
/// </summary>
/// <remarks>
/// This is the part of an i18n setup that decides whether translations rot. A missing key degrades
/// to the source language and nobody notices; a translation whose placeholder was renamed renders
/// <c>{count}</c> literally to a user. Neither shows up in a build, so CI has to ask.
/// </remarks>
public static class CatalogValidator
{
    /// <summary>
    /// Validates every catalog in <paramref name="catalogs"/> against <paramref name="sourceCulture"/>.
    /// Returns findings in a stable order so CI output diffs cleanly.
    /// </summary>
    public static IReadOnlyList<CatalogIssue> Validate(IEnumerable<StringCatalog> catalogs, CultureInfo sourceCulture)
    {
        ArgumentNullException.ThrowIfNull(catalogs);
        ArgumentNullException.ThrowIfNull(sourceCulture);

        StringCatalog[] all = catalogs.OrderBy(c => c.Culture.Name, StringComparer.Ordinal).ToArray();
        StringCatalog? source = all.FirstOrDefault(c =>
            string.Equals(c.Culture.Name, sourceCulture.Name, StringComparison.OrdinalIgnoreCase));

        if (source is null)
            return new[] { new CatalogIssue(CatalogIssueKind.MissingKey, sourceCulture.Name, "*", "The source catalog is missing entirely.") };

        var issues = new List<CatalogIssue>();

        foreach (StringCatalog catalog in all)
        {
            if (ReferenceEquals(catalog, source))
            {
                ValidateSelf(catalog, issues);
                continue;
            }

            ValidateAgainstSource(catalog, source, issues);
        }

        return issues
            .OrderBy(i => i.Culture, StringComparer.Ordinal)
            .ThenBy(i => i.Kind)
            .ThenBy(i => i.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidateSelf(StringCatalog source, List<CatalogIssue> issues)
    {
        foreach (var (key, entry) in source.Entries)
        {
            if (entry.AllTemplates().All(string.IsNullOrWhiteSpace))
                issues.Add(new CatalogIssue(CatalogIssueKind.EmptyValue, source.Culture.Name, key, "Entry is empty."));
        }
    }

    private static void ValidateAgainstSource(StringCatalog catalog, StringCatalog source, List<CatalogIssue> issues)
    {
        IReadOnlyList<PluralCategory> required = PluralRules.RequiredCategories(catalog.Culture);

        foreach (var (key, sourceEntry) in source.Entries)
        {
            if (!catalog.TryGet(key, out CatalogEntry? translated))
            {
                issues.Add(new CatalogIssue(CatalogIssueKind.MissingKey, catalog.Culture.Name, key, "Not translated."));
                continue;
            }

            if (translated.AllTemplates().All(string.IsNullOrWhiteSpace))
            {
                issues.Add(new CatalogIssue(CatalogIssueKind.EmptyValue, catalog.Culture.Name, key, "Translation is empty."));
                continue;
            }

            // Placeholders: every name in the source must appear in the translation. Extra names
            // are also wrong — the caller passes exactly the source's argument set.
            var sourcePlaceholders = new HashSet<string>(
                sourceEntry.AllTemplates().SelectMany(MessageTemplate.Placeholders), StringComparer.Ordinal);
            var translatedPlaceholders = new HashSet<string>(
                translated.AllTemplates().SelectMany(MessageTemplate.Placeholders), StringComparer.Ordinal);

            foreach (string missing in sourcePlaceholders.Except(translatedPlaceholders).OrderBy(p => p, StringComparer.Ordinal))
            {
                issues.Add(new CatalogIssue(CatalogIssueKind.PlaceholderMismatch, catalog.Culture.Name, key,
                    $"Translation drops placeholder {{{missing}}}."));
            }

            foreach (string extra in translatedPlaceholders.Except(sourcePlaceholders).OrderBy(p => p, StringComparer.Ordinal))
            {
                issues.Add(new CatalogIssue(CatalogIssueKind.PlaceholderMismatch, catalog.Culture.Name, key,
                    $"Translation uses placeholder {{{extra}}} that the source does not supply."));
            }

            if (sourceEntry.IsPlural)
            {
                if (!translated.IsPlural)
                {
                    // Flattening a plural entry to a single string loses grammatical number: the
                    // text is present, so nothing looks broken until a count other than 1 arrives.
                    issues.Add(new CatalogIssue(CatalogIssueKind.MissingPluralForm, catalog.Culture.Name, key,
                        "Source uses plural forms but the translation is a single string."));
                }
                else
                {
                    foreach (PluralCategory category in required)
                    {
                        if (!translated.Forms.ContainsKey(category))
                        {
                            issues.Add(new CatalogIssue(CatalogIssueKind.MissingPluralForm, catalog.Culture.Name, key,
                                $"Missing the '{category}' plural form required by {catalog.Culture.Name}."));
                        }
                    }
                }
            }
        }

        foreach (var (key, _) in catalog.Entries)
        {
            if (!source.Contains(key))
            {
                issues.Add(new CatalogIssue(CatalogIssueKind.OrphanTranslation, catalog.Culture.Name, key,
                    "Key does not exist in the source catalog."));
            }
        }
    }
}
