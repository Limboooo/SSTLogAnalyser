# 新参考 LOG 表头与覆盖审计（2026-10-30）

范围：`E:\10_工作项目\SST\LOGSample` 中的完整 LogAll、滚动分卷和 default.log，以及收件箱中的两份 20261030 LOG。共 69 个文件、1,927,612,282 字节，清点到 26 种测量 CSV 表头。路径、大小、SHA-256 和表头实例保存在 `audit/reference-table-inventory.json`。

先对全部 69 份运行解析与源行审计；修复后重新检查全部 47 份含测量表的文件。其余 22 份未出现本次测量表数值行，保留初次审计结果（包括三个老化分卷、诊断日志、MIXI 非 CSV 输出和两份 AdaptStar 工程运行 default.log）。这不表示这些文件应当存在 DC/AC 测试点。

最终已清点的验证表漏行数为 **0**。Covered 按源行计数；同一行产生八个 TMU 路径值或多个 Source Events 时仍只计一次。校准数据与验证数据分开，不能把校准阶段 Difference=0 或无状态记录当作验证通过。

## 本次修复

| 格式或现象 | 处理 |
|---|---|
| TMUVI 标题无逗号；TmuVIChannel / Expect_Level / Actual_Value | 归入 MVPI，QA/QB 与两种扫描方向分别保留；原始 Difference=期望−实测 |
| PE HV 的 Pin (FV) / HV_VALUE 表 | 恢复完整 FV 扫描，保持后续 VIHH/VILH 项目隔离 |
| HVPI 数值末列 Fail | 与 Failed 同样识别，保留显式失败 |
| HVPI 电流校准五列表头/六列数据 | 按源码在 DMM 后恢复遗漏的 ADC 列，保持限值/差值不串列 |
| TMU 在 PASS/Finish 之后异步落盘的行 | 保留原表与参考时间，直到新表/测试开始 |
| HDM/TMUVI/PE/CM 和普通 DC 校准过程表 | 保留到 Source Events，状态 Unknown，不增加验证通过率 |
| TMUVI CVA/CVB 系数 | 按 Channel、项目和系数类型保存 |

已有 HDM 特殊八列表仍按 DMM、HDM、原始差值映射。解析缓存版本为 20，重新导入自动淘汰旧缓存。

## 文件级结果

