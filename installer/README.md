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

- `dist\SSTLogAnalyser-v1.2.6-win-x64.msi`
- `dist\SSTLogAnalyser-v1.2.6-win-x64.msi.sha256`

The MSI defaults to the current user's local Programs directory, does not require administrator rights for that location, and lets the user choose another installation folder. It creates Start menu and desktop shortcuts, and supports upgrades and uninstall through Windows Settings.

## v1.2.6 sharing package

The generated local `dist/SSTLogAnalyser-v1.2.6-win-x64-share.zip` contains:

- The MSI and SHA256 file.
- `USER_MANUAL.md`, `RELEASE_NOTES_v1.2.6.md`, and brief Chinese installation instructions.
- `Samples/22_Comparison_Baseline.log` and `Samples/23_Comparison_Current.log`.

Recipients extract the ZIP, install the MSI, and import the two sample LOGs. In Baseline Comparison, select the baseline and current sample, choose Loop 1 on both sides, and click Compare. Expected result: 24 matched points and 2 new failures. Drag the grey divider to resize the chart and results table.

For this build, the MSI is approximately 53.5 MiB and the share ZIP is approximately 56 MiB. The MSI product version, included runtime, and release notes were checked; the self-contained published application started successfully. No user cache was included. Installation on a separate computer has not been tested.

Binary packages in `dist` and staging content in `artifacts` are ignored by Git. Use the build command above to generate the installer; assemble the share ZIP from the installer, documents, instructions, and the two repository fixtures.
