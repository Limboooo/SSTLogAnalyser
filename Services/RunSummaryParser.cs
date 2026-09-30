using System.IO;
using System.Text.RegularExpressions;
using SSTLogAnalyser.Models;

namespace SSTLogAnalyser.Services;

public static class RunSummaryParser
{
    public static bool IsSummaryFile(string path)
    {
        // Content recognition also supports renamed summaries and fixture filenames.
        return File.ReadLines(path).Take(100).Any(line => Family.IsMatch(line.Trim()) ||
            line.Contains("AutoTest Result Summary", StringComparison.Ordinal));
    }
    private static readonly Regex NumberedItem = new(@"^\s*\d+\.\d+\)\s*(?<name>.+?)\s*\.{2,}\s*(?<status>PASSED|failed|Abort|Not Test|Not Applied)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AutoItem = new(@"^\s*Item\[(?<name>[^]]+)\],Result\[(?<status>PASSED|failed|Abort|Not Test|Not Applied)\]\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FailedSlot = new(@"^\s*Slot-(?<slot>\d+(?:\(\d+\))?),\s*(?<board>[A-Z0-9]+),\s*\(--Fail--\)\s*$", RegexOptions.Compiled);
    private static readonly Regex Board = new(@"^Slot-(?<slot>\d+):\s*(?<description>.*?)(?:,\s*S/N:\s*(?<serial>.*))?$", RegexOptions.Compiled);
    private static readonly Regex Family = new(@"^\*+\s*(?<family>AStar|QStar|HStar|DStar|EStar) SST Summary\s*\*+$", RegexOptions.Compiled);

    public static RunSummary ParseFile(string filePath) => ParseLines(File.ReadLines(filePath), filePath);

    public static RunSummary ParseLines(IEnumerable<string> lines, string sourceFile = "")
    {
        var summary = new RunSummary { SourceFile = sourceFile };
        string section = string.Empty;
        SummaryItem? lastFailedItem = null;
        int number = 0;
        foreach (var line in lines)
        {
            number++;
            var source = new SummarySource { LineNumber = number, Text = line };
            var trimmed = line.Trim();
            if (trimmed.Contains("AutoTest Result Summary", StringComparison.Ordinal))
                summary.IsAutoTest = true;

            // The large ASCII result banner is emitted by showResultPicture in SST.
            if (!summary.IsAutoTest && summary.OverallStatusSource is null)
            {
                if (line.Contains("**   PPPPPPP            AAA", StringComparison.Ordinal)) SetOverall(SummaryStatus.Passed, source);
                else if (line.Contains("**   FFFFFFFFFF         AAA", StringComparison.Ordinal)) SetOverall(SummaryStatus.Failed, source);
                else if (line.Contains("**         AAA         BBBBBBBB", StringComparison.Ordinal)) SetOverall(SummaryStatus.Aborted, source);
            }

            var family = Family.Match(trimmed);
            if (family.Success) summary.TesterFamily = family.Groups["family"].Value;
            if (trimmed.StartsWith("******* ", StringComparison.Ordinal) && trimmed.Contains(" Summary", StringComparison.Ordinal))
                section = trimmed.Trim('*', ' ');

            var item = NumberedItem.Match(line);
            if (!item.Success) item = AutoItem.Match(line);
            if (item.Success)
            {
                var parsed = new SummaryItem
                {
                    Section = section,
                    Name = item.Groups["name"].Value.Trim(),
                    RawStatus = item.Groups["status"].Value,
                    Status = ParseStatus(item.Groups["status"].Value),
                    Source = source
                };
                summary.Items.Add(parsed);
                lastFailedItem = parsed.Status == SummaryStatus.Failed ? parsed : null;
                continue;
            }

            var failed = FailedSlot.Match(line);
            if (failed.Success && lastFailedItem is not null)
            {
                lastFailedItem.FailedSlots.Add(new SummaryFailedSlot
                {
                    Slot = failed.Groups["slot"].Value,
                    BoardType = failed.Groups["board"].Value,
                    Source = source
                });
                continue;
            }
            if (trimmed.Length == 0 || trimmed.StartsWith("*******", StringComparison.Ordinal)) lastFailedItem = null;

            var board = Board.Match(line);
            if (board.Success && line.Contains(':'))
            {
                summary.Boards.Add(new SummaryBoard
                {
                    Slot = board.Groups["slot"].Value,
                    Description = board.Groups["description"].Value.Trim(),
                    SerialNumber = board.Groups["serial"].Value.Trim(),
                    Source = source
                });
                continue;
            }

            foreach (var key in new[] { "Tester S/N", "Meter (Keysight 3458A) S/N", "DC Calibration Board S/N", "AC Calibration Board S/N", "Odd-Even Diag Board S/N", "Operator", "Date" })
            {
                if (line.StartsWith(key + ":", StringComparison.Ordinal))
                    summary.Metadata[key] = new SummaryField { Value = line[(key.Length + 1)..].Trim(), Source = source };
            }
        }
        return summary;

        void SetOverall(SummaryStatus status, SummarySource origin)
        {
            summary.OverallStatus = status;
            summary.OverallStatusSource = origin;
        }
    }

    private static SummaryStatus ParseStatus(string value) => value.ToUpperInvariant() switch
    {
        "PASSED" => SummaryStatus.Passed,
        "FAILED" => SummaryStatus.Failed,
        "ABORT" => SummaryStatus.Aborted,
        "NOT TEST" => SummaryStatus.NotTested,
        "NOT APPLIED" => SummaryStatus.NotApplicable,
        _ => SummaryStatus.Unknown
    };
}
