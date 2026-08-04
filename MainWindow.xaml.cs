using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SSTLogAnalyser.ViewModels;

namespace SSTLogAnalyser;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private Point? _chartLeftButtonDownPosition;
    private long _lastTooltipUpdateTimestamp;
    private int _channelPageStartIndex;
    private bool _suppressChannelSelectionChanged;
    private const int ChannelPageSize = 128;

    public MainWindow()
    {
        InitializeComponent();
        MainTabControl.Items.Remove(DiagnosticsTab);
        MainTabControl.Items.Insert(1, DiagnosticsTab);
        _vm = new MainViewModel();
        _vm.AvailableChannels.CollectionChanged += (_, _) =>
        {
            _channelPageStartIndex = 0;
            EnsureDefaultChannelSelection();
            UpdateChannelPageText();
        };
        _vm.AvailableLoops.CollectionChanged += (_, _) => EnsureDefaultLoopSelection();
        DataContext = _vm;
        UpdateChannelPageText();
        UpdateDiagnosticsActiveState();
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
        if (_suppressChannelSelectionChanged || sender is not ListBox lb) return;
        _vm.SetSelectedChannels(lb.SelectedItems.OfType<int>());
    }

    private void AllChannels_Click(object sender, RoutedEventArgs e) => SelectAllChannels();

    private void FindChannel_Click(object sender, RoutedEventArgs e) => FindChannel();

    private void ChannelSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        FindChannel();
        e.Handled = true;
    }

    private void FindChannel()
    {
        var searchText = ChannelSearchBox.Text;
        if (!TryParseChannelSearch(searchText, out var channel))
        {
            _vm.StatusText = "Enter a valid channel number, for example 36 or CH36.";
            ChannelSearchBox.SelectAll();
            ChannelSearchBox.Focus();
            return;
        }

        var channelIndex = _vm.AvailableChannels.IndexOf(channel);
        if (channelIndex < 0)
        {
            _vm.StatusText = $"Channel {channel} was not found in the current module.";
            ChannelSearchBox.SelectAll();
            ChannelSearchBox.Focus();
            return;
        }

        _channelPageStartIndex = (channelIndex / ChannelPageSize) * ChannelPageSize;
        _suppressChannelSelectionChanged = true;
        try
        {
            ChannelList.UnselectAll();
            ChannelList.SelectedItem = channel;
        }
        finally
        {
            _suppressChannelSelectionChanged = false;
        }

        _vm.SetSelectedChannels([channel]);
        ChannelList.UpdateLayout();
        ChannelList.ScrollIntoView(channel);
        UpdateChannelPageText();
        _vm.StatusText = $"Selected Channel {channel}.";
    }

    internal static bool TryParseChannelSearch(string? value, out int channel)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.StartsWith("Channel", StringComparison.OrdinalIgnoreCase))
            text = text["Channel".Length..].Trim();
        else if (text.StartsWith("CH", StringComparison.OrdinalIgnoreCase))
            text = text[2..].Trim();

        text = text.TrimStart('#', ':').Trim();
        return int.TryParse(text, out channel);
    }

    private void PreviousChannelPage_Click(object sender, RoutedEventArgs e) =>
        SelectChannelPage(_channelPageStartIndex - ChannelPageSize);

    private void NextChannelPage_Click(object sender, RoutedEventArgs e) =>
        SelectChannelPage(_channelPageStartIndex + ChannelPageSize);

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
        _suppressChannelSelectionChanged = true;
        try
        {
            ChannelList.UnselectAll();
        }
        finally
        {
            _suppressChannelSelectionChanged = false;
        }
        _vm.SetSelectedChannels(Array.Empty<int>());
        UpdateChannelPageText();
    }

    private void SelectChannelPage(int requestedStart)
    {
        var count = _vm.AvailableChannels.Count;
        if (count == 0)
        {
            UpdateChannelPageText();
            return;
        }

        var lastPageStart = ((count - 1) / ChannelPageSize) * ChannelPageSize;
        _channelPageStartIndex = Math.Clamp(requestedStart, 0, lastPageStart);
        var channels = _vm.AvailableChannels
            .Skip(_channelPageStartIndex)
            .Take(ChannelPageSize)
            .ToArray();

        _suppressChannelSelectionChanged = true;
        try
        {
            ChannelList.UnselectAll();
            foreach (var channel in channels)
                ChannelList.SelectedItems.Add(channel);
        }
        finally
        {
            _suppressChannelSelectionChanged = false;
        }

        _vm.SetSelectedChannels(channels);
        if (channels.Length > 0) ChannelList.ScrollIntoView(channels[0]);
        UpdateChannelPageText();
    }

    private void UpdateChannelPageText()
    {
        if (ChannelPageText == null) return;
        var count = _vm?.AvailableChannels.Count ?? 0;
        if (count == 0)
        {
            ChannelPageText.Text = "0 / 0";
            return;
        }

        var start = Math.Clamp(_channelPageStartIndex, 0, count - 1);
        var end = Math.Min(start + ChannelPageSize, count);
        ChannelPageText.Text = $"{start + 1}-{end} / {count}";
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

            var now = Stopwatch.GetTimestamp();
            if (_lastTooltipUpdateTimestamp != 0 &&
                Stopwatch.GetElapsedTime(_lastTooltipUpdateTimestamp, now) < TimeSpan.FromMilliseconds(40))
                return;
            _lastTooltipUpdateTimestamp = now;

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

            var nearest = _vm.FindNearestChartPoint(dataX, dataY, xScale, yScale, out var nearestDist);

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

            var nearest = _vm.FindNearestChartPoint(dataX, dataY, xScale, yScale, out var nearestDist);

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

        if (!_vm.TryGetChartBounds(out var xMin, out var xMax, out var yMin, out var yMax))
            return false;
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

    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MainTabControl)) return;
        UpdateDiagnosticsActiveState();
    }

    private void UpdateDiagnosticsActiveState()
    {
        if (DataContext is not MainViewModel viewModel || DiagnosticsTab == null) return;
        var diagnosticsIndex = MainTabControl.Items.IndexOf(DiagnosticsTab);
        var isFloating = diagnosticsIndex >= 0 && _floatingWindows.ContainsKey(diagnosticsIndex);
        viewModel.SetDiagnosticsActive(ReferenceEquals(MainTabControl.SelectedItem, DiagnosticsTab) || isFloating);
    }

    private readonly Dictionary<int, Window> _floatingWindows = new();
    private static readonly string[] TabNames = { "Chart", "Diagnostics", "Pass/Fail Matrix", "Data", "Statistics", "Errors / FATAL", "Device Info" };
    private string? _expandedDiagnosticPanel;

    private void SaveChartImage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string chartKey) return;

        var (target, fileLabel) = chartKey switch
        {
            "Chart" => (MainChartCaptureArea, "Chart"),
            "Tolerance" => (ToleranceHeatmapCaptureArea, "Tolerance-Heatmap"),
            "Coefficient" => (CoefficientDistributionCaptureArea, "Gain-Offset"),
            "Residual" => (ResidualSignatureCaptureArea, "Residual-Signature"),
            "Symmetry" => (SymmetryCaptureArea, "POS-NEG-Symmetry"),
            _ => ((FrameworkElement?)null, string.Empty)
        };
        if (target == null || target.ActualWidth <= 0 || target.ActualHeight <= 0) return;

        var dialog = new SaveFileDialog
        {
            Title = "Save chart image",
            Filter = "PNG image (*.png)|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = BuildChartImageFileName(chartKey, fileLabel)
        };

        var owner = Window.GetWindow(button) ?? this;
        if (dialog.ShowDialog(owner) != true) return;

        var actionButtonsOpacity = ChartActionButtons.Opacity;
        try
        {
            if (chartKey == "Chart") ChartActionButtons.Opacity = 0;
            target.UpdateLayout();
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            SaveElementAsPng(target, dialog.FileName);
            _vm.StatusText = "Chart image saved: " + dialog.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "Unable to save the chart image.\n\n" + ex.Message,
                "Save chart image", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ChartActionButtons.Opacity = actionButtonsOpacity;
        }
    }

    private string BuildChartImageFileName(string chartKey, string fallbackTestItem)
    {
        var testItem = _vm.SelectedTestItem;
        if (string.IsNullOrWhiteSpace(testItem) && chartKey == "Coefficient")
            testItem = _vm.SelectedCoefficientName;

        var testItemPart = SanitizeFileNamePart(testItem ?? fallbackTestItem, 80);
        var modulePart = SanitizeFileNamePart(_vm.SelectedModule ?? "All Modules", 40);
        var channelPart = BuildChannelFileNamePart();
        return $"{testItemPart} - {modulePart} - {channelPart} - {DateTime.Now:yyyyMMdd-HHmmss}.png";
    }

    private string BuildChannelFileNamePart()
    {
        var channels = _vm.SelectedChannels;
        if (channels.Count == 0) return "CH-ALL";
        if (channels.Count <= 6) return string.Join("+", channels.Select(channel => $"CH{channel}"));
        return $"CH{channels[0]}-CH{channels[^1]} ({channels.Count}CH)";
    }

    private static string SanitizeFileNamePart(string value, int maxLength)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray())
            .Trim()
            .TrimEnd('.');
        if (string.IsNullOrWhiteSpace(sanitized)) sanitized = "Unknown";
        return sanitized.Length <= maxLength ? sanitized : sanitized[..maxLength].TrimEnd();
    }

    private static void SaveElementAsPng(FrameworkElement element, string filePath)
    {
        var dpi = VisualTreeHelper.GetDpi(element);
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(filePath);
        encoder.Save(stream);
    }

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
        UpdateDiagnosticsActiveState();

        floatWin.Closed += (_, _) =>
        {
            var movedContent = floatWin.Content;
            floatWin.Content = null;
            tabItem.Content = movedContent;
            _floatingWindows.Remove(tabIndex);
            UpdateDiagnosticsActiveState();
        };

        floatWin.Show();
    }
}
