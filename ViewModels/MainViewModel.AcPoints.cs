using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace SSTLogAnalyser.ViewModels;

public partial class MainViewModel
{
    private bool _updatingAcPoints;
    public string[] AcPointDimensions { get; } = { "Groups", "Pins in selected group" };
    public string[] AcPointMetrics { get; } = { "Error vs target", "Offset vs base pin" };
    [ObservableProperty] private bool _acPointView;
    public bool IsAcPointViewActive => AcPointView && AcRelativeMode;
    [ObservableProperty] private string _selectedAcPointDimension = "Groups";
    [ObservableProperty] private string _selectedAcPointMetric = "Error vs target";
    [ObservableProperty] private IReadOnlyList<AcPointRow> _acPointRows = Array.Empty<AcPointRow>();
    [ObservableProperty] private AcPointRow? _selectedAcPointRow;
    [ObservableProperty] private IReadOnlyList<AcPointCell> _acPointCells = Array.Empty<AcPointCell>();
    [ObservableProperty] private ISeries[] _acPointHeatSeries = Array.Empty<ISeries>();
    [ObservableProperty] private ISeries[] _acPointTrendSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _acPointXAxes = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _acPointYAxes = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _acPointTrendXAxes = Array.Empty<Axis>();
    public Axis[] AcPointTrendYAxes { get; } = { new() { Name = "Error / offset (ps)", NameTextSize = 13, MinStep = 78, ForceStepToMin = true, TextSize = 10 } };
    [ObservableProperty] private string _acPointSummary = "Select a verification sweep to compare points.";
    [ObservableProperty] private string _acPointScaleLabel = string.Empty;

    partial void OnAcPointViewChanged(bool value) { OnPropertyChanged(nameof(IsAcPointViewActive)); OnPropertyChanged(nameof(AcChartHelp)); UpdateAcPointCharts(); }
    partial void OnSelectedAcPointDimensionChanged(string value) { SelectedAcPointRow = null; UpdateAcPointCharts(); }
    partial void OnSelectedAcPointMetricChanged(string value) => UpdateAcPointCharts();
    partial void OnSelectedAcPointRowChanged(AcPointRow? value) => UpdateAcPointCharts();
    [RelayCommand] private void ShowAcPointDetail() => AcPointView = false;

