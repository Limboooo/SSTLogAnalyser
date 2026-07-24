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
    private static readonly Regex CalibrationHeaderRegex = new(@"^(\w+)\s+Channel\s+(\d+):\s*(.+?)\s+Calibration\s*$", RegexOptions.Compiled);
    private static readonly Regex CoefficientRegex = new(@"^(\w+)\s+(.+?)\s+Gain:\s*([\d.\-Ee+]+),\s*Offset:\s*([\d.\-Ee+]+)", RegexOptions.Compiled);
    private static readonly Regex MixiCoefficientRegex = new(@"^(?:-+[^-]+-+\s+)?M/C\s+M\s*[:=]\s*([\d.\-Ee+]+)\s*[,;]?\s*C\s*[:=]\s*([\d.\-Ee+]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MixiCoefficientSummaryRegex = new(@"^\w+\s+(AWG|DTZ)\s*(\d+)\s*,\s*([^,]+)\s*,\s*M\s*[:=]\s*([\d.\-Ee+]+)\s*,\s*C\s*[:=]\s*([\d.\-Ee+]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PeVerificationRegex = new(@"^Pin:\s*(\d+)\s+the\s+(.+?)\s+Verification(?:\s+\([^)]+\))?:\s*(Pass|Fail)\.?\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PeTestHeaderRegex = new(@"^(Re-test\s+)?(?:(?:PE\s+)?(\w+))\s+Verification\s*$", RegexOptions.Compiled);
    private static readonly Regex PpmuVerificationHeaderRegex = new(@"^(Re-test\s+)?PPMU\s+(.+?)\s+Verification\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PpmuCalibrationHeaderRegex = new(@"^PPMU\s+(.+?)\s+Calibration\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PpmuCoefficientRegex = new(@"^PE\s+Pin:\s*(\d+)\s+PPMU\s+(.+?)\s+Calibration(?:\s+(.+?))?\s+Gain:\s*([\d.\-Ee+]+),\s*Offset:\s*([\d.\-Ee+]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DeviceGroupRegex = new(@"^Group\s+(\d+):\s*Location\s+(\d+)\((\d+)\)->\s*Slot\s+(.+?):\s*(.+?)\((\d+)\)", RegexOptions.Compiled);
    private static readonly Regex ToolVersionRegex = new(@"Version:\s*([\d.]+)", RegexOptions.Compiled);
    private static readonly Regex DmmTempRegex = new(@"DMM\s+Temp(?:er)?ature:([\d.]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SysClkRegex = new(@"SysClk\s+measured.*?([\d.]+)\s+MHz", RegexOptions.Compiled);
    private static readonly Regex TmuCalRegex = new(@"TMU\s+CalDate:\s*(.+?);\s*Tmu\s+Cal\s+Value:\s*(.+?);\s*Tmu\s+Next\s+CalDate:\s*(.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SoftwareNameRegex = new(@"SoftwareName:\s*(.+?)\s+FileVersion:\s*(.+)", RegexOptions.Compiled);
    private static readonly Regex MixiHeaderRegex = new(@"^(AWG|DTZ)\s+CH\s+(\d+)\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex MixiDataRegex = new(@"--(?<component>.+?)--\s+Target:[\d.\-Ee+]+.*?Meas:(?<measure>[\d.\-Ee+]+)\s+LowLimit:(?<low>[\d.\-Ee+]+)\s+HighLimit:(?<high>[\d.\-Ee+]+)\s+Meas-Target:(?<difference>[\d.\-Ee+]+)", RegexOptions.Compiled);

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
        bool inMixiBlock = false;
        string? mixiTestItem = null;
        int mixiChannel = 0;
        string? currentCalibrationItem = null;
        string? currentPpmuTestHeader = null;

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

            var calibrationMatch = CalibrationHeaderRegex.Match(message);
            if (calibrationMatch.Success)
            {
                currentModule = ParseModuleType(calibrationMatch.Groups[1].Value.Trim());
                currentChannel = int.Parse(calibrationMatch.Groups[2].Value);
                currentCalibrationItem = calibrationMatch.Groups[3].Value.Trim();
                inDataBlock = false;
                columnNames = null;
                continue;
            }

            var ppmuCalibrationMatch = PpmuCalibrationHeaderRegex.Match(message);
            if (ppmuCalibrationMatch.Success)
            {
                currentModule = ModuleType.PPMU;
                currentCalibrationItem = ppmuCalibrationMatch.Groups[1].Value.Trim();
                currentPpmuTestHeader = null;
                inDataBlock = false;
                columnNames = null;
                continue;
            }

            var ppmuCoefficientMatch = PpmuCoefficientRegex.Match(message);
            if (ppmuCoefficientMatch.Success)
            {
                var calibrationItem = ppmuCoefficientMatch.Groups[2].Value.Trim();
                var coefficientName = ppmuCoefficientMatch.Groups[3].Success
                    ? ppmuCoefficientMatch.Groups[3].Value.Trim()
                    : calibrationItem;
                AddOrUpdateCalibrationCoefficient(result, new CalibrationCoefficient
                {
                    LoopIndex = currentLoop,
                    ModuleType = ModuleType.PPMU,
                    ChannelId = int.Parse(ppmuCoefficientMatch.Groups[1].Value),
                    CalibrationItem = calibrationItem,
                    CoefficientName = NormalizeCoefficientName(coefficientName),
                    Gain = ParseDouble(ppmuCoefficientMatch.Groups[4].Value),
                    Offset = ParseDouble(ppmuCoefficientMatch.Groups[5].Value),
                    LineNumber = lineNumber
                });
                continue;
            }

            var coefficientMatch = CoefficientRegex.Match(message);
            if (coefficientMatch.Success && currentCalibrationItem != null)
            {
                var coefficientModule = ParseModuleType(coefficientMatch.Groups[1].Value.Trim());
                if (coefficientModule != ModuleType.Unknown && coefficientModule == currentModule)
                {
                    AddOrUpdateCalibrationCoefficient(result, new CalibrationCoefficient
                    {
                        LoopIndex = currentLoop,
                        ModuleType = coefficientModule,
                        ChannelId = currentChannel,
                        CalibrationItem = currentCalibrationItem,
                        CoefficientName = NormalizeCoefficientName(coefficientMatch.Groups[2].Value),
                        Gain = ParseDouble(coefficientMatch.Groups[3].Value),
                        Offset = ParseDouble(coefficientMatch.Groups[4].Value),
                        LineNumber = lineNumber
                    });
                    continue;
                }
            }

            var mixiCoefficientSummaryMatch = MixiCoefficientSummaryRegex.Match(message);
            if (mixiCoefficientSummaryMatch.Success)
            {
                var deviceType = mixiCoefficientSummaryMatch.Groups[1].Value.ToUpperInvariant();
                AddOrUpdateCalibrationCoefficient(result, new CalibrationCoefficient
                {
                    LoopIndex = currentLoop,
                    ModuleType = ModuleType.MIXI,
                    ChannelId = int.Parse(mixiCoefficientSummaryMatch.Groups[2].Value),
                    CalibrationItem = deviceType + " " + mixiCoefficientSummaryMatch.Groups[3].Value.Trim(),
                    CoefficientName = "M/C",
                    Gain = ParseDouble(mixiCoefficientSummaryMatch.Groups[4].Value),
                    Offset = ParseDouble(mixiCoefficientSummaryMatch.Groups[5].Value),
                    LineNumber = lineNumber
                });
                continue;
            }

            var ppmuVerificationMatch = PpmuVerificationHeaderRegex.Match(message);
            if (ppmuVerificationMatch.Success)
            {
                isReTest = ppmuVerificationMatch.Groups[1].Success && ppmuVerificationMatch.Groups[1].Value.Trim().Length > 0;
                currentModule = ModuleType.PPMU;
                currentPpmuTestHeader = ppmuVerificationMatch.Groups[2].Value.Trim();
                currentTestHeader = currentPpmuTestHeader;
                currentCalibrationItem = null;
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
                currentPpmuTestHeader = null;
                columnNames = null;
                inDataBlock = true;
                continue;
            }

            if (PeVerificationRegex.IsMatch(message))
                continue;

            // MIXI header detection: "AWG CH 42 AWG_CAL_HS_1M5_OFFSET_DAC" or "DTZ CH 43 DTZ_VER_..."
            var mixiHeaderMatch = MixiHeaderRegex.Match(message);
            if (mixiHeaderMatch.Success)
            {
                var deviceType = mixiHeaderMatch.Groups[1].Value; // AWG or DTZ
                mixiChannel = int.Parse(mixiHeaderMatch.Groups[2].Value);
                var testName = mixiHeaderMatch.Groups[3].Value.Trim();
                mixiTestItem = deviceType + " " + testName;
                inMixiBlock = true;
                continue;
            }

            // MIXI data line: "--POS-- Target:2.85 Meas:2.88 LowLimit:2.35 HighLimit:3.35 Meas-Target:0.03"
            if (inMixiBlock && mixiTestItem != null)
            {
                var mixiCoefficientMatch = MixiCoefficientRegex.Match(message);
                if (mixiCoefficientMatch.Success)
                {
                    AddOrUpdateCalibrationCoefficient(result, new CalibrationCoefficient
                    {
                        LoopIndex = currentLoop,
                        ModuleType = ModuleType.MIXI,
                        ChannelId = mixiChannel,
                        CalibrationItem = mixiTestItem,
                        CoefficientName = "M/C",
                        Gain = ParseDouble(mixiCoefficientMatch.Groups[1].Value),
                        Offset = ParseDouble(mixiCoefficientMatch.Groups[2].Value),
                        LineNumber = lineNumber
                    });
                    continue;
                }

                var mixiDataMatch = MixiDataRegex.Match(message);
                if (mixiDataMatch.Success)
                {
                    // Extract Target value separately (may have extra content like [WAVE:... OFFSET:...])
                    var targetMatch = System.Text.RegularExpressions.Regex.Match(message, @"Target:([\d.\-Ee+]+)");
                    double target = targetMatch.Success ? ParseDouble(targetMatch.Groups[1].Value) : 0;
                    double meas = ParseDouble(mixiDataMatch.Groups["measure"].Value);
                    double lowLimit = ParseDouble(mixiDataMatch.Groups["low"].Value);
                    double upLimit = ParseDouble(mixiDataMatch.Groups["high"].Value);
                    double diff = ParseDouble(mixiDataMatch.Groups["difference"].Value);
                    // MIXI limits are absolute bounds on Meas, not on Difference (Meas-Target)
                    bool isFailed = meas > upLimit || meas < lowLimit;

                    // Extract WAVE and OFFSET if present
                    double? waveValue = null, offsetValue = null;
                    var waveMatch = System.Text.RegularExpressions.Regex.Match(message, @"WAVE:([\d.\-Ee+]+)");
                    var offsetMatch = System.Text.RegularExpressions.Regex.Match(message, @"OFFSET:([\d.\-Ee+]+)");
                    if (waveMatch.Success) waveValue = ParseDouble(waveMatch.Groups[1].Value);
                    if (offsetMatch.Success) offsetValue = ParseDouble(offsetMatch.Groups[1].Value);

                    result.TestResults.Add(new TestResult
                    {
                        LoopIndex = currentLoop,
                        ModuleType = ModuleType.MIXI,
                        SlotNumber = 0,
                        ChannelId = mixiChannel,
                        TestItemName = mixiTestItem,
                        ExpectValue = target,
                        MeasureValue = meas,
                        LowLimit = lowLimit,
                        UpLimit = upLimit,
                        Difference = diff,
                        IsFailed = isFailed,
                        IsReTest = false,
                        LineNumber = lineNumber,
                        WaveValue = waveValue,
                        OffsetValue = offsetValue,
                        DiffValue = diff,
                        ComponentType = mixiDataMatch.Groups["component"].Value.Trim().Trim('-').Trim()
                    });
                    continue;
                }

                // Do not close MIXI block on noise lines; next header will overwrite
            }

            // Detect CSV column header (initial or mid-block switch, e.g. Measure Voltage after Force Voltage)
            if (inDataBlock && message.StartsWith(",") && message.Contains("Expect", StringComparison.OrdinalIgnoreCase))
            {
                var newCols = ParseCsvFields(message);
                columnNames = newCols;
                if (currentModule == ModuleType.PPMU && currentPpmuTestHeader != null)
                    currentTestHeader = FormatPpmuTestItem(currentPpmuTestHeader, newCols.FirstOrDefault());
                if (currentTestHeader != null
                    && currentTestHeader.Contains("Force Voltage", StringComparison.OrdinalIgnoreCase)
                    && newCols.Any(c => c.Contains("AdcMeasure", StringComparison.OrdinalIgnoreCase)))
                {
                    string inferredType = InferTestTypeFromColumns(newCols);
                    string rangePrefix = GetVoltageRangePrefix(currentTestHeader);
                    currentTestHeader = rangePrefix + ", " + inferredType;
                }
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
                // Close data block only on clear section transitions, not on mid-block noise
                if (!VerificationHeaderRegex.IsMatch(message) && !PeTestHeaderRegex.IsMatch(message)
                    && (message.Contains("Calibration", StringComparison.OrdinalIgnoreCase)
                        || message.StartsWith("Point :", StringComparison.Ordinal)
                        || message.Contains("Gain:", StringComparison.Ordinal)
                        || message.Contains("Clamp Currents", StringComparison.OrdinalIgnoreCase)))
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

    private static string GetVoltageRangePrefix(string testHeader)
    {
        // Extract range prefix from test header like "V, -3, 8, Force Voltage" �� "V, -3, 8"
        var parts = testHeader.Split(',').Select(p => p.Trim()).ToArray();
        if (parts.Length >= 3)
            return parts[0] + ", " + parts[1] + ", " + parts[2];
        return testHeader;
    }

    private static string InferTestTypeFromColumns(string[] columnNames)
    {
        // Detect ADC measure column which indicates Measure Voltage
        bool hasAdc = columnNames.Any(c =>
            c.Contains("AdcMeasure", StringComparison.OrdinalIgnoreCase));
        return hasAdc ? "Measure Voltage" : "Force Voltage";
    }

    private static ModuleType ParseModuleType(string moduleStr)
    {
        return moduleStr.ToUpperInvariant() switch
        {
            "DPS" => ModuleType.DPS,
            "PMU" => ModuleType.PMU,
            "PPMU" => ModuleType.PPMU,
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

    private static string FormatPpmuTestItem(string section, string? pinColumn)
    {
        var modeMatch = Regex.Match(pinColumn ?? string.Empty, @"\(([^)]+)\)");
        if (!modeMatch.Success) return section;

        var mode = modeMatch.Groups[1].Value.Trim().ToUpperInvariant();
        var operation = mode switch
        {
            "FV" => "Force Voltage",
            "MV" => "Measure Voltage",
            "FI" => "Force Current",
            "MI" => "Measure Current",
            _ => mode
        };
        return section + ", " + operation;
    }

    private static string NormalizeCoefficientName(string name) =>
        name.Trim().Replace("'s", string.Empty, StringComparison.OrdinalIgnoreCase);

    private static void AddOrUpdateCalibrationCoefficient(ParseResult result, CalibrationCoefficient coefficient)
    {
        var existingIndex = result.CalibrationCoefficients.FindIndex(existing =>
            existing.LoopIndex == coefficient.LoopIndex &&
            existing.ModuleType == coefficient.ModuleType &&
            existing.ChannelId == coefficient.ChannelId &&
            existing.CalibrationItem.Equals(coefficient.CalibrationItem, StringComparison.OrdinalIgnoreCase) &&
            existing.CoefficientName.Equals(coefficient.CoefficientName, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
            result.CalibrationCoefficients[existingIndex] = coefficient;
        else
            result.CalibrationCoefficients.Add(coefficient);
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
            // Strip trailing comma to avoid phantom empty field in split
            var trimmedLine = line.Trim().TrimStart(',');
            if (trimmedLine.EndsWith(','))
                trimmedLine = trimmedLine.TrimEnd(',');
            var parts = trimmedLine.Split(',');
            if (parts.Length < columnNames.Length)
                return null;

            // Filter out empty entries (from multiple consecutive commas)
            var filteredParts = parts.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).ToArray();
            if (filteredParts.Length < columnNames.Length)
                return null;
            parts = filteredParts;

            double expect, measure, lowLimit, upLimit, difference;
            int actualChannel = channelId;

            bool hasPin = columnNames.Length > 0 && columnNames[0].StartsWith("Pin", StringComparison.OrdinalIgnoreCase);

            // Find ADC measure column position in headers (-1 if not present)
            int adcCol = -1;
            if (!hasPin)
            {
                for (int i = 0; i < columnNames.Length; i++)
                {
                    if (columnNames[i].Contains("AdcMeasure", StringComparison.OrdinalIgnoreCase))
                    { adcCol = i; break; }
                }
            }

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
            else if (columnNames.Length >= 5 && adcCol >= 0 && adcCol < columnNames.Length - 3)
            {
                // ADC column present: columns are [expect, measure, ...adc..., lowLimit, upLimit, difference]
                int lowIdx = adcCol + 1;
                int upIdx = adcCol + 2;
                int diffIdx = adcCol + 3;
                if (diffIdx >= columnNames.Length || parts.Length < diffIdx + 1)
                    return null;
                expect = ParseDouble(parts[0]);
                measure = ParseDouble(parts[1]);
                lowLimit = ParseDouble(parts[lowIdx]);
                upLimit = ParseDouble(parts[upIdx]);
                difference = ParseDouble(parts[diffIdx]);
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
    public List<CalibrationCoefficient> CalibrationCoefficients { get; set; } = new();
}

public record ParseProgress(int Percent, int LinesRead, int DataPoints);
