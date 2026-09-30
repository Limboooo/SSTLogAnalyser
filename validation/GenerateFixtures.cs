using System.Globalization;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;

static class GenerateFixtures
{
    public static void Write(string directory)
    {
        Directory.CreateDirectory(directory);
        // A source edit must be reviewed before updating fixture shapes. Fail early
        // if the actual producer no longer contains the format being simulated.
        var sourceMarkers = new Dictionary<string, string[]> {
            ["MainWindow.xaml.cs"] = new[] { "DPS Channel ", "V, -10, 15, Force Voltage Verification", "AutoTest Result Summary", "3.1) DPS DC Verification" },
            ["Model/DcCal.cs"] = new[] { "DmmMeasure", "MvpiAdcMeasure", "DPS FV's Gain:", "have Leakage of PPMU:", "[PPMU_PIN_TO_PIN_ZERO]" },
            ["Model/DcDiag.cs"] = new[] { "DPSI Diagnosis Pass. Slot:", "DPSI Diagnosis Fail. Slot:" },
            ["Model/ACCalClass.cs"] = new[] { "#PinNum", "Target(ns),Spec(ns), MaxError(ns),Result" },
            ["Model/TmuCalClass.cs"] = new[] { "Start TMU Path Verification", "Tmu0_VOLStart_VOLStop,", "false;" },
            ["Model/Mixi/MixiUtility.cs"] = new[] { "[WAVE:{7} OFFSET:{8}]", "Meas-Target:{5}" },
            ["Model/Mixi/MixiVer.cs"] = new[] { "AWG CH ", "LogForceMeasureValue(\"--POS--", "LogForceMeasureValue(\"--NEG--" },
            ["Model/ThermalDetect.cs"] = new[] { "Warning!!" },
            ["Model/FlashAccess.cs"] = new[] { "Flash M/C mismatch. Write=" }
        };
        var hashes = new Dictionary<string, string>();
        foreach (var (relativePath, markers) in sourceMarkers)
        {
            var sourcePath = Path.Combine(Trunk, relativePath);
            var source = File.ReadAllText(sourcePath);
            foreach (var marker in markers)
                if (!source.Contains(marker, StringComparison.Ordinal))
                    throw new InvalidOperationException("Review changed Trunk format: " + relativePath + " / " + marker);
            hashes[relativePath] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath)));
        }
        void Save(string name, IEnumerable<string> lines) => File.WriteAllText(
            Path.Combine(directory, name), string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
        string Log(string message, string level = "INFO") => $"2026-09-30 10:00:00,000 {level}  - {message}";

        // DcCal.cs: DC verification CSV, including the separate MVPI ADC table.
        var dc = new List<string> { Log("Index 1 of 2") };
        foreach (var (module, item) in new[] {
            ("DPS", "V, -3, 8, Force Voltage"), ("DPSI", "V, -3, 8, Force Voltage"),
            ("PMU", "V, -3, 8, Force Voltage"), ("MVPI", "V, -10, 15, Force Voltage"),
            ("MDM", "V, 1.0V, Measure Voltage"), ("HVPI", "V, -40, 40, Force Voltage"),
            ("HDM", "V, 1.0V, Measure Voltage"), ("MXVI", "V, -25, 25, Force Voltage") })
        {
            dc.Add(Log($"{module} Channel 200: {item} Verification"));
            dc.Add(Log(",    Expect,          DmmMeasure,  LowLimit,   UpLimit,               Difference,"));
            dc.Add(Log(",0,0.0002,-0.001,0.001,0.0002,"));
            dc.Add(Log(",1,1.002,-0.001,0.001,0.002,Failed", "ERROR"));
        }
        foreach (var item in new[] { "VIH Verification", "PPMU FV Verification" })
        {
            dc.Add(Log(item));
            dc.Add(Log(",Pin,Expect,Measure,LowLimit,UpLimit,Difference,"));
            dc.Add(Log(",0,1,1.0002,-0.001,0.001,0.0002,"));
            dc.Add(Log(",1,1,1.002,-0.001,0.001,0.002,Failed", "ERROR"));
        }
        dc.AddRange(new[] { Log("MVPI Channel 201: V, -10, 15, Measure Voltage"),
            Log(",Expect,DmmMeasure,MvpiAdcMeasure,LowLimit,UpLimit,Difference,"),
            Log(",1,1.0002,1.0003,-0.001,0.001,0.0001,"),
            Log("Index 2 of 2"), Log("Re-test DPS Channel 200: V, -3, 8, Force Voltage Verification"),
            Log(",Expect,DmmMeasure,LowLimit,UpLimit,Difference,"), Log(",1,1.0001,-0.001,0.001,0.0001,") });
        Save("01_DC_LogAll.log", dc);

        // ACCalClass.SummaryReport: six edges, NA, whole-pin verdict.
        Save("02_AC_LogAll.log", new[] { Log("Index 1 of 1"),
            Log("#PinNum\t,CMPH(ns), CMPL(ns), RISE(ns), FALL(ns),ON(ns) ,OFF(ns) ,Target(ns),Spec(ns), MaxError(ns),Result"),
            Log("0\t,10.010,9.990,10.000,10.020,9.980,10.000,10.000,0.100,0.020,Pass,"),
            Log("1\t,10.250,10.000,10.000,10.000,10.000,10.000,10.000,0.100,0.250,Fail,", "ERROR"),
            Log("2\t,NA,NA,NA,NA,NA,NA,10.000,0.100,10.000,NA,") });

        // TmuCalClass: passing rows have no explicit result; failing rows append false;.
        Save("03_TMU_LogAll.log", new[] { Log("Index 1 of 1"),
            Log("**********Start TMU Path Verification**********"),
            Log("Expect error between -0.1ns ~ 0.1ns "), Log("Ref TestTime ActualValue = 10ns ,"),
            Log("PinNum,Tmu0_VOLStart_VOLStop,Tmu0_VOHStart_VOHStop,Tmu0_VOHStart_VOLStop,Tmu0_VOLStart_VOHStop,Tmu1_VOLStart_VOLStop,Tmu1_VOHStart_VOHStop,Tmu1_VOHStart_VOLStop,Tmu1_VOLStart_VOHStop,VerifyResult,"),
            Log("0000, 10.010, 9.990, 10.000, 10.000, 10.020, 10.000, 10.000, 10.000, "),
            Log("0001, 10.250, 10.000, 10.000, 10.000, 10.000, 10.000, 10.000, 10.000, false;", "ERROR") });

        // DcDiag and DcCal leakage: preserve misspellings, bounds and missing verdicts.
        Save("04_Events_LogAll.log", new[] { Log("Index 1 of 1"),
            Log("DPSI Diagnosis Pass. Slot: 2 Channel: 200 FV: 1 Measure result: 1.0002"),
            Log("DPSI Diagnosis Fail. Slot: 2 Channel: 201 FV: 1 Measure result: 1.02", "ERROR"),
            Log("PMU channel: 0 have Leakage of PPMU: 3E-08 out of expect range (-1E-08 =< M <= 1E-08)."),
            Log("I Range=2uA , PMU channel: 1 have Leakage of PPMU: 2E-09"),
            Log("[PPMU_PIN_TO_PIN_ZERO] Range=2uA, RefPin=0, TargetPin=1, PairZero=1E-09A, InactivePins=DISCONNECTED"),
            Log(",2,0,256,35,"), Log(",2,0,258,85, Warning!!"),
            Log("HVPI channel 200 FV: Flash M/C mismatch. Write=(1.0001, -0.0002), Read=(1, 0)") });

        // Coefficient-only CAL intentionally contains no verification points.
        Save("05_CAL_Only_LogAll.log", new[] { Log("Index 1 of 1"),
            Log("DPS Channel 200: V, -3, 8, Force Voltage Calibration"),
            Log("DPS FV's Gain: 1.0001, Offset: -0.0002"),
            Log("PPMU FIMI I1_2uA Calibration"), Log("PE Pin: 0 PPMU FIMI I1_2uA Calibration Gain: 0.9999, Offset: 1E-09") });

        // MixiUtility.LogForceMeasureValue F10 format. Analytical error injected;
        // coefficients below are ground truth, not inferred from current parser output.
        var mixi = new List<string>();
        foreach (var loop in new[] { 1, 2 })
        {
            mixi.Add(Log($"Index {loop} of 2"));
            foreach (var channel in new[] { 0, 1, 2, 3, 4 })
                foreach (var path in new[] { "POS", "NEG" })
                {
                    mixi.Add(Log($"AWG CH {channel} AWG_VER_HR_1K5_WAVE_DAC_OFFSET_DAC_{path}"));
                    int index = 0;
                    foreach (var wave in new[] { 0.5, 1.0 })
                        foreach (var offset in new[] { -0.25, 0.25 })
                        {
                            var signedWave = path == "NEG" ? -wave : wave;
                            var target = signedWave + offset;
                            var error = channel switch
                            {
                                0 => 0,
                                1 => signedWave * 0.002 * loop,
                                2 => offset * 0.008 * loop,
                                3 => 0.0015 * loop,
                                _ => (index++ % 2 == 0 ? 1 : -1) * (wave == 0.5 ? 0.002 : -0.002)
                            };
                            mixi.Add(Log(WaveLine(path, target, wave, offset, target + error)));
                        }
                }
        }
        Save("06_MIXI_Attribution_LogAll.log", mixi);

        // Unsupported fitting conditions use the same SST message shape: too few
        // points, collinear settings, missing sibling, and inconsistent composition.
        var sparse = new List<string> { Log("Index 1 of 1") };
        foreach (var channel in new[] { 10, 11, 12 })
        {
            sparse.Add(Log($"AWG CH {channel} AWG_VER_HR_1K5_WAVE_DAC_OFFSET_DAC_POS"));
            foreach (var wave in channel == 10 ? new[] { 0.5, 1.0 } : new[] { 0.5, 1.0, 1.5, 2.0 })
                sparse.Add(Log(WaveLine("POS", channel == 12 ? 99 : wave * 2, wave, wave, wave * 2 + 0.001)));
        }
        Save("07_MIXI_Insufficient_LogAll.log", sparse);

        // MainWindow summary formats and full ASCII banners are extracted from
        // Trunk at generation time, avoiding hand-invented overall verdict text.
        var mainSource = File.ReadAllLines(Path.Combine(Trunk, "MainWindow.xaml.cs"));
        string[] Banner(string marker)
        {
            var first = Array.FindIndex(mainSource, line => line.Contains("OutFile.WriteLine(\"" + marker, StringComparison.Ordinal));
            if (first < 0) throw new InvalidOperationException("Trunk result banner not found: " + marker);
            var lines = new List<string>();
            for (int i = first; i < mainSource.Length && mainSource[i].Contains("OutFile.WriteLine(\"", StringComparison.Ordinal); i++)
            {
                var start = mainSource[i].IndexOf("OutFile.WriteLine(\"", StringComparison.Ordinal) + 19;
                var end = mainSource[i].LastIndexOf("\");", StringComparison.Ordinal);
                lines.Add(mainSource[i][start..end]);
            }
            return lines.ToArray();
        }
        foreach (var (name, marker, status) in new[] {
            ("08_SST_Summary_Pass.txt", "**   PPPPPPP            AAA", "PASSED"),
            ("09_SST_Summary_Fail.txt", "**   FFFFFFFFFF         AAA", "failed"),
            ("10_SST_Summary_Abort.txt", "**         AAA         BBBBBBBB", "Abort") })
        {
            var lines = new List<string>(Banner(marker)) { "",
                "**************** AStar SST Summary *************************",
                "Tester S/N: SIMULATED-TESTER-ONLY", "Operator: SIMULATED",
                "Date: 2026-09-30", "Slot-2: DIGI-2,   S/N:   SIM-DIGI-002",
                "1.1) DPS DC Calibration ........   PASSED",
                "1.4) PPMU DC Calibration .......   Not Test",
                "1.5) Write to Flash ............   Not Applied",
                "3.1) DPS DC Verification ........   " + status };
            if (status == "failed") lines.Add("            Slot-2, DIGI,  (--Fail--)");
            lines.Add("***************** End of AStar SST Summary ******************");
            Save(name, lines);
        }
        Save("11_AUTOTEST_Result.txt", new[] { "***************** AutoTest Result Summary ******************",
            "Item[DIGI_AC_VERIFY_],Result[PASSED]", "Item[DIGI_DC_DIAG_DPS],Result[failed]",
            "***************** End of AutoTest Result Summary ******************" });
        Save("12_AC_Process_LogAll.log", new[] {
            Log("-----CLK 400.000MHz Verify result------"),
            Log("----|-------CMPH(ns)-----|-------CMPL(ns)-----|-------RISE(ns)-----|-------FALL(ns)-----|--------ON(ns)------|--------OFF(ns)-----|"),
            Log("Pin#,-MAX--,-MIN--,-Range|-MAX--,-MIN--,-Range|-MAX--,-MIN--,-Range|-MAX--,-MIN--,-Range|-MAX--,-MIN--,-Range|-MAX--,-MIN--,-Range|"),
            Log("0000,+0.050,-0.010,+0.060|+0.050,-0.010,+0.060|+0.050,-0.010,+0.060|+0.050,-0.010,+0.060|+0.050,-0.010,+0.060|+0.050,-0.010,+0.060|"),
            Log("0001,------,------,------|------,------,------|------,------,------|------,------,------|------,------,------|------,------,------|"),
            Log("Slot1  ,Pass"), Log("Slot2  ,**Fail**"),
            Log("Pin0: 0.100,0.120,Cal value:0.110,"), Log("Pin1: NA,Cal value:NA,"),
            Log("ALIGN_CMPH:"), Log("Pin#,\t10.000,\t10.100,\tSearch Result,ChangeP "),
            Log("Pin0\t,\t*-Found Change Point,\t10.100,\t") });
        Save("13_TMU_Cal_LogAll.log", new[] {
            Log("******************************Tmu Path Cal Summary Log******************************"),
            Log("Tmu0 Result"),
            Log("PinNumber , VolStart_VolStop(ns) , VohStart_VohStop(ns) , VolPath(ns) , VohPath(ns) , VolStart_VolStop(ns) , VolStart_VohStop(ns) , VohStart_VolStop(ns) , VolPinDiff(ns) , VohPinDiff(ns) :"),
            Log("Pin0000 , 10.010 , 9.990 , 10.000 , 10.020 , 0.010 , -0.010 , 0.000 , 0.020 , -0.020 ; "),
            Log("Tmu1 Result"),
            Log("Pin0000 , 10.020 , 9.980 , 10.010 , 10.030 , 0.020 , -0.020 , 0.010 , 0.030 , -0.030 ; "),
            Log("Pin0000 : FMEA = 1000000Hz : Calibration Value - 0.078ns ,EC = 12ppm"),
            Log("Pass : Pin0001 : FMEA = 1000000Hz ;EC = 12"),
            Log("Fail : Pin0002 : FMEA = 1000000Hz ;EC = 120ppm,Expect EC between -50ppm ~ 50ppm"),
            Log("FAIL - FMU-02 Calibration") });
        Save("14_Register_LogAll.log", new[] {
            Log("======= Start DPS Register Diagnostics ======"),
            Log("Channel : 0 -- Pass"),
            Log("Channel : 1 Addr : 0x00000530 Indata Index : 0x00000002 -- Fail"),
            Log(" Addr : 0x00000530 Write Data: 0x00000002 Read Data : 0x00000001 Mask : 0xFFFFFFFF", "DEBUG"),
            Log("======= End DPS Register Diagnostics ======"),
            Log("HVPI channel 200: Flash Write failed."), Log("HVPI channel 201: Flash readback failed.") });
        Save("15_Robustness_LogAll.log", new[] {
            Log("DPS Channel 0: V, -3, 8, Force Voltage Verification"),
            Log(",Expect,DmmMeasure,LowLimit,UpLimit,Difference,"),
            Log(",1,NOT_A_NUMBER,-0.001,0.001,0,"),
            Log(",1,NaN,-0.001,0.001,0,"),
            Log(",1,Infinity,-0.001,0.001,0,"),
            Log("unrecognized future module payload αβ 中文", "WARN"),
            "  continuation preserves leading spaces  ", "", "  ",
            Log("ERROR is a log level, not a verdict", "ERROR"),
            Log("unknown field Slot=2; NewCounter=123; flags=0b10", "DEBUG") });
        Save("16_Diagnosis_Variants_LogAll.log", new[] {
            Log("Pin List Diagnosis Fail. Pin: 0 Force voltage: 1 Min: 0.9 Max: 1.1 Mesure Voltage: 1.2"),
            Log("Channel List Diagnosis Pass. Slot: 2 Channel: 200 FV: 1 Measure result: 1.0001"),
            Log("Line Resistance Diagnosis Fail. Pin: 1, 0 FV: 1 Min Volt: 0.9 Max Volt: 1.1 Mesure Volt: 1.2"),
            Log("Line Resistance Diagnosis Pass. Pin: 1, 0 Calculate Res= 2ohm; Min Res: 1ohm Max Res: 3ohm FV: 1V MV: (1V, 0V) MI: 0.5A"),
            Log("PE Leakage Diagnosis Pass. Pin: 0,TestResult =1000000 ohm; MI = (1E-09A,2E-09A)"),
            Log("PPMU Diagnosis fail. Pin: 0 Force Voltage: 1 Expect value : 0b11 Compare status : 0b10") });
        Save("18_Sigma_Report.log", new[] {
            "***************** Sigma Report Result Summary ******************", "Instrument: DIGITAL_SIM-SN",
            ",Module,Channel,Target,Count,Sigma", ",DPS,0,1,10,0.0002", ",PMU,1,1,10,0.0003",
            "***************** End of Sigma Report Result Summary ******************",
            "0.9998", "1.0000", "1.0002" });
        Save("19_MIXI_FFT_LogAll.log", new[] {
            Log("========== MIXI FFT ANALYSIS =========="), Log("Result    : FAIL"),
            Log("Slot      : 2"), Log("DTZ CH    : 200"),
            Log("Metric | Measured | Target / Limit | Result"),
            Log("Frequency | 1000 Hz | 1000 Hz, Error 0% / Limit 1% | PASS"),
            Log("Waveform Max | 1.01 V | 1 V +/- 0.02 V | PASS"),
            Log("Waveform Min | -1.01 V | -1 V +/- 0.02 V | PASS"),
            Log("Waveform Mean | NaN V | 0 V +/- 0.01 V | FAIL"),
            Log("SNR | 80 dB | >= 70 dB | PASS"), Log("THD | -40 dB | <= -60 dB | FAIL") });
        Save("20_MXVI_Short_Resistor_LogAll.log", new[] {
            Log(string.Format(CultureInfo.InvariantCulture,
                "MXVI Short {0,-7} | Channel: {1,3} ({2,2}) | Status: {3,-4} | Target FV: {4,14:F8}, FI: {5,14:F8} | Check {6,2}: {7,14:F8} [{8,14:F8}, {9,14:F8}] | MV: {10,14:F8} | MI: {11,14:F8}",
                "Forward", 200, "FI", "PASS", 1.0, 0.01, "MV", 1.0001, 0.99, 1.01, 1.0001, 0.01)),
            Log(string.Format(CultureInfo.InvariantCulture,
                "MXVI Short {0,-7} | Channel: {1,3} ({2,2}) | Status: {3,-4} | Target FV: {4,14:F8}, FI: {5,14:F8} | Check {6,2}: {7,14:F8} [{8,14:F8}, {9,14:F8}] | MV: {10,14:F8} | MI: {11,14:F8}",
                "Reverse", 201, "FV", "FAIL", 1.0, 0.01, "MI", -0.02, -0.011, -0.009, 1.0, -0.02)),
            Log("Resistor: R2 Value: 2 MeasureValue: 2.001 Resule: Pass"),
            Log("Resistor: R100 Value: 100 MeasureValue: 110 Resule: Fail") });
        // Real 2026-02-04 LogAll uses a -3..8 V sweep, with error limits
        // +/- (0.001 + abs(Expect) * 0.0005). Values here remain synthetic.
        var sweep = new List<string>();
        foreach (var loop in new[] { 1, 2 })
        {
            sweep.Add(Log($"Index {loop} of 2"));
            foreach (var channel in new[] { 0, 1 })
            {
                sweep.Add(Log($"DPS Channel {channel}: V, -3, 8, Force Voltage Verification"));
                sweep.Add(Log(",    Expect,          DmmMeasure,  LowLimit,   UpLimit,               Difference,"));
                foreach (var expect in Enumerable.Range(-3, 12))
                {
                    var limit = 0.001 + Math.Abs(expect) * 0.0005;
                    var failed = loop == 2 && channel == 1 && (expect == -3 || expect == 8);
                    var difference = failed ? Math.Sign(expect) * limit * 1.2 : (expect - 2) * 0.00002 + channel * 0.00005;
                    sweep.Add(Log(string.Format(CultureInfo.InvariantCulture,
                        ",{0,10},{1,20:G17},{2,12:G17},{3,12:G17},{4,24:G17},{5}",
                        expect, expect + difference, -limit, limit, difference, failed ? "Failed" : ""), failed ? "ERROR" : "INFO"));
                }
            }
        }
        Save("21_DPS_Sweep_LogAll.log", sweep);
        var secondLoop = sweep.FindIndex(p => p.EndsWith("Index 2 of 2", StringComparison.Ordinal));
        Save("22_Comparison_Baseline.log", sweep.Take(secondLoop).Select(p => p.Replace("Index 1 of 2", "Index 1 of 1", StringComparison.Ordinal)));
        Save("23_Comparison_Current.log", sweep.Skip(secondLoop).Select(p => p.Replace("Index 2 of 2", "Index 1 of 1", StringComparison.Ordinal)));
        File.WriteAllText(Path.Combine(directory, "SOURCE_HASHES.json"), JsonSerializer.Serialize(hashes,
            new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
    }

    public static string Trunk = @"D:\1_SVN\SST\Trunk\S200VerCalDia";
    static string WaveLine(string path, double target, double wave, double offset, double measure) =>
        string.Format(CultureInfo.InvariantCulture,
            "{0} --{1}--\t Target:{2:F10} [WAVE:{3:F10} OFFSET:{4:F10}] Meas:{5:F10} LowLimit:{6:F10} HighLimit:{7:F10} Meas-Target:{8:F10} ",
            Math.Abs(measure - target) > 0.001 ? "***Error***" : "", path,
            target, wave, offset, measure, target - 0.001, target + 0.001, measure - target);
}
