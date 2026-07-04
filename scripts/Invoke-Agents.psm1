<#
.SYNOPSIS
    Encapsulates Codex and Claude invocation for the MouseKeyProxy workspace.

.DESCRIPTION
    Provides Invoke-Codex and Invoke-Claude wrappers for the delegated workflow:
    Codex owns design, test planning, review, and validation receipts; Claude owns
    product implementation code. The wrappers intentionally print the parameters
    and the complete executable/argument vector before launch, then let agent
    output flow through Tee-Object without suppressing it.

    Typical use:
        cd F:\GitHub\MouseKeyProxy
        Import-Module .\scripts\Invoke-Agents.psm1 -Force
        Invoke-Codex -PromptFile .\prompts\transition-lab-e2e-codex.md -DryRun
        Invoke-Claude -PromptFile .\prompts\implementation-slice.md
#>

$script:DefaultProductWorkspace = 'F:\GitHub\MouseKeyProxy'
$script:DefaultCodexExe = 'C:\Users\kingd\AppData\Local\Microsoft\WinGet\Links\codex.exe'
$script:DefaultClaudeExe = 'C:\Users\kingd\AppData\Local\Microsoft\WinGet\Packages\Anthropic.ClaudeCode_Microsoft.Winget.Source_8wekyb3d8bbwe\claude.exe'

function Resolve-AgentPromptFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$PromptFile,

        [Parameter(Mandatory = $true)]
        [string]$Workspace
    )

    $candidate = if ([System.IO.Path]::IsPathRooted($PromptFile)) {
        $PromptFile
    }
    else {
        Join-Path $Workspace $PromptFile
    }

    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Prompt file not found: $candidate"
    }

    (Resolve-Path -LiteralPath $candidate).Path
}

function Resolve-AgentLogFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$AgentName,

        [string]$LogFile,

        [Parameter(Mandatory = $true)]
        [string]$Workspace
    )

    if ([string]::IsNullOrWhiteSpace($LogFile)) {
        $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $LogFile = Join-Path $Workspace ("{0}-{1}.log" -f $AgentName.ToLowerInvariant(), $timestamp)
    }
    elseif (-not [System.IO.Path]::IsPathRooted($LogFile)) {
        $LogFile = Join-Path $Workspace $LogFile
    }

    $parent = Split-Path -Parent $LogFile
    if ($parent -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent | Out-Null
    }

    $LogFile
}

function Format-AgentArgument {
    [CmdletBinding()]
    param([AllowNull()][string]$Value)

    if ($null -eq $Value) {
        return '""'
    }

    '"{0}"' -f ($Value -replace '"', '\"')
}

function Write-AgentInvocationSummary {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$AgentName,

        [Parameter(Mandatory = $true)]
        [string]$Executable,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$Workspace,

        [Parameter(Mandatory = $true)]
        [string]$PromptPath,

        [Parameter(Mandatory = $true)]
        [string]$LogFile,

        [Parameter(Mandatory = $true)]
        [string]$PromptText,

        [Parameter(Mandatory = $true)]
        [bool]$DryRun
    )

    $promptBytes = [System.Text.Encoding]::UTF8.GetBytes($PromptText)
    $promptHash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($promptBytes)).ToLowerInvariant()
    $signature = @('&', (Format-AgentArgument $Executable)) + ($Arguments | ForEach-Object { Format-AgentArgument $_ })

    Write-Host "[$AgentName] Parameter summary"
    Write-Host "  Workspace: $Workspace"
    Write-Host "  PromptFile: $PromptPath"
    Write-Host "  PromptCharacters: $($PromptText.Length)"
    Write-Host "  PromptUtf8Bytes: $($promptBytes.Length)"
    Write-Host "  PromptSha256: $promptHash"
    Write-Host "  LogFile: $LogFile"
    Write-Host "  Executable: $Executable"
    Write-Host "  DryRun: $DryRun"
    Write-Host "[$AgentName] Complete agent call signature:"
    Write-Host ($signature -join ' ')
    Write-Host "[$AgentName] Complete argument vector:"
    for ($i = 0; $i -lt $Arguments.Count; $i++) {
        Write-Host ("  Arg[{0}]: {1}" -f $i, $Arguments[$i])
    }
}

