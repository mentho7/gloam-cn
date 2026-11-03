using System.Globalization;

namespace Gloam.Localization;

/// <summary>
/// The default <see cref="ILocalizer"/>: resolves through an ordered catalog chain, renders
/// <c>{name}</c> templates, and records misses.
/// </summary>
/// <remarks>
/// Threading model: the catalog chain is a single reference swapped on culture change, so readers
/// never lock. Everything else is either immutable data or a concurrent counter.
/// </remarks>
public sealed class Localizer : ILocalizer
{
    private readonly LocalizationOptions _options;
    private readonly StringCatalogSet _catalogs;
    private readonly object _chainLock = new();

    private IReadOnlyList<StringCatalog> _chain;
    private CultureInfo _culture;

    public Localizer(StringCatalogSet catalogs, LocalizationOptions? options = null, CultureInfo? culture = null)
    {
        _catalogs = catalogs ?? throw new ArgumentNullException(nameof(catalogs));
        _options = options ?? new LocalizationOptions();

        if (!_options.SourceCulture.Equals(CultureChain.SourceCulture) &&
            !_catalogs.Contains(_options.SourceCulture.Name))
        {
            // Authoring language with no catalog is a setup bug, but throwing would make the app
            // unusable rather than merely untranslated. Fall back to en-US and keep going.
            _options.SourceCulture = CultureChain.SourceCulture;
        }

        CultureInfo initial = culture ?? CultureInfo.CurrentUICulture;
        _culture = ResolveUsableCulture(initial);
        _chain = _catalogs.Resolve(_culture, _options.SourceCulture);
        MissingKeys = new MissingKeyReport(Math.Max(1, _options.MaxTrackedMissingKeys));
    }

    /// <summary>Builds a localizer straight from sources.</summary>
    public static Localizer Load(LocalizationOptions options, params ICatalogSource[] sources)
        => new(StringCatalogSet.Load(sources), options);

    public CultureInfo Culture => Volatile.Read(ref _culture);

    public MissingKeyReport MissingKeys { get; }

    public IReadOnlyList<CultureInfo> AvailableCultures => _catalogs.Catalogs
        .Select(c => c.Culture)
        .OrderBy(c => c.DisplayName, StringComparer.CurrentCulture)
        .ToArray();

    public event EventHandler<CultureChangedEventArgs>? CultureChanged;

    public string Get(string key)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;

        if (TryResolve(key, out CatalogEntry? entry))
            return entry.SelectTemplate(null);

        MissingKeys.Record(key);
        return _options.MissingKeyBehavior == MissingKeyBehavior.ReturnEmpty ? string.Empty : key;
    }

    public string Format(string key, params (string Name, object? Value)[] arguments)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;

        if (!TryResolve(key, out CatalogEntry? entry))
        {
            MissingKeys.Record(key);
            return _options.MissingKeyBehavior == MissingKeyBehavior.ReturnEmpty ? string.Empty : key;
        }

        PluralCategory? category = entry.IsPlural
            ? PluralRules.Select(Culture, ExtractCount(arguments))
            : null;

        return Render(entry.SelectTemplate(category), arguments);
    }

    public string Plural(string key, double count, params (string Name, object? Value)[] arguments)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;

        if (!TryResolve(key, out CatalogEntry? entry))
        {
            MissingKeys.Record(key);
            return _options.MissingKeyBehavior == MissingKeyBehavior.ReturnEmpty ? string.Empty : key;
        }

        // The explicit count wins over whatever the argument list happens to hold, so a caller
        // can pluralise on a value it does not also interpolate.
        PluralCategory category = PluralRules.Select(Culture, count);
        return Render(entry.SelectTemplate(category), arguments);
    }

    public bool TryGet(string key, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrEmpty(key) || !TryResolve(key, out CatalogEntry? entry))
            return false;

        value = entry.SelectTemplate(null);
        return true;
    }

    public void SetCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        CultureInfo previous = Culture;
        CultureInfo usable = ResolveUsableCulture(culture);
        if (string.Equals(previous.Name, usable.Name, StringComparison.OrdinalIgnoreCase))
            return;

        // Rebuild before publishing so a concurrent Get never sees the new culture with the old chain.
        IReadOnlyList<StringCatalog> chain = _catalogs.Resolve(usable, _options.SourceCulture);

        lock (_chainLock)
        {
            Volatile.Write(ref _culture, usable);
            Volatile.Write(ref _chain, chain);
        }

        // Misses are per-culture signal: keys absent in zh-Hans are not the same problem as keys
        // absent in en-US, and carrying the old set over would hide the ones that matter.
        MissingKeys.Clear();

        CultureChanged?.Invoke(this, new CultureChangedEventArgs(previous, usable));
    }

    /// <summary>
    /// Picks a culture this localizer can actually serve: the request if a catalog exists for it
    /// or for a language-matching one, otherwise the source culture.
    /// </summary>
    private CultureInfo ResolveUsableCulture(CultureInfo requested)
    {
        if (_catalogs.Resolve(requested, _options.SourceCulture).Any(c =>
                string.Equals(c.Culture.Name, requested.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return requested;
        }

        // No catalog for the exact tag; a same-language catalog is close enough to present.
        string language = requested.TwoLetterISOLanguageName;
        StringCatalog? sameLanguage = _catalogs.Catalogs.FirstOrDefault(c =>
            string.Equals(c.Culture.TwoLetterISOLanguageName, language, StringComparison.OrdinalIgnoreCase));

        return sameLanguage?.Culture ?? _options.SourceCulture;
    }

    private bool TryResolve(string key, out CatalogEntry entry)
    {
        // Read the chain reference once: a culture switch mid-lookup must not mix chains.
        IReadOnlyList<StringCatalog> chain = Volatile.Read(ref _chain);
        for (int i = 0; i < chain.Count; i++)
        {
            if (chain[i].TryGet(key, out CatalogEntry? found))
            {
                entry = found;
                return true;
            }
        }

        entry = null!;
        return false;
    }

    private string Render(string template, (string Name, object? Value)[] arguments)
    {
        // Rendered even with no arguments: "{{" / "}}" escapes must be unwrapped either way,
        // and a template whose placeholder the caller forgot to pass should stay visible.

        CultureInfo culture = Culture;
        string? Resolve(string name)
        {
            for (int i = 0; i < arguments.Length; i++)
            {
                if (string.Equals(arguments[i].Name, name, StringComparison.Ordinal))
                    return FormatValue(arguments[i].Value, culture);
            }

            return null;
        }

        return MessageTemplate.Render(template, Resolve, out _);
    }

    private static string? FormatValue(object? value, CultureInfo culture) => value switch
    {
        null => null,
        string s => s,
        IFormattable formattable => formattable.ToString(null, culture),
        _ => value.ToString(),
    };

    private static double ExtractCount((string Name, object? Value)[] arguments)
    {
        for (int i = 0; i < arguments.Length; i++)
        {
            if (!string.Equals(arguments[i].Name, "count", StringComparison.Ordinal))
                continue;

            return arguments[i].Value switch
            {
                null => 0d,
                double d => d,
                int n => n,
                long l => l,
                decimal m => (double)m,
                IConvertible convertible => convertible.ToDouble(CultureInfo.InvariantCulture),
                _ => 0d,
            };
        }

        return 0d;
    }
}
