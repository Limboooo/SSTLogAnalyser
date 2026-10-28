using SSTLogAnalyser.Models;
using SSTLogAnalyser.Services;

static class MvpiFimiCheck
{
    public static int Run(ParseResult parsed, IEnumerable<int> channels)
    {
        int checks = 0;
        void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException("MVPI real FIMI: " + message);
            checks++;
        }
        // Independently counted from the real LOG's four tables per current range.
        foreach (var channel in channels)
        {
            var cm = parsed.TestResults.Where(p => p.ModuleType == ModuleType.MVPI && p.ChannelId == channel &&
                p.TestItemName == "I, 1mA, Measure Current, -1, 42, Force Voltage, CM").ToArray();
            Check(cm.Length == 98 && cm.All(p => !p.AdcMeasureValue.HasValue) &&
                cm.GroupBy(p => p.TestSequence).Select(g => g.Count()).SequenceEqual(new[] { 49, 49 }), $"Ch{channel}: both 49-point CM tables stay separate from MI");
            foreach (var (range, points15, points42) in new[] { ("250mA", 11, 9), ("1mA", 9, 9), ("10uA", 9, 5) })
            foreach (var (voltage, count) in new[] { ("", points15), ("V, 42, ", points42) })
            foreach (var operation in new[] { "Force", "Measure" })
            {
                var item = $"I, {range}, {voltage}{operation} Current";
                var rows = parsed.TestResults.Where(p => p.ModuleType == ModuleType.MVPI &&
                    p.ChannelId == channel && p.TestItemName == item).OrderBy(p => p.ExpectValue).ToArray();
                Check(rows.Length == count, $"Ch{channel} {item}: expected {count}, got {rows.Length}");
                Check(rows.All(p => p.AdcMeasureValue.HasValue == (operation == "Measure")), $"Ch{channel} {item}: ADC attribution");
                Check(rows.All(p => p.TestSequence == 1 && p.LoopIndex == 1), $"Ch{channel} {item}: no phantom Run 2 or Loop changes");
                Check(rows.Select(p => p.ExpectValue).Distinct().Count() == count, $"Ch{channel} {item}: unique setpoints");
                foreach (var row in rows)
                {
                    var expected = operation == "Measure" ? row.MeasureValue - row.AdcMeasureValue!.Value : row.MeasureValue - row.ExpectValue;
                    Check(Math.Abs(row.Difference - expected) < 1e-15, $"Ch{channel} {item}: original error column at line {row.LineNumber}");
                }
            }
        }
        return checks;
    }
}
