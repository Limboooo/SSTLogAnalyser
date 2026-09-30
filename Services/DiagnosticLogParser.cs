using System.Globalization;
using System.Text.RegularExpressions;
using SSTLogAnalyser.Models;

namespace SSTLogAnalyser.Services;

// Each observation is backed by one SST output line. No cross-line limit or result inference.
public static class DiagnosticLogParser
{
    private const string Number = @"[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[Ee][-+]?\d+)?";
    private static readonly Regex Diagnosis = new(@"^(?<module>DPSI|DPS|PMU|PE|PPMU|MVPI|HVPI|MXVI|Line Resistance|PE Leakage|Pin List|Channel List)(?: K)? Diagnosis (?<status>Pass|Fail|pass|fail)\.\s*(?:Slot:\s*(?<slot>\d+)\s*)?(?:Channel:\s*(?<channel>\d+)\s*)?(?:Pin:\s*(?<pin>\d+)\s*)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MxviShort = new(@"^MXVI Short\s+(?<direction>\w+)\s*\|\s*Channel:\s*(?<channel>\d+)\s*\((?<mode>FI|FV)\)\s*\|\s*Status:\s*(?<status>PASS|FAIL)\s*\|", RegexOptions.Compiled);
    private static readonly Regex Leakage = new(@"^(?:I Range\s*=\s*(?<range>\d+)uA\s*,\s*)?PMU channel:\s*(?<channel>\d+) have Leakage of PPMU:\s*(?<value>" + Number + @")(?<tail>.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PinZero = new(@"^\[PPMU_PIN_TO_PIN_ZERO\]\s*Range=(?<range>[^,]+),\s*RefPin=(?<ref>\d+),\s*TargetPin=(?<pin>\d+),\s*PairZero=(?<value>" + Number + @")A", RegexOptions.Compiled);
    private static readonly Regex Resistor = new(@"^Resistor:\s*(?<name>.+?)\s+Value:\s*(?<target>" + Number + @")\s+MeasureValue:\s*(?<value>" + Number + @")\s+Resule:\s*(?<status>\S+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FlashMismatch = new(@"^HVPI channel\s+(?<channel>\d+)\s+(?<item>.+?):\s*Flash M/C mismatch\.\s*Write=\((?<wm>[^,]+),\s*(?<wc>[^)]+)\),\s*Read=\((?<rm>[^,]+),\s*(?<rc>[^)]+)\)", RegexOptions.Compiled);
    private static readonly Regex FftMetric = new(@"^(?<item>Frequency|Waveform Max|Waveform Min|Waveform Mean|SNR|THD)\s*\|\s*(?<value>" + Number + @"|NaN|[-+]?Infinity)\s*(?<unit>Hz|V|dB)\s*\|.+\|\s*(?<status>PASS|FAIL)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool TryParse(string message, string timestamp, int lineNumber, int loopIndex, out DiagnosticObservation observation)
    {
        observation = null!;
        if (string.IsNullOrWhiteSpace(message)) return false;
        var line = message.Trim();
        string kind, module, item, unit = string.Empty, status = "Unknown";
        int? slot = null, channel = null;
        double? value = null, target = null, limit = null, low = null, high = null;
        Match match;

        if ((match = MxviShort.Match(line)).Success)
        {
            kind = "Diagnosis"; module = "MXVI";
            item = "Short " + match.Groups["direction"].Value + " " + match.Groups["mode"].Value;
            channel = Int(match.Groups["channel"]);
            status = Status(match.Groups["status"].Value);
            // The checked quantity is MV for an FI channel, MI for an FV channel.
            var check = Regex.Match(line, @"\|\s*Check\s+(?<unit>MV|MI):\s*(?<value>" + Number + @")\s*\[\s*(?<low>" + Number + @"),\s*(?<high>" + Number + @")\s*\]");
            if (check.Success)
            {
                value = Double(check.Groups["value"]); low = Double(check.Groups["low"]); high = Double(check.Groups["high"]);
                unit = check.Groups["unit"].Value == "MV" ? "V" : "A";
            }
        }
        else if ((match = Diagnosis.Match(line)).Success)
        {
            kind = "Diagnosis"; module = match.Groups["module"].Value.ToUpperInvariant();
            item = module == "LINE RESISTANCE" ? "Line Resistance" : module == "PE LEAKAGE" ? "PE Leakage" : "Diagnosis";
            slot = Int(match.Groups["slot"]); channel = Int(match.Groups["channel"]) ?? Int(match.Groups["pin"]);
            status = Status(match.Groups["status"].Value);
            if (module == "PPMU")
            {
                // Compare status is a bit field, not an analog measurement.
                item = "Compare Status";
            }
            else if (module == "LINE RESISTANCE" || module == "PE LEAKAGE")
            {
                value = Field(line, module == "LINE RESISTANCE" ? @"Calculate Res\s*=" : @"TestResult\s*=");
                unit = "ohm";
                low = Field(line, @"Min Res:"); high = Field(line, @"Max Res:");
                if (module == "LINE RESISTANCE" && !value.HasValue)
                {
                    item = "Line Resistance voltage precheck"; unit = "V";
                    value = Field(line, @"Mesure Volt:"); target = Field(line, @"FV:");
                    low = Field(line, @"Min Volt:"); high = Field(line, @"Max Volt:");
                }
            }
            else
            {
                var current = line.Contains("Measure Current result:", StringComparison.OrdinalIgnoreCase);
                value = Field(line, current ? @"Measure Current result:" : @"(?:Measure result|Mesure result|Mesure Voltage):");
                unit = current ? "A" : "V";
                target = Field(line, @"(?:FV|Force voltage):");
                low = Field(line, @"Min:"); high = Field(line, @"Max:");
            }
        }
        else if ((match = PinZero.Match(line)).Success)
        {
            kind = "Leakage"; module = "PPMU"; item = "Pin-to-pin zero, ref " + match.Groups["ref"].Value + ", " + match.Groups["range"].Value;
            channel = Int(match.Groups["pin"]); value = Double(match.Groups["value"]); unit = "A";
        }
        else if ((match = Leakage.Match(line)).Success)
        {
            kind = "Leakage"; module = "PPMU"; item = match.Groups["range"].Success ? match.Groups["range"].Value + "uA range" : "PPMU leakage";
            channel = Int(match.Groups["channel"]); value = Double(match.Groups["value"]); unit = "A";
            // v1 emits the failure suffix; v2 emits a value only.
            if (match.Groups["tail"].Value.Contains("out of expect range", StringComparison.OrdinalIgnoreCase))
            {
                status = "Fail";
                var bounds = Regex.Match(match.Groups["tail"].Value, @"\((?<low>" + Number + @")\s*=<\s*M\s*<=\s*(?<high>" + Number + @")\)");
                if (bounds.Success) { low = Double(bounds.Groups["low"]); high = Double(bounds.Groups["high"]); }
            }
        }
        else if ((match = Resistor.Match(line)).Success)
        {
            kind = "Resistor"; module = "CAL"; item = match.Groups["name"].Value.Trim();
            target = Double(match.Groups["target"]); value = Double(match.Groups["value"]); unit = "ohm";
            status = Status(match.Groups["status"].Value);
        }
        else if (TryThermal(line, out slot, out channel, out value, out status))
        {
            kind = "Temperature"; module = "PE"; item = "Thermal"; unit = "°C";
        }
        else if ((match = FlashMismatch.Match(line)).Success)
        {
            kind = "Flash"; module = "HVPI"; item = match.Groups["item"].Value.Trim() + " M/C mismatch";
            channel = Int(match.Groups["channel"]); status = "Fail";
        }
        else if (line.StartsWith("MIXI FFT Analysis:", StringComparison.OrdinalIgnoreCase))
        {
            match = Regex.Match(line, @"^MIXI FFT Analysis:\s*(?<status>PASS|FAIL)\s*\|\s*Slot:\s*(?<slot>\d+)\s*\|\s*DTZ Channel:\s*(?<channel>\d+)", RegexOptions.IgnoreCase);
            if (!match.Success) return false;
            kind = "Diagnosis"; module = "MIXI"; item = "FFT Analysis";
            slot = Int(match.Groups["slot"]); channel = Int(match.Groups["channel"]); status = Status(match.Groups["status"].Value);
        }
        else if ((match = FftMetric.Match(line)).Success)
        {
            // Metric lines do not repeat Slot/DTZ Channel. Keep them unknown here.
            kind = "Diagnosis"; module = "MIXI"; item = "FFT " + match.Groups["item"].Value;
            value = Double(match.Groups["value"]); unit = match.Groups["unit"].Value; status = Status(match.Groups["status"].Value);
            var specification = line.Split('|')[2].Trim();
            var targetMatch = Regex.Match(specification, @"^(?<target>" + Number + @")\s*(?:V|Hz)");
            if (targetMatch.Success) target = Double(targetMatch.Groups["target"]);
            var tolerance = Regex.Match(specification, @"\+/-\s*(?<limit>" + Number + @")\s*V");
            if (tolerance.Success && target.HasValue)
            { limit = Double(tolerance.Groups["limit"]); low = target - limit; high = target + limit; }
            var bound = Regex.Match(specification, @"^(?<operator>>=|<=)\s*(?<value>" + Number + @")\s*dB");
            if (bound.Success)
            {
                if (bound.Groups["operator"].Value == ">=") low = Double(bound.Groups["value"]);
                else high = Double(bound.Groups["value"]);
            }
        }
        else return false;

        observation = new DiagnosticObservation
        {
            Kind = kind, Module = module, Slot = slot, Channel = channel, Item = item, Value = value,
            Unit = unit, Target = target, Limit = limit, LowLimit = low, HighLimit = high, Status = status,
            Timestamp = timestamp, LineNumber = lineNumber, LoopIndex = loopIndex, RawMessage = message
        };
        return true;
    }

    private static bool TryThermal(string line, out int? slot, out int? pin, out double? temperature, out string status)
    {
        slot = pin = null; temperature = null; status = "Unknown";
        var cells = line.Split(',');
        if (cells.Length < 6 || cells[0].Trim().Length != 0 ||
            (cells[5].Trim().Length != 0 && !cells[5].Contains("Warning!!", StringComparison.OrdinalIgnoreCase))) return false;
        if (!int.TryParse(cells[1].Trim(), out var parsedSlot) || !int.TryParse(cells[2].Trim(), out _) ||
            !int.TryParse(cells[3].Trim(), out var parsedPin) || !double.TryParse(cells[4].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedTemp)) return false;
        slot = parsedSlot; pin = parsedPin; temperature = parsedTemp;
        // The warning is explicit; an empty cell contains no recorded result.
        status = cells[5].Contains("Warning!!", StringComparison.OrdinalIgnoreCase) ? "Fail" : "Unknown";
        return true;
    }

    private static int? Int(Group group) => group.Success && int.TryParse(group.Value, out var value) ? value : null;
    private static double? Double(Group group) => group.Success && double.TryParse(group.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;
    private static double? Field(string line, string label)
    {
        var match = Regex.Match(line, label + @"\s*(?<number>" + Number + @")", RegexOptions.IgnoreCase);
        return Double(match.Groups["number"]);
    }
    private static string Status(string raw) => raw.Equals("pass", StringComparison.OrdinalIgnoreCase) || raw.Equals("true", StringComparison.OrdinalIgnoreCase) ? "Pass" :
        raw.Equals("fail", StringComparison.OrdinalIgnoreCase) || raw.Equals("false", StringComparison.OrdinalIgnoreCase) ? "Fail" : "Unknown";
}
