namespace SSTLogAnalyser.Models;

public class SlotBoardInfo
{
    public long FileId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int SlotNumber { get; set; }
    public string SlotInfo { get; set; } = string.Empty;
    public string SlotType { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Revision { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string CalDate { get; set; } = string.Empty;
    public int LineNumber { get; set; }
}

public record SlotBoardSummary(string FileName, List<SlotBoardSummaryEntry> Slots);
public record SlotBoardSummaryEntry(string SlotInfo, string BoardTypes);
