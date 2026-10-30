using System.Globalization;
using System.IO;
using Gloam.Localization;
using Xunit;

namespace Gloam.Tests;

/// <summary>
/// Covers the localization engine: culture resolution, fallback, template rendering, plural
/// selection, catalog parsing/merging and the cross-catalog validator.
/// </summary>
/// <remarks>
/// These tests are the executable specification for how the UI is expected to behave when a
/// translation is missing or wrong, which is the normal state of a partly translated app.
/// </remarks>
public class LocalizationCoreTests
{
    private const string EnglishJson = """
        {
          "culture": "en-US",
          "strings": {
            "app.name": "Gloam",
            "tray.monitorCount": { "one": "{count} monitor", "other": "{count} monitors" },
            "tray.gammaMode": "Gamma {version} on {monitor}",
            "dialog.literalBraces": "Use {{braces}} literally"
          }
        }
        """;

    private const string ChineseJson = """
        {
          "culture": "zh-Hans",
          "strings": {
            "app.name": "Gloam",
            "tray.monitorCount": { "other": "{count} 台显示器" },
            "tray.gammaMode": "在 {monitor} 上使用 Gamma {version}",
            "dialog.literalBraces": "使用 {{braces}} 字面量"
          }
        }
        """;

    private static StringCatalog English() => CatalogJson.Parse(EnglishJson, "en-US.json");

    private static StringCatalog Chinese() => CatalogJson.Parse(ChineseJson, "zh-Hans.json");

    private static Localizer CreateLocalizer(params StringCatalog[] catalogs)
    {
        var set = StringCatalogSet.Load(new InMemoryCatalogSource(catalogs));
        var options = new LocalizationOptions { SourceCulture = CultureInfo.GetCultureInfo("en-US") };
        return new Localizer(set, options, CultureInfo.GetCultureInfo("en-US"));
    }

    // ---- Culture resolution -------------------------------------------------------------

