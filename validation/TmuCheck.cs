using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.WPF;
using Microsoft.Data.Sqlite;
using SSTLogAnalyser.Converters;
using SSTLogAnalyser.Services;
using SSTLogAnalyser.ViewModels;

static class TmuCheck
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
                var directory = Path.Combine(Path.GetTempPath(), "SST-tmu-check-" + Guid.NewGuid().ToString("N"));
                Window? window = null;
                try
                {
                    void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException("TMU: " + message); checks++; }
                    var parsed = await new LogParser().ParseAsync(path);
                    var raw = await File.ReadAllLinesAsync(path);
                    Check(parsed.TestResults.Count == (fullLog ? 49152 : 32), "all source verification values retained");
                    foreach (var p in parsed.TestResults)
                    {
                        var cells = raw[p.LineNumber - 1].Split("INFO  - ", 2)[^1].Split(',');
                        int tmu = p.TestItemName.Contains("Tmu0_") ? 0 : 1;
                        var names = new[] { "VOLStart_VOLStop", "VOHStart_VOHStop", "VOHStart_VOLStop", "VOLStart_VOHStop" };
                        int column = Array.FindIndex(names, n => p.TestItemName.Contains("_" + n + " (ns)"));
                        Check(int.Parse(cells[0]) == p.ChannelId && double.Parse(cells[1 + tmu * 4 + column], CultureInfo.InvariantCulture) == p.MeasureValue &&
                            Math.Abs(p.Difference - (p.MeasureValue - p.ExpectValue)) < 1e-10, "source Pin, column, reference and error");
                    }
                    Check(parsed.TestResults.All(p => !p.HasExplicitResult && !p.IsFailed), "blank result remains Unknown; values within limits");
                    if (fullLog)
                    {
                        Check(Math.Abs(parsed.TestResults.Min(p => p.Difference) + .597) < 1e-9 && Math.Abs(parsed.TestResults.Max(p => p.Difference) - .655) < 1e-9, "independent full LOG extremes");
                        Check(parsed.DiagnosticObservations.Count(p => p.Kind == "Clock" && p.Item == "EC" && p.Status == "Pass" && p.Target.HasValue) == 192, "192 clock EC observations have reference frequency");
                    }
                    using var cache = new CacheService(directory);
                    var model = (MainViewModel)Activator.CreateInstance(typeof(MainViewModel), BindingFlags.Instance | BindingFlags.NonPublic,
                        null, new object[] { cache, new LogParser() }, null)!;
                    await model.LoadFilesFromPathsAsync(new[] { path });
                    Check(!model.StatusText.StartsWith("Error:"), "real import succeeds");
                    model.SelectedModule = "TMU";
                    Check(model.HasTmuCharts && model.TmuReports.Count == 1 && model.TmuReports[0].Title.Contains("Loop Count [00]"), "dedicated Chart and source path loop boundary");
                    Check(model.TmuReferences.SequenceEqual(fullLog ? new double[] { 160, 320, 640, 800, 1600, 2000 } : new double[] { 160, 2000 }), "all references sorted numerically");
                    model.SelectedTmuReference = 2000; model.SelectedTmuPin = 9;
                    Check(model.TmuDetailRows.Count == 8 && model.TmuDetailRows.Single(p => p.Tmu == 0 && p.Path == 3).Value is var peak && Math.Abs(peak - .655) < 1e-9, "Pin9 TMU0 VOL→VOH source error");
                    Check(model.Tmu0Series.OfType<ScatterSeries<ObservablePoint>>().Sum(s => s.Values!.Count()) == (fullLog ? 4096 : 8), "all four paths plotted by Pin");
                    model.TmuVolVol = false;
                    Check(model.Tmu0Series.OfType<ScatterSeries<ObservablePoint>>().Count() == 3, "path visibility updates plot");
                    model.TmuVolVol = true;
                    model.SelectedTmuReference = 160;
                    Check(model.TmuDetailRows.All(p => p.Reference == 160), "reference changes linked details");
                    model.SelectedTmuPinRange = model.TmuPinRanges.Single(r => r.First == 0);
                    Check(model.Tmu0Series.OfType<ScatterSeries<ObservablePoint>>().SelectMany(s => s.Values!).All(p => p.X is >= 0 and <= 31), "32-Pin range");
                    model.SelectedTmuTrendMode = model.TmuTrendModes[1];
                    Check(model.TmuTrendSeries.OfType<LineSeries<ObservablePoint>>().Count(s => s.Name!.Contains("Min–Max")) == 2, "both TMU ranges present");
                    model.SelectedTmuView = model.TmuViews[1];
                    Check(model.TmuReports.Count == 1 && model.TmuDetailRows.Count == 4 && model.TmuDetailRows.All(p => p.Low == null && p.High == null), "calibration table cached independently without verification limits");
                    foreach (var p in model.TmuDetailRows)
                    {
                        var cells = raw[p.Line - 1].Split("INFO  - ", 2)[^1].Trim().TrimEnd(';', ' ').Split(',');
                        Check(Math.Abs(p.Value-double.Parse(cells[8+p.Path], CultureInfo.InvariantCulture)) < 1e-9, "PinDiff comes from original last two columns");
                    }
                    model.SelectedTmuView = model.TmuViews[2];
                    Check(model.TmuReports.Count == 1 && model.TmuReferences.Count == 6 && !model.HasTmuSecondChart, "six distinct frequencies, no invented TMU1 clock");
                    model.SelectedTmuPin = 0;
                    Check(model.TmuDetailRows.Count == 1 && model.TmuDetailRows[0].SourceStatus == "Pass" && model.TmuDetailRows[0].Low == -10 && model.TmuDetailRows[0].High == 10, "clock context carries frequency and ±10ppm limits");
                    model.SelectedModule = "DPS"; Check(!model.HasTmuCharts, "other modules retain existing chart");
                    model.SelectedModule = "TMU"; model.SelectedTmuView = model.TmuViews[0]; model.SelectedTmuPinRange = model.TmuPinRanges[0];
                    model.SelectedTmuReference = 2000; model.SelectedTmuPin = 9; model.SelectedTmuTrendMode = model.TmuTrendModes[0];
                    if (screenshot != null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(screenshot))!);
                        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation", xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
                        var xml = XElement.Load("MainWindow.xaml").Descendants(wpf+"Grid").Single(x => (string?)x.Attribute(xaml+"Name") == "TmuChartPanel");
                        xml.SetAttributeValue(XNamespace.Xmlns+"lvc", "clr-namespace:LiveChartsCore.SkiaSharpView.WPF;assembly=LiveChartsCore.SkiaSharpView.WPF");
                        xml.SetAttributeValue(XNamespace.Xmlns+"x", xaml.NamespaceName);
                        // Supply the same converter before parsing the extracted live application panel.
                        xml.SetAttributeValue(XNamespace.Xmlns+"conv", "clr-namespace:SSTLogAnalyser.Converters;assembly=SSTLogAnalyser");
                        xml.AddFirst(new XElement(wpf+"Grid.Resources", new XElement(XName.Get("BoolToVisibilityConverter", "clr-namespace:SSTLogAnalyser.Converters;assembly=SSTLogAnalyser"), new XAttribute(xaml+"Key", "BoolToVis"))));
                        var content = (Grid)XamlReader.Parse(xml.ToString());
                        window = new Window { Width=1250, Height=1450, Content = new ScrollViewer { Content=content }, DataContext=model, Background=Brushes.White, Title="TMU real LOG validation" };
                        window.Show(); await Task.Delay(1600); window.UpdateLayout();
                        IEnumerable<DependencyObject> Visuals(DependencyObject item) { yield return item; for (int i=0;i<VisualTreeHelper.GetChildrenCount(item);i++) foreach(var c in Visuals(VisualTreeHelper.GetChild(item,i))) yield return c; }
                        var chart = Visuals(content).OfType<CartesianChart>().First();
                        var scatter = model.Tmu0Series.OfType<ScatterSeries<ObservablePoint>>().First();
                        var hit = ((ISeries)scatter).Fetch(chart.CoreChart).First(p => p.Coordinate.SecondaryValue != 9);
                        ((ISeries)scatter).OnDataPointerDown(chart, new[] { hit }, new LiveChartsCore.Drawing.LvcPoint());
                        Check(model.SelectedTmuPin == (int)hit.Coordinate.SecondaryValue && model.TmuDetailRows.All(p=>p.Pin==model.SelectedTmuPin), "actual chart click updates Pin, trend and details");
                        model.SelectedTmuPin=9; await Task.Delay(300); window.UpdateLayout();
                        var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
                        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(screenshot))encoder.Save(output);
                        Check(File.Exists(screenshot), "actual WPF chart rendered");
                    }
                    model.ClearAllCommand.Execute(null);
                    Check(!model.HasTmuCharts && model.TmuReports.Count == 0 && model.TmuDetailRows.Count == 0 && model.Tmu0Series.Length == 0, "clear removes all TMU data and plots");
                    await model.LoadFilesFromPathsAsync(new[] { path }); model.SelectedModule = "TMU";
                    Check(model.LoadedFiles.Count == 1 && model.HasTmuCharts && model.TmuReports.Count == 1, "reimport from SQLite cache reconstructs reports");
                    var edgePath=Path.Combine(directory,"contexts.log");
                    var edge=new[]{"Start TMU Path Verification","Expect error between -1.5ns ~ 1.5ns","**********Loop Count [00]**********","Ref TestTime ActualValue = 160ns ,",
                        "PinNum,Tmu0_VOLStart_VOLStop,Tmu0_VOHStart_VOHStop,Tmu0_VOHStart_VOLStop,Tmu0_VOLStart_VOHStop,Tmu1_VOLStart_VOLStop,Tmu1_VOHStart_VOHStop,Tmu1_VOHStart_VOLStop,Tmu1_VOLStart_VOHStop,VerifyResult,",
                        "0000, 160.1, 160.2, 160.3, 160.4, 160.1, 160.2, 160.3, 160.4,", "0000, 160.1, 160.2, 160.3, 160.4, 160.1, 160.2, 160.3, 160.4,",
                        "**********Loop Count [01]**********","Ref TestTime ActualValue = 160ns ,",
                        "PinNum,Tmu0_VOLStart_VOLStop,Tmu0_VOHStart_VOHStop,Tmu0_VOHStart_VOLStop,Tmu0_VOLStart_VOHStop,Tmu1_VOLStart_VOLStop,Tmu1_VOHStart_VOHStop,Tmu1_VOHStart_VOLStop,Tmu1_VOLStart_VOHStop,VerifyResult,",
                        "0000, 160.2, 160.2, 160.3, 160.4, 160.1, 160.2, 160.3, 160.4,"};
                    await File.WriteAllLinesAsync(edgePath,edge);
                    await model.LoadFilesFromPathsAsync(new[]{edgePath}); model.SelectedTmuView=model.TmuViews[0];
                    Check(model.TmuReports.Count==3,"files and internal path loops remain separate");
                    model.SelectedTmuReport=model.TmuReports.Single(r=>r.Title.Contains("contexts.log")&&r.Title.Contains("[00]"));
                    Check(model.TmuDetailRows.Count==0&&model.TmuSummary.Contains("duplicate keys"),"ambiguous duplicate points excluded");
                    model.SelectedTmuPin=0;model.SelectedTmuReport=model.TmuReports.Single(r=>r.Title.Contains("contexts.log")&&r.Title.Contains("[01]"));
                    Check(model.TmuDetailRows.Count==8&&Math.Abs(model.TmuDetailRows[0].Value-.2)<1e-9,"next path loop is independent");
                    model.SetSelectedLoops(new[]{999}); Check(!model.HasTmuCharts,"outer loop filter cannot leak another loop");
                }
                catch(Exception ex) { failure=ex; }
                finally { window?.Close();SqliteConnection.ClearAllPools();if(Directory.Exists(directory))Directory.Delete(directory,true);dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(failure!=null)throw new InvalidOperationException("TMU check failed",failure);
        Console.WriteLine($"TMU: {checks} checks passed ({path}).");return checks;
    }
}
