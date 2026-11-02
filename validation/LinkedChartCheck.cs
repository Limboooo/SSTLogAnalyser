using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using LiveChartsCore.Drawing;
using LiveChartsCore.SkiaSharpView.WPF;
using Microsoft.Data.Sqlite;
using SSTLogAnalyser.Services;
using SSTLogAnalyser.ViewModels;

static class LinkedChartCheck
{
    public static void Run(string fixtures, string output)
    {
        Exception? failure = null;
        int checks = 0;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                var directory = Path.Combine(Path.GetTempPath(), "SST-linked-check-" + Guid.NewGuid().ToString("N"));
                Window? window = null;
                try
                {
                    void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); checks++; }
                    using var cache = new CacheService(directory);
                    var model = (MainViewModel)Activator.CreateInstance(typeof(MainViewModel), BindingFlags.Instance | BindingFlags.NonPublic,
                        null, new object[] { cache, new LogParser() }, null)!;
                    XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation", xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
                    var source = XElement.Load("MainWindow.xaml");
                    var xml = source.Descendants(wpf + "Grid").Single(x => (string?)x.Attribute(xaml + "Name") == "GeneralChartPanel");
                    xml.SetAttributeValue(XNamespace.Xmlns + "lvc", "clr-namespace:LiveChartsCore.SkiaSharpView.WPF;assembly=LiveChartsCore.SkiaSharpView.WPF");
                    xml.SetAttributeValue(XNamespace.Xmlns + "x", xaml.NamespaceName);
                    XNamespace conv = "clr-namespace:SSTLogAnalyser.Converters;assembly=SSTLogAnalyser";
                    xml.SetAttributeValue(XNamespace.Xmlns + "conv", conv.NamespaceName);
                    xml.AddFirst(new XElement(wpf + "Grid.Resources", source.Element(wpf + "Window.Resources")!.Elements().Select(x => new XElement(x))));
                    foreach (var element in xml.Descendants().Where(x => x.Name.NamespaceName == "clr-namespace:SSTLogAnalyser.Converters"))
                        element.Name = conv + element.Name.LocalName;
                    // Load the production layout without window event handlers; interaction data is checked by UiImportCheck.
                    foreach (var attribute in xml.DescendantsAndSelf().Attributes().Where(a => a.Name.LocalName is
                        "PreviewMouseWheel" or "PreviewMouseLeftButtonDown" or "PreviewMouseLeftButtonUp" or "MouseMove" or "MouseLeave" or "Click").ToArray()) attribute.Remove();
                    var content = (Grid)XamlReader.Parse(xml.ToString());
                    window = new Window { Width = 1100, Height = 850, Content = content, DataContext = model, Title = "Linked chart validation" };
                    var main = (CartesianChart)content.FindName("MainChart");
                    var error = (CartesianChart)content.FindName("LinkedErrorChart");
                    window.Show();
                    Directory.CreateDirectory(output);
                    foreach (var (file, module, item, channel) in new[] {
                        ("36_HDM_Real.log", "HDM", "V, 0.2V, Measure Voltage", 800),
                        ("37_TMUVI_Real.log", "MVPI", "TMUVI MVPI QA Level", 800),
                        ("39_PE_HV_Real.log", "PE", "HV, Force Voltage", 0) })
                    {
                        model.ClearAllCommand.Execute(null);
                        await model.LoadFilesFromPathsAsync(new[] { Path.Combine(fixtures, file) });
                        model.SelectedModule = module; model.SelectedTestItem = item;
                        model.SetSelectedChannels(new[] { channel }); model.SetSelectedLoops(new[] { 1 });
                        model.SelectedChartMode = "Expected vs. Measured";
                        await Task.Delay(1200); window.UpdateLayout();
                        Check(main.IsVisible && error.IsVisible && main.ActualHeight > 150 && error.ActualHeight > 100, module + " both plots rendered");
                        Check(main.ZoomMode == LiveChartsCore.Measure.ZoomAndPanMode.Both && error.ZoomMode == LiveChartsCore.Measure.ZoomAndPanMode.Both, module + " both plots enable independent Y zoom");
                        var targets = model.ChartDataRows.Select(p => p.ExpectValue).ToArray();
                        foreach (var target in new[] { targets.Min(), targets.Max() })
                            Check(Math.Abs(main.ScaleDataToPixels(new LvcPointD(target, 0)).X - error.ScaleDataToPixels(new LvcPointD(target, 0)).X) < 1, module + " aligned X coordinates");
                        var min = targets.Min(); var max = targets.Max();
                        model.XAxes[0].MinLimit = min + (max - min) * .2;
                        model.XAxes[0].MaxLimit = min + (max - min) * .8;
                        await Task.Delay(350);
                        Check(model.LinkedErrorXAxes[0].MinLimit == model.XAxes[0].MinLimit && model.LinkedErrorXAxes[0].MaxLimit == model.XAxes[0].MaxLimit, module + " zoom synchronized");
                        model.YAxes[0].MinLimit = min; model.YAxes[0].MaxLimit = max;
                        Check(model.LinkedErrorYAxes[0].MinLimit == null && model.LinkedErrorYAxes[0].MaxLimit == null, module + " upper Y zoom preserves error scale");
                        model.LinkedErrorYAxes[0].MinLimit = -.0001; model.LinkedErrorYAxes[0].MaxLimit = .0001;
                        Check(model.YAxes[0].MinLimit == min && model.YAxes[0].MaxLimit == max, module + " lower Y zoom preserves measurement scale");
                        model.ResetChartZoomCommand.Execute(null); await Task.Delay(350);
                        Check(model.YAxes[0].MinLimit == null && model.YAxes[0].MaxLimit == null && model.LinkedErrorYAxes[0].MinLimit == null && model.LinkedErrorYAxes[0].MaxLimit == null, module + " Reset restores both Y axes");
                        var png = Path.Combine(output, module + "-linked.png");
                        typeof(SSTLogAnalyser.MainWindow).GetMethod("SaveElementAsPng", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { content, png });
                        Check(new FileInfo(png).Length > 10000, module + " production PNG export");
                        model.SelectedChartMode = "Error"; await Task.Delay(350);
                        Check(!error.IsVisible && main.ActualHeight > 500, module + " Error restores full plot height");
                    }
                }
                catch (Exception ex) { failure = ex; }
                finally
                {
                    window?.Close(); SqliteConnection.ClearAllPools();
                    if (Directory.Exists(directory)) Directory.Delete(directory, true);
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw new InvalidOperationException("Linked chart check failed", failure);
        Console.WriteLine($"Linked WPF charts: {checks} checks passed; previews: {output}");
    }
}