    [Theory]
    [InlineData("zh_CN", "zh-CN")]
    [InlineData("zh-cn", "zh-CN")]
    [InlineData("  en-US  ", "en-US")]
    [InlineData("zh", "zh")]
    public void TryParse_normalizes_the_tags_catalogs_and_settings_actually_contain(string input, string expected)
    {
        CultureInfo? culture = CultureChain.TryParse(input);
        Assert.NotNull(culture);
        Assert.Equal(expected, culture!.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("!!not-a-culture!!")]
    public void TryParse_returns_null_rather_than_throwing_on_garbage(string? input)
    {
        // Persisted settings are user-editable; a bad tag must not be fatal at startup.
        Assert.Null(CultureChain.TryParse(input));
    }

    [Fact]
    public void Build_puts_the_requested_culture_first_and_the_source_culture_last_without_duplicates()
    {
        IReadOnlyList<CultureInfo> chain = CultureChain.Build(
            CultureInfo.GetCultureInfo("zh-CN"), CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal("zh-CN", chain[0].Name);
        Assert.Equal("en-US", chain[^1].Name);
        Assert.Equal(chain.Count, chain.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // ---- Lookup and fallback ------------------------------------------------------------

    [Fact]
    public void Get_falls_back_to_the_source_language_for_a_key_the_translation_lacks()
    {
        StringCatalog zh = CatalogJson.Parse("""{ "culture": "zh-Hans", "strings": { "app.name": "Gloam" } }""");
        Localizer localizer = CreateLocalizer(English(), zh);
        localizer.SetCulture(CultureInfo.GetCultureInfo("zh-Hans"));

        Assert.Equal("Gamma {version} on {monitor}", localizer.Get("tray.gammaMode"));
        Assert.True(localizer.MissingKeys.IsEmpty, "falling back to the source language is not a miss");
    }

    [Fact]
    public void Get_returns_the_key_and_records_it_when_no_catalog_defines_it()
    {
        Localizer localizer = CreateLocalizer(English());

        Assert.Equal("dashboard.unknownKey", localizer.Get("dashboard.unknownKey"));
        Assert.Equal(1, localizer.MissingKeys.CountOf("dashboard.unknownKey"));
    }

    [Fact]
    public void Get_can_be_configured_to_return_empty_instead_of_the_key()
    {
        var set = StringCatalogSet.Load(new InMemoryCatalogSource(English()));
        var localizer = new Localizer(
            set,
            new LocalizationOptions { MissingKeyBehavior = MissingKeyBehavior.ReturnEmpty },
            CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal(string.Empty, localizer.Get("nope.key"));
    }

    [Fact]
    public void A_requested_culture_without_its_own_catalog_is_served_by_the_same_language()
    {
        // Windows reports zh-CN; only zh-Hans ships. The user must still get Chinese.
        Localizer localizer = CreateLocalizer(English(), Chinese());
        localizer.SetCulture(CultureInfo.GetCultureInfo("zh-CN"));

        Assert.Equal("在 {monitor} 上使用 Gamma {version}", localizer.Get("tray.gammaMode"));
    }

    [Fact]
    public void SetCulture_raises_the_change_event_once_with_previous_and_current()
    {
        Localizer localizer = CreateLocalizer(English(), Chinese());
        var events = new List<(string From, string To)>();
        localizer.CultureChanged += (_, e) => events.Add((e.Previous.Name, e.Current.Name));

        localizer.SetCulture(CultureInfo.GetCultureInfo("zh-Hans"));
        localizer.SetCulture(CultureInfo.GetCultureInfo("zh-Hans"));

        var change = Assert.Single(events);
        Assert.Equal("en-US", change.From);
        Assert.Equal("zh-Hans", change.To);
    }

    [Fact]
    public void SetCulture_to_an_unavailable_language_degrades_to_the_source_instead_of_throwing()
    {
        Localizer localizer = CreateLocalizer(English());
        localizer.SetCulture(CultureInfo.GetCultureInfo("de-DE"));

        Assert.Equal("en-US", localizer.Culture.Name);
        Assert.Equal("Gloam", localizer.Get("app.name"));
    }

    // ---- Rendering ----------------------------------------------------------------------

    [Fact]
    public void Format_substitutes_named_placeholders_so_translations_can_reorder_them()
    {
        Localizer localizer = CreateLocalizer(English(), Chinese());

        string english = localizer.Format("tray.gammaMode", ("version", "2.2"), ("monitor", "M27Q-P"));
        Assert.Equal("Gamma 2.2 on M27Q-P", english);

        localizer.SetCulture(CultureInfo.GetCultureInfo("zh-Hans"));
        string chinese = localizer.Format("tray.gammaMode", ("version", "2.2"), ("monitor", "M27Q-P"));
        Assert.Equal("在 M27Q-P 上使用 Gamma 2.2", chinese);
    }

    [Fact]
    public void Format_leaves_an_unresolved_placeholder_visible_so_the_bug_is_obvious()
    {
        Localizer localizer = CreateLocalizer(English());

        string rendered = localizer.Format("tray.gammaMode", ("version", "2.2"));

        Assert.Contains("{monitor}", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Doubled_braces_render_literally()
    {
        Localizer localizer = CreateLocalizer(English());
        Assert.Equal("Use {braces} literally", localizer.Format("dialog.literalBraces"));
    }

    [Fact]
    public void Numbers_are_formatted_in_the_active_culture()
    {
        const string json = """
            { "culture": "de-DE", "strings": { "size": "{value} MB" } }
            """;
        StringCatalog de = CatalogJson.Parse(json);
        Localizer localizer = CreateLocalizer(English(), de);
        localizer.SetCulture(CultureInfo.GetCultureInfo("de-DE"));

        Assert.Equal("1234,5 MB", localizer.Format("size", ("value", 1234.5)));
    }

    // ---- Plurals -------------------------------------------------------------------------

    [Fact]
    public void Plural_selects_english_forms_by_count()
    {
        Localizer localizer = CreateLocalizer(English());

        Assert.Equal("1 monitor", localizer.Format("tray.monitorCount", ("count", 1)));
        Assert.Equal("2 monitors", localizer.Format("tray.monitorCount", ("count", 2)));
        Assert.Equal("0 monitors", localizer.Format("tray.monitorCount", ("count", 0)));
    }

    [Fact]
    public void Plural_uses_the_single_form_a_language_without_grammatical_number_provides()
    {
        Localizer localizer = CreateLocalizer(English(), Chinese());
        localizer.SetCulture(CultureInfo.GetCultureInfo("zh-Hans"));

        Assert.Equal("1 台显示器", localizer.Format("tray.monitorCount", ("count", 1)));
        Assert.Equal("5 台显示器", localizer.Format("tray.monitorCount", ("count", 5)));
    }

    [Fact]
    public void Plural_category_selection_matches_cldr_for_slavic_counts()
    {
        CultureInfo ru = CultureInfo.GetCultureInfo("ru-RU");

        Assert.Equal(PluralCategory.One, PluralRules.Select(ru, 1));
        Assert.Equal(PluralCategory.Few, PluralRules.Select(ru, 3));
        Assert.Equal(PluralCategory.Many, PluralRules.Select(ru, 11));
        Assert.Equal(PluralCategory.Many, PluralRules.Select(ru, 0));
    }

    [Fact]
    public void A_plural_entry_without_the_needed_form_falls_back_to_other()
    {
        const string partial = """{ "culture": "en-US", "strings": { "n": { "other": "{count} items" } } }""";
        Localizer localizer = CreateLocalizer(CatalogJson.Parse(partial));

        Assert.Equal("1 items", localizer.Format("n", ("count", 1)));
    }

    // ---- Parsing -------------------------------------------------------------------------

    [Fact]
    public void Parse_accepts_comments_and_trailing_commas_because_translators_add_them()
    {
        const string json = """
            {
              // the app name is not translated
              "culture": "zh-Hans",
              "strings": {
                "app.name": "Gloam",
              },
            }
            """;

        StringCatalog catalog = CatalogJson.Parse(json);
        Assert.True(catalog.Contains("app.name"));
    }

    [Fact]
    public void Parse_rejects_an_unknown_plural_category_instead_of_ignoring_it()
    {
        const string json = """{ "culture": "en-US", "strings": { "n": { "singular": "x" } } }""";
        Assert.Throws<CatalogFormatException>(() => CatalogJson.Parse(json, "bad.json"));
    }

    [Fact]
    public void Parse_rejects_a_catalog_without_a_culture_tag()
    {
        const string json = """{ "strings": { "a": "b" } }""";
        Assert.Throws<CatalogFormatException>(() => CatalogJson.Parse(json));
    }

    [Fact]
    public void Serialize_round_trips_a_catalog_including_plural_forms()
    {
        StringCatalog original = English();
        StringCatalog reparsed = CatalogJson.Parse(CatalogJson.Serialize(original));

        Assert.Equal(original.Count, reparsed.Count);
        Assert.Equal("2 monitors", reparsed.Entries.First(e => e.Key == "tray.monitorCount").Value.SelectTemplate(PluralCategory.Other).Replace("{count}", "2"));
        Assert.True(reparsed.Entries.First(e => e.Key == "tray.monitorCount").Value.IsPlural);
    }

    // ---- Merging and external override ---------------------------------------------------

    [Fact]
    public void A_later_source_overrides_an_earlier_one_key_by_key()
    {
        var embedded = new InMemoryCatalogSource(English());
        StringCatalog overrideCatalog = CatalogJson.Parse("""{ "culture": "en-US", "strings": { "app.name": "Gloam Custom" } }""");
        var external = new InMemoryCatalogSource(overrideCatalog);

        StringCatalogSet set = StringCatalogSet.Load(new CompositeCatalogSource(embedded, external));
        var localizer = new Localizer(
            set,
            new LocalizationOptions { SourceCulture = CultureInfo.GetCultureInfo("en-US") },
            CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal("Gloam Custom", localizer.Get("app.name"));
        Assert.Equal("Gamma {version} on {monitor}", localizer.Get("tray.gammaMode"));
    }

    [Fact]
    public void A_catalog_file_dropped_into_the_external_folder_is_picked_up()
    {
        string folder = Path.Combine(Path.GetTempPath(), "gloam-i18n-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "en-US.json"), EnglishJson);
            File.WriteAllText(Path.Combine(folder, "zh-Hans.json"), ChineseJson);

            StringCatalogSet set = StringCatalogSet.Load(new DirectoryCatalogSource(folder));

            Assert.True(set.Contains("en-US"));
            Assert.True(set.Contains("zh-Hans"));
            Assert.Empty(set.LoadErrors);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_malformed_catalog_in_the_external_folder_is_reported_and_skipped_not_fatal()
    {
        string folder = Path.Combine(Path.GetTempPath(), "gloam-i18n-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "en-US.json"), EnglishJson);
            File.WriteAllText(Path.Combine(folder, "broken.json"), "{ not json");

            StringCatalogSet set = StringCatalogSet.Load(new DirectoryCatalogSource(folder));

            Assert.True(set.Contains("en-US"));
            Assert.Single(set.LoadErrors);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_missing_external_folder_is_not_an_error()
    {
        var source = new DirectoryCatalogSource(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        Assert.Empty(source.Load());
        Assert.Empty(source.Errors);
    }

    // ---- Validation ----------------------------------------------------------------------

    [Fact]
    public void Validator_reports_a_key_the_translation_never_defines()
    {
        StringCatalog zh = CatalogJson.Parse("""{ "culture": "zh-Hans", "strings": { "app.name": "Gloam" } }""");

        IReadOnlyList<CatalogIssue> issues = CatalogValidator.Validate(new[] { English(), zh }, CultureInfo.GetCultureInfo("en-US"));

        Assert.Contains(issues, i => i.Kind == CatalogIssueKind.MissingKey && i.Key == "tray.gammaMode");
    }

    [Fact]
    public void Validator_reports_a_translation_that_dropped_or_renamed_a_placeholder()
    {
        StringCatalog zh = CatalogJson.Parse("""
            {
              "culture": "zh-Hans",
              "strings": {
                "app.name": "Gloam",
                "tray.monitorCount": { "other": "{count} 台显示器" },
                "tray.gammaMode": "在 {display} 上使用 Gamma {version}",
                "dialog.literalBraces": "使用 {{braces}} 字面量"
              }
            }
            """);

        IReadOnlyList<CatalogIssue> issues = CatalogValidator.Validate(new[] { English(), zh }, CultureInfo.GetCultureInfo("en-US"));

        Assert.Contains(issues, i => i.Kind == CatalogIssueKind.PlaceholderMismatch && i.Message.Contains("monitor", StringComparison.Ordinal));
        Assert.Contains(issues, i => i.Kind == CatalogIssueKind.PlaceholderMismatch && i.Message.Contains("display", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_reports_a_translation_key_that_the_source_does_not_have()
    {
        StringCatalog zh = CatalogJson.Parse("""
            { "culture": "zh-Hans", "strings": {
                "app.name": "Gloam",
                "tray.monitorCount": { "other": "{count} 台显示器" },
                "tray.gammaMode": "在 {monitor} 上使用 Gamma {version}",
                "dialog.literalBraces": "使用 {{braces}} 字面量",
                "tray.typoKey": "多余"
            } }
            """);

        IReadOnlyList<CatalogIssue> issues = CatalogValidator.Validate(new[] { English(), zh }, CultureInfo.GetCultureInfo("en-US"));

        Assert.Contains(issues, i => i.Kind == CatalogIssueKind.OrphanTranslation && i.Key == "tray.typoKey");
    }

    [Fact]
    public void The_audit_does_not_count_catalog_keys_as_hard_coded_text()
    {
        // Regression: counting key literals made every migration *raise* the file's hard-coded
        // score, so the ratchet punished the work it exists to encourage.
        const string code = "var a = _loc.Get(\"tray.openDashboard\"); var b = \"Open Dashboard\";";

        SourceAuditResult result = LocalizationSourceAudit.AuditCSharp(code, "Tray.cs");

        var found = Assert.Single(result.Hardcoded);
        Assert.Equal("Open Dashboard", found.Text);
    }

    [Fact]
    public void Tray_menu_text_resolves_in_both_shipped_languages()
    {
        Localizer localizer = new(
            StringCatalogSet.Load(new EmbeddedCatalogSource(typeof(Gloam.Shell.App).Assembly, ShippedCatalogs.ResourcePrefix)),
            new LocalizationOptions { SourceCulture = CultureInfo.GetCultureInfo("en-US") },
            CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal("Exit", localizer.Get("tray.exit"));

        localizer.SetCulture(CultureInfo.GetCultureInfo("zh-Hans"));
        Assert.Equal("退出", localizer.Get("tray.exit"));
        Assert.Equal("Gloam 1.10.0", localizer.Format("tray.tooltip", ("version", "1.10.0")));
        Assert.True(localizer.MissingKeys.IsEmpty, "every tray key must exist in the catalog");
    }

    [Fact]
    public void The_shipped_catalogs_pass_validation()
    {
        // The catalogs that actually ship must be clean; this is what keeps a partially
        // translated release from shipping a literal {count} to a user.
        IReadOnlyList<CatalogIssue> issues = CatalogValidator.Validate(ShippedCatalogs.Load(), CultureChain.SourceCulture);
        Assert.Empty(issues);
    }
}
