using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Gloam.Core;
using Gloam.Features.Dashboard;
using Gloam.Features.GameLab;
using Gloam.Localization;
using Gloam.Localization.Wpf;
using Xunit;

namespace Gloam.Tests;

/// <summary>
/// What happens to already-constructed view models and open windows when the language changes.
/// </summary>
/// <remarks>
/// The switch is live: no window is recreated, so anything resolved once at construction time keeps
/// the old text. These pin the two shapes that have already caused bugs — a label list built in a
/// field initializer, and a combo box whose ItemsSource is replaced while a selection is set.
/// </remarks>
[ResetsLocalization]
[Collection(UiGlobalState.Name)]
    public class LanguageSwitchBehaviourTests
{
    [Fact]
    public void Profile_editor_option_labels_follow_a_language_switch()
    {
        WpfTestHost.Run(() =>
        {
            Localizer localizer = Publish("en-US");
            var editor = new GamerProfileEditorItem(new GamerProfileRule { AppName = "game.exe" });

            Assert.Equal("All displays", editor.AvailableDisplayScopes[1].Label);
            Assert.Equal("Require SDR", editor.AvailableHdrExpectations[1].Label);

            localizer.SetCulture(CultureInfo.GetCultureInfo("zh-Hans"));

            Assert.Equal("所有显示器", editor.AvailableDisplayScopes[1].Label);
            Assert.Equal("要求 SDR", editor.AvailableHdrExpectations[1].Label);
        });
    }

    [Fact]
    public void An_open_game_lab_window_keeps_its_combo_selections_across_a_language_switch()
    {
        WpfTestHost.Run(() =>
        {
            Localizer localizer = Publish("en-US");
            Window window = UiFixtures.BuildGameLab();

            try
            {
                window.Show();
                WpfTestHost.Pump(TimeSpan.FromMilliseconds(400));

                var viewModel = Assert.IsType<DashboardViewModel>(window.DataContext);
                var editor = new GamerProfileEditorItem(new GamerProfileRule { AppName = "game.exe" });
                viewModel.GamerMode.Profiles.Add(editor);
                viewModel.GamerMode.SelectedProfile = editor;
                WpfTestHost.Pump(TimeSpan.FromMilliseconds(300));

                // The advanced grid lives inside the profile editor's own name scope, so the
                // window's FindName cannot see it; walk the realized visual tree instead.
                foreach (Expander expander in Descendants<Expander>(window))
                    expander.IsExpanded = true;
                WpfTestHost.Pump(TimeSpan.FromMilliseconds(300));

                var scope = Assert.IsType<ComboBox>(FindByName(window, "AdvancedTargetDisplaysBox"));
                scope.SelectedValue = GamerDisplayScope.AllDisplays;
                WpfTestHost.Pump(TimeSpan.FromMilliseconds(100));
                Assert.Equal(GamerDisplayScope.AllDisplays, editor.DisplayScope);

                localizer.SetCulture(CultureInfo.GetCultureInfo("zh-Hans"));

                // Wait for the rebuilt items rather than for a fixed interval: the ItemsSource
                // refresh happens on the dispatcher, and 300ms was enough alone but not under a
                // full test run's load.
                WpfTestHost.PumpUntil(
                    () => scope.Items.Cast<object>().Any(i =>
                        i is GamerChoice<GamerDisplayScope> choice && choice.Label == "所有显示器"),
                    TimeSpan.FromSeconds(3));

                // Refreshing the labels means replacing ItemsSource. The risk is that the combo then
                // writes a stale or empty selection back through its TwoWay binding, silently
                // resetting the user's choice - so the value has to be asserted, not just the label.
                Assert.Equal(GamerDisplayScope.AllDisplays, editor.DisplayScope);
                Assert.Equal(GamerDisplayScope.AllDisplays, scope.SelectedValue);
                Assert.Contains("所有显示器", scope.Items.Cast<GamerChoice<GamerDisplayScope>>()
                    .Select(choice => choice.Label));
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static DependencyObject FindByName(DependencyObject root, string name)
    {
        foreach (DependencyObject child in Descendants<DependencyObject>(root))
        {
            if (child is FrameworkElement element && element.Name == name)
                return element;
        }

        throw new InvalidOperationException($"No element named {name} in the visual tree.");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;

            foreach (T nested in Descendants<T>(child))
                yield return nested;
        }
    }

    private static Localizer Publish(string culture)
    {
        var localizer = new Localizer(
            StringCatalogSet.Load(new EmbeddedCatalogSource(
                typeof(Gloam.Shell.App).Assembly, "Gloam.Resources.i18n.")),
            new LocalizationOptions(),
            CultureInfo.GetCultureInfo(culture));

        LocalizedStrings.Default = new LocalizedStrings(localizer);
        return localizer;
    }
}
