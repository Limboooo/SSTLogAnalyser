using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using SSTLogAnalyser.Models;
using SSTLogAnalyser.Services;
using Microsoft.Win32;

namespace SSTLogAnalyser.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly CacheService _cache;
    private readonly LogParser _parser;

    [ObservableProperty] private ISeries[] _series = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _xAxes = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _yAxes = Array.Empty<Axis>();
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private int _progressValue;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _selectedModule;
    [ObservableProperty] private string? _selectedTestItem;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _compareMultiChannel = true;
    [ObservableProperty] private bool _compareMultiLoop;
    [ObservableProperty] private bool _showLegend = true;
    [ObservableProperty] private bool _performanceMode;
    [ObservableProperty] private bool _tooltipVisible;
    [ObservableProperty] private string _tooltipTitle = string.Empty;
    [ObservableProperty] private string _tooltipExpect = string.Empty;
    [ObservableProperty] private string _tooltipDiff = string.Empty;
    [ObservableProperty] private string _tooltipExtra = string.Empty;

    public ObservableCollection<LogFileInfo> LoadedFiles { get; } = new();
    public ObservableCollection<string> AvailableModules { get; } = new();
    public ObservableCollection<int> AvailableChannels { get; } = new();
    public ObservableCollection<string> AvailableTestItems { get; } = new();
    public ObservableCollection<int> AvailableLoops { get; } = new();
    public ObservableCollection<int> SelectedChannels { get; } = new();
    public ObservableCollection<int> SelectedLoops { get; } = new();
    public ObservableCollection<ErrorLogEntry> ErrorEntries { get; } = new();
    public ObservableCollection<DeviceInfo> DeviceEntries { get; } = new();
    public ObservableCollection<SystemInfo> SystemInfos { get; } = new();
    public ObservableCollection<StatEntry> Statistics { get; } = new();
    public ObservableCollection<PassFailEntry> PassFailEntries { get; } = new();
    public ObservableCollection<LegendItem> LegendItems { get; } = new();
    public List<TooltipDataPoint> CurrentChartData { get; private set; } = new();
    public ObservableCollection<TestResult> ChartDataRows { get; } = new();
    [ObservableProperty] private TestResult? _selectedChartRow;

    private static readonly SKColor[] Palette = new[]
    {
        SKColor.Parse("#2196F3"), SKColor.Parse("#4CAF50"), SKColor.Parse("#FF9800"),
        SKColor.Parse("#9C27B0"), SKColor.Parse("#00BCD4"), SKColor.Parse("#E91E63"),
        SKColor.Parse("#795548"), SKColor.Parse("#607D8B"), SKColor.Parse("#FF5722"),
        SKColor.Parse("#3F51B5"), SKColor.Parse("#8BC34A"), SKColor.Parse("#CDDC39"),
    };

    public MainViewModel()
    {
        _cache = new CacheService();
        _parser = new LogParser();
        SelectedChannels.CollectionChanged += (_, _) => { RefreshTestItems(); UpdateChart(); };
        SelectedLoops.CollectionChanged += (_, _) => UpdateChart();
        InitializeChart();
    }

    [RelayCommand]
    private void ToggleLegend() { ShowLegend = !ShowLegend; }

    [RelayCommand]
    private async Task LoadFilesAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select LOG Files",
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog() != true) return;
        await LoadFilesInternalAsync(dialog.FileNames);
    }

    public async Task LoadFilesFromPathsAsync(string[] paths) => await LoadFilesInternalAsync(paths);

    private async Task LoadFilesInternalAsync(string[] paths)
    {
        IsLoading = true;
        ProgressValue = 0;
        StatusText = "Loading files...";
        try
        {
            foreach (var path in paths)
            {
                StatusText = "Processing: " + Path.GetFileName(path);
                var hash = await FileHashService.ComputeSha256Async(path);
                var existingId = _cache.FindFileByHash(hash);
                if (existingId.HasValue)
                {
                    var info = _cache.GetFileInfo(existingId.Value);
                    if (info != null && !LoadedFiles.Any(f => f.FileId == info.FileId))
                        LoadedFiles.Add(info);
                    StatusText = "Loaded from cache: " + Path.GetFileName(path);
                    continue;
                }
                var progress = new Progress<ParseProgress>(p =>
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        ProgressValue = p.Percent;
                        StatusText = "Parsing " + Path.GetFileName(path) + ": " + p.Percent + "% (" + p.DataPoints + " data points)";
                    });
                });
                var result = await _parser.ParseAsync(path, progress);
                var fileId = _cache.InsertLogFile(result.FileInfo, hash);
                result.FileInfo.FileId = fileId;
                if (result.TestResults.Count > 0) _cache.InsertTestResults(fileId, result.TestResults);
                if (result.Devices.Count > 0) _cache.InsertDevices(fileId, result.Devices);
                if (result.Errors.Count > 0) _cache.InsertErrors(fileId, result.Errors);
                if (result.SystemInfos.Count > 0) _cache.InsertSystemInfos(fileId, result.SystemInfos);
                LoadedFiles.Add(result.FileInfo);
                StatusText = "Parsed: " + Path.GetFileName(path) + " (" + result.TestResults.Count + " data points)";
            }
            RefreshFilters();
            UpdateChart();
            RefreshSecondaryViews();
        }
        catch (Exception ex) { StatusText = "Error: " + ex.Message; }
        finally { IsLoading = false; ProgressValue = 100; }
    }

    partial void OnSelectedModuleChanged(string? value)
    { RefreshChannels(); RefreshTestItems(); RefreshLoops(); UpdateChart(); RefreshSecondaryViews(); }
    partial void OnSelectedTestItemChanged(string? value) { RefreshLoops(); UpdateChart(); RefreshSecondaryViews(); }
    partial void OnSearchTextChanged(string value) { RefreshTestItems(); UpdateChart(); }
    partial void OnCompareMultiChannelChanged(bool value) { if (value) { CompareMultiLoop = false; SelectedChannels.Clear(); } UpdateChart(); }
    partial void OnCompareMultiLoopChanged(bool value) { if (value) CompareMultiChannel = false; UpdateChart(); }
    partial void OnPerformanceModeChanged(bool value) { UpdateChart(); }

    private void RefreshFilters()
    {
        var fileIds = LoadedFiles.Select(f => f.FileId).ToArray();
        if (fileIds.Length == 0) return;
        AvailableModules.Clear();
        foreach (var m in _cache.GetDistinctModules(fileIds)) AvailableModules.Add(m);
    }

    private void RefreshChannels()
    {
        var fileIds = LoadedFiles.Select(f => f.FileId).ToArray();
        if (fileIds.Length == 0) return;
        AvailableChannels.Clear();
        foreach (var c in _cache.GetDistinctChannels(fileIds, SelectedModule)) AvailableChannels.Add(c);
    }

    private void RefreshTestItems()
    {
        var fileIds = LoadedFiles.Select(f => f.FileId).ToArray();
        if (fileIds.Length == 0) return;
        AvailableTestItems.Clear();
        var channels = SelectedChannels.Count > 0 ? SelectedChannels.ToArray() : null;
        foreach (var t in _cache.GetDistinctTestItems(fileIds, SelectedModule, channels))
        {
            if (!string.IsNullOrEmpty(SearchText) && !t.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                continue;
            AvailableTestItems.Add(t);
        }
        if (SelectedTestItem != null && !AvailableTestItems.Contains(SelectedTestItem))
            SelectedTestItem = null;
    }

    private void RefreshLoops()
    {
        var fileIds = LoadedFiles.Select(f => f.FileId).ToArray();
        if (fileIds.Length == 0) return;
        AvailableLoops.Clear();
        foreach (var l in _cache.GetDistinctLoops(fileIds, SelectedModule, SelectedTestItem)) AvailableLoops.Add(l);
    }

    private void InitializeChart()
    {
        XAxes = new[] { new Axis { Name = "Expected Value", NameTextSize = 13, TextSize = 11 } };
        YAxes = new[] { new Axis { Name = "Difference", NameTextSize = 13, TextSize = 11 } };
        Series = Array.Empty<ISeries>();
        LegendItems.Clear();
        CurrentChartData.Clear();
    }

    private void UpdateChart()
    {
        var fileIds = LoadedFiles.Select(f => f.FileId).ToArray();
        if (fileIds.Length == 0 || SelectedModule == null || SelectedTestItem == null)
        { Series = Array.Empty<ISeries>(); LegendItems.Clear(); CurrentChartData.Clear(); ChartDataRows.Clear(); return; }

        var channels = CompareMultiChannel ? null : (SelectedChannels.Count > 0 ? SelectedChannels.ToArray() : null);
        var loops = SelectedLoops.Count > 0 ? SelectedLoops.ToArray() : null;
        var data = _cache.QueryTestResults(fileIds, SelectedModule, SelectedTestItem, channels, loops);
        if (data.Count == 0)
        { Series = Array.Empty<ISeries>(); LegendItems.Clear(); CurrentChartData.Clear(); ChartDataRows.Clear(); return; }

        var seriesList = new List<ISeries>();
        LegendItems.Clear();
        CurrentChartData.Clear();
        ChartDataRows.Clear();
        foreach (var r in data) ChartDataRows.Add(r);
        int colorIdx = 0;
        int groupIdx = 0;
        var multiFile = LoadedFiles.Count > 1;
        var fileNameMap = LoadedFiles.ToDictionary(f => f.FileId, f => Path.GetFileNameWithoutExtension(f.FileName));
        string Fpfx(long fid) => multiFile && fileNameMap.ContainsKey(fid) ? "[" + fileNameMap[fid] + "] " : "";

        if (CompareMultiChannel)
        {
            var groups = data.GroupBy(r => (r.FileId, r.ChannelId))
                             .OrderBy(g => g.Key.FileId).ThenBy(g => g.Key.ChannelId);
            foreach (var group in groups)
            {
                var color = Palette[colorIdx % Palette.Length];
                colorIdx++;
                AddGroupSeries(seriesList, Fpfx(group.Key.FileId) + "Ch" + group.Key.ChannelId,
                    group.ToList(), color, groupIdx == 0);
                groupIdx++;
            }
        }
        else
        {
            var channelId = channels?.FirstOrDefault() ?? data.First().ChannelId;
            var groups = data.Where(r => channels == null || channels.Contains(r.ChannelId))
                             .GroupBy(r => (r.FileId, r.LoopIndex))
                             .OrderBy(g => g.Key.FileId).ThenBy(g => g.Key.LoopIndex);
            foreach (var group in groups)
            {
                var color = Palette[colorIdx % Palette.Length];
                colorIdx++;
                var loopLabel = group.Key.LoopIndex == 0 ? "Initial" : "Loop " + group.Key.LoopIndex;
                AddGroupSeries(seriesList, Fpfx(group.Key.FileId) + loopLabel,
                    group.ToList(), color, groupIdx == 0);
                groupIdx++;
            }
        }

        Series = seriesList.ToArray();
        UpdateStatistics(data);
    }

    private void AddGroupSeries(List<ISeries> seriesList, string title, List<TestResult> points, SKColor color, bool drawLimits)
    {
        if (points.Count == 0) return;
        var ordered = points.OrderBy(r => r.ExpectValue).ToList();
        bool isMixi = ordered.Count > 0 && ordered[0].ModuleType == ModuleType.MIXI;

        if (PerformanceMode)
        {
            // Lightweight: line + limits, no geometry markers, no failed overlay
            var lineSeries = new LineSeries<ObservablePoint>
            {
                Name = title,
                Values = ordered.Select(p => new ObservablePoint(p.ExpectValue, p.Difference)).ToArray(),
                Stroke = new SolidColorPaint(color) { StrokeThickness = 1.5f },
                GeometryStroke = null,
                GeometrySize = 0f,
                Fill = null,
                LineSmoothness = 0f
            };
            seriesList.Add(lineSeries);
            LegendItems.Add(new LegendItem { Name = title, ColorBrush = new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue)) });

            foreach (var p in ordered)
                CurrentChartData.Add(new TooltipDataPoint { SeriesName = title, ExpectValue = p.ExpectValue, Difference = p.Difference, MeasureValue = p.MeasureValue, IsFailed = p.IsFailed, IsLimit = false, WaveValue = p.WaveValue, OffsetValue = p.OffsetValue, DiffValue = p.DiffValue, RowIndex = ChartDataRows.IndexOf(p) });

            if (drawLimits)
            {
                var highLimit = new LineSeries<ObservablePoint>
                {
                    Name = "High Limit",
                    Values = ordered.Select(p => new ObservablePoint(p.ExpectValue, isMixi ? p.UpLimit - p.ExpectValue : p.UpLimit)).ToArray(),
                    Stroke = new SolidColorPaint(new SKColor(220, 50, 50, 160)) { StrokeThickness = 1.5f },
                    GeometryStroke = null, GeometrySize = 0f, Fill = null, LineSmoothness = 0f
                };
                seriesList.Add(highLimit);
                LegendItems.Add(new LegendItem { Name = "High Limit", ColorBrush = new SolidColorBrush(Color.FromRgb(220, 50, 50)) });

                var lowLimit = new LineSeries<ObservablePoint>
                {
                    Name = "Low Limit",
                    Values = ordered.Select(p => new ObservablePoint(p.ExpectValue, isMixi ? p.LowLimit - p.ExpectValue : p.LowLimit)).ToArray(),
                    Stroke = new SolidColorPaint(new SKColor(50, 100, 220, 160)) { StrokeThickness = 1.5f },
                    GeometryStroke = null, GeometrySize = 0f, Fill = null, LineSmoothness = 0f
                };
                seriesList.Add(lowLimit);
                LegendItems.Add(new LegendItem { Name = "Low Limit", ColorBrush = new SolidColorBrush(Color.FromRgb(50, 100, 220)) });

                foreach (var p in ordered)
                {
                    CurrentChartData.Add(new TooltipDataPoint { SeriesName = "High Limit", ExpectValue = p.ExpectValue, Difference = isMixi ? p.UpLimit - p.ExpectValue : p.UpLimit, IsLimit = true });
                    CurrentChartData.Add(new TooltipDataPoint { SeriesName = "Low Limit", ExpectValue = p.ExpectValue, Difference = isMixi ? p.LowLimit - p.ExpectValue : p.LowLimit, IsLimit = true });
                }
            }
        }
        else
        {
            // Quality: LineSeries with ObservablePoint, markers, limit lines, failed overlays
            var lineSeries = new LineSeries<ObservablePoint>
            {
                Name = title,
                Values = ordered.Select(p => new ObservablePoint(p.ExpectValue, p.Difference)).ToArray(),
                Stroke = new SolidColorPaint(color) { StrokeThickness = 2f },
                GeometryStroke = new SolidColorPaint(color) { StrokeThickness = 2f },
                GeometrySize = 6f,
                Fill = null,
                LineSmoothness = 0f
            };
            seriesList.Add(lineSeries);
            LegendItems.Add(new LegendItem { Name = title, ColorBrush = new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue)) });

            foreach (var p in ordered)
                CurrentChartData.Add(new TooltipDataPoint { SeriesName = title, ExpectValue = p.ExpectValue, Difference = p.Difference, MeasureValue = p.MeasureValue, IsFailed = p.IsFailed, IsLimit = false, WaveValue = p.WaveValue, OffsetValue = p.OffsetValue, DiffValue = p.DiffValue, RowIndex = ChartDataRows.IndexOf(p) });

            if (drawLimits)
            {
                var highLimit = new LineSeries<ObservablePoint>
                {
                    Name = "High Limit",
                    Values = ordered.Select(p => new ObservablePoint(p.ExpectValue, isMixi ? p.UpLimit - p.ExpectValue : p.UpLimit)).ToArray(),
                    Stroke = new SolidColorPaint(new SKColor(220, 50, 50, 160)) { StrokeThickness = 1.5f },
                    GeometryStroke = null, GeometrySize = 0f, Fill = null, LineSmoothness = 0f
                };
                seriesList.Add(highLimit);
                LegendItems.Add(new LegendItem { Name = "High Limit", ColorBrush = new SolidColorBrush(Color.FromRgb(220, 50, 50)) });

                var lowLimit = new LineSeries<ObservablePoint>
                {
                    Name = "Low Limit",
                    Values = ordered.Select(p => new ObservablePoint(p.ExpectValue, isMixi ? p.LowLimit - p.ExpectValue : p.LowLimit)).ToArray(),
                    Stroke = new SolidColorPaint(new SKColor(50, 100, 220, 160)) { StrokeThickness = 1.5f },
                    GeometryStroke = null, GeometrySize = 0f, Fill = null, LineSmoothness = 0f
                };
                seriesList.Add(lowLimit);
                LegendItems.Add(new LegendItem { Name = "Low Limit", ColorBrush = new SolidColorBrush(Color.FromRgb(50, 100, 220)) });

                foreach (var p in ordered)
                {
                    CurrentChartData.Add(new TooltipDataPoint { SeriesName = "High Limit", ExpectValue = p.ExpectValue, Difference = isMixi ? p.UpLimit - p.ExpectValue : p.UpLimit, IsLimit = true });
                    CurrentChartData.Add(new TooltipDataPoint { SeriesName = "Low Limit", ExpectValue = p.ExpectValue, Difference = isMixi ? p.LowLimit - p.ExpectValue : p.LowLimit, IsLimit = true });
                }
            }

            var failedPoints = ordered.Where(p => p.IsFailed).ToList();
            if (failedPoints.Count > 0)
            {
                var failSeries = new ScatterSeries<ObservablePoint>
                {
                    Name = title + " (Failed)",
                    Values = failedPoints.Select(p => new ObservablePoint(p.ExpectValue, p.Difference)).ToArray(),
                    Fill = new SolidColorPaint(SKColors.Red), GeometrySize = 12f
                };
                seriesList.Add(failSeries);
                LegendItems.Add(new LegendItem { Name = title + " (Failed)", ColorBrush = Brushes.Red });
            }
        }
    }

    private void RefreshSecondaryViews()
    {
        var fileIds = LoadedFiles.Select(f => f.FileId).ToArray();
        if (fileIds.Length == 0) return;
        ErrorEntries.Clear(); foreach (var e in _cache.GetErrors(fileIds)) ErrorEntries.Add(e);
        DeviceEntries.Clear(); foreach (var d in _cache.GetDevices(fileIds)) DeviceEntries.Add(d);
        SystemInfos.Clear(); foreach (var s in _cache.GetSystemInfos(fileIds)) SystemInfos.Add(s);
        PassFailEntries.Clear();
        if (SelectedModule != null)
            foreach (var e in _cache.GetPassFailSummary(fileIds, SelectedModule)) PassFailEntries.Add(e);
    }

    private void UpdateStatistics(List<TestResult> data)
    {
        Statistics.Clear();
        if (data == null || data.Count == 0) return;
        var multiFile = LoadedFiles.Count > 1;
        var fileNameMap = LoadedFiles.ToDictionary(f => f.FileId, f => Path.GetFileNameWithoutExtension(f.FileName));
        var groups = CompareMultiChannel
            ? data.GroupBy(r => (multiFile && fileNameMap.ContainsKey(r.FileId) ? "[" + fileNameMap[r.FileId] + "] " : "") + "Ch" + r.ChannelId)
            : data.GroupBy(r => (multiFile && fileNameMap.ContainsKey(r.FileId) ? "[" + fileNameMap[r.FileId] + "] " : "") + (r.LoopIndex == 0 ? "Initial" : "Loop " + r.LoopIndex));
        foreach (var g in groups.OrderBy(x => x.Key))
        {
            var diffs = g.Select(r => r.Difference).ToList();
            if (diffs.Count == 0) continue;
            var mean = diffs.Average();
            Statistics.Add(new StatEntry
            {
                GroupName = g.Key, Count = diffs.Count,
                Max = diffs.Max(), Min = diffs.Min(), Mean = mean,
                StdDev = Math.Sqrt(diffs.Average(d => (d - mean) * (d - mean))),
                FailCount = g.Count(r => r.IsFailed)
            });
        }
    }

    [RelayCommand]
    private void ClearAll()
    {
        LoadedFiles.Clear(); AvailableModules.Clear(); AvailableChannels.Clear();
        AvailableTestItems.Clear(); AvailableLoops.Clear();
        SelectedChannels.Clear(); SelectedLoops.Clear();
        ErrorEntries.Clear(); DeviceEntries.Clear(); SystemInfos.Clear();
        Statistics.Clear(); PassFailEntries.Clear(); LegendItems.Clear(); CurrentChartData.Clear(); ChartDataRows.Clear(); SelectedChartRow = null;
        SelectedModule = null; SelectedTestItem = null;
        InitializeChart(); StatusText = "Ready";
    }
}

public class StatEntry
{
    public string GroupName { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Max { get; set; }
    public double Min { get; set; }
    public double Mean { get; set; }
    public double StdDev { get; set; }
    public int FailCount { get; set; }
}

public class LegendItem
{
    public string Name { get; set; } = string.Empty;
    public SolidColorBrush ColorBrush { get; set; } = Brushes.Gray;
}

public class TooltipDataPoint
{
    public string SeriesName { get; set; } = string.Empty;
    public double ExpectValue { get; set; }
    public double Difference { get; set; }
    public double MeasureValue { get; set; }
    public bool IsFailed { get; set; }
    public bool IsLimit { get; set; }
    public double? WaveValue { get; set; }
    public double? OffsetValue { get; set; }
    public double? DiffValue { get; set; }
    public int RowIndex { get; set; } = -1;
}
