# Localization

Gloam's UI text is being migrated from hard-coded English literals onto a catalog-driven
localization layer. This document is the contract for that layer: how text is resolved, how a
language switch happens without a restart, how to add a language, and how CI stops the migration
from regressing.

English (en-US) and Simplified Chinese (zh-Hans) ship today.

## Why not .resx

WPF's built-in story is `ResourceManager` plus `.resx`. It was rejected for three reasons:

- **Resx is a build artifact.** A translator cannot fix a string, drop a file in place and see it;
  every change needs a rebuild and a release.
- **Switching language means rebuilding the visual tree.** `x:Static` resolves once, at load time.
  A language change would tear down and recreate every window, losing scroll position, open
  dialogs and in-flight calibration state.
- **The diff is XML noise.** Nobody reviews a `.resx` change line by line.

The layer here is JSON catalogs plus a binding source, which is reviewable, hot-loadable and
switchable in place.

## Layers

```
Gloam.Localization          engine, no UI dependency
  ILocalizer                Get / Format / Plural / SetCulture / CultureChanged / MissingKeys
  Localizer                 catalog chain, rendering, miss recording
  StringCatalog(Set)        immutable catalogs, merged from ordered sources
  CatalogJson               wire format reader/writer
  CultureChain              tag normalisation and fallback chain
  PluralRules               CLDR cardinal rules
  MessageTemplate           {name} placeholders, {{ }} escapes
  CatalogValidator          cross-language checks for CI
  LocalizationSourceAudit   finds hard-coded strings in source

Gloam.Localization.Wpf      adapter
  TrExtension               {loc:Tr some.key} in XAML
  LocalizedStrings          binding source that re-resolves on language change
  LanguageService           the picker, the switch, the persisted choice
  CultureFontPolicy         CJK-capable font families for CJK cultures
```

`Gloam.Core` knows nothing about localization. The dependency points one way: UI → adapter →
engine.

## How a string is resolved

```xml
<TextBlock Text="{loc:Tr settings.language.heading}"/>
```

`TrExtension` returns a `Binding` to `LocalizedStrings.Default["settings.language.heading"]`, not a
string. On a culture change `LocalizedStrings` raises `PropertyChanged("Item[]")`, and every bound
element re-resolves in place. Windows stay open.

In code:

```csharp
string label = _loc.Get("tray.monitorCount");                      // static text
string line  = _loc.Format("tray.gammaMode", ("version", "2.2"), ("monitor", name));
string count = _loc.Plural("tray.monitorCount", monitors.Count, ("count", monitors.Count));
```

### Fallback

A request for `zh-CN` walks `zh-CN → zh-Hans → zh → en-US` and uses the first catalog that defines
the key, then finally any catalog sharing the requested language. So Windows reporting `zh-CN`
while only `zh-Hans` ships still produces Chinese, not English.

An unavailable key returns **the key itself** and is recorded in `MissingKeys`. A UI showing
`settings.language.heading` is unmistakably broken; a UI showing nothing is not.

### Placeholders

`{name}` by convention, never `{0}`. Word order differs between languages, and positional
placeholders force translators to rewrite grammar to match English argument order.

`{{` and `}}` render literal braces. An unresolved placeholder is left visible on purpose — it
names the binding that is missing.

### Plurals

A catalog entry is either a string or a set of CLDR categories:

```json
"tray.monitorCount": { "one": "{count} monitor", "other": "{count} monitors" }
```

`Format` selects the form from the `count` argument; `Plural` takes the count explicitly. Chinese
needs only `other`, which is exactly why the entry is per-language rather than per-key. Languages
whose rules are not implemented fall back to the English rule and `PluralRules.RequiredCategories`
reports the gap at validation time.

## Adding a language

1. Copy `src/Gloam/Resources/i18n/en-US.json` to `<tag>.json` (e.g. `ja-JP.json`).
2. Translate the values. Leave keys and placeholders untouched.
3. Run `dotnet test --filter FullyQualifiedName~Localization`. The validator reports missing keys,
   dropped or invented placeholders, missing plural forms and orphan translations.
4. Optionally add the language to `PluralRules` if it needs `few`/`many`/`zero` and does not already
   resolve to one of the implemented rule families. The validator will say so.

No code change, no registration: the picker is built from whatever catalogs are loaded.

### Shipping without a rebuild

