namespace SSTLogAnalyser.Models;

public sealed class DiagnosticObservation
{
    public string Kind { get; init; } = string.Empty;
    public string Module { get; init; } = string.Empty;
    public int? Slot { get; init; }
    public int? Channel { get; init; }
    public string Item { get; init; } = string.Empty;
    public double? Value { get; init; }
    public string Unit { get; init; } = string.Empty;
    public double? Target { get; init; }
    public double? Limit { get; init; }
    public double? LowLimit { get; init; }
    public double? HighLimit { get; init; }
    public string Status { get; init; } = "Unknown";
    public string Timestamp { get; init; } = string.Empty;
    public int LineNumber { get; init; }
    public int LoopIndex { get; init; }
    public string RawMessage { get; init; } = string.Empty;
}
