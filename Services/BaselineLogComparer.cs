using SSTLogAnalyser.Models;

namespace SSTLogAnalyser.Services;

public static class BaselineLogComparer
{
    // Floating-point serialization tolerance, not a nearest-point matching rule.
    public static bool NearlyEqual(double a, double b) => double.IsFinite(a) && double.IsFinite(b) &&
        Math.Abs(a - b) <= 1e-12 + 1e-9 * Math.Max(Math.Abs(a), Math.Abs(b));

    public static (double Low, double High) ErrorLimits(TestResult point) => point.ModuleType == ModuleType.MIXI
        ? (point.LowLimit - point.ExpectValue, point.UpLimit - point.ExpectValue)
        : (point.LowLimit, point.UpLimit);

    public static bool IsNumeric(TestResult point)
    {
        var (low, high) = ErrorLimits(point);
        return point.ModuleType is not (ModuleType.Unknown or ModuleType.AC or ModuleType.TMU) &&
            double.IsFinite(point.ExpectValue) && double.IsFinite(point.Difference) &&
            double.IsFinite(low) && double.IsFinite(high) && high > low;
    }

    public static List<BaselineComparisonRow> Compare(IReadOnlyList<TestResult> baseline, IReadOnlyList<TestResult> current)
    {
        var rows = new List<BaselineComparisonRow>();
        var groups = baseline.Select(p => (Point: p, Baseline: true))
            .Concat(current.Select(p => (Point: p, Baseline: false)))
            .GroupBy(p => (p.Point.ModuleType, p.Point.TestItemName, p.Point.SlotNumber, p.Point.ChannelId, p.Point.ComponentType));
        foreach (var group in groups)
        {
            var points = group.OrderBy(p => p.Point.ExpectValue).ToArray();
            var parent = Enumerable.Range(0, points.Length).ToArray();
            int Root(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
            for (int i = 0; i < points.Length; i++)
                for (int j = i + 1; j < points.Length; j++)
                {
                    if (!NearlyEqual(points[i].Point.ExpectValue, points[j].Point.ExpectValue)) break;
                    if (SameSettings(points[i].Point, points[j].Point)) parent[Root(j)] = Root(i);
                }
            foreach (var cluster in Enumerable.Range(0, points.Length).GroupBy(Root))
            {
                var oldPoints = cluster.Where(i => points[i].Baseline).Select(i => points[i].Point).ToArray();
                var newPoints = cluster.Where(i => !points[i].Baseline).Select(i => points[i].Point).ToArray();
                // Multiple possible matches on either side remain ambiguous, including retests.
                rows.Add(Create(oldPoints, newPoints));
            }
        }
        return rows.OrderBy(p => Priority(p.Status)).ThenByDescending(p => p.LimitsChanged)
            .ThenBy(p => p.Module).ThenBy(p => p.TestItem, StringComparer.Ordinal).ThenBy(p => p.Channel).ThenBy(p => p.Expect).ToList();
    }

    private static bool SameSettings(TestResult a, TestResult b) => a.ModuleType != ModuleType.MIXI ||
        (Same(a.WaveValue, b.WaveValue) && Same(a.OffsetValue, b.OffsetValue) && Same(a.DiffValue, b.DiffValue));
    private static bool Same(double? a, double? b) => a.HasValue == b.HasValue && (!a.HasValue || NearlyEqual(a.Value, b!.Value));

    private static BaselineComparisonRow Create(TestResult[] baseline, TestResult[] current)
    {
        var old = baseline.Length == 1 ? baseline[0] : null;
        var now = current.Length == 1 ? current[0] : null;
        if (baseline.Length > 1 || current.Length > 1) return Row("Duplicate / ambiguous", false, true);
        if (old == null) return Row("Added point", IsNumeric(now!), true);
        if (now == null) return Row("Missing point", IsNumeric(old), true);
        if (!IsNumeric(old) || !IsNumeric(now)) return Row("Unknown / unsupported", false, true);
        var (oldLow, oldHigh) = ErrorLimits(old);
        var (newLow, newHigh) = ErrorLimits(now);
        var changed = !NearlyEqual(oldLow, newLow) || !NearlyEqual(oldHigh, newHigh);
        var oldFail = old.IsFailed || old.Difference < oldLow || old.Difference > oldHigh;
        var newFail = now.IsFailed || now.Difference < newLow || now.Difference > newHigh;
        var oldUtil = Utilization(old.Difference, oldLow, oldHigh);
        var newUtil = Utilization(now.Difference, newLow, newHigh);
        var status = !oldFail && newFail ? "New failure" : oldFail && !newFail ? "Recovered" :
            oldFail && newFail ? "Persistent failure" : newUtil >= 0.8 ? "Near limit" : "Within limits";
        return new BaselineComparisonRow {
            BaselinePoints = baseline, CurrentPoints = current, Status = status, CanEvaluate = true,
            LimitsChanged = changed, NeedsAttention = changed || newFail || newUtil >= 0.8,
            BaselineUtilization = oldUtil, CurrentUtilization = newUtil,
            CurrentMargin = Math.Min(now.Difference - newLow, newHigh - now.Difference),
            UnderBaselineLimits = now.Difference < oldLow || now.Difference > oldHigh ? "Fail" : "Pass",
            BaselineVerdict = Verdict(old, oldFail), CurrentVerdict = Verdict(now, newFail)
        };
        BaselineComparisonRow Row(string label, bool numeric, bool attention) => new() {
            BaselinePoints = baseline, CurrentPoints = current, Status = label, CanEvaluate = numeric, NeedsAttention = attention,
            BaselineVerdict = numeric && old != null ? Verdict(old, old.IsFailed || old.Difference < ErrorLimits(old).Low || old.Difference > ErrorLimits(old).High) : "Unknown",
            CurrentVerdict = numeric && now != null ? Verdict(now, now.IsFailed || now.Difference < ErrorLimits(now).Low || now.Difference > ErrorLimits(now).High) : "Unknown"
        };
    }
    private static double Utilization(double error, double low, double high) => Math.Abs((error - (low + high) / 2) / ((high - low) / 2));
    private static string Verdict(TestResult point, bool failed) => (failed ? "Fail" : "Pass") + (point.HasExplicitResult ? " (recorded)" : " (calculated)");
    private static int Priority(string status) => status switch {
        "New failure" => 0, "Missing point" => 1, "Duplicate / ambiguous" => 2, "Persistent failure" => 3,
        "Unknown / unsupported" => 4, "Added point" => 5, "Near limit" => 6, "Recovered" => 7, _ => 8 };
}
