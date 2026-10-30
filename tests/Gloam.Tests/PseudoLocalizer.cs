using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Gloam.Localization;

namespace Gloam.Tests;

/// <summary>
/// English text padded with accents and brackets, to expose layouts that only fit the source language.
/// </summary>
/// <remarks>
/// Chinese is usually shorter than English, so a zh-Hans screenshot cannot prove the layout is
/// robust — it hides expansion instead of testing it. Pseudo-localisation does the opposite: it
/// keeps the string's meaning recognisable while making it ~40% longer and wider, which is roughly
/// what German or Russian would do to the same label. If a panel survives the pseudo language it
/// will survive a real one.
///
/// Not shipped: this lives in the test project and only runs when the screenshot harness asks for it.
/// </remarks>
internal sealed class PseudoLocalizer : ILocalizer
{
    private const string Accented = "åβçðéƒğhïjķļmñöþqŕšţüvŵxýž";

    private readonly ILocalizer _inner;

    internal PseudoLocalizer(ILocalizer inner) => _inner = inner;

    public CultureInfo Culture => _inner.Culture;

    public IReadOnlyList<CultureInfo> AvailableCultures => _inner.AvailableCultures;

    public event EventHandler<CultureChangedEventArgs>? CultureChanged
    {
        add => _inner.CultureChanged += value;
        remove => _inner.CultureChanged -= value;
    }

    public void SetCulture(CultureInfo culture) => _inner.SetCulture(culture);

    public MissingKeyReport MissingKeys => _inner.MissingKeys;

    public string Get(string key) => Transform(_inner.Get(key));

    public string Plural(string key, double count, params (string Name, object? Value)[] arguments)
        => Transform(_inner.Plural(key, count, arguments));

    public bool TryGet(string key, out string value)
    {
        bool found = _inner.TryGet(key, out value);
        value = found ? Transform(value) : value;
        return found;
    }

    public string Format(string key, params (string Name, object? Value)[] arguments)
        => Transform(_inner.Format(key, arguments));

    /// <summary>Accents each letter, pads by ~40%, and brackets the result so truncation is obvious.</summary>
    internal static string Transform(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var builder = new StringBuilder(text.Length * 2);
        builder.Append('[');

        int index = 0;
        foreach (char c in text)
        {
            if (char.IsLetter(c) && c < 128)
            {
                builder.Append(Accented[index++ % Accented.Length]);
            }
            else
            {
                builder.Append(c);
            }
        }

        // ~40% extra: English to German is commonly quoted at 30%, and the widest glyphs here are
        // wider still, so err upward rather than downward.
        int padding = Math.Max(3, text.Length * 2 / 5);
        builder.Append(' ');
        builder.Append('·', padding);
        builder.Append(']');
        return builder.ToString();
    }
}