| File | Points | Covered table rows | Missing verification rows | Calibration-only rows |
|---|---:|---:|---:|---:|
| E:\10_工作项目\SST\LOGSample\AK\AK_Digi\AC_cal---\AdaptstarSystemServiceTool_20261021-160436-LogAll.txt | 147456 | 38912 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\AK\AK_Digi\AC_ver---\AdaptstarSystemServiceTool_20261021-161411-LogAll.txt | 92160 | 20480 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\AK\AK_Digi\DC_cal---AK\AdaptstarSystemServiceTool_20261021-093919-LogAll.txt | 155392 | 155392 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\AK\AK_Digi\DC_ver---AK\AdaptstarSystemServiceTool_20261021-130758-LogAll.txt | 157696 | 156928 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\AK\AK_Digi\Diag---AK\AdaptstarSystemServiceTool_20261021-093142-LogAll.txt | 0 | 0 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\AK\AK_Digi\Tmu---cal\AdaptstarSystemServiceTool_20261021-163320-LogAll.txt | 49152 | 6144 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\AK\AK_Digi\Tmu--ver\AdaptstarSystemServiceTool_20261021-163759-LogAll.txt | 49152 | 6144 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\AK\AK_DPSI\DPSI-55185-MLS2011C000213 DC_cal---\AdaptstarSystemServiceTool_20261021-142510-LogAll.txt | 5696 | 5696 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\AK\AK_DPSI\DPSI-55185-MLS2011C000213 DC_ver---\AdaptstarSystemServiceTool_20261021-145709-LogAll.txt | 9280 | 8512 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\AK\AK_DPSI\DPSI-55185-MLS2011C000213 Diag----\AdaptstarSystemServiceTool_20261021-154839-LogAll.txt | 0 | 0 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\DIGI_LOG\Digix8---0335---0792-0774-0775-0785-0793-0773-0741-0800---209.9---10.23\AC-cal---10.20\AdaptstarSystemServiceTool_20261020-144124-LogAll.txt | 147456 | 38912 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\DIGI_LOG\Digix8---0335---0792-0774-0775-0785-0793-0773-0741-0800---209.9---10.23\AC-ver---10.20\AdaptstarSystemServiceTool_20261020-144648-LogAll.txt | 92160 | 20480 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\DIGI_LOG\Digix8---0335---0792-0774-0775-0785-0793-0773-0741-0800---209.9---10.23\AC-ver-after-aging---10.23\AdaptstarSystemServiceTool_20261023-165715-LogAll.txt | 92160 | 20480 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\DIGI_LOG\Digix8---0335---0792-0774-0775-0785-0793-0773-0741-0800---209.9---10.23\AC_Diag_CM_VM---10.23\AdaptstarSystemServiceTool_20261023-183841-LogAll.txt | 0 | 0 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\DIGI_LOG\Digix8---0335---0792-0774-0775-0785-0793-0773-0741-0800---209.9---10.23\aging---10.23\AdaptstarSystemServiceTool_20261020-161532-LogAll.txt | 0 | 0 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\DIGI_LOG\Digix8---0335---0792-0774-0775-0785-0793-0773-0741-0800---209.9---10.23\aging---10.23\AdaptstarSystemServiceTool_20261020-161532-LogAll.txt.1 | 0 | 0 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\DIGI_LOG\Digix8---0335---0792-0774-0775-0785-0793-0773-0741-0800---209.9---10.23\aging---10.23\AdaptstarSystemServiceTool_20261020-161532-LogAll.txt.2 | 0 | 0 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\DIGI_LOG\Digix8---0335---0792-0774-0775-0785-0793-0773-0741-0800---209.9---10.23\DC-cal---10.20\AdaptstarSystemServiceTool_20261020-104515-LogAll.txt | 165696 | 163392 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\DIGI_LOG\Digix8---0335---0792-0774-0775-0785-0793-0773-0741-0800---209.9---10.23\DC-ver---10.20\AdaptstarSystemServiceTool_20261020-130157-LogAll.txt | 173504 | 169408 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\DIGI_LOG\Digix8---0335---0792-0774-0775-0785-0793-0773-0741-0800---209.9---10.23\DC-ver-after-aging---10.23\AdaptstarSystemServiceTool_20261023-165835-LogAll.txt | 173513 | 169417 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\DIGI_LOG\Digix8---0335---0792-0774-0775-0785-0793-0773-0741-0800---209.9---10.23\diag---10.20\AdaptstarSystemServiceTool_20261020-104033-LogAll.txt | 0 | 0 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\DIGI_LOG\Digix8---0335---0792-0774-0775-0785-0793-0773-0741-0800---209.9---10.23\diag-after-aging---10.23\AdaptstarSystemServiceTool_20261023-165428-LogAll.txt | 0 | 0 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\DIGI_LOG\Digix8---0335---0792-0774-0775-0785-0793-0773-0741-0800---209.9---10.23\PE HV -cal---10.20\AdaptstarSystemServiceTool_20261020-144942-LogAll.txt | 6912 | 7296 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\DIGI_LOG\Digix8---0335---0792-0774-0775-0785-0793-0773-0741-0800---209.9---10.23\PE HV -ver---10.20\AdaptstarSystemServiceTool_20261020-145220-LogAll.txt | 6912 | 6912 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\DIGI_LOG\Digix8---0335---0792-0774-0775-0785-0793-0773-0741-0800---209.9---10.23\TMU-cal---10.20\AdaptstarSystemServiceTool_20261020-145454-LogAll.txt | 49152 | 6144 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\DIGI_LOG\Digix8---0335---0792-0774-0775-0785-0793-0773-0741-0800---209.9---10.23\TMU-ver---10.20\AdaptstarSystemServiceTool_20261020-145935-LogAll.txt | 49152 | 6144 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\DPSI_LOG\DPSI-55185-MLS2011C000215-DC-cal---10.23\AdaptstarSystemServiceTool_20261022-174740-LogAll.txt | 5696 | 5696 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\DPSI_LOG\DPSI-55185-MLS2011C000215-DC-ver---10.23\AdaptstarSystemServiceTool_20261023-085455-LogAll.txt | 9280 | 8512 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\DPSI_LOG\DPSI-55185-MLS2011C000215-Diag---10.23\AdaptstarSystemServiceTool_20261023-101517-LogAll.txt | 0 | 0 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\mix-2_log\MIX2-55259-JLS2021C000113,55259-JLS2021C000150_AC_Diag---20261027\default.log | 0 | 0 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\mix-2_log\MIX2-55259-JLS2021C000113,55259-JLS2021C000150_AC_left---20261027\default.log | 0 | 0 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\mix-2_log\MIX2-55259-JLS2021C000113,55259-JLS2021C000150_DC_cal---20261027\AdaptstarSystemServiceTool_20261027-162549-LogAll.txt | 3952 | 0 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\mix-2_log\MIX2-55259-JLS2021C000113,55259-JLS2021C000150_DC_ver---20261027\AdaptstarSystemServiceTool_20261027-164841-LogAll.txt | 3344 | 0 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\MVPI_LOG\MVPIB-55299-DKS2012C000215---209.9--20261027\MVPIB-55299-DKS2012C000215_DC_cal---20261027\AdaptstarSystemServiceTool_20261027-154110-LogAll.txt | 13504 | 12768 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\MVPI_LOG\MVPIB-55299-DKS2012C000215---209.9--20261027\MVPIB-55299-DKS2012C000215_DC_ver---20261027\AdaptstarSystemServiceTool_20261027-163357-LogAll.txt | 15968 | 14816 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\MVPI_LOG\MVPIB-55299-DKS2012C000215---209.9--20261027\MVPIB-55299-DKS2012C000215_Diag---20261027\AdaptstarSystemServiceTool_20261027-172147-LogAll.txt | 0 | 0 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\MVPI_LOG\MVPIB-55299-DKS2012C000221---209.9---20261027\MVPIB-55299-DKS2012C000221_DC_cal---20261027\AdaptstarSystemServiceTool_20261027-100611-LogAll.txt | 13504 | 12760 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\MVPI_LOG\MVPIB-55299-DKS2012C000221---209.9---20261027\MVPIB-55299-DKS2012C000221_DC_ver---20261027\AdaptstarSystemServiceTool_20261027-110227-LogAll.txt | 15968 | 14816 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Astar\MVPI_LOG\MVPIB-55299-DKS2012C000221---209.9---20261027\MVPIB-55299-DKS2012C000221_Diag---20261027\AdaptstarSystemServiceTool_20261027-121801-LogAll.txt | 0 | 0 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\DK\DIGI\AC Cal\AdaptstarSystemServiceTool_20261029-170828-LogAll.txt | 184320 | 48640 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\DK\DIGI\AC Ver\AdaptstarSystemServiceTool_20261029-172606-LogAll.txt | 115200 | 25600 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\DK\DIGI\DC AC Diag\AdaptstarSystemServiceTool_20261029-135242-LogAll.txt | 0 | 0 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\DK\DIGI\DC Cal\AdaptstarSystemServiceTool_20261029-151120-LogAll.txt | 188160 | 188160 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\DK\DIGI\DC Ver\AdaptstarSystemServiceTool_20261029-161545-LogAll.txt | 188160 | 188160 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\DK\DIGI\TMU\AdaptstarSystemServiceTool_20261029-172758-LogAll.txt | 61440 | 7680 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\DK\DPSI\Diag\AdaptstarSystemServiceTool_20261029-150953-LogAll.txt | 0 | 0 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\DK\DPSI\DPSI Cal\AdaptstarSystemServiceTool_20261029-173224-LogAll.txt | 22784 | 22784 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Dstar\DIGI-LOG\Digi_AC_cal----10.27\AdaptstarSystemServiceTool_20261027-162003-LogAll.txt | 165888 | 43776 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Dstar\DIGI-LOG\Digi_AC_ver---10.27\AdaptstarSystemServiceTool_20261027-163632-LogAll.txt | 103680 | 23040 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Dstar\DIGI-LOG\Digi_DC_cal---10.27\AdaptstarSystemServiceTool_20261027-092439-LogAll.txt | 185040 | 182448 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Dstar\DIGI-LOG\Digi_DC_ver----10.27\AdaptstarSystemServiceTool_20261027-142721-LogAll.txt | 193320 | 188712 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Dstar\DIGI-LOG\DIGI_Diag----10.26\AdaptstarSystemServiceTool_20261027-135106-LogAll.txt | 0 | 0 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Dstar\DIGI-LOG\PE_HV_cal----10.27\AdaptstarSystemServiceTool_20261027-164034-LogAll.txt | 7776 | 8208 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Dstar\DIGI-LOG\PE_HV_ver---- 10.28\AdaptstarSystemServiceTool_20261028-092157-LogAll.txt | 8640 | 8640 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Dstar\DPSI_LOG\DPSI_6_7_DC_cal---10.21\AdaptstarSystemServiceTool_20261021-174333-LogAll.txt | 11392 | 11392 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Dstar\DPSI_LOG\DPSI_6_7_DC_ver---10.21\AdaptstarSystemServiceTool_20261022-123027-LogAll.txt | 18560 | 17024 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Dstar\DPSI_LOG\DPSI_CAL_FAIL----10.28-1\AdaptstarSystemServiceTool_20261028-143016-LogAll.txt | 13338 | 13338 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Dstar\DPSI_LOG\dpsi_cal_FAIL---10.28-2\AdaptstarSystemServiceTool_20261028-162624-LogAll.txt | 13298 | 13298 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Dstar\DPSI_LOG\DPSI_Diag----10.21\AdaptstarSystemServiceTool_20261022-144146-LogAll.txt | 0 | 0 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Dstar\DPSI_LOG\DPSI_slot9_55185-MLS2011C000210 DC_cal----10.20\AdaptstarSystemServiceTool_20261020-154110-LogAll.txt | 5696 | 5696 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Dstar\DPSI_LOG\DPSI_slot9_55185-MLS2011C000210 DC_ver----10.20\AdaptstarSystemServiceTool_20261020-164046-LogAll.txt | 9280 | 8512 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Dstar\MIX-2_LOG\MIX2-55259-JNS2021C000227,55259-JNS2021C000210_DC_cal---20261016\AdaptstarSystemServiceTool_20261016-174725-LogAll.txt | 6008 | 0 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Dstar\MIX-2_LOG\MIX2-55259-JNS2021C000227,55259-JNS2021C000210_DC_ver---20261016\AdaptstarSystemServiceTool_20261016-181457-LogAll.txt | 3344 | 0 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Dstar\MVPI_LOG\MVPI_dc_CAL\AdaptstarSystemServiceTool_20261022-144721-LogAll.txt | 33280 | 34880 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Dstar\MVPI_LOG\MVPI_DC_cal---10.23\AdaptstarSystemServiceTool_20261022-163950-LogAll.txt | 33315 | 34915 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Dstar\MVPI_LOG\MVPI_DC_ver----10.23\AdaptstarSystemServiceTool_20261023-092743-LogAll.txt | 35072 | 32768 | 0 | 0 |
| E:\10_工作项目\SST\LOGSample\Dstar\MVPI_LOG\mvpi_Diag---10.22\AdaptstarSystemServiceTool_20261022-144321-LogAll.txt | 0 | 0 | 0 | 0 |
| E:\00_收件箱\AdaptstarSystemServiceTool_20261030-113157-LogAll.txt | 60120 | 60120 | 0 | 0 |
| E:\00_收件箱\HVPI Log ReCal fail\AdaptstarSystemServiceTool_20261030-105418-LogAll.txt | 4657 | 4885 | 0 | 0 |

