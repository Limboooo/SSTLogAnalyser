param([switch]$Generate, [switch]$Audit)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Regression.csproj'
if ($Audit) {
    & dotnet run --project $project -c Release -p:NoWarn=NU1701 -- --audit
} elseif ($Generate) {
    & dotnet run --project $project -c Release -p:NoWarn=NU1701 -- --generate
} else {
    & dotnet run --project $project -c Release -p:NoWarn=NU1701
}
if ($LASTEXITCODE -ne 0) { throw "Regression failed (exit $LASTEXITCODE)." }
