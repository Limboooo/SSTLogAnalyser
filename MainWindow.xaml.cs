using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SSTLogAnalyser.ViewModels;

namespace SSTLogAnalyser;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
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
        _vm.SelectedChannels.Clear();
        foreach (var item in lb.SelectedItems)
            if (item is int ch) _vm.SelectedChannels.Add(ch);
    }

    private void LoopList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox lb) return;
        _vm.SelectedLoops.Clear();
        foreach (var item in lb.SelectedItems)
            if (item is int loop) _vm.SelectedLoops.Add(loop);
    }

    private void MainChart_MouseMove(object sender, MouseEventArgs e)
    {
        try
        {
            if (_vm.CurrentChartData.Count == 0)
            {
                TooltipPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var pos = e.GetPosition(MainChart);
            var w = MainChart.ActualWidth;
            var h = MainChart.ActualHeight;
            if (w <= 0 || h <= 0) return;

            // Estimate plot area bounds
            double plotLeft = 65, plotTop = 10;
            double plotRight = w - 20, plotBottom = h - 40;
            double plotW = plotRight - plotLeft;
            double plotH = plotBottom - plotTop;
            if (plotW <= 0 || plotH <= 0) return;

            // Check if mouse is within plot area
            if (pos.X < plotLeft || pos.X > plotRight || pos.Y < plotTop || pos.Y > plotBottom)
            {
                TooltipPanel.Visibility = Visibility.Collapsed;
                _vm.TooltipVisible = false;
                return;
            }

            // Get axis ranges from data
            var allExpect = _vm.CurrentChartData.Select(d => d.ExpectValue);
            var allDiff = _vm.CurrentChartData.Select(d => d.Difference);
            double xMin = allExpect.Min(), xMax = allExpect.Max();
            double yMin = allDiff.Min(), yMax = allDiff.Max();
            double xPad = (xMax - xMin) * 0.05;
            double yPad = (yMax - yMin) * 0.05;
            if (xPad == 0) xPad = 0.5;
            if (yPad == 0) yPad = 0.001;
            xMin -= xPad; xMax += xPad;
            yMin -= yPad; yMax += yPad;

            double xRange = xMax - xMin;
            double yRange = yMax - yMin;

            // Convert mouse position to data coordinates
            double dataX = xMin + ((pos.X - plotLeft) / plotW) * xRange;
            double dataY = yMax - ((pos.Y - plotTop) / plotH) * yRange;

            // Normalize to pixel distance for fair comparison
            double xScale = plotW / xRange;
            double yScale = plotH / yRange;

            // Find closest non-limit data point
            TooltipDataPoint? nearest = null;
            double nearestDist = double.MaxValue;

            foreach (var dp in _vm.CurrentChartData)
            {
                if (dp.IsLimit) continue;
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
        TooltipPanel.Visibility = Visibility.Collapsed;
        _vm.TooltipVisible = false;
    }

    private void MainChart_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (_vm.CurrentChartData.Count == 0) return;

            var pos = e.GetPosition(MainChart);
            var w = MainChart.ActualWidth;
            var h = MainChart.ActualHeight;
            if (w <= 0 || h <= 0) return;

            double plotLeft = 65, plotTop = 10;
            double plotRight = w - 20, plotBottom = h - 40;
            double plotW = plotRight - plotLeft;
            double plotH = plotBottom - plotTop;
            if (plotW <= 0 || plotH <= 0) return;

            if (pos.X < plotLeft || pos.X > plotRight || pos.Y < plotTop || pos.Y > plotBottom)
                return;

            var allExpect = _vm.CurrentChartData.Select(d => d.ExpectValue);
            var allDiff = _vm.CurrentChartData.Select(d => d.Difference);
            double xMin = allExpect.Min(), xMax = allExpect.Max();
            double yMin = allDiff.Min(), yMax = allDiff.Max();
            double xPad = (xMax - xMin) * 0.05;
            double yPad = (yMax - yMin) * 0.05;
            if (xPad == 0) xPad = 0.5;
            if (yPad == 0) yPad = 0.001;
            xMin -= xPad; xMax += xPad;
            yMin -= yPad; yMax += yPad;

            double xRange = xMax - xMin;
            double yRange = yMax - yMin;
            double xScale = plotW / xRange;
            double yScale = plotH / yRange;

            TooltipDataPoint? nearest = null;
            double nearestDist = double.MaxValue;

            foreach (var dp in _vm.CurrentChartData)
            {
                if (dp.IsLimit) continue;
                double dx = (dp.ExpectValue - (xMin + ((pos.X - plotLeft) / plotW) * xRange)) * xScale;
                double dy = (dp.Difference - (yMax - ((pos.Y - plotTop) / plotH) * yRange)) * yScale;
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
                if (MainTabControl.Items.Count > 2)
                    MainTabControl.SelectedIndex = 2;
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

    private readonly Dictionary<int, Window> _floatingWindows = new();
    private static readonly string[] TabNames = { "Chart", "Pass/Fail Matrix", "Data", "Statistics", "Errors / FATAL", "Device Info" };

    private void DetachTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tagStr) return;
        if (!int.TryParse(tagStr, out int tabIndex)) return;
        if (tabIndex < 0 || tabIndex >= MainTabControl.Items.Count) return;

        // If already floating, focus the existing window
        if (_floatingWindows.TryGetValue(tabIndex, out var existing) && existing != null)
        {
            existing.Focus();
            return;
        }

        var tabItem = MainTabControl.Items[tabIndex] as TabItem;
        if (tabItem == null) return;

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