function Invoke-AgentProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$AgentName,

        [Parameter(Mandatory = $true)]
        [string]$Executable,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$Workspace,

        [Parameter(Mandatory = $true)]
        [string]$PromptPath,

        [Parameter(Mandatory = $true)]
        [string]$LogFile,

        [Parameter(Mandatory = $true)]
        [string]$PromptText,

        [switch]$DryRun
    )

    Write-AgentInvocationSummary -AgentName $AgentName -Executable $Executable -Arguments $Arguments -Workspace $Workspace -PromptPath $PromptPath -LogFile $LogFile -PromptText $PromptText -DryRun:$DryRun.IsPresent

    if ($DryRun) {
        Write-Host "[$AgentName] DryRun requested; agent process was not started."
        return
    }

    if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) {
        throw "$AgentName executable not found: $Executable"
    }

    Push-Location $Workspace
    try {
        Write-Host "[$AgentName] Starting agent. Output is streamed and logged to $LogFile"
        & $Executable @Arguments 2>&1 | Tee-Object -FilePath $LogFile
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            throw "$AgentName exited with code $exitCode"
        }
        Write-Host "[$AgentName] Agent execution complete."
    }
    finally {
        Pop-Location
    }
}

function Invoke-Codex {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [string]$PromptFile,

        [string]$Workspace = $script:DefaultProductWorkspace,

        [string]$LogFile,

        [string]$CodexExe = $script:DefaultCodexExe,

        [switch]$DryRun
    )

    $workspacePath = (Resolve-Path -LiteralPath $Workspace).Path
    $promptPath = Resolve-AgentPromptFile -PromptFile $PromptFile -Workspace $workspacePath
    $promptText = Get-Content -LiteralPath $promptPath -Raw
    $resolvedLog = Resolve-AgentLogFile -AgentName 'Codex' -LogFile $LogFile -Workspace $workspacePath
    $arguments = @('exec', '--dangerously-bypass-approvals-and-sandbox', $promptText)

    Invoke-AgentProcess -AgentName 'Codex' -Executable $CodexExe -Arguments $arguments -Workspace $workspacePath -PromptPath $promptPath -LogFile $resolvedLog -PromptText $promptText -DryRun:$DryRun.IsPresent
}

function Invoke-Claude {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [string]$PromptFile,

        [string]$Workspace = $script:DefaultProductWorkspace,

        [string]$LogFile,

        [string]$ClaudeExe = $script:DefaultClaudeExe,

        [string]$Model = 'opus',

        [string]$Effort = 'max',

        [switch]$DryRun
    )

    $workspacePath = (Resolve-Path -LiteralPath $Workspace).Path
    $promptPath = Resolve-AgentPromptFile -PromptFile $PromptFile -Workspace $workspacePath
    $promptText = Get-Content -LiteralPath $promptPath -Raw
    $resolvedLog = Resolve-AgentLogFile -AgentName 'Claude' -LogFile $LogFile -Workspace $workspacePath
    $arguments = @('--model', $Model, '--effort', $Effort, '--dangerously-skip-permissions', '--permission-mode', 'bypassPermissions', '--print', $promptText)

    Invoke-AgentProcess -AgentName 'Claude' -Executable $ClaudeExe -Arguments $arguments -Workspace $workspacePath -PromptPath $promptPath -LogFile $resolvedLog -PromptText $promptText -DryRun:$DryRun.IsPresent
}

function Invoke-MouseKeyProxyDelegation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('Codex', 'Claude')]
        [string]$Agent,

        [Parameter(Mandatory = $true)]
        [string]$PromptFile,

        [string]$Workspace = $script:DefaultProductWorkspace,

        [string]$LogFile,

        [switch]$DryRun
    )

    switch ($Agent) {
        'Codex' { Invoke-Codex -PromptFile $PromptFile -Workspace $Workspace -LogFile $LogFile -DryRun:$DryRun.IsPresent }
        'Claude' { Invoke-Claude -PromptFile $PromptFile -Workspace $Workspace -LogFile $LogFile -DryRun:$DryRun.IsPresent }
    }
}

Export-ModuleMember -Function Invoke-Codex, Invoke-Claude, Invoke-MouseKeyProxyDelegation

Write-Host 'MouseKeyProxy agent invocation cmdlets loaded. Use Invoke-Codex, Invoke-Claude, or Invoke-MouseKeyProxyDelegation.' -ForegroundColor Green
