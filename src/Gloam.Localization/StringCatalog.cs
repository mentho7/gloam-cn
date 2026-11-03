using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Gloam.Localization;

/// <summary>CLDR plural categories. The set the CLDR data actually uses; not every culture uses every one.</summary>
public enum PluralCategory
{
    Zero,
    One,
    Two,
    Few,
    Many,
    Other,
}

/// <summary>
/// One translatable string. A catalog value is either a plain template or a set of plural
/// forms; both are templates, so <c>{count}</c> interpolation works in either shape.
/// </summary>
public sealed class CatalogEntry
{
    private readonly string? _template;
    private readonly ImmutableDictionary<PluralCategory, string>? _forms;

    private CatalogEntry(string? template, ImmutableDictionary<PluralCategory, string>? forms)
    {
        _template = template;
        _forms = forms;
    }

    /// <summary>True when this entry carries CLDR plural forms instead of a single template.</summary>
    public bool IsPlural => _forms is not null;

    /// <summary>The single template, or an empty string for a plural entry.</summary>
    public string Template => _template ?? string.Empty;

    /// <summary>Plural forms, empty for a non-plural entry.</summary>
    public IReadOnlyDictionary<PluralCategory, string> Forms =>
        _forms ?? ImmutableDictionary<PluralCategory, string>.Empty;

    public static CatalogEntry Simple(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return new CatalogEntry(template, null);
    }

    public static CatalogEntry Plural(ImmutableDictionary<PluralCategory, string> forms)
    {
        ArgumentNullException.ThrowIfNull(forms);
        if (forms.Count == 0)
            throw new ArgumentException("A plural entry needs at least one form.", nameof(forms));
        return new CatalogEntry(null, forms);
    }

    /// <summary>
    /// Picks the template to render. Plural entries fall back to <see cref="PluralCategory.Other"/>
    /// the way CLDR requires, so a catalog with only <c>other</c> is still valid.
    /// </summary>
    public string SelectTemplate(PluralCategory? category)
    {
        if (_forms is null)
            return _template ?? string.Empty;

        if (category is { } c && _forms.TryGetValue(c, out var exact))
            return exact;

        return _forms.TryGetValue(PluralCategory.Other, out var other)
            ? other
            : _forms.Values.First();
    }

    /// <summary>Every template this entry can render. Used by the validator to check placeholders.</summary>
    public IEnumerable<string> AllTemplates()
        => _forms is null ? new[] { _template ?? string.Empty } : _forms.Values;
}

/// <summary>
/// An immutable, fully loaded catalog for one culture. Immutability is what makes a language
/// switch safe: the localizer swaps a reference, and a reader that already grabbed the old
/// catalog keeps reading consistent data instead of a half-updated dictionary.
/// </summary>
public sealed class StringCatalog
{
    private readonly ImmutableDictionary<string, CatalogEntry> _entries;

    public StringCatalog(CultureInfo culture, ImmutableDictionary<string, CatalogEntry> entries, string? origin = null)
    {
        Culture = culture ?? throw new ArgumentNullException(nameof(culture));
        _entries = entries ?? throw new ArgumentNullException(nameof(entries));
        Origin = origin;
    }

    public CultureInfo Culture { get; }

    /// <summary>Where this catalog came from (file path, assembly resource). Diagnostics only.</summary>
    public string? Origin { get; }

    public int Count => _entries.Count;

    public IEnumerable<KeyValuePair<string, CatalogEntry>> Entries => _entries;

    public bool TryGet(string key, [NotNullWhen(true)] out CatalogEntry? entry)
        => _entries.TryGetValue(key, out entry);

    public bool Contains(string key) => _entries.ContainsKey(key);

    public static StringCatalog Empty(CultureInfo culture) =>
        new(culture, ImmutableDictionary<string, CatalogEntry>.Empty);
}
