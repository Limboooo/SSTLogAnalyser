# 真实 LOG 调试样例

从 `E:\10_工作项目\SST\LOGSample` 按模块/场景各选一份完整 LogAll，于 2026-10-30 加入项目。共 8 份，覆盖 AK、Astar、DK、Dstar；只重命名文件，内容、编码、时间戳和换行均未修改。原目录不变。

`manifest.json` 记录每份文件的原始相对路径、字节数和 SHA-256；复制后已逐一与源文件校验。`.gitattributes` 禁止 Git 转换样例换行，确保以后取出时仍可核对哈希。

## 样例索引

以下是当前解析器的实际导入结果（2026-10-30）。点数/事件数用于调试参考，不等同于硬件通过判定，也不表示已经逐条验证全部解析语义。

| 文件 | 内容 | 测试点 | 诊断/过程事件 |
|---|---|---:|---:|
| [DIGI_DC_AK_VER_20261021-130758_LogAll.txt](DIGI_DC_AK_VER_20261021-130758_LogAll.txt) | DIGI DC：DPS、PMU、PPMU | 157696 | 1925 |
| [AC_AK_VER_20261021-161411_LogAll.txt](AC_AK_VER_20261021-161411_LogAll.txt) | AC 时序验证 | 92160 | 24596 |
| [TMU_AK_CAL_VER_20261021-163320_LogAll.txt](TMU_AK_CAL_VER_20261021-163320_LogAll.txt) | TMU 校准及验证 | 49152 | 18824 |
| [DPSI_Astar_VER_20261023-085455_LogAll.txt](DPSI_Astar_VER_20261023-085455_LogAll.txt) | DPSI DC 验证 | 9280 | 5 |
| [MIXI_Astar_VER_20261027-164841_LogAll.txt](MIXI_Astar_VER_20261027-164841_LogAll.txt) | MIX2 / MIXI：AWG、DTZ | 3344 | 0 |
| [MVPI_Dstar_VER_20261023-092743_LogAll.txt](MVPI_Dstar_VER_20261023-092743_LogAll.txt) | MVPI DC 验证 | 35072 | 5 |
| [PE_HV_Dstar_VER_20261028-092157_LogAll.txt](PE_HV_Dstar_VER_20261028-092157_LogAll.txt) | PE HV 验证（含恢复的 HV_VALUE 扫描） | 8640 | 0 |
| [DIAG_DK_DIAG_20261029-135242_LogAll.txt](DIAG_DK_DIAG_20261029-135242_LogAll.txt) | DIGI DC / AC 诊断；在 Source Events 查看 | 0 | 3840 |

这是源目录现有模块的代表性选择，不是全部 `ModuleType` 的完整覆盖集；例如未在本批中加入独立 MXVI、HVPI、MDM、HDM 样例。PE HV 文件按源日志名称归类，不作为独立 HVPI 板卡日志。

## 使用

在 SSTLogAnalyser 中直接导入对应 `.txt`。这些文件用于开发和人工调试，不随应用发布；现有 `validation/fixtures` 继续用于快速固定回归，默认回归不会自动扫描本目录。

在项目根目录执行，逐份检查完整解析、SQLite 写入、模块查询和重复导入缓存查找：

```powershell
dotnet build validation/Regression.csproj -c Release --no-restore
Get-ChildItem validation/samples/*_LogAll.txt | ForEach-Object {
    dotnet validation/bin/Release/net9.0-windows/Regression.dll --profile $_.FullName
    if ($LASTEXITCODE -ne 0) { throw "Import failed: $($_.Name)" }
}
```

TMU 样例与现有 `--tmu-log` 的固定预期原文件一致，可执行更严格的原始行、路径值和缓存检查（本次通过 49179 项检查）：

```powershell
dotnet validation/bin/Release/net9.0-windows/Regression.dll --tmu-log validation/samples/TMU_AK_CAL_VER_20261021-163320_LogAll.txt
```

其他 `--ac-log` 等专用入口包含各自原文件的固定预期，不能直接套用本目录中的另一份 LOG。

仅校验项目内文件是否保持原样：

```powershell
$manifest = Get-Content validation/samples/manifest.json -Raw | ConvertFrom-Json
foreach ($sample in $manifest.samples) {
    $path = Join-Path 'validation/samples' $sample.file
    if ((Get-Item -LiteralPath $path).Length -ne $sample.bytes -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $sample.sha256) {
        throw "Sample changed: $($sample.file)"
    }
}
```
