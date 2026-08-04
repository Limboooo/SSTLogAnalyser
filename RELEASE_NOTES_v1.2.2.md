# SST Log Analyser v1.2.2 Release Notes

Release date: 2026-07-24

## Chart image export

- Added a Save image button to the main Chart toolbar.
- Added a Save image button to each Diagnostics panel: Tolerance Utilization Heatmap, Gain / Offset Distribution, Residual Signature, and MIXI / AWG POS-NEG Symmetry Mismatch.
- Exports the current visible chart state as PNG, including the active zoom, selected or focused series, filters, summaries, and visible legend.
- Uses the current display DPI so exported text and lines remain sharp on high-DPI monitors.
- Generates the default name as `TestItem - Module - CH - yyyyMMdd-HHmmss.png`; large multi-channel selections are summarized to keep the name within Windows path limits.
- Allows the destination folder and file name to be changed before saving.
