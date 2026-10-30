using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using SSTLogAnalyser.Models;

namespace SSTLogAnalyser.ViewModels;

public partial class MainViewModel
{
    private readonly List<AcAlignmentSet> _acAlignmentTables = new();
    public ObservableCollection<AcAlignmentSet> AcAlignmentSets { get; } = new();
    public ObservableCollection<int> AcReferencePins { get; } = new();
    public int[] AcGroupSizes { get; } = { 64, 128 };
    [ObservableProperty] private int _selectedAcGroupSize = 64;
    [ObservableProperty] private IReadOnlyList<AcGroupRow> _acGroupRows = Array.Empty<AcGroupRow>();
    [ObservableProperty] private bool _acRelativeMode;
    [ObservableProperty] private AcAlignmentSet? _selectedAcAlignmentSet;
    [ObservableProperty] private int? _selectedAcReferencePin = 0;
    [ObservableProperty] private IReadOnlyList<AcRelativeRow> _acRelativeRows = Array.Empty<AcRelativeRow>();
    [ObservableProperty] private AcGroupRow? _selectedAcDetailGroup;
    [ObservableProperty] private ISeries[] _acPinDetailSeries = Array.Empty<ISeries>();
    [ObservableProperty] private string _acPinDetailSummary = "Select a group to inspect its pins.";
    public Axis[] AcGroupAxes { get; } = { new() { Name = "Group", NameTextSize = 13, MinStep = 1, ForceStepToMin = true, TextSize = 11 } };
    public Axis[] AcGroupErrorAxes { get; } = { new() { Name = "Mean vs base group (ps)", NameTextSize = 13, MinStep = 78, ForceStepToMin = true, Labeler = v => v.ToString("F0"), TextSize = 11 } };
    public Axis[] AcDetailPinAxes { get; } = { new() { Name = "Pin", NameTextSize = 13, MinStep = 1, TextSize = 11 } };
    public Axis[] AcDetailErrorAxes { get; } = { new() { Name = "Offset from base pin (ps)", NameTextSize = 13, MinStep = 78, ForceStepToMin = true, Labeler = v => v.ToString("F0"), TextSize = 11 } };
    public Axis[] AcOverviewXAxes => AcRelativeMode ? AcGroupAxes : AcPinAxes;
    public Axis[] AcOverviewYAxes => AcRelativeMode ? AcGroupErrorAxes : AcErrorAxes;
    public string AcErrorChartTitle => AcRelativeMode ? "Group alignment - mean offset from base group (click a bar to inspect pins)" : "Pin error envelope - hover for values, scroll to zoom";
    public string AcChartHelp => AcPointView && AcRelativeMode
        ? "Heatmap: points on X, groups or pins on Y; blue = early, red = late. Group cells are means. Click a cell to select its point and row; the curve below follows that row across the sweep. File, Loop, clock and sweep are kept separate."
        : AcRelativeMode
        ? "Bars compare group means with the base group. Click a bar for the pin detail below; dots show each pin relative to the base pin. Positive = later; negative = earlier. AK/DK: 64 pins/group; standard: 128. Tick: 78 ps. Values follow the same clock, edge and target."
        : "Tick spacing: 78 ps (0.078 ns). Positive = late; negative = early. Range is the error span across verification points, not jitter or pin-to-pin skew at one target. Before/after comparison requires verification LOGs with the same clock and test conditions. The Run Batch filter applies.";

