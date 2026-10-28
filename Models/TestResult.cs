namespace SSTLogAnalyser.Models;

public class TestResult
{
    public long FileId { get; set; }
    public int LoopIndex { get; set; }
    public int TestSequence { get; set; } = 1;
    public ModuleType ModuleType { get; set; }
    public int SlotNumber { get; set; } = -1;
    public int ChannelId { get; set; }
    public string TestItemName { get; set; } = string.Empty;
    public double ExpectValue { get; set; }
    public double MeasureValue { get; set; }
    public double LowLimit { get; set; }
    public double UpLimit { get; set; }
    public double Difference { get; set; }
    public bool IsFailed { get; set; }
    public bool IsReTest { get; set; }
    public int LineNumber { get; set; }
    public string Timestamp { get; set; } = string.Empty;
    public double? AdcMeasureValue { get; set; }
    public bool HasExplicitResult { get; set; }
    public double? WaveValue { get; set; }
    public double? OffsetValue { get; set; }
    public double? DiffValue { get; set; }
    public string ComponentType { get; set; } = string.Empty;
}
