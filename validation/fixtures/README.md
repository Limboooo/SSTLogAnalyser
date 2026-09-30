# SST 模拟日志

本目录的 23 份 `.log` / `.txt` 文件是根据 Trunk SST 输出代码构造的合成样本，不是实机测量记录。将文件直接导入 SSTLogAnalyser，即可用于人工验证及未来功能回归。

- `01`–`07`：DC、AC、TMU、诊断事件、纯 CAL 和 MIXI 误差归因。
- `08`–`11`：Passed、Failed、Aborted Summary 和 AutoTest 结果。
- `12`–`16`：AC/TMU 过程、寄存器、异常输入及诊断格式。
- `17_Trunk_OutputTemplates.log`：源码输出模板语料，包含占位符和不完整片段，仅用于容错测试，不代表完整运行日志。
- `18`–`20`：Sigma、MIXI FFT、MXVI Short / Resistor。
- `21_DPS_Sweep_LogAll.log`：参照 2026-02-04 真实 LogAll 的 DPS 电压扫描格式，2 个通道、2 个 Loop，每组 -3..8 V 共 12 点，48 点中刻意设置 2 点超限。上下限为误差限值 ±(0.001 + |Expect| × 0.0005)，用于查看 High / Low Limit 曲线；测量值仍为合成数据。
- `22_Comparison_Baseline.log` / `23_Comparison_Current.log`：分别作为基准和当前文件，双方 Loop 1；各 24 点，预期匹配 24 点、2 个新增失败点。进入 Baseline Comparison 选择文件后点击 Compare。

建议先使用 `01_DC_LogAll.log` 和 `06_MIXI_Attribution_LogAll.log`，再分别导入其他样本。`SOURCE_HASHES.json` 记录生成样本时参考的源码哈希，不需要导入 APP。

模拟样本能够覆盖已知格式和边界输入，不能替代实机日志对 DLL 输出及特殊运行路径的验证。