    partial void OnAcRelativeModeChanged(bool value)
    {
        OnPropertyChanged(nameof(AcErrorChartTitle)); OnPropertyChanged(nameof(AcChartHelp));
        OnPropertyChanged(nameof(AcOverviewXAxes)); OnPropertyChanged(nameof(AcOverviewYAxes));
        OnPropertyChanged(nameof(IsAcPointViewActive));
        UpdateAcOverview();
    }
    partial void OnSelectedAcReferencePinChanged(int? value) => UpdateAcOverview();
    partial void OnSelectedAcGroupSizeChanged(int value) => UpdateAcOverview();
    partial void OnSelectedAcDetailGroupChanged(AcGroupRow? value) { UpdateAcPinDetail(); UpdateAcPointCharts(); }
    partial void OnSelectedAcAlignmentSetChanged(AcAlignmentSet? value)
    {
        var reference = SelectedAcReferencePin;
        AcReferencePins.Clear();
        if (value != null)
            foreach (var pin in value.Points.Where(p => p.TestItemName == "AC " + SelectedAcEdge + "(ns)")
                         .GroupBy(p => p.ChannelId).Where(g => g.Count() == 1).Select(g => g.Key).Order())
                AcReferencePins.Add(pin);
        SelectedAcReferencePin = reference;
        UpdateAcOverview();
    }

    private void LoadAcAlignmentTables(LogFileInfo file, List<DiagnosticObservation> observations)
    {
        var headers = observations.Where(o => o.Module == "AC" && o.Kind == "Verification table").OrderBy(o => o.LineNumber).ToArray();
        if (headers.Length == 0) return;
        var points = _cache.QueryTestResults(new[] { file.FileId }, "AC", null, null, null)
            .Where(p => p.TestItemName != "AC Pin Overall (ns)").ToArray();
        var sweeps = observations.Where(o => o.Module == "AC" && o.Kind == "Verification sweep").OrderBy(o => o.LineNumber).ToArray();
        for (int i = 0; i < headers.Length; i++)
        {
            var header = headers[i];
            var end = i + 1 < headers.Length ? headers[i + 1].LineNumber : int.MaxValue;
            var rows = points.Where(p => p.LineNumber > header.LineNumber && p.LineNumber < end).ToArray();
            var targets = rows.Select(p => p.ExpectValue).Distinct().Take(2).ToArray();
            if (targets.Length != 1) continue;
            var sweep = sweeps.LastOrDefault(o => o.LineNumber < header.LineNumber);
            _acAlignmentTables.Add(new AcAlignmentSet($"{file.FileName} / Loop {header.LoopIndex} / {header.Item} / Target {targets[0]:F3} ns / Table L{header.LineNumber}", header.Item, targets[0], rows,
                sweep != null && sweep.Item == header.Item && sweep.LoopIndex == header.LoopIndex ? sweep.LineNumber : 0, header.LineNumber));
        }
    }

    private void RefreshAcAlignmentChoices()
    {
        var previous = SelectedAcAlignmentSet?.Title;
        AcAlignmentSets.Clear();
        foreach (var table in _acAlignmentTables.Where(t => t.Points.Any(p => p.TestItemName == "AC " + SelectedAcEdge + "(ns)")))
            AcAlignmentSets.Add(table);
        var selected = AcAlignmentSets.FirstOrDefault(t => t.Title == previous) ?? AcAlignmentSets.FirstOrDefault();
        if (ReferenceEquals(selected, SelectedAcAlignmentSet)) OnSelectedAcAlignmentSetChanged(selected);
        else SelectedAcAlignmentSet = selected;
    }

