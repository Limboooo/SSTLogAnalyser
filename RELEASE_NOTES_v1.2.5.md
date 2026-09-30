# SST Log Analyser v1.2.5 更新说明

更新日期：2026-09-30。

## 日志解析

- 补充 AC FinalVerSummary、Slot ALIGN、最终校准值，TMU 校准表、FMEA/EC、寄存器判定、Sigma 汇总和 MIXI FFT 整体结果解析。
- 修正诊断格式变体、Line Resistance 电压预检单位、MXVI Short F8 补空格范围，以及 FFT 数值与限值处理。
- 非法数字、NaN、Infinity 不再生成零值测试点；缺少明确结果时保持 Unknown。ERROR/FATAL 数据行同时进入错误列表，避免专用解析消耗后漏记。
- 支持更多日志级别及逗号/小数点毫秒时间戳；Summary / AutoTest 按内容识别，文件改名后仍能导入。

## 导入性能

- 移除 All Log Lines 页面、通用字段提取和逐行原文缓存；保留测试点、系数、错误及诊断/过程事件，AC/TMU/Sigma 使用专用表格解析。
- 缓存解析版本更新至 8，旧日志缓存重新解析；未完成的导入不会作为完整缓存复用。移除旧原文缓存表，不修改原始 LOG 文件。
- 同一份真实 45.07 MiB LogAll 测得完整解析由 29.020–35.766 秒降至 2.908 秒，结构化缓存写入 1.351 秒。修改前后均提取 186,408 个测试点和 299 个事件。
- 上述为本机测试；共享文件校验另耗时 4.274 秒，未包含窗口绘制耗时，不代表所有文件或机器的固定速度。

## 模拟日志与回归验证

- 项目内 `validation/fixtures` 收录 21 份模拟日志，覆盖 DC/AC/TMU、纯 CAL、Summary、诊断、寄存器、FFT、异常输入和 MIXI 误差归因。
- 修正最小 DC 样本的重复 Expect 点；新增参照真实日志格式的 DPS -3..8 V 连续扫描，用于验证 High / Low Limit 曲线。所有模拟测量值均为合成数据。
- 新增可重复回归、源码输出审计及分阶段导入性能测量工具。源码审计覆盖主项目 73 个编译文件、5,079 个候选调用；候选调用清单不等同于所有运行格式的语义证明。
- Release 构建成功；最终 860 项回归断言通过，含 10 项实际 ViewModel 导入链及曲线数据检查。未自动验证窗口像素、PNG 导出或硬件行为。

使用说明和命令见 [validation/README.md](validation/README.md)，详细覆盖范围见 [SOURCE_LOG_COVERAGE.md](SOURCE_LOG_COVERAGE.md)。外部 DLL 输出及特殊运行路径仍需真实日志补充验证。
