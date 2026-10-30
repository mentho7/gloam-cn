using System.Collections.Immutable;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Gloam.Localization;

/// <summary>A catalog file that could not be read. Carries the origin so the message names the file.</summary>
public sealed class CatalogFormatException : Exception
{
    public CatalogFormatException(string message, string? origin = null, Exception? inner = null)
        : base(origin is null ? message : $"{message} ({origin})", inner)
    {
        Origin = origin;
    }

    public string? Origin { get; }
}

/// <summary>
/// Reads and writes the catalog wire format.
/// </summary>
/// <remarks>
/// JSON rather than .resx on purpose. Resx needs a build step, a designer file and a rebuild per
/// language, and its diff is XML noise that no translator will touch. A JSON catalog can be
/// edited by hand, reviewed as text, hot-loaded from disk, and validated in CI — which is what
/// makes community translations practical.
///
/// Parsing is deliberately lenient about things that only help humans (comments, trailing
/// commas) and strict about things that would fail silently (unknown plural forms, missing
/// culture tag).
/// </remarks>
public static class CatalogJson
{
    private const string CultureProperty = "culture";
    private const string StringsProperty = "strings";

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 32,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        // Catalog text is mostly non-ASCII; escaping it to \uXXXX makes review diff unreadable.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static StringCatalog Parse(string json, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, DocumentOptions);
        }
        catch (JsonException ex)
        {
            throw new CatalogFormatException($"Catalog is not valid JSON: {ex.Message}", origin, ex);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new CatalogFormatException("Catalog root must be an object.", origin);

            if (!root.TryGetProperty(CultureProperty, out JsonElement cultureElement) ||
                cultureElement.ValueKind != JsonValueKind.String)
            {
                throw new CatalogFormatException($"Catalog is missing the \"{CultureProperty}\" string.", origin);
            }

            string? cultureTag = cultureElement.GetString();
            CultureInfo? culture = CultureChain.TryParse(cultureTag);
            if (culture is null)
                throw new CatalogFormatException($"Catalog declares an unusable culture \"{cultureTag}\".", origin);

            if (!root.TryGetProperty(StringsProperty, out JsonElement stringsElement) ||
                stringsElement.ValueKind != JsonValueKind.Object)
            {
                throw new CatalogFormatException($"Catalog is missing the \"{StringsProperty}\" object.", origin);
            }

            var builder = ImmutableDictionary.CreateBuilder<string, CatalogEntry>(StringComparer.Ordinal);
            foreach (JsonProperty property in stringsElement.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(property.Name))
                    throw new CatalogFormatException("Catalog contains an empty key.", origin);

                // Later duplicates win here; CatalogValidator reports them separately, because a
                // duplicate in a merged file is a translation bug worth seeing, not a crash.
                builder[property.Name] = ReadEntry(property.Value, property.Name, origin);
            }

            return new StringCatalog(culture, builder.ToImmutable(), origin);
        }
    }

    public static StringCatalog ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            throw new CatalogFormatException($"Catalog could not be read: {ex.Message}", path, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new CatalogFormatException($"Catalog could not be read: {ex.Message}", path, ex);
        }

        return Parse(json, path);
    }

    /// <summary>Writes a catalog back to the wire format. Used by tooling and by the round-trip test.</summary>
    public static string Serialize(StringCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var strings = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var (key, entry) in catalog.Entries)
        {
            if (!entry.IsPlural)
            {
                strings[key] = entry.Template;
                continue;
            }

            var forms = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var (category, template) in entry.Forms)
                forms[CategoryName(category)] = template;

            strings[key] = forms;
        }

        var root = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["$comment"] = "Gloam translation catalog. Placeholders use {name} syntax; plural entries use CLDR categories.",
            [CultureProperty] = catalog.Culture.Name,
            [StringsProperty] = strings,
        };

        return JsonSerializer.Serialize(root, WriteOptions);
    }

    private static CatalogEntry ReadEntry(JsonElement value, string key, string? origin)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return CatalogEntry.Simple(value.GetString() ?? string.Empty);

            case JsonValueKind.Object:
            {
                var forms = ImmutableDictionary.CreateBuilder<PluralCategory, string>();
                foreach (JsonProperty form in value.EnumerateObject())
                {
                    PluralCategory category = ParseCategory(form.Name, key, origin);
                    if (form.Value.ValueKind != JsonValueKind.String)
                    {
                        throw new CatalogFormatException(
                            $"Key \"{key}\" plural form \"{form.Name}\" must be a string.", origin);
                    }

                    forms[category] = form.Value.GetString() ?? string.Empty;
                }

                if (forms.Count == 0)
                    throw new CatalogFormatException($"Key \"{key}\" has an empty plural object.", origin);

                return CatalogEntry.Plural(forms.ToImmutable());
            }

            default:
                throw new CatalogFormatException(
                    $"Key \"{key}\" must be a string or a plural object, found {value.ValueKind}.", origin);
        }
    }

    private static PluralCategory ParseCategory(string name, string key, string? origin) => name switch
    {
        "zero" => PluralCategory.Zero,
        "one" => PluralCategory.One,
        "two" => PluralCategory.Two,
        "few" => PluralCategory.Few,
        "many" => PluralCategory.Many,
        "other" => PluralCategory.Other,
        _ => throw new CatalogFormatException(
            $"Key \"{key}\" has unknown plural form \"{name}\"; expected one of zero/one/two/few/many/other.", origin),
    };

    private static string CategoryName(PluralCategory category) => category switch
    {
        PluralCategory.Zero => "zero",
        PluralCategory.One => "one",
        PluralCategory.Two => "two",
        PluralCategory.Few => "few",
        PluralCategory.Many => "many",
        _ => "other",
    };
}
