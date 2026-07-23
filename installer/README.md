# Installer Build

The installer is a self-contained Windows x64 MSI. It includes the .NET 9 Windows Desktop runtime, so the target computer does not need a separate .NET installation.

## Prerequisites

```powershell
dotnet tool install --global wix --version 5.0.2
```

## Build

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\Build-Installer.ps1
```

Outputs:

- `dist\SSTLogAnalyser-v1.2.0-win-x64.msi`
- `dist\SSTLogAnalyser-v1.2.0-win-x64.msi.sha256`

The MSI installs the application for the current user without requiring administrator rights. It creates Start menu and desktop shortcuts, and supports upgrades and uninstall through Windows Settings.
