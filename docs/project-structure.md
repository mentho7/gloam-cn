# Project structure

Gloam is five projects plus tests. The layout is organised by feature, and the build is the
contract that keeps the boundaries honest.

```
src/
  Gloam.Interop/          P/Invoke surface: DisplayConfig, DXGI, GDI32, User32, WCS
  Gloam.Core/             Domain: gamma, night mode, calibration, settings, colour math
  Gloam.Localization/     Translation engine (no WPF, no Windows)
  Gloam.Localization.Wpf/ XAML adapter: markup extension, binding source, font policy
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

**Target framework: `net11.0` / `net11.0-windows` everywhere.** The project previously targeted
`net8.0`, which reaches end of support on 2026-11-10. .NET 11 is an STS release: fine for an
actively maintained desktop app, but it means the next forced move is .NET 13, not .NET 12 — worth
diarising.

**`global.json` sets an SDK floor, not a pin.** The floor is `11.0.100-rc.3` because SemVer orders
that *below* `11.0.100`, so asking for `11.0.100` while only the RC is installed fails SDK
resolution even with `allowPrerelease`. Raise the floor to `11.0.100` and drop `allowPrerelease`
once GA is the norm.

**`AnalysisLevel` is pinned to `11.0-recommended`.** It used to be `latest-recommended`, which
means the analyzer rule set followed whichever SDK happened to be installed. An SDK upgrade then
changed the build's pass/fail behaviour: a routine bump produced 48 new diagnostics, and because
`GloamStrictBuild` treats warnings as errors, that was a red build for a change nobody made.
Bumping the pin is now a deliberate change, made in the same commit that fixes what the new rules
report.

## Toolchain

**Target framework: `net11.0` / `net11.0-windows` everywhere.** The project previously targeted
`net8.0`, which reaches end of support on 2026-11-10. .NET 11 is an STS release: fine for an
actively maintained desktop app, but it means the next forced move is .NET 13, not .NET 12 — worth
diarising.

**`global.json` sets an SDK floor, not a pin.** The floor is `11.0.100-rc.3` because SemVer orders
that *below* `11.0.100`, so asking for `11.0.100` while only the RC is installed fails SDK
resolution even with `allowPrerelease`. Raise the floor to `11.0.100` and drop `allowPrerelease`
once GA is the norm.

**`AnalysisLevel` is pinned to `11.0-recommended`.** It used to be `latest-recommended`, which
means the analyzer rule set followed whichever SDK happened to be installed. An SDK upgrade then
changed the build's pass/fail behaviour: a routine bump produced 48 new diagnostics, and because
`GloamStrictBuild` treats warnings as errors, that was a red build for a change nobody made.
Bumping the pin is now a deliberate change, made in the same commit that fixes what the new rules
report.

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

**Domain code does not live in the UI project.** `HdrPatchRenderer` is the current exception and is
a candidate to move into `Gloam.Core`; it arrived in the UI project before there was a rule.

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