A catalog dropped into `%LOCALAPPDATA%\Gloam\i18n\` overrides the embedded one key by key, so a
translation can be corrected — or a language added — on a deployed machine. A malformed file there
is skipped and reported in the diagnostics, never fatal: it is user-controlled input on the startup
path.

## Governance

`tests/Gloam.Tests/LocalizationAuditTests.cs` runs three audits:

| Audit | Catches |
| --- | --- |
| Every XAML key exists in the source catalog | A rename that leaves `<TextBlock Text="{loc:Tr old.key}"/>` rendering a raw key |
| Every catalog key is referenced in source | Dead keys translators keep translating |
| Hard-coded strings do not increase | A new literal added after the migration started |

The third is a ratchet against `tests/Gloam.Tests/Fixtures/localization/hardcoded-baseline.json`,
which records the current count per file. Adding user-visible text without a catalog fails the
build. Lowering a count is always allowed.

Regenerate the baseline only after a migration commit, and review the diff:

```powershell
$env:GLOAM_WRITE_LOCALIZATION_BASELINE = "1"; dotnet test; Remove-Item Env:GLOAM_WRITE_LOCALIZATION_BASELINE
```

The heuristics are deliberately conservative. Code that logs is skipped (log text is developer
signal and should not be translated — it would also put user-language strings into crash reports),
as are bindings, pack URIs, geometry data and short symbol runs.

## Migrating the remaining strings

Status: **the interface is translated.** 619 keys in both catalogs cover the tray menu, the
dashboard (first screen and its collapsible panels), Settings, the Game Lab window, the generic
dialogs, and the calibration flow up to and including the report window's chrome.

What is deliberately still English:

| Area | Why |
| --- | --- |
| `CalibrationReportWindow` body and `ReportPrintBuilder` | A printed artefact meant for a support engineer. Decide the audience before translating it. |
| Strings built by concatenation at runtime | They need a `Loc.Format` template with named arguments, not a lookup. Listed per file in the baseline. |
| Developer-facing text | Log lines, analyzer pragmas, resource keys, colours, automation ids. Excluded from the audit, since none of them can be translated. |

The ratchet now reads 708 candidate strings, down from 1540 before this pass. That number is a
trend line, not a to-do list: it counts literals the audit cannot classify, and roughly half of what
remains is inside the two report files above.

### Adding a language

1. Copy `en-US.json`, translate the values, save as `<tag>.json` next to it.
2. `dotnet test --filter Localization` — the validator checks placeholder sets, plural categories and empty values.
3. Ship. No code change, no rebuild of the UI: the picker lists whatever catalogs are embedded.

### Making a switch take effect

There are three kinds of text, and only the first updates itself:

| Where the string lives | What happens on a switch |
| --- | --- |
| `{loc:Tr key}` in XAML | Updates automatically — the binding source raises `PropertyChanged("Item[]")` |
| A `Loc.Get(key)` inside a getter on a view model | **Keeps the old language** unless the view model re-notifies |
| A value captured once into a field or a `static readonly` list | **Keeps the old language** unless it is rebuilt |

The second row is the subtle one, and it produces the worst symptom: an English window showing
Chinese in a few controls, because the XAML half updated and the bound half did not. Every view model
with localized getters must do this:

```csharp
_loc.CultureChanged += (_, _) => OnPropertyChanged(string.Empty);
```

An empty property name means "every property changed", so all bindings re-resolve at once.
Unsubscribe in `Dispose` — the localizer outlives the window, so a subscription left behind keeps a
closed view model alive.

For the third row, prefer a computed property over a cached one:

```csharp
// Wrong: captures the labels from whichever language was active at first use.
public IReadOnlyList<Option> Options { get; } = BuildOptions();

// Right: re-read on demand, so a switch is picked up by the re-notification above.
public IReadOnlyList<Option> Options => BuildOptions();
```

`TrayViewModel` is the reference for text built into a menu rather than bound (it rebuilds the menu);
`DashboardViewModel` is the reference for bound getters.

### Strings that should not move

- Log messages and exception messages that end up in logs.
- Values written to files or the registry (`"Gamma 2.2"` persisted in `settings.json`).
- CLI output (`Gloam.Cli` is a developer tool).
- Interop and colour-math constants.

## Fonts

The bundled faces (Plus Jakarta Sans, Space Grotesk, Cascadia Mono) have no CJK glyphs. WPF falls
back per glyph, which avoids tofu but produces mixed metrics and inconsistent weight. For
`zh`/`ja`/`ko` cultures `CultureFontPolicy` swaps the `BodyFont` / `DisplayFont` tokens for a
CJK-capable family; because the UI consumes them with `{DynamicResource}`, open windows update too.
New XAML should use `{DynamicResource BodyFont}`, not `{StaticResource}`.

## Known limits

- **Right-to-left is untested.** The layout is fixed LTR; adding Arabic or Hebrew needs `FlowDirection`
  work in the window chrome, not just a catalog.
- **Text inside images and SVG brand marks is not translatable.**
- **Number and date formatting follows `CultureInfo.CurrentCulture`**, which `LanguageService` sets
  alongside the UI culture. A culture that should format numbers differently from its UI language
  would need the two decoupled.
- **`MissingKeys` is not yet in the diagnostics export.** Wiring it into `DiagnosticsBundle` is the
  natural next step: it turns a user report into a list of keys that are actually missing on their
  machine.
