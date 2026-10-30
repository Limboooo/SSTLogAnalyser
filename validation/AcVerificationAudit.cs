using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using SSTLogAnalyser.Models;
using SSTLogAnalyser.Services;

static class AcVerificationAudit
{
    public static async Task Run(string path, string reportPath)
    {
        var parsed = await new LogParser().ParseAsync(path);
        var actual = parsed.TestResults.ToLookup(p => (p.LineNumber, p.TestItemName));
        var events = parsed.DiagnosticObservations.ToLookup(p => p.LineNumber);
        int checks = 0, expectedPoints = 0, sourceRows = 0, explicitFailures = 0, summaryMetrics = 0;
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); checks++; }
        var indices = new List<int>();
        var tables = new List<(int Loop, string Clock, int Line, List<int> Pins, HashSet<double> Targets)>();
        string[]? columns = null, summaryEdges = null;
        string clock = "Clock unspecified", summaryClock = "Clock unspecified";
        int loop = 0;
        using var source = new StreamReader(path);
        string? raw; int line = 0;
        while ((raw = await source.ReadLineAsync()) != null)
        {
            line++;
            int separator = raw.IndexOf(" - ", StringComparison.Ordinal);
            var message = (separator >= 0 && raw.Length > 0 && char.IsDigit(raw[0]) ? raw[(separator + 3)..] : raw).Trim();
            var index = Regex.Match(message, @"^Index (\d+) of (\d+)$");
            if (index.Success) { loop = int.Parse(index.Groups[1].Value); indices.Add(loop); columns = null; }
            var frequency = Regex.Match(message, @"^-+SysClk ([\d.]+)Mhz verification (begin|end)-+$", RegexOptions.IgnoreCase);
            if (frequency.Success) clock = frequency.Groups[2].Value.Equals("begin", StringComparison.OrdinalIgnoreCase) ? frequency.Groups[1].Value + " MHz" : "Clock unspecified";
            var finalClock = Regex.Match(message, @"^-+CLK ([\d.]+)MHz Verify result-+$");
            if (finalClock.Success) summaryClock = finalClock.Groups[1].Value + " MHz";
            if (message.StartsWith("#PinNum") && message.Contains("Target(ns)"))
            {
                columns = message.TrimStart('#').Split(',').Select(s => s.Trim()).ToArray();
                tables.Add((loop, clock, line, new(), new()));
                Check(events[line].Any(o => o.Kind == "Verification table" && o.LoopIndex == loop && o.Item == clock), "table clock/Loop context mismatch at " + line);
                continue;
            }
            var cells = message.Split(',').Select(s => s.Trim()).ToArray();
            if (columns != null && cells.Length >= columns.Length && int.TryParse(cells[0], out int pin))
            {
                int targetIndex = Array.IndexOf(columns, "Target(ns)"), specIndex = Array.IndexOf(columns, "Spec(ns)");
                var target = Number(cells[targetIndex]); var spec = Number(cells[specIndex]);
                tables[^1].Pins.Add(pin); tables[^1].Targets.Add(target); sourceRows++;
                for (int i = 1; i < targetIndex; i++)
                    if (double.TryParse(cells[i], CultureInfo.InvariantCulture, out double measure))
                        Verify("AC " + columns[i], target, measure, -spec, spec, measure - target, Math.Abs(measure - target) > spec + 1e-9, false);
                int maxIndex = Array.IndexOf(columns, "MaxError(ns)"), verdictIndex = Array.IndexOf(columns, "Result");
                var fail = cells[verdictIndex].Equals("FAIL", StringComparison.OrdinalIgnoreCase);
                if (fail) explicitFailures++;
                Verify("AC Pin Overall (ns)", 0, Number(cells[maxIndex]), 0, spec, Number(cells[maxIndex]), fail, true);
                void Verify(string item, double expect, double measure, double low, double high, double difference, bool failed, bool explicitResult)
                {
                    var matches = actual[(line, item)].ToArray();
                    Check(matches.Length == 1, "missing or duplicate point at " + line + " / " + item);
                    var p = matches[0]; expectedPoints++;
                    Check(p.ModuleType == ModuleType.AC && p.LoopIndex == loop && p.ChannelId == pin, "wrong point attribution at " + line);
                    Check(p.ExpectValue == expect && p.MeasureValue == measure && p.LowLimit == low && p.UpLimit == high && Math.Abs(p.Difference - difference) < 1e-12, "wrong numeric field at " + line);
                    Check(p.IsFailed == failed && p.HasExplicitResult == explicitResult, "wrong result state at " + line);
                }
                continue;
            }
            if (columns != null && message.Length > 0 && !char.IsDigit(message[0])) columns = null;
            if (message.StartsWith("----|") && message.Contains("(ns)"))
            { summaryEdges = message.Split('|').Where(s => s.Contains("(ns)")).Select(s => s.Trim('-').Trim()).ToArray(); continue; }
            if (summaryEdges != null && message.EndsWith('|') && char.IsDigit(message[0]))
            {
                var groups = message.TrimEnd('|').Split('|');
                if (groups.Length != summaryEdges.Length) continue;
                int summaryPin = int.Parse(groups[0].Split(',')[0]);
                for (int e = 0; e < groups.Length; e++)
                {
                    var values = groups[e].Split(',').Skip(e == 0 ? 1 : 0).ToArray();
                    for (int k = 0; k < 3; k++)
                    {
                        var prefix = summaryEdges[e] + " " + new[] { "Max", "Min", "Range" }[k] + " [" + summaryClock + " / Report";
                        var metric = events[line].Where(o => o.Kind == "Summary" && o.Item.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
                        Check(metric.Length == 1 && metric[0].Channel == summaryPin && metric[0].LoopIndex == loop && metric[0].Value == Number(values[k]) && metric[0].Status == "Unknown", "wrong final summary at " + line);
                        summaryMetrics++;
                    }
                }
            }
        }
        Check(parsed.TestResults.Count == expectedPoints, "unexpected parsed points");
        Check(parsed.DiagnosticObservations.Count(o => o.Kind == "Summary") == summaryMetrics, "unexpected summary metrics");
        Check(tables.All(t => t.Pins.Count == 1280 && t.Pins.Distinct().Count() == 1280 && t.Pins.Min() == 0 && t.Pins.Max() == 1279 && t.Targets.Count == 1), "incomplete or ambiguous source table");
        var activeLoops = parsed.TestResults.Select(p => p.LoopIndex).Distinct().Order().ToArray();
        var cacheDirectory = Path.Combine(Path.GetTempPath(), "SST-ac-ver-audit-" + Guid.NewGuid().ToString("N"));
        int[] cachedLoops;
        try
        {
            using var cache = new CacheService(cacheDirectory);
            var id = cache.InsertLogFile(parsed.FileInfo, "ac-ver-audit");
            cache.InsertTestResults(id, parsed.TestResults);
            cache.InsertDiagnosticObservations(id, parsed.DiagnosticObservations);
            cache.InsertRunBatches(id, parsed.RunBatches); cache.MarkFileComplete(id);
            cachedLoops = cache.GetDistinctLoops(new[] { id }, "AC").ToArray();
            Check(cachedLoops.SequenceEqual(activeLoops), "cache changed active loops");
            Check(cache.QueryTestResults(new[] { id }, "AC", null, null, null).Count == expectedPoints, "cache lost points");
            Check(cache.GetDiagnosticObservations(new[] { id }).Count == parsed.DiagnosticObservations.Count, "cache lost summary or table contexts");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(cacheDirectory)) Directory.Delete(cacheDirectory, true);
        }
        var report = new List<string> { "# AC verification LOG audit", "", $"File: {Path.GetFullPath(path)}", "", $"PASS: {checks:N0} checks against original rows.", "",
            $"- Source Loop headings: {string.Join(", ", indices)}", $"- Parser LoopCount metadata: {parsed.FileInfo.LoopCount}", $"- Loops with measurement data: {string.Join(", ", activeLoops)}",
            $"- Empty source Loops: {string.Join(", ", indices.Except(activeLoops))}", $"- Verification tables: {tables.Count}; source rows: {sourceRows:N0}", $"- Parsed points: {expectedPoints:N0}; failures: {parsed.TestResults.Count(p => p.IsFailed)}; source FAIL rows: {explicitFailures}",
            $"- Final summary metrics: {summaryMetrics:N0}; parser errors: {parsed.Errors.Count}; run batches: {parsed.RunBatches.Count}", "", "| Loop | Clock | Tables | Pin rows |", "|---|---|---:|---:|" };
        report.AddRange(tables.GroupBy(t => (t.Loop, t.Clock)).Select(g => $"| {g.Key.Loop} | {g.Key.Clock} | {g.Count()} | {g.Sum(t => t.Pins.Count):N0} |"));
        report.Add(""); report.Add($"Cache round trip: points and all observations retained; data Loops = {string.Join(", ", cachedLoops)}.");
        report.Add(""); report.Add("LoopCount records the declared total, not the number of loops containing measurements. Empty Loop headings are not fabricated as repeated data.");
        await File.WriteAllLinesAsync(reportPath, report);
        Console.WriteLine(string.Join(Environment.NewLine, report));
    }
    private static double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);
}
