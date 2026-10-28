# 可重复验证用模拟 LOG

补充：`24`～`27` 是用户提供的 MXVI LOG 的回归摘录，保留测量值并统一时间戳；并非下述模拟生成样本。`27_MXVI_Run_Batches.log` 包含两次运行（先 1 轮，再截取后一次运行的前 2 轮），共 1044 点。回归检查原始 Loop 编号、批次切换、缓存重载和基准对比隔离。可用 `dotnet run --project validation/Regression.csproj -c Release -- --batch-log <原始完整LOG路径>` 验证同一场景的完整文件。

这些文件是 **SIMULATED 合成样本**，依据 2026-09-30 本机 `D:\1_SVN\SST\Trunk\S200VerCalDia` 工作副本的日志输出代码生成。测量值、板卡序列号、时间和误差均为人为构造，不是实机测试记录，也不表示这些模块在同一台机器上同时执行过。

## 直接在工具中使用

从本目录 `fixtures` 选择文件导入 SSTLogAnalyser。建议分别加载每一份，便于和下表核对；Summary 文件导入后在 Run Summary 页查看，事件文件在 Source Events 页查看。

| 文件 | 场景及固定预期 |
|---|---|
| `01_DC_LogAll.log` | DPS、DPSI、PMU、MVPI、MDM、HVPI、HDM、MXVI、PE、PPMU；22 个测试点，10 个超限；2 个 Loop，Loop 2 有 1 个 Re-test；MVPI Ch201 的 ADC 原值为 1.0003 |
| `02_AC_LogAll.log` | 两个 Pin 各有 6 个边沿和 1 个整 Pin 判定，共 14 点、2 点 Fail；Pin1 只有一个边沿超限；Pin2 全 NA，不能生成数值点或 Pass |
| `03_TMU_LogAll.log` | 16 个路径点和 1 个整 Pin Fail，共 17 点、2 点 Fail；Pin0 结果空白，不能补出显式 Pass；参考时间 10ns |
| `04_Events_LogAll.log` | 8 条 Source Events：Pass 1、Fail 4、Unknown 3；包含 DPSI 诊断、PPMU Leakage、PairZero、温度和 Flash mismatch |
| `05_CAL_Only_LogAll.log` | 0 个测试点、2 条校准系数；DPS 和 PPMU 在模块筛选中仍应可选；适合检查纯 CAL 文件 |
| `06_MIXI_Attribution_LogAll.log` | 80 点，60 点超限；5 个 Channel、2 个 Loop，POS/NEG 每组 8 点；误差归因预期见下表 |
| `07_MIXI_Insufficient_LogAll.log` | 10 点；Ch10 只有两点，Ch11 的 WAVE/OFFSET 共线，Ch12 的 Target 不符合组成；均不能生成可信拟合。Ch12 是刻意注入的数据一致性异常 |
| `08_SST_Summary_Pass.txt` | 整体 Passed；4 个阶段条目，含 Not Test 和 Not Applied；板卡 S/N 为 SIM-DIGI-002 |
| `09_SST_Summary_Fail.txt` | 整体 Failed；DPS DC Verification failed，并关联 Slot-2 / DIGI |
| `10_SST_Summary_Abort.txt` | 整体 Aborted；验证阶段 Abort；不附加失败 Slot |
| `11_AUTOTEST_Result.txt` | 2 个条目，一 Pass、一 Fail；没有整体结果横幅，Overall 应为 Unknown |
| `12_AC_Process_LogAll.log` | 18 个 AC 汇总指标、2 个 Slot ALIGN 判定、1 个 Cal value；另含 NA 和压缩搜索轨迹 |
| `13_TMU_Cal_LogAll.log` | 18 个 TMU 路径校准值及 8 个时钟/EC/校准事件；重复列名不能覆盖 |
| `14_Register_LogAll.log` | 寄存器 Pass/Fail、十六进制 DEBUG 读写、Flash Write/readback 失败 |
| `15_Robustness_LogAll.log` | 非法数字/NaN/Infinity 不生成零值测试点；未知日志、中文、续行及空白均保留 |
| `16_Diagnosis_Variants_LogAll.log` | Pin List、Channel List、电阻电压预检、电阻、PE Leakage、PPMU，共 6 事件 |
| `17_Trunk_OutputTemplates.log` | 源码模板原文保留语料；含占位符与不完整片段，不是完整运行日志 |
| `18_Sigma_Report.log` | 2 个 Sigma 指标及 3 个无表头标量值；缺失单位和判定保持未知 |
| `19_MIXI_FFT_LogAll.log` | 实际多行 FFT 块：整体 Fail、6 个指标、NaN 和 V/dB 限值 |
| `20_MXVI_Short_Resistor_LogAll.log` | MXVI FI/FV 两种 Check、实际 F8 补空格范围及 Resule 电阻判定 |
| `21_DPS_Sweep_LogAll.log` | 参照 2026-02-04 实际日志的 -3..8 V DPS 扫描；2 通道、2 Loop，共 48 点、2 点超限；上下限为误差边界 ±(0.001 + |Expect| × 0.0005) |
| `22_Comparison_Baseline.log` | 对比基准，2 通道、单 Loop、24 点，全部在限值内 |
| `23_Comparison_Current.log` | 对比当前文件，24 点，其中 2 点超限；与 22 配对后应有 2 个新增失败点 |

