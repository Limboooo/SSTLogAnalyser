using SSTLogAnalyser.ViewModels;

static class AcRelativeCheck
{
    public static int Run(MainViewModel model, bool fullLog)
    {
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException("AC relative: " + message); checks++; }
        model.SelectedModule = "AC"; model.SelectedAcEdge = "RISE"; model.AcRelativeMode = true;
        var table = model.AcAlignmentSets.First(t => t.Clock == "500.000 MHz" && t.Target == 10);
        model.SelectedAcAlignmentSet = table; model.SelectedAcReferencePin = 0;
        Check(model.AcRelativeRows.Count == (fullLog ? 1280 : 3), "one clock, edge, target and table includes all unique pins");
        Check(model.AcRelativeRows.Single(r => r.Pin == 0).RelativeNs == 0, "reference pin is exactly zero");
        Check(Math.Abs(model.AcRelativeRows.Single(r => r.Pin == 1).RelativePs + 78) < 1e-6, "real Pin1 is 78ps earlier than Pin0 at 500MHz, target10ns, RISE");
        Check(model.AcErrorSeries.Length == 3 && model.AcRangeSeries.Length == 0, "relative overview contains group bars, zero line and selection marker");
        var bars = model.AcErrorSeries.OfType<LiveChartsCore.SkiaSharpView.ColumnSeries<LiveChartsCore.Defaults.ObservablePoint>>().Single();
        Check(bars.Values!.Count() == model.AcGroupRows.Count && bars.Values!.All(p => Math.Abs(p.Y!.Value - model.AcGroupRows.Single(g => g.Group == p.X).GroupOffsetPs) < 1e-9), "one bar per group uses mean relative to base group in ps");
        Check(model.SelectedAcDetailGroup!.Group == model.AcGroupRows.MaxBy(g => Math.Abs(g.GroupOffsetPs))!.Group, "initial detail selects greatest group offset");
        Check(model.AcPinDetailSeries.OfType<LiveChartsCore.SkiaSharpView.ScatterSeries<LiveChartsCore.Defaults.ObservablePoint>>().First().Values!.Count() == (fullLog ? 64 : 3), "detail scatter only shows one group");
        Check(model.AcGroupRows.Count == (fullLog ? 20 : 1) && model.AcGroupRows.Sum(g => g.Count) == model.AcRelativeRows.Count, "AK/DK uses 64-pin groups");
        model.SelectedAcGroupSize = 128;
        Check(model.AcGroupRows.Count == (fullLog ? 10 : 1), "standard uses 128-pin groups");
        Check(model.AcPinDetailSeries.OfType<LiveChartsCore.SkiaSharpView.ScatterSeries<LiveChartsCore.Defaults.ObservablePoint>>().First().Values!.Count() == (fullLog ? 128 : 3), "128-pin detail does not mix groups");
        model.SelectedAcGroupSize = 64;
        var otherClock = model.AcAlignmentSets.First(t => t.Clock == "250.000 MHz" && t.Target == 10);
        model.SelectedAcAlignmentSet = otherClock;
        Check(model.AcRelativeRows.Single(r => r.Pin == 1).RelativeNs == 0, "same target at 250MHz stays separate");
        model.SelectedAcAlignmentSet = table; model.SelectedAcReferencePin = 1;
        Check(Math.Abs(model.AcRelativeRows.Single(r => r.Pin == 0).RelativePs - 78) < 1e-6, "reference switch reverses sign");
        model.SelectedAcReferencePin = 99999;
        Check(model.AcRelativeRows.Count == 0 && model.AcErrorSeries.Length == 0, "missing reference cannot become zero");
        model.SelectedAcReferencePin = 0;
        var duplicate = table.Points.First(p => p.ChannelId == 0 && p.TestItemName == "AC RISE(ns)");
        model.SelectedAcAlignmentSet = table with { Points = table.Points.Concat(new[] { duplicate }).ToArray() };
        Check(model.AcRelativeRows.Count == 0, "duplicate reference blocks comparison");
        model.SelectedAcAlignmentSet = table; model.SelectedAcReferencePin = 1;
        model.SelectedAcAlignmentSet = table with { Points = table.Points.Concat(new[] { duplicate }).ToArray() };
        Check(model.AcRelativeRows.All(r => r.Pin != 0) && model.AcRelativeRows.Count == (fullLog ? 1279 : 2), "duplicate non-reference pin excluded");
        model.SelectedAcAlignmentSet = table with { Clock = "Clock unspecified" };
        Check(model.AcRelativeRows.Count == 0, "unknown clock cannot be silently matched");
        var baseMeasure = table.Points.First(p => p.ChannelId == 0 && p.TestItemName == "AC RISE(ns)").MeasureValue;
        var boundaryPoints = new[] { (0, 0d), (63, 0.078), (64, 0.156), (127, 0.234), (128, 0.312) }
            .Select(p => new SSTLogAnalyser.Models.TestResult { ChannelId = p.Item1, MeasureValue = baseMeasure + p.Item2, TestItemName = "AC RISE(ns)" }).ToArray();
        model.SelectedAcReferencePin = 0;
        model.SelectedAcAlignmentSet = table with { Points = boundaryPoints };
        Check(model.AcGroupRows.Count == 3 && model.AcRelativeRows.Single(r => r.Pin == 64).Group == 1 && model.AcRelativeRows.Single(r => r.Pin == 128).Group == 2, "64-pin grouping boundaries");
        Check(Math.Abs(model.AcGroupRows[1].GroupOffsetPs - 156) < 1e-6 && Math.Abs(model.AcGroupRows[0].SpanPs - 78) < 1e-6, "group offsets use group means, span is within-group");
        model.SelectedAcGroupSize = 128;
        Check(model.AcGroupRows.Count == 2 && Math.Abs(model.AcGroupRows[1].GroupOffsetPs - 195) < 1e-6, "128-pin mean offsets recomputed");
        model.SelectedAcGroupSize = 64;
        model.SelectedAcAlignmentSet = table; model.SelectedAcReferencePin = 0;
        model.SelectedAcDetailGroup = model.AcGroupRows.Last();
        Check(model.AcDetailPinAxes[0].MinLimit == model.AcGroupRows.Last().FirstPin - 0.5, "group selection moves pin detail range");
        model.SelectedAcDetailGroup = model.AcGroupRows.MaxBy(g => Math.Abs(g.GroupOffsetPs));
        Console.WriteLine($"AC relative: {checks} checks passed; Pin1-Pin0 = -78ps at 500MHz/10ns/RISE.");
        return checks;
    }
}
