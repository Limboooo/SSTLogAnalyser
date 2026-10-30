using System.IO;
using SSTLogAnalyser.ViewModels;

static class AcPointCheck
{
    public static async Task<int> Run(MainViewModel model, string fixturePath)
    {
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException("AC points: " + message); checks++; }
        var original = model.SelectedAcAlignmentSet!.Title;
        await model.LoadFilesFromPathsAsync(new[] { fixturePath });
        model.SelectedAcEdge = "RISE"; model.AcRelativeMode = true; model.AcPointView = true;
        var tables = model.AcAlignmentSets.Where(t => t.Title.StartsWith("34_AC_Point_Scopes.log")).ToArray();
        var first = tables.First(); model.SelectedAcAlignmentSet = first;
        model.SelectedAcReferencePin = 0; model.SelectedAcPointDimension = "Groups";
        Check(model.AcPointCells.Count == 4 && model.AcPointCells.Select(c => c.TableLine).Distinct().Count() == 4, "four points from one file/Loop/clock/sweep only");
        Check(Math.Abs(model.AcPointCells.Single(c => c.Column == 0).ValuePs - 39) < 1e-6, "group mean target error at P0");
        Check(Math.Abs(model.AcPointCells.Single(c => c.Column == 1).ValuePs - 39) < 1e-6, "group mean target error at P1");
        Check(Math.Abs(model.AcPointCells.Single(c => c.Column == 2).ValuePs - 156) < 1e-6 && model.AcPointCells.Single(c => c.Column == 2).Count == 1, "missing pin is not zero-filled");
        Check(model.AcPointCells.Single(c => c.Column == 3).Count == 1 && model.AcPointCells.Single(c => c.Column == 3).ValuePs == 0, "duplicate pin excluded from mean");
        model.SelectedAcPointMetric = "Offset vs base pin";
        Check(model.AcPointCells.Count == 3 && model.AcPointCells.All(c => c.Column != 2), "missing reference leaves a gap for whole column");
        Check(Math.Abs(model.AcPointCells.Single(c => c.Column == 0).ValuePs - 39) < 1e-6 && Math.Abs(model.AcPointCells.Single(c => c.Column == 1).ValuePs + 39) < 1e-6, "reference offset recomputed at each Point");
        model.SelectedAcPointDimension = "Pins in selected group";
        model.SelectedAcPointRow = model.AcPointRows.Single(r => r.Id == 1);
        Check(model.AcPointCells.Count == 5 && Math.Abs(model.AcPointCells.Single(c => c.Column == 0 && c.Entity == 1).ValuePs - 78) < 1e-6, "pin by Point values retained");
        var trend = model.AcPointTrendSeries.OfType<LiveChartsCore.SkiaSharpView.LineSeries<LiveChartsCore.Defaults.ObservablePoint>>().Single().Values!.ToArray();
        Check(trend.Length == 4 && trend[2].Y == null && trend[3].Y == null, "missing/duplicate pin breaks trend rather than bridging it");
        model.SelectedAcPointDimension = "Groups"; model.SelectedAcPointMetric = "Error vs target";
        model.SelectedAcAlignmentSet = tables.First(t => t.Clock == "250.000 MHz");
        Check(model.AcPointCells.Count == 1 && Math.Abs(model.AcPointCells[0].ValuePs + 39) < 1e-6, "250MHz remains separate");
        model.SelectedAcAlignmentSet = tables.Where(t => t.Clock == "500.000 MHz").First(t => t.SweepLine != first.SweepLine);
        Check(model.AcPointCells.Count == 1 && Math.Abs(model.AcPointCells[0].ValuePs - 117) < 1e-6, "same-clock repeated sweep is separate");
        model.SelectedAcAlignmentSet = tables.First(t => t.Points[0].LoopIndex == 2);
        Check(model.AcPointCells.Count == 1 && Math.Abs(model.AcPointCells[0].ValuePs + 78) < 1e-6, "Loop2 is separate");
        model.AcPointView = false;
        model.SelectedAcAlignmentSet = model.AcAlignmentSets.Single(t => t.Title == original);
        Console.WriteLine($"AC points: {checks} checks passed (scopes, missing and duplicate data, numeric matrix and gaps).");
        return checks;
    }
}
