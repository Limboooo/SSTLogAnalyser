# SST Log Analyser v1.2.11 更新说明

更新日期：2026-11-02。

## Device Info 板卡配置与汇总

- 新增 Slot Board Configuration，按来源 LOG、SLOT 展示板卡类型、Device、Revision、SN、CalDate 和原始行号。
- 支持普通 Slot 及 DPSI、MVPI、HVPI、MXVI、MIXI 记录；普通数字板类型显示为 DIGI，MIXI 的 `14(13)` 等编号保留原文。
- 新增两行 Summary：第一行 Slot number，第二行 Board type，每个 SLOT 一列。同一 SLOT 的重复类型合并，多文件各自汇总。
- 重复初始化配置合并，版本、序列号或校准日期变化时保留不同配置；缺失字段留空，未记录的 SLOT 不推断为空槽。
- 板卡信息支持缓存重载与 Clear All；缓存解析版本提升到 22，旧 LOG 重新导入时自动重解析。板卡信息覆盖整个文件，不随测量 Batch/Loop 筛选变化。

## 验证与发布范围

- 回归通过 18,071 项断言，覆盖板卡解析、MIXI 括号编号、重复与变化配置、SQLite 缓存、两行汇总、多文件隔离和界面数据绑定。
- 三份真实 LOG 的全部不同板卡记录核对通过，分别为 172、184、44 条。
- 发布包含 .NET 9 Windows Desktop 运行时的 Windows x64 MSI、SHA256 和分享 ZIP。分享 ZIP 仅包含安装包、校验文件、文档及两个合成对比样例，不包含真实硬件 LOG 或用户缓存。
- 不包含硬件验证；其他电脑的安装环境未验证。
