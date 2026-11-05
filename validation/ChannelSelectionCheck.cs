using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SSTLogAnalyser;
using SSTLogAnalyser.Services;
using SSTLogAnalyser.ViewModels;

static class ChannelSelectionCheck
{
    public static int Run(bool preview = false)
    {
        int checks = 0;
        void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException("Channel selection: " + message);
            checks++;
        }
        foreach (var module in Enum.GetNames<SSTLogAnalyser.Models.ModuleType>())
            Check(ChannelSlotSelection.GetSlot(800, module, "Slot x 100") == 8, module + " configurable rule");
        foreach (var module in new[] { "MVPI", "MDM", "HVPI", "HDM", "HVI", "DPSI", "MXVI" })
        {
            Check(ChannelSlotSelection.GetSlot(831, module, "Module default") == 8, module + " hundred encoding");
            Check(ChannelSlotSelection.GetSlot(900, module, "Module default") == 9, module + " next slot");
        }
        Check(ChannelSlotSelection.GetSlot(15, "DPS", "Module default") == 1, "DPS 8 channels");
        Check(ChannelSlotSelection.GetSlot(16, "PMU", "Module default") == 2, "PMU boundary");
        Check(ChannelSlotSelection.GetSlot(127, "AC", "Module default") == 0, "128 pins boundary");
        Check(ChannelSlotSelection.GetSlot(128, "PPMU", "Module default") == 1, "next pin slot");
        Check(ChannelSlotSelection.GetSlot(64, "PE", "64 pins / slot") == 1, "64-pin override");
        Check(ChannelSlotSelection.GetSlot(32, "TMU", "32 pins / slot") == 1, "32-pin override");
        foreach (var (channel, slot) in new[] { (0, 8), (15, 8), (16, 7), (112, 1), (128, 9), (239, 15) })
            Check(ChannelSlotSelection.GetSlot(channel, "MIXI", "Module default") == slot, "MIXI physical slot");

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(() =>
            {
                MainWindow? window = null;
                try
                {
                    window = new MainWindow { Title = "Channel selection check" };
                    window.Show();
                    window.UpdateLayout();
                    var model = (MainViewModel)window.DataContext;
                    model.SelectedModule = "MVPI";
                    model.AvailableChannels.Clear();
                    foreach (var c in new[] { 800, 801, 805, 900, 903 }) model.AvailableChannels.Add(c);
                    var list = (ListBox)window.FindName("ChannelList");
                    var slots = (ComboBox)window.FindName("ChannelSlotPicker");
                    Check(slots.Items.Count == 2, "sparse slots from current available channels");
                    slots.SelectedIndex = 0;
                    Check(model.SelectedChannels.SequenceEqual(new[] { 800, 801, 805 }), "select entire slot without filling absent channels");
                    Check(list.SelectionMode == SelectionMode.Extended && list.SelectedItems.Count == 3, "native single-click with drag multi-selection");
                    slots.SelectedIndex = 1;
                    Check(model.SelectedChannels.SequenceEqual(new[] { 900, 903 }), "slot replaces selection");
                    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    void Field(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
                    void Drag(int start, int end, bool append = false)
                    {
                        Field("_channelDragAnchor", start);
                        Field("_channelDragBase", append ? list.SelectedItems.OfType<int>().ToArray() : Array.Empty<int>());
                        Field("_suppressChannelSelectionChanged", true);
                        typeof(MainWindow).GetMethod("UpdateDraggedChannelSelection", flags)!.Invoke(window, new object[] { end });
                        typeof(MainWindow).GetMethod("FinishChannelDrag", flags)!.Invoke(window, null);
                    }
                    Drag(0, 2);
                    Check(model.SelectedChannels.SequenceEqual(new[] { 800, 801, 805 }), "drag selects a sparse range and replaces previous selection");
                    slots.SelectedIndex = 1;
                    Drag(2, 0);
                    Check(model.SelectedChannels.SequenceEqual(new[] { 800, 801, 805 }), "reverse drag selects the same range");
                    Drag(3, 4, append: true);
                    Check(model.SelectedChannels.Count == 5, "Ctrl drag can append a range");
                    model.AvailableChannels.Clear();
                    Check(slots.Items.Count == 0, "empty module clears slot choices");
                    foreach (var c in Enumerable.Range(800, 32).Concat(Enumerable.Range(900, 16))) model.AvailableChannels.Add(c);
                    list.UnselectAll();
                    if (preview)
                    {
                        window.Show();
                        window.Closed += (_, _) => dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                        return;
                    }
                }
                catch (Exception ex) { failure = ex; }
                window?.Close();
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure != null) throw failure;
        Console.WriteLine($"Channel selection PASS: {checks} checks.");
        return checks;
    }
}
