using Gloam.Core;

namespace Gloam.Features.NightMode;

/// <summary>Language-independent vocabulary for rendering controls. Values remain domain enums.</summary>
public interface INightModeText
{
    string AlgorithmLabel(NightModeAlgorithm algorithm);
    string AlgorithmDescription(NightModeAlgorithm algorithm);
    string AlgorithmSummary(NightModeAlgorithm algorithm);
}

/// <summary>One resource-backed strategy for every catalog; translations stay in JSON.</summary>
public sealed class CatalogNightModeText(ILocalizer localizer) : INightModeText
{
    /// <summary>
    /// Fallback for an enum value this build does not know: a persisted profile written by a newer
    /// version, or a hand-edited settings file. Rendering empty strings left three blank rows in
    /// the UI with no exception and no log line, so the user could neither read nor diagnose the
    /// setting; one visible "unknown" entry at least names the problem.
    /// </summary>
    private static readonly (string Label, string Description, string Summary) UnknownCatalog =
        ("night.profile.unknown", "night.mode.unknown.description", "night.mode.unknown.summary");

    public string AlgorithmLabel(NightModeAlgorithm algorithm) => localizer.Get(Catalog(algorithm).Label);

    public string AlgorithmDescription(NightModeAlgorithm algorithm) => localizer.Get(Catalog(algorithm).Description);

    public string AlgorithmSummary(NightModeAlgorithm algorithm) => localizer.Get(Catalog(algorithm).Summary);

    /// <summary>
    /// Single key table for all three views of an algorithm. Kept as one switch so the fallback
    /// case exists in exactly one place: three separate switches previously drifted apart and each
    /// one degraded on its own.
    /// </summary>
    private (string Label, string Description, string Summary) Catalog(NightModeAlgorithm algorithm) =>
        algorithm switch
        {
            NightModeAlgorithm.Perceptual => (
                "night.profile.perceptual",
                "nightmode.warm.white.point.eased.toward.neutral.partial.chromatic",
                "dashboard.balanced.color.preservation"),
            NightModeAlgorithm.UltraNight => (
                "night.profile.ultra",
                "nightmode.amber.red.for.maximum.circadian.protection.cuts.90.of",
                "dashboard.amber.red.maximum.protection"),
            NightModeAlgorithm.AccurateCIE1931 => (
                "night.profile.accurate",
                "nightmode.true.black.body.white.point.at.full.strength",
                "dashboard.full.cie.white.point.shift"),
            NightModeAlgorithm.Standard => (
                "night.profile.classic",
                "nightmode.classic.photo.style.warm.tint.tanner.helland.pair.with",
                "dashboard.classic.warm.tint"),
            _ => UnknownCatalog,
        };
}
