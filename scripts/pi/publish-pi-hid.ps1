param(
    [string]$Configuration = 'Release',
    [string]$Output = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $repoRoot 'output/pi-hid/linux-arm64'
}

Set-Location $repoRoot
dotnet publish src/MouseKeyProxy.PiHid/MouseKeyProxy.PiHid.csproj -c $Configuration -r linux-arm64 --self-contained true -o $Output
Write-Host "Pi HID publish output: $Output"
