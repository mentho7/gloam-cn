using System.Globalization;
using Gloam.Localization;
using Gloam.Localization.Wpf;

namespace Gloam.Tests;

/// <summary>
/// Gives the test process the same localizer the running app has, so tests see real text.
/// </summary>
/// <remarks>
/// Without this, <c>LocalizedStrings.Default</c> is the null object and every migrated string
/// resolves to its catalog key. Tests that assert on user-visible copy then fail for a reason that
/// has nothing to do with the behaviour under test — which is how a suite starts getting "fixed" by
/// asserting keys instead of sentences.
///
/// English is used because the source catalog is English, so an assertion written as
/// <c>Assert.Equal("Refresh", …)</c> keeps meaning what it says.
/// </remarks>
internal static class TestLocalization
{
    internal static void UseEnglish()
    {
        Localizer localizer = new(
            StringCatalogSet.Load(new EmbeddedCatalogSource(
                typeof(Gloam.Shell.App).Assembly, "Gloam.Resources.i18n.")),
            new LocalizationOptions { SourceCulture = CultureInfo.GetCultureInfo("en-US") },
            CultureInfo.GetCultureInfo("en-US"));

        LocalizedStrings.Default = new LocalizedStrings(localizer);
    }
}

/// <summary>
/// Restores the English catalog after a test that replaced <c>LocalizedStrings.Default</c>.
/// </summary>
/// <remarks>
/// The default is process-wide, so a test that publishes its own localizer silently changes the
/// language every later test sees. That produced failures in unrelated classes whose only sin was
/// running after this one.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
internal sealed class ResetsLocalizationAttribute : Xunit.Sdk.BeforeAfterTestAttribute
{
    public override void After(System.Reflection.MethodInfo methodUnderTest) => TestLocalization.UseEnglish();
}
