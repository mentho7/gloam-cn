# Project structure

Gloam is seven source projects plus one test project. The layout is organised by feature, and the build is the
contract that keeps the boundaries honest.

```
src/
  Gloam.Interop/          P/Invoke surface: DisplayConfig, DXGI, GDI32, User32, WCS
  Gloam.Core/             Domain: gamma, night mode, calibration, settings, colour math
  Gloam.Localization/     Translation engine (no WPF, no Windows)
  Gloam.Localization.Wpf/ XAML adapter: markup extension, binding source, font policy
  Gloam.Cli/              Headless LUT and color diagnostics
  Gloam.DebugMonitors/    Windows monitor diagnostics
  Gloam/                  UI
    Shell/                App, Program, MainWindow — composition root only
    Features/
      Tray/               Tray menu, monitor items, toasts, hotkeys
      Dashboard/          Dashboard window and item view models
      Calibration/        Every calibration window, coordinator and report builder
      GameLab/            Per-game profiles and the compact in-game window
      NightMode/          Schedule editor control and view model
      Settings/           Per-monitor settings window
    Infrastructure/
      Dialogs/            Busy, confirm, crash dialogs
      Theming/            Design tokens, theme swap, window chrome
      Platform/           Startup registration, updates, window bounds, safe async
    Controls/             Controls shared across features (BrandMark)
    Themes/, Fonts/       Resource assets referenced by absolute pack URIs
tests/Gloam.Tests/        xunit; drives real WPF windows through WpfTestHost
```

## Why features rather than layers

The previous layout was `src/Gloam/` with forty files in one folder: every window, every dialog,
the theme, the tray view model, threading helpers and the composition root side by side. Adding a
feature meant guessing which of five folders owned the window you needed, and a review of "the
night mode change" scrolled past unrelated files.

A feature folder now answers "what is this and who owns it" from the path alone. The layers inside
a feature are still the familiar ones — window, view model, coordinator — they are just not spread
across the tree.

## Namespaces follow folders, with one deliberate exception

`Gloam.Features.Calibration.CalibrationWindow` names its owner. But `Gloam.csproj` also declares
each feature namespace as a project-level global using:

```xml
<Using Include="Gloam.Features.Calibration" />
```

The UI assembly is one deployable module, so a file moving between two folders inside a feature
must not cascade into a using-directive edit across dozens of unrelated files. Namespaces stay for
readability and code review; the global usings keep the churn local.

This is a UI-assembly-only policy. `Gloam.Core` and `Gloam.Localization` use explicit usings, which
is what you want at a real module boundary.

## Toolchain

**Target framework: `net10.0` / `net10.0-windows` everywhere.** .NET 10 is the current LTS,
supported to 2028-11-14. The project sat on `net8.0` past its end-of-support date (2026-11-10)
before this was fixed, so the rule from here is: move to the next **LTS**, not the next release.
LTS lands every two years (10, then 12), and an app that only ever jumps LTS never has to make the
move under pressure.

**`global.json` sets an SDK floor, not a pin, and stays inside the 10.0 feature band.** With
`rollForward: latestFeature`, an installed SDK 11 or 12 is *refused* rather than used, so the
compiler cannot drift away from what CI runs. This is deliberate: a build that quietly changes
because someone installed a newer SDK is the failure mode the next point exists to prevent.

**`AnalysisLevel` is pinned to `10.0-recommended`.** It used to be `latest-recommended`, which
means the analyzer rule set followed whichever SDK happened to be installed. An SDK upgrade then
changed the build's pass/fail behaviour: the .NET 11 attempt produced 48 new diagnostics, and
because `GloamStrictBuild` treats warnings as errors, that was a red build for a change nobody
made. Bumping the pin is now a deliberate change, made in the same commit that fixes what the new
rules report.

**Line endings are declared in `.gitattributes`, not left to `core.autocrlf`.** The repo previously
had neither, while `.editorconfig` asked for `end_of_line = lf` — so the actual behaviour depended
on each clone's global git config.

## Rules worth keeping

**The composition root stays a composition root.** `Shell/App.xaml.cs` builds the container and
starts the app. It does not host theme state (that is `Infrastructure/Theming/BrutalistTheme`), and
it does not host windows.

**Resource dictionaries are referenced by absolute pack URI.** A relative
`Source="Themes/DarkControls.xaml"` resolves against the *window's* folder, so moving a window
silently breaks its theme at runtime and no compiler complains. Use
`pack://application:,,,/Gloam;component/Themes/...` in XAML windows.

**`Themes/` and `Fonts/` stay at the project root.** Their pack URIs, and the font family names the
theme publishes, are runtime identity.

**Domain calculations do not live in the UI project.** Report uncertainty is computed by
`Gloam.Core/Calibration/ReportUncertaintyCalculator`. `HdrPatchRenderer` intentionally remains
in the calibration UI feature: it owns a Win32 presentation window and uses localized window
titles and errors through the WPF localization adapter. Moving it to Core as-is would reverse
the dependency boundary. A future reusable renderer should accept presentation text from its
caller and retain explicit window/thread ownership; moving the file alone is not sufficient.

## Adding a feature

1. `src/Gloam/Features/<Name>/` holding the window, its view model and any coordinator.
2. Register the namespace in `Gloam.csproj` alongside the other `<Using>` entries.
3. Register services in `App.ConfigureServices`; features are wired explicitly, not discovered.
4. If the feature renders text, put the strings in a catalog — see [localization](localization.md).

## Tests

`dotnet test` runs the whole suite, including the WPF layout regressions and the localization
audits. The suite is configured with `DisableTestParallelization`: the app has process-wide state
(theme tokens, UI culture, static paths) and pretending otherwise produces flaky tests rather than
faster ones.
