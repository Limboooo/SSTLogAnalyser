namespace SSTLogAnalyser.Models;

public enum SummaryStatus { Unknown, NotTested, NotApplicable, Passed, Failed, Aborted }

public sealed class SummarySource
{
    public int LineNumber { get; init; }
    public string Text { get; init; } = string.Empty;
}

public sealed class SummaryField
{
    public string Value { get; init; } = string.Empty;
    public SummarySource Source { get; init; } = new();
}

public sealed class SummaryItem
{
    public string Section { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string RawStatus { get; init; } = string.Empty;
    public SummaryStatus Status { get; init; }
    public SummarySource Source { get; init; } = new();
    public List<SummaryFailedSlot> FailedSlots { get; } = new();
}

public sealed class SummaryFailedSlot
{
    public string Slot { get; init; } = string.Empty;
    public string BoardType { get; init; } = string.Empty;
    public SummarySource Source { get; init; } = new();
}

public sealed class SummaryBoard
{
    public string Slot { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string SerialNumber { get; init; } = string.Empty;
    public SummarySource Source { get; init; } = new();
}

public sealed class RunSummary
{
    public string SourceFile { get; init; } = string.Empty;
    public bool IsAutoTest { get; internal set; }
    public SummaryStatus OverallStatus { get; internal set; } = SummaryStatus.Unknown;
    public SummarySource? OverallStatusSource { get; internal set; }
    public string? TesterFamily { get; internal set; }
    public Dictionary<string, SummaryField> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<SummaryBoard> Boards { get; } = new();
    public List<SummaryItem> Items { get; } = new();
}
