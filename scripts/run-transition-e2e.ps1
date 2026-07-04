param(
    [string]$ReceiptPath = '',
    [string]$LocalHost = '',
    [string]$RemoteHost = '',
    [string]$RemoteCredentialPath = $(if ($env:MKP_REMOTE_CREDENTIAL_PATH) { $env:MKP_REMOTE_CREDENTIAL_PATH } else { '' }),
    [int]$GrpcPort = $(if ($env:MKP_GRPC_PORT) { [int]$env:MKP_GRPC_PORT } else { 50051 })
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Resolve-LabPeers {
    $machine = $env:COMPUTERNAME
    if ($machine -ieq 'PAYTON-LEGION2' -or $machine -ieq 'payton-legion2') {
        return @{ Local = 'payton-legion2'; Remote = 'payton-desktop' }
    }
    if ($machine -ieq 'PAYTON-DESKTOP' -or $machine -ieq 'payton-desktop') {
        return @{ Local = 'payton-desktop'; Remote = 'payton-legion2' }
    }
    throw "Machine '$machine' is not a configured lab peer. Expected payton-legion2 or payton-desktop."
}

$peers = Resolve-LabPeers
if ([string]::IsNullOrWhiteSpace($LocalHost)) { $LocalHost = $peers.Local }
if ([string]::IsNullOrWhiteSpace($RemoteHost)) { $RemoteHost = $peers.Remote }

if ([string]::IsNullOrWhiteSpace($ReceiptPath)) {
    $ReceiptPath = Join-Path $repoRoot 'docs' 'receipts-transition-e2e.txt'
}

function Write-ReceiptLine([string]$Line) {
    Write-Host $Line
    $Line | Out-File -FilePath $ReceiptPath -Append -Encoding utf8
}

function Test-GrpcTcp([string]$HostName, [int]$Port) {
    $tcp = New-Object System.Net.Sockets.TcpClient
    $tcp.ReceiveTimeout = 5000
    $tcp.SendTimeout = 5000
    $tcp.Connect($HostName, $Port)
    $tcp.Close()
}

"=== PLAN-MKP-006 Transition two-machine E2E (lab) ===" | Out-File -FilePath $ReceiptPath -Encoding utf8
Write-ReceiptLine "Date: $(Get-Date -Format o)"
Write-ReceiptLine "MachineName: $env:COMPUTERNAME"
Write-ReceiptLine "LocalPeer: $LocalHost"
Write-ReceiptLine "RemotePeer: $RemoteHost"
Write-ReceiptLine "GrpcPort: $GrpcPort"

$mkpExe = $null
$toolDir = Join-Path $env:TEMP 'mkp-e2e-tool'
if (Test-Path (Join-Path $toolDir 'mkp.exe')) {
    $mkpExe = Join-Path $toolDir 'mkp.exe'
}
else {
    $scratch = Join-Path $env:TEMP 'mkp-e2e-pack'
    New-Item -ItemType Directory -Force -Path $scratch | Out-Null
    dotnet pack src/MouseKeyProxy.Repl/MouseKeyProxy.Repl.csproj -c Release -o $scratch /p:PackageVersion=0.5.0 2>&1 | Out-Null
    New-Item -ItemType Directory -Force -Path $toolDir | Out-Null
    dotnet tool install MouseKeyProxy.Repl --tool-path $toolDir --add-source $scratch 2>&1 | Out-Null
    $mkpExe = Join-Path $toolDir 'mkp.exe'
}

if (-not (Test-Path $mkpExe)) {
    throw "mkp.exe not found at $mkpExe"
}

$mkpLiteral = $mkpExe.Replace("'", "''")
$elevScript = Join-Path $env:TEMP 'mkp-e2e-elev.ps1'
@"
`$ErrorActionPreference = 'Continue'
`$env:MKP_GRPC = 'http://${LocalHost}:$GrpcPort'
`$exitCode = 0
Write-Output "=== MKP service status (local $LocalHost) ==="
& '$mkpLiteral' service status 2>&1
if (`$LASTEXITCODE -ne 0) { `$exitCode = `$LASTEXITCODE }
Write-Output '=== MKP pair local (valid-test) ==='
& '$mkpLiteral' pair valid-test 2>&1
if (`$LASTEXITCODE -ne 0) { `$exitCode = `$LASTEXITCODE }
Write-Output '=== MKP toggle ==='
& '$mkpLiteral' toggle 2>&1
if (`$LASTEXITCODE -ne 0) { `$exitCode = `$LASTEXITCODE }
exit `$exitCode
"@ | Set-Content -Path $elevScript -Encoding utf8

$localSmokeOk = $false
Write-ReceiptLine '=== LOCAL ELEVATED SMOKE (single gsudo batch; client fallback if elevation canceled) ==='
if (Get-Command gsudo -ErrorAction SilentlyContinue) {
    gsudo --wait pwsh -ExecutionPolicy Bypass -File $elevScript 2>&1 | ForEach-Object { Write-ReceiptLine $_ }
    if ($LASTEXITCODE -eq 0) {
        $localSmokeOk = $true
    }
    else {
        Write-ReceiptLine "LOCAL gsudo batch exit: $LASTEXITCODE"
        Write-ReceiptLine '=== LOCAL CLIENT SMOKE FALLBACK (non-elevated) ==='
        pwsh -ExecutionPolicy Bypass -File $elevScript 2>&1 | ForEach-Object { Write-ReceiptLine $_ }
        $localSmokeOk = ($LASTEXITCODE -eq 0)
    }
}
else {
    pwsh -ExecutionPolicy Bypass -File $elevScript 2>&1 | ForEach-Object { Write-ReceiptLine $_ }
    $localSmokeOk = ($LASTEXITCODE -eq 0)
}
Remove-Item -Path $elevScript -Force -ErrorAction SilentlyContinue
if (-not $localSmokeOk) {
    Write-ReceiptLine 'SMOKE: FAIL (local REPL smoke failed)'
    exit 1
}

Write-ReceiptLine '=== LOCAL GRPC PROBE ==='
try {
    Test-GrpcTcp -HostName $LocalHost -Port $GrpcPort
    Write-ReceiptLine "LOCAL gRPC TCP: REACHABLE $LocalHost`:$GrpcPort"
}
catch {
    Write-ReceiptLine "LOCAL gRPC TCP: UNREACHABLE $LocalHost`:$GrpcPort ($($_.Exception.Message))"
    Write-ReceiptLine 'SMOKE: FAIL'
    exit 1
}

