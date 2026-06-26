using System.IO;
using Microsoft.Data.Sqlite;
using SSTLogAnalyser.Models;

namespace SSTLogAnalyser.Services;

public class CacheService : IDisposable
{
    private readonly string _dbPath;
    private SqliteConnection _connection;

    public CacheService(string? dbDirectory = null)
    {
        dbDirectory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SSTLogAnalyser");
        Directory.CreateDirectory(dbDirectory);
        _dbPath = Path.Combine(dbDirectory, "cache.db");
        _connection = new SqliteConnection("Data Source=" + _dbPath);
        _connection.Open();
        InitializeSchema();
    }

    private void InitializeSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS log_files (
                file_id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_path TEXT NOT NULL,
                file_name TEXT NOT NULL,
                file_hash TEXT NOT NULL UNIQUE,
                file_size INTEGER NOT NULL,
                parse_time TEXT NOT NULL,
                loop_count INTEGER DEFAULT 0,
                tool_version TEXT DEFAULT ''
            );
            CREATE TABLE IF NOT EXISTS test_results (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_id INTEGER NOT NULL,
                loop_index INTEGER DEFAULT 0,
                module_type TEXT NOT NULL,
                slot_number INTEGER DEFAULT 0,
                channel_id INTEGER DEFAULT 0,
                test_item_name TEXT NOT NULL,
                expect_value REAL DEFAULT 0,
                measure_value REAL DEFAULT 0,
                low_limit REAL DEFAULT 0,
                up_limit REAL DEFAULT 0,
                difference_value REAL DEFAULT 0,
                is_failed INTEGER DEFAULT 0,
                is_retest INTEGER DEFAULT 0,
                line_number INTEGER DEFAULT 0,
                FOREIGN KEY (file_id) REFERENCES log_files(file_id)
            );
            CREATE INDEX IF NOT EXISTS idx_tr_lookup
                ON test_results(file_id, module_type, channel_id, test_item_name, loop_index);
            CREATE TABLE IF NOT EXISTS device_info (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_id INTEGER NOT NULL,
                group_number INTEGER,
                location_id INTEGER,
                slot_info TEXT DEFAULT '',
                device_name TEXT DEFAULT '',
                FOREIGN KEY (file_id) REFERENCES log_files(file_id)
            );
            CREATE TABLE IF NOT EXISTS error_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_id INTEGER NOT NULL,
                loop_index INTEGER DEFAULT 0,
                timestamp TEXT DEFAULT '',
                level TEXT DEFAULT '',
                message TEXT DEFAULT '',
                line_number INTEGER DEFAULT 0,
                FOREIGN KEY (file_id) REFERENCES log_files(file_id)
            );
            CREATE TABLE IF NOT EXISTS system_info (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_id INTEGER NOT NULL,
                key TEXT NOT NULL,
                value TEXT DEFAULT '',
                FOREIGN KEY (file_id) REFERENCES log_files(file_id)
            );";
        cmd.ExecuteNonQuery();
    }

    public long? FindFileByHash(string hash)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT file_id FROM log_files WHERE file_hash = @hash";
        cmd.Parameters.AddWithValue("@hash", hash);
        var result = cmd.ExecuteScalar();
        return result as long?;
    }

    public LogFileInfo? GetFileInfo(long fileId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM log_files WHERE file_id = @id";
        cmd.Parameters.AddWithValue("@id", fileId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return MapLogFile(reader);
    }

    public long InsertLogFile(LogFileInfo info, string hash)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO log_files (file_path, file_name, file_hash, file_size, parse_time, loop_count, tool_version)
            VALUES (@path, @name, @hash, @size, @time, @loops, @ver);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@path", info.FilePath);
        cmd.Parameters.AddWithValue("@name", info.FileName);
        cmd.Parameters.AddWithValue("@hash", hash);
        cmd.Parameters.AddWithValue("@size", info.FileSize);
        cmd.Parameters.AddWithValue("@time", info.ParseTime.ToString("o"));
        cmd.Parameters.AddWithValue("@loops", info.LoopCount);
        cmd.Parameters.AddWithValue("@ver", info.ToolVersion);
        return (long)cmd.ExecuteScalar()!;
    }

    public void InsertTestResults(long fileId, IEnumerable<TestResult> results)
    {
        using var transaction = _connection.BeginTransaction();
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"INSERT INTO test_results (file_id, loop_index, module_type, slot_number, channel_id,
                test_item_name, expect_value, measure_value, low_limit, up_limit,
                difference_value, is_failed, is_retest, line_number)
            VALUES (@fid, @loop, @mod, @slot, @ch, @test, @exp, @meas, @low, @up, @diff, @fail, @retest, @line)";

        var pFid = cmd.CreateParameter(); pFid.ParameterName = "@fid"; cmd.Parameters.Add(pFid);
        var pLoop = cmd.CreateParameter(); pLoop.ParameterName = "@loop"; cmd.Parameters.Add(pLoop);
        var pMod = cmd.CreateParameter(); pMod.ParameterName = "@mod"; cmd.Parameters.Add(pMod);
        var pSlot = cmd.CreateParameter(); pSlot.ParameterName = "@slot"; cmd.Parameters.Add(pSlot);
        var pCh = cmd.CreateParameter(); pCh.ParameterName = "@ch"; cmd.Parameters.Add(pCh);
        var pTest = cmd.CreateParameter(); pTest.ParameterName = "@test"; cmd.Parameters.Add(pTest);
        var pExp = cmd.CreateParameter(); pExp.ParameterName = "@exp"; cmd.Parameters.Add(pExp);
        var pMeas = cmd.CreateParameter(); pMeas.ParameterName = "@meas"; cmd.Parameters.Add(pMeas);
        var pLow = cmd.CreateParameter(); pLow.ParameterName = "@low"; cmd.Parameters.Add(pLow);
        var pUp = cmd.CreateParameter(); pUp.ParameterName = "@up"; cmd.Parameters.Add(pUp);
        var pDiff = cmd.CreateParameter(); pDiff.ParameterName = "@diff"; cmd.Parameters.Add(pDiff);
        var pFail = cmd.CreateParameter(); pFail.ParameterName = "@fail"; cmd.Parameters.Add(pFail);
        var pRetest = cmd.CreateParameter(); pRetest.ParameterName = "@retest"; cmd.Parameters.Add(pRetest);
        var pLine = cmd.CreateParameter(); pLine.ParameterName = "@line"; cmd.Parameters.Add(pLine);

        foreach (var r in results)
        {
            pFid.Value = fileId;
            pLoop.Value = r.LoopIndex;
            pMod.Value = r.ModuleType.ToString();
            pSlot.Value = r.SlotNumber;
            pCh.Value = r.ChannelId;
            pTest.Value = r.TestItemName;
            pExp.Value = r.ExpectValue;
            pMeas.Value = r.MeasureValue;
            pLow.Value = r.LowLimit;
            pUp.Value = r.UpLimit;
            pDiff.Value = r.Difference;
            pFail.Value = r.IsFailed ? 1 : 0;
            pRetest.Value = r.IsReTest ? 1 : 0;
            pLine.Value = r.LineNumber;
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void InsertDevices(long fileId, IEnumerable<DeviceInfo> devices)
    {
        using var transaction = _connection.BeginTransaction();
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "INSERT INTO device_info (file_id, group_number, location_id, slot_info, device_name) VALUES (@fid, @grp, @loc, @slot, @name)";
        foreach (var d in devices)
        {
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@fid", fileId);
            cmd.Parameters.AddWithValue("@grp", d.GroupNumber);
            cmd.Parameters.AddWithValue("@loc", d.LocationId);
            cmd.Parameters.AddWithValue("@slot", d.SlotInfo);
            cmd.Parameters.AddWithValue("@name", d.DeviceName);
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void InsertErrors(long fileId, IEnumerable<ErrorLogEntry> errors)
    {
        using var transaction = _connection.BeginTransaction();
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "INSERT INTO error_log (file_id, loop_index, timestamp, level, message, line_number) VALUES (@fid, @loop, @ts, @level, @msg, @line)";
        foreach (var e in errors)
        {
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@fid", fileId);
            cmd.Parameters.AddWithValue("@loop", e.LoopIndex);
            cmd.Parameters.AddWithValue("@ts", e.Timestamp);
            cmd.Parameters.AddWithValue("@level", e.Level);
            cmd.Parameters.AddWithValue("@msg", e.Message);
            cmd.Parameters.AddWithValue("@line", e.LineNumber);
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void InsertSystemInfos(long fileId, IEnumerable<SystemInfo> infos)
    {
        using var transaction = _connection.BeginTransaction();
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "INSERT INTO system_info (file_id, key, value) VALUES (@fid, @key, @val)";
        foreach (var s in infos)
        {
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@fid", fileId);
            cmd.Parameters.AddWithValue("@key", s.Key);
            cmd.Parameters.AddWithValue("@val", s.Value);
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public List<string> GetDistinctModules(long[] fileIds)
    {
        using var cmd = _connection.CreateCommand();
        var where = BuildFileIdWhere(fileIds);
        cmd.CommandText = "SELECT DISTINCT module_type FROM test_results WHERE " + where + " ORDER BY module_type";
        AddFileIdParams(cmd, fileIds);
        var list = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(reader.GetString(0));
        return list;
    }

    public List<int> GetDistinctChannels(long[] fileIds, string? moduleType = null)
    {
        using var cmd = _connection.CreateCommand();
        var where = BuildFileIdWhere(fileIds);
        if (moduleType != null) where += " AND module_type = @mod";
        cmd.CommandText = "SELECT DISTINCT channel_id FROM test_results WHERE " + where + " ORDER BY channel_id";
        if (moduleType != null) cmd.Parameters.AddWithValue("@mod", moduleType);
        AddFileIdParams(cmd, fileIds);
        var list = new List<int>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(reader.GetInt32(0));
        return list;
    }

    public List<string> GetDistinctTestItems(long[] fileIds, string? moduleType = null, int? channelId = null)
    {
        using var cmd = _connection.CreateCommand();
        var where = BuildFileIdWhere(fileIds);
        if (moduleType != null) where += " AND module_type = @mod";
        if (channelId.HasValue) where += " AND channel_id = @ch";
        cmd.CommandText = "SELECT DISTINCT test_item_name FROM test_results WHERE " + where + " ORDER BY test_item_name";
        if (moduleType != null) cmd.Parameters.AddWithValue("@mod", moduleType);
        if (channelId.HasValue) cmd.Parameters.AddWithValue("@ch", channelId.Value);
        AddFileIdParams(cmd, fileIds);
        var list = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(reader.GetString(0));
        return list;
    }

    public List<int> GetDistinctLoops(long[] fileIds, string? moduleType = null, string? testItem = null, int? channelId = null)
    {
        using var cmd = _connection.CreateCommand();
        var where = BuildFileIdWhere(fileIds);
        if (moduleType != null) where += " AND module_type = @mod";
        if (testItem != null) where += " AND test_item_name = @test";
        if (channelId.HasValue) where += " AND channel_id = @ch";
        cmd.CommandText = "SELECT DISTINCT loop_index FROM test_results WHERE " + where + " ORDER BY loop_index";
        if (moduleType != null) cmd.Parameters.AddWithValue("@mod", moduleType);
        if (testItem != null) cmd.Parameters.AddWithValue("@test", testItem);
        if (channelId.HasValue) cmd.Parameters.AddWithValue("@ch", channelId.Value);
        AddFileIdParams(cmd, fileIds);
        var list = new List<int>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(reader.GetInt32(0));
        return list;
    }

    public List<TestResult> QueryTestResults(long[] fileIds, string? moduleType, string? testItem,
        int[]? channels, int[]? loops, string? search = null)
    {
        using var cmd = _connection.CreateCommand();
        var where = BuildFileIdWhere(fileIds);
        if (moduleType != null) where += " AND module_type = @mod";
        if (testItem != null) where += " AND test_item_name = @test";
        if (search != null) where += " AND test_item_name LIKE @search";
        cmd.CommandText = "SELECT file_id, loop_index, module_type, slot_number, channel_id, test_item_name, expect_value, measure_value, low_limit, up_limit, difference_value, is_failed, is_retest, line_number FROM test_results WHERE " + where + " ORDER BY loop_index, channel_id, expect_value";
        if (moduleType != null) cmd.Parameters.AddWithValue("@mod", moduleType);
        if (testItem != null) cmd.Parameters.AddWithValue("@test", testItem);
        if (search != null) cmd.Parameters.AddWithValue("@search", "%" + search + "%");
        AddFileIdParams(cmd, fileIds);

        var list = new List<TestResult>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(MapTestResult(reader));

        if (channels != null && channels.Length > 0)
        {
            var chSet = new HashSet<int>(channels);
            list = list.Where(r => chSet.Contains(r.ChannelId)).ToList();
        }
        if (loops != null && loops.Length > 0)
        {
            var lpSet = new HashSet<int>(loops);
            list = list.Where(r => lpSet.Contains(r.LoopIndex)).ToList();
        }
        return list;
    }

    public List<ErrorLogEntry> GetErrors(long[] fileIds)
    {
        using var cmd = _connection.CreateCommand();
        var where = BuildFileIdWhere(fileIds);
        cmd.CommandText = "SELECT file_id, loop_index, timestamp, level, message, line_number FROM error_log WHERE " + where + " ORDER BY line_number";
        AddFileIdParams(cmd, fileIds);
        var list = new List<ErrorLogEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ErrorLogEntry
            {
                FileId = reader.GetInt64(0),
                LoopIndex = reader.GetInt32(1),
                Timestamp = reader.GetString(2),
                Level = reader.GetString(3),
                Message = reader.GetString(4),
                LineNumber = reader.GetInt32(5)
            });
        }
        return list;
    }

    public List<DeviceInfo> GetDevices(long[] fileIds)
    {
        using var cmd = _connection.CreateCommand();
        var where = BuildFileIdWhere(fileIds);
        cmd.CommandText = "SELECT file_id, group_number, location_id, slot_info, device_name FROM device_info WHERE " + where + " ORDER BY group_number, location_id";
        AddFileIdParams(cmd, fileIds);
        var list = new List<DeviceInfo>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new DeviceInfo
            {
                FileId = reader.GetInt64(0),
                GroupNumber = reader.GetInt32(1),
                LocationId = reader.GetInt32(2),
                SlotInfo = reader.GetString(3),
                DeviceName = reader.GetString(4)
            });
        }
        return list;
    }

    public List<SystemInfo> GetSystemInfos(long[] fileIds)
    {
        using var cmd = _connection.CreateCommand();
        var where = BuildFileIdWhere(fileIds);
        cmd.CommandText = "SELECT key, value FROM system_info WHERE " + where;
        AddFileIdParams(cmd, fileIds);
        var list = new List<SystemInfo>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(new SystemInfo { Key = reader.GetString(0), Value = reader.GetString(1) });
        return list;
    }

    public List<PassFailEntry> GetPassFailSummary(long[] fileIds, string? moduleType)
    {
        using var cmd = _connection.CreateCommand();
        var where = BuildFileIdWhere(fileIds);
        if (moduleType != null) where += " AND module_type = @mod";
        cmd.CommandText = "SELECT channel_id, test_item_name, SUM(CASE WHEN is_failed = 1 THEN 1 ELSE 0 END) as fail_count, COUNT(*) as total_count FROM test_results WHERE " + where + " GROUP BY channel_id, test_item_name ORDER BY channel_id, test_item_name";
        if (moduleType != null) cmd.Parameters.AddWithValue("@mod", moduleType);
        AddFileIdParams(cmd, fileIds);

        var list = new List<PassFailEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new PassFailEntry
            {
                ChannelId = reader.GetInt32(0),
                TestItemName = reader.GetString(1),
                FailCount = reader.GetInt32(2),
                TotalCount = reader.GetInt32(3)
            });
        }
        return list;
    }

    private string BuildFileIdWhere(long[] fileIds)
    {
        if (fileIds.Length == 1) return "file_id = @fid0";
        var ids = string.Join(",", fileIds.Select((_, i) => "@fid" + i));
        return "file_id IN (" + ids + ")";
    }

    private void AddFileIdParams(SqliteCommand cmd, long[] fileIds)
    {
        if (fileIds.Length == 1) { cmd.Parameters.AddWithValue("@fid0", fileIds[0]); return; }
        for (int i = 0; i < fileIds.Length; i++)
            cmd.Parameters.AddWithValue("@fid" + i, fileIds[i]);
    }

    private static LogFileInfo MapLogFile(SqliteDataReader reader)
    {
        return new LogFileInfo
        {
            FileId = reader.GetInt64(0),
            FilePath = reader.GetString(1),
            FileName = reader.GetString(2),
            Hash = reader.GetString(3),
            FileSize = reader.GetInt64(4),
            LoopCount = reader.GetInt32(6),
            ToolVersion = reader.GetString(7)
        };
    }

    private static TestResult MapTestResult(SqliteDataReader reader)
    {
        return new TestResult
        {
            FileId = reader.GetInt64(0),
            LoopIndex = reader.GetInt32(1),
            ModuleType = Enum.Parse<ModuleType>(reader.GetString(2)),
            SlotNumber = reader.GetInt32(3),
            ChannelId = reader.GetInt32(4),
            TestItemName = reader.GetString(5),
            ExpectValue = reader.GetDouble(6),
            MeasureValue = reader.GetDouble(7),
            LowLimit = reader.GetDouble(8),
            UpLimit = reader.GetDouble(9),
            Difference = reader.GetDouble(10),
            IsFailed = reader.GetInt32(11) == 1,
            IsReTest = reader.GetInt32(12) == 1,
            LineNumber = reader.GetInt32(13)
        };
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}

public class PassFailEntry
{
    public int ChannelId { get; set; }
    public string TestItemName { get; set; } = string.Empty;
    public int FailCount { get; set; }
    public int TotalCount { get; set; }
    public bool HasFailure => FailCount > 0;
}
