using System.Text.RegularExpressions;

namespace Gloam.Localization;

/// <summary>A user-visible string still hard-coded in source.</summary>
public sealed record HardcodedString(string File, int Line, string Text);

/// <summary>Result of auditing one source file.</summary>
public sealed record SourceAuditResult(
    IReadOnlyList<string> ReferencedKeys,
    IReadOnlyList<HardcodedString> Hardcoded);

/// <summary>
/// Finds translation keys in use and user-visible strings that are still hard-coded.
/// </summary>
/// <remarks>
/// This is governance, not enforcement. Nobody migrates two thousand literals in one commit, so
/// the audit produces a baseline count per file that CI can ratchet down: a change may reduce the
/// number of hard-coded strings, never increase it. Without that, a migration stalls halfway and
/// the half that was migrated quietly regresses.
///
/// The heuristics are line-oriented and deliberately conservative. They report what is certainly
/// user-facing (a literal in a XAML <c>Text</c> attribute) and skip what they cannot judge rather
/// than producing noise people learn to ignore.
/// </remarks>
public static partial class LocalizationSourceAudit
{
    /// <summary>Attributes that render text to a user.</summary>
    private static readonly HashSet<string> TextAttributes = new(StringComparer.Ordinal)
    {
        "Text", "Content", "Header", "Title", "ToolTip", "Watermark", "Tag",
    };

    /// <summary>Extracts every key a file asks the localizer for.</summary>
    public static IReadOnlyList<string> ExtractReferencedKeys(string content)
    {
        if (string.IsNullOrEmpty(content))
            return Array.Empty<string>();

        var keys = new List<string>();
        foreach (Match match in TrReference().Matches(content))
        {
            string key = match.Groups["key"].Value;
            if (!keys.Contains(key, StringComparer.Ordinal))
                keys.Add(key);
        }

        return keys;
    }

    /// <summary>Audits XAML text.</summary>
    public static SourceAuditResult AuditXaml(string xaml, string file)
    {
        ArgumentNullException.ThrowIfNull(xaml);

        var hardcoded = new List<HardcodedString>();
        string[] lines = SplitLines(xaml);

        for (int i = 0; i < lines.Length; i++)
        {
            foreach (Match match in XamlTextAttribute().Matches(lines[i]))
            {
                string value = match.Groups["value"].Value.Trim();
                if (!LooksTranslatable(value))
                    continue;

                hardcoded.Add(new HardcodedString(file, i + 1, value));
            }
        }

        return new SourceAuditResult(ExtractReferencedKeys(xaml), hardcoded);
    }

    /// <summary>
    /// Audits C# text. Skips lines that log, since log text is developer-facing and should not be
    /// translated — translating it would also put user-language strings into crash reports.
    /// </summary>
    public static SourceAuditResult AuditCSharp(string code, string file)
    {
        ArgumentNullException.ThrowIfNull(code);

        var hardcoded = new List<HardcodedString>();
        string[] lines = SplitLines(code);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (IsNonUserFacingLine(line))
                continue;

            foreach (Match match in CSharpLiteral().Matches(line))
            {
                string value = match.Groups["value"].Value.Trim();
                if (!LooksTranslatable(value))
                    continue;

                hardcoded.Add(new HardcodedString(file, i + 1, value));
            }
        }

        return new SourceAuditResult(ExtractReferencedKeys(code), hardcoded);
    }

    /// <summary>
    /// A string is worth translating when it contains a letter and either a space or enough
    /// characters to be a label. Filters out units, keys, format specifiers and symbol runs.
    /// </summary>
    private static bool LooksTranslatable(string value)
    {
        if (value.Length < 4)
            return false;

        // Markup extension or binding, not literal text.
        if (value.StartsWith('{') || value.StartsWith("pack://", StringComparison.Ordinal))
            return false;

        // Format or path-ish noise.
        if (value.Contains("://", StringComparison.Ordinal) ||
            value.StartsWith("M ", StringComparison.Ordinal) ||   // geometry data
            value.StartsWith("0x", StringComparison.Ordinal))
        {
            return false;
        }

        bool hasLetter = false;
        bool hasSpace = false;
        foreach (char c in value)
        {
            if (char.IsLetter(c)) hasLetter = true;
            if (c == ' ') hasSpace = true;
        }

        if (!hasLetter)
            return false;

        // Single words that are identifiers/keys rather than prose: require whitespace, or a
        // value long enough that a bare word is still a label ("Refresh", "Calibration").
        return hasSpace || value.Length >= 6;
    }

    private static bool IsNonUserFacingLine(string line)
    {
        if (line.Contains("Log.", StringComparison.Ordinal) ||
            line.Contains("Debug.", StringComparison.Ordinal) ||
            line.Contains("Trace.", StringComparison.Ordinal) ||
            line.Contains("nameof(", StringComparison.Ordinal))
        {
            return true;
        }

        string trimmed = line.TrimStart();
        return trimmed.StartsWith("//", StringComparison.Ordinal) ||
               trimmed.StartsWith("///", StringComparison.Ordinal) ||
               trimmed.StartsWith('*');
    }

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    /// <summary>Matches <c>{loc:Tr Key}</c>, <c>{loc:Tr Key=Some.Key}</c> and any xmlns prefix.</summary>
    [GeneratedRegex(@"\{\s*\w+:Tr\s+(?:Key\s*=\s*)?(?<key>[A-Za-z0-9_][A-Za-z0-9_.\-]*)", RegexOptions.CultureInvariant)]
    private static partial Regex TrReference();

    /// <summary>Matches a text-bearing XAML attribute with a non-binding literal value.</summary>
    [GeneratedRegex(@"(?<attr>Text|Content|Header|Title|ToolTip|Watermark)\s*=\s*""(?<value>[^""{}]*)""", RegexOptions.CultureInvariant)]
    private static partial Regex XamlTextAttribute();

    /// <summary>Matches a plain C# string literal without interpolation holes.</summary>
    [GeneratedRegex(@"""(?<value>[^""\\\r\n]{2,})""", RegexOptions.CultureInvariant)]
    private static partial Regex CSharpLiteral();
}
