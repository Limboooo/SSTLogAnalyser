namespace SSTLogAnalyser.Models;

public sealed class BaselineComparisonRow
{
    public required IReadOnlyList<TestResult> BaselinePoints { get; init; }
    public required IReadOnlyList<TestResult> CurrentPoints { get; init; }
    public required string Status { get; init; }
    public bool LimitsChanged { get; init; }
    public bool NeedsAttention { get; init; }
    public TestResult Point => BaselinePoints.FirstOrDefault() ?? CurrentPoints[0];
    public ModuleType Module => Point.ModuleType;
    public string TestItem => Point.TestItemName;
    public int Channel => Point.ChannelId;
    public int Slot => Point.SlotNumber;
    public double Expect => Point.ExpectValue;
    public string Component => Point.ComponentType;
    public double? Wave => Point.WaveValue;
    public double? Offset => Point.OffsetValue;
    public TestResult? Baseline => BaselinePoints.Count == 1 ? BaselinePoints[0] : null;
    public TestResult? Current => CurrentPoints.Count == 1 ? CurrentPoints[0] : null;
    public bool CanEvaluate { get; init; }
    public double? BaselineError => CanEvaluate ? Baseline?.Difference : null;
    public double? CurrentError => CanEvaluate ? Current?.Difference : null;
    public double? ErrorChange => CurrentError - BaselineError;
    public double? AbsoluteErrorChange => CurrentError.HasValue && BaselineError.HasValue
        ? Math.Abs(CurrentError.Value) - Math.Abs(BaselineError.Value) : null;
    public double? BaselineUtilization { get; init; }
    public double? CurrentUtilization { get; init; }
    public double? CurrentMargin { get; init; }
    public string UnderBaselineLimits { get; init; } = "Unknown";
    public string BaselineVerdict { get; init; } = "Unknown";
    public string CurrentVerdict { get; init; } = "Unknown";
    public string BaselineLines => string.Join(", ", BaselinePoints.Select(p => p.LineNumber));
    public string CurrentLines => string.Join(", ", CurrentPoints.Select(p => p.LineNumber));
}