    private void UpdateAcPointCharts()
    {
        if (_updatingAcPoints) return;
        _updatingAcPoints = true;
        try
        {
            AcPointHeatSeries = Array.Empty<ISeries>(); AcPointTrendSeries = Array.Empty<ISeries>();
            AcPointScaleLabel = string.Empty;
            AcPointCells = Array.Empty<AcPointCell>();
            var current = SelectedAcAlignmentSet;
            if (!AcPointView || !AcRelativeMode || current == null || current.Clock == "Clock unspecified" || current.SweepLine == 0 || current.Points.Count == 0 || SelectedAcGroupSize is not (64 or 128))
            { AcPointRows = Array.Empty<AcPointRow>(); SelectedAcPointRow = null; AcPointXAxes = Array.Empty<Axis>(); AcPointYAxes = Array.Empty<Axis>(); AcPointTrendXAxes = Array.Empty<Axis>(); AcPointSummary = "No verification sweep with a known clock is selected."; return; }
            var first = current.Points[0];
            var scope = AcAlignmentSets.Where(t => t.Clock == current.Clock && t.SweepLine == current.SweepLine && t.Points.Count > 0 &&
                t.Points[0].FileId == first.FileId && t.Points[0].LoopIndex == first.LoopIndex).OrderBy(t => t.TableLine).ToArray();
            bool pinMode = SelectedAcPointDimension == "Pins in selected group";
            bool relative = SelectedAcPointMetric == "Offset vs base pin";
            var rawCells = new List<(int Column, int Entity, double Value, int Count)>();
            for (int column = 0; column < scope.Length; column++)
            {
                var unique = scope[column].Points.Where(p => p.TestItemName == "AC " + SelectedAcEdge + "(ns)")
                    .GroupBy(p => p.ChannelId).Where(g => g.Count() == 1).Select(g => g.Single()).ToArray();
                var reference = unique.SingleOrDefault(p => p.ChannelId == SelectedAcReferencePin);
                if (relative && reference == null) continue;
                var values = unique.Where(p => !pinMode || p.ChannelId / SelectedAcGroupSize == SelectedAcDetailGroup?.Group)
                    .GroupBy(p => pinMode ? p.ChannelId : p.ChannelId / SelectedAcGroupSize);
                foreach (var group in values)
                    rawCells.Add((column, group.Key, group.Average(p => relative ? (p.MeasureValue - reference!.MeasureValue) * 1000 : p.Difference * 1000), group.Count()));
            }
            var rows = rawCells.Select(c => c.Entity).Distinct().Order().Select(id => new AcPointRow(id, (pinMode ? "Pin " : "Group ") + id)).ToArray();
            var previous = SelectedAcPointRow?.Id;
            AcPointRows = rows;
            SelectedAcPointRow = rows.FirstOrDefault(r => r.Id == previous) ?? rows.FirstOrDefault(r => r.Id == (pinMode ? SelectedAcReferencePin : SelectedAcDetailGroup?.Group)) ?? rows.FirstOrDefault();
            var rowIndex = rows.Select((r, i) => (r.Id, Index: i)).ToDictionary(r => r.Id, r => r.Index);
            AcPointCells = rawCells.Select(c => new AcPointCell(c.Column, c.Entity, scope[c.Column].Target, c.Value, c.Count, scope[c.Column].TableLine)).ToArray();
            if (rawCells.Count == 0) { AcPointXAxes = Array.Empty<Axis>(); AcPointYAxes = Array.Empty<Axis>(); AcPointTrendXAxes = Array.Empty<Axis>(); AcPointSummary = "No unique valid points for the selected dimension and metric."; return; }
            var limit = Math.Max(78, Math.Ceiling(rawCells.Max(c => Math.Abs(c.Value)) / 78) * 78);
            var heat = new HeatSeries<WeightedPoint>
            {
                Name = SelectedAcPointMetric + " (ps)",
                Values = rawCells.Select(c => new WeightedPoint(c.Column, rowIndex[c.Entity], c.Value)).ToArray(),
                HeatMap = new[] { SKColor.Parse("#1971C2").AsLvcColor(), SKColor.Parse("#F8F9FA").AsLvcColor(), SKColor.Parse("#D9480F").AsLvcColor() },
                ColorStops = new[] { 0d, 0.5d, 1d }, MinValue = -limit, MaxValue = limit,
                PointPadding = new LiveChartsCore.Drawing.Padding(0.5f)
            };
            heat.ChartPointPointerDown += (_, point) =>
            {
                int col = (int)Math.Round(point.Coordinate.SecondaryValue), row = (int)Math.Round(point.Coordinate.PrimaryValue);
                if (col < 0 || col >= scope.Length || row < 0 || row >= rows.Length) return;
                _updatingAcPoints = true;
                try
                {
                    SelectedAcAlignmentSet = scope[col]; SelectedAcPointRow = rows[row];
                    var groupId = pinMode ? rows[row].Id / SelectedAcGroupSize : rows[row].Id;
                    SelectedAcDetailGroup = AcGroupRows.FirstOrDefault(g => g.Group == groupId);
                }
                finally { _updatingAcPoints = false; }
                UpdateAcPointCharts();
            };
            AcPointHeatSeries = new ISeries[] { heat };
            var labels = scope.Select((t, i) => $"P{i}\n{t.Target:F3}").ToArray();
            AcPointXAxes = new[] { new Axis { Name = "Point / Target (ns)", NameTextSize = 12, Labels = labels, MinStep = 1, ForceStepToMin = true, TextSize = 10, MinLimit = -0.5, MaxLimit = scope.Length - 0.5 } };
            AcPointYAxes = new[] { new Axis { Name = pinMode ? "Pin" : "Group", NameTextSize = 12, Labels = rows.Select(r => r.Label).ToArray(), MinStep = 1, ForceStepToMin = !pinMode, IsInverted = true, TextSize = 10, MinLimit = -0.5, MaxLimit = rows.Length - 0.5 } };
            AcPointTrendXAxes = new[] { new Axis { Name = "Point / Target (ns)", NameTextSize = 12, Labels = labels, MinStep = 1, ForceStepToMin = true, TextSize = 10, MinLimit = -0.25, MaxLimit = scope.Length - 0.75 } };
            var selectedCells = AcPointCells.Where(c => c.Entity == SelectedAcPointRow?.Id).ToDictionary(c => c.Column);
            AcPointTrendSeries = new ISeries[] { new LineSeries<ObservablePoint> {
                Name = SelectedAcPointRow?.Label,
                Values = Enumerable.Range(0, scope.Length).Select(i => new ObservablePoint(i, selectedCells.TryGetValue(i, out var cell) ? cell.ValuePs : (double?)null)).ToArray(),
                Fill = null, Stroke = new SolidColorPaint(SKColor.Parse("#1971C2")) { StrokeThickness = 2 },
                GeometrySize = 6, GeometryFill = new SolidColorPaint(SKColor.Parse("#1971C2")), GeometryStroke = null, LineSmoothness = 0 } };
            AcPointTrendYAxes[0].MinLimit = -limit; AcPointTrendYAxes[0].MaxLimit = limit;
            AcPointTrendYAxes[0].Name = SelectedAcPointMetric + " (ps)";
            AcPointScaleLabel = $"Early (blue): -{limit:F0} ps     |     Zero (white)     |     Late (red): +{limit:F0} ps     |     Missing: gray";
            AcPointSummary = $"{SelectedAcEdge} / {current.Clock} / Loop {first.LoopIndex} / Sweep L{current.SweepLine} | {scope.Length} points × {rows.Length} {(pinMode ? "pins" : "groups")} | {SelectedAcPointMetric} (ps){(pinMode ? "" : "; group means")} | Click a cell to select its point and row. Data: cell values and valid-pin counts.";
        }
        finally { _updatingAcPoints = false; }
    }
}

public sealed record AcPointRow(int Id, string Label);
public sealed record AcPointCell(int Column, int Entity, double Target, double ValuePs, int Count, int TableLine);
