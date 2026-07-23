# SST Log Analyser v1.2 更新说明

发布日期：2026-07-23

## 更新概览

v1.2 重点增强了多 Channel、多 Loop 校准数据的交互分析能力，并新增 Diagnostics 诊断工作区。工程师现在可以从容差风险、校准系数、残差形状和 MIXI/AWG 正负路径对称性等角度快速定位异常 Channel、Target 和 Loop。

## Chart 交互与对比

- 支持鼠标滚轮缩放图表。
- 支持左键拖动平移、右键区域缩放，并提供 Reset 按钮恢复完整视图。
- 支持在 Legend 中点击某条曲线进行单线聚焦，其他曲线暂时隐藏；可使用 Show all 恢复全部曲线。
- Legend 内容超过显示区域时可滚动，并采用与应用风格一致的窄滚动条。
- 支持 Channel 多选和 Loop 多选，Channel、Loop 均提供 ALL 快捷入口。
- Multi-Channel 模式默认固定一个 Loop，避免不同 Loop 数据无意叠加。
- Multi-Loop 模式默认固定一个 Channel，避免不同 Channel 数据无意叠加。
- 修复切换 Channel 后 Test Item 被清空、需要重复选择的问题。
- 修复 Channel 和 Loop 同时为 ALL 时，同一 Channel 的不同 Loop 被错误合并为一条曲线的问题。
- 修复模式切换后残留选择造成曲线数量不符合预期的问题。

## Diagnostics 诊断工作区

Diagnostics 标签移动到 Chart 后方，包含以下四类诊断图：

### 容差利用率热力图

- 按 Channel 和 Test Item 展示最差容差利用率。
- 使用低风险、接近限制和失败颜色快速定位异常单元。
- 汇总 Near limit、Failed 和 Worst Channel/Test Item。
- 多文件或多 Loop 条件下保留每个单元的最差结果。
- Channel 横轴根据窗口宽度自动调整标签密度；滚轮放大后自动显示更多 Channel 标签。

### Gain / Offset 分布图

- 按 Channel 展示 Gain 和 Offset 系数分布。
- 显示 Gain、Offset 中位数参考线和汇总值。
- 支持从多个校准项中选择需要检查的系数。
- 兼容 `Gain/Offset` 和 `M/C` 两种 LOG 命名：`Gain = M`，`Offset = C`。
- Channel 横轴采用自适应标签密度，改善大量 Channel 时的可读性。

### Residual Signature 残差特征图

- 以 Expected/Target 为横轴、Residual/Difference 为纵轴展示误差形状。
- 按文件、Channel、Loop 和 MIXI Component 分组显示。
- 为每组数据增加线性趋势线和零残差参考线。
- 汇总 RMS、最大绝对残差、对应 Channel 和最差容差利用率。
- 缩小图例和坐标文字，改善多曲线场景下的可读性。

### MIXI / AWG POS-NEG 对称性图

- 展示 POS、NEG、AVG 和 DIFF 分量。
- 计算匹配 Target 下 POS/NEG 的平均和最大不对称量。
- 保留 MIXI Component 类型，便于区分正负路径和差分结果。

### 诊断图布局

- 四个诊断图均可单独放大并一键还原四宫格。
- Diagnostics 标签仍可通过 `[+]` 弹出为独立窗口；弹窗后也可继续放大单图。
- 所有诊断图支持滚轮缩放和拖动查看。

## LOG 解析与缓存

- 新增校准系数数据模型和 SQLite `calibration_coefficients` 缓存表。
- 支持标准 `Gain/Offset` 系数行。
- 支持 MIXI/AWG/DTZ 过程行，例如 `M/C M=... C=...`。
- 支持高精度汇总行，例如 `M:..., C:...`。
- 同一 Loop、Module、Channel、Calibration Item 和 Coefficient Name 自动去重，优先保留后续高精度汇总值。
- 新增 `component_type` 缓存字段，支持 MIXI POS、NEG、DIFF 和 AVG 分析。
- Channel 和 Loop 过滤下推到 SQLite 查询，减少大 LOG 下不必要的数据加载。
- Parser Cache Version 升级到 3。旧缓存会在对应 LOG 再次加载时自动失效并重新解析。

## 界面与品牌

- 新增 SST Log Analyser 应用程序图标，并应用于 EXE 和主窗口。
- Diagnostics 标签顺序调整为：`Chart -> Diagnostics -> Pass/Fail Matrix -> Data -> Statistics -> Errors / FATAL -> Device Info`。
- 标签弹窗定位改为根据实际所属 Tab 判断，后续调整标签顺序不会打开错误页面。

## 验证记录

- `dotnet build --no-restore`：构建成功，0 个错误。
- DPSI 示例：解析 5,696 条测试结果和 576 组 Gain/Offset 系数。
- MIXI 示例：解析 1,724 条测试结果和 104 组 M/C 系数。
- MIXI M/C 重复键检查：0 个重复键。
- M/C 映射抽查：M 正确写入 Gain，C 正确写入 Offset。
- Windows 应用窗口启动验证通过。

## 升级提示

- 更新后首次重新加载旧 LOG 时，程序需要刷新旧版本缓存，因此首次解析时间可能略长。
- 当前 OpenTK、OpenTK.GLWpfControl 和 SkiaSharp.Views.WPF 仍会产生既有的 NuGet 兼容性警告，不影响本次构建通过和现有功能运行。
