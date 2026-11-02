# SST Log Analyser v1.2.10 更新说明

更新日期：2026-11-02。

## 期望值与实测值联动图表

- Chart 新增 Expected vs. Measured 模式，默认仍为 Error。上图显示期望虚线、实测实线及独立 ADC/HDM 点线，下图保留 LOG Difference 和限值，避免细微误差被绝对量级掩盖。
- 上下图横轴同步缩放、拖动，纵轴可分别缩放；操作上图不会改变下图的误差量级，Reset 同时恢复两图。悬停竖线联动，测量点可跳转 Data 原始行，PNG 导出包含上下图。文件、Batch、Channel、Loop 和重复 Run 筛选继续生效。
- CM 的横轴是施加电压，纵轴是测量电流，没有同量纲的期望电流，因此保持 Error 模式。AC 总览和 TMU 专用面板继续使用各自视图。

## 日志解析与缓存

- HDM 验证表正确区分 Target、DMM 实测和 HDM 读数，原始 Difference 为 DMM−HDM；原测和 Re-test 分成独立 Run，校准系数保留通道和量程。
- MVPI TMUVI QA/QB 电平验证按方向隔离 Run，保留原始 Difference=期望−实测，以及 CVA/CVB 系数。
- 补齐 PE HV_VALUE 扫描，避免与后续 VIHH/VILH 项目混合；HVPI 同时识别 Fail/Failed，电流校准五列表头/六列数据恢复遗漏 ADC 列。
- TMU 的 PASS/Finish 后异步测量行保持原表上下文。HDM、TMUVI、PE、CM 和普通 DC 校准过程数据进入 Source Events，状态 Unknown，不计入验证通过率。
- 解析缓存版本提升到 20；重新导入旧文件会自动重解析，无需删除用户数据库。

## 验证与发布范围

- 固定回归通过 17,996 项断言，覆盖解析、SQLite 缓存、筛选、重测、图表数值、双图坐标范围同步和基准对比。
- 69 份完整参考 LOG 的已清点测量表复查未发现验证表漏行；包含约 1.93 GB 原始日志。此结论限于已清点表格，不代表所有未知格式或硬件正确性。
- HDM、TMUVI、PE HV 的生产 WPF 图表布局、X 坐标对齐、横轴联动、独立纵轴缩放与 Reset、Error 布局恢复和生产 PNG 导出通过 30 项检查，并检查导出图像。
- 发布自带 .NET 9 Windows Desktop 运行时的 Windows x64 MSI、SHA256 及分享 ZIP；ZIP 仅附两个合成对比样例，不包含真实硬件 LOG 或用户缓存。
- 本机升级与安装载荷另行核对；其他电脑的安装环境未验证。
