# SST Log Analyser - Design Document

## Overview

SST Log Analyser is a WPF desktop application for parsing, visualizing, and comparing calibration data from semiconductor test log files. It supports drag-and-drop loading of multiple LOG files, caches parsed results in a local SQLite database, and provides interactive charting with filtering by module type, channel, test item, and loop index.

## Tech Stack

| Layer | Technology |
|-------|-----------|
| UI Framework | WPF (.NET 9) |
| MVVM | CommunityToolkit.Mvvm 8.4 |
| Charting | LiveCharts2 (SkiaSharp) |
| Database | SQLite via Microsoft.Data.Sqlite |
| Target | Windows x64, net9.0-windows |

## Architecture

```
┌─────────────────────────────────────────────┐
│                  MainWindow.xaml            │
│  ┌──────────┐  ┌──────────────────────────┐ │
│  │  Filter   │  │     TabControl          │ │
│  │  Panel    │  │  ┌───────────────────┐  │ │
│  │           │  │  │ Chart (LiveCharts)│  │ │
│  │ - Module  │  │  │ + Custom Legend   │  │ │
│  │ - Channel │  │  │ + Custom Tooltip  │  │ │
│  │ - TestItem│  │  ├───────────────────┤  │ │
│  │ - Loop    │  │  │ Data (raw table)  │  │ │
│  │ - Mode    │  │  │ Pass/Fail Matrix  │  │ │
│  │           │  │  │ Statistics        │  │ │
│  └──────────┘  │  │ Errors / FATAL    │  │ │
│                │  │ Device Info       │  │ │
│                │  └───────────────────┘  │ │
│                └──────────────────────────┘ │
└─────────────────────┬───────────────────────┘
                      │ Binding
┌─────────────────────▼───────────────────────┐
│           MainViewModel (MVVM)              │
│  - File loading & parsing orchestration     │
│  - Filter state & reactive chart updates    │
│  - Series/Axes data for LiveCharts2         │
│  - Tooltip hit-testing logic                │
└───────┬──────────────────┬──────────────────┘
        │                  │
┌───────▼───────┐  ┌───────▼────────┐
│  LogParser    │  │  CacheService  │
│  (regex-based │  │  (SQLite CRUD) │
│   extraction) │  │                │
└───────────────┘  └────────────────┘
```

## Data Flow

```
LOG File ──? SHA256 Hash ──? Cache Lookup
   │                              │
   │ (miss)                  (hit)
   ▼                              │
LogParser.ParseAsync()            │
   │                              │
   ├── FileInfo (loops, metadata) │
   ├── TestResults                │
   ├── Devices (group/location)   │
   ├── Errors (ERROR/FATAL lines) │
   └── SystemInfo (version, etc.) │
                                  ▼
                          CacheService (SQLite)
                                  │
                                  ▼
                          QueryTestResults()
                                  │
                                  ▼
                          UpdateChart() / RefreshViews()
```

## Log File Parsing

The `LogParser` processes LOG files line by line using regex and string matching. It extracts:

| Category | Extraction Method |
|----------|------------------|
| Tool Version | Regex `Version:\s*([\d.]+)` |
| Software Drivers | Regex `SoftwareName:\s*(\S+)\s+FileVersion:\s*(\S+)` — checked before Tool Version to avoid false match |
| DMM Temperature | Regex for temperature values |
| System Clock | Regex for MHz values |
| TMU Calibration | Regex extracting CalDate, CalValue, NextCalDate |
| Test Results | CSV-style block parsing with header detection (Expected, Measured, LowLimit, UpLimit, Difference) |
| Device Mapping | Group/Location/Slot pattern matching |
| Errors | Lines starting with `ERROR` or `FATAL` |
| Operator/Tester ID/Meter ID | String prefix matching |

Test result blocks are identified by detecting header lines and parsing subsequent data rows. Each row produces a `TestResult` with fields: Module, Channel, TestItem, LoopIndex, Expected, Measured, Difference, Limits, Pass/Fail status.

