using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using SSTLogAnalyser.Models;
using SSTLogAnalyser.Services;

static class HvpiLogCheck
{
    public static int Run(string path, ParseResult parsed)
    {
        int checks = 0, sourcePoints = 0;
        void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException("HVPI real LOG: " + message);
            checks++;
        }
        Check(parsed.TestResults.Count == 256, "256 verification points including 72 MICM points");
        Check(parsed.TestResults.All(p => p.ModuleType == ModuleType.HVPI && p.ChannelId == 800 && p.LoopIndex == 1), "original module, channel and Loop");
        Check(parsed.TestResults.Count(p => p.IsFailed) == 13 && parsed.TestResults.Count(p => p.HasExplicitResult) == 13, "all 13 explicit failures retained");
        var cm = parsed.TestResults.Where(p => p.TestItemName.StartsWith("MICM,")).ToArray();
        Check(cm.Length == 72 && cm.GroupBy(p => p.TestItemName).Count() == 8 &&
            cm.GroupBy(p => p.TestItemName).All(g => g.Count() == 9 && g.All(p => p.TestSequence == 1 && !p.IsFailed)), "eight independent nine-point MICM Items");
        foreach (var operation in new[] { "Force", "Measure" })
        {
            var rows = parsed.TestResults.Where(p => p.TestItemName == $"I, 10uA, {operation} Current").ToArray();
            Check(rows.Count(p => !p.IsReTest && p.TestSequence == 1) == 9 && rows.Count(p => p.IsReTest && p.TestSequence == 2) == 9, operation + " original/retest preserved");
        }

        // Read the source independently: map each numeric verification row by its source line
        // and column names, rather than accepting parser output as the expected baseline.
        var byLine = parsed.TestResults.ToDictionary(p => p.LineNumber);
        string? item = null;
        string range = "";
        string[]? columns = null;
        bool retry = false;
        var lines = File.ReadAllLines(path);
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var message = Regex.Replace(lines[lineIndex], @"^.*?\s+(?:INFO|ERROR|WARN|DEBUG|FATAL)\s+-\s*", "").Trim();
            var title = Regex.Match(message, @"^(Re-test )?HVPI Channel (\d+): (.+) (Verification|Calibration)$");
            if (title.Success)
            {
                item = title.Groups[4].Value == "Verification" ? title.Groups[3].Value : null;
                retry = title.Groups[1].Success;
                columns = null;
                range = "";
                continue;
            }
            if (item == null) continue;
            var rangeMatch = Regex.Match(message, @"IRange:(\w+)");
            if (rangeMatch.Success) { range = rangeMatch.Groups[1].Value; continue; }
            var cells = Regex.Replace(message, @"\s*Failed\s*,?\s*$", "", RegexOptions.IgnoreCase)
                .Trim(',').Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
            if (cells.Length > 1 && cells[0] is "Expect" or "FV_Value") { columns = cells; continue; }
            if (columns == null || cells.Length < columns.Length ||
                !double.TryParse(cells[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _)) continue;
            sourcePoints++;
            var fields = columns.Select((column, i) => (column, value: double.Parse(cells[i], CultureInfo.InvariantCulture)))
                .ToDictionary(p => p.column, p => p.value, StringComparer.OrdinalIgnoreCase);
            var isCm = fields.ContainsKey("CM_Difference");
            Check(byLine.TryGetValue(lineIndex + 1, out var row), "missing source line " + (lineIndex + 1));
            Check(row!.TestItemName == item + (isCm ? $" [{range}]" : "") && row.IsReTest == retry, "Item/retest at line " + row.LineNumber);
            Check(row.ExpectValue == fields[isCm ? "FV_Value" : "Expect"] &&
                row.MeasureValue == fields[isCm ? "MI_ADC" : "DmmMeasure"] &&
                row.AdcMeasureValue == fields[isCm ? "MI_ADC" : "ADCMeasure"] &&
                row.LowLimit == fields[isCm ? "Low Limit" : "LowLimit"] &&
                row.UpLimit == fields[isCm ? "High Limit" : "UpLimit"] &&
                row.Difference == fields[isCm ? "CM_Difference" : "Difference"], "source numeric columns at line " + row.LineNumber);
            var explicitFail = Regex.IsMatch(message, @"\bFailed$", RegexOptions.IgnoreCase);
            Check(row.HasExplicitResult == explicitFail && row.IsFailed ==
                (explicitFail || row.Difference < row.LowLimit || row.Difference > row.UpLimit), "failure marker and limits at line " + row.LineNumber);
        }
        Check(sourcePoints == parsed.TestResults.Count, "no missing verification rows or extra calibration rows");
        return checks;
    }
}