## 测量表头清单

- `#PinNum, CMPH(ns), CMPL(ns), Target(ns), Spec(ns), MaxError(ns), Result`
- `#PinNum, RISE(ns), FALL(ns), ON(ns), OFF(ns), Target(ns), Spec(ns), MaxError(ns), Result`
- `Expect, DmmMeasure, LowLimit, UpLimit, Difference`
- `Expect, DmmMeasure, DpsAdcMeasure, LowLimit, UpLimit, Difference`
- `Expect, Measure, LowLimit, UpLimit, Difference`
- `Expect, DpsAdcMeasure, LowLimit, UpLimit, Difference`
- `Pin (FV), Expect, Measure, LowLimit, UpLimit, Difference`
- `Pin (MV), Expect, Measure, LowLimit, UpLimit, Difference`
- `Pin (FI), Expect, Measure, LowLimit, UpLimit, Difference`
- `Pin (MI), Expect, Measure, LowLimit, UpLimit, Difference`
- `Pin, Expect, Measure, LowLimit, UpLimit, Difference`
- `PinNum, Tmu0_VOLStart_VOLStop, Tmu0_VOHStart_VOHStop, Tmu0_VOHStart_VOLStop, Tmu0_VOLStart_VOHStop, Tmu1_VOLStart_VOLStop, Tmu1_VOHStart_VOHStop, Tmu1_VOHStart_VOLStop, Tmu1_VOLStart_VOHStop, VerifyResult`
- `Expect, DmmMeasure, PmuAdcMeasure, LowLimit, UpLimit, Difference`
- `Pin (FV), HV_VALUE, Measure, LowLimit, UpLimit, Difference`
- `Expect, DmmMeasure, MvpiAdcMeasure, LowLimit, UpLimit, Difference`
- `Expect, DmmMeasure, MdmAdcMeasure, LowLimit, UpLimit, Difference`
- `TmuVIChannel, QA_Level, dTargetValue, Actual_Value`
- `TmuVIChannel, Expect_Level, Actual_Value, LowLimit, UpLimit, Difference`
- `TmuVIChannel, QB_Level, dTargetValue, Actual_Value, Difference`
- `ExpectV, MeasureI, LowLimit, UpLimit, Difference`
- `Expect, DmmMeasure, ADCMeasure, LowLimit, UpLimit, Difference, ADCDifference`
- `Expect, DmmMeasure, AdcMeasure, LowLimit, UpLimit, Difference`
- `FV_Value, MI_CM, MI_ADC, MV_ADC, Low Limit, High Limit`
- `FV_Value, MI_CM, MI_ADC, MV_ADC, Low Limit, High Limit, CM_Difference`
- `HVIChannel, TargetValue, HDM_Value, ActualValue, Difference`
- `HVIChannel, TargetValue, HDM_Value, ActualValue, LowerLimit, HighLimit, Difference, Result`

该检查核对源数值行覆盖，新增格式另有逐列/符号、缓存、图表和真实摘录回归。不代表全部任意文本、STDF/压缩附件或真实硬件已验证。
