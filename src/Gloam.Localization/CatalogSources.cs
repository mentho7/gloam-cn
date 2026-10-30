using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;

namespace Gloam.Localization;

/// <summary>
/// Where catalogs come from. The seam exists so shipping translations is not tied to shipping a
/// binary: embedded resources for the default set, a watched folder for overrides, and whatever
/// a host app wants next (a database, a remote bundle) without touching the localizer.
/// </summary>
public interface ICatalogSource
{
    /// <summary>Human-readable origin, used in diagnostics and error messages.</summary>
    string Description { get; }

    /// <summary>Reads every catalog this source can currently provide.</summary>
    /// <exception cref="CatalogFormatException">A catalog exists but is malformed.</exception>
    IReadOnlyList<StringCatalog> Load();
}

/// <summary>Catalogs compiled into an assembly as embedded resources.</summary>
public sealed class EmbeddedCatalogSource : ICatalogSource
{
    private readonly Assembly _assembly;
    private readonly string _prefix;

    /// <param name="assembly">Assembly holding the resources.</param>
    /// <param name="resourceNamePrefix">
    /// Resource-name prefix to scan, e.g. <c>Gloam.Resources.i18n.</c>. All <c>*.json</c> resources
    /// under it are loaded.
    /// </param>
    public EmbeddedCatalogSource(Assembly assembly, string resourceNamePrefix)
    {
        _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
        _prefix = resourceNamePrefix ?? throw new ArgumentNullException(nameof(resourceNamePrefix));
    }

    public string Description => $"embedded resources {_prefix}* in {_assembly.GetName().Name}";

    public IReadOnlyList<StringCatalog> Load()
    {
        var catalogs = new List<StringCatalog>();
        foreach (string name in _assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(_prefix, StringComparison.Ordinal) ||
                !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using Stream? stream = _assembly.GetManifestResourceStream(name);
            if (stream is null)
                continue;

            using var reader = new StreamReader(stream);
            catalogs.Add(CatalogJson.Parse(reader.ReadToEnd(), name));
        }

        return catalogs;
    }
}

/// <summary>
/// Catalogs read from a folder of <c>*.json</c> files, e.g. <c>%LOCALAPPDATA%\Gloam\i18n</c>.
/// </summary>
/// <remarks>
/// A missing folder yields nothing rather than throwing: the folder is optional by design. A
/// malformed file is skipped and surfaced through <see cref="Errors"/> so one bad hand-edited
/// file cannot take the app down.
/// </remarks>
public sealed class DirectoryCatalogSource : ICatalogSource
{
    private readonly string _directory;

