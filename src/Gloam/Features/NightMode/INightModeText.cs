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
    public string AlgorithmLabel(NightModeAlgorithm algorithm) => algorithm switch
    {
        NightModeAlgorithm.Perceptual => localizer.Get("night.profile.perceptual"),
        NightModeAlgorithm.UltraNight => localizer.Get("night.profile.ultra"),
        NightModeAlgorithm.AccurateCIE1931 => localizer.Get("night.profile.accurate"),
        NightModeAlgorithm.Standard => localizer.Get("night.profile.classic"),
        _ => string.Empty,
    };

    public string AlgorithmDescription(NightModeAlgorithm algorithm) => algorithm switch
    {
        NightModeAlgorithm.Perceptual => localizer.Get("nightmode.warm.white.point.eased.toward.neutral.partial.chromatic"),
        NightModeAlgorithm.AccurateCIE1931 => localizer.Get("nightmode.true.black.body.white.point.at.full.strength"),
        NightModeAlgorithm.Standard => localizer.Get("nightmode.classic.photo.style.warm.tint.tanner.helland.pair.with"),
        NightModeAlgorithm.UltraNight => localizer.Get("nightmode.amber.red.for.maximum.circadian.protection.cuts.90.of"),
        _ => string.Empty,
    };

    public string AlgorithmSummary(NightModeAlgorithm algorithm) => algorithm switch
    {
        NightModeAlgorithm.Perceptual => localizer.Get("dashboard.balanced.color.preservation"),
        NightModeAlgorithm.UltraNight => localizer.Get("dashboard.amber.red.maximum.protection"),
        NightModeAlgorithm.AccurateCIE1931 => localizer.Get("dashboard.full.cie.white.point.shift"),
        NightModeAlgorithm.Standard => localizer.Get("dashboard.classic.warm.tint"),
        _ => string.Empty,
    };
}
