using System.Text;

namespace Gloam.Localization;

/// <summary>
/// Minimal message template syntax: <c>{name}</c> placeholders, <c>{{</c> and <c>}}</c> as
/// literal braces.
/// </summary>
/// <remarks>
/// Deliberately not ICU MessageFormat. ICU brings plural selectors, nested selects and a parser
/// the size of this whole library, and the catalog already expresses plurals as separate forms —
/// the one ICU feature this app needs. Named placeholders are what make translators' lives
/// bearable: word order can change without the code knowing, which is the entire reason
/// positional <c>{0}</c> strings are a bad idea in a translated UI.
/// </remarks>
public static class MessageTemplate
{
    /// <summary>
    /// Lists the placeholder names in <paramref name="template"/>, in first-appearance order and
    /// without duplicates. Used by the validator to compare a translation against its source.
    /// </summary>
    public static IReadOnlyList<string> Placeholders(string template)
    {
        if (string.IsNullOrEmpty(template))
            return Array.Empty<string>();

        var found = new List<string>();
        int i = 0;
        while (i < template.Length)
        {
            if (TryReadToken(template, i, out string? name, out int next))
            {
                if (name is not null && !found.Contains(name, StringComparer.Ordinal))
                    found.Add(name);
                i = next;
                continue;
            }

            i++;
        }

        return found;
    }

    /// <summary>
    /// Renders <paramref name="template"/>, asking <paramref name="resolve"/> for each placeholder.
    /// Unresolved placeholders are left in place, verbatim, and reported through
    /// <paramref name="unresolved"/>.
    /// </summary>
    /// <remarks>
    /// Leaving the token visible is intentional: a UI that shows <c>{deviceName}</c> is obviously
    /// broken and gets fixed, whereas a UI that silently drops it reads as if the feature were
    /// designed that way. The token also names the exact binding that is missing.
    /// </remarks>
    public static string Render(string template, Func<string, string?> resolve, out IReadOnlyList<string> unresolved)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(resolve);

        List<string>? missing = null;
        var sb = new StringBuilder(template.Length + 16);
        int i = 0;

        while (i < template.Length)
        {
            char c = template[i];

            // Literal braces first: "{{" must not be read as an empty placeholder.
            if (c == '{' && i + 1 < template.Length && template[i + 1] == '{')
            {
                sb.Append('{');
                i += 2;
                continue;
            }

            if (c == '}' && i + 1 < template.Length && template[i + 1] == '}')
            {
                sb.Append('}');
                i += 2;
                continue;
            }

            if (TryReadToken(template, i, out string? name, out int next) && name is not null)
            {
                string? value = resolve(name);
                if (value is null)
                {
                    (missing ??= new List<string>()).Add(name);
                    sb.Append(template, i, next - i);
                }
                else
                {
                    sb.Append(value);
                }

                i = next;
                continue;
            }

            sb.Append(c);
            i++;
        }

        unresolved = missing ?? (IReadOnlyList<string>)Array.Empty<string>();
        return sb.ToString();
    }

    /// <summary>
    /// Reads a <c>{name}</c> token at <paramref name="start"/>. Returns false when the text there
    /// is not a well-formed token, in which case the caller emits the character literally.
    /// </summary>
    private static bool TryReadToken(string template, int start, out string? name, out int next)
    {
        name = null;
        next = start;

        if (template[start] != '{')
            return false;

        int close = template.IndexOf('}', start + 1);
        if (close < 0)
            return false;

        ReadOnlySpan<char> inner = template.AsSpan(start + 1, close - start - 1);
        if (inner.IsEmpty || inner.Length > 64)
            return false;

        // Names are identifiers: letters, digits, underscore; must not start with a digit.
        if (!char.IsLetter(inner[0]) && inner[0] != '_')
            return false;

        foreach (char c in inner)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
                return false;
        }

        name = inner.ToString();
        next = close + 1;
        return true;
    }
}
