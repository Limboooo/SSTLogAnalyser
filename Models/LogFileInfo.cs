namespace SSTLogAnalyser.Models;

public class LogFileInfo
{
    public long FileId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime ParseTime { get; set; }
    public int LoopCount { get; set; }
    public string ToolVersion { get; set; } = string.Empty;
}
