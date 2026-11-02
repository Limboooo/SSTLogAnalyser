using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace SSTLogAnalyser.ViewModels;

public partial class MainViewModel
{
    public LiveChartsCore.Measure.Margin LinkedChartMargin { get; } = new(85, 28, 24, 45);
    [ObservableProperty] private ISeries[] _linkedErrorSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _linkedErrorXAxes = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _linkedErrorYAxes = Array.Empty<Axis>();
    private List<TooltipDataPoint> LinkedErrorData { get; } = new();
    private readonly Dictionary<string, TooltipDataPoint[]> _linkedErrorIndex = new(StringComparer.Ordinal);

    private void InitializeLinkedAxes()
    {
        var main = XAxes[0];
        var error = new Axis { Name = main.Name, NameTextSize = 13, TextSize = 11 };
        LinkedErrorXAxes = new[] { error };
        LinkedErrorYAxes = SelectedModule == "AC" ? new[] { CreateAcAxis("Error (ns)") }
            : new[] { new Axis { Name = "Difference (LOG)", NameTextSize = 13, TextSize = 11 } };
        // Separate axis instances keep each chart's measurement state independent.
        bool syncing = false;
        void Sync(Axis source, Axis target, string? name)
        {
            if (syncing || name is not (nameof(Axis.MinLimit) or nameof(Axis.MaxLimit))) return;
            syncing = true;
            try { target.MinLimit = source.MinLimit; target.MaxLimit = source.MaxLimit; }
            finally { syncing = false; }
        }
        main.PropertyChanged += (_, e) => Sync(main, error, e.PropertyName);
        error.PropertyChanged += (_, e) => Sync(error, main, e.PropertyName);
    }

}
