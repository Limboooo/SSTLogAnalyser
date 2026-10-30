using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using SkiaSharp;
using SSTLogAnalyser.Models;

namespace SSTLogAnalyser.ViewModels;

public partial class MainViewModel
{
    public ObservableCollection<AcReportView> AcReports { get; } = new();
    public string[] AcEdges { get; } = { "CMPH", "CMPL", "RISE", "FALL", "ON", "OFF" };
    [ObservableProperty] private AcReportView? _selectedAcReport;
    [ObservableProperty] private AcReportView? _baselineAcReport;
    [ObservableProperty] private bool _acVerificationMode;
    public bool HasAcOverview => SelectedModule == "AC" && (AcReports.Count > 0 || AcAlignmentSets.Count > 0);
    public bool IsAcOverviewActive => HasAcOverview && !AcVerificationMode;
    [ObservableProperty] private string _selectedAcEdge = "ON";
    [ObservableProperty] private ISeries[] _acErrorSeries = Array.Empty<ISeries>();
    [ObservableProperty] private ISeries[] _acRangeSeries = Array.Empty<ISeries>();
    [ObservableProperty] private IReadOnlyList<AcPinRow> _acPinRows = Array.Empty<AcPinRow>();
    [ObservableProperty] private string _acOverviewSummary = "Load an AC LOG to view pin timing errors.";
    public Axis[] AcPinAxes { get; } = { new() { Name = "Pin", NameTextSize = 13, MinStep = 1, TextSize = 11 } };
    public Axis[] AcRangePinAxes { get; } = { new() { Name = "Pin", NameTextSize = 13, MinStep = 1, TextSize = 11 } };
    public Axis[] AcErrorAxes { get; } = { CreateAcAxis("Error (ns)") };
    public Axis[] AcRangeAxes { get; } = { CreateAcAxis("Max − Min (ns)", 0) };

    private static Axis CreateAcAxis(string name, double? min = null) => new()
    {
        Name = name, MinStep = 0.078, ForceStepToMin = true, MinLimit = min,
        Labeler = value => value.ToString("F3"), NameTextSize = 13, TextSize = 11
    };

    partial void OnAcVerificationModeChanged(bool value) => OnPropertyChanged(nameof(IsAcOverviewActive));
    partial void OnBaselineAcReportChanged(AcReportView? value) => UpdateAcOverview();
    [RelayCommand] private void ClearAcBaseline() => BaselineAcReport = null;

    partial void OnSelectedAcReportChanged(AcReportView? value) => UpdateAcOverview();
    partial void OnSelectedAcEdgeChanged(string value) { RefreshAcAlignmentChoices(); UpdateAcOverview(); }

    private void RefreshAcReports()
    {
        var previous = SelectedAcReport?.Title;
        var baseline = BaselineAcReport?.Title;
        AcReports.Clear();
        _acAlignmentTables.Clear();
        foreach (var file in LoadedFiles)
        {
            var observations = _cache.GetDiagnosticObservations(new[] { file.FileId });
            LoadAcAlignmentTables(file, observations);
            foreach (var group in observations.Where(o => o.Module == "AC" && o.Kind == "Summary" && o.Item.Contains(" ["))
                         .GroupBy(o => (o.LoopIndex, Report: o.Item[o.Item.IndexOf(" [", StringComparison.Ordinal)..])))
                AcReports.Add(new AcReportView($"{file.FileName} / Loop {group.Key.LoopIndex}{group.Key.Report}",
                    group.Key.Report.Split(" / Report", StringSplitOptions.None)[0].Trim(' ', '['), group.ToArray()));
        }
        SelectedAcReport = AcReports.FirstOrDefault(r => r.Title == previous) ?? AcReports.FirstOrDefault();
        BaselineAcReport = AcReports.FirstOrDefault(r => r.Title == baseline);
        RefreshAcAlignmentChoices();
        OnPropertyChanged(nameof(HasAcOverview));
        OnPropertyChanged(nameof(IsAcOverviewActive));
        UpdateAcOverview();
    }

