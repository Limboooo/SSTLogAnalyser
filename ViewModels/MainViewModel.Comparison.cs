using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using SSTLogAnalyser.Models;
using SSTLogAnalyser.Services;

namespace SSTLogAnalyser.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private LogFileInfo? _baselineFile;
    [ObservableProperty] private LogFileInfo? _currentComparisonFile;
    [ObservableProperty] private int? _baselineLoop;
    [ObservableProperty] private int? _currentComparisonLoop;
    public ObservableCollection<int> BaselineLoops { get; } = new();
    public ObservableCollection<int> CurrentComparisonLoops { get; } = new();
    public string[] ComparisonFilters { get; } = { "Attention", "All", "New failure", "Recovered", "Persistent failure", "Missing point", "Added point", "Duplicate / ambiguous", "Near limit", "Limits changed", "Unknown / unsupported" };
    [ObservableProperty] private string _comparisonFilter = "Attention";
    [ObservableProperty] private string _comparisonSearch = string.Empty;
    [ObservableProperty] private IReadOnlyList<BaselineComparisonRow> _comparisonRows = Array.Empty<BaselineComparisonRow>();
    [ObservableProperty] private BaselineComparisonRow? _selectedComparisonRow;
    [ObservableProperty] private string _comparisonSummary = "Load LOGs, select files and Loops, then click Compare. Loop 0 means Initial.";
    [ObservableProperty] private string _comparisonChartSummary = "Select a result to compare the channel curves.";
    [ObservableProperty] private ISeries[] _comparisonSeries = Array.Empty<ISeries>();
    public Axis[] ComparisonXAxes { get; } = { new() { Name = "Expect / Target", Labeler = v => v.ToString("G6") } };
    public Axis[] ComparisonYAxes { get; } = { new() { Name = "Error (source units)", Labeler = v => v.ToString("G6") } };
    private IReadOnlyList<BaselineComparisonRow> _allComparisonRows = Array.Empty<BaselineComparisonRow>();
    private int _comparisonRevision;

    private void RefreshComparisonFiles()
    {
        if (BaselineFile == null || !LoadedFiles.Contains(BaselineFile)) BaselineFile = LoadedFiles.FirstOrDefault();
        if (CurrentComparisonFile == null || !LoadedFiles.Contains(CurrentComparisonFile))
            CurrentComparisonFile = LoadedFiles.FirstOrDefault(p => p != BaselineFile);
    }
    partial void OnBaselineFileChanged(LogFileInfo? value) { SetComparisonLoops(value, BaselineLoops, true); }
    partial void OnCurrentComparisonFileChanged(LogFileInfo? value) { SetComparisonLoops(value, CurrentComparisonLoops, false); }
    private void SetComparisonLoops(LogFileInfo? file, ObservableCollection<int> loops, bool baseline)
    {
        loops.Clear();
        if (file != null) foreach (var loop in _cache.GetDistinctLoops(new[] { file.FileId }, null, null)) loops.Add(loop);
        if (baseline) BaselineLoop = loops.Count == 0 ? null : loops.Max();
        else CurrentComparisonLoop = loops.Count == 0 ? null : loops.Max();
        InvalidateComparison();
    }
    partial void OnBaselineLoopChanged(int? value) => InvalidateComparison();
    partial void OnCurrentComparisonLoopChanged(int? value) => InvalidateComparison();
    private void InvalidateComparison()
    {
        _comparisonRevision++;
        _allComparisonRows = Array.Empty<BaselineComparisonRow>();
        ComparisonRows = Array.Empty<BaselineComparisonRow>();
        SelectedComparisonRow = null;
        ComparisonSeries = Array.Empty<ISeries>();
        ComparisonSummary = "Selections changed. Click Compare to calculate results. Loop 0 means Initial.";
        ComparisonChartSummary = "Select a result to compare the channel curves.";
    }
    partial void OnComparisonFilterChanged(string value) => FilterComparison();
    partial void OnComparisonSearchChanged(string value) => FilterComparison();
    private void FilterComparison()
    {
        ComparisonRows = _allComparisonRows.Where(p => ComparisonFilter == "All" ||
                (ComparisonFilter == "Attention" ? p.NeedsAttention : ComparisonFilter == "Limits changed" ? p.LimitsChanged : p.Status == ComparisonFilter))
            .Where(p => string.IsNullOrWhiteSpace(ComparisonSearch) ||
                $"{p.Module} {p.TestItem} Ch{p.Channel} {p.Component}".Contains(ComparisonSearch.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (SelectedComparisonRow == null || !ComparisonRows.Contains(SelectedComparisonRow)) SelectedComparisonRow = ComparisonRows.FirstOrDefault();
    }

    [RelayCommand]
    private async Task CompareBaselineAsync()
    {
        if (BaselineFile == null || CurrentComparisonFile == null || BaselineLoop == null || CurrentComparisonLoop == null)
        { ComparisonSummary = "Choose files with parsed test points and one Loop on each side."; return; }
        var revision = _comparisonRevision;
        ComparisonSummary = "Comparing cached test points...";
        try
        {
            var baseline = _cache.QueryTestResults(new[] { BaselineFile.FileId }, null, null, null, new[] { BaselineLoop.Value });
            var current = _cache.QueryTestResults(new[] { CurrentComparisonFile.FileId }, null, null, null, new[] { CurrentComparisonLoop.Value });
            var rows = await Task.Run(() => BaselineLogComparer.Compare(baseline, current));
            if (revision != _comparisonRevision) return;
            _allComparisonRows = rows;
            var matched = rows.Count(p => p.BaselinePoints.Count == 1 && p.CurrentPoints.Count == 1);
            ComparisonSummary = $"Baseline {baseline.Count:N0} points / Current {current.Count:N0} points | Matched {matched:N0} | " +
                $"New failures {rows.Count(p => p.Status == "New failure")} | Recovered {rows.Count(p => p.Status == "Recovered")} | " +
                $"Missing {rows.Count(p => p.Status == "Missing point")} | Added {rows.Count(p => p.Status == "Added point")} | " +
                $"Ambiguous {rows.Count(p => p.Status == "Duplicate / ambiguous")} | Limits changed {rows.Count(p => p.LimitsChanged)} | " +
                $"Unknown {rows.Count(p => p.Status == "Unknown / unsupported")}. Near limit: >=80% utilization; DC/MIXI numeric comparison only.";
            FilterComparison();
        }
        catch (Exception ex) { if (revision == _comparisonRevision) ComparisonSummary = "Comparison failed: " + ex.Message; }
    }

    partial void OnSelectedComparisonRowChanged(BaselineComparisonRow? value)
    {
        ComparisonSeries = Array.Empty<ISeries>();
        foreach (var axis in ComparisonXAxes.Concat(ComparisonYAxes)) { axis.MinLimit = null; axis.MaxLimit = null; }
        if (value == null) { ComparisonChartSummary = "Select a result to compare the channel curves."; return; }
        var group = _allComparisonRows.Where(p => p.Module == value.Module && p.TestItem == value.TestItem &&
            p.Channel == value.Channel && p.Slot == value.Slot && p.Component == value.Component).ToArray();
        var baseline = group.SelectMany(p => p.BaselinePoints).Where(BaselineLogComparer.IsNumeric).OrderBy(p => p.ExpectValue).ToArray();
        var current = group.SelectMany(p => p.CurrentPoints).Where(BaselineLogComparer.IsNumeric).OrderBy(p => p.ExpectValue).ToArray();
        var series = new List<ISeries>();
        Add(baseline, "Baseline", SKColors.DodgerBlue);
        Add(current, "Current", SKColors.DarkOrange);
        ComparisonSeries = series.ToArray();
        var unique = group.Where(p => p.CanEvaluate && p.BaselinePoints.Count == 1 && p.CurrentPoints.Count == 1).ToArray();
        string Metrics(bool old)
        {
            var errors = unique.Select(p => old ? p.BaselineError!.Value : p.CurrentError!.Value).ToArray();
            var points = unique.Select(p => old ? p.Baseline! : p.Current!).ToArray();
            var usages = unique.Select(p => old ? p.BaselineUtilization!.Value : p.CurrentUtilization!.Value).ToArray();
            return errors.Length == 0 ? "No uniquely matched numeric points" :
                $"RMS {Math.Sqrt(errors.Average(p => p * p)):G6}, Max |error| {errors.Max(p => Math.Abs(p)):G6}, " +
                $"Failed {points.Count(p => p.IsFailed || p.Difference < BaselineLogComparer.ErrorLimits(p).Low || p.Difference > BaselineLogComparer.ErrorLimits(p).High)}, Worst usage {usages.Max():P1}";
        }
        ComparisonChartSummary = $"{value.Module} / {value.TestItem} / Ch{value.Channel} / {value.Component}\n" +
            $"Baseline: {BaselineFile?.FileName}, Loop {BaselineLoop}, lines [{value.BaselineLines}] | " +
            $"Current: {CurrentComparisonFile?.FileName}, Loop {CurrentComparisonLoop}, lines [{value.CurrentLines}]\n" +
            $"Matched unique points {unique.Length}: Baseline {Metrics(true)} | Current {Metrics(false)}. " +
            "Curves include unmatched and repeated points; repeated X values may overlap. Positive delta |error| means worse.";
        void Add(TestResult[] points, string name, SKColor color)
        {
            if (points.Length == 0) return;
            series.Add(Line(name, points.Select(p => new ObservablePoint(p.ExpectValue, p.Difference)), color, 5));
            series.Add(Line(name + " High", points.Select(p => new ObservablePoint(p.ExpectValue, BaselineLogComparer.ErrorLimits(p).High)), color.WithAlpha(130), 3));
            series.Add(Line(name + " Low", points.Select(p => new ObservablePoint(p.ExpectValue, BaselineLogComparer.ErrorLimits(p).Low)), color.WithAlpha(130), 3));
        }
    }
    private static LineSeries<ObservablePoint> Line(string name, IEnumerable<ObservablePoint> values, SKColor color, double size) => new() {
        Name = name, Values = values.ToArray(), Stroke = new SolidColorPaint(color) { StrokeThickness = 1.5f },
        GeometryStroke = new SolidColorPaint(color), GeometrySize = size, Fill = null, LineSmoothness = 0
    };
}
