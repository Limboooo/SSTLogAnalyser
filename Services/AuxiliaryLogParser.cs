using System.Globalization;
using System.Text.RegularExpressions;
using SSTLogAnalyser.Models;

namespace SSTLogAnalyser.Services;

// These tables use channel/level columns rather than the ordinary Expect CSV layout.
internal sealed class AuxiliaryLogParser
{
    private static readonly Regex Title = new(@"^(Re-test )?TMUVI Channel (\d+): (.+ Level) (Calibration|Verification)$", RegexOptions.Compiled);
    private static readonly Regex Coefficient = new(@"^TMUVI\s+(\d+)\s*:\s*(CVA|CVB) Gain:\s*([\d.Ee+-]+)\s*,\s*Offset:\s*([\d.Ee+-]+)", RegexOptions.Compiled);
    private string? _phase, _table;
    private string _item = "";
    private bool _retry;
    private int _headerLine;
    private int _channel;
    private string _module = "";
    private string[] _calibrationColumns = Array.Empty<string>();
    private readonly Dictionary<(int Loop, int Channel, string Item), (int Header, int Sequence)> _sequences = new();

    public void Reset() { _phase = _table = null; _sequences.Clear(); }

    public bool Parse(string message, string timestamp, int line, int loop, ParseResult result)
    {
        if (_phase == null && !message.EndsWith("Calibration", StringComparison.Ordinal) &&
            !message.Contains("TMUVI Channel", StringComparison.Ordinal)) return false;
        var title = Title.Match(message);
        if (title.Success)
        {
            _phase = "TMUVI " + title.Groups[4].Value;
            _item = "TMUVI " + title.Groups[3].Value;
            _retry = title.Groups[1].Success;
            _channel = int.Parse(title.Groups[2].Value);
            _table = null;
            return true;
        }
        var cmTitle = Regex.Match(message, @"^(MVPI|HVPI) Channel (\d+): (.+, CM|MICM,.*) Calibration$");
        if (cmTitle.Success)
        {
            _phase = cmTitle.Groups[1].Value + " CM Calibration";
            _item = cmTitle.Groups[3].Value; _channel = int.Parse(cmTitle.Groups[2].Value);
            _table = null;
            return false; // Preserve the main parser's calibration/coefficient scope.
        }
        var dcCalibration = Regex.Match(message, @"^(DPS|DPSI|PMU|MVPI|MDM|HVI|HVPI|MXVI) Channel (\d+): (.+) Calibration$");
        if (dcCalibration.Success)
        {
            _phase = "DC Calibration"; _module = dcCalibration.Groups[1].Value;
            _channel = int.Parse(dcCalibration.Groups[2].Value); _item = dcCalibration.Groups[3].Value;
            _table = null;
            return false;
        }
        if (Regex.IsMatch(message, @"^(Re-test )?\w+(?: TMU)? Channel \d+: .+ (Calibration|Verification)$") ||
            Regex.IsMatch(message, @"^PE (HV|VIHH|VILH) (Calibration|Verification)$") ||
            Regex.IsMatch(message, @"^Channel:\s*\d+\s+ItemName:.*Verification(?:VI|AWG)?$") ||
            message.StartsWith("Finish to call", StringComparison.Ordinal))
        {
            _phase = message.StartsWith("HDM Channel", StringComparison.Ordinal) && message.EndsWith("Calibration", StringComparison.Ordinal)
                ? "HDM Calibration" : message == "PE HV Calibration" ? "PE HV Calibration"
                : message is "PE VIHH Calibration" or "PE VILH Calibration" ? "PE Level Calibration" : null;
            _item = _phase == "PE Level Calibration" ? message : _phase ?? "";
            _table = null;
        }
        if (_phase == null) return false;
        var coefficient = Coefficient.Match(message);
        if (coefficient.Success)
        {
            result.CalibrationCoefficients.Add(new CalibrationCoefficient
            {
                LoopIndex = loop, ModuleType = ModuleType.MVPI, ChannelId = int.Parse(coefficient.Groups[1].Value),
                CalibrationItem = _item, CoefficientName = "TMUVI " + coefficient.Groups[2].Value,
                Gain = double.Parse(coefficient.Groups[3].Value, CultureInfo.InvariantCulture),
                Offset = double.Parse(coefficient.Groups[4].Value, CultureInfo.InvariantCulture), LineNumber = line
            });
            return true;
        }
        if (!message.StartsWith(',') && !(_table == "HVPI CM Calibration" && Regex.IsMatch(message, @"^[+-]?\d"))) return false;
        var fields = message.Trim().Trim(',', '，').Split(',').Select(s => s.Trim()).ToArray();
        if (_phase == "DC Calibration" && fields.Length >= 5 && fields[0] == "Expect" && fields.Contains("Difference"))
        {
            _calibrationColumns = fields; _table = "DC Calibration";
            return true;
        }
        if (fields.Length > 0 && fields[0] is "TmuVIChannel" or "HVIChannel" or "Pin (FV)" or "Pin" or "ExpectV" or "FV_Value")
        {
            var header = string.Join(",", fields);
            _table = (_phase, header) switch
            {
                ("TMUVI Verification", "TmuVIChannel,Expect_Level,Actual_Value,LowLimit,UpLimit,Difference") => "TMUVI Verification",
                ("TMUVI Calibration", "TmuVIChannel,QA_Level,dTargetValue,Actual_Value") => "TMUVI Calibration",
                ("TMUVI Calibration", "TmuVIChannel,QA_Level,dTargetValue,Actual_Value,Difference") => "TMUVI Calibration",
                ("TMUVI Calibration", "TmuVIChannel,QB_Level,dTargetValue,Actual_Value,Difference") => "TMUVI Calibration",
                ("HDM Calibration", "HVIChannel,TargetValue,HDM_Value,ActualValue,Difference") => "HDM Calibration",
                ("PE HV Calibration", "Pin (FV),HV_VALUE,Measure,LowLimit,UpLimit,Difference") => "PE HV Calibration",
                ("PE Level Calibration", "Pin,Expect,Measure,LowLimit,UpLimit,Difference") => "PE Level Calibration",
                ("MVPI CM Calibration", "ExpectV,MeasureI,LowLimit,UpLimit,Difference") => "MVPI CM Calibration",
                ("HVPI CM Calibration", "FV_Value,MI_CM,MI_ADC,MV_ADC,Low Limit,High Limit") => "HVPI CM Calibration",
                _ => null
            };
            _headerLine = line;
            return _table != null;
        }
        if (_table == null) return false;
        var failed = Regex.IsMatch(message, @"(?:[,，]|\s)\s*Fail(?:ed)?\s*,?$");
        if (failed) fields = Regex.Replace(message, @"(?:[,，]|\s)\s*Fail(?:ed)?\s*,?$", "").Trim().Trim(',', '，').Split(',').Select(s => s.Trim()).ToArray();
        // Legacy HVPI FI calibration omits ADCMeasure from its five-label header,
        // but DcCal writes six numeric columns: target, DMM, ADC, low, high, difference.
        if (_table == "DC Calibration" && _module == "HVPI" && _item.Contains("Force Current", StringComparison.Ordinal) &&
            fields.Length == 6 && _calibrationColumns.SequenceEqual(new[] { "Expect", "DmmMeasure", "LowLimit", "UpLimit", "Difference" }))
            _calibrationColumns = new[] { "Expect", "DmmMeasure", "ADCMeasure", "LowLimit", "UpLimit", "Difference" };
        var count = _table == "DC Calibration" ? _calibrationColumns.Length : _table is "HDM Calibration" or "MVPI CM Calibration" ? 5 : _table == "TMUVI Calibration" ? 4 : 6;
        bool isCmCalibration = _table is "MVPI CM Calibration" or "HVPI CM Calibration" or "DC Calibration";
        var channel = _channel;
        if (fields.Length != count || (!isCmCalibration && !int.TryParse(fields[0], out channel))) return true;
        var values = new double[count];
        for (int i = 0; i < count; i++)
            if (!double.TryParse(fields[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || !double.IsFinite(values[i])) return true;
        if (_table == "DC Calibration")
        {
            var unit = _item.StartsWith("I,", StringComparison.Ordinal) ? "A" : "V";
            for (int i = 1; i < values.Length; i++)
                result.DiagnosticObservations.Add(new DiagnosticObservation
                {
                    Module = _module, Kind = "Calibration measurement", Item = _item + " / " + _calibrationColumns[i],
                    Channel = channel, LoopIndex = loop, Value = values[i], Target = values[0], Unit = unit,
                    Status = "Unknown", Timestamp = timestamp, LineNumber = line, RawMessage = message
                });
        }
        else if (isCmCalibration)
        {
            void ObserveCm(string label, double value, string unit) => result.DiagnosticObservations.Add(new DiagnosticObservation
            {
                Module = _table.StartsWith("MVPI") ? "MVPI" : "HVPI", Kind = "Calibration measurement",
                Item = _item + " / FV=" + values[0].ToString("G10", CultureInfo.InvariantCulture) + " V / " + label,
                Channel = channel, LoopIndex = loop, Value = value, Unit = unit,
                Status = "Unknown", Timestamp = timestamp, LineNumber = line, RawMessage = message
            });
            ObserveCm("Measured current", values[_table.StartsWith("MVPI") ? 1 : 2], "A");
            if (_table.StartsWith("HVPI")) ObserveCm("Measured voltage", values[3], "V");
        }
        else if (_table == "TMUVI Verification")
        {
            var key = (loop, channel, _item);
            _sequences.TryGetValue(key, out var previous);
            int sequence = previous.Header == _headerLine ? previous.Sequence : previous.Sequence + 1;
            _sequences[key] = (_headerLine, sequence);
            result.TestResults.Add(new TestResult
            {
                ModuleType = ModuleType.MVPI, ChannelId = channel, SlotNumber = -1, LoopIndex = loop,
                TestItemName = _item, ExpectValue = values[1], MeasureValue = values[2],
                LowLimit = values[3], UpLimit = values[4], Difference = values[5],
                IsFailed = failed || values[5] < values[3] || values[5] > values[4],
                HasExplicitResult = failed, IsReTest = _retry, TestSequence = sequence,
                Timestamp = timestamp, LineNumber = line
            });
        }
        else
        {
            var module = _table.StartsWith("TMUVI") ? "MVPI" : _table.StartsWith("HDM") ? "HDM" : "PE";
            void Observe(string label, double value) => result.DiagnosticObservations.Add(new DiagnosticObservation
            {
                Module = module, Kind = "Calibration measurement", Item = _item + " / " + label,
                Channel = channel, LoopIndex = loop, Value = value, Target = values[1], Unit = "V",
                Status = "Unknown", Timestamp = timestamp, LineNumber = line, RawMessage = message
            });
            Observe(_table == "HDM Calibration" ? "HDM" : "Measured", values[2]);
            if (_table == "HDM Calibration") Observe("DMM", values[3]);
            // TMUVI's old QA header is mislabeled; the source writes target, DMM, difference.
            Observe("Difference", values[^1]);
        }
        return true;
    }
}
