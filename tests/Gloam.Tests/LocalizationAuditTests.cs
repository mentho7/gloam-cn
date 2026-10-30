using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gloam.Localization;
using Xunit;

namespace Gloam.Tests;

/// <summary>
/// Keeps the localization foundation honest as the rest of the app is migrated onto it.
/// </summary>
/// <remarks>
/// Three audits, each covering a failure mode that no compiler catches:
///
/// 1. A XAML key that no catalog defines renders as <c>settings.language.heading</c> in front of
///    the user.
/// 2. A catalog key nobody references is dead weight every translator still has to translate.
/// 3. A hard-coded string added after the migration started silently grows the backlog again.
///    The baseline ratchet allows the number to fall, never rise.
/// </remarks>
public partial class LocalizationAuditTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private const string BaselineRelativePath = "tests/Gloam.Tests/Fixtures/localization/hardcoded-baseline.json";

    private static readonly JsonSerializerOptions BaselineJsonOptions = new() { WriteIndented = true };

    /// <summary>Every source file that renders text: XAML plus the code-behind and view models.</summary>
    private static IEnumerable<string> SourceFiles()
    {
        foreach (string file in Directory.EnumerateFiles(Path.Combine(RepoRoot, "src", "Gloam"), "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            string extension = Path.GetExtension(file);
            if (extension is ".xaml" or ".cs")
                yield return file;
        }
    }

    /// <summary>Every hand-written source file under src/, across all projects.</summary>
    private static IEnumerable<string> AllSourceFiles()
    {
        foreach (string file in Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            string extension = Path.GetExtension(file);
            if (extension is ".xaml" or ".cs")
                yield return file;
        }
    }

    private static string Relative(string absolute) =>
        Path.GetRelativePath(RepoRoot, absolute).Replace('\\', '/');

    [Fact]
    public void Every_key_referenced_from_xaml_exists_in_the_source_catalog()
    {
        IReadOnlyList<StringCatalog> catalogs = ShippedCatalogs.Load();
        StringCatalog source = catalogs.Single(c => c.Culture.Name == "en-US");

        var missing = new List<string>();
        foreach (string file in SourceFiles())
        {
            if (!file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (string key in LocalizationSourceAudit.ExtractReferencedKeys(File.ReadAllText(file)))
            {
                if (!source.Contains(key))
                    missing.Add($"{Relative(file)}: {key}");
            }
        }

        Assert.True(missing.Count == 0,
            "XAML references keys the en-US catalog does not define:\n  " + string.Join("\n  ", missing));
    }

    /// <summary>
    /// Keys whose translation is legitimately identical to the source.
    /// </summary>
    /// <remarks>
    /// Brand names, product names, industry specification names and unit symbols read the same in
    /// every language. Listing them by key - rather than by "looks like English" heuristics - means
    /// a new entry that copies the source has to be added here deliberately, with a reason.
    /// </remarks>
    private static readonly HashSet<string> UntranslatableByDesign = new(StringComparer.Ordinal)
    {
        "tray.tooltip",              // brand + version number
        "dashboard.gameLab.title",   // product feature name
        "settings.gamma.v22",        // standard curve designation
        "settings.gamma.v24",
        "calibration.ccfl.lcd",      // backlight technology
        "calibration.argyllcms",     // third-party tool name
        "calibration.dci.p3.d65",    // colour space + white point
        "calibration.e2000",         // metric symbol
        "calibration.k",             // unit: kelvin
        "calibration.cd.m2",         // unit: candela per square metre
    };

    [Fact]
    public void Every_translation_differs_from_the_source_unless_it_cannot_be_translated()
    {
        IReadOnlyList<StringCatalog> catalogs = ShippedCatalogs.Load();
        StringCatalog source = catalogs.First(c => c.Culture.TwoLetterISOLanguageName == "en");
        var copied = new List<string>();

        foreach (StringCatalog catalog in catalogs)
        {
            if (ReferenceEquals(catalog, source))
                continue;

            foreach (var (key, entry) in catalog.Entries)
            {
                if (entry.IsPlural || UntranslatableByDesign.Contains(key))
                    continue;

                if (!source.TryGet(key, out CatalogEntry? sourceEntry) || sourceEntry is null ||
                    sourceEntry.IsPlural)
                {
                    continue;
                }

                if (string.Equals(sourceEntry.Template, entry.Template, StringComparison.Ordinal))
                    copied.Add($"{catalog.Culture.Name}: {key}");
            }
        }

        Assert.True(copied.Count == 0,
            "Translated catalogs contain entries identical to the English source. Either translate " +
            "them or add them to UntranslatableByDesign with a reason://n  " +
            string.Join("\n  ", copied));
    }

    [Fact]
    public void Every_catalog_key_is_referenced_somewhere_in_source()
    {
        // Two spellings count as a reference: a XAML markup extension ({loc:Tr some.key}) and a
        // quoted literal anywhere in source (a view model calling Loc.T("some.key")). Scanning
        // all of src/ rather than only the UI project matters because the localization adapter
        // holds keys too (the "follow Windows" label).
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in AllSourceFiles())
        {
            string text = File.ReadAllText(file);
            foreach (string key in LocalizationSourceAudit.ExtractReferencedKeys(text))
                referenced.Add(key);
            foreach (Match match in DottedKeyLiteral().Matches(text))
                referenced.Add(match.Groups["key"].Value);
        }

        var unused = new List<string>();
        foreach (StringCatalog catalog in ShippedCatalogs.Load())
        {
            foreach (var (key, _) in catalog.Entries)
            {
                if (!referenced.Contains(key))
                    unused.Add($"{catalog.Culture.Name}: {key}");
            }
        }

        Assert.True(unused.Count == 0,
            "Catalog keys no source file references (delete them or wire them up):\n  " + string.Join("\n  ", unused));
    }

    [Fact]
    public void Hardcoded_user_visible_strings_do_not_increase()
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (string file in SourceFiles())
        {
            string text = File.ReadAllText(file);
            string relative = Relative(file);
            SourceAuditResult audit = file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                ? LocalizationSourceAudit.AuditXaml(text, relative)
                : LocalizationSourceAudit.AuditCSharp(text, relative);

            if (audit.Hardcoded.Count > 0)
                counts[relative] = audit.Hardcoded.Count;
        }

        string baselinePath = Path.Combine(RepoRoot, BaselineRelativePath);

        // Regeneration is an explicit act: `GLOAM_WRITE_LOCALIZATION_BASELINE=1 dotnet test`.
        if (Environment.GetEnvironmentVariable("GLOAM_WRITE_LOCALIZATION_BASELINE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
            File.WriteAllText(baselinePath, JsonSerializer.Serialize(
                new SortedDictionary<string, int>(counts, StringComparer.Ordinal),
                BaselineJsonOptions));
            return;
        }

        Assert.True(File.Exists(baselinePath),
            $"Missing {BaselineRelativePath}. Generate it with GLOAM_WRITE_LOCALIZATION_BASELINE=1 dotnet test.");

        var baseline = JsonSerializer.Deserialize<SortedDictionary<string, int>>(File.ReadAllText(baselinePath))
                       ?? new SortedDictionary<string, int>(StringComparer.Ordinal);

        var regressions = new List<string>();
        foreach (var (file, count) in counts)
        {
            int allowed = baseline.TryGetValue(file, out int value) ? value : 0;
            if (count > allowed)
                regressions.Add($"{file}: {count} hard-coded strings, baseline allows {allowed}");
        }

        Assert.True(regressions.Count == 0,
            "This change adds user-visible text that bypasses the localization layer. Move it into a catalog:\n  "
            + string.Join("\n  ", regressions));

    }

    /// <summary>Test files and anything else that spells a catalog key as a literal.</summary>
    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "Gloam.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }

    [GeneratedRegex(@"""(?<key>[a-z][a-zA-Z0-9]*(\.[a-zA-Z0-9]+)+)""")]
    private static partial Regex DottedKeyLiteral();
}
