# Export Orange Pi Zero 2W case as a multi-object 3MF (base + lid) for Orca Slicer.
# Requires OpenSCAD CLI. Optionally opens the 3MF in Orca.
[CmdletBinding()]
param(
    [string]$OpenScadPath = '',
    [string]$OrcaPath = 'C:\Users\kingd\OneDrive\Documents\3d\OrcaSlicer\orca-slicer.exe',
    [switch]$OpenInOrca
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$scadBase = Join-Path $here 'export-base.scad'
$scadLid = Join-Path $here 'export-lid.scad'
$baseStl = Join-Path $here 'zero2w-case-base.stl'
$lidStl = Join-Path $here 'zero2w-case-lid.stl'
$out3mf = Join-Path $here 'zero2w-case.3mf'

function Find-OpenScad {
    param([string]$Explicit)
    if ($Explicit -and (Test-Path -LiteralPath $Explicit)) { return (Resolve-Path $Explicit).Path }
    $cmd = Get-Command openscad -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidates = @(
        'C:\Program Files\OpenSCAD\openscad.exe',
        'C:\Program Files\OpenSCAD (Nightly)\openscad.exe',
        (Join-Path $env:LOCALAPPDATA 'Programs\OpenSCAD\openscad.exe'),
        (Join-Path $env:USERPROFILE 'scoop\apps\openscad\current\openscad.exe')
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path -LiteralPath $c)) { return $c }
    }
    return $null
}

function Read-StlAscii {
    param([string]$Path)
    $vertices = New-Object System.Collections.Generic.List[object]
    $triangles = New-Object System.Collections.Generic.List[object]
    $indexByKey = @{}
    $text = Get-Content -LiteralPath $Path -Raw
    $facetRe = [regex]'facet\s+normal\s+[-\d.eE+]+\s+[-\d.eE+]+\s+[-\d.eE+]+\s+outer\s+loop\s+vertex\s+([-\d.eE+]+)\s+([-\d.eE+]+)\s+([-\d.eE+]+)\s+vertex\s+([-\d.eE+]+)\s+([-\d.eE+]+)\s+([-\d.eE+]+)\s+vertex\s+([-\d.eE+]+)\s+([-\d.eE+]+)\s+([-\d.eE+]+)\s+endloop\s+endfacet'
    $matches = $facetRe.Matches($text)
    if ($matches.Count -eq 0) {
        throw "No ASCII facets found in $Path (binary STL not supported by this exporter)."
    }
    foreach ($m in $matches) {
        $idxs = @()
        for ($i = 0; $i -lt 3; $i++) {
            $x = [double]$m.Groups[1 + $i * 3].Value
            $y = [double]$m.Groups[2 + $i * 3].Value
            $z = [double]$m.Groups[3 + $i * 3].Value
            # quantize for weld
            $key = ('{0:F5}|{1:F5}|{2:F5}' -f $x, $y, $z)
            if (-not $indexByKey.ContainsKey($key)) {
                $indexByKey[$key] = $vertices.Count
                $vertices.Add([pscustomobject]@{ X = $x; Y = $y; Z = $z }) | Out-Null
            }
            $idxs += $indexByKey[$key]
        }
        $triangles.Add([pscustomobject]@{ V1 = $idxs[0]; V2 = $idxs[1]; V3 = $idxs[2] }) | Out-Null
    }
    return [pscustomobject]@{ Vertices = $vertices; Triangles = $triangles }
}

