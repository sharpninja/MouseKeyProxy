# Pack existing STLs into a stamped 3MF and open in Orca (no OpenSCAD re-run).
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$orca = 'C:\Users\kingd\OneDrive\Documents\3d\OrcaSlicer\orca-slicer.exe'
$baseStl = Join-Path $here 'zero2w-case-base.stl'
$lidStl = Join-Path $here 'zero2w-case-lid.stl'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$fresh = Join-Path $here ("zero2w-case-{0}.3mf" -f $stamp)
$latest = Join-Path $here 'zero2w-case.3mf'

if (-not (Test-Path $baseStl) -or (Get-Item $baseStl).Length -lt 1000) { throw "missing base STL" }
if (-not (Test-Path $lidStl) -or (Get-Item $lidStl).Length -lt 1000) { throw "missing lid STL" }

# Load packing helpers by extracting functions from export-3mf.ps1 via parse - simpler: call with a flag.
# Inline minimal pack by invoking export-3mf's New-3mfFromMeshes after reading its script as text is fragile.
# Instead run a sub-script that only defines functions.

$helper = Join-Path $here '_pack-helpers.ps1'
# Strip main from export-3mf: take everything before "# --- main ---"
$src = Get-Content (Join-Path $here 'export-3mf.ps1') -Raw
$idx = $src.IndexOf('# --- main ---')
if ($idx -lt 0) { throw 'export-3mf.ps1 missing main marker' }
$funcs = $src.Substring(0, $idx)
# Remove param block and early path setup that runs at parse - keep functions only
# The file starts with param/CmdletBinding and path vars - functions start at function Find-OpenScad
$fn = $src.IndexOf('function Find-OpenScad')
if ($fn -lt 0) { throw 'functions not found' }
$end = $src.IndexOf('# --- main ---')
Set-Content -LiteralPath $helper -Value $src.Substring($fn, $end - $fn) -Encoding UTF8
. $helper

$baseMesh = Read-Stl -Path $baseStl
$lidMesh = Read-Stl -Path $lidStl
Write-Host ("base tris={0} verts={1} mtime={2}" -f $baseMesh.Triangles.Count, $baseMesh.Vertices.Count, (Get-Item $baseStl).LastWriteTime)
Write-Host ("lid  tris={0} verts={1} mtime={2}" -f $lidMesh.Triangles.Count, $lidMesh.Vertices.Count, (Get-Item $lidStl).LastWriteTime)

New-3mfFromMeshes -BaseMesh $baseMesh -LidMesh $lidMesh -OutputPath $latest -PlateGap 40.0
Copy-Item -LiteralPath $latest -Destination $fresh -Force

Write-Host ("FRESH_3MF {0} bytes={1}" -f $fresh, (Get-Item $fresh).Length)
Start-Process -FilePath $orca -ArgumentList @($fresh)
Write-Host "ORCA_OPENED $fresh"
Write-Host "Close old zero2w-case.3mf tabs in Orca - this is a new filename."
Remove-Item -LiteralPath $helper -Force -ErrorAction SilentlyContinue