Write-ReceiptLine '=== REMOTE GRPC PROBE ==='
try {
    Test-GrpcTcp -HostName $RemoteHost -Port $GrpcPort
    Write-ReceiptLine "REMOTE gRPC TCP: REACHABLE $RemoteHost`:$GrpcPort"
}
catch {
    Write-ReceiptLine "REMOTE gRPC TCP: UNREACHABLE $RemoteHost`:$GrpcPort ($($_.Exception.Message))"
    Write-ReceiptLine 'SMOKE: FAIL'
    exit 1
}

Write-ReceiptLine "=== REMOTE REPL SMOKE (WinRM $RemoteHost) ==="
$remoteOk = $false
try {
    $invokeParams = @{
        ComputerName = $RemoteHost
        ArgumentList = @($RemoteHost, $GrpcPort)
        ErrorAction = 'Stop'
        ScriptBlock = {
            param($RemoteGrpcHost, $RemoteGrpcPort)
            $ErrorActionPreference = 'Stop'
            $repoRoot = 'F:\GitHub\MouseKeyProxy'
            Set-Location $repoRoot

            $toolDir = Join-Path $env:TEMP 'mkp-lab-install\tool'
            $mkpExe = Join-Path $toolDir 'mkp.exe'
            if (-not (Test-Path $mkpExe)) {
                $scratch = Join-Path $env:TEMP 'mkp-e2e-pack'
                if (Test-Path $scratch) { Remove-Item -Path $scratch -Recurse -Force }
                New-Item -ItemType Directory -Force -Path $scratch | Out-Null
                dotnet pack src/MouseKeyProxy.Repl/MouseKeyProxy.Repl.csproj -c Release -o $scratch /p:PackageVersion=0.5.0 2>&1 | Out-Null
                $toolDir = Join-Path $env:TEMP 'mkp-e2e-tool'
                if (Test-Path $toolDir) { Remove-Item -Path $toolDir -Recurse -Force }
                New-Item -ItemType Directory -Force -Path $toolDir | Out-Null
                dotnet tool install MouseKeyProxy.Repl --tool-path $toolDir --add-source $scratch 2>&1 | Out-Null
                $mkpExe = Join-Path $toolDir 'mkp.exe'
            }

            if (-not (Test-Path $mkpExe)) {
                throw "mkp.exe not found on remote at $mkpExe"
            }

            $env:MKP_GRPC = "http://${RemoteGrpcHost}:$RemoteGrpcPort"
            Write-Output "=== REMOTE pair via $env:MKP_GRPC ==="
            & $mkpExe pair valid-test 2>&1
            if ($LASTEXITCODE -ne 0) { throw "remote pair failed exit $LASTEXITCODE" }
            Write-Output '=== REMOTE toggle ==='
            & $mkpExe toggle 2>&1
            if ($LASTEXITCODE -ne 0) { throw "remote toggle failed exit $LASTEXITCODE" }
        }
    }

    if ([string]::IsNullOrWhiteSpace($RemoteCredentialPath)) {
        $candidate = Join-Path $HOME '.desktopcred.xml'
        if (Test-Path $candidate) { $RemoteCredentialPath = $candidate }
    }
    if (-not [string]::IsNullOrWhiteSpace($RemoteCredentialPath)) {
        $invokeParams.Credential = Import-Clixml -LiteralPath $RemoteCredentialPath
        Write-ReceiptLine "REMOTE WinRM credential: $RemoteCredentialPath"
    }

    Invoke-Command @invokeParams 2>&1 | ForEach-Object { Write-ReceiptLine $_ }
    Write-ReceiptLine 'REMOTE WinRM: PASS'
    $remoteOk = $true
}
catch {
    Write-ReceiptLine "REMOTE WinRM: FAIL ($($_.Exception.Message))"
}

if (-not $remoteOk) {
    Write-ReceiptLine 'SMOKE: FAIL (remote WinRM or REPL smoke required)'
    exit 1
}

Write-ReceiptLine 'SMOKE: PASS (payton-legion2 + payton-desktop lab)'
exit 0
