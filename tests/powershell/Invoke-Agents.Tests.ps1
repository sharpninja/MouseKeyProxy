# TEST-MKP-017 / TEST-MKP-018 (FR-MKP-009, FR-MKP-010, TR-MKP-AGENTCMD-001)
# Pester tests over scripts/Invoke-Agents.psm1:
#  - TEST-MKP-017: dry-run writes the parameter summary + call signature + argument vector, honors the
#    workspace default and log-path handling, and launches no agent process.
#  - TEST-MKP-018: a controlled live invocation streams the agent's stdout to the host AND the log file
#    via Tee-Object, with no Out-Null suppression.

BeforeAll {
    $script:ModulePath = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' 'scripts' 'Invoke-Agents.psm1')).Path
    Import-Module $script:ModulePath -Force

    $script:TempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("mkp-agenttest-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $script:TempDir | Out-Null
    $script:PromptFile = Join-Path $script:TempDir 'prompt.md'
    Set-Content -LiteralPath $script:PromptFile -Value 'PROMPT_BODY'
    $script:MissingExe = Join-Path $script:TempDir 'does-not-exist.exe'
}

AfterAll {
    Remove-Module Invoke-Agents -Force -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force $script:TempDir -ErrorAction SilentlyContinue
}

Describe 'TEST-MKP-017: agent dry-run' {
    It 'writes the parameter summary (workspace, log, executable, DryRun) without launching a process' {
        $out = (Invoke-Claude -PromptFile $script:PromptFile -Workspace $script:TempDir -ClaudeExe $script:MissingExe -DryRun *>&1) | Out-String
        $out | Should -Match 'Workspace:'
        $out | Should -Match 'LogFile:'
        $out | Should -Match 'Executable:'
        $out | Should -Match 'DryRun: True'
        $out | Should -Match 'was not started'
        $out | Should -Not -Match 'executable not found'
    }

    It 'echoes the argument vector and the Model/Effort overrides' {
        $out = (Invoke-Claude -PromptFile $script:PromptFile -Workspace $script:TempDir -ClaudeExe $script:MissingExe -Model sonnet -Effort high -DryRun *>&1) | Out-String
        $out | Should -Match '--model'
        $out | Should -Match 'sonnet'
        $out | Should -Match '--effort'
        $out | Should -Match 'high'
        $out | Should -Match '--print'
    }

    It 'defaults the workspace to the product repo when not supplied' {
        $out = (Invoke-Claude -PromptFile $script:PromptFile -ClaudeExe $script:MissingExe -DryRun *>&1) | Out-String
        $out | Should -Match 'Workspace: F:\\GitHub\\MouseKeyProxy'
    }

    It 'auto-generates a per-agent log file path when none is supplied' {
        $out = (Invoke-Claude -PromptFile $script:PromptFile -Workspace $script:TempDir -ClaudeExe $script:MissingExe -DryRun *>&1) | Out-String
        $out | Should -Match 'LogFile:.*claude.*\.log'
    }

    It 'launches no process for Invoke-Codex dry-run either' {
        $out = (Invoke-Codex -PromptFile $script:PromptFile -Workspace $script:TempDir -CodexExe $script:MissingExe -DryRun *>&1) | Out-String
        $out | Should -Match 'was not started'
        $out | Should -Match 'exec'
    }
}

Describe 'TEST-MKP-018: controlled live invocation streams to host and log' {
    It 'flows agent stdout to both the host stream and the log file (no Out-Null suppression)' {
        $sentinel = 'MKP_LIVE_SENTINEL_' + [guid]::NewGuid().ToString('N')
        $fakeAgent = Join-Path $script:TempDir 'fake-claude.cmd'
        Set-Content -LiteralPath $fakeAgent -Value "@echo off`r`necho $sentinel"
        $logFile = Join-Path $script:TempDir 'live.log'

        $out = (Invoke-Claude -PromptFile $script:PromptFile -Workspace $script:TempDir -ClaudeExe $fakeAgent -LogFile $logFile *>&1) | Out-String

        # Streamed to the host (captured pipeline output).
        $out | Should -Match $sentinel
        # And teed to the log file.
        Test-Path -LiteralPath $logFile | Should -BeTrue
        (Get-Content -LiteralPath $logFile -Raw) | Should -Match $sentinel
    }
}