    private void UpdateAcRelativeChart()
    {
        var detailGroup = SelectedAcDetailGroup?.Group;
        AcRelativeRows = Array.Empty<AcRelativeRow>(); AcPinRows = Array.Empty<AcPinRow>();
        AcGroupRows = Array.Empty<AcGroupRow>();
        SelectedAcDetailGroup = null; AcPinDetailSeries = Array.Empty<ISeries>();
        AcPinDetailSummary = "No pin detail for the current selection.";
        AcErrorSeries = Array.Empty<ISeries>(); AcRangeSeries = Array.Empty<ISeries>();
        AcErrorAxes[0].Name = "Relative timing (ns)";
        if (SelectedAcGroupSize is not (64 or 128))
        { AcOverviewSummary = "Select a group size of 64 (AK/DK) or 128 (standard)."; return; }
        var table = SelectedAcAlignmentSet;
        if (table == null || table.Clock == "Clock unspecified")
        { AcOverviewSummary = "No verification table with a known clock is selected."; return; }
        var groups = table.Points.Where(p => p.TestItemName == "AC " + SelectedAcEdge + "(ns)").GroupBy(p => p.ChannelId).ToArray();
        var reference = groups.SingleOrDefault(g => g.Key == SelectedAcReferencePin);
        if (reference == null || reference.Count() != 1)
        { AcOverviewSummary = "Reference pin is missing or duplicated. Select a unique reference pin."; return; }
        var basePoint = reference.Single();
        var rows = groups.Where(g => g.Count() == 1).Select(g => g.Single()).OrderBy(p => p.ChannelId)
            .Select(p => new AcRelativeRow(p.ChannelId, p.MeasureValue, basePoint.MeasureValue, p.MeasureValue - basePoint.MeasureValue, p.LineNumber, basePoint.LineNumber, p.ChannelId / SelectedAcGroupSize)).ToArray();
        AcRelativeRows = rows;
        var grouped = rows.GroupBy(r => r.Group).OrderBy(g => g.Key).ToArray();
        var baseMean = grouped.Single(g => g.Key == basePoint.ChannelId / SelectedAcGroupSize).Average(r => r.RelativeNs);
        AcGroupRows = grouped.Select(g => new AcGroupRow(g.Key, g.Key * SelectedAcGroupSize, (g.Key + 1) * SelectedAcGroupSize - 1,
            g.Count(), g.Average(r => r.RelativeNs) * 1000, (g.Average(r => r.RelativeNs) - baseMean) * 1000,
            (g.Max(r => r.RelativeNs) - g.Min(r => r.RelativeNs)) * 1000)).ToArray();
        SelectedAcDetailGroup = AcGroupRows.FirstOrDefault(g => g.Group == detailGroup) ?? AcGroupRows.MaxBy(g => Math.Abs(g.GroupOffsetPs));
        UpdateAcPinDetail();
        var worst = rows.MaxBy(r => Math.Abs(r.RelativeNs))!;
        var worstGroup = AcGroupRows.MaxBy(g => Math.Abs(g.GroupOffsetPs))!;
        AcOverviewSummary = $"{SelectedAcEdge} / {table.Clock} / Target {table.Target:F3} ns | Base Pin {basePoint.ChannelId} | {rows.Length:N0} pins / {grouped.Length} groups of {SelectedAcGroupSize} | Max group shift {Math.Abs(worstGroup.GroupOffsetPs):F1} ps (Group {worstGroup.Group}) | Max pin offset {Math.Abs(worst.RelativePs):F1} ps (Pin {worst.Pin})";
        if (groups.Any(g => g.Count() != 1)) AcOverviewSummary += $" | Duplicate pins excluded: {groups.Count(g => g.Count() != 1)}";
    }

