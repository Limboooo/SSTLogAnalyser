using SSTLogAnalyser.Models;
using SSTLogAnalyser.Services;

static class ComparisonCheck
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException("Baseline comparison: " + message); checks++; }
        TestResult Point(double expect, double error = 0.0002, double limit = 0.001, int line = 1) => new() {
            ModuleType = ModuleType.DPS, TestItemName = "FV -3..8", ChannelId = 0,
            ExpectValue = expect, MeasureValue = expect + error, Difference = error, LowLimit = -limit, UpLimit = limit, LineNumber = line
        };
        var before = new[] { Point(0), Point(1), Point(2, 0.0012), Point(3, 0.0013), Point(4), Point(5), Point(6), Point(8) };
        var after = new[] { Point(0, 0.0003), Point(1, 0.0012, 0.002), Point(2), Point(3, 0.0014), Point(4, 0.0008), Point(6, 0.0015), Point(7), Point(8), Point(8, 0.0003, line: 2) };
        var rows = BaselineLogComparer.Compare(before, after);
        BaselineComparisonRow Row(double x) => rows.Single(p => p.Expect == x);
        Check(Row(6).Status == "New failure", "new out-of-limit point");
        Check(Row(2).Status == "Recovered" && Row(2).AbsoluteErrorChange < 0, "recovery and absolute improvement");
        Check(Row(3).Status == "Persistent failure", "persistent failure");
        Check(Row(4).Status == "Near limit", "80 percent inclusive threshold");
        Check(Row(5).Status == "Missing point" && Row(7).Status == "Added point", "missing / added separated");
        Check(Row(8).Status == "Duplicate / ambiguous" && Row(8).CurrentPoints.Count == 2 && Row(8).ErrorChange == null, "duplicates not silently chosen");
        Check(Row(1).LimitsChanged && Row(1).Status == "Within limits" && Row(1).UnderBaselineLimits == "Fail", "looser limits do not hide baseline failure");
        Check(Math.Abs(Row(0).ErrorChange!.Value - 0.0001) < 1e-12 && Math.Abs(Row(4).CurrentMargin!.Value - 0.0002) < 1e-12, "error delta and margin");
        Check(BaselineLogComparer.Compare(new[] { Point(1) }, new[] { Point(1 + 1e-10) }).Count == 1, "serialization tolerance matches");
        Check(BaselineLogComparer.Compare(new[] { Point(1) }, new[] { Point(1.00001) }).Count == 2, "nearby distinct scan points never nearest-matched");
        Check(BaselineLogComparer.Compare(new[] { Point(1e-6) }, new[] { Point(2e-6) }).Count == 2, "microamp scan spacing preserved");
        var duplicate = BaselineLogComparer.Compare(new[] { Point(0), Point(0) }, Array.Empty<TestResult>()).Single();
        Check(duplicate.Status == "Duplicate / ambiguous", "duplicates remain ambiguous even without counterpart");
        var negative = BaselineLogComparer.Compare(new[] { Point(0, -0.0008) }, new[] { Point(0, -0.0002) }).Single();
        Check(negative.ErrorChange > 0 && negative.AbsoluteErrorChange < 0, "signed delta is not improvement indicator");
        var unknown = Point(0); unknown.ModuleType = ModuleType.TMU;
        Check(BaselineLogComparer.Compare(new[] { unknown }, new[] { unknown }).Single().Status == "Unknown / unsupported", "TMU overall never fabricated as numeric pass");
        var invalid = Point(0); invalid.UpLimit = invalid.LowLimit;
        Check(BaselineLogComparer.Compare(new[] { invalid }, new[] { invalid }).Single().CurrentError == null, "zero-width limits unknown");
        TestResult Mixi(double wave, double offset, string component = "POS") => new() {
            ModuleType = ModuleType.MIXI, TestItemName = "AWG WAVE OFFSET", ChannelId = 0,
            ExpectValue = wave + offset, MeasureValue = wave + offset + 0.0002, Difference = 0.0002,
            WaveValue = wave, OffsetValue = offset, ComponentType = component,
            LowLimit = wave + offset - 0.001, UpLimit = wave + offset + 0.001
        };
        var mixi = BaselineLogComparer.Compare(new[] { Mixi(0.5, 0.5), Mixi(0.75, 0.25) }, new[] { Mixi(0.75, 0.25), Mixi(0.5, 0.5) });
        Check(mixi.Count == 2 && mixi.All(p => p.Status == "Within limits" && !p.LimitsChanged), "MIXI same target different settings matched independently");
        Check(Math.Abs(mixi[0].CurrentUtilization!.Value - 0.2) < 1e-10, "MIXI absolute measurement limits converted to error limits");
        Check(BaselineLogComparer.Compare(new[] { Mixi(0.5, 0.5) }, new[] { Mixi(0.5, 0.5, "NEG") }).Count == 2, "MIXI component isolated");
        var changedItem = Point(0); changedItem.TestItemName = "Other";
        Check(BaselineLogComparer.Compare(new[] { Point(0) }, new[] { changedItem }).Count == 2, "different test item not aliased");
        var changedSlot = Point(0); changedSlot.SlotNumber = 1;
        Check(BaselineLogComparer.Compare(new[] { Point(0) }, new[] { changedSlot }).Count == 2, "known slot changes not silently matched");
        var chain = BaselineLogComparer.Compare(new[] { Point(1), Point(1 + 1.5e-9) }, new[] { Point(1 + 0.75e-9) });
        Check(chain.Count == 1 && chain[0].Status == "Duplicate / ambiguous", "multiple tolerance candidates ambiguous");
        var many = Enumerable.Range(0, 100_000).Select(i => new TestResult {
            ModuleType = ModuleType.DPS, TestItemName = "FV", ChannelId = i / 100, ExpectValue = i % 100,
            Difference = 0.0001, LowLimit = -0.001, UpLimit = 0.001 }).ToArray();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var matched = BaselineLogComparer.Compare(many, many);
        Check(matched.Count == many.Length && matched.All(p => p.Status == "Within limits"), "100000 point exact comparison");
        Console.WriteLine($"Comparison: {checks} checks passed; 100000 points compared in {watch.Elapsed.TotalSeconds:F3}s.");
        return checks;
    }
}
