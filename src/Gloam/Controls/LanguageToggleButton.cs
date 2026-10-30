using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Gloam.Localization.Wpf;

namespace Gloam.Controls
{
    /// <summary>
    /// Title-bar language switch. Shows the language a click would switch <em>to</em> ("ZH" while
    /// the UI is English), which is the convention users already read as "take me there" — showing
    /// the current language instead leaves people guessing whether the chip is a state or a button.
    /// </summary>
    /// <remarks>
    /// Pulls <see cref="LanguageService"/> from the composition root rather than taking it as a
    /// constructor argument: the button is declared in XAML, and XAML cannot inject. If the service
    /// is absent (designer, a host without localization) the button removes itself instead of
    /// rendering a dead control.
    /// </remarks>
    public sealed class LanguageToggleButton : Button
    {
        private LanguageService? _languages;

        public LanguageToggleButton()
        {
            Cursor = Cursors.Hand;
            Focusable = true;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            Click += OnClick;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _languages = Shell.App.Services?.GetService(typeof(LanguageService)) as LanguageService;
            if (_languages is null)
            {
                Visibility = Visibility.Collapsed;
                return;
            }

            _languages.LanguageChanged += OnLanguageChanged;
            Refresh();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_languages is not null)
                _languages.LanguageChanged -= OnLanguageChanged;

            _languages = null;
        }

        private void OnClick(object sender, RoutedEventArgs e) => _languages?.Toggle();

        private void OnLanguageChanged(object? sender, CultureChangedEventArgs e) => Refresh();

        private void Refresh()
        {
            LanguageOption? target = _languages?.NextOption();
            if (target is null)
            {
                // One shipped language: a switch control would be a no-op.
                Visibility = Visibility.Collapsed;
                return;
            }

            Visibility = Visibility.Visible;
            Content = target.ShortLabel;

            string tooltip = LocalizedStrings.Default.Localizer.Format(
                "chrome.language.switchTo", ("language", target.DisplayName));
            ToolTip = tooltip;

            // Screen readers get the same sentence the tooltip shows.
            AutomationProperties.SetName(this, tooltip);
        }
    }
}
