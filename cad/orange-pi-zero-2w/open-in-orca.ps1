$ErrorActionPreference = 'Stop'
$orca = 'C:\Users\kingd\OneDrive\Documents\3d\OrcaSlicer\orca-slicer.exe'
$threeMf = Join-Path $PSScriptRoot 'zero2w-case.3mf'

if (-not (Test-Path -LiteralPath $orca)) {
    throw "Orca not found: $orca"
}
if (-not (Test-Path -LiteralPath $threeMf)) {
    Write-Host "3MF missing; running export-3mf.ps1..."
    & (Join-Path $PSScriptRoot 'export-3mf.ps1') -OpenScadPath 'C:\Program Files\OpenSCAD\openscad.exe'
}
if (-not (Test-Path -LiteralPath $threeMf)) {
    throw "3MF still missing: $threeMf"
}

Start-Process -FilePath $orca -ArgumentList @($threeMf)
Write-Host "ORCA_OPENED $threeMf"
