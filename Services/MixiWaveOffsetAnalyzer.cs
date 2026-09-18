using SSTLogAnalyser.Models;

namespace SSTLogAnalyser.Services;

public sealed record WaveOffsetContribution(
    string Path,
    double Target,
    double SignedWave,
    double Offset,
    double Residual,
    double WaveContribution,
    double OffsetContribution,
    double BiasContribution,
    double PredictedResidual,
    bool IsFailed);

public sealed record WaveOffsetAttribution(
    double WaveSlope,
    double OffsetSlope,
    double Bias,
    double RSquared,
    double Rmse,
    double WaveEffectRms,
    double OffsetEffectRms,
    double MaxAbsResidual,
    IReadOnlyList<WaveOffsetContribution> Points)
{
    public double Severity => Math.Max(Math.Max(Math.Abs(WaveEffectRms), Math.Abs(OffsetEffectRms)), Math.Abs(Bias));

    public string Confidence => RSquared >= 0.85 ? "High" : RSquared >= 0.6 ? "Medium" : "Low";

    public string LikelyCause
    {
        get
        {
            if (RSquared < 0.5) return "Mixed / low confidence";

            var effects = new[]
            {
                (Name: "WAVE CAL", Value: Math.Abs(WaveEffectRms)),
                (Name: "OFFSET CAL", Value: Math.Abs(OffsetEffectRms)),
                (Name: "Common bias", Value: Math.Abs(Bias))
            }.OrderByDescending(effect => effect.Value).ToArray();

            if (effects[0].Value <= Math.Max(Rmse, 1e-12)) return "No dominant CAL bias";
            if (effects[1].Value > 0 && effects[0].Value < effects[1].Value * 1.35)
                return "Mixed WAVE / OFFSET";
            return effects[0].Name;
        }
    }
}

public static class MixiWaveOffsetAnalyzer
{
    private sealed record Sample(string Path, double Target, double SignedWave, double Offset, double Residual, bool IsFailed);

    public static bool TryAnalyze(IEnumerable<TestResult> results, out WaveOffsetAttribution attribution)
    {
        var samples = results
            .Where(result => result.WaveValue.HasValue && result.OffsetValue.HasValue)
            .Select(CreateSample)
            .Where(sample => sample != null)
            .Cast<Sample>()
            .ToArray();

        attribution = Empty();
        if (samples.Length < 4) return false;

        var meanWave = samples.Average(sample => sample.SignedWave);
        var meanOffset = samples.Average(sample => sample.Offset);
        var meanResidual = samples.Average(sample => sample.Residual);

        var waveVariance = samples.Sum(sample => Square(sample.SignedWave - meanWave));
        var offsetVariance = samples.Sum(sample => Square(sample.Offset - meanOffset));
        var covariance = samples.Sum(sample =>
            (sample.SignedWave - meanWave) * (sample.Offset - meanOffset));
        var waveResidualCovariance = samples.Sum(sample =>
            (sample.SignedWave - meanWave) * (sample.Residual - meanResidual));
        var offsetResidualCovariance = samples.Sum(sample =>
            (sample.Offset - meanOffset) * (sample.Residual - meanResidual));

        var determinant = waveVariance * offsetVariance - covariance * covariance;
        var scale = Math.Max(waveVariance * offsetVariance, 1e-30);
        if (!double.IsFinite(determinant) || Math.Abs(determinant) <= scale * 1e-12) return false;

        var waveSlope = (waveResidualCovariance * offsetVariance - offsetResidualCovariance * covariance) / determinant;
        var offsetSlope = (offsetResidualCovariance * waveVariance - waveResidualCovariance * covariance) / determinant;
        var bias = meanResidual - waveSlope * meanWave - offsetSlope * meanOffset;
        if (!double.IsFinite(waveSlope) || !double.IsFinite(offsetSlope) || !double.IsFinite(bias)) return false;

        var points = samples.Select(sample =>
        {
            var waveContribution = waveSlope * sample.SignedWave;
            var offsetContribution = offsetSlope * sample.Offset;
            var predicted = waveContribution + offsetContribution + bias;
            return new WaveOffsetContribution(
                sample.Path,
                sample.Target,
                sample.SignedWave,
                sample.Offset,
                sample.Residual,
                waveContribution,
                offsetContribution,
                bias,
                predicted,
                sample.IsFailed);
        }).ToArray();

        var residualSumSquares = points.Sum(point => Square(point.Residual - point.PredictedResidual));
        var totalSumSquares = samples.Sum(sample => Square(sample.Residual - meanResidual));
        var rSquared = totalSumSquares <= 1e-30
            ? residualSumSquares <= 1e-30 ? 1 : 0
            : 1 - residualSumSquares / totalSumSquares;
        var rmse = Math.Sqrt(residualSumSquares / samples.Length);
        var waveEffectRms = SignedRms(waveSlope, samples.Select(sample => sample.SignedWave));
        var offsetEffectRms = SignedRms(offsetSlope, samples.Select(sample => sample.Offset));

        attribution = new WaveOffsetAttribution(
            waveSlope,
            offsetSlope,
            bias,
            rSquared,
            rmse,
            waveEffectRms,
            offsetEffectRms,
            points.Max(point => Math.Abs(point.Residual)),
            points);
        return true;
    }

    private static Sample? CreateSample(TestResult result)
    {
        var wave = result.WaveValue!.Value;
        var offset = result.OffsetValue!.Value;
        var path = GetPath(result);
        if (!double.IsFinite(wave) || !double.IsFinite(offset) || path == null) return null;

        var signedWave = path == "NEG" ? -wave : wave;
        var composedTarget = signedWave + offset;
        var tolerance = Math.Max(1e-8, Math.Abs(result.ExpectValue) * 1e-8);
        if (Math.Abs(result.ExpectValue - composedTarget) > tolerance) return null;

        return new Sample(path, result.ExpectValue, signedWave, offset, result.Difference, result.IsFailed);
    }

    private static string? GetPath(TestResult result)
    {
        var component = result.ComponentType.Trim().ToUpperInvariant();
        if (component.Contains("NEG", StringComparison.Ordinal)) return "NEG";
        if (component.Contains("POS", StringComparison.Ordinal)) return "POS";

        var testItem = result.TestItemName.Trim().ToUpperInvariant();
        if (testItem.EndsWith("_NEG", StringComparison.Ordinal)) return "NEG";
        if (testItem.EndsWith("_POS", StringComparison.Ordinal)) return "POS";
        return null;
    }

    private static double SignedRms(double slope, IEnumerable<double> inputs)
    {
        var values = inputs.ToArray();
        if (values.Length == 0) return 0;
        return Math.Sign(slope) * Math.Sqrt(values.Average(input => Square(slope * input)));
    }

    private static double Square(double value) => value * value;

    private static WaveOffsetAttribution Empty() =>
        new(0, 0, 0, 0, 0, 0, 0, 0, Array.Empty<WaveOffsetContribution>());
}
