# Installer Build

The installer is a self-contained Windows x64 MSI. It includes the .NET 9 Windows Desktop runtime, so the target computer does not need a separate .NET installation.

## Prerequisites

```powershell
dotnet tool install --global wix --version 5.0.2
wix extension add -g WixToolset.UI.wixext/5.0.2
```

## Build

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\Build-Installer.ps1
```

Outputs:

- `dist\SSTLogAnalyser-v1.2.8-win-x64.msi`
- `dist\SSTLogAnalyser-v1.2.8-win-x64.msi.sha256`

The current release is v1.2.8. It includes AC overview, relative Pin/Group timing, across-point heatmaps and trends, and the AC boundary parsing fix. See `RELEASE_NOTES_v1.2.8.md` for verified scope. The v1.2.8 share ZIP includes the installer, checksum, manual, release notes, Chinese installation instructions and the two synthetic baseline-comparison samples; it excludes real hardware LOGs and user cache.

Previous v1.2.7 validation: its MSI is 53.53 MiB and its share ZIP is 53.13 MiB. Administrative extraction succeeded; all 419 extracted payload files matched the published files by SHA256. The extracted application created a responsive window, and its host trace confirmed use of the bundled `hostfxr.dll` as a self-contained app. The MSI product version, shortcut targets, package checksum and ZIP's embedded MSI were checked. The published parser also reproduced all 256 real HVPI verification points and 13 failures with zero source-column mismatches. Installation on another computer has not been tested.

The MSI defaults to the current user's local Programs directory, does not require administrator rights for that location, and lets the user choose another installation folder. It creates Start menu and desktop shortcuts, and supports upgrades and uninstall through Windows Settings.

## v1.2.6 sharing package

The generated local `dist/SSTLogAnalyser-v1.2.6-win-x64-share.zip` contains:

- The MSI and SHA256 file.
- `USER_MANUAL.md`, `RELEASE_NOTES_v1.2.6.md`, and brief Chinese installation instructions.
- `Samples/22_Comparison_Baseline.log` and `Samples/23_Comparison_Current.log`.

Recipients extract the ZIP, install the MSI, and import the two sample LOGs. In Baseline Comparison, select the baseline and current sample, choose Loop 1 on both sides, and click Compare. Expected result: 24 matched points and 2 new failures. Drag the grey divider to resize the chart and results table.

For this build, the MSI is approximately 53.5 MiB and the share ZIP is approximately 56 MiB. The MSI product version, included runtime, and release notes were checked; the self-contained published application started successfully. No user cache was included. Installation on a separate computer has not been tested.

Binary packages in `dist` and staging content in `artifacts` are ignored by Git. Use the build command above to generate the installer; assemble the share ZIP from the installer, documents, instructions, and the two repository fixtures.
