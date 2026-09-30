using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using SSTLogAnalyser.Models;
using SSTLogAnalyser.Services;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var fixtureDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../fixtures"));
if (args.Length == 2 && args[0] == "--profile")
{
    await ProfileImport.Run(args[1]);
    return;
}
if (args.Contains("--audit"))
{
    AuditTrunkLogs.Write(Path.GetFullPath(Path.Combine(fixtureDirectory, "../audit")));
    return;
}
if (args.Contains("--generate")) GenerateFixtures.Write(fixtureDirectory);
int checks = 0;
void Check(bool ok, string description)
{
    if (!ok) throw new InvalidOperationException("FAIL: " + description);
    checks++;
}
void Near(double actual, double expected, string description) =>
    Check(Math.Abs(actual - expected) < 1e-9, $"{description}: expected {expected:G10}, got {actual:G10}");
var parser = new LogParser();
var parsed = new Dictionary<string, ParseResult>();
foreach (var path in Directory.GetFiles(fixtureDirectory, "*.log").Order())
{
    var result = await parser.ParseAsync(path);
    parsed[Path.GetFileName(path)[..2]] = result;
    Check(result.TestResults.All(row => row.LineNumber > 0 && row.Timestamp == "2026-09-30 10:00:00,000"),
        Path.GetFileName(path) + " source locations and timestamps");
    Console.WriteLine($"{Path.GetFileName(path)}: points={result.TestResults.Count}, failed={result.TestResults.Count(p => p.IsFailed)}, coefficients={result.CalibrationCoefficients.Count}, events={result.DiagnosticObservations.Count}");
}
Check(parsed.Count >= 12, "expanded LogAll fixtures loaded");
var dc = parsed["01"];
Check(dc.TestResults.Count == 22 && dc.TestResults.Count(p => p.IsFailed) == 10, "DC expected 22 points / 10 failures");
Check(dc.TestResults.Select(p => p.ModuleType).Distinct().Count() == 10, "DC module recognition");
Check(dc.TestResults.Count(p => p.HasExplicitResult) == 10, "DC explicit Failed suffixes");
Check(dc.Errors.Count == 10, "error-level data rows retained even when consumed by a point parser");
Check(dc.FileInfo.LoopCount == 2, "DC loop count");
Check(dc.TestResults.Count(p => p.IsReTest && p.LoopIndex == 2) == 1, "DC retest belongs to loop 2");
Near(dc.TestResults.Single(p => p.ChannelId == 201).AdcMeasureValue!.Value, 1.0003, "MVPI ADC column");
var ac = parsed["02"];
var sweep = parsed["21"].TestResults;
Check(sweep.Count == 48 && sweep.Count(p => p.IsFailed) == 2, "DPS sweep 48 points / 2 injected failures");
foreach (var group in sweep.GroupBy(p => (p.ChannelId, p.LoopIndex)))
{
    Check(group.Select(p => p.ExpectValue).Distinct().Count() == 12, "each sweep spans 12 distinct X positions");
    foreach (var point in group)
    {
        Near(point.UpLimit, 0.001 + Math.Abs(point.ExpectValue) * 0.0005, "sweep high error limit");
        Near(point.LowLimit, -point.UpLimit, "sweep low error limit");
    }
}
Check(ac.TestResults.Count == 14 && ac.TestResults.Count(p => p.IsFailed) == 2, "AC 12 edges + 2 whole-pin verdicts");
Check(!ac.TestResults.Any(p => p.ChannelId == 2), "AC NA is not a numeric point or Pass");
Check(ac.TestResults.Count(p => p.HasExplicitResult) == 2, "AC explicit verdict belongs only to whole pin");
Check(ac.TestResults.Count(p => p.ChannelId == 1 && p.IsFailed && !p.HasExplicitResult) == 1,
    "AC failing whole pin does not mark every edge as failed");