    public DirectoryCatalogSource(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public string Description => $"folder {_directory}";

    /// <summary>Files that were present but unreadable, with the reason.</summary>
    public IReadOnlyList<string> Errors { get; private set; } = Array.Empty<string>();

    public IReadOnlyList<StringCatalog> Load()
    {
        var errors = new List<string>();
        var catalogs = new List<StringCatalog>();

        if (!Directory.Exists(_directory))
        {
            Errors = errors;
            return catalogs;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(_directory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add($"{_directory}: {ex.Message}");
            Errors = errors;
            return catalogs;
        }

        foreach (string file in files)
        {
            try
            {
                catalogs.Add(CatalogJson.ParseFile(file));
            }
            catch (CatalogFormatException ex)
            {
                errors.Add(ex.Message);
            }
        }

        Errors = errors;
        return catalogs;
    }
}

/// <summary>In-memory catalogs. Test support, and the escape hatch for host-provided bundles.</summary>
public sealed class InMemoryCatalogSource : ICatalogSource
{
    private readonly List<StringCatalog> _catalogs;

    public InMemoryCatalogSource(params StringCatalog[] catalogs)
        => _catalogs = new List<StringCatalog>(catalogs ?? Array.Empty<StringCatalog>());

    public string Description => $"in-memory ({_catalogs.Count} catalogs)";

    public IReadOnlyList<StringCatalog> Load() => _catalogs;
}

/// <summary>Reads several sources in order. Later sources override earlier ones, key by key.</summary>
public sealed class CompositeCatalogSource : ICatalogSource
{
    private readonly IReadOnlyList<ICatalogSource> _sources;

    public CompositeCatalogSource(params ICatalogSource[] sources)
        => _sources = sources ?? Array.Empty<ICatalogSource>();

    public string Description => string.Join(" + ", _sources.Select(s => s.Description));

    public IReadOnlyList<StringCatalog> Load()
        => _sources.SelectMany(s => s.Load()).ToArray();
}

/// <summary>
/// The merged catalog set, indexed by culture. Immutable once built, so a request that starts
/// resolving against it cannot observe a partially merged language.
/// </summary>
public sealed class StringCatalogSet
{
    private readonly Dictionary<string, StringCatalog> _byCulture;

    private StringCatalogSet(Dictionary<string, StringCatalog> byCulture, IReadOnlyList<string> loadErrors)
    {
        _byCulture = byCulture;
        LoadErrors = loadErrors;
    }

    /// <summary>Problems encountered while loading, non-fatal. Surfaced in the diagnostics export.</summary>
    public IReadOnlyList<string> LoadErrors { get; }

    public IReadOnlyCollection<StringCatalog> Catalogs => _byCulture.Values;

    public static StringCatalogSet Load(params ICatalogSource[] sources) => Load((IEnumerable<ICatalogSource>)sources);

    public static StringCatalogSet Load(IEnumerable<ICatalogSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        // Ordered so a later source wins per key: the merge below inserts into the same entry
        // dictionary, and the last write for a key is the one kept.
        var merged = new Dictionary<string, Dictionary<string, CatalogEntry>>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        foreach (ICatalogSource source in sources)
        {
            IReadOnlyList<StringCatalog> catalogs;
            try
            {
                catalogs = source.Load();
            }
            catch (CatalogFormatException ex)
            {
                errors.Add(ex.Message);
                continue;
            }

            foreach (StringCatalog catalog in catalogs)
            {
                if (!merged.TryGetValue(catalog.Culture.Name, out var entries))
                {
                    entries = new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);
                    merged[catalog.Culture.Name] = entries;
                }

                foreach (var (key, entry) in catalog.Entries)
                    entries[key] = entry;
            }

            if (source is DirectoryCatalogSource directory)
                errors.AddRange(directory.Errors);
        }

        var byCulture = new Dictionary<string, StringCatalog>(StringComparer.OrdinalIgnoreCase);
        foreach (var (cultureName, entries) in merged)
        {
            CultureInfo culture = CultureChain.TryParse(cultureName) ?? CultureChain.SourceCulture;
            byCulture[cultureName] = new StringCatalog(culture, entries.ToImmutableDictionary(StringComparer.Ordinal));
        }

        return new StringCatalogSet(byCulture, errors);
    }

    public bool Contains(string cultureName) => _byCulture.ContainsKey(cultureName);

    public StringCatalog? Get(string cultureName)
        => _byCulture.TryGetValue(cultureName, out StringCatalog? catalog) ? catalog : null;

    /// <summary>
    /// The ordered catalogs that may serve <paramref name="requested"/>: the culture chain first,
    /// then any catalog sharing the requested language.
    /// </summary>
    /// <remarks>
    /// The last step matters in practice. Windows reports <c>zh-CN</c>; a translator may ship only
    /// <c>zh-Hans</c>. Their language matches, so the Chinese catalog serves the request instead of
    /// the UI dropping to English.
    /// </remarks>
    public IReadOnlyList<StringCatalog> Resolve(CultureInfo requested, CultureInfo sourceCulture)
    {
        ArgumentNullException.ThrowIfNull(requested);

        var chain = new List<StringCatalog>(4);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (CultureInfo candidate in CultureChain.Build(requested, sourceCulture))
        {
            if (_byCulture.TryGetValue(candidate.Name, out StringCatalog? catalog) && seen.Add(catalog.Culture.Name))
                chain.Add(catalog);
        }

        string language = requested.TwoLetterISOLanguageName;
        foreach (StringCatalog catalog in _byCulture.Values)
        {
            if (string.Equals(catalog.Culture.TwoLetterISOLanguageName, language, StringComparison.OrdinalIgnoreCase) &&
                seen.Add(catalog.Culture.Name))
            {
                chain.Add(catalog);
            }
        }

        if (_byCulture.TryGetValue(sourceCulture.Name, out StringCatalog? source) && seen.Add(source.Culture.Name))
            chain.Add(source);

        return chain;
    }
}
