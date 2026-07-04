param(
    [string]$Scratch = $env:MKP_SCRATCH
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($Scratch)) {
    $Scratch = 'C:\Users\kingd\AppData\Local\Temp\grok-goal-517f749f32af\implementer'
}

if (-not (Test-Path $Scratch)) {
    New-Item -ItemType Directory -Force -Path $Scratch | Out-Null
}

Get-ChildItem -Path $Scratch -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^(test-|verif-)' -or ($_.Extension -eq '.log' -and $_.Name -notin @('git-visibility.log', 'build.log', 'full-test-output.log', 'repl-install.log', 'repl-run.log')) } |
    Remove-Item -Force -ErrorAction SilentlyContinue

foreach ($dirName in @('nupkg-inspect', 'mkp-tool', 'nupkg-check')) {
    $dirPath = Join-Path $Scratch $dirName
    if (Test-Path $dirPath) {
        Remove-Item -Path $dirPath -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$gitLog = Join-Path $Scratch 'git-visibility.log'
$buildLog = Join-Path $Scratch 'build.log'
$installLog = Join-Path $Scratch 'repl-install.log'
$replLog = Join-Path $Scratch 'repl-run.log'

Write-Host "=== SCRATCH: $Scratch ==="

Write-Host '=== GIT VISIBILITY (git-visibility.log) ==='
"=== GIT STATUS (src/) ===" | Out-File -FilePath $gitLog -Encoding utf8
git status --porcelain 'src/' 2>&1 | Out-File -FilePath $gitLog -Append -Encoding utf8
"=== GIT DIFF NAME-ONLY (src/) ===" | Out-File -FilePath $gitLog -Append -Encoding utf8
git diff --no-color --name-only 'src/' 2>&1 | Out-File -FilePath $gitLog -Append -Encoding utf8
"=== GIT DIFF (src/) ===" | Out-File -FilePath $gitLog -Append -Encoding utf8
git diff --no-color 'src/' 2>&1 | Out-File -FilePath $gitLog -Append -Encoding utf8

Write-Host '=== RESTORE + BUILD (build.log) ==='
"=== RESTORE + BUILD ===" | Out-File -FilePath $buildLog -Encoding utf8
dotnet restore MouseKeyProxy.slnx 2>&1 | Out-Null
dotnet build MouseKeyProxy.slnx -c Release 2>&1 | Tee-Object -FilePath $buildLog -Append | Out-Null

Write-Host '=== RUN UNFILTERED TEST ==='
$testOut = dotnet test MouseKeyProxy.slnx -c Release --no-build --verbosity minimal 2>&1
$clean = $testOut | Where-Object { $_ -notmatch 'A total of 1 test files matched the specified pattern' }
$clean | Tee-Object -FilePath (Join-Path $Scratch 'full-test-output.log') | Out-Null

Write-Host '=== CATEGORY FILTER TESTS (verification plan step 4) ==='
"=== CATEGORY FILTER TESTS ===" | Out-File -FilePath $buildLog -Append -Encoding utf8
foreach ($filter in @('Category=WireframeUI', 'Category=NukePayload', 'Category=MCPCompliance', 'Category=HarnessContract', 'Category=SecurityNegative', 'Category=ClipboardMerge')) {
    "=== FILTER: $filter ===" | Out-File -FilePath $buildLog -Append -Encoding utf8
    dotnet test MouseKeyProxy.slnx -c Release --no-build --filter $filter --verbosity minimal 2>&1 |
        Where-Object { $_ -notmatch 'A total of 1 test files matched the specified pattern' } |
        Out-File -FilePath $buildLog -Append -Encoding utf8
}

Write-Host '=== PACK REPL (build.log) ==='
"=== PACK REPL ===" | Out-File -FilePath $buildLog -Append -Encoding utf8
dotnet pack src/MouseKeyProxy.Repl/MouseKeyProxy.Repl.csproj -c Release -o $Scratch 2>&1 | Out-File -FilePath $buildLog -Append -Encoding utf8

Write-Host '=== DOTNET TOOL INSTALL + MKP SERVICE (repl-install.log) ==='
"=== DOTNET TOOL INSTALL ===" | Out-File -FilePath $installLog -Encoding utf8
$toolDir = Join-Path $Scratch 'mkp-tool'
New-Item -ItemType Directory -Force -Path $toolDir | Out-Null
$installOut = dotnet tool install MouseKeyProxy.Repl --tool-path $toolDir --add-source $Scratch 2>&1
$installOut | Out-File -FilePath $installLog -Append -Encoding utf8
$mkpExe = Join-Path $toolDir 'mkp.exe'
if (-not (Test-Path $mkpExe)) {
    throw "mkp.exe not found after dotnet tool install at $mkpExe"
}

if (Get-Command gsudo -ErrorAction SilentlyContinue) {
    "=== pre-clean tray agent and scheduled task ===" | Out-File -FilePath $installLog -Append -Encoding utf8
    schtasks /End /TN "MouseKeyProxyTray" 2>&1 | Out-File -FilePath $installLog -Append -Encoding utf8
    taskkill /IM MouseKeyProxy.Agent.exe /F 2>&1 | Out-File -FilePath $installLog -Append -Encoding utf8
    Start-Sleep -Seconds 2

    $elevScript = Join-Path $Scratch 'mkp-service-elev.ps1'
    $mkpExeLiteral = $mkpExe.Replace("'", "''")
    @"
`$ErrorActionPreference = 'Continue'
Write-Output '=== MKP service uninstall (gsudo pre-clean) ==='
& '$mkpExeLiteral' service uninstall 2>&1
Write-Output '=== MKP service install (gsudo production path) ==='
& '$mkpExeLiteral' service install 2>&1
Write-Output '=== MKP service status (gsudo post-install) ==='
& '$mkpExeLiteral' service status 2>&1
"@ | Set-Content -Path $elevScript -Encoding utf8

    "=== MKP service operations (single gsudo batch) ===" | Out-File -FilePath $installLog -Append -Encoding utf8
    gsudo --wait pwsh -ExecutionPolicy Bypass -File $elevScript 2>&1 |
        Out-File -FilePath $installLog -Append -Encoding utf8
    Remove-Item -Path $elevScript -Force -ErrorAction SilentlyContinue
}
else {
    "=== MKP service install (non-admin; expect elevation exit code) ===" | Out-File -FilePath $installLog -Append -Encoding utf8
    & $mkpExe service install 2>&1 | Out-File -FilePath $installLog -Append -Encoding utf8
    "=== MKP service status (non-admin) ===" | Out-File -FilePath $installLog -Append -Encoding utf8
    & $mkpExe service status 2>&1 | Out-File -FilePath $installLog -Append -Encoding utf8
    "=== install path validated by TEST-MKP-007 when gsudo unavailable ===" | Out-File -FilePath $installLog -Append -Encoding utf8
}

Write-Host '=== RUN REPL COMMANDS (repl-run.log) ==='
"=== DOTNET RUN REPL COMMANDS ===" | Out-File -FilePath $replLog -Encoding utf8
dotnet run --project src/MouseKeyProxy.Repl/MouseKeyProxy.Repl.csproj -c Release --no-build -- pair --help 2>&1 | Out-File -FilePath $replLog -Append -Encoding utf8
dotnet run --project src/MouseKeyProxy.Repl/MouseKeyProxy.Repl.csproj -c Release --no-build -- clipboard list 2>&1 | Out-File -FilePath $replLog -Append -Encoding utf8
dotnet run --project src/MouseKeyProxy.Repl/MouseKeyProxy.Repl.csproj -c Release --no-build -- inject-text 'verif-frame' 2>&1 | Out-File -FilePath $replLog -Append -Encoding utf8
dotnet run --project src/MouseKeyProxy.Repl/MouseKeyProxy.Repl.csproj -c Release --no-build -- toggle 2>&1 | Out-File -FilePath $replLog -Append -Encoding utf8

Write-Host '=== PAIRED CONTROL PROOF (paired-control-proof.log) ==='
$proofLog = Join-Path $Scratch 'paired-control-proof.log'
$transitionReceipt = Join-Path $repoRoot 'docs\receipts-transition-e2e.txt'
$proofScript = Join-Path $repoRoot 'scripts\assert-paired-control-proof.ps1'
& $proofScript -ReceiptPath $transitionReceipt -LocalHost 'payton-legion2' -RemoteHost 'payton-desktop' -RequireSmokePass *> $proofLog
$proofExit = $LASTEXITCODE
Get-Content -LiteralPath $proofLog | ForEach-Object { Write-Host $_ }
if ($proofExit -ne 0) {
    throw "paired-control proof failed; see $proofLog"
}

$canonical = @('git-visibility.log', 'build.log', 'full-test-output.log', 'repl-install.log', 'repl-run.log', 'paired-control-proof.log')
Get-ChildItem -Path $Scratch -File | ForEach-Object {
    if ($canonical -notcontains $_.Name -and $_.Extension -ne '.nupkg') {
        Remove-Item -Path $_.FullName -Force
    }
}
Get-ChildItem -Path $Scratch -Directory -ErrorAction SilentlyContinue | ForEach-Object {
    Remove-Item -Path $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
}

$files = Get-ChildItem -Path $Scratch -File | Select-Object -ExpandProperty Name
Write-Host "=== SCRATCH FILES: $($files -join ', ') ==="
Write-Host '=== VERIFICATION SCRIPT COMPLETE ==='
exit 0