function Read-StlBinary {
    param([string]$Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 84) { throw "STL too small: $Path" }
    $triCount = [BitConverter]::ToUInt32($bytes, 80)
    $expected = 84 + (50 * $triCount)
    if ($bytes.Length -lt $expected) { throw "Binary STL truncated: $Path" }

    $vertices = New-Object System.Collections.Generic.List[object]
    $triangles = New-Object System.Collections.Generic.List[object]
    $indexByKey = @{}
    $offset = 84
    for ($t = 0; $t -lt $triCount; $t++) {
        $offset += 12 # skip normal
        $idxs = @()
        for ($i = 0; $i -lt 3; $i++) {
            $x = [BitConverter]::ToSingle($bytes, $offset); $offset += 4
            $y = [BitConverter]::ToSingle($bytes, $offset); $offset += 4
            $z = [BitConverter]::ToSingle($bytes, $offset); $offset += 4
            $key = ('{0:F5}|{1:F5}|{2:F5}' -f $x, $y, $z)
            if (-not $indexByKey.ContainsKey($key)) {
                $indexByKey[$key] = $vertices.Count
                $vertices.Add([pscustomobject]@{ X = [double]$x; Y = [double]$y; Z = [double]$z }) | Out-Null
            }
            $idxs += $indexByKey[$key]
        }
        $offset += 2 # attribute byte count
        $triangles.Add([pscustomobject]@{ V1 = $idxs[0]; V2 = $idxs[1]; V3 = $idxs[2] }) | Out-Null
    }
    return [pscustomobject]@{ Vertices = $vertices; Triangles = $triangles }
}

function Read-Stl {
    param([string]$Path)
    $fs = [System.IO.File]::OpenRead($Path)
    try {
        $buf = New-Object byte[] 5
        [void]$fs.Read($buf, 0, 5)
        $head = [Text.Encoding]::ASCII.GetString($buf)
    } finally {
        $fs.Dispose()
    }
    if ($head -like 'solid*') {
        # Could still be binary with "solid" header; check size heuristic
        $len = (Get-Item -LiteralPath $Path).Length
        $bytes = [System.IO.File]::ReadAllBytes($Path)
        $triCount = [BitConverter]::ToUInt32($bytes, 80)
        $expected = 84 + (50L * $triCount)
        if ($len -eq $expected) {
            return Read-StlBinary -Path $Path
        }
        return Read-StlAscii -Path $Path
    }
    return Read-StlBinary -Path $Path
}

function ConvertTo-MeshXml {
    param($Mesh, [int]$ObjectId, [string]$Name)
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine(('    <object id="{0}" name="{1}" type="model">' -f $ObjectId, $Name))
    [void]$sb.AppendLine('      <mesh>')
    [void]$sb.AppendLine('        <vertices>')
    foreach ($v in $Mesh.Vertices) {
        [void]$sb.AppendLine(('          <vertex x="{0:G9}" y="{1:G9}" z="{2:G9}" />' -f $v.X, $v.Y, $v.Z))
    }
    [void]$sb.AppendLine('        </vertices>')
    [void]$sb.AppendLine('        <triangles>')
    foreach ($t in $Mesh.Triangles) {
        [void]$sb.AppendLine(('          <triangle v1="{0}" v2="{1}" v3="{2}" />' -f $t.V1, $t.V2, $t.V3))
    }
    [void]$sb.AppendLine('        </triangles>')
    [void]$sb.AppendLine('      </mesh>')
    [void]$sb.AppendLine('    </object>')
    return $sb.ToString()
}

function Get-MeshBounds {
    param($Mesh)
    $minX = [double]::PositiveInfinity; $maxX = [double]::NegativeInfinity
    $minY = [double]::PositiveInfinity; $maxY = [double]::NegativeInfinity
    $minZ = [double]::PositiveInfinity; $maxZ = [double]::NegativeInfinity
    foreach ($v in $Mesh.Vertices) {
        if ($v.X -lt $minX) { $minX = $v.X }; if ($v.X -gt $maxX) { $maxX = $v.X }
        if ($v.Y -lt $minY) { $minY = $v.Y }; if ($v.Y -gt $maxY) { $maxY = $v.Y }
        if ($v.Z -lt $minZ) { $minZ = $v.Z }; if ($v.Z -gt $maxZ) { $maxZ = $v.Z }
    }
    return [pscustomobject]@{
        MinX = $minX; MaxX = $maxX
        MinY = $minY; MaxY = $maxY
        MinZ = $minZ; MaxZ = $maxZ
        SizeX = $maxX - $minX
        SizeY = $maxY - $minY
        SizeZ = $maxZ - $minZ
    }
}

