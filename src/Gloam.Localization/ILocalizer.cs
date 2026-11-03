using System.Globalization;

namespace Gloam.Localization;

/// <summary>
/// Read-only translation surface. Everything the UI needs to display a translated string
/// goes through here, so no caller ever reaches into a catalog directly.
/// </summary>
/// <remarks>
/// Implementations must be safe to read from any thread: the WPF binding layer resolves
/// strings from the dispatcher, background tasks resolve the same strings for log-adjacent
/// UI updates, and both may run at once during a language switch.
/// </remarks>
public interface ILocalizer
{
    /// <summary>Culture currently used for lookup and for value formatting.</summary>
    CultureInfo Culture { get; }

    /// <summary>
    /// Cultures that actually have a catalog loaded, ordered by display name. This is what a
    /// language picker should offer — not <see cref="CultureInfo.GetCultures(CultureTypes)"/>,
    /// which would list hundreds of cultures the app cannot render.
    /// </summary>
    IReadOnlyList<CultureInfo> AvailableCultures { get; }

    /// <summary>
    /// Resolves <paramref name="key"/> in the current culture, falling back through the
    /// culture chain. Returns the key itself when no catalog defines it, and records the miss
    /// in <see cref="MissingKeys"/>.
    /// </summary>
    string Get(string key);

    /// <summary>
    /// Resolves and renders <paramref name="key"/> with named arguments, e.g.
    /// <c>Format("dashboard.monitorCount", ("count", 3))</c>.
    /// </summary>
    string Format(string key, params (string Name, object? Value)[] arguments);

    /// <summary>
    /// Resolves and renders <paramref name="key"/> selecting the CLDR plural form for
    /// <paramref name="count"/>. <paramref name="arguments"/> should also contain
    /// <c>count</c> so the rendered text can interpolate it.
    /// </summary>
    string Plural(string key, double count, params (string Name, object? Value)[] arguments);

    /// <summary>Resolves without recording a miss. Used by tooling and by fallback chains.</summary>
    bool TryGet(string key, out string value);

    /// <summary>
    /// Switches the active culture, re-resolves the fallback chain and raises
    /// <see cref="CultureChanged"/>. Unknown cultures fall back to the source culture rather
    /// than throwing: a stale or hand-edited settings file must not brick the app.
    /// </summary>
    void SetCulture(CultureInfo culture);

    /// <summary>Raised after <see cref="SetCulture"/> completed. Handlers run on the caller's thread.</summary>
    event EventHandler<CultureChangedEventArgs>? CultureChanged;

    /// <summary>Keys requested but not found in any catalog. Feeds diagnostics and CI audits.</summary>
    MissingKeyReport MissingKeys { get; }
}

/// <summary>Describes a completed language switch.</summary>
public sealed class CultureChangedEventArgs : EventArgs
{
    public CultureChangedEventArgs(CultureInfo previous, CultureInfo current)
    {
        Previous = previous;
        Current = current;
    }

    public CultureInfo Previous { get; }

    public CultureInfo Current { get; }
}
