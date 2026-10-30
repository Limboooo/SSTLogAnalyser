using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using SSTLogAnalyser.Services;
using SSTLogAnalyser.ViewModels;

static class AcCheck
{
    public static int Run(string path, bool fullLog = false, string? screenshot = null)
    {
        Exception? failure = null;
        int checks = 0;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                var directory = Path.Combine(Path.GetTempPath(), "SST-ac-check-" + Guid.NewGuid().ToString("N"));
                Window? window = null;
                try
                {
                    void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException("AC: " + message); checks++; }
                    var parsed = await new LogParser().ParseAsync(path);
                    Check(parsed.TestResults.Count == (fullLog ? 184320 : 5), "real verification row count");
                    Check(parsed.TestResults.All(p => !p.IsFailed), "inclusive boundary has no phantom failures");
                    Check(parsed.TestResults.Any(p => p.ChannelId == 636 && p.Difference < -0.312 && !p.IsFailed), "real ON floating-point boundary retained");
                    using var cache = new CacheService(directory);
                    var model = (MainViewModel)Activator.CreateInstance(typeof(MainViewModel), BindingFlags.Instance | BindingFlags.NonPublic,
                        null, new object[] { cache, new LogParser() }, null)!;
                    await model.LoadFilesFromPathsAsync(new[] { path });
                    Check(model.AcReports.Count == 2, "two clocks remain separate through cache and import");
                    Check(model.AcReports[0].Title.Contains("500.000 MHz") && model.AcReports[1].Title.Contains("250.000 MHz"), "clock labels preserved");
                    model.SelectedModule = "AC";
                    Check(model.HasAcOverview && model.IsAcOverviewActive, "AC summary is the default Chart view");
                    model.AcVerificationMode = true;
                    Check(!model.IsAcOverviewActive && model.YAxes.Single().MinStep == 0.078 && model.YAxes.Single().ForceStepToMin && model.XAxes.Single().MinStep == 0.078, "point chart uses a 78ps tick on timing axes");
                    model.AcVerificationMode = false;
                    Check(model.AcErrorAxes.Single().MinStep == 0.078 && model.AcRangeAxes.Single().ForceStepToMin, "summary ticks fixed at 78ps");
                    foreach (var report in model.AcReports)
                    {
                        model.SelectedAcReport = report;
                        foreach (var edge in model.AcEdges)
                        {
                            model.SelectedAcEdge = edge;
                            Check(model.AcPinRows.Count == (fullLog ? 1280 : 2), "all pins for " + edge);
                            Check(model.AcErrorSeries.Length == 3 && model.AcRangeSeries.Length == 1, "bounded curve count");
                            Check(model.AcPinRows.All(r => Math.Abs(r.Range - (r.Max - r.Min)) < 0.0011), "source range agrees within printed precision");
                        }
                    }
                    model.SelectedAcReport = model.AcReports[0]; model.SelectedAcEdge = "ON";
                    Check(model.AcPinRows[0].Max == 0.078 && model.AcPinRows[0].Min == -0.078 && model.AcPinRows[0].Range == 0.156, "Pin0 500MHz ON source values");
                    model.SelectedAcReport = model.AcReports[1];
                    Check(model.AcPinRows[0].Min == 0 && model.AcPinRows[0].Range == 0.078, "clock switch updates graph data");
                    model.BaselineAcReport = model.AcReports[0];
                    Check(model.AcErrorSeries.Length == 3 && model.AcOverviewSummary.Contains("Baseline not overlaid"), "different clocks cannot be overlaid as before/after");
                    model.ClearAcBaselineCommand.Execute(null);
                    var baselinePath = Path.Combine(directory, "before-verification-excerpt.log");
                    await File.WriteAllLinesAsync(baselinePath, new[] {
                        "-----CLK 250.000MHz Verify result------",
                        "----|-------CMPH(ns)-----|-------CMPL(ns)-----|-------RISE(ns)-----|-------FALL(ns)-----|--------ON(ns)------|--------OFF(ns)-----|",
                        model.AcReports[1].Observations.First().RawMessage
                    });
                    await model.LoadFilesFromPathsAsync(new[] { baselinePath });
                    model.SelectedAcReport = model.AcReports[1];
                    model.BaselineAcReport = model.AcReports.Single(r => r.Title.StartsWith("before-verification-excerpt.log"));
                    Check(model.AcErrorSeries.Length == 5 && model.AcRangeSeries.Length == 2 && model.AcOverviewSummary.Contains("Common pins in baseline: 1"), "separate imported same-clock baseline matches only common pins");
                    model.ClearAcBaselineCommand.Execute(null);
                    Check(model.AcErrorSeries.Length == 3, "clear baseline restores current report only");
                    model.SelectedModule = "DPS";
                    Check(!model.IsAcOverviewActive, "non-AC module uses normal Chart");
                    model.SelectedModule = "AC";
                    if (!fullLog)
                        await model.LoadFilesFromPathsAsync(new[] { Path.Combine(Path.GetDirectoryName(path)!, "33_AC_Relative_Real.log") });
                    checks += AcRelativeCheck.Run(model, fullLog);
                    checks += await AcPointCheck.Run(model, Path.Combine(Directory.GetCurrentDirectory(), "validation/fixtures/34_AC_Point_Scopes.log"));
                    if (fullLog)
                    {
                        var source = await File.ReadAllLinesAsync(path);
                        string[] Cells(int line) => source[line - 1][(source[line - 1].IndexOf(" - ", StringComparison.Ordinal) + 3)..]
                            .Split(',').Select(c => c.Trim()).ToArray();
                        foreach (var row in model.AcRelativeRows)
                        {
                            var cells = Cells(row.Line); var reference = Cells(row.ReferenceLine);
                            Check(int.Parse(cells[0]) == row.Pin && double.Parse(cells[5]) == 10 && double.Parse(cells[1]) == row.Measured,
                                "relative row matches original Pin, target and RISE column");
                            Check(int.Parse(reference[0]) == 0 && double.Parse(reference[5]) == 10 &&
                                Math.Abs(row.RelativePs - (double.Parse(cells[1]) - double.Parse(reference[1])) * 1000) < 1e-9,
                                "relative offset independently matches original Pin0 row");
                        }
                        Console.WriteLine(model.AcOverviewSummary);
                    }
                    if (screenshot != null)
                    {
                        var csvPath = Path.ChangeExtension(screenshot, ".csv");
                        await File.WriteAllLinesAsync(csvPath, new[] { "Pin,Group,Measured_ns,Base_ns,Offset_ps,Source_line,Base_line" }
                            .Concat(model.AcRelativeRows.Select(r => $"{r.Pin},{r.Group},{r.Measured:F3},{r.Reference:F3},{r.RelativePs:F3},{r.Line},{r.ReferenceLine}")));
                        await File.WriteAllLinesAsync(Path.Combine(Path.GetDirectoryName(csvPath)!, Path.GetFileNameWithoutExtension(csvPath) + "-groups.csv"),
                            new[] { "Group,First_pin,Last_pin,Valid_pins,Mean_vs_base_pin_ps,Mean_vs_base_group_ps,Group_span_ps" }
                            .Concat(model.AcGroupRows.Select(r => $"{r.Group},{r.FirstPin},{r.LastPin},{r.Count},{r.MeanPs:F3},{r.GroupOffsetPs:F3},{r.SpanPs:F3}")));
                        model.SelectedAcEdge = "RISE";
                        var root = XElement.Load(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
                        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
                        var panel = root.Descendants(wpf + "Grid").Single(x => (string?)x.Attribute(xaml + "Name") == "AcChartPanel");
                        Check(!panel.Descendants(wpf + "DataGrid").Any(), "Chart has no AC data tables");
                        var dataPanel = root.Descendants(wpf + "Grid").Single(x => (string?)x.Attribute(xaml + "Name") == "AcDataPanel");
                        Check(dataPanel.Ancestors(wpf + "TabItem").Single().Descendants(wpf + "TextBlock").Any(x => (string?)x.Attribute("Text") == "Data")
                            && dataPanel.Descendants(wpf + "DataGrid").Count() == 4, "all AC tables are in existing Data tab");
                        Check(panel.Ancestors(wpf + "TabItem").Single().Descendants(wpf + "TextBlock").Any(x => (string?)x.Attribute("Text") == "Chart"), "AC panel is inside existing Chart tab");
                        Check(!root.Descendants(wpf + "TabItem").Any(x => (string?)x.Attribute("Header") == "AC 时序总览"), "no separate AC tab");
                        var panelXml = new XElement(panel);
                        panelXml.SetAttributeValue(XNamespace.Xmlns + "lvc", "clr-namespace:LiveChartsCore.SkiaSharpView.WPF;assembly=LiveChartsCore.SkiaSharpView.WPF");
                        panelXml.SetAttributeValue(XNamespace.Xmlns + "x", xaml.NamespaceName);
                        var content = (Grid)XamlReader.Parse(panelXml.ToString());
                        window = new Window { Title = "Chart - AC Timing Errors - Real LOG Preview", Width = 1250, Height = 850, DataContext = model, Content = content, Background = Brushes.White };
                        window.Show();
                        await Task.Delay(1800);
                        IEnumerable<DependencyObject> Visuals(DependencyObject item)
                        {
                            yield return item;
                            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++)
                                foreach (var child in Visuals(VisualTreeHelper.GetChild(item, i))) yield return child;
                        }
                        var chart = Visuals(content).OfType<LiveChartsCore.SkiaSharpView.WPF.CartesianChart>().First();
                        var bars = model.AcErrorSeries.OfType<LiveChartsCore.SkiaSharpView.ColumnSeries<LiveChartsCore.Defaults.ObservablePoint>>().Single();
                        var previousGroup = model.SelectedAcDetailGroup;
                        var clickedGroup = model.AcGroupRows.First(g => g.Group != previousGroup!.Group);
                        var hit = ((LiveChartsCore.ISeries)bars).Fetch(chart.CoreChart).Single(p => p.Coordinate.SecondaryValue == clickedGroup.Group);
                        ((LiveChartsCore.ISeries)bars).OnDataPointerDown(chart, new[] { hit }, new LiveChartsCore.Drawing.LvcPoint());
                        Check(model.SelectedAcDetailGroup?.Group == clickedGroup.Group, "bar pointer event selects matching detail group");
                        model.SelectedAcDetailGroup = previousGroup;
                        await Task.Delay(300);
                        window.UpdateLayout();
                        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(window);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var output = File.Create(screenshot); encoder.Save(output);
                        Check(File.Exists(screenshot), "WPF preview rendered");
                        var dataXml = new XElement(dataPanel);
                        dataXml.SetAttributeValue(XNamespace.Xmlns + "x", xaml.NamespaceName);
                        window.Content = (Grid)XamlReader.Parse(dataXml.ToString());
                        await Task.Delay(300);
                        window.UpdateLayout();
                        var dataBitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                        dataBitmap.Render(window);
                        var dataEncoder = new PngBitmapEncoder(); dataEncoder.Frames.Add(BitmapFrame.Create(dataBitmap));
                        using var dataOutput = File.Create(Path.Combine(Path.GetDirectoryName(screenshot)!, Path.GetFileNameWithoutExtension(screenshot) + "-Data.png"));
                        dataEncoder.Save(dataOutput);
                    }
                    model.ClearAllCommand.Execute(null);
                    Check(model.AcReports.Count == 0 && model.AcPinRows.Count == 0 && model.AcErrorSeries.Length == 0, "clear removes stale AC charts");
                }
                catch (Exception ex) { failure = ex; }
                finally
                {
                    window?.Close();
                    SqliteConnection.ClearAllPools();
                    if (Directory.Exists(directory)) Directory.Delete(directory, true);
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw new InvalidOperationException("AC check failed", failure);
        Console.WriteLine($"AC: {checks} checks passed ({path}).");
        return checks;
    }
}
