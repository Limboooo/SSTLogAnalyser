using System.IO;
using System.Reflection;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using SSTLogAnalyser.Services;
using SSTLogAnalyser.ViewModels;

static class UiImportCheck
{
    public static int Run(string fixtures)
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
                        Path.Combine(fixtures, "05_CAL_Only_LogAll.log"), Path.Combine(directory, "missing.log"),
                        Path.Combine(fixtures, "06_MIXI_Attribution_LogAll.log"), Path.Combine(fixtures, "15_Robustness_LogAll.log") });
                    Check(model.RunSummaries.Count == 2, "renamed summary files loaded into Run Summary");
                    Check(model.LoadedFiles.Count == 5, "files after an unreadable one still imported");
                    Check(model.StatusText.Contains("1 of 6 file(s) failed") && model.StatusText.Contains("missing.log"), "failed file reported by name");
                    Check(!model.IsLoading && model.ClearAllCommand.CanExecute(null), "commands re-enabled after import");
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
                    var sweepImport = model.LoadFilesFromPathsAsync(new[] { Path.Combine(fixtures, "21_DPS_Sweep_LogAll.log") });
                    Check(model.IsLoading && !model.ClearAllCommand.CanExecute(null) && !model.LoadFilesCommand.CanExecute(null),
                        "file commands disabled during import");
                    await model.LoadFilesFromPathsAsync(new[] { Path.Combine(fixtures, "22_Comparison_Baseline.log") });
                    Check(model.StatusText.StartsWith("Still loading"), "overlapping drop rejected while importing");
                    await sweepImport;
                    Check(model.LoadedFiles.Count == 6 && !model.LoadedFiles.Any(p => p.FileName == "22_Comparison_Baseline.log"),
                        "rejected drop did not import concurrently");
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

                    // Fast rendering used to drop the failed-point markers entirely.
                    model.SelectedModule = "DPS";
                    model.SelectedTestItem = model.AvailableTestItems.Single(p => p.Contains("-3, 8, Force Voltage"));
                    model.SetSelectedChannels(new[] { 0, 1 });
                    model.SetSelectedLoops(Array.Empty<int>());
                    model.PerformanceMode = true;
                    var fastFailures = model.Series.OfType<LiveChartsCore.SkiaSharpView.ScatterSeries<LiveChartsCore.Defaults.ObservablePoint>>()
                        .Sum(p => p.Values!.Count());
                    Check(fastFailures == model.ChartDataRows.Count(p => p.IsFailed) && fastFailures > 0, "failed points stay visible in fast mode");
                    model.PerformanceMode = false;

                    // More than 12 curves: envelope + worst curves; table and hover still see every raw point.
                    var wide = Path.Combine(directory, "wide_sweep.log");
                    var lines = new List<string> { "2026-10-08 10:00:00,000 INFO  - Index 1 of 1" };
                    for (var channel = 0; channel < 20; channel++)
                    {
                        lines.Add($"2026-10-08 10:00:00,000 INFO  - DPS Channel {channel}: V, -5, 5, Force Voltage Verification");
                        lines.Add("2026-10-08 10:00:00,000 INFO  - ,    Expect,          DmmMeasure,  LowLimit,   UpLimit,               Difference,");
                        for (var x = -5; x <= 6; x++)
                        {
                            var difference = channel * 1e-5 + x * 1e-5;
                            if (channel == 17 && x == 2) difference = 0.009;
                            if (channel == 4 && (x == -1 || x == 3)) difference = -0.003;
                            lines.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                                $"2026-10-08 10:00:00,000 INFO  - ,{x},{x + difference},-0.002,0.002,{difference},"));
                        }
                    }
                    File.WriteAllLines(wide, lines);
                    await model.LoadFilesFromPathsAsync(new[] { wide });
                    model.SelectedModule = "DPS";
                    model.SelectedTestItem = "V, -5, 5, Force Voltage";
                    model.SetSelectedChannels(Array.Empty<int>());
                    var lineSeries = model.Series.OfType<LiveChartsCore.SkiaSharpView.LineSeries<LiveChartsCore.Defaults.ObservablePoint>>().ToArray();
                    Check(model.ChartRenderSummary.StartsWith("20 curves") && model.ChartRenderSummary.Contains("Envelope + worst 8") &&
                        model.ChartRenderSummary.Contains("all 3 failed points shown"), "envelope mode above 12 curves");
                    Check(lineSeries.Count(p => p.ZIndex < 0 && p.Fill != null) == 4 && lineSeries.Any(p => p.Name == "Median"), "two-layer band and median drawn");
                    Check(model.LegendItems.Count == 14 && model.LegendItems[3].Name.EndsWith("Ch17 · mean utilization 45%") &&
                        model.LegendItems[4].Name.Contains("Ch4 · mean utilization"), "legend ranks curves by mean utilization");
                    var failedSeries = model.Series.OfType<LiveChartsCore.SkiaSharpView.ScatterSeries<LiveChartsCore.Defaults.ObservablePoint>>().Single();
                    Check(failedSeries.Values!.Count() == 3, "every failed point drawn in envelope mode");
                    Check(model.ChartDataRows.Count == 240 && model.CurrentChartData.Count(p => !p.IsLimit) == 240, "table and hover keep all raw points");
                    var hovered = model.FindNearestChartPoint(2, 0.009, 1, 1, out _);
                    Check(hovered?.SeriesName.EndsWith("Ch17") == true && hovered.IsFailed && hovered.RowIndex >= 0, "hover names the channel under the band and maps to its table row");
                    model.ToggleSeriesFocusCommand.Execute(model.LegendItems[3]);
                    Check(lineSeries.Count(p => p.IsVisible && p.ZIndex >= 0 && p.Name != "Median" && !p.Name!.EndsWith("Limit")) == 1 &&
                        lineSeries.Where(p => p.ZIndex < 0).All(p => p.IsVisible), "focusing a worst curve keeps the band visible");

                    // Manual override: the original one-line-per-curve view and a forced envelope.
                    model.CurveModeAllCurves = true;
                    var curveSeries = model.Series.OfType<LiveChartsCore.SkiaSharpView.LineSeries<LiveChartsCore.Defaults.ObservablePoint>>().ToArray();
                    Check(model.CurveMode == ChartCurveMode.AllCurves && !model.CurveModeAuto &&
                        curveSeries.Count(p => p.Name!.EndsWith("Limit") == false) == 20 && curveSeries.All(p => p.ZIndex >= 0) &&
                        model.ChartRenderSummary.StartsWith("Showing all 20 curves (envelope off)"), "All curves mode draws every curve");
                    Check(model.Series.OfType<LiveChartsCore.SkiaSharpView.ScatterSeries<LiveChartsCore.Defaults.ObservablePoint>>().Sum(p => p.Values!.Count()) == 3 &&
                        model.LegendItems.Count == 20 + 2 + 2, "All curves mode keeps failed points and per-curve legend");
                    model.SetSelectedChannels(new[] { 4, 17 });
                    model.CurveModeEnvelope = true;
                    Check(model.ChartRenderSummary.StartsWith("2 curves") && model.Series.Any(p => p.Name == "Median"), "Envelope mode can be forced for few curves");
                    model.CurveModeAuto = true;
                    Check(model.CurveMode == ChartCurveMode.Auto && !model.Series.Any(p => p.Name == "Median"), "Auto returns to one line per curve at or below 12");
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
