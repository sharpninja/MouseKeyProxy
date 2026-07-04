param(
    [string]$ReceiptPath = '',
    [string]$LocalHost = '',
    [string]$RemoteHost = '',
    [string]$RemoteCredentialPath = $(if ($env:MKP_REMOTE_CREDENTIAL_PATH) { $env:MKP_REMOTE_CREDENTIAL_PATH } else { '' }),
    [int]$GrpcPort = $(if ($env:MKP_GRPC_PORT) { [int]$env:MKP_GRPC_PORT } else { 50051 }),
    [string]$ControlSentinel = $(if ($env:MKP_CONTROL_SENTINEL) { $env:MKP_CONTROL_SENTINEL } else { 'MKP-CONTROL-PROOF' })
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

$toolDir = Join-Path $env:TEMP 'mkp-e2e-tool'
$scratch = Join-Path $env:TEMP 'mkp-e2e-pack'
if (Test-Path $scratch) { Remove-Item -Path $scratch -Recurse -Force }
if (Test-Path $toolDir) { Remove-Item -Path $toolDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
New-Item -ItemType Directory -Force -Path $toolDir | Out-Null
dotnet pack src/MouseKeyProxy.Repl/MouseKeyProxy.Repl.csproj -c Release -o $scratch /p:PackageVersion=0.5.0 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed exit $LASTEXITCODE" }
dotnet tool install MouseKeyProxy.Repl --tool-path $toolDir --add-source $scratch --version 0.5.0 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet tool install failed exit $LASTEXITCODE" }
$mkpExe = Join-Path $toolDir 'mkp.exe'

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

Write-ReceiptLine "PAIRING: PASS local=$LocalHost remote=$RemoteHost"
Write-ReceiptLine "=== LEGION2 TO DESKTOP CONTROL PROOF ==="
$env:MKP_GRPC = "http://${RemoteHost}:$GrpcPort"

try {
    Write-ReceiptLine "=== REMOTE INTERACTIVE NOTEPAD PREP (WinRM $RemoteHost) ==="
    $proofTask = 'MouseKeyProxyProofNotepad'
    $taskTime = (Get-Date).AddMinutes(2).ToString('HH:mm')
    $notepadParams = @{
        ComputerName = $RemoteHost
        ArgumentList = @($proofTask, $taskTime)
        ErrorAction = 'Stop'
        ScriptBlock = {
            param($TaskName, $TaskTime)
            schtasks.exe /Delete /TN $TaskName /F 2>&1 | Out-Null
            schtasks.exe /Create /TN $TaskName /TR 'notepad.exe' /SC ONCE /ST $TaskTime /IT /F 2>&1
            if ($LASTEXITCODE -ne 0) { throw "notepad proof task create failed exit $LASTEXITCODE" }
            schtasks.exe /Run /TN $TaskName 2>&1
            if ($LASTEXITCODE -ne 0) { throw "notepad proof task run failed exit $LASTEXITCODE" }
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($RemoteCredentialPath)) {
        $notepadParams.Credential = Import-Clixml -LiteralPath $RemoteCredentialPath
    }
    Invoke-Command @notepadParams 2>&1 | ForEach-Object { Write-ReceiptLine $_ }
}
catch {
    Write-ReceiptLine "REMOTE NOTEPAD PREP: WARN ($($_.Exception.Message))"
}

Start-Sleep -Seconds 2
$cursorX = 321
$cursorY = 234
& $mkpExe set-mouse --display primary --x $cursorX --y $cursorY 2>&1 | ForEach-Object { Write-ReceiptLine $_ }
if ($LASTEXITCODE -ne 0) {
    Write-ReceiptLine "CURSOR_CONTROL: FAIL from=$LocalHost to=$RemoteHost x=$cursorX y=$cursorY exit=$LASTEXITCODE"
    Write-ReceiptLine 'SMOKE: FAIL (cursor control command failed)'
    exit 1
}
Write-ReceiptLine "CURSOR_CONTROL: PASS from=$LocalHost to=$RemoteHost x=$cursorX y=$cursorY via=set-mouse"

$locateOutput = & $mkpExe locate-process notepad 2>&1
$locateOutput | ForEach-Object { Write-ReceiptLine $_ }
$hwnd = $null
foreach ($line in $locateOutput) {
    if ([string]$line -match 'HWND=(0x[0-9a-fA-F]+)') {
        $hwnd = $Matches[1]
        break
    }
}
if ($hwnd) {
    & $mkpExe focus-hwnd $hwnd 2>&1 | ForEach-Object { Write-ReceiptLine $_ }
    if ($LASTEXITCODE -ne 0) {
        Write-ReceiptLine "FOCUS: WARN hwnd=$hwnd exit=$LASTEXITCODE"
    }
}
else {
    Write-ReceiptLine 'FOCUS: WARN no notepad hwnd found; injecting into current foreground target'
}

& $mkpExe inject-text $ControlSentinel 2>&1 | ForEach-Object { Write-ReceiptLine $_ }
if ($LASTEXITCODE -ne 0) {
    Write-ReceiptLine "SENTINEL_INPUT: FAIL from=$LocalHost to=$RemoteHost text=$ControlSentinel exit=$LASTEXITCODE"
    Write-ReceiptLine 'SMOKE: FAIL (sentinel input command failed)'
    exit 1
}
Write-ReceiptLine "SENTINEL_INPUT: PASS from=$LocalHost to=$RemoteHost text=$ControlSentinel via=inject-text"

Write-ReceiptLine '=== PAIRED CONTROL PROOF GATE ==='
$proofScript = Join-Path $PSScriptRoot 'assert-paired-control-proof.ps1'
& $proofScript -ReceiptPath $ReceiptPath -LocalHost $LocalHost -RemoteHost $RemoteHost -Sentinel $ControlSentinel 2>&1 | ForEach-Object { Write-ReceiptLine $_ }
if ($LASTEXITCODE -ne 0) {
    Write-ReceiptLine 'SMOKE: FAIL (paired-control proof missing)'
    exit 1
}

Write-ReceiptLine 'SMOKE: PASS (payton-legion2 + payton-desktop lab with paired-control proof)'
exit 0