var tmu = parsed["03"];
Check(tmu.TestResults.Count == 17 && tmu.TestResults.Count(p => p.IsFailed) == 2, "TMU 16 paths + 1 whole-pin failure");
Check(tmu.TestResults.Count(p => p.HasExplicitResult) == 1, "TMU blank verdict never invents explicit Pass");
Near(tmu.TestResults.First().Difference, 0.01, "TMU reference subtraction");
var events = parsed["04"].DiagnosticObservations;
Check(events.Count == 8, "eight diagnostic / leakage / temperature / Flash observations");
Check(events.Select(p => p.Status).SequenceEqual(new[] { "Pass", "Fail", "Fail", "Unknown", "Unknown", "Unknown", "Fail", "Fail" }),
    "explicit status vs unknown leakage");
Near(events[2].LowLimit!.Value, -1e-8, "leakage lower bound");
Check(events[3].Slot == null && events[4].Slot == null, "missing Slot is not invented");
Check(events[5].Slot == 2 && events[5].Channel == 256 && events[5].Value == 35, "thermal location and value");
Check(events[7].Kind == "Flash" && events[7].Channel == 200, "Flash mismatch classified");
Check(parsed["05"].TestResults.Count == 0 && parsed["05"].CalibrationCoefficients.Count == 2,
    "CAL-only fixture has coefficients without test points");
var mixi = parsed["06"];
Check(mixi.TestResults.Count == 80, "MIXI five channels, two loops, eight points each");
foreach (var group in mixi.TestResults.GroupBy(p => (p.ChannelId, p.LoopIndex)))
{
    Check(MixiWaveOffsetAnalyzer.TryAnalyze(group, out var fit), "MIXI independent model: " + group.Key);
    var (channel, loop) = group.Key;
    Near(fit.WaveSlope, channel == 1 ? 0.002 * loop : 0, "known injected WAVE slope");
    Near(fit.OffsetSlope, channel == 2 ? 0.008 * loop : 0, "known injected OFFSET slope");
    Near(fit.Bias, channel == 3 ? 0.0015 * loop : 0, "known injected common bias");
    var cause = channel switch { 0 => "No dominant CAL bias", 1 => "WAVE CAL", 2 => "OFFSET CAL", 3 => "Common bias", _ => "Mixed / low confidence" };
    Check(fit.LikelyCause == cause, $"MIXI Ch{channel} Loop{loop}: {cause}");
    Check(fit.Confidence == (channel == 4 ? "Low" : "High"), "MIXI confidence");
    Near(fit.Rmse, channel == 4 ? 0.002 : 0, "known fit residual");
}
foreach (var group in parsed["07"].TestResults.GroupBy(p => p.ChannelId))
    Check(!MixiWaveOffsetAnalyzer.TryAnalyze(group, out _), "reject insufficient / collinear / inconsistent model Ch" + group.Key);
Check(parsed["12"].DiagnosticObservations.Count == 21, "AC final summary 18 metrics + 2 align verdicts + 1 calibration value");
Check(parsed["12"].DiagnosticObservations.Count(p => p.Status == "Fail") == 1, "only explicit AC alignment failure");
Check(parsed["13"].DiagnosticObservations.Count == 26, "TMU path 18 cells + clock / EC / FMU events");
Check(parsed["13"].DiagnosticObservations.Count(p => p.Item.Contains("VolStart_VolStop(ns)")) == 4, "repeated TMU column labels retained");
Check(parsed["13"].DiagnosticObservations.Single(p => p.Item == "EC" && p.Channel == 2).HighLimit == 50, "TMU explicit EC bounds");
Check(parsed["14"].DiagnosticObservations.Count == 4, "register and Flash verdicts");
Check(parsed["15"].TestResults.Count == 0, "malformed / NaN / infinite measurements never become zero-valued Pass points");
Check(parsed["16"].DiagnosticObservations.Count == 6, "diagnosis variants");
Check(parsed["16"].DiagnosticObservations[2].Unit == "V" && parsed["16"].DiagnosticObservations[2].Value == 1.2, "line resistance precheck voltage is not labelled ohm");
Check(parsed["18"].DiagnosticObservations.Count == 2 && parsed["18"].DiagnosticObservations[0].Value == 0.0002, "Sigma summary extracted without inventing Pass/Fail");
Check(parsed["19"].DiagnosticObservations.Count == 7, "FFT explicit overall and six metrics");
Check(parsed["19"].DiagnosticObservations[0].Slot == 2 && parsed["19"].DiagnosticObservations[0].Channel == 200 && parsed["19"].DiagnosticObservations[0].Status == "Fail", "actual FFT block verdict and metadata");
Check(parsed["19"].DiagnosticObservations.Single(p => p.Item == "FFT Waveform Mean").Value == null, "FFT NaN never represented as zero");
Near(parsed["19"].DiagnosticObservations.Single(p => p.Item == "FFT Waveform Max").HighLimit!.Value, 1.02, "FFT voltage tolerance extracted");
Near(parsed["19"].DiagnosticObservations.Single(p => p.Item == "FFT THD").HighLimit!.Value, -60, "FFT THD limit extracted");
Check(parsed["20"].DiagnosticObservations.Count == 4, "MXVI Short and resistor formats");
var shortEvents = parsed["20"].DiagnosticObservations;
Check(shortEvents[0].Unit == "V" && shortEvents[1].Unit == "A", "MXVI FI checks voltage / FV checks current");
Near(shortEvents[0].LowLimit!.Value, 0.99, "MXVI padded bracket lower bound");
Near(shortEvents[1].HighLimit!.Value, -0.009, "MXVI padded bracket upper bound");
Check(shortEvents[2].Status == "Pass" && shortEvents[3].Status == "Fail" && shortEvents[2].Unit == "ohm", "Resule misspelling and resistor result");