PPMU blocks use dedicated section headers such as `PPMU FV Verification` and `PPMU FI I1_2uA Verification`. Pin qualifiers `(FV)`, `(MV)`, `(FI)`, and `(MI)` are normalized into separate test items. PPMU calibration rows are stored as `CalibrationCoefficient` records under the `PPMU` module.

### MIXI Module Parsing

The MIXI module type contains sub-modules AWG and DTZ, each with distinct test items and channels. MIXI data lines follow the format:

```
--POS-- Target:2.85 Meas:2.88 LowLimit:2.35 HighLimit:3.35 Meas-Target:0.03
```

Key semantic difference from other modules:

| Field | MIXI Semantics | Other Modules |
|-------|---------------|---------------|
| Target | Expected value | Expected value |
| Meas | Absolute measured value | Measured value |
| LowLimit / HighLimit | Absolute bounds on **Meas** | Bounds on **Difference** |
| Meas-Target | Stored as `Difference` | Stored as `Difference` |
| Pass/Fail | `Meas` compared to limits | `Difference` compared to limits |

When loading from cache, `diff_value` is mapped to `TestResult.DiffValue` for MIXI tooltip display.

## Database Schema (SQLite)

All data is stored in `%LOCALAPPDATA%\SSTLogAnalyser\cache.db`.

### Tables

**log_files**
```
id, file_name, file_hash, tool_version, loop_count, module_types, parse_time
```

**test_results**
```
file_id, loop_index, module_type, slot_number, channel_id,
test_item_name, expect_value, measure_value, low_limit, up_limit,
difference_value, is_failed, is_retest, line_number,
wave_value, offset_value, diff_value
```

**devices** — `file_id, group_number, location_id, slot_info, device_name`

**errors** — `file_id, loop_index, timestamp, level, message, line_number`

**system_info** — `file_id, key, value`

File deduplication is handled by SHA256 hash. Re-loading a previously parsed file retrieves results from cache without re-parsing.

## Chart Design (LiveCharts2)

### Series Composition

For each data group (channel or loop), the chart generates up to 4 series:

1. **Data line** (`LineSeries<ObservablePoint>`) — Expected vs Difference, colored from a 12-color palette
2. **High Limit line** — dashed red, drawn only for the first group to avoid visual clutter
3. **Low Limit line** — dashed blue, drawn only for the first group
4. **Failed points** (`ScatterSeries<ObservablePoint>`) — red markers, only if failures exist

### MIXI Limit Line Conversion

For MIXI modules, the chart Y-axis displays `Difference` (Meas-Target), but the stored limits are absolute bounds on `Meas`. The chart converts them at render time:

```
Chart High Limit Y = UpLimit - ExpectValue
Chart Low Limit Y  = LowLimit - ExpectValue
```

This ensures limit lines and data points share the same coordinate system (difference space).

### Multi-File Handling

When multiple files are loaded, series are grouped by `(FileId, ChannelId)` or `(FileId, LoopIndex)`. Series names are prefixed with `[filename]` to distinguish data from different files sharing the same loop/channel numbers.

### Custom Legend

The built-in LiveCharts2 legend is disabled (`LegendPosition="Hidden"`). A custom overlay panel is rendered in the chart's top-right corner with 70% opacity (`#B0FFFFFF`). Users can toggle visibility via a "Legend" button.

### Custom Tooltip

The built-in tooltip is disabled (`TooltipPosition="Hidden"`). Mouse move events on the chart:

1. Convert screen coordinates to data coordinates using estimated plot area bounds
2. Find the nearest non-limit data point (within 25px threshold)
3. Display a floating border with series name, expected value, and difference
4. Failed points are annotated with `[FAIL]`

## Comparison Modes

