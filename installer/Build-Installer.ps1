param(
    [string]$Version = "1.2.3",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectPath = Join-Path $repoRoot "SSTLogAnalyser.csproj"
$publishDir = Join-Path $repoRoot "artifacts\publish\win-x64"
$distDir = Join-Path $repoRoot "dist"
$installerPath = Join-Path $distDir "SSTLogAnalyser-v$Version-win-x64.msi"
$wixSource = Join-Path $PSScriptRoot "Product.wxs"
$wixUiSource = Join-Path $PSScriptRoot "InstallerUI.wxs"

New-Item -ItemType Directory -Force -Path $publishDir, $distDir | Out-Null

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDir `
    -p:PublishProfile=win-x64 `
    -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$wixCommand = Get-Command wix.exe -ErrorAction SilentlyContinue
$wixExe = $wixCommand.Source
if (-not $wixCommand) {
    $wixToolPath = Join-Path $env:USERPROFILE ".dotnet\tools\wix.exe"
    if (Test-Path -LiteralPath $wixToolPath) {
        $wixExe = $wixToolPath
    }
}
if (-not $wixExe) {
    throw "WiX Toolset was not found. Install it with: dotnet tool install --global wix --version 5.0.2"
}

$wixVersion = (& $wixExe --version).Trim()
if (-not $wixVersion.StartsWith("5.")) {
    throw "WiX Toolset 5.x is required. Found: $wixVersion"
}

& $wixExe build $wixSource $wixUiSource `
    -ext WixToolset.UI.wixext `
    -arch x64 `
    -d "AppVersion=$Version" `
    -d "PublishDir=$publishDir" `
    -d "ProjectRoot=$repoRoot" `
    -defaultcompressionlevel high `
    -pdbtype none `
    -o $installerPath
if ($LASTEXITCODE -ne 0) { throw "WiX build failed with exit code $LASTEXITCODE." }

$hash = Get-FileHash -LiteralPath $installerPath -Algorithm SHA256
$hashPath = "$installerPath.sha256"
"$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($installerPath))" |
    Set-Content -LiteralPath $hashPath -Encoding ascii

Write-Host "Installer: $installerPath"
Write-Host "SHA256:    $($hash.Hash.ToLowerInvariant())"
