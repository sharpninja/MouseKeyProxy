param(
    [Parameter(Mandatory = $true)]
    [string]$ReceiptPath,

    [string]$LocalHost = 'payton-legion2',

    [string]$RemoteHost = 'payton-desktop',

    [string]$Sentinel = '',

    [switch]$RequireSmokePass
)

$ErrorActionPreference = 'Stop'

function Fail-Proof([string]$Message) {
    Write-Error "PAIRED CONTROL PROOF: FAIL - $Message"
    exit 1
}

if (-not (Test-Path -LiteralPath $ReceiptPath -PathType Leaf)) {
    Fail-Proof "receipt not found: $ReceiptPath"
}

$text = Get-Content -LiteralPath $ReceiptPath -Raw

$forbiddenPatterns = @(
    'REMOTE:\s*SKIPPED',
    'SMOKE:\s*PARTIAL',
    'PAIRED_CONTROL:\s*SKIPPED',
    'CURSOR_CONTROL:\s*SKIPPED',
    'SENTINEL_INPUT:\s*SKIPPED'
)
foreach ($pattern in $forbiddenPatterns) {
    if ($text -match $pattern) {
        Fail-Proof "forbidden receipt marker matched: $pattern"
    }
}

$localPattern = [regex]::Escape($LocalHost)
$remotePattern = [regex]::Escape($RemoteHost)
$requiredPatterns = @(
    "PAIRING:\s*PASS.*$localPattern.*$remotePattern|PAIRING:\s*PASS.*$remotePattern.*$localPattern",
    "CURSOR_CONTROL:\s*PASS.*from=$localPattern.*to=$remotePattern",
    "SENTINEL_INPUT:\s*PASS.*from=$localPattern.*to=$remotePattern"
)

if ($Sentinel) {
    $requiredPatterns += "SENTINEL_INPUT:\s*PASS.*text=$([regex]::Escape($Sentinel))"
}

if ($RequireSmokePass) {
    $requiredPatterns += 'SMOKE:\s*PASS'
}

foreach ($pattern in $requiredPatterns) {
    if ($text -notmatch $pattern) {
        Fail-Proof "missing required receipt evidence pattern: $pattern"
    }
}

Write-Host "PAIRED CONTROL PROOF: PASS ($LocalHost -> $RemoteHost)"
exit 0
