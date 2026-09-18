# SST Log Analyser v1.2.4 Release Notes

Release date: 2026-08-04

## MIXI WAVE/OFFSET error attribution

- Replaced the Diagnostics POS/NEG mismatch panel with a WAVE/OFFSET attribution view.
- Automatically pairs MIXI `*_POS` and `*_NEG` sibling Test Items.
- Validates that targets are composed from WAVE and OFFSET before including points in the model.
- Fits WAVE slope error, OFFSET slope error, and common bias independently, with R-squared and RMSE confidence indicators.
- Added Detail mode with WAVE/OFFSET/Bias contribution columns and Actual/Prediction residual curves.
- Added Channel Map mode for scanning many Channels and identifying the riskiest Loop per Channel.
- A single selected Channel defaults to Detail; multiple Channels or ALL default to Channel Map.
- The new panel supports individual expansion and PNG export.
