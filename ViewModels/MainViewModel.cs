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
    [ObservableProperty] private bool _hasSeriesFocus;
    [ObservableProperty] private ISeries[] _toleranceHeatmapSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _toleranceHeatmapXAxes = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _toleranceHeatmapYAxes = Array.Empty<Axis>();
    [ObservableProperty] private string _toleranceHeatmapSummary = "Load a LOG file to calculate tolerance utilization.";
    [ObservableProperty] private ISeries[] _gainSeries = Array.Empty<ISeries>();
    [ObservableProperty] private ISeries[] _offsetSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _gainXAxes = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _offsetXAxes = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _gainYAxes = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _offsetYAxes = Array.Empty<Axis>();
    [ObservableProperty] private string _coefficientSummary = "No calibration coefficient data.";
    [ObservableProperty] private string? _selectedCoefficientName;
    [ObservableProperty] private ISeries[] _residualSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _residualXAxes = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _residualYAxes = Array.Empty<Axis>();
    [ObservableProperty] private string _residualSummary = "Select a test item to inspect residual behavior.";
    [ObservableProperty] private ISeries[] _symmetrySeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _symmetryXAxes = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _symmetryYAxes = Array.Empty<Axis>();
    [ObservableProperty] private string _symmetrySummary = "Select MIXI/AWG data to inspect POS/NEG symmetry.";

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
    public ObservableCollection<string> AvailableCoefficientNames { get; } = new();
    public List<TooltipDataPoint> CurrentChartData { get; private set; } = new();
    public ObservableCollection<TestResult> ChartDataRows { get; } = new();
    [ObservableProperty] private TestResult? _selectedChartRow;
    private readonly Dictionary<ISeries, string> _seriesFocusKeys = new();
    private readonly HashSet<string> _focusedSeriesNames = new(StringComparer.Ordinal);
    private bool _isUpdatingChannelSelection;
    private bool _isUpdatingLoopSelection;

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
        SelectedChannels.CollectionChanged += (_, _) =>
        {
            if (_isUpdatingChannelSelection) return;
            RefreshTestItems();
            UpdateChart();
            UpdateDiagnostics();
        };
        SelectedLoops.CollectionChanged += (_, _) =>
        {
            if (!_isUpdatingLoopSelection)
            {
                UpdateChart();
                UpdateDiagnostics();
            }
        };
        InitializeChart();
        InitializeDiagnostics();
    }

    [RelayCommand]
    private void ToggleLegend() { ShowLegend = !ShowLegend; }

    [RelayCommand]
    private void ResetChartZoom()
    {
        foreach (var axis in XAxes.Concat(YAxes))
        {
            axis.MinLimit = null;
            axis.MaxLimit = null;
        }
    }

    [RelayCommand]
    private void ToggleSeriesFocus(LegendItem? item)
    {
        if (string.IsNullOrEmpty(item?.FocusKey)) return;

        if (!_focusedSeriesNames.Add(item.FocusKey))
            _focusedSeriesNames.Remove(item.FocusKey);
        ApplySeriesFocus();
    }

    [RelayCommand]
    private void ShowAllSeries()
    {
        _focusedSeriesNames.Clear();
        ApplySeriesFocus();
    }

    private void ApplySeriesFocus()
    {
        HasSeriesFocus = _focusedSeriesNames.Count > 0;

        foreach (var series in Series)
        {
            var belongsToGroup = _seriesFocusKeys.TryGetValue(series, out var focusKey);
            series.IsVisible = !HasSeriesFocus || !belongsToGroup || _focusedSeriesNames.Contains(focusKey!);
        }

        foreach (var item in LegendItems)
        {
            var isFocused = item.FocusKey != null && _focusedSeriesNames.Contains(item.FocusKey);
            item.DisplayOpacity = !HasSeriesFocus || item.FocusKey == null || isFocused ? 1 : 0.35;
            item.FontWeight = isFocused
                ? FontWeights.Bold
                : FontWeights.Normal;
        }
    }

    public bool IsChartSeriesVisible(string seriesName) =>
        _focusedSeriesNames.Count == 0 || _focusedSeriesNames.Contains(seriesName);

    public void SetSelectedChannels(IEnumerable<int> channels)
    {
        var selection = channels.Distinct().OrderBy(channel => channel).ToArray();
        if (SelectedChannels.SequenceEqual(selection)) return;

        _isUpdatingChannelSelection = true;
        try
        {
            SelectedChannels.Clear();
            foreach (var channel in selection)
                SelectedChannels.Add(channel);
        }
        finally
        {
            _isUpdatingChannelSelection = false;
        }

        RefreshTestItems();
        UpdateChart();
        UpdateDiagnostics();
    }

    public void SetSelectedLoops(IEnumerable<int> loops)
    {
        var selection = loops.Distinct().OrderBy(loop => loop).ToArray();
        if (SelectedLoops.SequenceEqual(selection)) return;

        _isUpdatingLoopSelection = true;
        try
        {
            SelectedLoops.Clear();
            foreach (var loop in selection)
                SelectedLoops.Add(loop);
        }
        finally
        {
            _isUpdatingLoopSelection = false;
        }

        UpdateChart();
        UpdateDiagnostics();
    }

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
                if (result.CalibrationCoefficients.Count > 0)
                    _cache.InsertCalibrationCoefficients(fileId, result.CalibrationCoefficients);
                if (result.Devices.Count > 0) _cache.InsertDevices(fileId, result.Devices);
                if (result.Errors.Count > 0) _cache.InsertErrors(fileId, result.Errors);
                if (result.SystemInfos.Count > 0) _cache.InsertSystemInfos(fileId, result.SystemInfos);
                LoadedFiles.Add(result.FileInfo);
                StatusText = "Parsed: " + Path.GetFileName(path) + " (" + result.TestResults.Count + " data points)";
            }
            RefreshFilters();
            RefreshCoefficientNames();
            UpdateChart();
            UpdateDiagnostics();
            RefreshSecondaryViews();
        }
        catch (Exception ex) { StatusText = "Error: " + ex.Message; }
        finally { IsLoading = false; ProgressValue = 100; }
    }

    partial void OnSelectedModuleChanged(string? value)
    {
        RefreshChannels();
        RefreshTestItems();
        RefreshLoops();
        RefreshCoefficientNames();
        UpdateChart();
        UpdateDiagnostics();
        RefreshSecondaryViews();
    }
    partial void OnSelectedTestItemChanged(string? value)
    {
        RefreshLoops();
        UpdateChart();
        var fileIds = LoadedFiles.Select(f => f.FileId).ToArray();
        if (fileIds.Length > 0)
        {
            UpdateResidualChart(fileIds);
            UpdateSymmetryChart(fileIds);
        }
        RefreshSecondaryViews();
    }
    partial void OnSelectedCoefficientNameChanged(string? value) => UpdateCoefficientCharts();
    partial void OnSearchTextChanged(string value) { RefreshTestItems(); UpdateChart(); UpdateDiagnostics(); }
    partial void OnCompareMultiChannelChanged(bool value) { if (value) CompareMultiLoop = false; UpdateChart(); }
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
        var selectedTestItem = SelectedTestItem;
        var channels = SelectedChannels.Count > 0 ? SelectedChannels.ToArray() : null;
        var testItems = _cache.GetDistinctTestItems(fileIds, SelectedModule, channels)
            .Where(t => string.IsNullOrEmpty(SearchText) ||
                        t.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            .ToList();

        SynchronizeTestItems(testItems);

        if (selectedTestItem != null)
            SelectedTestItem = testItems.Contains(selectedTestItem) ? selectedTestItem : null;
    }

    private void SynchronizeTestItems(IReadOnlyList<string> testItems)
    {
        var available = testItems.ToHashSet(StringComparer.Ordinal);
        for (var i = AvailableTestItems.Count - 1; i >= 0; i--)
        {
            if (!available.Contains(AvailableTestItems[i]))
                AvailableTestItems.RemoveAt(i);
        }

        for (var targetIndex = 0; targetIndex < testItems.Count; targetIndex++)
        {
            var currentIndex = AvailableTestItems.IndexOf(testItems[targetIndex]);
            if (currentIndex < 0)
                AvailableTestItems.Insert(targetIndex, testItems[targetIndex]);
            else if (currentIndex != targetIndex)
                AvailableTestItems.Move(currentIndex, targetIndex);
        }
    }

    private void RefreshLoops()
    {
        var fileIds = LoadedFiles.Select(f => f.FileId).ToArray();
        if (fileIds.Length == 0) return;
        AvailableLoops.Clear();
        foreach (var l in _cache.GetDistinctLoops(fileIds, SelectedModule, SelectedTestItem)) AvailableLoops.Add(l);
    }

    private void RefreshCoefficientNames()
    {
        var fileIds = LoadedFiles.Select(f => f.FileId).ToArray();
        var previous = SelectedCoefficientName;
        AvailableCoefficientNames.Clear();

        if (fileIds.Length == 0 || SelectedModule == null)
        {
            SelectedCoefficientName = null;
            UpdateCoefficientCharts();
            return;
        }

        foreach (var name in _cache.GetDistinctCoefficientNames(fileIds, SelectedModule))
            AvailableCoefficientNames.Add(name);

        var next = previous != null && AvailableCoefficientNames.Contains(previous)
            ? previous
            : AvailableCoefficientNames.FirstOrDefault();
        SelectedCoefficientName = next;
        UpdateCoefficientCharts();
    }

    private void InitializeChart()
    {
        _focusedSeriesNames.Clear();
        HasSeriesFocus = false;
        XAxes = new[] { new Axis { Name = "Expected Value", NameTextSize = 13, TextSize = 11 } };
        YAxes = new[] { new Axis { Name = "Difference", NameTextSize = 13, TextSize = 11 } };
        Series = Array.Empty<ISeries>();
        LegendItems.Clear();
        CurrentChartData.Clear();
    }

    private void InitializeDiagnostics()
    {
        ToleranceHeatmapSeries = Array.Empty<ISeries>();
        ToleranceHeatmapXAxes = new[] { new Axis { Name = "Channel", MinStep = 1, TextSize = 9 } };
        ToleranceHeatmapYAxes = new[] { new Axis { Name = "Test Item", MinStep = 1, ForceStepToMin = true, TextSize = 9 } };

        GainXAxes = new[] { new Axis { Name = "Channel", MinStep = 1, TextSize = 9 } };
        OffsetXAxes = new[] { new Axis { Name = "Channel", MinStep = 1, TextSize = 9 } };
        GainYAxes = new[] { new Axis { Name = "Gain", TextSize = 10 } };
        OffsetYAxes = new[] { new Axis { Name = "Offset", TextSize = 10 } };

        ResidualXAxes = new[] { new Axis { Name = "Expected / Target", NameTextSize = 10, TextSize = 9 } };
        ResidualYAxes = new[] { new Axis { Name = "Residual", NameTextSize = 10, TextSize = 9 } };
        SymmetryXAxes = new[] { new Axis { Name = "Target", TextSize = 10 } };
        SymmetryYAxes = new[] { new Axis { Name = "Meas - Target", TextSize = 10 } };
    }

    private void UpdateChart()
    {
        ResetChartZoom();
        _focusedSeriesNames.Clear();
        HasSeriesFocus = false;
        _seriesFocusKeys.Clear();
        var fileIds = LoadedFiles.Select(f => f.FileId).ToArray();
        if (fileIds.Length == 0 || SelectedModule == null || SelectedTestItem == null)
        { Series = Array.Empty<ISeries>(); LegendItems.Clear(); CurrentChartData.Clear(); ChartDataRows.Clear(); return; }

        var channels = SelectedChannels.Count > 0 ? SelectedChannels.ToArray() : null;
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
        var hasMultipleLoops = data.Select(r => r.LoopIndex).Distinct().Take(2).Count() > 1;
        string Fpfx(long fid) => multiFile && fileNameMap.ContainsKey(fid) ? "[" + fileNameMap[fid] + "] " : "";

        if (CompareMultiChannel)
        {
            var groups = data.GroupBy(r => (r.FileId, r.ChannelId, r.LoopIndex))
                             .OrderBy(g => g.Key.FileId).ThenBy(g => g.Key.ChannelId).ThenBy(g => g.Key.LoopIndex);
            foreach (var group in groups)
            {
                var color = Palette[colorIdx % Palette.Length];
                colorIdx++;
                var loopLabel = group.Key.LoopIndex == 0 ? "Initial" : "Loop " + group.Key.LoopIndex;
                var title = Fpfx(group.Key.FileId) + "Ch" + group.Key.ChannelId +
                            (hasMultipleLoops ? " / " + loopLabel : "");
                AddGroupSeries(seriesList, title,
                    group.ToList(), color, groupIdx == 0);
                groupIdx++;
            }
        }
        else
        {
            var hasMultipleChannels = data.Select(r => r.ChannelId).Distinct().Take(2).Count() > 1;
            var groups = data.GroupBy(r => (r.FileId, r.ChannelId, r.LoopIndex))
                             .OrderBy(g => g.Key.FileId).ThenBy(g => g.Key.ChannelId).ThenBy(g => g.Key.LoopIndex);
            foreach (var group in groups)
            {
                var color = Palette[colorIdx % Palette.Length];
                colorIdx++;
                var loopLabel = group.Key.LoopIndex == 0 ? "Initial" : "Loop " + group.Key.LoopIndex;
                var channelLabel = hasMultipleChannels ? "Ch" + group.Key.ChannelId + " / " : "";
                AddGroupSeries(seriesList, Fpfx(group.Key.FileId) + channelLabel + loopLabel,
                    group.ToList(), color, groupIdx == 0);
                groupIdx++;
            }
        }

        Series = seriesList.ToArray();
        UpdateStatistics(data);
    }

    private void UpdateDiagnostics()
    {
        var fileIds = LoadedFiles.Select(f => f.FileId).ToArray();
        if (fileIds.Length == 0 || SelectedModule == null)
        {
            ClearDiagnostics();
            return;
        }

        UpdateToleranceHeatmap(fileIds);
        UpdateCoefficientCharts();
        UpdateResidualChart(fileIds);
        UpdateSymmetryChart(fileIds);
    }

    private void ClearDiagnostics()
    {
        ToleranceHeatmapSeries = Array.Empty<ISeries>();
        GainSeries = Array.Empty<ISeries>();
        OffsetSeries = Array.Empty<ISeries>();
        ResidualSeries = Array.Empty<ISeries>();
        SymmetrySeries = Array.Empty<ISeries>();
        ToleranceHeatmapSummary = "Load a LOG file to calculate tolerance utilization.";
        CoefficientSummary = "No calibration coefficient data.";
        ResidualSummary = "Select a test item to inspect residual behavior.";
        SymmetrySummary = "Select MIXI/AWG data to inspect POS/NEG symmetry.";
    }

    private void UpdateToleranceHeatmap(long[] fileIds)
    {
        var channels = SelectedChannels.Count > 0 ? SelectedChannels.ToArray() : null;
        var loops = SelectedLoops.Count > 0 ? SelectedLoops.ToArray() : null;
        var search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText;
        var data = _cache.QueryTestResults(fileIds, SelectedModule, null, channels, loops, search);

        var cells = new Dictionary<(int Channel, string Item), (double Utilization, bool Failed)>();
        foreach (var result in data)
        {
            var utilization = CalculateToleranceUtilization(result);
            if (!utilization.HasValue) continue;

            var key = (result.ChannelId, result.TestItemName);
            var value = (Math.Max(utilization.Value, result.IsFailed ? 1.01 : 0), result.IsFailed);
            if (!cells.TryGetValue(key, out var existing) || value.Item1 > existing.Utilization)
                cells[key] = value;
            else if (value.Item2 && !existing.Failed)
                cells[key] = (existing.Utilization, true);
        }

        if (cells.Count == 0)
        {
            ToleranceHeatmapSeries = Array.Empty<ISeries>();
            ToleranceHeatmapSummary = "No limit data for the current module and filters.";
            return;
        }

        var channelLabels = cells.Keys.Select(k => k.Channel).Distinct().OrderBy(c => c).ToArray();
        var itemLabels = cells.GroupBy(c => c.Key.Item)
            .OrderBy(g => g.Max(c => c.Value.Utilization))
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .ToArray();
        var channelIndex = channelLabels.Select((channel, index) => (channel, index)).ToDictionary(x => x.channel, x => x.index);
        var itemIndex = itemLabels.Select((item, index) => (item, index)).ToDictionary(x => x.item, x => x.index);

        var values = cells.Select(cell => new WeightedPoint(
            channelIndex[cell.Key.Channel],
            itemIndex[cell.Key.Item],
            Math.Min(cell.Value.Utilization, 1.25))).ToArray();

        ToleranceHeatmapSeries = new ISeries[]
        {
            new HeatSeries<WeightedPoint>
            {
                Name = "Tolerance utilization",
                Values = values,
                HeatMap = new[]
                {
                    SKColor.Parse("#D9E2E5").AsLvcColor(),
                    SKColor.Parse("#4D9F8C").AsLvcColor(),
                    SKColor.Parse("#F0B43C").AsLvcColor(),
                    SKColor.Parse("#D94B4B").AsLvcColor()
                },
                ColorStops = new[] { 0d, 0.48d, 0.64d, 0.8d },
                MinValue = 0,
                MaxValue = 1.25
            }
        };
        ToleranceHeatmapXAxes = new[]
        {
            new Axis
            {
                Name = "Channel",
                Labels = channelLabels.Select(c => "Ch" + c).ToArray(),
                MinStep = 1,
                TextSize = 9
            }
        };
        ToleranceHeatmapYAxes = new[]
        {
            new Axis
            {
                Name = "Test Item",
                Labels = itemLabels,
                MinStep = 1,
                ForceStepToMin = true,
                TextSize = 9
            }
        };

        var nearLimit = cells.Count(c => c.Value.Utilization >= 0.8 && c.Value.Utilization < 1);
        var failed = cells.Count(c => c.Value.Failed || c.Value.Utilization >= 1);
        var worst = cells.MaxBy(c => c.Value.Utilization);
        ToleranceHeatmapSummary = $"{cells.Count} cells  |  Near limit: {nearLimit}  |  Failed: {failed}  |  " +
            $"Worst: Ch{worst.Key.Channel} / {worst.Key.Item} ({worst.Value.Utilization:P1})";
    }

    private void UpdateCoefficientCharts()
    {
        var fileIds = LoadedFiles.Select(f => f.FileId).ToArray();
        if (fileIds.Length == 0 || SelectedModule == null || SelectedCoefficientName == null)
        {
            GainSeries = Array.Empty<ISeries>();
            OffsetSeries = Array.Empty<ISeries>();
            CoefficientSummary = AvailableCoefficientNames.Count == 0
                ? "No Gain / Offset coefficients were found for this module."
                : "Select a calibration coefficient.";
            return;
        }

        var channels = SelectedChannels.Count > 0 ? SelectedChannels.ToArray() : null;
        var loops = SelectedLoops.Count > 0 ? SelectedLoops.ToArray() : null;
        var data = _cache.QueryCalibrationCoefficients(
            fileIds, SelectedModule, SelectedCoefficientName, channels, loops);
        if (data.Count == 0)
        {
            GainSeries = Array.Empty<ISeries>();
            OffsetSeries = Array.Empty<ISeries>();
            CoefficientSummary = "No coefficients match the current Channel / Loop filter.";
            return;
        }

        var gainSeries = new List<ISeries>();
        var offsetSeries = new List<ISeries>();
        var grouped = data.GroupBy(c => (c.FileId, c.LoopIndex))
            .OrderBy(g => g.Key.FileId).ThenBy(g => g.Key.LoopIndex);
        var colorIndex = 0;
        foreach (var group in grouped)
        {
            var color = Palette[colorIndex++ % Palette.Length];
            var title = BuildDiagnosticGroupTitle(group.Key.FileId, group.Key.LoopIndex);
            gainSeries.Add(new ScatterSeries<ObservablePoint>
            {
                Name = title,
                Values = group.Select(c => new ObservablePoint(c.ChannelId, c.Gain)).ToArray(),
                Fill = new SolidColorPaint(color),
                GeometrySize = 8
            });
            offsetSeries.Add(new ScatterSeries<ObservablePoint>
            {
                Name = title,
                Values = group.Select(c => new ObservablePoint(c.ChannelId, c.Offset)).ToArray(),
                Fill = new SolidColorPaint(color),
                GeometrySize = 8
            });
        }

        AddMedianLine(gainSeries, data.Select(c => (double)c.ChannelId), data.Select(c => c.Gain));
        AddMedianLine(offsetSeries, data.Select(c => (double)c.ChannelId), data.Select(c => c.Offset));
        GainSeries = gainSeries.ToArray();
        OffsetSeries = offsetSeries.ToArray();
        GainXAxes = new[] { new Axis { Name = "Channel", MinStep = 1, TextSize = 9 } };
        OffsetXAxes = new[] { new Axis { Name = "Channel", MinStep = 1, TextSize = 9 } };
        GainYAxes = new[] { new Axis { Name = "Gain", TextSize = 10 } };
        OffsetYAxes = new[] { new Axis { Name = "Offset", TextSize = 10 } };

        var gainMedian = Median(data.Select(c => c.Gain));
        var offsetMedian = Median(data.Select(c => c.Offset));
        CoefficientSummary = $"{data.Count} points  |  Gain median: {gainMedian:G7}  |  Offset median: {offsetMedian:G7}";
    }

    private void UpdateResidualChart(long[] fileIds)
    {
        if (SelectedTestItem == null)
        {
            ResidualSeries = Array.Empty<ISeries>();
            ResidualSummary = "Select a test item to inspect residual behavior.";
            return;
        }

        var channels = SelectedChannels.Count > 0 ? SelectedChannels.ToArray() : null;
        var loops = SelectedLoops.Count > 0 ? SelectedLoops.ToArray() : null;
        var data = _cache.QueryTestResults(fileIds, SelectedModule, SelectedTestItem, channels, loops);
        if (data.Count == 0)
        {
            ResidualSeries = Array.Empty<ISeries>();
            ResidualSummary = "No residual data matches the current filters.";
            return;
        }

        var series = new List<ISeries>();
        var groups = data.GroupBy(r => (
                r.FileId,
                r.ChannelId,
                r.LoopIndex,
                Component: r.ModuleType == ModuleType.MIXI ? NormalizeMixiComponent(r.ComponentType) : string.Empty))
            .OrderBy(g => g.Key.FileId).ThenBy(g => g.Key.ChannelId).ThenBy(g => g.Key.LoopIndex).ThenBy(g => g.Key.Component);
        var colorIndex = 0;
        foreach (var group in groups)
        {
            var color = Palette[colorIndex++ % Palette.Length];
            var title = BuildDiagnosticGroupTitle(group.Key.FileId, group.Key.LoopIndex, group.Key.ChannelId, group.Key.Component);
            var points = group.OrderBy(r => r.ExpectValue).ToArray();
            series.Add(new LineSeries<ObservablePoint>
            {
                Name = title,
                Values = points.Select(p => new ObservablePoint(p.ExpectValue, p.Difference)).ToArray(),
                Stroke = new SolidColorPaint(color) { StrokeThickness = 1.5f },
                GeometryStroke = new SolidColorPaint(color) { StrokeThickness = 1.5f },
                GeometryFill = new SolidColorPaint(SKColors.White),
                GeometrySize = 5,
                Fill = null,
                LineSmoothness = 0
            });

            if (TryLinearRegression(points.Select(p => (p.ExpectValue, p.Difference)), out var slope, out var intercept))
            {
                var minX = points.Min(p => p.ExpectValue);
                var maxX = points.Max(p => p.ExpectValue);
                series.Add(new LineSeries<ObservablePoint>
                {
                    Name = null,
                    IsVisibleAtLegend = false,
                    Values = new[]
                    {
                        new ObservablePoint(minX, slope * minX + intercept),
                        new ObservablePoint(maxX, slope * maxX + intercept)
                    },
                    Stroke = new SolidColorPaint(new SKColor(color.Red, color.Green, color.Blue, 120)) { StrokeThickness = 1 },
                    GeometryStroke = null,
                    GeometrySize = 0,
                    Fill = null,
                    LineSmoothness = 0
                });
            }
        }

        var minExpected = data.Min(r => r.ExpectValue);
        var maxExpected = data.Max(r => r.ExpectValue);
        if (Math.Abs(maxExpected - minExpected) < 1e-20)
        {
            minExpected -= 0.5;
            maxExpected += 0.5;
        }
        series.Insert(0, new LineSeries<ObservablePoint>
        {
            Name = null,
            IsVisibleAtLegend = false,
            Values = new[] { new ObservablePoint(minExpected, 0), new ObservablePoint(maxExpected, 0) },
            Stroke = new SolidColorPaint(new SKColor(90, 98, 108, 130)) { StrokeThickness = 1 },
            GeometryStroke = null,
            GeometrySize = 0,
            Fill = null,
            LineSmoothness = 0
        });

        ResidualSeries = series.ToArray();
        ResidualXAxes = new[] { new Axis { Name = "Expected / Target", NameTextSize = 10, TextSize = 9 } };
        ResidualYAxes = new[] { new Axis { Name = "Residual", NameTextSize = 10, TextSize = 9 } };
        var rms = Math.Sqrt(data.Average(r => r.Difference * r.Difference));
        var worst = data.OrderByDescending(r => Math.Abs(r.Difference)).First();
        var worstUtilization = data.Select(CalculateToleranceUtilization).Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty(0).Max();
        ResidualSummary = $"{data.Count} points  |  RMS: {rms:G6}  |  Max |residual|: {Math.Abs(worst.Difference):G6} " +
            $"(Ch{worst.ChannelId})  |  Worst utilization: {worstUtilization:P1}";
    }

    private void UpdateSymmetryChart(long[] fileIds)
    {
        if (SelectedModule != ModuleType.MIXI.ToString() || SelectedTestItem == null)
        {
            SymmetrySeries = Array.Empty<ISeries>();
            SymmetrySummary = "Select MIXI/AWG data to inspect POS/NEG symmetry.";
            return;
        }

        var channels = SelectedChannels.Count > 0 ? SelectedChannels.ToArray() : null;
        var loops = SelectedLoops.Count > 0 ? SelectedLoops.ToArray() : null;
        var data = _cache.QueryTestResults(fileIds, SelectedModule, SelectedTestItem, channels, loops)
            .Where(r => !string.IsNullOrWhiteSpace(r.ComponentType))
            .ToList();
        if (data.Count == 0)
        {
            SymmetrySeries = Array.Empty<ISeries>();
            SymmetrySummary = "No POS / NEG component labels were found. Reload this LOG to refresh its cache.";
            return;
        }

        var componentColors = new Dictionary<string, SKColor>(StringComparer.OrdinalIgnoreCase)
        {
            ["POS"] = SKColor.Parse("#238A8D"),
            ["NEG"] = SKColor.Parse("#E07A3F"),
            ["AVG"] = SKColor.Parse("#66727F"),
            ["DIFF"] = SKColor.Parse("#C63D4F")
        };
        var series = new List<ISeries>();
        var groups = data.GroupBy(r => (
                r.FileId,
                r.ChannelId,
                r.LoopIndex,
                Component: NormalizeMixiComponent(r.ComponentType)))
            .OrderBy(g => g.Key.FileId).ThenBy(g => g.Key.ChannelId).ThenBy(g => g.Key.LoopIndex).ThenBy(g => g.Key.Component);
        foreach (var group in groups)
        {
            var color = componentColors.TryGetValue(group.Key.Component, out var mapped) ? mapped : SKColors.Gray;
            var title = BuildDiagnosticGroupTitle(group.Key.FileId, group.Key.LoopIndex, group.Key.ChannelId, group.Key.Component);
            series.Add(new LineSeries<ObservablePoint>
            {
                Name = title,
                Values = group.OrderBy(r => r.ExpectValue)
                    .Select(r => new ObservablePoint(r.ExpectValue, r.Difference)).ToArray(),
                Stroke = new SolidColorPaint(color) { StrokeThickness = 1.5f },
                GeometryStroke = new SolidColorPaint(color) { StrokeThickness = 1.5f },
                GeometryFill = new SolidColorPaint(SKColors.White),
                GeometrySize = 6,
                Fill = null,
                LineSmoothness = 0
            });
        }

        SymmetrySeries = series.ToArray();
        SymmetryXAxes = new[] { new Axis { Name = "Target", TextSize = 10 } };
        SymmetryYAxes = new[] { new Axis { Name = "Meas - Target", TextSize = 10 } };

        var pairedDifferences = data.GroupBy(r => (
                r.FileId,
                r.ChannelId,
                r.LoopIndex,
                Target: Math.Round(r.ExpectValue, 12)))
            .Select(group =>
            {
                var pos = group.FirstOrDefault(r => NormalizeMixiComponent(r.ComponentType) == "POS");
                var neg = group.FirstOrDefault(r => NormalizeMixiComponent(r.ComponentType) == "NEG");
                return pos != null && neg != null ? Math.Abs(pos.Difference - neg.Difference) : (double?)null;
            })
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();

        SymmetrySummary = pairedDifferences.Length > 0
            ? $"{pairedDifferences.Length} POS/NEG pairs  |  Mean mismatch: {pairedDifferences.Average():G6}  |  Max mismatch: {pairedDifferences.Max():G6}"
            : $"{data.Count} component points  |  No matching POS/NEG target pairs in this item.";
    }

    private string BuildDiagnosticGroupTitle(long fileId, int loopIndex, int? channelId = null, string? component = null)
    {
        var title = LoadedFiles.Count > 1
            ? "[" + Path.GetFileNameWithoutExtension(LoadedFiles.FirstOrDefault(f => f.FileId == fileId)?.FileName ?? fileId.ToString()) + "] "
            : string.Empty;
        if (channelId.HasValue) title += "Ch" + channelId.Value + " / ";
        title += loopIndex == 0 ? "Initial" : "Loop " + loopIndex;
        if (!string.IsNullOrEmpty(component)) title += " / " + component;
        return title;
    }

    private static void AddMedianLine(List<ISeries> series, IEnumerable<double> xValues, IEnumerable<double> yValues)
    {
        var x = xValues.ToArray();
        var y = yValues.ToArray();
        if (x.Length == 0 || y.Length == 0) return;
        var minX = x.Min();
        var maxX = x.Max();
        if (Math.Abs(maxX - minX) < 1e-20)
        {
            minX -= 0.5;
            maxX += 0.5;
        }
        var median = Median(y);
        series.Insert(0, new LineSeries<ObservablePoint>
        {
            Name = "Median",
            IsVisibleAtLegend = false,
            Values = new[] { new ObservablePoint(minX, median), new ObservablePoint(maxX, median) },
            Stroke = new SolidColorPaint(new SKColor(80, 88, 98, 150)) { StrokeThickness = 1 },
            GeometryStroke = null,
            GeometrySize = 0,
            Fill = null,
            LineSmoothness = 0
        });
    }

    private static double? CalculateToleranceUtilization(TestResult result)
    {
        var width = result.UpLimit - result.LowLimit;
        if (!double.IsFinite(width) || Math.Abs(width) < 1e-30) return null;
        var value = result.ModuleType == ModuleType.MIXI ? result.MeasureValue : result.Difference;
        var center = (result.UpLimit + result.LowLimit) / 2d;
        return Math.Abs((value - center) / (width / 2d));
    }

    private static bool TryLinearRegression(IEnumerable<(double X, double Y)> values, out double slope, out double intercept)
    {
        var points = values.ToArray();
        slope = intercept = 0;
        if (points.Length < 2) return false;
        var meanX = points.Average(p => p.X);
        var meanY = points.Average(p => p.Y);
        var denominator = points.Sum(p => (p.X - meanX) * (p.X - meanX));
        if (Math.Abs(denominator) < 1e-30) return false;
        slope = points.Sum(p => (p.X - meanX) * (p.Y - meanY)) / denominator;
        intercept = meanY - slope * meanX;
        return double.IsFinite(slope) && double.IsFinite(intercept);
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        if (ordered.Length == 0) return 0;
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2d
            : ordered[middle];
    }

    private static string NormalizeMixiComponent(string component)
    {
        var normalized = component.Trim().ToUpperInvariant().Replace(" ", string.Empty);
        if (normalized.Contains("POS+NEG", StringComparison.Ordinal)) return "AVG";
        if (normalized.Contains("DIFF", StringComparison.Ordinal)) return "DIFF";
        if (normalized.Contains("POS", StringComparison.Ordinal)) return "POS";
        if (normalized.Contains("NEG", StringComparison.Ordinal)) return "NEG";
        return string.IsNullOrEmpty(normalized) ? "OTHER" : normalized;
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
            _seriesFocusKeys[lineSeries] = title;
            LegendItems.Add(new LegendItem { Name = title, FocusKey = title, ColorBrush = new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue)) });

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
            _seriesFocusKeys[lineSeries] = title;
            LegendItems.Add(new LegendItem { Name = title, FocusKey = title, ColorBrush = new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue)) });

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
                _seriesFocusKeys[failSeries] = title;
                LegendItems.Add(new LegendItem { Name = title + " (Failed)", FocusKey = title, ColorBrush = Brushes.Red });
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
        var hasMultipleChannels = data.Select(r => r.ChannelId).Distinct().Take(2).Count() > 1;
        var hasMultipleLoops = data.Select(r => r.LoopIndex).Distinct().Take(2).Count() > 1;
        var groups = CompareMultiChannel
            ? data.GroupBy(r =>
                (multiFile && fileNameMap.ContainsKey(r.FileId) ? "[" + fileNameMap[r.FileId] + "] " : "") +
                "Ch" + r.ChannelId +
                (hasMultipleLoops ? " / " + (r.LoopIndex == 0 ? "Initial" : "Loop " + r.LoopIndex) : ""))
            : data.GroupBy(r =>
                (multiFile && fileNameMap.ContainsKey(r.FileId) ? "[" + fileNameMap[r.FileId] + "] " : "") +
                (hasMultipleChannels ? "Ch" + r.ChannelId + " / " : "") +
                (r.LoopIndex == 0 ? "Initial" : "Loop " + r.LoopIndex));
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
        AvailableCoefficientNames.Clear();
        SelectedChannels.Clear(); SelectedLoops.Clear();
        ErrorEntries.Clear(); DeviceEntries.Clear(); SystemInfos.Clear();
        Statistics.Clear(); PassFailEntries.Clear(); LegendItems.Clear(); CurrentChartData.Clear(); ChartDataRows.Clear(); SelectedChartRow = null;
        SelectedModule = null; SelectedTestItem = null;
        SelectedCoefficientName = null;
        InitializeChart();
        InitializeDiagnostics();
        ClearDiagnostics();
        StatusText = "Ready";
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

public partial class LegendItem : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string? FocusKey { get; set; }
    public SolidColorBrush ColorBrush { get; set; } = Brushes.Gray;
    [ObservableProperty] private double _displayOpacity = 1;
    [ObservableProperty] private FontWeight _fontWeight = FontWeights.Normal;
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