| Mode | Grouping | Use Case |
|------|----------|----------|
| Multi-Channel | Same loop, compare across channels | Verify channel-to-channel consistency |
| Multi-Loop | Same channel, compare across loops | Track calibration drift over time |

Modes are mutually exclusive via radio buttons. Selecting one deselects the other.

## Filter Pipeline

```
LoadedFiles → AvailableModules (distinct module types)
SelectedModule → AvailableChannels, AvailableTestItems, AvailableLoops
SelectedChannels → AvailableTestItems (filtered by selected channels)
SelectedTestItem → AvailableLoops (further filtered)
SearchText → AvailableTestItems (substring filter)
SelectedChannels + SelectedLoops → QueryTestResults → UpdateChart
```

For MIXI (AWG/DTZ), selecting a channel filters the test item list to only show items belonging to that channel, since AWG and DTZ channels have distinct test item sets.

All filter changes trigger reactive updates via `CommunityToolkit.Mvvm` partial methods (`OnSelectedModuleChanged`, etc.).

## Data Tab & Chart-to-Table Navigation

The Data tab displays all raw `TestResult` rows currently plotted in the chart. Key features:

- **Live population**: `ChartDataRows` (ObservableCollection) is populated alongside chart series in `UpdateChart()`
- **Chart click → table select**: Clicking a data point on the chart finds the nearest `TooltipDataPoint`, reads its `RowIndex`, selects the corresponding row in the Data tab, switches to the Data tab, and scrolls into view
- **Failed row highlighting**: Rows with `IsFailed = true` are styled with a light red background via DataTrigger
- **Virtualized scrolling**: `VirtualizingPanel.IsVirtualizing="True"` for large datasets

The mapping is achieved by storing `RowIndex = ChartDataRows.IndexOf(p)` when building tooltip data points, creating a direct index from chart point to table row.

## Detachable Tab Windows

Each tab header includes a `[+]` button that detaches the tab content into a floating window:

- Button uses ASCII `[+]` text for universal font support
- Click handler reads the tab index from the button's `Tag` property
- Tab content is moved to a new `Window` (with same `DataContext` for binding continuity)
- Original tab shows a gray placeholder text
- Closing the floating window restores content to its original tab
- Re-clicking an already-detached tab focuses the existing window
- Managed via `Dictionary<int, Window>` keyed by tab index

## Key Models

| Model | Purpose |
|-------|---------|
| `LogFileInfo` | File metadata (name, hash, loop count, modules) |
| `TestResult` | Single calibration data point |
| `DeviceInfo` | Hardware group/location/slot mapping |
| `ErrorLogEntry` | Error/FATAL log lines |
| `SystemInfo` | Key-value system metadata (versions, temperatures, etc.) |
| `PassFailEntry` | Aggregated pass/fail summary per channel + test item |
| `StatEntry` | Computed statistics (mean, stddev, min, max, fail count) |
| `LegendItem` | Custom legend display entry |
| `TooltipDataPoint` | Cached data for tooltip hit-testing and chart-to-table row mapping |

## Project Structure

```
SSTLogAnalyser/
├── Models/
│   ├── DeviceInfo.cs
│   ├── ErrorLogEntry.cs
│   ├── LogFileInfo.cs
│   ├── ModuleType.cs
│   ├── SystemInfo.cs
│   └── TestResult.cs
├── Services/
│   ├── CacheService.cs      # SQLite CRUD & query builders
│   ├── FileHashService.cs   # SHA256 computation
│   └── LogParser.cs         # Regex-based log extraction
├── ViewModels/
│   └── MainViewModel.cs     # MVVM, chart logic, filter state
├── Converters/
│   └── Converters.cs        # BoolToVisibility, FailCountToColor
├── App.xaml / App.xaml.cs
├── MainWindow.xaml           # UI layout
├── MainWindow.xaml.cs        # Drag-drop, tooltip mouse events
├── SSTLogAnalyser.csproj
└── .gitignore
```
