using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LiveChartsCore.Drawing;
using LiveChartsCore.SkiaSharpView.WPF;
using Microsoft.Win32;
using SSTLogAnalyser.ViewModels;
using SSTLogAnalyser.Services;

namespace SSTLogAnalyser;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private Point? _chartLeftButtonDownPosition;
    private long _lastTooltipUpdateTimestamp;
    private int _channelPageStartIndex;
    private bool _suppressChannelSelectionChanged;
    private const int ChannelPageSize = 128;
    private int _channelDragAnchor = -1;
    private Point _channelDragStart;
    private int[] _channelDragBase = [];
    private bool _updatingChannelSlots;
    private int _channelRangeAnchor = -1;

    public MainWindow()
    {
        InitializeComponent();
        ChannelSlotRule.ItemsSource = ChannelSlotSelection.Rules;
        ChannelSlotRule.SelectedIndex = 0;
        ChannelList.AddHandler(Mouse.MouseMoveEvent, new MouseEventHandler(ChannelList_MouseMove), true);
        MainTabControl.Items.Remove(DiagnosticsTab);
        MainTabControl.Items.Insert(1, DiagnosticsTab);
        _vm = new MainViewModel();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.Series) or nameof(MainViewModel.IsExpectedMeasuredMode))
                ClearLinkedChartCursor();
            if (e.PropertyName == nameof(MainViewModel.TooltipVisible) && !_vm.TooltipVisible)
                TooltipPanel.Visibility = Visibility.Collapsed;
        };
        _vm.RunSummaries.CollectionChanged += (_, _) =>
        {
            if (_vm.RunSummaries.Count > 0) MainTabControl.SelectedItem = RunSummaryTab;
        };
        _vm.AvailableChannels.CollectionChanged += (_, _) =>
        {
            _channelPageStartIndex = 0;
            _channelRangeAnchor = -1;
            EnsureDefaultChannelSelection();
            UpdateChannelPageText();
            RefreshChannelSlots();
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

    private void ChannelSlotRule_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshChannelSlots();

    private void RefreshChannelSlots()
    {
        if (_vm == null || ChannelSlotPicker == null) return;
        _updatingChannelSlots = true;
        try
        {
            var rule = ChannelSlotRule.SelectedItem as string ?? "Module default";
            ChannelSlotPicker.ItemsSource = _vm.AvailableChannels
                .GroupBy(c => ChannelSlotSelection.GetSlot(c, _vm.SelectedModule, rule))
                .OrderBy(g => g.Key)
                .Select(g => new ChannelSlotChoice(g.Key, g.ToArray())).ToArray();
            ChannelSlotPicker.SelectedIndex = -1;
        }
        finally { _updatingChannelSlots = false; }
    }

    private sealed record ChannelSlotChoice(int Slot, int[] Channels)
    {
        public override string ToString() => $"Slot {Slot}: {Channels.Min()}–{Channels.Max()} ({Channels.Length})";
    }

    private void ChannelSlotPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingChannelSlots || ChannelSlotPicker.SelectedItem is not ChannelSlotChoice slot) return;
        ApplyChannelSelection(slot.Channels);
        ChannelList.ScrollIntoView(slot.Channels[0]);
        // A slot is a selection shortcut, so the same slot can be chosen again after editing channels.
        ChannelSlotPicker.SelectedIndex = -1;
        _vm.StatusText = $"Selected Slot {slot.Slot}: {slot.Channels.Length} available channels.";
    }

    private void ApplyChannelSelection(int[] channels)
    {
        _suppressChannelSelectionChanged = true;
        try
        {
            ChannelList.UnselectAll();
            foreach (var channel in channels) ChannelList.SelectedItems.Add(channel);
        }
        finally { _suppressChannelSelectionChanged = false; }
        _vm.SetSelectedChannels(channels);
    }

    private void ChannelList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(ChannelList, e.OriginalSource as DependencyObject) is not ListBoxItem item) return;
        var index = ChannelList.ItemContainerGenerator.IndexFromContainer(item);
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && _channelRangeAnchor >= 0)
        {
            ApplyChannelSelection(ChannelList.SelectedItems.OfType<int>().Union(_vm.AvailableChannels
                .Skip(Math.Min(_channelRangeAnchor, index)).Take(Math.Abs(index - _channelRangeAnchor) + 1)).ToArray());
            item.Focus();
            e.Handled = true;
            return;
        }
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) return;
        _channelDragAnchor = ChannelList.ItemContainerGenerator.IndexFromContainer(item);
        _channelRangeAnchor = _channelDragAnchor;
        _channelDragStart = e.GetPosition(ChannelList);
        _channelDragBase = (Keyboard.Modifiers & ModifierKeys.Control) != 0
            ? ChannelList.SelectedItems.OfType<int>().ToArray() : [];
        // Keep native click/keyboard behavior; publish one chart update when the gesture ends.
        _suppressChannelSelectionChanged = true;
    }

    private void ChannelList_MouseMove(object sender, MouseEventArgs e)
    {
        if (_channelDragAnchor < 0 || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(ChannelList);
        if (Math.Abs(point.Y - _channelDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        if (ItemsControl.ContainerFromElement(ChannelList, e.OriginalSource as DependencyObject) is not ListBoxItem item) return;
        var end = ChannelList.ItemContainerGenerator.IndexFromContainer(item);
        UpdateDraggedChannelSelection(end);
        // Native ListBox mouse handling retains capture and automatic scrolling at the edges.
    }

    private void UpdateDraggedChannelSelection(int end)
    {
        if (_channelDragAnchor < 0 || end < 0 || end >= _vm.AvailableChannels.Count) return;
        var selection = _channelDragBase.Union(_vm.AvailableChannels
            .Skip(Math.Min(_channelDragAnchor, end)).Take(Math.Abs(end - _channelDragAnchor) + 1)).ToHashSet();
        foreach (var channel in ChannelList.SelectedItems.OfType<int>().Where(c => !selection.Contains(c)).ToArray())
            ChannelList.SelectedItems.Remove(channel);
        foreach (var channel in selection)
            if (!ChannelList.SelectedItems.Contains(channel)) ChannelList.SelectedItems.Add(channel);
    }

    private void ChannelList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => FinishChannelDrag();
    private void ChannelList_LostMouseCapture(object sender, MouseEventArgs e) => FinishChannelDrag();

    private void FinishChannelDrag()
    {
        if (_channelDragAnchor < 0) return;
        _channelDragAnchor = -1;
        _channelDragBase = [];
        _suppressChannelSelectionChanged = false;
        _vm.SetSelectedChannels(ChannelList.SelectedItems.OfType<int>());
    }

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
                ClearLinkedChartCursor();
                TooltipPanel.Visibility = Visibility.Collapsed;
                _vm.TooltipVisible = false;
                return;
            }

            if (_vm.CurrentChartData.Count == 0)
            {
                TooltipPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var chart = (CartesianChart)sender;
            if (!TryGetChartPointerData(chart, e, out var pos, out var dataX, out var dataY, out var xScale, out var yScale))
            {
                ClearLinkedChartCursor();
                TooltipPanel.Visibility = Visibility.Collapsed;
                _vm.TooltipVisible = false;
                return;
            }

            // Cursor transforms are independent of chart redraws and tooltip throttling.
            if (_vm.IsExpectedMeasuredMode)
            {
                PositionChartCursor(MainChart, MainChartCursor, dataX);
                PositionChartCursor(LinkedErrorChart, LinkedErrorChartCursor, dataX);
            }
            var now = Stopwatch.GetTimestamp();
            if (_lastTooltipUpdateTimestamp != 0 &&
                Stopwatch.GetElapsedTime(_lastTooltipUpdateTimestamp, now) < TimeSpan.FromMilliseconds(40))
                return;
            _lastTooltipUpdateTimestamp = now;
            var nearest = _vm.FindNearestChartPoint(dataX, dataY, xScale, yScale, out var nearestDist, chart == LinkedErrorChart);

            if (nearest == null || nearestDist > 25)
            {
                TooltipPanel.Visibility = Visibility.Collapsed;
                _vm.TooltipVisible = false;
                return;
            }

            var failTag = nearest.IsFailed ? " [FAIL]" : "";
            _vm.TooltipTitle = nearest.SeriesName + failTag;
            _vm.TooltipExpect = "Expected: " + nearest.ExpectValue.ToString("G6");
            _vm.TooltipDiff = _vm.IsExpectedMeasuredMode
                ? "Measured: " + nearest.MeasureValue.ToString("G6") +
                  (nearest.AdcMeasureValue.HasValue ? (_vm.SelectedModule == "HDM" ? "\nHDM: " : "\nADC: ") + nearest.AdcMeasureValue.Value.ToString("G6") : "") +
                  "\nDifference (LOG): " + nearest.Difference.ToString("G6")
                : nearest.DiffValue.HasValue
                ? "Measured: " + nearest.MeasureValue.ToString("G6") + "\nMeas-Target: " + nearest.DiffValue.Value.ToString("G6")
                : "Difference: " + nearest.Difference.ToString("G6");
            _vm.TooltipExtra = (nearest.WaveValue.HasValue && nearest.OffsetValue.HasValue)
                ? "Wave: " + nearest.WaveValue.Value.ToString("G6") + "\nOffset: " + nearest.OffsetValue.Value.ToString("G6")
                : "";
            _vm.TooltipVisible = true;

            // Position tooltip near cursor using Margin
            var tooltipPosition = e.GetPosition(GeneralChartPlotArea);
            double ttX = tooltipPosition.X + 16;
            double ttY = tooltipPosition.Y - 12;
            var w = GeneralChartPlotArea.ActualWidth;
            if (ttX + 180 > w) ttX = tooltipPosition.X - 180;
            if (ttY < 0) ttY = tooltipPosition.Y + 16;
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
        ClearLinkedChartCursor();
    }

    private void MainChart_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _chartLeftButtonDownPosition = e.GetPosition((CartesianChart)sender);

    private void ClearLinkedChartCursor()
    {
        MainChartCursor.Visibility = Visibility.Collapsed;
        LinkedErrorChartCursor.Visibility = Visibility.Collapsed;
    }

    private void Chart_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ClearLinkedChartCursor();
        TooltipPanel.Visibility = Visibility.Collapsed;
        _vm.TooltipVisible = false;
    }

    private static void PositionChartCursor(CartesianChart chart, System.Windows.Shapes.Line cursor, double x)
    {
        var location = chart.CoreChart.DrawMarginLocation;
        var size = chart.CoreChart.DrawMarginSize;
        var pixel = chart.ScaleDataToPixels(new LvcPointD(x, 0));
        if (!double.IsFinite(pixel.X) || size.Width <= 0 || size.Height <= 0 ||
            pixel.X < location.X || pixel.X > location.X + size.Width)
        {
            cursor.Visibility = Visibility.Collapsed;
            return;
        }
        cursor.Y1 = location.Y;
        cursor.Y2 = location.Y + size.Height;
        ((TranslateTransform)cursor.RenderTransform).X = pixel.X;
        cursor.Visibility = Visibility.Visible;
    }

    private void MainChart_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var downPosition = _chartLeftButtonDownPosition;
            _chartLeftButtonDownPosition = null;
            if (!downPosition.HasValue) return;

            var chart = (CartesianChart)sender;
            var upPosition = e.GetPosition(chart);
            if (Math.Abs(upPosition.X - downPosition.Value.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(upPosition.Y - downPosition.Value.Y) >= SystemParameters.MinimumVerticalDragDistance)
                return;

            if (_vm.CurrentChartData.Count == 0) return;

            if (!TryGetChartPointerData(chart, e, out _, out var dataX, out var dataY, out var xScale, out var yScale))
                return;

            var nearest = _vm.FindNearestChartPoint(dataX, dataY, xScale, yScale, out var nearestDist, chart == LinkedErrorChart);

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
        CartesianChart chart,
        MouseEventArgs e,
        out Point position,
        out double dataX,
        out double dataY,
        out double xScale,
        out double yScale)
    {
        position = e.GetPosition(chart);
        dataX = dataY = xScale = yScale = 0;

        var width = chart.ActualWidth;
        var height = chart.ActualHeight;
        if (width <= 0 || height <= 0 || _vm.CurrentChartData.Count == 0) return false;

        var location = chart.CoreChart.DrawMarginLocation;
        var size = chart.CoreChart.DrawMarginSize;
        var plotLeft = location.X;
        var plotTop = location.Y;
        var plotRight = plotLeft + size.Width;
        var plotBottom = plotTop + size.Height;
        var plotWidth = plotRight - plotLeft;
        var plotHeight = plotBottom - plotTop;
        if (plotWidth <= 0 || plotHeight <= 0) return false;

        if (position.X < plotLeft || position.X > plotRight ||
            position.Y < plotTop || position.Y > plotBottom)
            return false;

        var topLeft = chart.ScalePixelsToData(new LvcPointD(plotLeft, plotTop));
        var bottomRight = chart.ScalePixelsToData(new LvcPointD(plotRight, plotBottom));
        var pointer = chart.ScalePixelsToData(new LvcPointD(position.X, position.Y));
        var xRange = bottomRight.X - topLeft.X;
        var yRange = topLeft.Y - bottomRight.Y;
        if (xRange <= 0 || yRange <= 0) return false;

        dataX = pointer.X;
        dataY = pointer.Y;
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
            "WaveOffset" => (WaveOffsetCaptureArea, "WAVE-OFFSET-Attribution"),
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

    private void WaveOffsetDetailMode_Checked(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.SetWaveOffsetViewMode(detail: true);
    }

    private void WaveOffsetChannelMapMode_Checked(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.SetWaveOffsetViewMode(detail: false);
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
            "WaveOffset" => WaveOffsetPanel,
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
            (Panel: WaveOffsetPanel, Row: 2, Column: 2)
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
        WaveOffsetPanel
    ];

    private Button[] GetDiagnosticExpandButtons() =>
    [
        ToleranceHeatmapExpandButton,
        CoefficientDistributionExpandButton,
        ResidualSignatureExpandButton,
        WaveOffsetExpandButton
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
