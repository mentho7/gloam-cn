using System;
using System.Windows;
using System.Windows.Media;

namespace Gloam.Infrastructure.Theming
{
    /// <summary>
    /// App-wide light/dark swap for the design tokens defined in App.xaml.
    /// Replaces the Theme* brush entries in Application.Resources, which every
    /// {DynamicResource Theme*} consumer re-resolves. Swapping (not mutating) is
    /// required because sealed styles freeze the brushes they reference.
    /// </summary>
    /// <remarks>
    /// Extracted from App.xaml.cs so the composition root stays a composition root: theme
    /// state is infrastructure, not application startup glue. Consumers outside this
    /// namespace reach it through the project-level global usings declared in Gloam.csproj.
    /// </remarks>
    public static class BrutalistTheme
    {
        public static bool IsDark { get; private set; } = true;

        /// <summary>Raised after a swap. Canvas-drawn UI (e.g. the schedule graph)
        /// subscribes to repaint, since it samples token colors once at draw time.</summary>
        public static event Action? Changed;

        private static readonly (string Key, string Dark, string Light)[] Palette =
        {
            ("ThemeBg",       "#0E1116", "#F7F8FA"),
            ("ThemeSurface",  "#171C23", "#FFFFFF"),
            ("ThemeHover",    "#222A34", "#EEF1F4"),
            ("ThemeText",     "#F4F7FA", "#15191F"),
            ("ThemeTextDim",  "#A8B0BC", "#64707D"),
            ("ThemeBorder",   "#465567", "#B9C4D0"),
            ("ThemeAccent",   "#E35F52", "#CF4A40"),
            ("ThemeOnAccent", "#FFFFFF", "#FFFFFF"),
            ("ThemeMeter",    "#82B7F2", "#327CC7"),
            ("ThemeTrack",    "#29313B", "#DFE5EC"),
            ("ThemeAmber",    "#D89A2B", "#B87512"),
            ("ThemeWindowFrame", "#364252", "#B9C4D0"),
        };

        // Legacy resource keys the older windows use, kept pointed at a token brush so
        // those windows theme without per-reference edits. (alias -> token)
        private static readonly (string Alias, string Token)[] Aliases =
        {
            ("AccentBrush",        "ThemeAccent"),
            ("AccentHoverBrush",   "ThemeAccent"),
            ("CardBackground",     "ThemeSurface"),
            ("CardBorder",         "ThemeBorder"),
            ("TextDim",            "ThemeTextDim"),
            ("InputBackground",    "ThemeSurface"),
            ("DropdownBackground", "ThemeSurface"),
            ("DarkAccent",         "ThemeAccent"),
            ("DarkCardBorder",     "ThemeBorder"),
        };

        /// <summary>Set by App to persist the user's manual choice. Initialize does not call it.</summary>
        public static Action<bool>? Persist { get; set; }

        public static void Initialize(bool dark) { IsDark = dark; Apply(); }
        public static void Toggle() { IsDark = !IsDark; Apply(); Persist?.Invoke(IsDark); }

        /// <summary>Current value of a token as a Color, for code-drawn visuals.</summary>
        public static Color Color(string key)
            => (Application.Current?.Resources[key] as SolidColorBrush)?.Color ?? Colors.Magenta;

        private static void Apply()
        {
            var res = Application.Current?.Resources;
            if (res == null) return;
            foreach (var (key, dark, light) in Palette)
                res[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(IsDark ? dark : light));
            // Point each legacy alias at its token's fresh brush instance.
            foreach (var (alias, token) in Aliases)
                res[alias] = res[token];
            Changed?.Invoke();
        }
    }
}
