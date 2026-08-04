using System.IO;
using Microsoft.Data.Sqlite;
using SSTLogAnalyser.Models;

namespace SSTLogAnalyser.Services;

public class CacheService : IDisposable
{
    private const int CurrentParserVersion = 4;
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
                tool_version TEXT DEFAULT '',
                parser_version INTEGER DEFAULT 1
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
                wave_value REAL,
                offset_value REAL,
                diff_value REAL,
                component_type TEXT DEFAULT '',
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
            );
            CREATE TABLE IF NOT EXISTS calibration_coefficients (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_id INTEGER NOT NULL,
                loop_index INTEGER DEFAULT 0,
                module_type TEXT NOT NULL,
                channel_id INTEGER DEFAULT 0,
                calibration_item TEXT NOT NULL,
                coefficient_name TEXT NOT NULL,
                gain REAL DEFAULT 0,
                offset REAL DEFAULT 0,
                line_number INTEGER DEFAULT 0,
                FOREIGN KEY (file_id) REFERENCES log_files(file_id)
            );";
        cmd.ExecuteNonQuery();

        // Migration: add wave_value and offset_value columns if they don't exist
        using var alterCmd = _connection.CreateCommand();
        alterCmd.CommandText = "ALTER TABLE test_results ADD COLUMN wave_value REAL";
        try { alterCmd.ExecuteNonQuery(); } catch { /* column already exists */ }
        alterCmd.CommandText = "ALTER TABLE test_results ADD COLUMN offset_value REAL";
        try { alterCmd.ExecuteNonQuery(); } catch { /* column already exists */ }
        alterCmd.CommandText = "ALTER TABLE test_results ADD COLUMN diff_value REAL";
        try { alterCmd.ExecuteNonQuery(); } catch { /* column already exists */ }
        alterCmd.CommandText = "ALTER TABLE test_results ADD COLUMN component_type TEXT DEFAULT ''";
        try { alterCmd.ExecuteNonQuery(); } catch { /* column already exists */ }
        alterCmd.CommandText = "ALTER TABLE log_files ADD COLUMN parser_version INTEGER DEFAULT 1";
        try { alterCmd.ExecuteNonQuery(); } catch { /* column already exists */ }

        using var indexCmd = _connection.CreateCommand();
        indexCmd.CommandText = @"CREATE INDEX IF NOT EXISTS idx_cc_lookup
            ON calibration_coefficients(file_id, module_type, calibration_item, coefficient_name, channel_id, loop_index);";
        indexCmd.ExecuteNonQuery();
    }

    public long? FindFileByHash(string hash)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT file_id, parser_version FROM log_files WHERE file_hash = @hash";
        cmd.Parameters.AddWithValue("@hash", hash);
        long? fileId = null;
        var parserVersion = 1;
        using (var reader = cmd.ExecuteReader())
        {
            if (reader.Read())
            {
                fileId = reader.GetInt64(0);
                parserVersion = reader.GetInt32(1);
            }
        }

        if (fileId.HasValue && parserVersion != CurrentParserVersion)
        {
            DeleteCachedFile(fileId.Value);
            return null;
        }

        return fileId;
    }

    private void DeleteCachedFile(long fileId)
    {
        using var transaction = _connection.BeginTransaction();
        foreach (var table in new[] { "test_results", "device_info", "error_log", "system_info", "calibration_coefficients", "log_files" })
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = $"DELETE FROM {table} WHERE file_id = @id";
            cmd.Parameters.AddWithValue("@id", fileId);
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
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
        cmd.CommandText = @"INSERT INTO log_files (file_path, file_name, file_hash, file_size, parse_time, loop_count, tool_version, parser_version)
            VALUES (@path, @name, @hash, @size, @time, @loops, @ver, @parserVersion);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@path", info.FilePath);
        cmd.Parameters.AddWithValue("@name", info.FileName);
        cmd.Parameters.AddWithValue("@hash", hash);
        cmd.Parameters.AddWithValue("@size", info.FileSize);
        cmd.Parameters.AddWithValue("@time", info.ParseTime.ToString("o"));
        cmd.Parameters.AddWithValue("@loops", info.LoopCount);
        cmd.Parameters.AddWithValue("@ver", info.ToolVersion);
        cmd.Parameters.AddWithValue("@parserVersion", CurrentParserVersion);
        return (long)cmd.ExecuteScalar()!;
    }

    public void InsertTestResults(long fileId, IEnumerable<TestResult> results)
    {
        using var transaction = _connection.BeginTransaction();
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"INSERT INTO test_results (file_id, loop_index, module_type, slot_number, channel_id,
                test_item_name, expect_value, measure_value, low_limit, up_limit,
                difference_value, is_failed, is_retest, line_number, wave_value, offset_value, diff_value, component_type)
            VALUES (@fid, @loop, @mod, @slot, @ch, @test, @exp, @meas, @low, @up, @diff, @fail, @retest, @line, @wave, @offset, @diffval, @component)";

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
        var pWave = cmd.CreateParameter(); pWave.ParameterName = "@wave"; cmd.Parameters.Add(pWave);
        var pOffset = cmd.CreateParameter(); pOffset.ParameterName = "@offset"; cmd.Parameters.Add(pOffset);
        var pDiffVal = cmd.CreateParameter(); pDiffVal.ParameterName = "@diffval"; cmd.Parameters.Add(pDiffVal);
        var pComponent = cmd.CreateParameter(); pComponent.ParameterName = "@component"; cmd.Parameters.Add(pComponent);

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
            pWave.Value = (object?)r.WaveValue ?? DBNull.Value;
            pOffset.Value = (object?)r.OffsetValue ?? DBNull.Value;
            pDiffVal.Value = (object?)r.DiffValue ?? DBNull.Value;
            pComponent.Value = r.ComponentType;
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void InsertCalibrationCoefficients(long fileId, IEnumerable<CalibrationCoefficient> coefficients)
    {
        using var transaction = _connection.BeginTransaction();
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"INSERT INTO calibration_coefficients
            (file_id, loop_index, module_type, channel_id, calibration_item, coefficient_name, gain, offset, line_number)
            VALUES (@fid, @loop, @mod, @channel, @item, @name, @gain, @offset, @line)";

        var pFile = cmd.Parameters.Add("@fid", SqliteType.Integer);
        var pLoop = cmd.Parameters.Add("@loop", SqliteType.Integer);
        var pModule = cmd.Parameters.Add("@mod", SqliteType.Text);
        var pChannel = cmd.Parameters.Add("@channel", SqliteType.Integer);
        var pItem = cmd.Parameters.Add("@item", SqliteType.Text);
        var pName = cmd.Parameters.Add("@name", SqliteType.Text);
        var pGain = cmd.Parameters.Add("@gain", SqliteType.Real);
        var pOffset = cmd.Parameters.Add("@offset", SqliteType.Real);
        var pLine = cmd.Parameters.Add("@line", SqliteType.Integer);

        foreach (var coefficient in coefficients)
        {
            pFile.Value = fileId;
            pLoop.Value = coefficient.LoopIndex;
            pModule.Value = coefficient.ModuleType.ToString();
            pChannel.Value = coefficient.ChannelId;
            pItem.Value = coefficient.CalibrationItem;
            pName.Value = coefficient.CoefficientName;
            pGain.Value = coefficient.Gain;
            pOffset.Value = coefficient.Offset;
            pLine.Value = coefficient.LineNumber;
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

    public List<string> GetDistinctTestItems(long[] fileIds, string? moduleType = null, int[]? channelIds = null)
    {
        using var cmd = _connection.CreateCommand();
        var where = BuildFileIdWhere(fileIds);
        if (moduleType != null) where += " AND module_type = @mod";
        if (channelIds != null && channelIds.Length > 0)
        {
            var chParams = string.Join(",", channelIds.Select((_, i) => "@ch" + i));
            where += " AND channel_id IN (" + chParams + ")";
            for (int i = 0; i < channelIds.Length; i++)
                cmd.Parameters.AddWithValue("@ch" + i, channelIds[i]);
        }
        cmd.CommandText = "SELECT DISTINCT test_item_name FROM test_results WHERE " + where + " ORDER BY test_item_name";
        if (moduleType != null) cmd.Parameters.AddWithValue("@mod", moduleType);
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

    public List<string> GetDistinctCoefficientNames(long[] fileIds, string? moduleType)
    {
        using var cmd = _connection.CreateCommand();
        var where = BuildFileIdWhere(fileIds);
        if (moduleType != null) where += " AND module_type = @mod";
        cmd.CommandText = @"SELECT DISTINCT calibration_item, coefficient_name
            FROM calibration_coefficients WHERE " + where + " ORDER BY calibration_item, coefficient_name";
        if (moduleType != null) cmd.Parameters.AddWithValue("@mod", moduleType);
        AddFileIdParams(cmd, fileIds);

        var list = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(reader.GetString(0) + " | " + reader.GetString(1));
        return list;
    }

    public List<CalibrationCoefficient> QueryCalibrationCoefficients(long[] fileIds, string? moduleType,
        string? displayName, int[]? channels, int[]? loops)
    {
        using var cmd = _connection.CreateCommand();
        var where = BuildFileIdWhere(fileIds);
        if (moduleType != null) where += " AND module_type = @mod";
        if (displayName != null) where += " AND calibration_item || ' | ' || coefficient_name = @name";
        cmd.CommandText = @"SELECT file_id, loop_index, module_type, channel_id, calibration_item,
            coefficient_name, gain, offset, line_number FROM calibration_coefficients WHERE " + where +
            " ORDER BY file_id, loop_index, channel_id";
        if (moduleType != null) cmd.Parameters.AddWithValue("@mod", moduleType);
        if (displayName != null) cmd.Parameters.AddWithValue("@name", displayName);
        AddFileIdParams(cmd, fileIds);

        var list = new List<CalibrationCoefficient>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new CalibrationCoefficient
            {
                FileId = reader.GetInt64(0),
                LoopIndex = reader.GetInt32(1),
                ModuleType = Enum.Parse<ModuleType>(reader.GetString(2)),
                ChannelId = reader.GetInt32(3),
                CalibrationItem = reader.GetString(4),
                CoefficientName = reader.GetString(5),
                Gain = reader.GetDouble(6),
                Offset = reader.GetDouble(7),
                LineNumber = reader.GetInt32(8)
            });
        }

        if (channels != null && channels.Length > 0)
        {
            var channelSet = new HashSet<int>(channels);
            list = list.Where(c => channelSet.Contains(c.ChannelId)).ToList();
        }
        if (loops != null && loops.Length > 0)
        {
            var loopSet = new HashSet<int>(loops);
            list = list.Where(c => loopSet.Contains(c.LoopIndex)).ToList();
        }
        return list;
    }

    public List<TestResult> QueryTestResults(long[] fileIds, string? moduleType, string? testItem,
        int[]? channels, int[]? loops)
    {
        using var cmd = _connection.CreateCommand();
        var where = BuildFileIdWhere(fileIds);
        if (moduleType != null) where += " AND module_type = @mod";
        if (testItem != null) where += " AND test_item_name = @test";
        if (channels != null && channels.Length > 0)
        {
            var channelParams = string.Join(",", channels.Select((_, i) => "@queryChannel" + i));
            where += " AND channel_id IN (" + channelParams + ")";
            for (var i = 0; i < channels.Length; i++)
                cmd.Parameters.AddWithValue("@queryChannel" + i, channels[i]);
        }
        if (loops != null && loops.Length > 0)
        {
            var loopParams = string.Join(",", loops.Select((_, i) => "@queryLoop" + i));
            where += " AND loop_index IN (" + loopParams + ")";
            for (var i = 0; i < loops.Length; i++)
                cmd.Parameters.AddWithValue("@queryLoop" + i, loops[i]);
        }
        cmd.CommandText = "SELECT file_id, loop_index, module_type, slot_number, channel_id, test_item_name, expect_value, measure_value, low_limit, up_limit, difference_value, is_failed, is_retest, line_number, wave_value, offset_value, diff_value, component_type FROM test_results WHERE " + where + " ORDER BY loop_index, channel_id, expect_value";
        if (moduleType != null) cmd.Parameters.AddWithValue("@mod", moduleType);
        if (testItem != null) cmd.Parameters.AddWithValue("@test", testItem);
        AddFileIdParams(cmd, fileIds);

        var list = new List<TestResult>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(MapTestResult(reader));
        return list;
    }

    public List<ToleranceCell> QueryToleranceCells(
        long[] fileIds,
        string? moduleType,
        int[]? channels,
        int[]? loops)
    {
        using var cmd = _connection.CreateCommand();
        var where = BuildFileIdWhere(fileIds);
        if (moduleType != null) where += " AND module_type = @mod";
        if (channels != null && channels.Length > 0)
        {
            var channelParams = string.Join(",", channels.Select((_, index) => "@cellChannel" + index));
            where += " AND channel_id IN (" + channelParams + ")";
            for (var index = 0; index < channels.Length; index++)
                cmd.Parameters.AddWithValue("@cellChannel" + index, channels[index]);
        }
        if (loops != null && loops.Length > 0)
        {
            var loopParams = string.Join(",", loops.Select((_, index) => "@cellLoop" + index));
            where += " AND loop_index IN (" + loopParams + ")";
            for (var index = 0; index < loops.Length; index++)
                cmd.Parameters.AddWithValue("@cellLoop" + index, loops[index]);
        }

        cmd.CommandText = @"
            SELECT channel_id, test_item_name,
                   MAX(ABS(
                       ((CASE WHEN module_type = 'MIXI' THEN measure_value ELSE difference_value END)
                         - ((up_limit + low_limit) / 2.0))
                       / ((up_limit - low_limit) / 2.0))) AS utilization,
                   MAX(is_failed) AS is_failed
            FROM test_results
            WHERE " + where + @" AND ABS(up_limit - low_limit) >= 1e-30
            GROUP BY channel_id, test_item_name
            ORDER BY channel_id, test_item_name";
        if (moduleType != null) cmd.Parameters.AddWithValue("@mod", moduleType);
        AddFileIdParams(cmd, fileIds);

        var cells = new List<ToleranceCell>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(2)) continue;
            cells.Add(new ToleranceCell
            {
                ChannelId = reader.GetInt32(0),
                TestItemName = reader.GetString(1),
                Utilization = reader.GetDouble(2),
                IsFailed = reader.GetInt32(3) == 1
            });
        }
        return cells;
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
            LineNumber = reader.GetInt32(13),
            WaveValue = reader.IsDBNull(14) ? null : reader.GetDouble(14),
            OffsetValue = reader.IsDBNull(15) ? null : reader.GetDouble(15),
            DiffValue = reader.IsDBNull(16) ? null : reader.GetDouble(16),
            ComponentType = reader.IsDBNull(17) ? string.Empty : reader.GetString(17)
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