foreach (var (prefix, status) in new[] { ("08", SummaryStatus.Passed), ("09", SummaryStatus.Failed), ("10", SummaryStatus.Aborted) })
{
    var path = Directory.GetFiles(fixtureDirectory, prefix + "*.txt").Single();
    var summary = RunSummaryParser.ParseFile(path);
    Check(RunSummaryParser.IsSummaryFile(path), "renamed Summary content recognized");
    Check(summary.OverallStatus == status && summary.TesterFamily == "AStar", "Summary overall " + status);
    Check(summary.Items.Count == 4 && summary.Items.Last().Status == status, "Summary stage verdicts");
    Check(summary.Items[1].Status == SummaryStatus.NotTested && summary.Items[2].Status == SummaryStatus.NotApplicable,
        "Summary Not Test / Not Applied");
    Check(summary.Boards.Single().SerialNumber == "SIM-DIGI-002", "Summary board serial number");
    Check(summary.Items.SelectMany(i => i.FailedSlots).Count() == (status == SummaryStatus.Failed ? 1 : 0), "Summary failed Slot attachment");
    Check(summary.Metadata["Tester S/N"].Value == "SIMULATED-TESTER-ONLY", "Summary metadata");
    Check(summary.OverallStatusSource!.LineNumber > 0 && summary.Items.All(i => i.Source.LineNumber > 0), "Summary source evidence");
    Console.WriteLine($"{Path.GetFileName(path)}: overall={summary.OverallStatus}, items={summary.Items.Count}");
}
var auto = RunSummaryParser.ParseFile(Path.Combine(fixtureDirectory, "11_AUTOTEST_Result.txt"));
Check(auto.IsAutoTest && auto.OverallStatus == SummaryStatus.Unknown && auto.Items.Count == 2,
    "AutoTest no overall banner means Unknown");
Check(auto.Items[0].Status == SummaryStatus.Passed && auto.Items[1].Status == SummaryStatus.Failed, "AutoTest item status");
Check(RunSummaryParser.IsSummaryFile(Path.Combine(fixtureDirectory, "11_AUTOTEST_Result.txt")), "renamed AutoTest recognized");

