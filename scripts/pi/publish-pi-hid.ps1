param(
    [string]$Configuration = 'Release',
    [ValidateSet('linux-arm', 'linux-arm64')]
    [string]$Rid = 'linux-arm64',
    [string]$Output = '',
    # Also publish the main MouseKeyProxy.Service for appliance deploy.
    [switch]$Service
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $repoRoot "output/pi-hid/$Rid"
}

Set-Location $repoRoot
dotnet publish src/MouseKeyProxy.PiHid/MouseKeyProxy.PiHid.csproj -c $Configuration -r $Rid --self-contained true -o $Output
Write-Host "Pi HID publish output: $Output (rid=$Rid)"

if ($Service) {
    $svcOut = Join-Path $repoRoot "output/service/$Rid"
    dotnet publish src/MouseKeyProxy.Service/MouseKeyProxy.Service.csproj -c $Configuration -r $Rid --self-contained true -o $svcOut
    Write-Host "Service publish output: $svcOut (rid=$Rid)"
}
