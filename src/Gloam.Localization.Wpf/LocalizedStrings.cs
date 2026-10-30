using System.ComponentModel;
using System.Globalization;
using Gloam.Localization;

namespace Gloam.Localization.Wpf;

/// <summary>
/// The binding source behind <see cref="TrExtension"/>: an indexer that resolves keys and raises a
/// change notification for every bound element when the language switches.
/// </summary>
/// <remarks>
/// This is what makes a live language switch possible without recreating windows. WPF's own
/// resource mechanism (<c>x:Static</c> against a ResourceDictionary) resolves once, at load time;
/// a language change would need every window torn down and rebuilt, losing scroll position, open
/// dialogs and in-flight calibration state. Binding to an indexer that raises
/// <c>PropertyChanged("Item[]")</c> updates every consumer in place instead.
/// </remarks>
public sealed class LocalizedStrings : INotifyPropertyChanged
{
    /// <summary>The notification WPF listens for on indexer bindings.</summary>
    private const string IndexerPropertyName = "Item[]";

    private static LocalizedStrings _default = new(NullLocalizer.Instance);

    private readonly ILocalizer _localizer;

    public LocalizedStrings(ILocalizer localizer)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _localizer.CultureChanged += OnCultureChanged;
    }

    /// <summary>
    /// The instance XAML binds to. Markup extensions cannot be handed a dependency, so the host
    /// application publishes the one instance it built here; tests may publish their own.
    /// </summary>
    public static LocalizedStrings Default
    {
        get => _default;
        set => _default = value ?? new LocalizedStrings(NullLocalizer.Instance);
    }

    public ILocalizer Localizer => _localizer;

    /// <summary>Resolves <paramref name="key"/>. Never throws; a missing key renders as the key.</summary>
    public string this[string key] =>
        string.IsNullOrEmpty(key) ? string.Empty : _localizer.Get(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Re-resolves every bound string. Called automatically on a culture change.</summary>
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(IndexerPropertyName));

    private void OnCultureChanged(object? sender, CultureChangedEventArgs e) => Refresh();

    /// <summary>
    /// Stand-in used before the app initialises localization. Returns the key so a design-time
    /// preview and a failed startup both show something that identifies itself.
    /// </summary>
    private sealed class NullLocalizer : ILocalizer
    {
        public static readonly NullLocalizer Instance = new();

        public CultureInfo Culture => CultureInfo.CurrentUICulture;

        public IReadOnlyList<CultureInfo> AvailableCultures => Array.Empty<CultureInfo>();

        public MissingKeyReport MissingKeys { get; } = new();

        public event EventHandler<CultureChangedEventArgs>? CultureChanged
        {
            add { }
            remove { }
        }

        public string Get(string key) => key;

        public string Format(string key, params (string Name, object? Value)[] arguments) => key;

        public string Plural(string key, double count, params (string Name, object? Value)[] arguments) => key;

        public bool TryGet(string key, out string value)
        {
            value = key;
            return false;
        }

        public void SetCulture(CultureInfo culture) { }
    }
}
