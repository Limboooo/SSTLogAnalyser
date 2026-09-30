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
