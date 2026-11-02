using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SSTLogAnalyser.Services;

static class ReferenceTableAudit
{
    public static async Task Run(string inventory, string output)
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(inventory));
        string Normalize(string message) => string.Join(",", message.Trim().Trim(',', '，').Split(',').Select(s => s.Trim()));
        var headers = document.RootElement.GetProperty("headers").EnumerateObject().Select(p => Normalize(p.Name)).ToHashSet();
        var report = new StringBuilder("# Reference LOG table audit\n\n| File | Points | Covered table rows | Missing verification rows | Calibration-only rows |\n|---|---:|---:|---:|---:|\n");
        var missing = new List<string>();
        foreach (var file in document.RootElement.GetProperty("files").EnumerateArray())
        {
            var path = file.GetProperty("file").GetString()!;
            var parsed = await new LogParser().ParseAsync(path);
            var covered = parsed.TestResults.Select(p => p.LineNumber).Concat(parsed.DiagnosticObservations.Select(p => p.LineNumber)).ToHashSet();
            string? table = null; bool calibration = false;
            int line = 0, captured = 0, absent = 0, cal = 0;
            foreach (var raw in File.ReadLines(path))
            {
                line++;
                var message = Regex.Replace(raw, @"^\d{4}-\d\d-\d\d .+?\s+(?:INFO|ERROR|FATAL|WARN|DEBUG|TRACE)\s+-\s*", "").Trim();
                if (Regex.IsMatch(message, @"(?:Calibration|Verification)(?:VI|AWG)?$"))
                { calibration = message.EndsWith("Calibration", StringComparison.Ordinal); table = null; }
                var normalized = Normalize(message);
                if (headers.Contains(normalized)) { table = normalized; continue; }
                if (table == null || !Regex.IsMatch(message, @"^,?\s*[+-]?\d+(?:[.,\s])")) continue;
                if (!message.Contains(',')) continue;
                if (covered.Contains(line)) captured++;
                else if (calibration) cal++;
                else
                {
                    absent++;
                    if (missing.Count < 150) missing.Add($"- {path}:{line} / {table} / {message[..Math.Min(message.Length, 180)]}");
                }
            }
            report.AppendLine($"| {path} | {parsed.TestResults.Count} | {captured} | {absent} | {cal} |");
            Console.WriteLine($"{Path.GetFileName(path)}: points={parsed.TestResults.Count}, covered={captured}, missing={absent}, calibration-only={cal}");
            parsed = null;
            GC.Collect();
        }
        report.AppendLine("\n## Missing-row examples\n");
        report.AppendLine(missing.Count == 0 ? "None." : string.Join('\n', missing));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        await File.WriteAllTextAsync(output, report.ToString());
        Console.WriteLine("Audit: " + output);
    }
}
