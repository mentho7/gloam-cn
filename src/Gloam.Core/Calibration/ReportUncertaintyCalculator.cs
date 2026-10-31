namespace Gloam.Core.Calibration;

/// <summary>Report uncertainty calculation independent of windows and settings storage.</summary>
public static class ReportUncertaintyCalculator
{
    public static UncertaintyBudget.Result? Compute(
        IReadOnlyList<MeasurementResult>? measurements, IEnumerable<MeasurementResult> gradedMeasurements,
        CalibrationTarget target, double? peakWhiteDriftFraction, bool driftCompensationApplied,
        string? colorimeterModel, bool hasCorrection)
    {
        if (measurements == null || measurements.Count == 0) return null;
        try
        {
            var noiseModel = LuminanceNoiseModel.FromMeasurements(measurements);

            // Drift term: prefer the run's PERSISTED pre-compensation peak drift
            // (CalibrationResult.PeakWhiteDriftFraction). measurements have ALREADY been
            // drift-normalized by the orchestrator, so re-analyzing them here is
            // idempotent-to-zero and would silently drop the drift-residual term for
            // every compensated run. Fall back to re-analysis only when the persisted
            // fraction is unavailable (e.g. a historical report re-opened from a saved
            // profile) — there the measurements are whatever the profile carried.
            double? peakDrift;
            bool driftApplied;
            if (peakWhiteDriftFraction is double persistedPeak)
            {
                peakDrift = persistedPeak;
                driftApplied = driftCompensationApplied;
            }
            else
            {
                var drift = DriftCompensator.Compensate(measurements);
                peakDrift = drift.MaxWhiteDriftFraction;
                driftApplied = drift.Applied;
            }

            var instrument = UncertaintyBudget.ClassifyInstrument(colorimeterModel, hasCorrection);

            var context = new UncertaintyBudget.Context(
                instrument, noiseModel, peakDrift, driftApplied);
            CalibrationVerifier.ComputeMetrics(gradedMeasurements, target, context, out var uncertainty);
            return uncertainty;
        }
        catch (Exception ex)
        {
            Log.Info($"CalibrationReportWindow: uncertainty budget failed (report continues without it): {ex.Message}");
            return null;
        }
    }
}