    private void UpdateAcPinDetail()
    {
        AcPinDetailSeries = Array.Empty<ISeries>();
        var group = SelectedAcDetailGroup;
        if (group == null || !AcRelativeMode || AcRelativeRows.Count == 0) return;
        var columns = CreateContributionColumns("Mean vs base group", AcGroupRows.Select(g => new ObservablePoint(g.Group, g.GroupOffsetPs)), SKColor.Parse("#1971C2"));
        columns.MaxBarWidth = 34;
        columns.ChartPointPointerDown += (_, point) => SelectedAcDetailGroup = AcGroupRows.FirstOrDefault(g => g.Group == (int)point.Coordinate.SecondaryValue);
        var overview = new List<ISeries> { columns };
        AddDiagnosticLine(overview, "Zero group offset", new[] { ((double)AcGroupRows.Min(g => g.Group) - 0.5, 0d), ((double)AcGroupRows.Max(g => g.Group) + 0.5, 0d) }, SKColors.Gray, true, 1);
        overview.Add(new ScatterSeries<ObservablePoint> { Name = "Selected group", Values = new[] { new ObservablePoint(group.Group, group.GroupOffsetPs) }, GeometrySize = 11, Fill = new SolidColorPaint(SKColor.Parse("#E8590C")), Stroke = null });
        AcErrorSeries = overview.ToArray();
        AcGroupAxes[0].MinLimit = AcGroupRows.Min(g => g.Group) - 0.5;
        AcGroupAxes[0].MaxLimit = AcGroupRows.Max(g => g.Group) + 0.5;
        AcGroupErrorAxes[0].MinLimit = Math.Floor(Math.Min(0, AcGroupRows.Min(g => g.GroupOffsetPs)) / 78) * 78;
        AcGroupErrorAxes[0].MaxLimit = Math.Max(78, Math.Ceiling(AcGroupRows.Max(g => g.GroupOffsetPs) / 78) * 78);
        if (AcGroupErrorAxes[0].MinLimit == AcGroupErrorAxes[0].MaxLimit) AcGroupErrorAxes[0].MinLimit -= 78;
        var pins = AcRelativeRows.Where(r => r.Group == group.Group).ToArray();
        if (pins.Length == 0) return;
        var worst = pins.MaxBy(r => Math.Abs(r.RelativePs))!;
        AcPinDetailSummary = $"Group {group.Group} | Pins {group.FirstPin}-{group.LastPin} | {pins.Length}/{SelectedAcGroupSize} valid | Mean {group.MeanPs:F1} ps vs Pin {SelectedAcReferencePin} | Within-group span {group.SpanPs:F1} ps | Worst Pin {worst.Pin}: {worst.RelativePs:F1} ps";
        var details = new List<ISeries> {
            new ScatterSeries<ObservablePoint> { Name = $"Pins vs Pin {SelectedAcReferencePin}", Values = pins.Select(r => new ObservablePoint(r.Pin, r.RelativePs)).ToArray(), GeometrySize = 6, Fill = new SolidColorPaint(SKColor.Parse("#1971C2")), Stroke = null }
        };
        AddDiagnosticLine(details, "Group mean", new[] { ((double)group.FirstPin, group.MeanPs), ((double)group.LastPin, group.MeanPs) }, SKColor.Parse("#E8590C"), true, 2);
        AddDiagnosticLine(details, "Zero pin offset", new[] { ((double)group.FirstPin, 0d), ((double)group.LastPin, 0d) }, SKColors.Gray, true, 1);
        details.Add(new ScatterSeries<ObservablePoint> { Name = "Worst pin", Values = new[] { new ObservablePoint(worst.Pin, worst.RelativePs) }, GeometrySize = 10, Fill = new SolidColorPaint(SKColor.Parse("#C92A2A")), Stroke = null });
        AcPinDetailSeries = details.ToArray();
        AcDetailPinAxes[0].MinLimit = group.FirstPin - 0.5; AcDetailPinAxes[0].MaxLimit = group.LastPin + 0.5;
        AcDetailErrorAxes[0].MinLimit = Math.Floor(Math.Min(0, pins.Min(r => r.RelativePs)) / 78) * 78;
        AcDetailErrorAxes[0].MaxLimit = Math.Ceiling(Math.Max(0, pins.Max(r => r.RelativePs)) / 78) * 78;
        if (AcDetailErrorAxes[0].MinLimit == AcDetailErrorAxes[0].MaxLimit) AcDetailErrorAxes[0].MaxLimit += 78;
        AcDetailErrorAxes[0].MinLimit -= 5; AcDetailErrorAxes[0].MaxLimit += 5;
    }
}

public sealed record AcAlignmentSet(string Title, string Clock, double Target, IReadOnlyList<TestResult> Points, int SweepLine = 0, int TableLine = 0);
public sealed record AcRelativeRow(int Pin, double Measured, double Reference, double RelativeNs, int Line, int ReferenceLine, int Group)
{
    public double RelativePs => RelativeNs * 1000;
}
public sealed record AcGroupRow(int Group, int FirstPin, int LastPin, int Count, double MeanPs, double GroupOffsetPs, double SpanPs);
