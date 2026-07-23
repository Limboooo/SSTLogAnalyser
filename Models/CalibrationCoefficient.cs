namespace SSTLogAnalyser.Models;

public class CalibrationCoefficient
{
    public long FileId { get; set; }
    public int LoopIndex { get; set; }
    public ModuleType ModuleType { get; set; }
    public int ChannelId { get; set; }
    public string CalibrationItem { get; set; } = string.Empty;
    public string CoefficientName { get; set; } = string.Empty;
    public double Gain { get; set; }
    public double Offset { get; set; }
    public int LineNumber { get; set; }

    public string DisplayName => CalibrationItem + " | " + CoefficientName;
}
