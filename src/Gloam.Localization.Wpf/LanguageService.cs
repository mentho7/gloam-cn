using System.Globalization;
using Gloam.Localization;

namespace Gloam.Localization.Wpf;

/// <summary>One entry in the language picker.</summary>
public sealed class LanguageOption
{
    internal LanguageOption(string? tag, string displayName, bool followsSystem)
    {
        Tag = tag;
        DisplayName = displayName;
        FollowsSystem = followsSystem;
    }

    /// <summary>Culture tag to persist, or <c>null</c> for "follow Windows".</summary>
    public string? Tag { get; }

    /// <summary>Native name of the language, e.g. "中文(简体中文)".</summary>
    public string DisplayName { get; }

    /// <summary>True for the "follow Windows" entry.</summary>
    public bool FollowsSystem { get; }

    public override string ToString() => DisplayName;
}

/// <summary>
/// Owns the "which language is the UI in" decision: what the picker offers, what is currently
/// selected, applying it at startup, and persisting the choice.
/// </summary>
/// <remarks>
/// Persistence is a callback, not a dependency. The settings store lives in Gloam.Core, which this
/// adapter knows nothing about; passing <c>settings.SetLanguage</c> in keeps the dependency
/// pointing the right way and makes the service trivial to test with a lambda.
/// </remarks>
public sealed class LanguageService
{
    private readonly ILocalizer _localizer;
    private readonly Action<string?> _persist;
    private readonly string _followSystemLabelKey;

    private List<LanguageOption> _options = new();
    private LanguageOption _selected;

    /// <param name="localizer">The localizer to drive.</param>
    /// <param name="persist">Called with the chosen tag; <c>null</c> means "follow Windows".</param>
    /// <param name="followSystemLabelKey">Catalog key for the "follow Windows" entry.</param>
    public LanguageService(ILocalizer localizer, Action<string?> persist, string followSystemLabelKey = "settings.language.followSystem")
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _persist = persist ?? throw new ArgumentNullException(nameof(persist));
        _followSystemLabelKey = followSystemLabelKey;

        Rebuild();
        _selected = FindFor(_localizer.Culture) ?? _options[0];
    }

    /// <summary>Raised after the language changed and fonts were reapplied.</summary>
    public event EventHandler<CultureChangedEventArgs>? LanguageChanged;

    /// <summary>Picker entries: "follow Windows" first, then every shipped language by native name.</summary>
    public IReadOnlyList<LanguageOption> Options => _options;

    public LanguageOption Selected => _selected;

    /// <summary>
    /// Applies the saved choice, or the OS language when nothing was saved. Call once at startup,
    /// before the first window is created.
    /// </summary>
    public void ApplyStartup(string? savedTag)
    {
        CultureInfo target = ResolveTarget(savedTag);
        Apply(target);

        // Reconcile the picker with what the localizer actually ended up serving: the request may
        // have been zh-CN while the catalog is zh-Hans. An explicit "follow system" choice stays
        // on the follow-system entry rather than being re-derived.
        _selected = string.IsNullOrWhiteSpace(savedTag)
            ? _options[0]
            : FindFor(_localizer.Culture) ?? _selected;
    }

    /// <summary>Applies and persists <paramref name="option"/>.</summary>
    public void Select(LanguageOption option)
    {
        ArgumentNullException.ThrowIfNull(option);

        CultureInfo previous = _localizer.Culture;
        Apply(ResolveTarget(option.Tag));

        // The chosen option is authoritative. Deriving it back from the resolved culture would
        // turn "follow Windows" into a concrete language the first time the OS language happened
        // to match a shipped catalog, silently pinning the user to it.
        _selected = option;

        _persist(option.FollowsSystem ? null : option.Tag);
        LanguageChanged?.Invoke(this, new CultureChangedEventArgs(previous, _localizer.Culture));
    }

    /// <summary>
    /// The culture a saved tag maps to. A tag that no longer exists falls back to the OS language
    /// rather than to English: an obsolete translation should degrade to the user's system
    /// language, not to a foreign one.
    /// </summary>
    public CultureInfo ResolveTarget(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return CultureInfo.InstalledUICulture;

        return CultureChain.TryParse(tag) ?? CultureInfo.InstalledUICulture;
    }

    /// <summary>Pushes a culture into the process, the localizer and the font tokens.</summary>
    private void Apply(CultureInfo culture)
    {
        // Keep WPF's own text services and the localizer's notion of the language in step.
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        _localizer.SetCulture(culture);
        CultureFontPolicy.Apply(_localizer.Culture);

        _selected = FindFor(_localizer.Culture) ?? _selected;
    }

    private LanguageOption? FindFor(CultureInfo culture)
    {
        // Exact tag first, then same language: the localizer may have resolved zh-CN to a
        // zh-Hans catalog, and the picker must still highlight the Chinese entry.
        LanguageOption? exact = _options.FirstOrDefault(o =>
            !o.FollowsSystem && string.Equals(o.Tag, culture.Name, StringComparison.OrdinalIgnoreCase));

        if (exact is not null)
            return exact;

        string language = culture.TwoLetterISOLanguageName;
        return _options.FirstOrDefault(o =>
            !o.FollowsSystem &&
            o.Tag is not null &&
            string.Equals(CultureChain.TryParse(o.Tag)?.TwoLetterISOLanguageName, language, StringComparison.OrdinalIgnoreCase));
    }

    private void Rebuild()
    {
        var options = new List<LanguageOption>
        {
            new(null, _localizer.Get(_followSystemLabelKey), followsSystem: true),
        };

        foreach (CultureInfo culture in _localizer.AvailableCultures)
        {
            string name = string.IsNullOrWhiteSpace(culture.NativeName) ? culture.Name : culture.NativeName;
            options.Add(new LanguageOption(culture.Name, name, followsSystem: false));
        }

        _options = options;
    }
}
