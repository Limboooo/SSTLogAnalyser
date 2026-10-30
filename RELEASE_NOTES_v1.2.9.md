# SST Log Analyser v1.2.9 更新说明

更新日期：2026-10-30。

## TMU 校准图表

- 在现有 Chart 页选择 Module Type = TMU，即可使用 Path verification error、Path calibration compensation 和 Timebase verification EC 三种视图。界面标签统一沿用现有英文风格，使用说明保持中文。
- 路径验证以 Pin 为横轴、实测时间减参考时间为纵轴；TMU0/1 上下分图，共用纵轴范围。支持参考时间切换、全部 Pin 或连续 32 Pin 范围，以及四种 VOL/VOH Start/Stop 组合的显示开关。
- 点击测量点或输入 Selected pin，可联动查看该 Pin 的原始值、误差、限值、源日志结果、日志行号及参考时间趋势。趋势支持单 Pin，或当前 Pin 范围的均值与 Min–Max 竖线。
- 路径校准补偿分别显示 VOL/VOH PinDiff，不套用路径验证限值。时基验证按参考频率显示 EC（ppm），明细保留实测频率（Hz）和日志 Pass/Fail。

## 数据与缓存

- 复用现有路径测试点及过程指标缓存，补充 TMU 段边界、内层 Loop Count、FEPT 参考频率和跨行 EC 限值上下文。
- 文件、Run Batch、外层 Loop 和内层 TMU 验证段保持隔离。同一段重复的 Pin/TMU/路径/参考值键排除并提示，缺失点不补零。
- 空 VerifyResult 保持 Unknown；按数值计算的超限数与源日志显式 Pass/Fail 分开。纯 TMU 校准/时基日志也能在 Module Type 中选择。
- 缓存解析版本升至 18。重新导入旧缓存时自动重解析，原始 LOG 不变。

## 验证与安装

- 全量回归通过 7,002 项检查，覆盖既有模块及新增 TMU 筛选、缓存重载和重复键/内层 Loop 边界。
- 用户提供的完整 TMU LOG：49,152 个路径值逐行核对 Pin、列、参考值和差值；误差范围为 −0.597～+0.655 ns，按日志 ±1.5 ns 限值计算无超限点；另核对 192 个时基 EC 点。
- 实际 WPF TMU 面板渲染和测量点点击联动通过，完整日志检查通过 49,181 项断言。Min–Max 使用竖线显示各参考时间的范围。
- 发布自带 .NET 9 Windows Desktop 运行时的 Windows x64 MSI、SHA256 校验文件及分享 ZIP；支持从旧版覆盖升级，保留用户 LOG 和解析缓存。
- 验证结论来自已有真实日志和软件检查，不代表新增硬件校准试验；其他电脑的安装环境未验证。
