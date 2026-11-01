using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using Gloam.Core;
using Gloam.Features.NightMode;
using Gloam.Localization;
using Gloam.Localization.Wpf;
using Xunit;

namespace Gloam.Tests;

[ResetsLocalization]
[Collection(UiGlobalState.Name)]
public class NightModeLocalizationTests
{
    private static Localizer CreateLocalizer() => new(
        StringCatalogSet.Load(new EmbeddedCatalogSource(typeof(Gloam.Shell.App).Assembly,
            "Gloam.Resources.i18n.")), culture: CultureInfo.GetCultureInfo("zh-Hans"));

    [Fact]
    public void Existing_bindings_switch_both_ways_without_replacing_options_or_editing_settings()
    {
        WpfTestHost.Run(() =>
        {
            var localizer = CreateLocalizer();
            using var vm = new NightModeScheduleViewModel(localizer);
            var settings = new NightModeSettings { Algorithm = NightModeAlgorithm.Perceptual };
            vm.Initialize(settings);
            int edits = 0;
            vm.SettingsEdited += () => edits++;
            var options = vm.AvailableAlgorithms;
            var option = options[0];
            var label = new TextBlock();
            label.SetBinding(TextBlock.TextProperty, new Binding("Label") { Source = option });
            var description = new TextBlock();
            description.SetBinding(TextBlock.TextProperty,
                new Binding(nameof(vm.AlgorithmDescription)) { Source = vm });

            foreach (var culture in new[] { "zh-Hans", "en-US", "zh-Hans", "en-US" })
            {
                localizer.SetCulture(CultureInfo.GetCultureInfo(culture));
                Assert.Equal(localizer.Get("night.profile.perceptual"), label.Text);
                Assert.Equal(localizer.Get("nightmode.warm.white.point.eased.toward.neutral.partial.chromatic"), description.Text);
                Assert.Same(options, vm.AvailableAlgorithms);
                Assert.Same(option, vm.AvailableAlgorithms[0]);
                Assert.Equal(NightModeAlgorithm.Perceptual, settings.Algorithm);
                Assert.Equal(NightModeAlgorithm.Perceptual, vm.Algorithm);
            }
            Assert.Equal(0, edits);
        });
    }

    [Fact]
    public void Unloaded_subscription_stops_refreshing_and_reload_catches_up_once()
    {
        WpfTestHost.Run(() =>
        {
            var localizer = CreateLocalizer();
            using var vm = new NightModeScheduleViewModel(localizer);
            int refreshes = 0;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(vm.AlgorithmDescription)) refreshes++;
            };
            vm.Dispose();
            localizer.SetCulture(CultureInfo.GetCultureInfo("en-US"));
            Assert.Equal(0, refreshes);
            vm.ActivateLocalization();
            Assert.Equal(1, refreshes);
            localizer.SetCulture(CultureInfo.GetCultureInfo("zh-Hans"));
            Assert.Equal(2, refreshes);
        });
    }
}
