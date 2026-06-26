namespace SSTLogAnalyser.Models;

public class DeviceInfo
{
    public long FileId { get; set; }
    public int GroupNumber { get; set; }
    public int LocationId { get; set; }
    public string SlotInfo { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
}