// Use an isolated disposable cache, never the application's user cache.
var cacheDirectory = Path.Combine(Path.GetTempPath(), "SSTLogAnalyser-regression-" + Guid.NewGuid().ToString("N"));
try
{
    using (var cache = new CacheService(cacheDirectory))
    {
        long mixiFileId = 0;
        foreach (var (name, result) in parsed)
        {
            var fileId = cache.InsertLogFile(result.FileInfo, name);
            cache.InsertTestResults(fileId, result.TestResults);
            cache.InsertCalibrationCoefficients(fileId, result.CalibrationCoefficients);
            cache.InsertDiagnosticObservations(fileId, result.DiagnosticObservations);
            cache.MarkFileComplete(fileId);
            var rows = cache.QueryTestResults(new[] { fileId }, null, null, null, null);
            Check(rows.Count == result.TestResults.Count && rows.Count(p => p.IsFailed) == result.TestResults.Count(p => p.IsFailed), "cache count / failure roundtrip " + name);
            foreach (var expected in result.TestResults)
            {
                var actual = rows.Single(p => p.LineNumber == expected.LineNumber && p.TestItemName == expected.TestItemName);
                Check(actual.ModuleType == expected.ModuleType && actual.SlotNumber == expected.SlotNumber &&
                    actual.ChannelId == expected.ChannelId && actual.LoopIndex == expected.LoopIndex &&
                    actual.Timestamp == expected.Timestamp && actual.HasExplicitResult == expected.HasExplicitResult &&
                    actual.AdcMeasureValue == expected.AdcMeasureValue && actual.WaveValue == expected.WaveValue &&
                    actual.OffsetValue == expected.OffsetValue && actual.IsReTest == expected.IsReTest,
                    "cache fields " + name + ":" + expected.LineNumber);
                Near(actual.Difference, expected.Difference, "cache residual");
            }
            var cachedEvents = cache.GetDiagnosticObservations(new[] { fileId });
            Check(cachedEvents.Count == result.DiagnosticObservations.Count, "cache events " + name);
            foreach (var expected in result.DiagnosticObservations)
            {
                var actual = cachedEvents.Single(p => p.LineNumber == expected.LineNumber && p.Item == expected.Item);
                Check(actual.Status == expected.Status && actual.Value == expected.Value &&
                    actual.LowLimit == expected.LowLimit && actual.HighLimit == expected.HighLimit &&
                    actual.Slot == expected.Slot && actual.Channel == expected.Channel &&
                    actual.RawMessage == expected.RawMessage, "cache event details " + expected.LineNumber);
            }
            Check(cache.QueryCalibrationCoefficients(new[] { fileId }, null, null, null, null).Count == result.CalibrationCoefficients.Count, "cache coefficients " + name);
            if (name == "05") Check(cache.GetDistinctModules(new[] { fileId }).Order().SequenceEqual(new[] { "DPS", "PPMU" }), "CAL-only modules available in UI filters");
            if (name == "06")
            {
                mixiFileId = fileId;
                Check(cache.GetDistinctChannels(new[] { fileId }, "MIXI").Count == 5, "MIXI cache channel filters");
                foreach (var group in rows.GroupBy(p => (p.ChannelId, p.LoopIndex)))
                    Check(MixiWaveOffsetAnalyzer.TryAnalyze(group, out _), "MIXI attribution survives cache");
                Check(cache.QueryTestResults(new[] { fileId }, "MIXI", null, new[] { 1 }, new[] { 2 }).Count == 8, "MIXI channel / loop query");
            }
            Check(cache.FindFileByHash(name) == fileId, "current cache version reusable " + name);
        }
        using (var db = new SqliteConnection("Data Source=" + Path.Combine(cacheDirectory, "cache.db")))
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "UPDATE log_files SET parser_version=-1 WHERE file_hash='06'";
            command.ExecuteNonQuery();
        }
        Check(cache.FindFileByHash("06") == null, "outdated parser cache invalidated");
        Check(cache.QueryTestResults(new[] { mixiFileId }, null, null, null, null).Count == 0,
            "outdated cache child rows removed");
        cache.InsertLogFile(parsed["01"].FileInfo, "unfinished-import");
        Check(cache.FindFileByHash("unfinished-import") == null, "unfinished import cannot be reused as a complete cache");
    }
}
finally
{
    SqliteConnection.ClearAllPools();
    if (Directory.Exists(cacheDirectory)) Directory.Delete(cacheDirectory, true);
}
checks += UiImportCheck.Run(fixtureDirectory);
checks += ComparisonCheck.Run();
Console.WriteLine($"PASS: {checks} assertions. Parser, summaries, process logs, attribution, import ViewModel and isolated cache verified.");
