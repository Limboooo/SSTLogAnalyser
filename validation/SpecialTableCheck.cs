using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using SSTLogAnalyser.Models;
using SSTLogAnalyser.Services;

static class SpecialTableCheck
{
    public static int Run(string fixtures, ParseResult tmuvi, ParseResult lateTmu)
    {
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException("Special tables: " + message); checks++; }
        Check(tmuvi.TestResults.Count == 168 && tmuvi.TestResults.All(p => p.ModuleType == ModuleType.MVPI && p.ChannelId == 800), "TMUVI contains four complete 42-point sweeps");
        Check(tmuvi.TestResults.GroupBy(p => p.TestItemName).Count() == 2 &&
            tmuvi.TestResults.GroupBy(p => (p.TestItemName, p.TestSequence)).All(g => g.Count() == 42), "QA/QB and opposite scan directions stay separate");
        Check(tmuvi.CalibrationCoefficients.Count == 2 && tmuvi.DiagnosticObservations.Count == 16, "CVA/CVB coefficients and eight calibration measurements retained separately");
        var source = File.ReadAllLines(Path.Combine(fixtures, "37_TMUVI_Real.log"));
        foreach (var row in tmuvi.TestResults)
        {
            var cells = Regex.Replace(source[row.LineNumber - 1], @"^.*?INFO\s+-\s*", "").Trim().Trim(',').Split(',').Select(s => double.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray();
            Check(row.ExpectValue == cells[1] && row.MeasureValue == cells[2] && row.LowLimit == cells[3] && row.UpLimit == cells[4] && row.Difference == cells[5], "TMUVI values independently match source columns");
            Check(Math.Abs(row.Difference - (row.ExpectValue - row.MeasureValue)) < 1e-12, "TMUVI keeps the source's target-minus-measured sign");
        }
        int rawRows = 0;
        var late = File.ReadAllLines(Path.Combine(fixtures, "38_TMU_Async_Tail.log"));
        for (int i = 0; i < late.Length; i++)
        {
            var message = Regex.Replace(late[i], @"^.*?INFO\s+-\s*", "").Trim();
            if (!Regex.IsMatch(message, @"^\d{4},")) continue;
            rawRows++;
            var values = message.TrimEnd(',').Split(',').Select(s => double.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray();
            var rows = lateTmu.TestResults.Where(p => p.LineNumber == i + 1).ToArray();
            Check(rows.Length == 8 && rows.All(p => p.ChannelId == values[0] && p.ExpectValue == 160), "all eight TMU paths survive interleaved status logs");
            for (int j = 0; j < 8; j++) Check(rows[j].MeasureValue == values[j + 1] && Math.Abs(rows[j].Difference - (values[j + 1] - 160)) < 1e-12, "late TMU paths match original columns");
        }
        Check(lateTmu.TestResults.Count == rawRows * 8, "no extra TMU rows inferred from banners");
        Console.WriteLine($"Special tables: {checks} checks passed.");
        return checks;
    }
}
