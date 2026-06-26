using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using SSTLogAnalyser.Models;

namespace SSTLogAnalyser.Services;

public class LogParser
{
    private static readonly Regex TimestampRegex = new(@"^(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2},\d{3})\s+(INFO|ERROR|FATAL|WARN)\s+-\s*(.*)", RegexOptions.Compiled);
    private static readonly Regex LoopRegex = new(@"Index\s+(\d+)\s+of\s+(\d+)", RegexOptions.Compiled);
    private static readonly Regex VerificationHeaderRegex = new(@"^(Re-test\s+)?(\w+)\s+Channel\s+(\d+):\s*(.+?),\s*(.+)\s+Verification\s*$", RegexOptions.Compiled);
    private static readonly Regex PeVerificationRegex = new(@"^Pin:\s*(\d+)\s+the\s+(.+)\s+Verification:\s*(Pass|Fail)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PeTestHeaderRegex = new(@"^(Re-test\s+)?PE\s+(.+)\s+Verification\s*$", RegexOptions.Compiled);
    private static readonly Regex DeviceGroupRegex = new(@"^Group\s+(\d+):\s*Location\s+(\d+)\((\d+)\)->\s*Slot\s+(.+?):\s*(.+?)\((\d+)\)", RegexOptions.Compiled);
    private static readonly Regex ToolVersionRegex = new(@"Version:\s*([\d.]+)", RegexOptions.Compiled);
    private static readonly Regex DmmTempRegex = new(@"DMM\s+Temp(?:er)?ature:([\d.]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SysClkRegex = new(@"SysClk\s+measured.*?([\d.]+)\s+MHz", RegexOptions.Compiled);
    private static readonly Regex TmuCalRegex = new(@"TMU\s+CalDate:\s*(.+?);\s*Tmu\s+Cal\s+Value:\s*(.+?);\s*Tmu\s+Next\s+CalDate:\s*(.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SoftwareNameRegex = new(@"SoftwareName:\s*(.+?)\s+FileVersion:\s*(.+)", RegexOptions.Compiled);

    public async Task<ParseResult> ParseAsync(string filePath, IProgress<ParseProgress>? progress = null, CancellationToken ct = default)
    {
        var result = new ParseResult();
        var fileInfo = new FileInfo(filePath);
        result.FileInfo = new LogFileInfo
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            FileSize = fileInfo.Length
        };

        long estimatedLines = fileInfo.Length / 130;
        int currentLoop = 0;
        int maxLoop = 0;
        string? currentTestHeader = null;
        ModuleType currentModule = ModuleType.Unknown;
        int currentChannel = 0;
        bool isReTest = false;
        string[]? columnNames = null;
        bool inDataBlock = false;
        string currentTimestamp = string.Empty;

        using var reader = new StreamReader(filePath);
        int lineNumber = 0;
        string? line;

        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            lineNumber++;
            ct.ThrowIfCancellationRequested();

            if (lineNumber % 10000 == 0)
            {
                var pct = Math.Min(100, (int)(lineNumber * 100.0 / estimatedLines));
                progress?.Report(new ParseProgress(pct, lineNumber, 0));
            }

            var tsMatch = TimestampRegex.Match(line);
            string message;
            string level;
            if (tsMatch.Success)
            {
                currentTimestamp = tsMatch.Groups[1].Value;
                level = tsMatch.Groups[2].Value;
                message = tsMatch.Groups[3].Value.Trim();
            }
            else
            {
                level = "INFO";
                message = line.Trim();
            }

            var loopMatch = LoopRegex.Match(message);
            if (loopMatch.Success)
            {
                int idx = int.Parse(loopMatch.Groups[1].Value);
                int total = int.Parse(loopMatch.Groups[2].Value);
                if (!isReTest || idx != currentLoop)
                {
                    currentLoop = idx;
                    maxLoop = Math.Max(maxLoop, total);
                }
                continue;
            }

            var verMatch = VerificationHeaderRegex.Match(message);
            if (verMatch.Success)
            {
                isReTest = verMatch.Groups[1].Success && verMatch.Groups[1].Value.Trim().Length > 0;
                string moduleStr = verMatch.Groups[2].Value.Trim();
                currentChannel = int.Parse(verMatch.Groups[3].Value);
                string rangeInfo = verMatch.Groups[4].Value.Trim();
                string testName = verMatch.Groups[5].Value.Trim();
                currentModule = ParseModuleType(moduleStr);
                currentTestHeader = FormatTestItemName(rangeInfo, testName);
                columnNames = null;
                inDataBlock = true;
                continue;
            }

            var peMatch = PeTestHeaderRegex.Match(message);
            if (peMatch.Success)
            {
                isReTest = peMatch.Groups[1].Success && peMatch.Groups[1].Value.Trim().Length > 0;
                currentModule = ModuleType.PE;
                currentTestHeader = peMatch.Groups[2].Value.Trim();
                columnNames = null;
                inDataBlock = true;
                continue;
            }

            if (PeVerificationRegex.IsMatch(message))
                continue;

            if (inDataBlock && message.StartsWith(",") && message.Contains("Expect"))
            {
                columnNames = ParseCsvFields(message);
                continue;
            }

            if (inDataBlock && columnNames != null && message.StartsWith(","))
            {
                var tr = ParseDataRow(message, columnNames, currentLoop, currentModule, currentChannel,
                    currentTestHeader ?? "Unknown", isReTest, lineNumber);
                if (tr != null)
                    result.TestResults.Add(tr);
                continue;
            }

            if (inDataBlock && !message.StartsWith(",") && !string.IsNullOrWhiteSpace(message))
            {
                if (!VerificationHeaderRegex.IsMatch(message) && !PeTestHeaderRegex.IsMatch(message))
                {
                    inDataBlock = false;
                    columnNames = null;
                    isReTest = false;
                }
            }

            if (level == "ERROR" || level == "FATAL")
            {
                if (message.Contains("**") || string.IsNullOrWhiteSpace(message))
                    continue;
                result.Errors.Add(new ErrorLogEntry
                {
                    LoopIndex = currentLoop,
                    Timestamp = currentTimestamp,
                    Level = level,
                    Message = message,
                    LineNumber = lineNumber
                });
            }

            var devMatch = DeviceGroupRegex.Match(message);
            if (devMatch.Success)
            {
                result.Devices.Add(new DeviceInfo
                {
                    GroupNumber = int.Parse(devMatch.Groups[1].Value),
                    LocationId = int.Parse(devMatch.Groups[3].Value),
                    SlotInfo = devMatch.Groups[4].Value.Trim(),
                    DeviceName = devMatch.Groups[5].Value.Trim()
                });
                continue;
            }

            ExtractSystemInfo(message, result);
        }

        result.FileInfo.LoopCount = maxLoop;
        result.FileInfo.ParseTime = DateTime.Now;
        progress?.Report(new ParseProgress(100, lineNumber, result.TestResults.Count));
        return result;
    }

    private static ModuleType ParseModuleType(string moduleStr)
    {
        return moduleStr.ToUpperInvariant() switch
        {
            "DPS" => ModuleType.DPS,
            "PMU" => ModuleType.PMU,
            "PE" => ModuleType.PE,
            "DPSI" => ModuleType.DPSI,
            "AWG" => ModuleType.AWG,
            "DTZ" => ModuleType.DTZ,
            "MIXI" => ModuleType.MIXI,
            _ => ModuleType.Unknown
        };
    }

    private static string FormatTestItemName(string rangeInfo, string testName)
    {
        var range = rangeInfo.Trim();
        var test = testName.Trim();
        if (!string.IsNullOrEmpty(range))
            return range + ", " + test;
        return test;
    }

    private static string[] ParseCsvFields(string line)
    {
        var trimmed = line.Trim().TrimStart(',').TrimEnd(',');
        return trimmed.Split(',').Select(f => f.Trim()).Where(f => f.Length > 0).ToArray();
    }

    private TestResult? ParseDataRow(string line, string[] columnNames, int loopIndex,
        ModuleType moduleType, int channelId, string testItemName, bool isReTest, int lineNumber)
    {
        try
        {
            var parts = line.Trim().TrimStart(',').TrimEnd(',').Split(',');
            if (parts.Length < columnNames.Length)
                return null;

            // Filter out empty trailing entries (from trailing commas)
            var filteredParts = parts.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).ToArray();
            if (filteredParts.Length < columnNames.Length)
                return null;
            parts = filteredParts;

            double expect, measure, lowLimit, upLimit, difference;
            int actualChannel = channelId;

            bool hasPin = columnNames.Length > 0 && columnNames[0].Equals("Pin", StringComparison.OrdinalIgnoreCase);

            if (hasPin && columnNames.Length >= 6)
            {
                if (!int.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out actualChannel))
                    return null;
                expect = ParseDouble(parts[1]);
                measure = ParseDouble(parts[2]);
                lowLimit = ParseDouble(parts[3]);
                upLimit = ParseDouble(parts[4]);
                difference = ParseDouble(parts[5]);
            }
            else if (columnNames.Length >= 6)
            {
                expect = ParseDouble(parts[0]);
                measure = ParseDouble(parts[1]);
                lowLimit = ParseDouble(parts[3]);
                upLimit = ParseDouble(parts[4]);
                difference = ParseDouble(parts[5]);
            }
            else if (columnNames.Length >= 5)
            {
                expect = ParseDouble(parts[0]);
                measure = ParseDouble(parts[1]);
                lowLimit = ParseDouble(parts[2]);
                upLimit = ParseDouble(parts[3]);
                difference = ParseDouble(parts[4]);
            }
            else
            {
                return null;
            }

            bool isFailed = false;
            string lastField = parts.Length > columnNames.Length ? parts[parts.Length - 1].Trim() : string.Empty;
            if (lastField.Equals("Failed", StringComparison.OrdinalIgnoreCase))
                isFailed = true;
            else if (difference > upLimit || difference < lowLimit)
                isFailed = true;

            return new TestResult
            {
                LoopIndex = loopIndex,
                ModuleType = moduleType,
                SlotNumber = 0,
                ChannelId = actualChannel,
                TestItemName = testItemName,
                ExpectValue = expect,
                MeasureValue = measure,
                LowLimit = lowLimit,
                UpLimit = upLimit,
                Difference = difference,
                IsFailed = isFailed,
                IsReTest = isReTest,
                LineNumber = lineNumber
            };
        }
        catch
        {
            return null;
        }
    }

    private static double ParseDouble(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return 0;
        if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
            return val;
        return 0;
    }

    private void ExtractSystemInfo(string message, ParseResult result)
    {
        var swMatch = SoftwareNameRegex.Match(message);
        if (swMatch.Success)
        {
            var driverName = swMatch.Groups[1].Value.Trim();
            var driverVersion = swMatch.Groups[2].Value.Trim();
            result.SystemInfos.Add(new SystemInfo { Key = driverName, Value = driverVersion });
            return;
        }

        var verMatch = ToolVersionRegex.Match(message);
        if (verMatch.Success)
        {
            result.FileInfo.ToolVersion = verMatch.Groups[1].Value;
            result.SystemInfos.Add(new SystemInfo { Key = "Tool Version", Value = verMatch.Groups[1].Value });
            return;
        }

        var tempMatch = DmmTempRegex.Match(message);
        if (tempMatch.Success)
        {
            result.SystemInfos.Add(new SystemInfo { Key = "DMM Temperature", Value = tempMatch.Groups[1].Value + " C" });
            return;
        }

        var clkMatch = SysClkRegex.Match(message);
        if (clkMatch.Success)
        {
            result.SystemInfos.Add(new SystemInfo { Key = "System Clock", Value = clkMatch.Groups[1].Value + " MHz" });
            return;
        }

        var tmuMatch = TmuCalRegex.Match(message);
        if (tmuMatch.Success)
        {
            result.SystemInfos.Add(new SystemInfo { Key = "TMU CalDate", Value = tmuMatch.Groups[1].Value.Trim() });
            result.SystemInfos.Add(new SystemInfo { Key = "TMU CalValue", Value = tmuMatch.Groups[2].Value.Trim() });
            result.SystemInfos.Add(new SystemInfo { Key = "TMU NextCalDate", Value = tmuMatch.Groups[3].Value.Trim() });
            return;
        }

        if (message.StartsWith("Operator:"))
            result.SystemInfos.Add(new SystemInfo { Key = "Operator", Value = message.Substring(9).Trim() });
        else if (message.StartsWith("Tester ID:"))
            result.SystemInfos.Add(new SystemInfo { Key = "Tester ID", Value = message.Substring(10).Trim() });
        else if (message.StartsWith("Meter ID:"))
            result.SystemInfos.Add(new SystemInfo { Key = "Meter ID", Value = message.Substring(9).Trim() });
    }
}

public class ParseResult
{
    public LogFileInfo FileInfo { get; set; } = new();
    public List<TestResult> TestResults { get; set; } = new();
    public List<DeviceInfo> Devices { get; set; } = new();
    public List<ErrorLogEntry> Errors { get; set; } = new();
    public List<SystemInfo> SystemInfos { get; set; } = new();
}

public record ParseProgress(int Percent, int LinesRead, int DataPoints);
