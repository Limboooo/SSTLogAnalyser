using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using SSTLogAnalyser.Services;
using SSTLogAnalyser.ViewModels;

static class AcPointLogCheck
{
    public static void Run(string path, string screenshot)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                string directory = Path.Combine(Path.GetTempPath(), "SST-point-map-" + Guid.NewGuid().ToString("N"));
                Window? window = null; int checks = 0;
                void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); checks++; }
                try
                {
                    using var cache = new CacheService(directory);
                    var model = (MainViewModel)Activator.CreateInstance(typeof(MainViewModel), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { cache, new LogParser() }, null)!;
                    await model.LoadFilesFromPathsAsync(new[] { path });
                    model.SelectedModule = "AC"; model.SelectedAcEdge = "RISE";
                    model.AcRelativeMode = true; model.SelectedAcReferencePin = 0;
                    model.SelectedAcAlignmentSet = model.AcAlignmentSets.First(t => t.Clock == "500.000 MHz" && t.Target == 10);
                    model.AcPointView = true;
                    Check(model.AcPointCells.Count == 180 && model.AcPointCells.All(c => c.Count == 64), "9 points × 20 groups, each 64 valid pins");
                    model.SelectedAcGroupSize = 128;
                    Check(model.AcPointCells.Count == 90 && model.AcPointCells.All(c => c.Count == 128), "standard: 9 points × 10 groups");
                    model.SelectedAcGroupSize = 64;
                    foreach (var cell in model.AcPointCells)
                    {
                        var table = model.AcAlignmentSets.Single(t => t.TableLine == cell.TableLine);
                        var values = table.Points.Where(p => p.TestItemName == "AC RISE(ns)" && p.ChannelId >= cell.Entity * 64 && p.ChannelId < (cell.Entity + 1) * 64).ToArray();
                        Check(table.Clock == "500.000 MHz" && table.SweepLine == model.SelectedAcAlignmentSet.SweepLine && table.Points.All(p => p.LoopIndex == 1), "matrix scope must stay within original sweep");
                        Check(Math.Abs(cell.ValuePs - values.Average(p => (p.MeasureValue - p.ExpectValue) * 1000)) < 1e-8, "group heat value vs measured-target");
                    }
                    model.SelectedAcAlignmentSet = model.AcAlignmentSets.First(t => t.Clock == "250.000 MHz" && t.Target == 10);
                    Check(model.AcPointCells.Count == 180 && model.AcPointCells.All(c => model.AcAlignmentSets.Single(t => t.TableLine == c.TableLine).Clock == "250.000 MHz"), "250MHz must not mix with 500MHz");
                    model.SelectedAcAlignmentSet = model.AcAlignmentSets.First(t => t.Clock == "500.000 MHz" && t.Target == 10);
                    model.SelectedAcDetailGroup = model.AcGroupRows.Single(g => g.Group == 0);
                    model.SelectedAcPointDimension = "Pins in selected group";
                    Check(model.AcPointCells.Count == 576 && model.AcPointCells.All(c => c.Count == 1), "64 pins × 9 points");
                    model.SelectedAcPointMetric = "Offset vs base pin";
                    Check(model.AcPointCells.Where(c => c.Entity == 0).Count() == 9 && model.AcPointCells.Where(c => c.Entity == 0).All(c => c.ValuePs == 0), "base pin is zero at every point");
                    model.SelectedAcPointDimension = "Groups"; model.SelectedAcPointMetric = "Error vs target";
                    model.SelectedAcPointRow = model.AcPointRows.Single(r => r.Id == 4);
                    var root = XElement.Load(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
                    XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation", x = "http://schemas.microsoft.com/winfx/2006/xaml";
                    object Panel(string name)
                    {
                        var element = new XElement(root.Descendants(wpf + "Grid").Single(e => (string?)e.Attribute(x + "Name") == name));
                        element.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
                        element.SetAttributeValue(XNamespace.Xmlns + "lvc", "clr-namespace:LiveChartsCore.SkiaSharpView.WPF;assembly=LiveChartsCore.SkiaSharpView.WPF");
                        return XamlReader.Parse(element.ToString());
                    }
                    var chartPanel = (System.Windows.Controls.Grid)Panel("AcChartPanel");
                    window = new Window { Width = 1350, Height = 950, Content = chartPanel, DataContext = model, Background = Brushes.White };
                    window.Show(); await Task.Delay(1600);
                    IEnumerable<DependencyObject> Visuals(DependencyObject item)
                    {
                        yield return item;
                        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++)
                            foreach (var child in Visuals(VisualTreeHelper.GetChild(item, i))) yield return child;
                    }
                    var chart = Visuals(chartPanel).OfType<LiveChartsCore.SkiaSharpView.WPF.CartesianChart>().First(c => c.IsVisible && c.Series!.Any(s => s is LiveChartsCore.SkiaSharpView.HeatSeries<LiveChartsCore.Defaults.WeightedPoint>));
                    var heat = model.AcPointHeatSeries.Single();
                    var hit = heat.Fetch(chart.CoreChart).Single(p => p.Coordinate.SecondaryValue == 1 && p.Coordinate.PrimaryValue == 4);
                    heat.OnDataPointerDown(chart, new[] { hit }, new LiveChartsCore.Drawing.LvcPoint());
                    Check(model.SelectedAcAlignmentSet.TableLine == model.AcPointCells.First(c => c.Column == 1).TableLine && model.SelectedAcPointRow?.Id == 4 && model.SelectedAcDetailGroup?.Group == 4, "heat cell pointer links point, trend and group detail");
                    await Task.Delay(350);
                    void Capture(string outputPath)
                    {
                        window.UpdateLayout();
                        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var output = File.Create(outputPath); encoder.Save(output);
                    }
                    Capture(screenshot);
                    window.Content = Panel("AcDataPanel"); await Task.Delay(200);
                    Capture(Path.Combine(Path.GetDirectoryName(screenshot)!, Path.GetFileNameWithoutExtension(screenshot) + "-Data.png"));
                    Console.WriteLine($"PASS AC point map: {checks} checks. " + model.AcPointSummary);
                }
                catch (Exception ex) { failure = ex; }
                finally
                {
                    window?.Close(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                    if (Directory.Exists(directory)) Directory.Delete(directory, true);
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw new InvalidOperationException("AC point log validation failed", failure);
    }
}
