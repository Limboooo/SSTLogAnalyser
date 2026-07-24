namespace SSTLogAnalyser.Models;

public class ToleranceCell
{
    public int ChannelId { get; set; }
    public string TestItemName { get; set; } = string.Empty;
    public double Utilization { get; set; }
    public bool IsFailed { get; set; }
}
