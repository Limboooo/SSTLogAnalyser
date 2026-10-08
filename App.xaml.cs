using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace SSTLogAnalyser;

public partial class App : Application
{
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SSTLogAnalyser", "error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        // Without these handlers an exception in a UI event or fire-and-forget task closes the app silently.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteCrashLog(args.Exception);
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) WriteCrashLog(ex);
        };
        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);
        MessageBox.Show(
            "An unexpected error occurred. The current view may be incomplete; reload the files if it looks wrong.\n\n" +
            e.Exception.Message + "\n\nDetails: " + CrashLogPath,
            "SST Log Analyser", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void WriteCrashLog(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(CrashLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never raise a second error from the crash handler.
        }
    }
}
