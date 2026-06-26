namespace SSTLogAnalyser.Models;

public class ErrorLogEntry
{
    public long FileId { get; set; }
    public int LoopIndex { get; set; }
    public string Timestamp { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int LineNumber { get; set; }
}
