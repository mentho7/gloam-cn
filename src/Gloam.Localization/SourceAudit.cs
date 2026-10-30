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
        int logDepth = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            // Log calls are frequently wrapped over several lines, with the message literal on a
            // continuation line that mentions neither "Log" nor the method name. Line-local checks
            // then counted developer-facing text as translatable - "TrayViewModel: update {v}
            // downloaded..." was reported as user-visible copy. Track the call instead: when a line
            // opens a log call that it does not close, keep skipping until the parentheses balance.
            if (logDepth > 0)
            {
                logDepth += Count(line, '(') - Count(line, ')');
                if (logDepth > 0)
                    continue;
            }
            else if (LogCall().IsMatch(line))
            {
                int depth = Count(line, '(') - Count(line, ')');
                if (depth > 0)
                {
                    logDepth = depth;
                    continue;
                }

                continue;
            }

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

        // A catalog key, not prose. Without this the audit counts every key a migration *adds*
        // (Loc.Get("tray.openDashboard")) as hard-coded text, so migrating a file would raise its
        // score and the ratchet would punish exactly the work it exists to encourage.
        if (DottedIdentifier().IsMatch(value))
            return false;

        // Format or path-ish noise.
        if (value.Contains("://", StringComparison.Ordinal) ||
            value.StartsWith("M ", StringComparison.Ordinal) ||   // geometry data
            value.StartsWith("0x", StringComparison.Ordinal))
        {
            return false;
        }

        // Values that are data rather than prose. Each of these was found inflating the baseline
        // by a few dozen entries apiece: a hex colour, a WPF resource key (ThemeAccent), an
        // automation id (display-identify-close), a file name (srgb_to_gamma2p2_sdr.icm) or a
        // registry value (AppsUseLightTheme). None of them can ever be translated, so counting
        // them made the ratchet's number meaningless and its per-file numbers wrong.
        if (ColorLiteral().IsMatch(value) ||
            ResourceKey().IsMatch(value) ||
            AutomationId().IsMatch(value) ||
            FileName().IsMatch(value))
        {
            return false;
        }

        // A bare lowercase word is an identifier, not a label: the first element of a
        // ("language", value) template argument, a JSON property, a setting name. UI labels in
        // this codebase are capitalised ("Refresh"), so requiring a capital or a space keeps the
        // real ones and drops the plumbing.
        if (LowercaseWord().IsMatch(value))
            return false;

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

    private static int Count(string text, char needle)
    {
        int total = 0;
        foreach (char c in text)
        {
            if (c == needle)
                total++;
        }

        return total;
    }

    private static bool IsNonUserFacingLine(string line)
    {
        // Log calls carry developer-facing text: it goes to a file and into crash reports, where a
        // translated sentence would be actively unhelpful. Matching the call rather than the string
        // means a log line is excluded however the logger field happens to be named; the previous
        // check only recognised the literal "Log." and let "_log.Warn(...)" through.
        if (LogCall().IsMatch(line) ||
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

    /// <summary>Matches a logger call: <c>Log.Warn(</c>, <c>_log.Error(</c>, <c>Logger.Info(</c>.</summary>
    [GeneratedRegex(@"\b(?:Log|_log|_logger|Logger|Diag|Diagnostics)\s*\.\s*\w+\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex LogCall();

    /// <summary>Matches a #RGB / #RRGGBB colour literal.</summary>
    [GeneratedRegex(@"^#[0-9A-Fa-f]{3,8}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorLiteral();

    /// <summary>
    /// Matches a WPF resource key: <c>ThemeAccent</c>, <c>DarkCardBorder</c>, <c>SuccessBrush</c>.
    /// These name a brush or a style, so they appear in string form but are never shown as text.
    /// </summary>
    [GeneratedRegex(@"^(Theme|Dark|Light|Menu|Card|Input|Dropdown|Accent|Text|Success|Error|Warning)[A-Za-z]*(Brush|Background|Border|Foreground|Color|Key|Meter|Track|Hover|Surface|Dim|Text|Accent)?$", RegexOptions.CultureInvariant)]
    private static partial Regex ResourceKey();

    /// <summary>Matches an automation id: lower-case words joined by hyphens.</summary>
    [GeneratedRegex(@"^[a-z][a-z0-9]*(-[a-z0-9]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex AutomationId();

    /// <summary>Matches a file name whose extension marks it as data, not text.</summary>
    [GeneratedRegex(@"^[^\s]+\.(icm|json|csv|ccss|ccmx|log|exe|txt|xaml|png|ico)$", RegexOptions.CultureInvariant)]
    private static partial Regex FileName();

    /// <summary>Matches a single lowercase word: an identifier or argument name, never a label.</summary>
    [GeneratedRegex(@"^[a-z][a-zA-Z0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex LowercaseWord();

    /// <summary>
    /// Matches a lowercase dotted identifier: a catalog key (<c>tray.openDashboard</c>) or a file
    /// name (<c>settings.json</c>). Neither is user-facing prose.
    /// </summary>
    [GeneratedRegex(@"^[a-z][a-zA-Z0-9]*(\.[a-zA-Z0-9]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex DottedIdentifier();

    /// <summary>Matches a plain C# string literal without interpolation holes.</summary>
    [GeneratedRegex(@"""(?<value>[^""\\\r\n]{2,})""", RegexOptions.CultureInvariant)]
    private static partial Regex CSharpLiteral();
}
