# SST 源码日志解析覆盖表

核对日期：2026-09-30。源代码为本机 D:\1_SVN\SST\Trunk\S200VerCalDia 当前工作副本；未修改 Trunk。

## 覆盖的含义

按用户要求移除 All Log Lines 页面、通用字段提取和逐行原文缓存。只解析已知业务格式；不匹配的过程文本不提供字段展示或原文检索。已有 source_lines 缓存表在新版本初始化时删除，原始 LOG 文件不受影响。

结构化覆盖：下表中的已知格式另提取测试点、系数、诊断/过程指标、表格列或 Summary。ERROR 是级别；只有日志明确记录的结果才标为显式 Pass/Fail。数值超限推导与源代码判定分开。

源码调用清单和模板容错测试不能证明所有动态路径的业务语义均已验证。

| 输出格式 / 源码依据 | 当前处理 | 固定验证样本 |
|---|---|---|
| MainWindow 模块/Channel/Re-test；DcCal DC 验证 CSV | DPS、DPSI、PMU、MVPI、MDM、HVPI、HDM、MXVI、PE、PPMU 测试点；ADC、残差、限值、Loop、Re-test | 01：22 点，10 超限；ADC=1.0003 |
| DcCal Gain/Offset 与 MIXI M/C | 系数独立缓存；纯 CAL 模块可筛选 | 05：0 点、2 系数 |
| ACCalClass SummaryReport 边沿 CSV | 数值边沿与整 Pin 显式结果分开；NA 不作数值或 Pass | 02：14 点，2 Fail |
| ACCalClass FinalVerSummary 两级 pipe/comma 表 | 按边沿提取 Max/Min/Range，保留时钟和汇总表原始行号；Chart 内 AC 总览显示各 Pin 曲线，误差刻度 0.078 ns，可叠加同频率验证汇总，无判定保持 Unknown | 12：18 个汇总指标；32：双频率真实摘录 |
| AC 同目标逐 Pin 相对时序 | 保留 SysClk begin/end 和每个验证表的边界；同表、同边沿、同目标按测量时间减基准 Pin。未知频率、缺失/重复基准不作比较，重复 Pin 排除。支持 64/128 连续 Pin 分组、组均值相对基准组及组内跨度；不代替 HwConfig 物理映射 | 33：500/250 MHz 同目标真实摘录 |
| AC Across points | 同一文件/Loop/时钟/SysClk begin 验证段内，以 Point 顺序绘制 Group/Pin 误差热图和选中行趋势。支持目标误差及逐 Point 相对基准 Pin 偏移，缺失和重复点留空；相同目标的重复验证段不会合并 | 34：合成多验证段、Loop、缺失/重复数据；真实 VER 9 Point × 20 组 |
| ACCalClass Slot ALIGN、CalValueLog、SearchingReport | 保留 Slot 判定、数值最终校准值；不再提取采样序列、NA 和搜索轨迹字段 | 12：2 Slot 判定、1 Cal value |
| TmuCalClass Path Verification | 8 路径；false; 才是显式整体 Fail，空结果不补 Pass；专用 Chart 按 Pin/参考时间绘制误差，源文件/批次/外层 Loop/内层 Loop 分段；重复键排除 | 03：17 点、2 Fail；35：真实 TMU 摘录；--tmu-log 完整日志验证 |
| TmuCalClass Path Cal Summary / Tmu0、Tmu1 表 | 每列独立提取；重复列名按顺序保留，并标记组别 | 13：18 个校准表指标 |
| TmuCalClass FMEA/EC、Calibration Value、FAIL-FMU/TMU | 频率、EC、校准值、同一行或当前段 ppm 范围与结果；保留 FEPT 参考频率用于时基 Chart；不由颜色推断结果 | 13：8 个时钟/校准事件；35：6 个频率 |
| DcDiag Diagnosis、Pin/Channel List、Line Resistance、PE Leakage、PPMU | 数值/结果/明确限值；电压预检标 V、电阻标 ohm；比较状态另保留二进制文本 | 04、16 |
| DcDiag MXVI Short | FI 检查 MV(V)，FV 检查 MI(A)；Check 范围支持实际 F8 补空格格式 | 20：FI/FV 检查及边界；另含 Resule 电阻判定 |
| DcDiag MIXI FFT 多行表及单行结果 | Result/Slot/DTZ CH 总体结果；六指标、V 容差、SNR/THD 边界；NaN 保留空值及明确判定 | 19：1 整体判定、6 指标；频率百分比限值仍在事件原文中，不作为 Hz 限值 |
| DcCal PPMU Leakage v1/v2、PairZero | 电流、通道、v1 超限后缀及范围；无判定保持 Unknown | 04 |
| ThermalDetect、FlashAccess | 温度/Warning、Flash M/C Write/Read 字段、Write/readback 失败 | 04、14 |
| RegDiag/ViRegDiag/MIXI Register -- Pass/Fail | 保留通道判定及事件原文；不再通用提取地址、读写、Mask 字段；没有板卡类型时不强填 Module | 14 |
| MainWindow SST_Summary、AUTOTEST_Result | 按内容识别改名文件；总体/阶段/板卡/SN/失败 Slot/元数据 | 08–11，实际导入链已验证 |
| MainWindow Sigma Summary / 数字序列 CSV | 专用表头提取 Module/Channel/Sigma 事件；不再通用提取无表头标量 | 18 |
| 阶段 Start/End、配置、调试、其他过程信息 | 不再提供通用字段提取；ERROR/FATAL 仍通过既有错误列表处理 | 15、17 验证不崩溃及非法数值不变成测试点 |

