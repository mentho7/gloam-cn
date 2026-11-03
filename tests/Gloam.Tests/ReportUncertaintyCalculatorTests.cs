using Gloam.Core.Calibration;
using Xunit;

namespace Gloam.Tests;

public class ReportUncertaintyCalculatorTests
{
    [Fact]
    public void PersistedDriftSurvivesAlreadyNormalizedMeasurements()
    {
        var measurements = new[] { Gray(0.5, 20), Gray(1, 100) };
        var result = ReportUncertaintyCalculator.Compute(measurements, measurements,
            StandardTargets.SrgbGamma22, 0.1, true, null, false);
        Assert.NotNull(result);
        Assert.True(result.DriftStdU > 0);
        var withoutDrift = ReportUncertaintyCalculator.Compute(measurements, measurements,
            StandardTargets.SrgbGamma22, 0, true, null, false);
        Assert.NotNull(withoutDrift);
        Assert.Equal(0, withoutDrift.DriftStdU);
    }

    private static MeasurementResult Gray(double signal, double y) => new()
    {
        Patch = new ColorPatch { Name = "Gray", DisplayRgb = new LinearRgb(signal, signal, signal), Category = PatchCategory.Grayscale },
        Xyz = new CieXyz(y * 0.95047, y, y * 1.08883),
        IsValid = true,
        ReadingCount = 1
    };
}
