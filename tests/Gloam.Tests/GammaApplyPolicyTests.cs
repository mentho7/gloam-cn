using Gloam.Core;
using Xunit;

namespace Gloam.Tests;

public class GammaApplyPolicyTests
{
    [Theory]
    [InlineData(false, false, 2200)]
    [InlineData(true, false, 2200)]
    [InlineData(false, true, 6500)]
    [InlineData(true, true, 6500)]
    public void NightOpsAndManualOverridePreservePriority(bool blocked, bool disabled, int expected)
    {
        var game = new GamerProfileRule { NightPolicy = GamerNightPolicy.NightOps, NightOpsKelvin = 2200, GameplayLock = false };
        var result = GammaApplyPolicy.Resolve(new(default, false, null, 2700, blocked, disabled, game, 3000));
        Assert.Equal(expected, result.Kelvin);
    }

    [Fact]
    public void ExplicitPreviewSuppressesGameAndStillRespectsExclusion()
    {
        var game = new GamerProfileRule { NightPolicy = GamerNightPolicy.NightOps, NightOpsKelvin = 2200 };
        var result = GammaApplyPolicy.Resolve(new(default, false, 4000, 2700, true, false, game, 3000));
        Assert.False(result.UseGameProfile);
        Assert.Equal(6500, result.Kelvin);
        Assert.True(result.NightModeActive);
    }
}