## 源码清单与回归

扫描主项目 csproj 实际 Compile 列表中的 73 个源文件，用 C# 语法树抽取 5,079 个候选日志 API、文本写入及文本构造调用，涉及 28 个文件；排除 BackUP 和注释。详见 [源码清单](validation/audit/TRUNK_LOG_AUDIT.md)。完整表达式、行号、源码哈希在同目录 JSON。

清单涵盖 LogAll、FatalAndError、Warning、Summary、Sigma、AC/TMU、寄存器/Flash、MIXI、系统及生产配置；也包含需要辨别的 UI-only 信息和配置文本构造。XML/二进制是配置或数据产物，不等同于 LOG，不会伪装成测试点。

17_Trunk_OutputTemplates.log 是候选调用原始字符串模板拼成的鲁棒性语料，含占位符和不完整片段。它验证不会导致解析崩溃，不代表实际运行会连续输出这些片段，也不验证插值值的硬件含义。

当前 Release/Debug 的 AdaptstarSystemServiceToolLog4cpp.config.xml 使用 %date  %-5level - %message%newline；支持逗号/小数点毫秒、INFO/WARN/ERROR/FATAL/DEBUG/TRACE 及多行续行。

运行 powershell -NoProfile -File .\validation\Run-Regression.ps1，验证结构化预期、缓存/旧版本失效及实际 MainViewModel 导入/筛选/MIXI/限值图表数据。当前缓存解析版本为 18，重新加载旧文件会重解析。

删除通用字段服务后，真实 45.07 MiB LogAll 测得解析 2.908 秒、结构化缓存写入 1.351 秒；提取 186,408 个测试点和 299 个事件，数量与修改前一致。共享文件 SHA256 另耗时 4.274 秒；不含实际窗口绘制耗时。

## 尚未证明的范围

尚未证明所有业务格式 100% 正确：外部 DLL 输出、运行时拼接的所有参数组合、条件编译分支、特殊机型路径仍需实机样本。HVI 等仅有规则而未纳入固定场景的格式也需补独立验证。跨阶段耗时配对和校准改善量不从日志缺失或未经核实的 M/C 公式推导。图表像素、PNG 导出和硬件行为未在自动导入测试中验证。