function Move-Mesh {
    param($Mesh, [double]$Dx = 0, [double]$Dy = 0, [double]$Dz = 0)
    # PSCustomObject copies break in foreach — rewrite list entries
    for ($i = 0; $i -lt $Mesh.Vertices.Count; $i++) {
        $v = $Mesh.Vertices[$i]
        $Mesh.Vertices[$i] = [pscustomobject]@{
            X = $v.X + $Dx
            Y = $v.Y + $Dy
            Z = $v.Z + $Dz
        }
    }
}

function New-3mfFromMeshes {
    param(
        $BaseMesh,
        $LidMesh,
        [string]$OutputPath,
        # Orca's "too close" warning is conservative; keep a wide clear gap.
        [double]$PlateGap = 40.0
    )

    # Normalize both meshes to origin / Z=0, then place lid to the +X of base.
    # Keep each mesh in its own local space and apply plate placement via build
    # item transforms (3MF-standard; more reliable than baking world verts only).
    $bbBase = Get-MeshBounds -Mesh $BaseMesh
    $bbLid = Get-MeshBounds -Mesh $LidMesh
    Move-Mesh -Mesh $BaseMesh -Dx (-$bbBase.MinX) -Dy (-$bbBase.MinY) -Dz (-$bbBase.MinZ)
    Move-Mesh -Mesh $LidMesh -Dx (-$bbLid.MinX) -Dy (-$bbLid.MinY) -Dz (-$bbLid.MinZ)

    $bbBase2 = Get-MeshBounds -Mesh $BaseMesh
    $bbLidLocal = Get-MeshBounds -Mesh $LidMesh
    $lidTx = $bbBase2.SizeX + $PlateGap
    $lidTy = 0.0
    $lidTz = 0.0

    # 3MF transform: 12 floats = 3x3 row-major rotation + translation (tx ty tz)
    # Identity + translate: "1 0 0 0 1 0 0 0 1 tx ty tz"
    $tfBase = '1 0 0 0 1 0 0 0 1 0 0 0'
    $tfLid = ('1 0 0 0 1 0 0 0 1 {0:G9} {1:G9} {2:G9}' -f $lidTx, $lidTy, $lidTz)

    Write-Host ("plate layout: base local X=[{0:F2},{1:F2}] Y=[{2:F2},{3:F2}]" -f `
        $bbBase2.MinX, $bbBase2.MaxX, $bbBase2.MinY, $bbBase2.MaxY)
    Write-Host ("plate layout: lid  local X=[{0:F2},{1:F2}] Y=[{2:F2},{3:F2}]" -f `
        $bbLidLocal.MinX, $bbLidLocal.MaxX, $bbLidLocal.MinY, $bbLidLocal.MaxY)
    Write-Host ("plate layout: lid transform tx={0:F2} gap={1:F2} mm (bbox clear along X)" -f $lidTx, $PlateGap)

    $model = @"
<?xml version="1.0" encoding="UTF-8"?>
<model unit="millimeter" xml:lang="en-US"
  xmlns="http://schemas.microsoft.com/3dmanufacturing/core/2015/02">
  <metadata name="Application">MouseKeyProxy export-3mf.ps1</metadata>
  <metadata name="Title">Orange Pi Zero 2W case (base + lid)</metadata>
  <resources>
$(ConvertTo-MeshXml -Mesh $BaseMesh -ObjectId 1 -Name 'zero2w-case-base')
$(ConvertTo-MeshXml -Mesh $LidMesh -ObjectId 2 -Name 'zero2w-case-lid')
  </resources>
  <build>
    <item objectid="1" transform="$tfBase" />
    <item objectid="2" transform="$tfLid" />
  </build>
</model>
"@

    $contentTypes = @"
<?xml version="1.0" encoding="UTF-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
  <Default Extension="model" ContentType="application/vnd.ms-package.3dmanufacturing-3dmodel+xml" />
</Types>
"@

    $rels = @"
<?xml version="1.0" encoding="UTF-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Target="/3D/3dmodel.model" Id="rel0" Type="http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel" />
</Relationships>
"@

    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("mkp-3mf-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tmp -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $tmp '3D') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $tmp '_rels') -Force | Out-Null

    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText((Join-Path $tmp '[Content_Types].xml'), $contentTypes, $utf8NoBom)
    [System.IO.File]::WriteAllText((Join-Path $tmp '_rels\.rels'), $rels, $utf8NoBom)
    [System.IO.File]::WriteAllText((Join-Path $tmp '3D\3dmodel.model'), $model, $utf8NoBom)

    if (Test-Path -LiteralPath $OutputPath) { Remove-Item -LiteralPath $OutputPath -Force }
    # Zip as 3MF (Store preferred; Deflate is fine for Orca)
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($tmp, $OutputPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    Remove-Item -LiteralPath $tmp -Recurse -Force
}

# --- main ---
if (-not (Test-Path -LiteralPath $scadBase)) { throw "Missing $scadBase" }
if (-not (Test-Path -LiteralPath $scadLid)) { throw "Missing $scadLid" }

$openscad = Find-OpenScad -Explicit $OpenScadPath
if (-not $openscad) {
    throw @"
OpenSCAD CLI not found. Install OpenSCAD, then re-run:
  winget install --id OpenSCAD.OpenSCAD
  pwsh -File cad/orange-pi-zero-2w/export-3mf.ps1 -OpenInOrca
"@
}

Write-Host "OPENSCAD=$openscad"
# Drop stale meshes so a failed OpenSCAD run cannot pack yesterday's STL.
Remove-Item -LiteralPath $baseStl, $lidStl -Force -ErrorAction SilentlyContinue

Write-Host "Exporting base STL..."
& $openscad -o $baseStl $scadBase
if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) {
    throw "OpenSCAD base export exit code: $LASTEXITCODE"
}
if (-not (Test-Path -LiteralPath $baseStl) -or (Get-Item $baseStl).Length -lt 1000) {
    throw "Base STL export failed"
}

Write-Host "Exporting lid STL..."
& $openscad -o $lidStl $scadLid
if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) {
    throw "OpenSCAD lid export exit code: $LASTEXITCODE"
}
if (-not (Test-Path -LiteralPath $lidStl) -or (Get-Item $lidStl).Length -lt 1000) {
    throw "Lid STL export failed"
}

Write-Host "Reading meshes..."
$baseMesh = Read-Stl -Path $baseStl
$lidMesh = Read-Stl -Path $lidStl
Write-Host ("base tris={0} verts={1}" -f $baseMesh.Triangles.Count, $baseMesh.Vertices.Count)
Write-Host ("lid  tris={0} verts={1}" -f $lidMesh.Triangles.Count, $lidMesh.Vertices.Count)

Write-Host "Writing 3MF (side-by-side plate layout, 40 mm gap along X)..."
New-3mfFromMeshes -BaseMesh $baseMesh -LidMesh $lidMesh -OutputPath $out3mf -PlateGap 40.0

$fi = Get-Item -LiteralPath $out3mf
Write-Host ("3MF_OK path={0} len={1}" -f $fi.FullName, $fi.Length)

if ($OpenInOrca) {
    if (-not (Test-Path -LiteralPath $OrcaPath)) { throw "Orca not found: $OrcaPath" }
    Start-Process -FilePath $OrcaPath -ArgumentList @($out3mf)
    Write-Host "ORCA_OPENED $out3mf"
}
