using System.IO;
using System.Reflection;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using SSTLogAnalyser.Services;
using SSTLogAnalyser.ViewModels;

static class UiImportCheck
{
    public static int Run(string fixtures, string? batchLog = null)
    {
        Exception? failure = null;
        int checks = 0;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                var directory = Path.Combine(Path.GetTempPath(), "SSTLogAnalyser-ui-check-" + Guid.NewGuid().ToString("N"));
                try
                {
                    using var cache = new CacheService(directory);
                    var model = (MainViewModel)Activator.CreateInstance(typeof(MainViewModel), BindingFlags.Instance | BindingFlags.NonPublic,
                        null, new object[] { cache, new LogParser() }, null)!;
                    void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException("Import chain: " + message); checks++; }
                    await model.LoadFilesFromPathsAsync(new[] {
                        Path.Combine(fixtures, "09_SST_Summary_Fail.txt"), Path.Combine(fixtures, "11_AUTOTEST_Result.txt"),
                        Path.Combine(fixtures, "05_CAL_Only_LogAll.log"), Path.Combine(fixtures, "06_MIXI_Attribution_LogAll.log"),
                        Path.Combine(fixtures, "15_Robustness_LogAll.log") });
                    Check(model.RunSummaries.Count == 2, "renamed summary files loaded into Run Summary");
                    Check(model.LoadedFiles.Count == 5, "all imported files registered");
                    Check(model.AvailableModules.Contains("PPMU"), "pure CAL module in filters");
                    model.SelectedModule = "MIXI";
                    model.SelectedTestItem = "AWG AWG_VER_HR_1K5_WAVE_DAC_OFFSET_DAC_POS";
                    model.SetDiagnosticsActive(true);
                    model.SetSelectedChannels(new[] { 1 });
                    Check(model.WaveOffsetDetailMode && model.WaveOffsetSummary.Contains("WAVE CAL"), "Detail rendered for known WAVE bias");
                    model.SetSelectedChannels(new[] { 1, 2, 3 });
                    Check(model.WaveOffsetChannelMapMode && model.WaveOffsetSummary.Contains("Channel Map: 3 channels"), "Channel Map import and filters");
                    await model.LoadFilesFromPathsAsync(new[] { Path.Combine(fixtures, "06_MIXI_Attribution_LogAll.log") });
                    Check(model.LoadedFiles.Count == 5, "cached file not duplicated");
                    await model.LoadFilesFromPathsAsync(new[] { Path.Combine(fixtures, "21_DPS_Sweep_LogAll.log") });
                    model.SetSelectedChannels(new[] { 0 });
                    model.SelectedModule = "DPS";
                    model.SelectedTestItem = model.AvailableTestItems.Single(p => p.Contains("Force Voltage"));
                    model.SetSelectedLoops(new[] { 1 });
                    foreach (var name in new[] { "High Limit", "Low Limit" })
                    {
                        var series = model.Series.OfType<LiveChartsCore.SkiaSharpView.LineSeries<LiveChartsCore.Defaults.ObservablePoint>>().Single(p => p.Name == name);
                        var values = series.Values!.ToArray();
                        Check(values.Length == 12 && values.Select(p => p.X).Distinct().Count() == 12, name + " has a drawable -3..8 sweep");
                        Check(values.All(p => Math.Abs(p.Y!.Value) >= 0.001), name + " carries nonzero error bounds");
                    }
                    var sweepFile = model.LoadedFiles.Single(p => p.FileName == "21_DPS_Sweep_LogAll.log");
                    model.BaselineFile = sweepFile;
                    model.CurrentComparisonFile = sweepFile;
                    Check(model.BaselineLoop == 2 && model.CurrentComparisonLoop == 2, "latest Loop selected independently");
                    model.BaselineLoop = 1;
                    model.CurrentComparisonLoop = 2;
                    await model.CompareBaselineCommand.ExecuteAsync(null);
                    Check(model.ComparisonSummary.Contains("Matched 24") && model.ComparisonSummary.Contains("New failures 2"), "comparison reads selected cached Loops");
                    Check(model.ComparisonRows.Count == 2 && model.ComparisonRows.All(p => p.Status == "New failure"), "attention view defaults to anomalies");
                    Check(model.ComparisonSeries.Length == 6, "selection builds two error and four limit curves");
                    model.ComparisonFilter = "All";
                    Check(model.ComparisonRows.Count == 24, "all comparison rows available");
                    model.ComparisonSearch = "Ch0";
                    Check(model.ComparisonRows.Count == 12, "comparison independent channel search");
                    model.ComparisonSearch = string.Empty;
                    model.CurrentComparisonLoop = 1;
                    Check(model.ComparisonRows.Count == 0 && model.ComparisonSeries.Length == 0, "selection change clears stale results");
                    await model.CompareBaselineCommand.ExecuteAsync(null);
                    Check(model.ComparisonRows.Count == 24 && model.ComparisonRows.All(p => p.Status == "Within limits"), "same Loop self comparison stable");
                    await model.LoadFilesFromPathsAsync(new[] { Path.Combine(fixtures, "22_Comparison_Baseline.log"), Path.Combine(fixtures, "23_Comparison_Current.log") });
                    model.BaselineFile = model.LoadedFiles.Single(p => p.FileName == "22_Comparison_Baseline.log");
                    model.CurrentComparisonFile = model.LoadedFiles.Single(p => p.FileName == "23_Comparison_Current.log");
                    model.ComparisonFilter = "New failure";
                    await model.CompareBaselineCommand.ExecuteAsync(null);
                    Check(model.ComparisonRows.Count == 2 && model.ComparisonRows.All(p => p.Status == "New failure"), "two separate files compare with own Loops");
                    Check(model.ComparisonChartSummary.Contains("22_Comparison_Baseline.log") && model.ComparisonChartSummary.Contains("23_Comparison_Current.log"), "both file names and source lines displayed");
                    await model.LoadFilesFromPathsAsync(new[] { Path.Combine(fixtures, "24_MXVI_Measure_Force_Transition.log") });
                    model.SelectedModule = "MXVI";
                    model.SetSelectedChannels(new[] { 200 });
                    model.SetSelectedLoops(new[] { 1 });
                    model.SelectedTestItem = "V, -25, 25, Force Voltage";
                    Check(model.ChartTitle == "MXVI / V, -25, 25, Force Voltage", "chart title follows selected module and full Item");
                    var forceSeries = model.Series.OfType<LiveChartsCore.SkiaSharpView.LineSeries<LiveChartsCore.Defaults.ObservablePoint>>()
                        .Where(p => p.Name!.Contains("Ch200")).ToArray();
                    Check(forceSeries.Length == 2 && forceSeries.All(p => p.Values!.Count() == 1), "repeated Force Voltage tables draw separate curves");
                    Check(forceSeries.Select(p => p.Name).Distinct().Count() == 2, "test sequences have distinct legend and tooltip names");
                    model.SetDiagnosticsActive(true);
                    Check(model.ResidualSeries.OfType<LiveChartsCore.SkiaSharpView.LineSeries<LiveChartsCore.Defaults.ObservablePoint>>()
                        .Count(p => p.Name?.Contains("Ch200") == true) == 2, "residual chart separates repeated Force Voltage tables");
                    model.SelectedTestItem = "V, -25, 25, Measure Voltage";
                    Check(model.ChartTitle == "MXVI / V, -25, 25, Measure Voltage", "chart title updates when Item changes");
                    Check(model.ChartDataRows.Count == 1 && model.ChartDataRows[0].AdcMeasureValue.HasValue, "switching back to Measure Voltage preserves ADC-only data");
                    model.ClearAllCommand.Execute(null);
                    Check(model.ChartTitle.Length == 0 && model.ChartContext.Length == 0, "clearing data clears chart heading");
                    await model.LoadFilesFromPathsAsync(new[] { Path.Combine(fixtures, "25_MXVI_Repeated_Sweeps.log") });
                    model.SelectedModule = "MXVI";
                    model.SetSelectedChannels(new[] { 200, 201, 202, 203 });
                    model.SetSelectedLoops(new[] { 1 });
                    foreach (var item in new[] { "Force Voltage", "Measure Voltage" })
                    {
                        model.SelectedTestItem = "V, -25, 25, " + item;
                        var curves = model.Series.OfType<LiveChartsCore.SkiaSharpView.LineSeries<LiveChartsCore.Defaults.ObservablePoint>>()
                            .Where(p => p.Name?.Contains("Ch") == true).ToArray();
                        Check(curves.Length == (item == "Force Voltage" ? 8 : 4) && curves.All(p => p.Values!.Count() == 11 && p.Values!.Select(v => v.X).Distinct().Count() == 11),
                            "real sweep chart: " + item + " has distinct complete curves without interleaved X");
                    }
                    model.BaselineFile = model.LoadedFiles.Single();
                    model.CurrentComparisonFile = model.LoadedFiles.Single();
                    model.ComparisonFilter = "All";
                    await model.CompareBaselineCommand.ExecuteAsync(null);
                    model.SelectedComparisonRow = model.ComparisonRows.First(p => p.TestItem.EndsWith("Force Voltage"));
                    Check(model.SelectedComparisonRow.Status == "Duplicate / ambiguous", "repeated runs are not silently paired across files");
                    Check(model.ComparisonSeries.Length == 12 && model.ComparisonSeries
                        .OfType<LiveChartsCore.SkiaSharpView.LineSeries<LiveChartsCore.Defaults.ObservablePoint>>()
                        .All(p => p.Values!.Count() == 11 && p.Values!.Select(v => v.X).Distinct().Count() == 11), "baseline comparison also separates repeated sweeps");
                    model.ClearAllCommand.Execute(null);
                    await model.LoadFilesFromPathsAsync(new[] { Path.Combine(fixtures, "26_MXVI_VI_AWG.log") });
                    model.SelectedModule = "MXVI";
                    model.SetSelectedChannels(new[] { 200 });
                    model.SetSelectedLoops(new[] { 1 });
                    foreach (var item in new[] { "V, -25, 25, Force Voltage [VI]", "V, -25, 25, Force Voltage [AWG]", "V, -25, 25, Measure Voltage", "I, 300mA, Force Current [VI]", "I, 300mA, Measure Current" })
                    {
                        model.SelectedTestItem = item;
                        Check(model.ChartTitle == "MXVI / " + item, "chart title preserves VI/AWG mode: " + item);
                        var curves = model.Series.OfType<LiveChartsCore.SkiaSharpView.LineSeries<LiveChartsCore.Defaults.ObservablePoint>>()
                            .Where(p => p.Name?.Contains("Ch200") == true).ToArray();
                        var count = item.StartsWith("V,") ? 11 : 13;
                        Check(curves.Length == 1 && curves[0].Values!.Count() == count && model.ChartDataRows.All(p => p.TestItemName == item), "VI/AWG chart isolation: " + item);
                    }
                    model.ClearAllCommand.Execute(null);
                    foreach (var reload in new[] { false, true })
                    {
                        await model.LoadFilesFromPathsAsync(new[] { Path.Combine(fixtures, "28_MVPI_FIMI_Real.log") });
                        if (reload) Check(model.StatusText.StartsWith("Loaded from cache:"), "MVPI corrected sections survive cached reimport");
                        model.SelectedModule = "MVPI";
                        model.SetSelectedChannels(new[] { 800 });
                        model.SetSelectedLoops(new[] { 1 });
                        foreach (var (item, count, adc) in new[] {
                            ("I, 10uA, Measure Current", 9, true),
                            ("I, 10uA, V, 42, Force Current", 5, false),
                            ("I, 10uA, V, 42, Measure Current", 5, true) })
                        {
                            model.SelectedTestItem = item;
                            Check(model.ChartDataRows.Count == count && model.ChartDataRows.All(p => p.AdcMeasureValue.HasValue == adc), "MVPI chart isolates " + item);
                            Check(model.Series.OfType<LiveChartsCore.SkiaSharpView.LineSeries<LiveChartsCore.Defaults.ObservablePoint>>()
                                .Count(p => p.Name?.StartsWith("Ch800") == true) == 1, "MVPI chart has no phantom Run 2: " + item);
                        }
                        model.ClearAllCommand.Execute(null);
                    }
                    foreach (var reload in new[] { false, true })
                    {
                        await model.LoadFilesFromPathsAsync(new[] { Path.Combine(fixtures, "30_HVPI_Real.log") });
                        if (reload) Check(model.StatusText.StartsWith("Loaded from cache:"), "HVPI MICM survives cached reimport");
                        model.SelectedModule = "HVPI";
                        model.SetSelectedChannels(new[] { 800 });
                        model.SetSelectedLoops(new[] { 1 });
                        var cmItems = model.AvailableTestItems.Where(p => p.StartsWith("MICM,")).ToArray();
                        Check(cmItems.Length == 8, "HVPI exposes eight voltage/current-range MICM Items");
                        foreach (var item in cmItems)
                        {
                            model.SelectedTestItem = item;
                            Check(model.ChartDataRows.Count == 9 && model.ChartDataRows.All(p => p.TestItemName == item) &&
                                model.ChartTitle == "HVPI / " + item, "MICM chart/title isolates " + item);
                            var curves = model.Series.OfType<LiveChartsCore.SkiaSharpView.LineSeries<LiveChartsCore.Defaults.ObservablePoint>>()
                                .Where(p => p.Name?.StartsWith("Ch800") == true).ToArray();
                            Check(curves.Length == 1 && curves[0].Values!.Count() == 9, "MICM draws one nine-point curve per range");
                        }
                        var fileId = model.LoadedFiles.Single().FileId;
                        var hvpiRows = cache.QueryTestResults(new[] { fileId }, "HVPI", null, null, null);
                        Check(hvpiRows.Count == 256 && hvpiRows.Count(p => p.IsFailed) == 13 && hvpiRows.Count(p => p.HasExplicitResult) == 13,
                            "HVPI cache retains all MICM points and explicit failures");
                        model.ClearAllCommand.Execute(null);
                    }
                    var batchPath = batchLog ?? Path.Combine(fixtures, "27_MXVI_Run_Batches.log");
                    var source = await new LogParser().ParseAsync(batchPath);
                    Check(source.RunBatches.Count == 2, "two independently started verification batches");
                    foreach (var reload in new[] { false, true })
                    {
                        model.ClearAllCommand.Execute(null);
                        await model.LoadFilesFromPathsAsync(new[] { batchPath });
                        Check(model.AvailableBatches.SequenceEqual(new[] { 1, 2 }) && model.SelectedBatch == 2, "latest batch selected on import/cache reload");
                        if (reload) Check(model.StatusText.StartsWith("Loaded from cache:"), "batch boundaries survive cached reimport");
                        model.SelectedModule = "MXVI";
                        model.SelectedTestItem = "V, -25, 25, Measure Voltage";
                        model.SetSelectedChannels(new[] { 200, 201, 202, 203 });
                        foreach (var batch in new[] { 1, 2, 1 })
                        {
                            model.SelectedBatch = batch;
                            var range = source.RunBatches[batch - 1];
                            var expected = source.TestResults.Where(p => p.LineNumber >= range.StartLine && p.LineNumber <= range.EndLine).ToArray();
                            Check(model.AvailableLoops.SequenceEqual(expected.Select(p => p.LoopIndex).Distinct().Order()), "batch filter preserves original Loop numbers");
                            model.SetSelectedLoops(new[] { 1 });
                            Check(model.ChartDataRows.Count == 44 && model.ChartDataRows.All(p => p.LineNumber >= range.StartLine && p.LineNumber <= range.EndLine), "Loop 1 contains only the selected batch's 44 Measure Voltage points");
                            Check(model.ChartContext.Contains($"Batch {batch} | CH 200, 201, 202, 203 | Loop 1 | 44 points") &&
                                model.ChartContext.StartsWith(Path.GetFileName(batchPath)), "chart context follows actual file, batch, channels and Loop");
                            var fileId = model.LoadedFiles.Single().FileId;
                            Check(cache.QueryTestResults(new[] { fileId }, null, null, null, null).Count == expected.Length, "batch cache query isolates all test items");
                            model.BaselineFile = model.LoadedFiles.Single();
                            model.CurrentComparisonFile = model.LoadedFiles.Single();
                            model.BaselineLoop = 1;
                            model.CurrentComparisonLoop = 1;
                            model.ComparisonFilter = "All";
                            await model.CompareBaselineCommand.ExecuteAsync(null);
                            Check(model.ComparisonRows.Count == 348 && model.ComparisonRows.All(p => p.Status != "Duplicate / ambiguous"), "baseline comparison does not merge batches at Loop 1");
                        }
                    }
                }
                catch (Exception ex) { failure = ex; }
                finally
                {
                    SqliteConnection.ClearAllPools();
                    if (Directory.Exists(directory)) Directory.Delete(directory, true);
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure != null) throw new InvalidOperationException("UI import regression failed", failure);
        Console.WriteLine($"Import ViewModel: {checks} checks passed (no window opened).");
        return checks;
    }
}
