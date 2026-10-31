namespace Gloam.Core;

/// <summary>Pure request-time priority resolution; no persistence or hardware writes.</summary>
internal static class GammaApplyPolicy
{
    internal sealed record Input(GammaMode Mode, bool ManualCalibration, int? KelvinOverride,
        int ScheduledKelvin, bool NightBlocked, bool ManuallyDisabled,
        GamerProfileRule? Game, int LockedKelvin);

    internal readonly record struct Result(GammaMode Mode, int Kelvin, bool NightModeActive, bool UseGameProfile);

    internal static Result Resolve(Input input)
    {
        bool useGame = input.Game != null && !input.ManualCalibration && !input.KelvinOverride.HasValue;
        int kelvin = input.KelvinOverride ?? input.ScheduledKelvin;
        if (input.NightBlocked) kelvin = 6500;
        var mode = input.Mode;
        if (useGame)
        {
            var game = input.Game!;
            int baseline = NightModeSettings.ClampKelvin(game.GameplayLock ? input.LockedKelvin : kelvin);
            kelvin = game.NightPolicy switch
            {
                GamerNightPolicy.ForceDaylight => 6500,
                GamerNightPolicy.NightOps => Math.Min(baseline, game.NightOpsKelvin),
                _ => baseline
            };
            if (game.OverrideGamma) mode = game.GammaMode;
        }
        // The manual hotkey remains authoritative even for Night Ops.
        if (input.ManuallyDisabled) kelvin = 6500;
        return new(mode, kelvin, input.KelvinOverride.HasValue || kelvin < 6450, useGame);
    }
}
