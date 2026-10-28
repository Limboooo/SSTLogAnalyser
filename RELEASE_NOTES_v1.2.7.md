# SST Log Analyser v1.2.7 更新说明

更新日期：2026-10-28。

## 图表与运行批次

- 主图显示模块及完整 Item 标题，包含 VI/AWG 模式；副标题显示文件、批次、通道、原始 Loop 和点数，随筛选更新。主图 PNG 包含标题，残差图同步显示 Item。
- 独立运行按 Run Batch 筛选，首次导入默认选择最新批次，保留原始 Loop 编号。图表、统计、诊断和基准对比使用当前批次。
- 同一 Item 的重复扫描按 Run 分开绘图，避免曲线交错。真实 Re-test 记录保留，不任意取一条覆盖。

## 真实 LOG 解析修复

- MXVI：识别配对 ItemName 标题与 VerificationVI/VerificationAWG，区分 Force/Measure 和扫描序列。
- MVPI：识别 FI/MI/FIMI 的 15V/42V 分段，将后续 Force 点从上一 Measure 项分开；无重复标题的 ExpectV/MeasureI 表归回 CM。
- HVPI：支持 MICM 七列表头和无前导逗号的数据行。按电压范围与 IRange 生成独立 Item，使用 FV_Value、MI_ADC、源 CM_Difference 和源限值，不将 MI_CM 寄存器计数作为电流值。
- HVPI Force Voltage 表包含 ADCMeasure/ADCDifference 时仍保持 Force 项归属；识别末列紧跟 Failed、没有额外逗号的显式失败标记。
- 解析缓存版本升级为 14，重新导入旧缓存文件时自动重解析。

## 验证与安装

- 真实 HVPI LOG 的 256 个验证点逐行核对一致，包含 72 个 MICM 点和 13 条显式失败；8 个 MICM Item 每项 9 点，缓存重载及图表数据检查通过。
- 完整真实 MVPI LOG 解析 15,968 点，核对 Ch800–831 的三个电流量程及 15V/42V Force/Measure 归属。
- 含完整 HVPI LOG 的回归通过 7425 项断言；含完整 MVPI LOG 的回归通过 11290 项断言。这两次回归包含重叠检查，不相加计算覆盖量。
- Windows x64 MSI 自带 .NET 9 Windows Desktop 运行环境，目标电脑无需单独安装 .NET；创建桌面和开始菜单快捷方式，支持旧版本升级及卸载。
- 安装包与校验文件保存在 dist，未包含用户缓存或真实测试 LOG。跨电脑安装、窗口像素与 PNG 视觉效果仍需目标环境验证。
