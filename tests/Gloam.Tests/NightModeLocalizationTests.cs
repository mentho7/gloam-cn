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
    [Fact]
    public void Game_library_presets_refresh_in_place_and_keep_the_selected_preset()
    {
        WpfTestHost.Run(() =>
        {
            var localizer = CreateLocalizer();
            LocalizedStrings.Default = new LocalizedStrings(localizer);
            using var item = new Gloam.Features.GameLab.GamerModeItem();
            var preset = item.AvailableLibraryPresets[0];
            item.SelectedLibraryPreset = preset;
            var label = new TextBlock();
            label.SetBinding(TextBlock.TextProperty, new Binding("Label") { Source = preset });
            foreach (var culture in new[] { "en-US", "zh-Hans" })
            {
                localizer.SetCulture(CultureInfo.GetCultureInfo(culture));
                Assert.Equal(localizer.Get("gamelab.accurate.all.games"), label.Text);
                Assert.Same(preset, item.SelectedLibraryPreset);
            }
        });
    }

    [Fact]
    public void View_model_uses_injected_typed_strategy_for_labels_and_description()
    {
        using var vm = new NightModeScheduleViewModel(CreateLocalizer(), new TestNightModeText());
        Assert.Equal("label:Perceptual", vm.AvailableAlgorithms[0].Label);
        Assert.Equal("description:Perceptual", vm.AlgorithmDescription);
        vm.Algorithm = NightModeAlgorithm.UltraNight;
        Assert.Equal("description:UltraNight", vm.AlgorithmDescription);
    }

    private sealed class TestNightModeText : INightModeText
    {
        public string AlgorithmLabel(NightModeAlgorithm algorithm) => $"label:{algorithm}";
        public string AlgorithmDescription(NightModeAlgorithm algorithm) => $"description:{algorithm}";
        public string AlgorithmSummary(NightModeAlgorithm algorithm) => $"summary:{algorithm}";
    }

    [Fact]
    public void Actual_control_keeps_selection_and_refreshes_collapsed_combo_text()
    {
        WpfTestHost.Run(() =>
        {
            var localizer = CreateLocalizer();
            LocalizedStrings.Default = new LocalizedStrings(localizer);
            var control = new NightModeScheduleControl();
            using var vm = control.Vm;
            control.Initialize(new NightModeSettings { Algorithm = NightModeAlgorithm.Perceptual });
            control.Measure(new System.Windows.Size(1200, 900));
            control.Arrange(new System.Windows.Rect(0, 0, 1200, 900));
            control.UpdateLayout();
            var combo = VisualChildren(control).OfType<ComboBox>()
                .Single(c => ReferenceEquals(c.ItemsSource, vm.AvailableAlgorithms));
            var selected = combo.SelectedItem;
            int saves = 0;
            control.ScheduleChanged += () => saves++;
            foreach (var culture in new[] { "en-US", "zh-Hans", "en-US" })
            {
                localizer.SetCulture(CultureInfo.GetCultureInfo(culture));
                control.UpdateLayout();
                Assert.Same(selected, combo.SelectedItem);
                Assert.Contains(VisualChildren(combo).OfType<TextBlock>(),
                    text => text.Text == localizer.Get("night.profile.perceptual"));
            }
            Assert.Equal(0, saves);
            control.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.FrameworkElement.UnloadedEvent));
        });
    }

    private static IEnumerable<System.Windows.DependencyObject> VisualChildren(System.Windows.DependencyObject parent)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in VisualChildren(child)) yield return descendant;
        }
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("zh-Hans")]
    public void Typed_strategy_covers_every_offered_algorithm_with_shipped_translations(string culture)
    {
        var localizer = CreateLocalizer();
        localizer.SetCulture(CultureInfo.GetCultureInfo(culture));
        INightModeText text = new CatalogNightModeText(localizer);
        var options = NightModeAlgorithmOption.CreateOptions(text);
        Assert.Equal(4, options.Count);
        foreach (var option in options)
        {
            var algorithm = option.Value;
            Assert.False(string.IsNullOrWhiteSpace(text.AlgorithmLabel(algorithm)));
            Assert.False(string.IsNullOrWhiteSpace(text.AlgorithmDescription(algorithm)));
            Assert.False(string.IsNullOrWhiteSpace(text.AlgorithmSummary(algorithm)));
        }
        Assert.Equal(string.Empty, text.AlgorithmLabel((NightModeAlgorithm)999));
        Assert.Empty(localizer.MissingKeys.Keys);
    }

    [Fact]
    public void Background_switch_refreshes_on_ui_thread_and_dispose_cancels_queued_refresh()
    {
        WpfTestHost.Run(() =>
        {
            var localizer = CreateLocalizer();
            int uiThread = Environment.CurrentManagedThreadId;
            int calls = 0;
            using var subscription = new LanguageRefreshSubscription(localizer, () =>
            {
                Assert.Equal(uiThread, Environment.CurrentManagedThreadId);
                calls++;
            });
            Task.Run(() => localizer.SetCulture(CultureInfo.GetCultureInfo("en-US"))).GetAwaiter().GetResult();
            WpfTestHost.PumpUntil(() => calls == 1, TimeSpan.FromSeconds(3));
            Assert.Equal(1, calls);
            Task.Run(() => localizer.SetCulture(CultureInfo.GetCultureInfo("zh-Hans"))).GetAwaiter().GetResult();
            subscription.Dispose();
            WpfTestHost.Pump(TimeSpan.FromMilliseconds(100));
            Assert.Equal(1, calls);
        });
    }

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
