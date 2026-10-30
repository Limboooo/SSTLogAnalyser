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
    public string[] TmuViews { get; } = { "Path verification error", "Path calibration compensation", "Timebase verification EC" };
    public string[] TmuPaths { get; } = { "VOL → VOL", "VOH → VOH", "VOH → VOL", "VOL → VOH" };
    public string[] TmuTrendModes { get; } = { "Selected pin", "Current range - mean and Min-Max" };
    private static readonly string[] TmuColumnPaths = { "VOLStart_VOLStop", "VOHStart_VOHStop", "VOHStart_VOLStop", "VOLStart_VOHStop" };
    private readonly List<TmuReport> _tmuReports = new();
    private bool _updatingTmu;
    public ObservableCollection<TmuReport> TmuReports { get; } = new();
    public ObservableCollection<double> TmuReferences { get; } = new();
    public ObservableCollection<TmuPinRange> TmuPinRanges { get; } = new();
    [ObservableProperty] private string _selectedTmuView = "Path verification error";
    [ObservableProperty] private TmuReport? _selectedTmuReport;
    [ObservableProperty] private double? _selectedTmuReference;
    [ObservableProperty] private TmuPinRange? _selectedTmuPinRange;
    [ObservableProperty] private int _selectedTmuPin;
    [ObservableProperty] private string _selectedTmuTrendPath = "VOL → VOH";
    [ObservableProperty] private string _selectedTmuTrendMode = "Selected pin";
    [ObservableProperty] private bool _tmuVolVol = true, _tmuVohVoh = true, _tmuVohVol = true, _tmuVolVoh = true;
    [ObservableProperty] private bool _tmuVolDiff = true, _tmuVohDiff = true;
    [ObservableProperty] private ISeries[] _tmu0Series = Array.Empty<ISeries>(), _tmu1Series = Array.Empty<ISeries>(), _tmuTrendSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _tmu0XAxes = Array.Empty<Axis>(), _tmu1XAxes = Array.Empty<Axis>(), _tmuTrendXAxes = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _tmu0YAxes = Array.Empty<Axis>(), _tmu1YAxes = Array.Empty<Axis>(), _tmuTrendYAxes = Array.Empty<Axis>();
    [ObservableProperty] private IReadOnlyList<TmuPoint> _tmuDetailRows = Array.Empty<TmuPoint>();
    [ObservableProperty] private string _tmuSummary = string.Empty, _tmu0Title = string.Empty, _tmu1Title = string.Empty, _tmuTrendTitle = string.Empty;
    public bool HasTmuCharts => SelectedModule == "TMU" && _tmuReports.Count > 0;
    public bool IsTmuVerification => SelectedTmuView == TmuViews[0];
    public bool IsTmuCompensation => SelectedTmuView == TmuViews[1];
    public bool IsTmuFrequency => SelectedTmuView == TmuViews[2];
    public bool HasTmuSecondChart => !IsTmuFrequency;
    public string TmuReferenceLabel => IsTmuFrequency ? "Reference frequency (Hz)" : "Reference time (ns)";
    public string TmuHelp => IsTmuVerification
        ? "Error = measured - reference. Out-of-limit counts are calculated; an empty VerifyResult remains Unknown. Click a point to select its pin, details and trend. This view uses its own pin/path filters; source file, Run Batch and outer Loop remain separate."
        : IsTmuCompensation ? "PinDiff is calibration compensation; path verification limits do not apply. Click a point to inspect its source line."
        : "EC is in ppm; measured frequency is in Hz. Limits come from the current timebase verification section. Pass/Fail is retained from the source LOG.";

    partial void OnSelectedTmuViewChanged(string value)
    {
        foreach (var name in new[] { nameof(IsTmuVerification), nameof(IsTmuCompensation), nameof(IsTmuFrequency), nameof(HasTmuSecondChart), nameof(TmuReferenceLabel), nameof(TmuHelp) }) OnPropertyChanged(name);
        ChooseTmuReports();
    }
    partial void OnSelectedTmuReportChanged(TmuReport? value) => RefreshTmuChoices();
    partial void OnSelectedTmuReferenceChanged(double? value) => UpdateTmuCharts();
    partial void OnSelectedTmuPinRangeChanged(TmuPinRange? value) => UpdateTmuCharts();
    partial void OnSelectedTmuPinChanged(int value)
    {
        if (_updatingTmu) return;
        if (SelectedTmuPinRange is { First: not null, Last: not null } r && (value < r.First || value > r.Last))
            SelectedTmuPinRange = TmuPinRanges.FirstOrDefault();
        UpdateTmuCharts();
    }
    partial void OnSelectedTmuTrendPathChanged(string value) => UpdateTmuCharts();
    partial void OnSelectedTmuTrendModeChanged(string value) => UpdateTmuCharts();
    partial void OnTmuVolVolChanged(bool value) => UpdateTmuCharts();
    partial void OnTmuVohVohChanged(bool value) => UpdateTmuCharts();
    partial void OnTmuVohVolChanged(bool value) => UpdateTmuCharts();
    partial void OnTmuVolVohChanged(bool value) => UpdateTmuCharts();
    partial void OnTmuVolDiffChanged(bool value) => UpdateTmuCharts();
    partial void OnTmuVohDiffChanged(bool value) => UpdateTmuCharts();

    private void RefreshTmuReports()
    {
        _tmuReports.Clear();
        foreach (var file in LoadedFiles)
        {
            var events = _cache.GetDiagnosticObservations(new[] { file.FileId }).Where(o => o.Module == "TMU").ToArray();
            var tests = _cache.QueryTestResults(new[] { file.FileId }, "TMU", null, null, null);
            var loops = events.Select(o => o.LoopIndex).Concat(tests.Select(t => t.LoopIndex)).Distinct();
            foreach (var loop in loops.Where(l => SelectedLoops.Count == 0 || SelectedLoops.Contains(l)))
            {
                var observations = events.Where(o => o.LoopIndex == loop).ToArray();
                var points = tests.Where(t => t.LoopIndex == loop).ToArray();
                var sections = observations.Where(o => o.Kind == "TMU section").OrderBy(o => o.LineNumber).ToArray();
                var starts = new[] { 0 }.Concat(sections.Select(s => s.LineNumber)).ToArray();
                for (int i = 0; i < starts.Length; i++)
                {
                    int start = starts[i], end = i + 1 < starts.Length ? starts[i + 1] : int.MaxValue;
                    var rows = new List<TmuPoint>();
                    foreach (var p in points.Where(p => p.LineNumber >= start && p.LineNumber < end))
                        for (int tmu = 0; tmu < 2; tmu++)
                            for (int path = 0; path < TmuColumnPaths.Length; path++)
                                if (p.TestItemName == $"TMU Path Tmu{tmu}_{TmuColumnPaths[path]} (ns)")
                                    rows.Add(new(TmuViews[0], p.ChannelId, tmu, path, TmuPaths[path], p.ExpectValue, p.MeasureValue,
                                        p.Difference, p.LowLimit, p.UpLimit, p.LineNumber, p.Timestamp, p.HasExplicitResult ? (p.IsFailed ? "Fail" : "Pass") : "Unknown"));
                    foreach (var p in observations.Where(p => p.LineNumber >= start && p.LineNumber < end && p.Channel.HasValue && p.Value.HasValue))
                    {
                        if (p.Kind == "Clock" && p.Item == "EC" && p.Target.HasValue && p.Status is "Pass" or "Fail")
                        {
                            var freq = observations.SingleOrDefault(o => o.LineNumber == p.LineNumber && o.Item == "FMEA");
                            if (freq?.Value is double hz) rows.Add(new(TmuViews[2], p.Channel!.Value, -1, 0, "EC", p.Target.Value, hz, p.Value!.Value,
                                p.LowLimit, p.HighLimit, p.LineNumber, p.Timestamp, p.Status));
                        }
                        else if (p.Kind == "Calibration" && (p.Item.Contains("VolPinDiff(ns)", StringComparison.Ordinal) || p.Item.Contains("VohPinDiff(ns)", StringComparison.Ordinal)))
                        {
                            int tmu = p.Item.StartsWith("Tmu0 Result ", StringComparison.Ordinal) ? 0 : p.Item.StartsWith("Tmu1 Result ", StringComparison.Ordinal) ? 1 : -1;
                            if (tmu < 0) continue;
                            int path = p.Item.Contains("VolPinDiff", StringComparison.Ordinal) ? 0 : 1;
                            rows.Add(new(TmuViews[1], p.Channel!.Value, tmu, path, path == 0 ? "VOL PinDiff" : "VOH PinDiff", 0, p.Value!.Value,
                                p.Value.Value, null, null, p.LineNumber, p.Timestamp, p.Status));
                        }
                    }
                    if (rows.Count == 0) continue;
                    string section = sections.LastOrDefault(s => s.LineNumber == start)?.Item ?? "TMU";
                    _tmuReports.Add(new($"{file.FileName} / Loop {loop} / {section} / L{start}", rows));
                }
            }
        }
        OnPropertyChanged(nameof(HasTmuCharts));
        if (_tmuReports.Count > 0 && !_tmuReports.Any(r => r.Points.Any(p => p.View == SelectedTmuView)))
            SelectedTmuView = TmuViews.First(v => _tmuReports.Any(r => r.Points.Any(p => p.View == v)));
        ChooseTmuReports();
    }

    private void ChooseTmuReports()
    {
        var previous = SelectedTmuReport?.Title;
        _updatingTmu = true;
        try
        {
            TmuReports.Clear();
            foreach (var report in _tmuReports.Where(r => r.Points.Any(p => p.View == SelectedTmuView))) TmuReports.Add(report);
            SelectedTmuReport = TmuReports.FirstOrDefault(r => r.Title == previous) ?? TmuReports.FirstOrDefault();
        }
        finally { _updatingTmu = false; }
        RefreshTmuChoices();
    }

    private void RefreshTmuChoices()
    {
        if (_updatingTmu) return;
        _updatingTmu = true;
        try
        {
            var rows = SelectedTmuReport?.Points.Where(p => p.View == SelectedTmuView).ToArray() ?? Array.Empty<TmuPoint>();
            var previous = SelectedTmuReference;
            TmuReferences.Clear();
            foreach (var reference in rows.Select(p => p.Reference).Distinct().Order()) TmuReferences.Add(reference);
            SelectedTmuReference = previous.HasValue && TmuReferences.Contains(previous.Value) ? previous : TmuReferences.Count > 0 ? TmuReferences[^1] : null;
            var first = SelectedTmuPinRange?.First;
            TmuPinRanges.Clear(); TmuPinRanges.Add(new("All pins", null, null));
            foreach (var group in rows.Select(p => p.Pin / 32).Distinct().Order())
                TmuPinRanges.Add(new($"{group * 32:D4}–{group * 32 + 31:D4}", group * 32, group * 32 + 31));
            SelectedTmuPinRange = TmuPinRanges.FirstOrDefault(r => r.First == first) ?? TmuPinRanges[0];
            if (!rows.Any(p => p.Pin == SelectedTmuPin)) SelectedTmuPin = rows.FirstOrDefault()?.Pin ?? 0;
        }
        finally { _updatingTmu = false; }
        UpdateTmuCharts();
    }

    private TmuPoint[] TmuUniquePoints(out int ambiguous)
    {
        var groups = (SelectedTmuReport?.Points ?? Array.Empty<TmuPoint>()).Where(p => p.View == SelectedTmuView)
            .GroupBy(p => (p.Pin, p.Tmu, p.Path, p.Reference)).ToArray();
        ambiguous = groups.Count(g => g.Count() != 1);
        return groups.Where(g => g.Count() == 1).Select(g => g.Single()).ToArray();
    }
    private bool TmuPathVisible(TmuPoint point) => IsTmuCompensation ? (point.Path == 0 ? TmuVolDiff : TmuVohDiff)
        : IsTmuFrequency || new[] { TmuVolVol, TmuVohVoh, TmuVohVol, TmuVolVoh }[point.Path];
    private bool InTmuRange(TmuPoint p) => SelectedTmuPinRange?.First is not int first || (p.Pin >= first && p.Pin <= SelectedTmuPinRange.Last);
    private static Axis[] TmuAxes(string name, double? min = null, double? max = null, bool pins = false) => new[] {
        new Axis { Name = name, MinLimit = min, MaxLimit = max, MinStep = pins ? 1 : 0, NameTextSize = 12, TextSize = 11,
            Labeler = value => value.ToString(pins ? "F0" : "0.###") } };

    private void UpdateTmuCharts()
    {
        if (_updatingTmu) return;
        var all = TmuUniquePoints(out int ambiguous);
        var rows = all.Where(p => (IsTmuCompensation || p.Reference == SelectedTmuReference) && (IsTmuFrequency || InTmuRange(p)) && TmuPathVisible(p)).OrderBy(p => p.Pin).ToArray();
        TmuDetailRows = all.Where(p => p.Pin == SelectedTmuPin && (IsTmuCompensation || p.Reference == SelectedTmuReference)).OrderBy(p => p.Tmu).ThenBy(p => p.Path).ToArray();
        var ys = rows.Select(p => p.Value).Concat(rows.SelectMany(p => new[] { p.Low, p.High }).OfType<double>()).Append(0).ToArray();
        double min = ys.Min(), max = ys.Max(), pad = Math.Max((max - min) * .09, .05);
        string unit = IsTmuFrequency ? "ppm" : "ns", axis = IsTmuCompensation ? "Compensation (ns)" : IsTmuFrequency ? "EC (ppm)" : "Error (ns)";
        Tmu0XAxes = TmuAxes("Pin", pins: true); Tmu1XAxes = TmuAxes("Pin", pins: true);
        Tmu0YAxes = TmuAxes(axis, min-pad, max+pad); Tmu1YAxes = TmuAxes(axis, min-pad, max+pad);
        Tmu0Series = TmuMainSeries(rows.Where(p => p.Tmu == (IsTmuFrequency ? -1 : 0)).ToArray());
        Tmu1Series = TmuMainSeries(rows.Where(p => p.Tmu == 1).ToArray());
        Tmu0Title = IsTmuFrequency ? $"Timebase verification - {SelectedTmuReference:G} Hz" : $"TMU0 - {(IsTmuCompensation ? "PinDiff compensation" : $"{SelectedTmuReference:G} ns verification error")}";
        Tmu1Title = Tmu0Title.Replace("TMU0", "TMU1", StringComparison.Ordinal);
        var limits = rows.Where(p => p.Low.HasValue && p.High.HasValue).Select(p => (p.Low, p.High)).Distinct().ToArray();
        TmuSummary = (rows.Length == 0 ? "No unique valid points for this selection. Missing/duplicate points are not replaced by zero." :
            $"{rows.Length:N0} points | {rows.Min(p => p.Value):+0.000;-0.000;0.000} to {rows.Max(p => p.Value):+0.000;-0.000;0.000} {unit}" +
            (IsTmuCompensation ? " | Calibration values; no verification verdict" : $" | Out of limits: {rows.Count(p => p.Low.HasValue && p.High.HasValue && (p.Value < p.Low || p.Value > p.High))}" +
             (limits.Length == 1 && limits[0].Low.HasValue && limits[0].High.HasValue ? $" | Limits [{limits[0].Low:G}, {limits[0].High:G}] {unit}" : " | Missing/mixed limits; no common limit lines"))) +
            (ambiguous > 0 ? $" | Excluded duplicate keys: {ambiguous}" : "");
        UpdateTmuTrend(all);
    }

    private ISeries[] TmuMainSeries(TmuPoint[] rows)
    {
        var series = new List<ISeries>();
        foreach (var group in rows.GroupBy(p => p.Path))
        {
            var color = Palette[group.Key];
            var scatter = new ScatterSeries<ObservablePoint> {
                Name = group.First().PathName, Values = group.Select(p => new ObservablePoint(p.Pin, p.Value)).ToArray(),
                GeometrySize = rows.Length > 256 ? 3 : 6, Fill = new SolidColorPaint(color), Stroke = null };
            scatter.ChartPointPointerDown += (_, p) => SelectedTmuPin = (int)Math.Round(p.Coordinate.SecondaryValue);
            series.Add(scatter);
        }
        if (rows.Length > 0)
        {
            double lo = rows.Min(p => p.Pin), hi = rows.Max(p => p.Pin); if (lo == hi) { lo -= .5; hi += .5; }
            AddDiagnosticLine(series, "Zero", new[] { (lo, 0d), (hi, 0d) }, SKColors.Gray, true, 1);
            var limits = rows.Select(p => (p.Low, p.High)).Distinct().ToArray();
            if (!IsTmuCompensation && limits.Length == 1 && limits[0].Low.HasValue && limits[0].High.HasValue)
                foreach (var limit in new[] { limits[0].Low!.Value, limits[0].High!.Value })
                    AddDiagnosticLine(series, $"Limit {limit:G}", new[] { (lo, limit), (hi, limit) }, SKColors.IndianRed, true, 1);
        }
        return series.ToArray();
    }

    private void UpdateTmuTrend(TmuPoint[] all)
    {
        var series = new List<ISeries>();
        int path = Array.IndexOf(TmuPaths, SelectedTmuTrendPath);
        bool range = SelectedTmuTrendMode == TmuTrendModes[1];
        var data = all.Where(p => p.Path == path && (range ? InTmuRange(p) : p.Pin == SelectedTmuPin)).ToArray();
        TmuTrendTitle = $"{(range ? "Current range - mean and Min-Max" : $"Pin {SelectedTmuPin:D4}")} / {SelectedTmuTrendPath}";
        foreach (int tmu in new[] { 0, 1 })
        {
            var groups = data.Where(p => p.Tmu == tmu).GroupBy(p => p.Reference).ToDictionary(g => g.Key, g => g.ToArray());
            var values = TmuReferences.Select(r => new ObservablePoint(r, groups.TryGetValue(r, out var ps) ? ps.Average(p => p.Value) : null)).ToArray();
            series.Add(new LineSeries<ObservablePoint> { Name = $"TMU{tmu}{(range ? " Mean" : "")}", Values = values, Fill = null,
                Stroke = new SolidColorPaint(Palette[tmu]) { StrokeThickness = 2 }, GeometrySize = 5,
                GeometryFill = new SolidColorPaint(Palette[tmu]), GeometryStroke = null, LineSmoothness = 0 });
            if (range)
            {
                // Separate vertical whiskers preserve each measured reference; no interpolated sample counts.
                var whiskers = TmuReferences.SelectMany(r => groups.TryGetValue(r, out var ps)
                    ? new[] { new ObservablePoint(r, ps.Min(p => p.Value)), new ObservablePoint(r, ps.Max(p => p.Value)), new ObservablePoint(r, null) }
                    : new[] { new ObservablePoint(r, null) }).ToArray();
                series.Add(new LineSeries<ObservablePoint> { Name = $"TMU{tmu} Min–Max", Values = whiskers, Fill = null, GeometrySize = 0,
                    Stroke = new SolidColorPaint(Palette[tmu].WithAlpha(100)) { StrokeThickness = 4 }, LineSmoothness = 0 });
            }
        }
        var ys = data.Select(p => p.Value).Concat(data.SelectMany(p => new[] { p.Low, p.High }).OfType<double>()).Append(0).ToArray();
        double min = ys.Min(), max = ys.Max(), pad = Math.Max((max-min)*.09, .05);
        if (TmuReferences.Count > 0 && data.Length > 0)
        {
            var limits = data.Select(p => (p.Low, p.High)).Distinct().ToArray();
            if (limits.Length == 1 && limits[0].Low.HasValue && limits[0].High.HasValue)
                foreach (var limit in new[] { limits[0].Low!.Value, limits[0].High!.Value })
                    AddDiagnosticLine(series, $"Limit {limit:G}", new[] { (TmuReferences[0], limit), (TmuReferences[^1], limit) }, SKColors.IndianRed, true, 1);
        }
        TmuTrendSeries = IsTmuVerification ? series.ToArray() : Array.Empty<ISeries>();
        TmuTrendXAxes = TmuAxes("Reference time (ns)"); TmuTrendYAxes = TmuAxes("Error (ns)", min-pad, max+pad);
    }
}

public sealed record TmuReport(string Title, IReadOnlyList<TmuPoint> Points);
public sealed record TmuPinRange(string Label, int? First, int? Last);
public sealed record TmuPoint(string View, int Pin, int Tmu, int Path, string PathName, double Reference, double Measured,
    double Value, double? Low, double? High, int Line, string Timestamp, string SourceStatus)
{
    public string TmuLabel => Tmu < 0 ? "Timebase" : $"TMU{Tmu}";
}
