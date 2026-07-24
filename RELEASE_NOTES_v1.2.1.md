# SST Log Analyser v1.2.1 更新说明

发布日期：2026-07-24

## 安装器

- 新增 Destination Folder 页面，可在安装时选择目标目录。
- 默认目录仍为 `%LOCALAPPDATA%\Programs\SST Log Analyser`，使用默认位置不需要管理员权限。
- 自定义目录需要保证当前账号具有写权限；不建议选择 `Program Files` 等受保护位置。
- 安装包继续内置 .NET 9 Windows Desktop Runtime，目标电脑无需单独安装运行时。

## Diagnostics

- 将 `MIXI / AWG POS-NEG Symmetry` 改为真正的派生不对称量图。
- 每个 Channel/Loop 绘制 `Mismatch = POS residual - NEG residual`，不再重复 Residual Signature 的原始 POS/NEG/DIFF 曲线。
- 增加零基准线；曲线越接近零，POS/NEG 路径越对称。
- 摘要显示 POS/NEG 配对数、平均绝对不对称量、最大绝对不对称量及对应的 Channel/Target。

## 大数据稳定性

- 恢复单一 `Performance Mode` 开关，不再绘制 Overview 的 P5/Median/P95 分位线。
- 默认显示原始曲线、点标记和失败点；Performance Mode 保留原始曲线并关闭点标记与失败点叠加。
- Channel 选择 `ALL` 时绘制全部原始 Channel/Loop 曲线，不再截取风险最高的 128/256 组，High/Low Limit 始终保留。
- 超过 128 组或 50,000 个点时自动关闭点标记、失败点叠加和动画，并批量更新虚拟化图例；Data 与 Statistics 仍保留完整数据。
- Channel 列表增加上一页/下一页按钮，每页快速选择 128 个 Channel，适配 2048 Channel 的常见机台配置。
- 图例使用虚拟列表，Data/Statistics 改为批量绑定，Tooltip 改为索引查找并限制刷新频率。
- Diagnostics 仅在可见时刷新；Residual 和 POS-NEG 图在大数据时显示风险最高的原始曲线，容差热力图改由 SQLite 直接聚合最坏结果。

## PPMU 解析

- 新增独立 `PPMU` 模块，识别 `PPMU ... Verification` 与带 `Pin (FV/MV/FI/MI)` 的数据表。
- FV/MV、I1_2uA 至 I5_40mA 的 FI/MI、VCH、VCL 分别显示为独立 Test Item。
- 解析 `PE Pin: ... PPMU ... Calibration ... Gain/Offset` 校准系数，供 Gain/Offset 分布图使用。
- 解析缓存版本升级；旧版本缓存的同一 LOG 会自动失效并重新解析。

## 验证

- `dotnet build --no-restore` 构建通过，0 个错误。
- 已验证默认目录和自定义目录的安装、启动与卸载流程。
- 通过 2048 Channel × 10 Loop 合成压力测试，验证 ALL 全量曲线与自动快速渲染。
- 压力测试覆盖主图、Diagnostics、Tooltip、Data 和 Statistics 的完整数据路径。
- 使用真实 LOG 验证 74,368 个 PPMU 数据点、896 个 Channel、14 类 Test Item 和 12,544 组校准系数。
