using Gloam.Localization;

namespace Gloam.Tests;

/// <summary>
/// Loads the catalogs that actually ship inside the application assembly, exactly the way the app
/// loads them at startup.
/// </summary>
internal static class ShippedCatalogs
{
    /// <summary>Resource-name prefix used by Gloam.csproj for <c>Resources/i18n/*.json</c>.</summary>
    public const string ResourcePrefix = "Gloam.Resources.i18n.";

    public static IReadOnlyList<StringCatalog> Load()
        => new EmbeddedCatalogSource(typeof(Gloam.Shell.App).Assembly, ResourcePrefix).Load();
}
