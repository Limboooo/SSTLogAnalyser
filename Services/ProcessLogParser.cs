using System.Globalization;
using System.Text.RegularExpressions;
using SSTLogAnalyser.Models;

namespace SSTLogAnalyser.Services;

public sealed class ProcessLogParser
{
    private DiagnosticLogLine? _fftResult;
    private int? _fftSlot;
    private string _fftStatus = "Unknown";
    private bool _inFft;
    private string _tmuPhase = string.Empty;
    private double? _tmuFrequency, _tmuEcLow, _tmuEcHigh;
    private const string Number = @"[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[Ee][-+]?\d+)?";
    public IEnumerable<DiagnosticObservation> Parse(string message, string timestamp, int lineNumber, int loopIndex)
    {
        var source = new DiagnosticLogLine(message, timestamp, lineNumber, loopIndex);
        var line = source.Message;
        string? tmuSection = line switch
        {
            "Start TMU Calibration" => "Clock calibration",
            "Start TMU Verification" => "Clock verification",
            _ when line.Contains("Tmu Path Cal Summary Log", StringComparison.Ordinal) => "Path calibration",
            _ when line.Contains("Start TMU Path Verification", StringComparison.Ordinal) => "Path verification",
            _ => null
        };
        if (tmuSection != null)
        {
            _tmuPhase = tmuSection;
            _tmuFrequency = _tmuEcLow = _tmuEcHigh = null;
            yield return Observation("TMU", "TMU section", tmuSection, null, "", "Unknown");
        }
        if (_tmuPhase.StartsWith("Path verification", StringComparison.Ordinal) && line.Contains("Loop Count [", StringComparison.Ordinal))
            yield return Observation("TMU", "TMU section", "Path verification / " + line.Trim('*', ' '), null, "", "Unknown");
        if (line == "End TMU Calibration") { _tmuPhase = string.Empty; _tmuFrequency = _tmuEcLow = _tmuEcHigh = null; }
        if (_tmuPhase.Length > 0)
        {
            var frequency = Regex.Match(line, @"FEPT\s*=\s*(?<value>" + Number + @")Hz");
            if (frequency.Success) _tmuFrequency = Num(frequency.Groups["value"].Value);
            var ecBounds = Regex.Match(line, @"^Expect EC between\s*(?<low>" + Number + @")ppm\s*~\s*(?<high>" + Number + @")ppm");
            if (ecBounds.Success) { _tmuEcLow = Num(ecBounds.Groups["low"].Value); _tmuEcHigh = Num(ecBounds.Groups["high"].Value); }
        }
        var verificationClock = Regex.Match(line, @"^-+SysClk\s+(?<clock>\d+(?:\.\d+)?)Mhz verification (?<phase>begin|end)-+$", RegexOptions.IgnoreCase);
        if (verificationClock.Success)
        {
            _acVerificationClock = verificationClock.Groups["phase"].Value.Equals("begin", StringComparison.OrdinalIgnoreCase)
                ? verificationClock.Groups["clock"].Value + " MHz" : "Clock unspecified";
            if (verificationClock.Groups["phase"].Value.Equals("begin", StringComparison.OrdinalIgnoreCase))
                yield return Observation("AC", "Verification sweep", _acVerificationClock, null, "", "Unknown");
        }
        if (line.StartsWith("#PinNum", StringComparison.OrdinalIgnoreCase) && line.Contains("Target(ns)") && line.Contains("Result"))
            yield return Observation("AC", "Verification table", _acVerificationClock, null, "", "Unknown");
        var acClock = Regex.Match(line, @"^-+CLK\s+(?<clock>\d+(?:\.\d+)?)MHz Verify result-+$");
        if (acClock.Success)
        {
            _acClock = acClock.Groups["clock"].Value + " MHz";
            _acEdges = null;
        }
        if (line.StartsWith("----|", StringComparison.Ordinal) && line.Contains("(ns)", StringComparison.Ordinal))
            _acReport = $" [{_acClock} / Report L{lineNumber}]";
        if (line == "========== MIXI FFT ANALYSIS ==========")
        { _inFft = true; _fftResult = null; _fftSlot = null; _fftStatus = "Unknown"; }
        else if (_inFft)
        {
            var verdict = Regex.Match(line, @"^Result\s*:\s*(PASS|FAIL)$");
            var slot = Regex.Match(line, @"^Slot\s*:\s*(\d+)$");
            var channel = Regex.Match(line, @"^DTZ CH\s*:\s*(\d+)$");
            if (verdict.Success) { _fftResult = source; _fftStatus = verdict.Groups[1].Value == "PASS" ? "Pass" : "Fail"; }
            else if (slot.Success) _fftSlot = int.Parse(slot.Groups[1].Value, CultureInfo.InvariantCulture);
            else if (channel.Success && _fftResult != null)
                yield return new DiagnosticObservation { Kind = "Diagnosis", Module = "MIXI", Item = "FFT Analysis",
                    Slot = _fftSlot, Channel = int.Parse(channel.Groups[1].Value, CultureInfo.InvariantCulture), Status = _fftStatus,
                    Timestamp = _fftResult.Timestamp, LineNumber = _fftResult.LineNumber, LoopIndex = _fftResult.LoopIndex,
                    RawMessage = _fftResult.Message };
            else if (!Regex.IsMatch(line, @"^(Metric|Frequency|Waveform (Max|Min|Mean)|SNR|THD)\s*\|") && line.Length > 0)
                _inFft = false;
        }
        var slotAlign = Regex.Match(line, @"^Slot(?<slot>\d+)\s*,\s*(?<status>Pass|\*\*Fail\*\*)\s*$");
        if (slotAlign.Success)
            yield return Observation("AC", "Alignment", "Slot align", null, "", slotAlign.Groups["status"].Value == "Pass" ? "Pass" : "Fail",
                slot: int.Parse(slotAlign.Groups["slot"].Value, CultureInfo.InvariantCulture));
        var register = Regex.Match(line, @"^(?<kind>Channel|Pin)\s*:\s*(?<channel>\d+)(?<details>.*?)\s*--\s*(?<status>Pass|Fail)\s*$", RegexOptions.IgnoreCase);
        if (register.Success)
            yield return Observation("", "Register", register.Groups["details"].Value.Trim().Length == 0 ? "Channel verdict" : register.Groups["details"].Value.Trim(),
                null, "", register.Groups["status"].Value.Equals("Pass", StringComparison.OrdinalIgnoreCase) ? "Pass" : "Fail",
                channel: int.Parse(register.Groups["channel"].Value, CultureInfo.InvariantCulture));
        var clock = Regex.Match(line, @"^(?:(?<status>Pass|Fail)\s*:\s*)?Pin(?<pin>\d+)\s*:\s*FMEA\s*=\s*(?<freq>" + Number + @")Hz.*?EC\s*=\s*(?<ec>" + Number + @")", RegexOptions.IgnoreCase);
        if (clock.Success)
        {
            var status = clock.Groups["status"].Success ? clock.Groups["status"].Value.Equals("Pass", StringComparison.OrdinalIgnoreCase) ? "Pass" : "Fail" : "Unknown";
            var pin = int.Parse(clock.Groups["pin"].Value, CultureInfo.InvariantCulture);
            yield return Observation("TMU", "Clock", "FMEA", Num(clock.Groups["freq"].Value), "Hz", status, channel: pin);
            var bounds = Regex.Match(line, @"Expect EC between\s*(?<low>" + Number + @")ppm\s*~\s*(?<high>" + Number + @")ppm");
            yield return Observation("TMU", "Clock", "EC", Num(clock.Groups["ec"].Value), "ppm", status, channel: pin,
                low: bounds.Success ? Num(bounds.Groups["low"].Value) : _tmuEcLow,
                high: bounds.Success ? Num(bounds.Groups["high"].Value) : _tmuEcHigh, target: _tmuFrequency);
            var cal = Regex.Match(line, @"Calibration Value\s*-\s*(?<value>" + Number + @")ns");
            if (cal.Success) yield return Observation("TMU", "Calibration", "Clock calibration value", Num(cal.Groups["value"].Value), "ns", "Unknown", channel: pin);
        }
        var failure = Regex.Match(line, @"^FAIL - (?<module>TMU|FMU)-(?<channel>\d+) Calibration$");
        if (failure.Success) yield return Observation(failure.Groups["module"].Value, "Calibration", "Calibration", null, "", "Fail",
            channel: int.Parse(failure.Groups["channel"].Value, CultureInfo.InvariantCulture));
        var flash = Regex.Match(line, @"^(?<module>HVPI) channel (?<channel>\d+): Flash (?<item>Write|readback) failed\.$");
        if (flash.Success) yield return Observation(flash.Groups["module"].Value, "Flash", flash.Groups["item"].Value,
            null, "", "Fail", channel: int.Parse(flash.Groups["channel"].Value, CultureInfo.InvariantCulture));
        var calValue = Regex.Match(line, @"^Pin(?<pin>\d+):.*?Cal value:(?<value>" + Number + @")(?:,|$)");
        if (calValue.Success) yield return Observation("AC", "Calibration", "Cal value", Num(calValue.Groups["value"].Value), "ns", "Unknown",
            channel: int.Parse(calValue.Groups["pin"].Value, CultureInfo.InvariantCulture));
        var fields = ExtractTableFields(line);
        if (fields.Count == 0) yield break;
        if (fields.Any(p => p.Key == "Module") && fields.Any(p => p.Key == "Sigma"))
        {
            var sigma = fields.First(p => p.Key == "Sigma").Value;
            if (double.TryParse(sigma, NumberStyles.Float, CultureInfo.InvariantCulture, out var sigmaValue) && double.IsFinite(sigmaValue))
                yield return Observation(fields.First(p => p.Key == "Module").Value, "Statistics", "Sigma", sigmaValue, "", "Unknown",
                    channel: int.TryParse(fields.FirstOrDefault(p => p.Key == "Channel").Value, out var sigmaChannel) ? sigmaChannel : null);
        }
        var section = fields.FirstOrDefault(p => p.Key == "Section").Value;
        var pinField = fields.FirstOrDefault(p => p.Key is "PinNumber" or "Pin").Value;
        if (pinField != null && int.TryParse(pinField.Replace("Pin", "", StringComparison.Ordinal), out var tablePin))
        {
            int index = 0;
            foreach (var field in fields)
            {
                if (!double.TryParse(field.Value.TrimEnd(':', ';', ' '), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value)) continue;
                if (section is "Tmu0 Result" or "Tmu1 Result" && field.Key.Contains("(ns)", StringComparison.Ordinal))
                    yield return Observation("TMU", "Calibration", section + " " + field.Key + " [" + (++index) + "]", value, "ns", "Unknown", channel: tablePin);
                else if (field.Key.Contains("(ns)", StringComparison.Ordinal) &&
                    (field.Key.EndsWith("Max", StringComparison.Ordinal) || field.Key.EndsWith("Min", StringComparison.Ordinal) || field.Key.EndsWith("Range", StringComparison.Ordinal)))
                    yield return Observation("AC", "Summary", field.Key + _acReport, value, "ns", "Unknown", channel: tablePin);
            }
        }
        DiagnosticObservation Observation(string module, string kind, string item, double? value, string unit, string status,
            int? slot = null, int? channel = null, double? low = null, double? high = null, double? target = null) => new() {
                Module = module, Kind = kind, Item = item, Value = value, Unit = unit, Status = status, Slot = slot, Channel = channel,
                LowLimit = low, HighLimit = high, Target = target, Timestamp = source.Timestamp, LineNumber = source.LineNumber,
                LoopIndex = source.LoopIndex, RawMessage = line };
    }
    private static double Num(string text) => double.Parse(text, CultureInfo.InvariantCulture);

