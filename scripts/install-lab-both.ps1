param([string]$RepoRoot = 'F:\GitHub\MouseKeyProxy')

$ErrorActionPreference = 'Stop'
$install = Join-Path $RepoRoot 'scripts' 'install-lab-service.ps1'

Write-Host "=== LOCAL: $env:COMPUTERNAME ==="
& $install
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$remote = if ($env:COMPUTERNAME -ieq 'PAYTON-LEGION2') { 'payton-desktop' } else { 'payton-legion2' }
Write-Host "=== REMOTE: $remote (WinRM) ==="
try {
    Invoke-Command -ComputerName $remote -ScriptBlock {
        param($InstallPath)
        & pwsh -NoProfile -ExecutionPolicy Bypass -File $InstallPath
    } -ArgumentList $install -ErrorAction Stop
}
catch {
    Write-Error "Remote install on $remote failed: $($_.Exception.Message)"
    exit 1
}

Write-Host '=== LAB BOTH INSTALLED ==='
exit 0