曲线验证请优先使用 `21_DPS_Sweep_LogAll.log`，选择 DPS / Force Voltage / Channel 0 / Loop 1。`01` 是最小解析样本，其中 DC 双点现已改为 Expect=0 和 1，避免同一横坐标导致限值线退化；PE/PPMU 单点及纯 CAL、事件样本仍不适合验证连续限值曲线。2026-09-30 本次回归为 925 项断言通过，含 15 项实际导入及曲线数据检查；未执行窗口像素验证。

### MIXI 界面核对

加载 `06_MIXI_Attribution_LogAll.log`，选择 MIXI、`AWG AWG_VER_HR_1K5_WAVE_DAC_OFFSET_DAC_POS` 或 NEG。两种选择应自动合并同系列 POS/NEG 点。模型为：

`残差 = WAVE斜率 × signedWAVE + OFFSET斜率 × OFFSET + 公共偏置`

NEG 使用 `signedWAVE = -WAVE`。下面是**生成时注入的真实参数**，与工具拟合输出独立定义。

| Channel | Loop 1 | Loop 2 | 应显示的主要来源 |
|---|---|---|---|
| Ch0 | 全部误差为 0 | 全部误差为 0 | No dominant CAL bias |
| Ch1 | WAVE 斜率 2000ppm | WAVE 斜率 4000ppm | WAVE CAL，High |
| Ch2 | OFFSET 斜率 8000ppm | OFFSET 斜率 16000ppm | OFFSET CAL，High |
| Ch3 | 公共偏置 +1.5mV | 公共偏置 +3mV | Common bias，High |
| Ch4 | 非线性残差 ±2mV | 非线性残差 ±2mV | Mixed / low confidence，Low；RMSE 2mV |

单 Channel 默认进入 Detail；选多个 Channel 或 ALL 默认进入 Channel Map。未限制 Loop 时，Detail 选严重程度更大的组，Ch1/2/3 应显示 Loop 2；Channel Map 也按每个 Channel 的最严重组显示。尝试切换 POS/NEG、Loop 1/2、展开图表并导出 PNG，确认图表和筛选一致。

## 自动回归入口

`31_HVPI_MICM_Edges.log` 是合成边界样例，检查 MICM 末列紧跟 Failed、无前导逗号、量程切换、NaN 拒绝、校准数据排除及后续 Force 项不会继承 MICM 状态。

`30_HVPI_Real.log` 来自真实 `AdaptstarSystemServiceTool_20261028-160956-LogAll.txt` 的原始行 454–845（Ch800、Loop 1），仅统一时间戳。共 256 个验证点，含两个电压范围 × 四个 IRange × 每组 9 点的 72 个 MICM 点，以及 13 条显式 Failed。`HvpiLogCheck.cs` 独立按源标题、列名和行号逐点核对解析结果，并检查校准点不会混入、真实 Re-test 不会丢失。完整文件可用 `dotnet run --project validation/Regression.csproj -c Release -- --hvpi-log <完整LOG路径>` 验证。

`29_HVPI_ADC_Columns.log` 是按 `DcCal.cs` 中 HVPI Force 的七列表头（含 ADCMeasure、ADCDifference）及 Measure 的六列表头构造的合成样例，验证 Force 不会因包含 ADC 列被误改为 Measure；数值为人为构造，不是真实 HVPI LOG。

`28_MVPI_FIMI_Real.log` 截取自真实 `AdaptstarSystemServiceTool_20260921-172654-LogAll.txt` 的原始行 237–808（Ch800），仅统一时间戳。保留 CM、夹位和三个电流量程的实测值，检查 FIMI 15V/42V 分段不会将后续 Force 点归入前一 Measure 项。完整文件可用 `dotnet run --project validation/Regression.csproj -c Release -- --mvpi-log <完整LOG路径>` 验证 Ch800–831。此样例不代表 HVPI 已经实测验证。