    private sealed record DiagnosticLogLine(string Message, string Timestamp, int LineNumber, int LoopIndex);
    private string[]? _columns;
    private string[]? _acEdges;
    private string _acClock = "Clock unspecified";
    private string _acVerificationClock = "Clock unspecified";
    private string _acReport = string.Empty;
    private string _section = string.Empty;

    // Only the AC/TMU/Sigma tables needed by Source Events are decoded here.
    private List<KeyValuePair<string, string>> ExtractTableFields(string message)
    {
        var fields = new List<KeyValuePair<string, string>>();
        var trimmed = message.Trim();
        if (trimmed is "Tmu0 Result" or "Tmu1 Result") { _section = trimmed; return fields; }
        if (trimmed.StartsWith("----|", StringComparison.Ordinal) && trimmed.Contains("(ns)", StringComparison.Ordinal))
        {
            _acEdges = trimmed.Split('|').Skip(1).Where(s => s.Contains("(ns)", StringComparison.Ordinal))
                .Select(s => s.Trim('-').Trim()).ToArray();
            _columns = null;
            return fields;
        }
        if (_acEdges != null && trimmed.Length > 0 && char.IsDigit(trimmed[0]) && trimmed.EndsWith('|'))
        {
            var groups = trimmed.TrimEnd('|').Split('|');
            if (groups.Length == _acEdges.Length)
            {
                fields.Add(new("Pin", groups[0].Split(',')[0]));
                for (int i = 0; i < groups.Length; i++)
                {
                    var cells = groups[i].Split(',').Skip(i == 0 ? 1 : 0).ToArray();
                    if (cells.Length != 3) { fields.Clear(); break; }
                    for (int j = 0; j < 3; j++) fields.Add(new(_acEdges[i] + " " + new[] { "Max", "Min", "Range" }[j], cells[j].Trim()));
                }
            }
            return fields;
        }
        if (trimmed.StartsWith("PinNumber", StringComparison.Ordinal) || trimmed.StartsWith(",Module,", StringComparison.Ordinal))
        {
            _columns = trimmed.TrimStart(',').TrimEnd(',', ';', ' ').Split(',').Select(s => s.Trim()).ToArray();
            if (_columns[0] == "Module") _section = string.Empty;
            return fields;
        }
        if (_columns == null) return fields;
        var row = trimmed.TrimStart(',').TrimEnd(',', ';', ' ').Split(',').Select(s => s.Trim()).ToArray();
        var isRow = _columns[0] == "Module" || (row[0].StartsWith("Pin", StringComparison.Ordinal) && int.TryParse(row[0][3..], out _));
        if (!isRow || row.Length != _columns.Length) { _columns = null; return fields; }
        for (int i = 0; i < _columns.Length; i++) fields.Add(new(_columns[i].TrimEnd(':', ' '), row[i]));
        if (_section.Length > 0) fields.Add(new("Section", _section));
        return fields;
    }
}
