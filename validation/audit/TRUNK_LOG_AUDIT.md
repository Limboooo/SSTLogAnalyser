# Trunk 日志写入点清单

扫描当前 S200VerCalDia.csproj 实际 Compile 列表，使用 C# 语法树提取调用；不扫描 BackUP 或注释。

源文件 73 个，候选日志/文本写入/输出文本构造调用 5079 个，涉及 28 个文件。

| 源文件 | 候选调用数 |
|---|---:|
| MainWindow.xaml.cs | 1507 |
| Model/ACCal.cs | 241 |
| Model/ACCalClass.cs | 58 |
| Model/ACCalDebug.cs | 74 |
| Model/ACCalDStar.cs | 145 |
| Model/ACCalHStar.cs | 185 |
| Model/AcDiag/AcDiagClass.cs | 250 |
| Model/AcDiag/CapMemClass.cs | 3 |
| Model/ACRefCal.cs | 59 |
| Model/DcCal.cs | 1139 |
| Model/DcDiag.cs | 280 |
| Model/Flash/FlashContent.cs | 8 |
| Model/FlashAccess.cs | 330 |
| Model/FlashDiagnosis.cs | 25 |
| Model/FreqMeasure/FreqCounter.cs | 2 |
| Model/FreqMeasure/Ports/Port.cs | 3 |
| Model/Mixi/MixiCal.cs | 110 |
| Model/Mixi/MixiFlashDiag.cs | 32 |
| Model/Mixi/MixiFlashUpdate.cs | 65 |
| Model/Mixi/MixiRegDiag.cs | 69 |
| Model/Mixi/MixiUtility.cs | 18 |
| Model/Mixi/MixiVer.cs | 92 |
| Model/Mixi/MixiWindow.cs | 30 |
| Model/ProductionConfigClass.cs | 127 |
| Model/RegDiag.cs | 61 |
| Model/ThermalDetect.cs | 22 |
| Model/TmuCalClass.cs | 70 |
| Model/ViRegDiag.cs | 74 |

TRUNK_LOG_CALLS.json 保存文件、行号、所在方法、调用表达式和字符串片段。TRUNK_SOURCE_HASHES.json 标记本次工作副本。

这是输出点清单，不是全部动态输出格式已完成结构化解析的证明。候选包含 UI-only 日志、文本构造、配置文件写入和条件分支；需结合调用链判断最终输出。循环可在同一调用处产生多种运行时值。禁用的 #if 分支和外部 DLL 输出不在本清单里。