    private void UpdateAcOverview()
    {
        foreach (var axis in AcPinAxes.Concat(AcRangePinAxes)) { axis.MinLimit = null; axis.MaxLimit = null; }
        AcErrorAxes[0].MinLimit = null; AcErrorAxes[0].MaxLimit = null;
        if (AcRelativeMode) { UpdateAcRelativeChart(); UpdateAcPointCharts(); return; }
        UpdateAcPointCharts();
        AcErrorAxes[0].Name = "Error (ns)";
        var rows = ReadAcRows(SelectedAcReport);
        AcPinRows = rows;
        var errors = new List<ISeries>();
        var ranges = new List<ISeries>();
        if (rows.Count > 0)
        {
            AddDiagnosticLine(errors, "Current Max", rows.Select(r => ((double)r.Pin, r.Max)), SKColor.Parse("#E8590C"), true);
            AddDiagnosticLine(errors, "Current Min", rows.Select(r => ((double)r.Pin, r.Min)), SKColor.Parse("#1971C2"), true);
            AddDiagnosticLine(errors, "Zero error", new[] { ((double)rows[0].Pin, 0d), ((double)rows[^1].Pin, 0d) }, SKColors.Gray, true, 1);
            AddDiagnosticLine(ranges, "Current Range", rows.Select(r => ((double)r.Pin, r.Range)), SKColor.Parse("#7048E8"), true);
            var worst = rows.MaxBy(r => Math.Max(Math.Abs(r.Max), Math.Abs(r.Min)))!;
            var widest = rows.MaxBy(r => r.Range)!;
            AcOverviewSummary = $"{SelectedAcEdge} · {rows.Count:N0} Pins  |  Max absolute error {Math.Max(Math.Abs(worst.Max), Math.Abs(worst.Min)):F3} ns (Pin {worst.Pin})  |  Max range {widest.Range:F3} ns (Pin {widest.Pin})";
            if (BaselineAcReport != null)
            {
                if (BaselineAcReport.Clock != SelectedAcReport!.Clock || BaselineAcReport.Clock == "Clock unspecified")
                    AcOverviewSummary += "  |  Baseline not overlaid: select reports with the same known clock.";
                else
                {
                    var pins = rows.Select(r => r.Pin).ToHashSet();
                    var before = ReadAcRows(BaselineAcReport).Where(r => pins.Contains(r.Pin)).ToArray();
                    AddDiagnosticLine(errors, "Baseline Max", before.Select(r => ((double)r.Pin, r.Max)), SKColor.Parse("#2B8A3E"), true);
                    AddDiagnosticLine(errors, "Baseline Min", before.Select(r => ((double)r.Pin, r.Min)), SKColor.Parse("#C2255C"), true);
                    AddDiagnosticLine(ranges, "Baseline Range", before.Select(r => ((double)r.Pin, r.Range)), SKColor.Parse("#2B8A3E"), true);
                    AcOverviewSummary += $"  |  Common pins in baseline: {before.Length:N0} (test conditions must match)";
                }
            }
        }
        else AcOverviewSummary = AcReports.Count == 0 ? "No AC final summary found. Load a LOG containing a CLK Verify result table." : "No valid values for this edge. NA values are not plotted as zero.";
        AcErrorSeries = errors.ToArray();
        AcRangeSeries = ranges.ToArray();
    }

    private List<AcPinRow> ReadAcRows(AcReportView? report)
    {
        var rows = new List<AcPinRow>();
        if (report != null)
        {
            foreach (var pin in report.Observations
                         .Where(o => o.Channel.HasValue && o.Item.StartsWith(SelectedAcEdge + "(ns) ", StringComparison.Ordinal))
                         .GroupBy(o => o.Channel!.Value).OrderBy(g => g.Key))
            {
                var max = pin.SingleOrDefault(o => o.Item.StartsWith(SelectedAcEdge + "(ns) Max ", StringComparison.Ordinal));
                var min = pin.SingleOrDefault(o => o.Item.StartsWith(SelectedAcEdge + "(ns) Min ", StringComparison.Ordinal));
                var range = pin.SingleOrDefault(o => o.Item.StartsWith(SelectedAcEdge + "(ns) Range ", StringComparison.Ordinal));
                if (max?.Value is double hi && min?.Value is double lo && range?.Value is double span)
                    rows.Add(new AcPinRow(pin.Key, hi, lo, span, max.LineNumber));
            }
        }
        return rows;
    }
}

public sealed record AcReportView(string Title, string Clock, IReadOnlyList<DiagnosticObservation> Observations);
public sealed record AcPinRow(int Pin, double Max, double Min, double Range, int Line);
