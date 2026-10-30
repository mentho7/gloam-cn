using System.Windows.Data;
using System.Windows.Markup;

namespace Gloam.Localization.Wpf;

/// <summary>
/// XAML markup extension that resolves a translation key at bind time and re-resolves it when the
/// language changes: <c>Text="{loc:Tr settings.language.heading}"</c>.
/// </summary>
/// <remarks>
/// Returns a <see cref="Binding"/> rather than a string so the element keeps listening. A markup
/// extension that returned the resolved string would be simpler and would freeze the text at load
/// time — which is exactly the behaviour that forces a restart on every language change.
/// </remarks>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string? Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        if (string.IsNullOrWhiteSpace(Key))
            return string.Empty;

        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizedStrings.Default,
            Mode = BindingMode.OneWay,
        };

        return binding.ProvideValue(serviceProvider);
    }
}
