param(
    [string]$ReceiptPath = '',
    [switch]$SkipBuildStyleChecks
)

$ErrorActionPreference = 'Continue'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($ReceiptPath)) {
    $stamp = Get-Date -Format 'yyyyMMddTHHmmssZ'
    $ReceiptPath = Join-Path $repoRoot "docs\receipts-plan-readiness-$stamp.txt"
}

$receiptDir = Split-Path -Parent $ReceiptPath
if ($receiptDir -and -not (Test-Path -LiteralPath $receiptDir)) {
    $null = New-Item -ItemType Directory -Path $receiptDir -Force
}

$script:failed = 0

function Write-Receipt([string]$Line) {
    Write-Host $Line
    $Line | Out-File -LiteralPath $ReceiptPath -Append -Encoding utf8
}

function Invoke-Gate {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Command,
        [string]$Expected = 'PASS'
    )

    Write-Receipt "=== GATE: $Name ==="
    $output = & $Command 2>&1
    $exit = if ($null -ne $global:LASTEXITCODE) { $global:LASTEXITCODE } else { 0 }
    foreach ($line in $output) { Write-Receipt ([string]$line) }

    if ($exit -eq 0) {
        Write-Receipt "GATE RESULT: PASS $Name"
        if ($Expected -eq 'FAIL') {
            Write-Receipt "GATE EXPECTATION: unexpected pass; this gate was expected to remain red until its external proof is supplied"
        }
    }
    else {
        Write-Receipt "GATE RESULT: FAIL $Name exit=$exit"
        $script:failed++
    }
    Write-Receipt ''
}

"=== MouseKeyProxy Plan Readiness ===" | Out-File -LiteralPath $ReceiptPath -Encoding utf8
Write-Receipt "Date: $(Get-Date -Format o)"
Write-Receipt "Repo: $repoRoot"
Write-Receipt "Machine: $env:COMPUTERNAME"
Write-Receipt ''

Invoke-Gate -Name 'Moq ban compliance' -Command {
    dotnet test 'tests\MouseKeyProxy.Compliance.Tests\MouseKeyProxy.Compliance.Tests.csproj' --no-restore -v minimal --filter 'FullyQualifiedName~TestDoubleComplianceTests'
}

Invoke-Gate -Name 'UI and hacker mouse branding compliance' -Command {
    dotnet test 'tests\MouseKeyProxy.Compliance.Tests\MouseKeyProxy.Compliance.Tests.csproj' --no-restore -v minimal --filter 'FullyQualifiedName~AgentUiBrandingComplianceTests'
}

Invoke-Gate -Name 'Advanced gRPC control service seam' -Command {
    dotnet test 'tests\MouseKeyProxy.Service.Tests\MouseKeyProxy.Service.Tests.csproj' --no-restore -v minimal --filter 'FullyQualifiedName~AdvancedControlServiceTests'
}

Invoke-Gate -Name 'Paired-control proof receipt' -Command {
    $receipt = Join-Path $repoRoot 'docs\receipts-transition-e2e.txt'
    pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot 'scripts\assert-paired-control-proof.ps1') -ReceiptPath $receipt -LocalHost 'payton-legion2' -RemoteHost 'payton-desktop' -RequireSmokePass
}

if (-not $SkipBuildStyleChecks) {
    Invoke-Gate -Name 'Git diff whitespace check' -Command {
        git diff --check
    }
}

Write-Receipt '=== SUMMARY ==='
if ($script:failed -eq 0) {
    Write-Receipt 'PLAN READINESS: PASS'
}
else {
    Write-Receipt "PLAN READINESS: FAIL red_gates=$script:failed"
    Write-Receipt 'Remaining completion gates must be green before the plan can be considered complete.'
}
Write-Receipt "Receipt: $ReceiptPath"

if ($script:failed -gt 0) {
    $host.SetShouldExit(1)
}
