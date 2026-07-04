param(
    [string]$Scratch = (Join-Path $env:TEMP 'mkp-lab-install')
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

New-Item -ItemType Directory -Force -Path $Scratch | Out-Null
schtasks /End /TN 'MouseKeyProxyTray' 2>&1 | Out-Null
taskkill /IM MouseKeyProxy.Agent.exe /F 2>&1 | Out-Null
Start-Sleep -Seconds 2

Write-Host '=== PACK REPL (fresh payloads) ==='
dotnet pack src/MouseKeyProxy.Repl/MouseKeyProxy.Repl.csproj -c Release -o $Scratch /p:PackageVersion=0.5.0
if ($LASTEXITCODE -ne 0) { throw "pack failed exit $LASTEXITCODE" }

$toolDir = Join-Path $Scratch 'tool'
if (Test-Path $toolDir) { Remove-Item $toolDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $toolDir | Out-Null

Write-Host '=== TOOL INSTALL ==='
dotnet tool install MouseKeyProxy.Repl --tool-path $toolDir --add-source $Scratch
if ($LASTEXITCODE -ne 0) { throw "tool install failed exit $LASTEXITCODE" }

$mkpExe = Join-Path $toolDir 'mkp.exe'
if (-not (Test-Path $mkpExe)) { throw "mkp.exe missing at $mkpExe" }

$elevScript = Join-Path $Scratch 'mkp-lab-elev.ps1'
$mkpLiteral = $mkpExe.Replace("'", "''")
@"
`$ErrorActionPreference = 'Continue'
Write-Output '=== uninstall ==='
& '$mkpLiteral' service uninstall 2>&1
Write-Output '=== install ==='
& '$mkpLiteral' service install 2>&1
Write-Output '=== status ==='
& '$mkpLiteral' service status 2>&1
"@ | Set-Content -Path $elevScript -Encoding utf8

if (-not (Get-Command gsudo -ErrorAction SilentlyContinue)) {
    throw 'gsudo required for lab service install'
}

Write-Host '=== ELEVATED INSTALL (single gsudo batch) ==='
gsudo --wait pwsh -ExecutionPolicy Bypass -File $elevScript
if ($LASTEXITCODE -ne 0) { throw "elevated install failed exit $LASTEXITCODE" }

Remove-Item -Path $elevScript -Force -ErrorAction SilentlyContinue

$localPeer = if ($env:COMPUTERNAME -ieq 'PAYTON-LEGION2') { 'payton-legion2' } else { 'payton-desktop' }
$probe = New-Object Net.Sockets.TcpClient
try {
    $probe.Connect($localPeer, 50051)
    Write-Host "=== gRPC REACHABLE $localPeer`:50051 ==="
}
catch {
    throw "gRPC not reachable on $localPeer`:50051 after install: $($_.Exception.Message)"
}
finally {
    $probe.Close()
}

Write-Host "=== LAB SERVICE INSTALLED on $env:COMPUTERNAME ==="
exit 0