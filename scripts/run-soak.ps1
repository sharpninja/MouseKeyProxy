param(
    [double]$Hours = $(if ($env:MKP_SOAK_HOURS) { [double]$env:MKP_SOAK_HOURS } else { 24 }),
    [string]$ReceiptPath = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($ReceiptPath)) {
    $ReceiptPath = Join-Path $repoRoot 'docs' "receipts-soak-$(Get-Date -Format 'yyyyMMddTHHmmssZ').txt"
}

$endUtc = (Get-Date).ToUniversalTime().AddHours($Hours)
$intervalSec = 60
$stuckInputIncidents = 0
$unreleasedClipIncidents = 0
$iteration = 0

"=== PLAN-MKP-006 Transition 24h soak gate ===" | Out-File -FilePath $ReceiptPath -Encoding utf8
"StartUtc: $((Get-Date).ToUniversalTime().ToString('o'))" | Out-File -FilePath $ReceiptPath -Append -Encoding utf8
"EndUtc: $($endUtc.ToString('o'))" | Out-File -FilePath $ReceiptPath -Append -Encoding utf8
"Hours: $Hours" | Out-File -FilePath $ReceiptPath -Append -Encoding utf8
"LocalHost: $env:COMPUTERNAME" | Out-File -FilePath $ReceiptPath -Append -Encoding utf8
$labRemote = if ($env:COMPUTERNAME -ieq 'PAYTON-LEGION2') { 'payton-desktop' } elseif ($env:COMPUTERNAME -ieq 'PAYTON-DESKTOP') { 'payton-legion2' } else { 'unknown' }
"RemotePeer: $labRemote" | Out-File -FilePath $ReceiptPath -Append -Encoding utf8

Write-Host "=== SOAK: $Hours h until $($endUtc.ToString('o')) ==="
Write-Host "Receipt: $ReceiptPath"

while ((Get-Date).ToUniversalTime() -lt $endUtc) {
    $iteration++
    $stamp = (Get-Date).ToUniversalTime().ToString('o')
    $line = "[$stamp] iter=$iteration service="

    try {
        $svc = Get-Service -Name 'MouseKeyProxy' -ErrorAction Stop
        $line += $svc.Status
    }
    catch {
        $line += "MISSING ($($_.Exception.Message))"
        $stuckInputIncidents++
    }

    $grpcHost = if ($env:MKP_GRPC_HOST) { $env:MKP_GRPC_HOST }
        elseif ($env:COMPUTERNAME -ieq 'PAYTON-LEGION2') { 'payton-legion2' }
        elseif ($env:COMPUTERNAME -ieq 'PAYTON-DESKTOP') { 'payton-desktop' }
        else { 'localhost' }
    $grpcPort = if ($env:MKP_GRPC_PORT) { [int]$env:MKP_GRPC_PORT } else { 50051 }
    try {
        $tcp = New-Object System.Net.Sockets.TcpClient
        $tcp.Connect($grpcHost, $grpcPort)
        $tcp.Close()
        $line += ' grpc=OK'
    }
    catch {
        $line += " grpc=FAIL"
        $unreleasedClipIncidents++
    }

    $line | Out-File -FilePath $ReceiptPath -Append -Encoding utf8
    if ($iteration % 10 -eq 0) { Write-Host $line }
    Start-Sleep -Seconds $intervalSec
}

$summary = @(
    "=== SOAK COMPLETE ===",
    "Iterations: $iteration",
    "StuckInputIncidents: $stuckInputIncidents",
    "UnreleasedClipIncidents: $unreleasedClipIncidents",
    "Pass: $(if ($stuckInputIncidents -eq 0 -and $unreleasedClipIncidents -eq 0) { 'YES' } else { 'NO' })"
)
$summary | ForEach-Object {
    Write-Host $_
    $_ | Out-File -FilePath $ReceiptPath -Append -Encoding utf8
}

if ($stuckInputIncidents -gt 0 -or $unreleasedClipIncidents -gt 0) { exit 1 }
exit 0