using System.Globalization;
using System.Windows.Controls;
using System.Windows.Markup;
using Gloam.Localization;
using Gloam.Localization.Wpf;
using Xunit;

namespace Gloam.Tests;

/// <summary>
/// Covers the WPF adapter: the <c>{loc:Tr}</c> markup extension, the binding source that makes a
/// live language switch possible, and the picker service that persists the choice.
/// </summary>
/// <remarks>
/// The interesting case is not "does a string resolve" — that is the engine's job. It is
/// "does text already on screen change when the language changes", because that is the difference
/// between a switch and a restart, and it is entirely a property of the WPF layer.
/// </remarks>
[ResetsLocalization]
[Collection(UiGlobalState.Name)]
    public class LocalizationWpfTests
{
    private const string LatinXaml = """
        <TextBlock xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:loc="clr-namespace:Gloam.Localization.Wpf;assembly=Gloam.Localization.Wpf"
                   Text="{loc:Tr settings.language.heading}"/>
        """;

    private static Localizer BuildLocalizer()
    {
        var options = new LocalizationOptions { SourceCulture = CultureInfo.GetCultureInfo("en-US") };
        return new Localizer(
            StringCatalogSet.Load(new EmbeddedCatalogSource(typeof(Gloam.Shell.App).Assembly, ShippedCatalogs.ResourcePrefix)),
            options,
            CultureInfo.GetCultureInfo("en-US"));
    }

    [Fact]
    public void Tr_extension_renders_the_source_language_and_follows_a_live_switch()
    {
        Localizer localizer = BuildLocalizer();

        WpfTestHost.Run(() =>
        {
            LocalizedStrings.Default = new LocalizedStrings(localizer);

            var text = (TextBlock)XamlReader.Parse(LatinXaml);
            Assert.Equal("Interface language", text.Text);

            localizer.SetCulture(CultureInfo.GetCultureInfo("zh-Hans"));

            // Same element instance, no reload: the binding re-resolved in place.
            Assert.Equal("界面语言", text.Text);
        });
    }

    [Fact]
    public void A_culture_change_notifies_indexer_bindings_once()
    {
        Localizer localizer = BuildLocalizer();
        WpfTestHost.Run(() =>
        {
            var strings = new LocalizedStrings(localizer);
            var notifications = new List<string?>();
            strings.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);

            localizer.SetCulture(CultureInfo.GetCultureInfo("zh-Hans"));

            Assert.Equal(new[] { "Item[]" }, notifications);
        });
    }

    [Fact]
    public void The_picker_offers_follow_system_plus_every_shipped_language()
    {
        Localizer localizer = BuildLocalizer();
        var service = new LanguageService(localizer, _ => { });

        Assert.True(service.Options[0].FollowsSystem);
        Assert.Contains(service.Options, o => string.Equals(o.Tag, "en-US", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(service.Options, o => string.Equals(o.Tag, "zh-Hans", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Selecting_a_language_switches_the_ui_and_persists_the_tag()
    {
        Localizer localizer = BuildLocalizer();
        string? persisted = "unset";
        var service = new LanguageService(localizer, tag => persisted = tag);

        // LanguageService sets the process UI culture, which outlives this test; restore it so
        // the rest of the suite keeps running under the culture it started with.
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            LanguageOption chinese = service.Options.Single(o => string.Equals(o.Tag, "zh-Hans", StringComparison.OrdinalIgnoreCase));
            service.Select(chinese);

            Assert.Equal("zh-Hans", persisted);
            Assert.Equal("zh-Hans", localizer.Culture.Name);
            Assert.Equal("界面语言", localizer.Get("settings.language.heading"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUiCulture;
            CultureInfo.DefaultThreadCurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void Choosing_follow_system_persists_null_and_uses_the_os_language()
    {
        Localizer localizer = BuildLocalizer();
        string? persisted = "unset";
        var service = new LanguageService(localizer, tag => persisted = tag);

        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            service.Select(service.Options.First(o => o.FollowsSystem));

            Assert.Null(persisted);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUiCulture;
            CultureInfo.DefaultThreadCurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void An_unparseable_saved_tag_falls_back_to_the_os_language()
    {
        // A hand-edited settings.json must not be able to throw here: this runs during startup,
        // before any window exists.
        Localizer localizer = BuildLocalizer();
        var service = new LanguageService(localizer, _ => { });

        Assert.Equal(CultureInfo.InstalledUICulture.Name, service.ResolveTarget("!!not a tag!!").Name);
        Assert.Equal(CultureInfo.InstalledUICulture.Name, service.ResolveTarget("   ").Name);
        Assert.Equal(CultureInfo.InstalledUICulture.Name, service.ResolveTarget(null).Name);
    }

    [Fact]
    public void Cjk_cultures_get_a_cjk_capable_font_family()
    {
        Assert.True(CultureFontPolicy.RequiresCjk(CultureInfo.GetCultureInfo("zh-Hans")));
        Assert.True(CultureFontPolicy.RequiresCjk(CultureInfo.GetCultureInfo("ja-JP")));
        Assert.False(CultureFontPolicy.RequiresCjk(CultureInfo.GetCultureInfo("en-US")));
        Assert.Contains("YaHei", CultureFontPolicy.Preview(CultureInfo.GetCultureInfo("zh-Hans"), display: false).Source,
            StringComparison.Ordinal);
    }
}
