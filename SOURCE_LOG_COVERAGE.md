# SST 源码日志解析覆盖表

本表以 `D:\1_SVN\SST\Trunk\S200VerCalDia` 中的日志写入代码为依据。`ERROR` 是日志级别，不直接等于测试失败；只有 SST 明写的判定才标记为“显式结果”。

| 源码输出 | 当前工具处理 | 可得结论 / 边界 |
|---|---|---|
| `MainWindow.xaml.cs` 的 `SST_Summary_*.txt`、`AUTOTEST_Result.txt` | Run Summary 页读取总体状态、阶段、失败 Slot、板卡和元数据，并保留行号 | Summary 是整次运行的权威判定；AutoTest 无总体状态时显示 Unknown |
| `DcCal.cs` 的 DPS/DPSI/PMU/PE/PPMU/MVPI/MDM/HVI/HVPI/HDM/MXVI 验证表及 Gain/Offset | 测试点、ADC 原值、系数分布；无 Slot 的行标成未知 Slot | 可看通道残差、限值和系数离群；不能单靠 M/C 推断校准前误差 |
| `ACCalClass.cs` 的 `SummaryReport` | 六种边沿数值拆成测试点；整 Pin `Result` 单独存放 | 边沿超限可定位；整 Pin Fail 不会被错误套给所有边沿；NA 不会当 Pass |
| `TmuCalClass.cs` 的 Path Verification | 每条路径数值和明确的 `false;` 判定 | 可比较路径误差；无明确 Pass 字段时不推断 Overall Pass |
| `DcDiag.cs` 的 Diagnosis、MXVI Short、MIXI FFT | Source Events 页展示数值、限值、结果和原文行 | 可追查明确的诊断失败；未在单行给出的 Slot 保持空白 |
| `DcCal.cs` 的 PPMU Leakage / Pin-to-pin Zero | Source Events 页展示电流与通道 | v1 超限后缀可判 Fail；v2 和 PairZero 无判定则为 Unknown |
| `ThermalDetect.cs`、`FlashAccess.cs`、电阻校准写入点 | Source Events 页展示温度告警、Flash 不一致和电阻测值 | 温度无 Warning 时不擅自标 Pass；Flash mismatch 明确标 Fail |

## 尚未接入

- `ACCalClass.cs` 的 FinalVerSummary、ALIGN 检查、CalValue / ChangePoint 原始过程及搜索轨迹。部分过程日志受源码开关控制，缺失不代表未执行。
- `TmuCalClass.cs` 的 Path Cal Summary 和 FMEA/EC 数值。现有 Path Verification 不等于校准过程全覆盖。
- 跨阶段耗时轴。源码有 Start/End 锚点，但需先定义缺失 End、嵌套阶段、并行输出的配对规则。
- PPMU Leakage 的专用分布图和同通道跨文件趋势；当前可在 Source Events 逐行查看与排序。
- 校准改善量。提案中的 `x·(1−1/M)+C/M` 不能未经 `CalculateMC` 语义核实就用于硬件结论，尤其 DPS FV 的 M/C 量纲与普通线性增益不同。

导入方式：加载 LogAll 解析测试点及事件；另加载 `SST_Summary_*.txt` 或 `AUTOTEST_Result.txt` 解析运行总览。缓存版本更新后，重新加载旧文件会自动重解析。
