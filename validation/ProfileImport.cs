using System.Diagnostics;
using System.IO;
using Microsoft.Data.Sqlite;
using SSTLogAnalyser.Services;

static class ProfileImport
{
    public static async Task Run(string path)
    {
        var watch = Stopwatch.StartNew();
        void Report(string stage) { Console.WriteLine($"{stage}: {watch.Elapsed.TotalSeconds:F3}s"); watch.Restart(); }
        Console.WriteLine($"File size: {new FileInfo(path).Length / 1048576d:F2} MiB");
        var hash = await FileHashService.ComputeSha256Async(path);
        Report("Network file SHA256");
        var parsed = await new LogParser().ParseAsync(path);
        Report("Full parser + file read");
        Console.WriteLine($"Points={parsed.TestResults.Count}, events={parsed.DiagnosticObservations.Count}");
        var directory = Path.Combine(Path.GetTempPath(), "SST-profile-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var cache = new CacheService(directory);
            var id = cache.InsertLogFile(parsed.FileInfo, hash);
            Report("Isolated cache initialization");
            cache.InsertTestResults(id, parsed.TestResults);
            cache.InsertCalibrationCoefficients(id, parsed.CalibrationCoefficients);
            cache.InsertDevices(id, parsed.Devices);
            cache.InsertErrors(id, parsed.Errors);
            cache.InsertDiagnosticObservations(id, parsed.DiagnosticObservations);
            cache.InsertSystemInfos(id, parsed.SystemInfos);
            Report("Structured data cache writes");
            cache.MarkFileComplete(id);
            cache.GetDistinctModules(new[] { id });
            Report("Module filter query");
            cache.FindFileByHash(hash);
            Report("Repeated import cache lookup");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
