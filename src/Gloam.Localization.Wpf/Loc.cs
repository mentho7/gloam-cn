namespace Gloam.Localization.Wpf;

/// <summary>
/// Shorthand for resolving a string outside a constructor, for code that has no localizer injected.
/// </summary>
/// <remarks>
/// Most user-visible text in this app lives in a view model that could take an <c>ILocalizer</c>, and
/// the ones that do use it. Plenty more lives in windows, dialogs and static helpers that are
/// constructed in a dozen places each; threading a dependency through every one of those call sites
/// to translate a button caption is a poor trade. This facade resolves through the same instance the
/// XAML bindings use, so a language switch is still picked up on the next read.
///
/// Anything that is re-read on demand is fine. Anything cached in a field is not — see
/// docs/localization.md for the three cases and their symptoms.
/// </remarks>
public static class Loc
{
    /// <summary>The localizer the running application published, or a null object before startup.</summary>
    public static ILocalizer Instance => LocalizedStrings.Default.Localizer;

    /// <summary>Resolves <paramref name="key"/>. A missing key renders as the key itself.</summary>
    public static string Get(string key) => Instance.Get(key);

    /// <summary>Resolves <paramref name="key"/> and substitutes <c>{name}</c> placeholders.</summary>
    public static string Format(string key, params (string Name, object? Value)[] arguments)
        => Instance.Format(key, arguments);
}
