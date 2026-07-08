#Requires -Version 7.0
# FR-MKP-011 / TEST-MKP-017 / TEST-MKP-018: runs the PowerShell (Pester) tests over the operator
# tooling scripts and returns a non-zero exit code on any failure, so the Nuke TestPowerShell target
# and CI fail the build. Pester 5 is installed for the current user if it is not already present.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

if (-not (Get-Module -ListAvailable Pester | Where-Object { $_.Version -ge [version]'5.0.0' })) {
    Write-Host 'Pester 5 not found; installing for CurrentUser...'
    Install-Module Pester -MinimumVersion 5.0.0 -Force -Scope CurrentUser -SkipPublisherCheck
}

Import-Module Pester -MinimumVersion 5.0.0 -Force

$result = Invoke-Pester -Path $PSScriptRoot -Output Detailed -PassThru
if ($result.FailedCount -gt 0) {
    Write-Error "Pester failed: $($result.FailedCount) failed of $($result.TotalCount)."
    exit 1
}

Write-Host "Pester OK: $($result.PassedCount) passed, 0 failed."
exit 0
