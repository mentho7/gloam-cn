using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Gloam.Localization.Wpf;

namespace Gloam.Infrastructure.Dialogs
{
    /// <summary>
    /// Dark-themed Yes/No confirmation, replacing MessageBox where it would render
    /// stock chrome - and, on Topmost fullscreen windows (calibration), where a
    /// plain MessageBox can open BEHIND the window with no visible feedback.
    /// </summary>
    public sealed class ConfirmDialog : Window
    {
        private bool _confirmed;

        private ConfirmDialog(string title, string message, string confirmLabel, string? cancelLabel)
        {
            Title = title;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            SizeToContent = SizeToContent.WidthAndHeight;
            MaxWidth = 460;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            this.SetResourceReference(ForegroundProperty, "ThemeText");

            Button MakeButton(string label, bool accent)
            {
                var b = new Button
                {
                    Content = label,
                    Padding = new Thickness(16, 7, 16, 7),
                    Margin = new Thickness(8, 0, 0, 0),
                    BorderThickness = new Thickness(1),
                    MinWidth = 80,
                };
                b.SetResourceReference(BackgroundProperty, accent ? "ThemeAccent" : "ThemeSurface");
                b.SetResourceReference(ForegroundProperty, accent ? "ThemeOnAccent" : "ThemeText");
                b.SetResourceReference(Control.BorderBrushProperty, accent ? "ThemeAccent" : "ThemeBorder");
                return b;
            }

            var confirm = MakeButton(confirmLabel, accent: true);
            confirm.Click += (_, _) => { _confirmed = true; DialogResult = true; Close(); };
            confirm.IsDefault = true;

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0),
            };
            buttons.Children.Add(confirm);

            if (cancelLabel != null)
            {
                var cancel = MakeButton(cancelLabel, accent: false);
                cancel.Click += (_, _) => { DialogResult = false; Close(); };
                cancel.IsCancel = true;
                buttons.Children.Add(cancel);
            }
            else
            {
                // Informational dialog: the single OK button also answers Escape.
                confirm.IsCancel = true;
            }

            var display = Application.Current?.Resources["DisplayFont"] as FontFamily;
            var body = Application.Current?.Resources["BodyFont"] as FontFamily;

            var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            stack.Children.Add(new TextBlock
            {
                Text = title.ToUpperInvariant(),
                FontFamily = display,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10),
            });
            var messageText = new TextBlock
            {
                Text = message,
                FontFamily = body,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
            };
            messageText.SetResourceReference(ForegroundProperty, "ThemeTextDim");
            stack.Children.Add(messageText);
            stack.Children.Add(buttons);

            // Opens in the current app theme; confirmations appear over calibration/utility UI.
            var frame = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(0),
                Child = stack,
            };
            frame.SetResourceReference(Border.BackgroundProperty, "ThemeBg");
            frame.SetResourceReference(Border.BorderBrushProperty, "ThemeWindowFrame");
            Content = frame;

            // Merge the shared dark styles so the buttons get the templated look.
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Gloam;component/Themes/DarkControls.xaml", UriKind.Absolute),
            });
        }

        /// <summary>
        /// Shows the dialog modally over the owner and returns true when confirmed.
        /// </summary>
        /// <remarks>
        /// The labels are taken as literal text. Prefer <see cref="ConfirmKeys"/>: a literal passed
        /// from a call site is text that no catalog can change, so every caller ends up being its own
        /// translation point.
        /// </remarks>
        public static bool Confirm(Window owner, string title, string message,
            string? confirmLabel = null, string? cancelLabel = null)
        {
            confirmLabel ??= Loc.Get("dialog.yes");
            cancelLabel ??= Loc.Get("dialog.no");

            var dialog = new ConfirmDialog(title, message, confirmLabel, cancelLabel)
            {
                Owner = owner,
                // Match the owner so the dialog isn't buried under a Topmost
                // fullscreen window (the old MessageBox failure mode).
                Topmost = owner?.Topmost == true,
            };
            dialog.ShowDialog();
            return dialog._confirmed;
        }

        /// <summary>
        /// Confirms using catalog keys instead of literal text, so the dialog follows the language
        /// without each caller resolving the strings itself.
        /// </summary>
        /// <param name="messageKey">Catalog key, or a value from <paramref name="arguments"/>.</param>
        public static bool ConfirmKeys(Window owner, string titleKey, string messageKey,
            string? confirmKey = null, string? cancelKey = null,
            params (string Name, object? Value)[] arguments)
        {
            return Confirm(
                owner,
                Loc.Get(titleKey),
                arguments.Length == 0 ? Loc.Get(messageKey) : Loc.Format(messageKey, arguments),
                Loc.Get(confirmKey ?? "dialog.yes"),
                Loc.Get(cancelKey ?? "dialog.no"));
        }

        /// <summary>
        /// Shows an informational dialog (single OK button) modally over the owner.
        /// Replaces MessageBox for status reports, errors and multiline summaries.
        /// </summary>
        public static void Info(Window owner, string title, string message)
        {
            var dialog = new ConfirmDialog(title, message, Loc.Get("dialog.ok"), cancelLabel: null)
            {
                Owner = owner,
                Topmost = owner?.Topmost == true,
                // Informational messages can be long multiline reports (e.g. the HDR
                // renderer validation) - give them more width before wrapping.
                MaxWidth = 640,
            };
            dialog.ShowDialog();
        }
    }
}
