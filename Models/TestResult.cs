namespace SSTLogAnalyser.Models;

public class TestResult
{
    public long FileId { get; set; }
    public int LoopIndex { get; set; }
    public ModuleType ModuleType { get; set; }
    public int SlotNumber { get; set; }
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
}