在项目目录运行：

```powershell
powershell -NoProfile -File .\validation\Run-Regression.ps1
```

正常结束输出 `PASS: ... assertions`；任何预期不符都会抛错并返回失败。需要 Windows、.NET 9 SDK 和应用现有依赖，不增加测试框架。回归项目引用并构建实际应用，验证解析器、缓存和导入 ViewModel；脚本只隐藏已知 NU1701 兼容警告。

验证范围：解析点数和失败数、NA/Unknown/显式判定、ADC、源行号、时间戳、Loop/Re-test、Summary 总体及阶段状态、失败 Slot、已知 MIXI 拟合参数、缓存读写、模块/通道筛选、缓存后的归因、旧解析版本失效。缓存存入独立临时目录并在结束时清理，不使用工具的用户缓存。

需要重新生成时运行（必须能读到上述 Trunk 路径）：

```powershell
powershell -NoProfile -File .\validation\Run-Regression.ps1 -Generate
```

日常回归**不要加 `-Generate`**，保留固定样本验证新程序。生成器会检查 Trunk 中的格式标记；若标记变化，需要先审查格式，再更新样本。Summary 横幅直接从 Trunk 的 `showResultPicture` 输出字符串提取；`SOURCE_HASHES.json` 记录本次生成参考源码的 SHA-256。标记检查只检查部分格式，不是完整的源码兼容性审计。

新增功能时，在生成器里添加一个有明确预期的场景，并在 `Program.cs` 中加入独立预期断言，然后重新生成、检查差异、运行回归。不要直接把新解析器输出当作新基线，否则容易把错误也保存下来。

## 源码依据

路径均相对 Trunk 的 `S200VerCalDia`，行号是本次生成时的参考位置。

| 格式 | 源码位置 |
|---|---|
| DC 项目名称 / Channel / Re-test | `MainWindow.xaml.cs:4442`、`4473`、`4540`、`4591`、`4644`、`4692`、`4726`、`4772`、`4790`、`11146` |
| DC CSV / ADC / 系数 / Pin 表格 | `Model/DcCal.cs:729`、`1075`、`20140`、`493`、`14446`、`16624` |
| AC 边沿表及 NA | `Model/ACCalClass.cs:3549-3608` 的 SummaryReport |
| TMU 路径表及失败后缀 | `Model/TmuCalClass.cs:737-807` |
| DPSI 诊断 | `Model/DcDiag.cs:1073-1082` |
| PPMU Leakage / PairZero | `Model/DcCal.cs:34585-34610`、`34711` |
| 温度 | `Model/ThermalDetect.cs:210-226` |
| Flash mismatch | `Model/FlashAccess.cs:4543` |
| MIXI 数据格式 / POS、NEG 调用 | `Model/Mixi/MixiUtility.cs:173-197`、`Model/Mixi/MixiVer.cs:278`、`439`、`513` |
| Summary / AutoTest / ASCII 结果横幅 | `MainWindow.xaml.cs:26325`、`26492-26540`、`26875-26943`、`27719`、`28761`、`28804`、`28847` |

数值、通道和组合是用于软件验证的测试设计；不是完整硬件配置或所有 Trunk 输出格式的覆盖。自动回归不启动 WPF 界面，图表视觉、点击操作及 PNG 导出仍需按上面的界面步骤检查。将来有实机 LOG 时应补充真实样本验证。

按用户要求已移除 All Log Lines、通用字段提取及原文缓存。当前回归为 998 项断言，包含 20 项实际导入/图表/基准对比页面数据链检查及 22 项匹配规则检查。上文 925 项为历史结果。

`ComparisonCheck.cs` 验证新增/恢复/持续失败、80% 临界点、缺失/新增点、重复和浮点歧义、限值放宽、负误差改善、无效限值、MIXI 同 Target 不同组成、Slot/Component/测试项隔离，以及 10 万点匹配。`UiImportCheck.cs` 验证独立文件/Loop、默认最后 Loop、筛选与搜索、双误差/四限值曲线和更换选择后旧结果清空。窗口像素和 PNG 导出不在这些自动检查范围内。

刷新源码候选输出点清单及模板语料：

```powershell
powershell -NoProfile -File .\validation\Run-Regression.ps1 -Audit
```

然后不带参数运行固定回归。源码清单、工作副本哈希和范围说明在 `audit` 目录。源码模板语料现仅验证容错，不验证原文缓存；详见项目根目录 `SOURCE_LOG_COVERAGE.md`。
