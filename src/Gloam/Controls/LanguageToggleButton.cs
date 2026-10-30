using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        private ILocalizer? _fallback;

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
            if (_languages is not null)
            {
                _languages.LanguageChanged += OnLanguageChanged;
                Refresh();
                return;
            }

            // No container: design time, a screenshot harness, or a host that never registered the
            // service. Fall back to the published localizer so the control still switches; only the
            // persisted preference is lost, which is better than a missing control.
            ILocalizer localizer = LocalizedStrings.Default.Localizer;
            if (localizer.AvailableCultures.Count < 2)
            {
                Visibility = Visibility.Collapsed;
                return;
            }

            _fallback = localizer;
            _fallback.CultureChanged += OnFallbackCultureChanged;
            RefreshFallback();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_languages is not null)
                _languages.LanguageChanged -= OnLanguageChanged;

            if (_fallback is not null)
                _fallback.CultureChanged -= OnFallbackCultureChanged;

            _languages = null;
            _fallback = null;
        }

        private void OnClick(object sender, RoutedEventArgs e)
        {
            if (_languages is not null)
            {
                _languages.Toggle();
                return;
            }

            if (_fallback is null)
                return;

            IReadOnlyList<CultureInfo> cultures = _fallback.AvailableCultures;
            int current = -1;
            for (int i = 0; i < cultures.Count; i++)
            {
                if (string.Equals(cultures[i].Name, _fallback.Culture.Name, StringComparison.OrdinalIgnoreCase))
                    current = i;
            }

            CultureInfo next = cultures[(current + 1) % cultures.Count];
            CultureInfo.CurrentUICulture = next;
            _fallback.SetCulture(next);
            CultureFontPolicy.Apply(_fallback.Culture);
        }

        private void OnFallbackCultureChanged(object? sender, CultureChangedEventArgs e) => RefreshFallback();

        private void RefreshFallback()
        {
            if (_fallback is null)
                return;

            IReadOnlyList<CultureInfo> cultures = _fallback.AvailableCultures;
            string current = _fallback.Culture.TwoLetterISOLanguageName;
            CultureInfo next = cultures.FirstOrDefault(c =>
                !string.Equals(c.TwoLetterISOLanguageName, current, StringComparison.OrdinalIgnoreCase)) ?? cultures[0];

            Visibility = Visibility.Visible;
            Content = next.TwoLetterISOLanguageName.ToUpperInvariant();

            string tooltip = _fallback.Format("chrome.language.switchTo", ("language", next.NativeName));
            ToolTip = tooltip;
            AutomationProperties.SetName(this, tooltip);
        }

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
