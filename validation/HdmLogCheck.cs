using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using SSTLogAnalyser.Models;
using SSTLogAnalyser.Services;

static class HdmLogCheck
{
    public static int Run(string path, ParseResult parsed)
    {
        int checks = 0, count = 0;
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException("HDM LOG: " + message); checks++; }
        var rows = parsed.TestResults.Where(p => p.ModuleType == ModuleType.HDM).ToDictionary(p => p.LineNumber);
        bool verifying = false, retest = false;
        string? item = null;
        var lines = File.ReadAllLines(path);
        for (int i = 0; i < lines.Length; i++)
        {
            var message = Regex.Replace(lines[i], @"^.*?\s+(?:INFO|ERROR|WARN|DEBUG|FATAL)\s+-\s*", "").Trim();
            var title = Regex.Match(message, @"^(Re-test )?HDM Channel \d+: (.+) (Verification|Calibration)$");
            if (title.Success)
            {
                verifying = title.Groups[3].Value == "Verification";
                retest = title.Groups[1].Success; item = title.Groups[2].Value;
                continue;
            }
            if (Regex.IsMatch(message, @"^(Re-test )?\w+ Channel \d+:")) verifying = false;
            if (!verifying || !Regex.IsMatch(message, @"^,\s*\d+,")) continue;
            var cells = message.Trim(',').Split(',').Select(s => s.Trim()).ToArray();
            if (cells.Length != 8 || cells[7] is not ("Pass" or "Failed")) continue;
            var numbers = cells.Take(7).Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            count++;
            Check(rows.TryGetValue(i + 1, out var row), "missing original line " + (i + 1));
            Check(row!.ChannelId == numbers[0] && row.TestItemName == item && row.IsReTest == retest, "channel/range/retest at " + (i + 1));
            Check(row.ExpectValue == numbers[1] && row.AdcMeasureValue == numbers[2] && row.MeasureValue == numbers[3] &&
                row.LowLimit == numbers[4] && row.UpLimit == numbers[5] && row.Difference == numbers[6], "all seven numeric columns at " + (i + 1));
            Check(row.HasExplicitResult && row.IsFailed == (cells[7] == "Failed" || numbers[6] < numbers[4] || numbers[6] > numbers[5]), "explicit result and limits at " + (i + 1));
        }
        Check(count > 0 && count == rows.Count, "no missing verification rows or extra calibration rows");
        Console.WriteLine($"HDM: {count} source rows, {rows.Values.Count(p => p.IsFailed)} failed; {checks} checks passed ({path}).");
        return checks;
    }
}
