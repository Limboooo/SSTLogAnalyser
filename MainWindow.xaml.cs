using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SSTLogAnalyser.ViewModels;

namespace SSTLogAnalyser;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private Point? _chartLeftButtonDownPosition;

    public MainWindow()
    {
        InitializeComponent();
        MainTabControl.Items.Remove(DiagnosticsTab);
        MainTabControl.Items.Insert(1, DiagnosticsTab);
        _vm = new MainViewModel();
        _vm.AvailableChannels.CollectionChanged += (_, _) => EnsureDefaultChannelSelection();
        _vm.AvailableLoops.CollectionChanged += (_, _) => EnsureDefaultLoopSelection();
        DataContext = _vm;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            _ = _vm.LoadFilesFromPathsAsync(files);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void ChannelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox lb) return;
        _vm.SetSelectedChannels(lb.SelectedItems.OfType<int>());
    }

    private void AllChannels_Click(object sender, RoutedEventArgs e) => SelectAllChannels();

    private void MultiChannel_Checked(object sender, RoutedEventArgs e)
    {
        SelectAllChannels();
        EnsureDefaultLoopSelection(force: true);
    }

    private void AllLoops_Click(object sender, RoutedEventArgs e) => SelectAllLoops();

    private void MultiLoop_Checked(object sender, RoutedEventArgs e)
    {
        SelectAllLoops();
        EnsureDefaultChannelSelection(force: true);
    }

    private void SelectAllChannels()
    {
        ChannelList.UnselectAll();
        if (DataContext is MainViewModel viewModel)
            viewModel.SetSelectedChannels(Array.Empty<int>());
    }

    private void EnsureDefaultLoopSelection(bool force = false)
    {
        if (DataContext is not MainViewModel viewModel ||
            (!force && !viewModel.CompareMultiChannel) ||
            LoopList.SelectedItems.Count > 0 ||
            viewModel.AvailableLoops.Count == 0)
            return;

        LoopList.SelectedItem = viewModel.AvailableLoops[0];
    }

    private void SelectAllLoops()
    {
        LoopList.UnselectAll();
        if (DataContext is MainViewModel viewModel)
            viewModel.SetSelectedLoops(Array.Empty<int>());
    }

    private void EnsureDefaultChannelSelection(bool force = false)
    {
        if (DataContext is not MainViewModel viewModel ||
            (!force && !viewModel.CompareMultiLoop) ||
            ChannelList.SelectedItems.Count > 0 ||
            viewModel.AvailableChannels.Count == 0)
            return;

        ChannelList.SelectedItem = viewModel.AvailableChannels[0];
    }

    private void LoopList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox lb) return;
        _vm.SetSelectedLoops(lb.SelectedItems.OfType<int>());
    }

    private void MainChart_MouseMove(object sender, MouseEventArgs e)
    {
        try
        {
            if (e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed)
            {
                TooltipPanel.Visibility = Visibility.Collapsed;
                _vm.TooltipVisible = false;
                return;
            }

            if (_vm.CurrentChartData.Count == 0)
            {
                TooltipPanel.Visibility = Visibility.Collapsed;
                return;
            }

            if (!TryGetChartPointerData(e, out var pos, out var dataX, out var dataY, out var xScale, out var yScale))
            {
                TooltipPanel.Visibility = Visibility.Collapsed;
                _vm.TooltipVisible = false;
                return;
            }

            // Find closest non-limit data point
            TooltipDataPoint? nearest = null;
            double nearestDist = double.MaxValue;

            foreach (var dp in _vm.CurrentChartData)
            {
                if (dp.IsLimit || !_vm.IsChartSeriesVisible(dp.SeriesName)) continue;
                double dx = (dp.ExpectValue - dataX) * xScale;
                double dy = (dp.Difference - dataY) * yScale;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = dp;
                }
            }

            if (nearest == null || nearestDist > 25)
            {
                TooltipPanel.Visibility = Visibility.Collapsed;
                _vm.TooltipVisible = false;
                return;
            }

            var failTag = nearest.IsFailed ? " [FAIL]" : "";
            _vm.TooltipTitle = nearest.SeriesName + failTag;
            _vm.TooltipExpect = "Expected: " + nearest.ExpectValue.ToString("G6");
            _vm.TooltipDiff = nearest.DiffValue.HasValue
                ? "Measured: " + nearest.MeasureValue.ToString("G6") + "\nMeas-Target: " + nearest.DiffValue.Value.ToString("G6")
                : "Difference: " + nearest.Difference.ToString("G6");
            _vm.TooltipExtra = (nearest.WaveValue.HasValue && nearest.OffsetValue.HasValue)
                ? "Wave: " + nearest.WaveValue.Value.ToString("G6") + "\nOffset: " + nearest.OffsetValue.Value.ToString("G6")
                : "";
            _vm.TooltipVisible = true;

            // Position tooltip near cursor using Margin
            double ttX = pos.X + 16;
            double ttY = pos.Y - 12;
            var w = MainChart.ActualWidth;
            if (ttX + 180 > w) ttX = pos.X - 180;
            if (ttY < 0) ttY = pos.Y + 16;
            TooltipPanel.Margin = new Thickness(ttX, ttY, 0, 0);
            TooltipPanel.Visibility = Visibility.Visible;
        }
        catch
        {
            TooltipPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void MainChart_MouseLeave(object sender, MouseEventArgs e)
    {
        _chartLeftButtonDownPosition = null;
        TooltipPanel.Visibility = Visibility.Collapsed;
        _vm.TooltipVisible = false;
    }

    private void MainChart_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _chartLeftButtonDownPosition = e.GetPosition(MainChart);

    private void MainChart_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var downPosition = _chartLeftButtonDownPosition;
            _chartLeftButtonDownPosition = null;
            if (!downPosition.HasValue) return;

            var upPosition = e.GetPosition(MainChart);
            if (Math.Abs(upPosition.X - downPosition.Value.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(upPosition.Y - downPosition.Value.Y) >= SystemParameters.MinimumVerticalDragDistance)
                return;

            if (_vm.CurrentChartData.Count == 0) return;

            if (!TryGetChartPointerData(e, out _, out var dataX, out var dataY, out var xScale, out var yScale))
                return;

            TooltipDataPoint? nearest = null;
            double nearestDist = double.MaxValue;

            foreach (var dp in _vm.CurrentChartData)
            {
                if (dp.IsLimit || !_vm.IsChartSeriesVisible(dp.SeriesName)) continue;
                double dx = (dp.ExpectValue - dataX) * xScale;
                double dy = (dp.Difference - dataY) * yScale;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = dp;
                }
            }

            if (nearest == null || nearestDist > 25 || nearest.RowIndex < 0)
                return;

            // Select the row in the data table
            if (nearest.RowIndex < _vm.ChartDataRows.Count)
            {
                var row = _vm.ChartDataRows[nearest.RowIndex];
                _vm.SelectedChartRow = row;
                // Switch to Data tab
                if (MainTabControl.Items.Count > 3)
                    MainTabControl.SelectedIndex = 3;
                // Scroll to the selected row
                ChartDataGrid.UpdateLayout();
                ChartDataGrid.ScrollIntoView(row);
            }
        }
        catch
        {
            // Ignore errors
        }
    }

    private bool TryGetChartPointerData(
        MouseEventArgs e,
        out Point position,
        out double dataX,
        out double dataY,
        out double xScale,
        out double yScale)
    {
        position = e.GetPosition(MainChart);
        dataX = dataY = xScale = yScale = 0;

        var width = MainChart.ActualWidth;
        var height = MainChart.ActualHeight;
        if (width <= 0 || height <= 0 || _vm.CurrentChartData.Count == 0) return false;

        const double plotLeft = 65;
        const double plotTop = 10;
        var plotRight = width - 20;
        var plotBottom = height - 40;
        var plotWidth = plotRight - plotLeft;
        var plotHeight = plotBottom - plotTop;
        if (plotWidth <= 0 || plotHeight <= 0) return false;

        if (position.X < plotLeft || position.X > plotRight ||
            position.Y < plotTop || position.Y > plotBottom)
            return false;

        var xMin = _vm.CurrentChartData.Min(d => d.ExpectValue);
        var xMax = _vm.CurrentChartData.Max(d => d.ExpectValue);
        var yMin = _vm.CurrentChartData.Min(d => d.Difference);
        var yMax = _vm.CurrentChartData.Max(d => d.Difference);
        var xPadding = (xMax - xMin) * 0.05;
        var yPadding = (yMax - yMin) * 0.05;
        if (xPadding == 0) xPadding = 0.5;
        if (yPadding == 0) yPadding = 0.001;
        xMin -= xPadding;
        xMax += xPadding;
        yMin -= yPadding;
        yMax += yPadding;

        var xAxis = _vm.XAxes.FirstOrDefault();
        var yAxis = _vm.YAxes.FirstOrDefault();
        xMin = xAxis?.MinLimit ?? xMin;
        xMax = xAxis?.MaxLimit ?? xMax;
        yMin = yAxis?.MinLimit ?? yMin;
        yMax = yAxis?.MaxLimit ?? yMax;

        var xRange = xMax - xMin;
        var yRange = yMax - yMin;
        if (xRange <= 0 || yRange <= 0) return false;

        dataX = xMin + ((position.X - plotLeft) / plotWidth) * xRange;
        dataY = yMax - ((position.Y - plotTop) / plotHeight) * yRange;
        xScale = plotWidth / xRange;
        yScale = plotHeight / yRange;
        return true;
    }

    private readonly Dictionary<int, Window> _floatingWindows = new();
    private static readonly string[] TabNames = { "Chart", "Diagnostics", "Pass/Fail Matrix", "Data", "Statistics", "Errors / FATAL", "Device Info" };
    private string? _expandedDiagnosticPanel;

    private void DiagnosticExpand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string panelKey) return;

        if (_expandedDiagnosticPanel == panelKey)
        {
            RestoreDiagnosticPanels();
            return;
        }

        RestoreDiagnosticPanels();

        var target = panelKey switch
        {
            "Tolerance" => ToleranceHeatmapPanel,
            "Coefficient" => CoefficientDistributionPanel,
            "Residual" => ResidualSignaturePanel,
            "Symmetry" => SymmetryPanel,
            _ => null
        };
        if (target == null) return;

        foreach (var panel in GetDiagnosticPanels())
            panel.Visibility = ReferenceEquals(panel, target) ? Visibility.Visible : Visibility.Collapsed;

        DiagnosticsVerticalSplitter.Visibility = Visibility.Collapsed;
        DiagnosticsHorizontalSplitter.Visibility = Visibility.Collapsed;
        Grid.SetRow(target, 0);
        Grid.SetColumn(target, 0);
        Grid.SetRowSpan(target, 3);
        Grid.SetColumnSpan(target, 3);

        _expandedDiagnosticPanel = panelKey;
        SetDiagnosticExpandButton(button, isExpanded: true);
    }

    private void RestoreDiagnosticPanels()
    {
        var layouts = new[]
        {
            (Panel: ToleranceHeatmapPanel, Row: 0, Column: 0),
            (Panel: CoefficientDistributionPanel, Row: 0, Column: 2),
            (Panel: ResidualSignaturePanel, Row: 2, Column: 0),
            (Panel: SymmetryPanel, Row: 2, Column: 2)
        };

        foreach (var layout in layouts)
        {
            layout.Panel.Visibility = Visibility.Visible;
            Grid.SetRow(layout.Panel, layout.Row);
            Grid.SetColumn(layout.Panel, layout.Column);
            Grid.SetRowSpan(layout.Panel, 1);
            Grid.SetColumnSpan(layout.Panel, 1);
        }

        DiagnosticsVerticalSplitter.Visibility = Visibility.Visible;
        DiagnosticsHorizontalSplitter.Visibility = Visibility.Visible;
        foreach (var button in GetDiagnosticExpandButtons())
            SetDiagnosticExpandButton(button, isExpanded: false);
        _expandedDiagnosticPanel = null;
    }

    private GroupBox[] GetDiagnosticPanels() =>
    [
        ToleranceHeatmapPanel,
        CoefficientDistributionPanel,
        ResidualSignaturePanel,
        SymmetryPanel
    ];

    private Button[] GetDiagnosticExpandButtons() =>
    [
        ToleranceHeatmapExpandButton,
        CoefficientDistributionExpandButton,
        ResidualSignatureExpandButton,
        SymmetryExpandButton
    ];

    private static void SetDiagnosticExpandButton(Button button, bool isExpanded)
    {
        if (button.Content is TextBlock icon)
            icon.Text = isExpanded ? "\uE73F" : "\uE740";
        button.ToolTip = isExpanded ? "Restore chart layout" : "Maximize chart";
    }

    private void DetachTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (ItemsControl.ContainerFromElement(MainTabControl, btn) is not TabItem tabItem) return;
        var tabIndex = MainTabControl.Items.IndexOf(tabItem);
        if (tabIndex < 0 || tabIndex >= MainTabControl.Items.Count) return;

        // If already floating, focus the existing window
        if (_floatingWindows.TryGetValue(tabIndex, out var existing) && existing != null)
        {
            existing.Focus();
            return;
        }

        var originalContent = tabItem.Content;
        var tabName = TabNames[tabIndex];

        // Replace tab content with placeholder
        tabItem.Content = new TextBlock
        {
            Text = tabName + " - floating (close window to restore)",
            Foreground = System.Windows.Media.Brushes.Gray,
            FontStyle = System.Windows.FontStyles.Italic,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20)
        };

        // Create floating window
        var floatWin = new Window
        {
            Title = "SST Log Analyser - " + tabName,
            Width = 900,
            Height = 600,
            Owner = this,
            DataContext = this.DataContext,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = originalContent
        };

        _floatingWindows[tabIndex] = floatWin;

        floatWin.Closed += (_, _) =>
        {
            var movedContent = floatWin.Content;
            floatWin.Content = null;
            tabItem.Content = movedContent;
            _floatingWindows.Remove(tabIndex);
        };

        floatWin.Show();
    }
}
