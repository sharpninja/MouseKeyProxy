# Force a clean OpenSCAD rebuild and open a NEW 3MF path so Orca cannot show a stale tab.
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$openscad = 'C:\Program Files\OpenSCAD\openscad.exe'
$orca = 'C:\Users\kingd\OneDrive\Documents\3d\OrcaSlicer\orca-slicer.exe'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$baseStl = Join-Path $here 'zero2w-case-base.stl'
$lidStl = Join-Path $here 'zero2w-case-lid.stl'
$out3mf = Join-Path $here ("zero2w-case-{0}.3mf" -f $stamp)
$latest = Join-Path $here 'zero2w-case.3mf'
$logBase = Join-Path $here '_export-base.log'
$logLid = Join-Path $here '_export-lid.log'

if (-not (Test-Path -LiteralPath $openscad)) { throw "OpenSCAD not found: $openscad" }
if (-not (Test-Path -LiteralPath $orca)) { throw "Orca not found: $orca" }

# Delete intermediates so we cannot pack stale meshes
Remove-Item -LiteralPath $baseStl, $lidStl -Force -ErrorAction SilentlyContinue

function Invoke-OpenScad {
    param([string]$Out, [string]$Scad, [string]$Log)
    $p = Start-Process -FilePath $openscad -ArgumentList @('-o', $Out, $Scad) `
        -Wait -PassThru -NoNewWindow `
        -RedirectStandardError $Log `
        -RedirectStandardOutput ($Log + '.out')
    $err = if (Test-Path $Log) { Get-Content $Log -Raw } else { '' }
    if ($err -match 'ERROR|Parser error|Can''t parse') {
        Write-Host $err
        throw "OpenSCAD reported errors for $Scad"
    }
    if ($p.ExitCode -ne 0) { throw "OpenSCAD exit $($p.ExitCode) for $Scad" }
    if (-not (Test-Path -LiteralPath $Out) -or (Get-Item $Out).Length -lt 1000) {
        throw "Missing/small output: $Out"
    }
    Write-Host ("OK {0} bytes={1}" -f (Split-Path $Out -Leaf), (Get-Item $Out).Length)
    if ($err) {
        $vol = ($err | Select-String -Pattern 'Volumes:\s+\d+|Simple:\s+\w+|Vertices:\s+\d+|Facets:\s+\d+')
        if ($vol) { $vol | ForEach-Object { Write-Host $_.Line } }
    }
}

Write-Host "=== BASE ==="
Invoke-OpenScad -Out $baseStl -Scad (Join-Path $here 'export-base.scad') -Log $logBase
Write-Host "=== LID ==="
Invoke-OpenScad -Out $lidStl -Scad (Join-Path $here 'export-lid.scad') -Log $logLid

# Pack via existing helper (reads STLs we just wrote)
Write-Host "=== 3MF ==="
& (Join-Path $here 'export-3mf.ps1')
# export-3mf re-runs openscad; that is fine. Copy to stamped name after.
if (-not (Test-Path -LiteralPath $latest)) { throw "export-3mf did not write $latest" }
Copy-Item -LiteralPath $latest -Destination $out3mf -Force

$fi = Get-Item $out3mf
Write-Host ("FRESH_3MF path={0} len={1} time={2}" -f $fi.FullName, $fi.Length, $fi.LastWriteTime)

Start-Process -FilePath $orca -ArgumentList @($out3mf)
Write-Host "ORCA_OPENED $out3mf"
Write-Host "Close any old Orca tab of zero2w-case.3mf — use this new file."
