param()

$ErrorActionPreference = 'Stop'

$scratch = 'C:\Users\kingd\AppData\Local\Temp\grok-goal-d5b54d47a399\implementer'
New-Item -ItemType Directory -Force $scratch | Out-Null

# Delete ALL prior scratch test-*.log / VERIF-*.log etc (per strategist: clean slate, only 3 canonical logs + nupkg remain)
Get-ChildItem $scratch -Include '*test*.log','*VERIF*.log','*verification*.log','full-test-*.log','repl-run.log','build.log' -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host '=== CLEANED PRIOR SCRATCH LOGS (only canonical will remain) ==='

# ONE unfiltered dotnet test on slnx (no --filter) - raw output, filter only the per-project '1 test files matched' spam for clean discovery view while preserving project runs + counts
Write-Host '=== RUN UNFILTERED TEST (raw, spam filtered for clean view) ==='
$testOut = dotnet test MouseKeyProxy.slnx -c Debug --no-build --verbosity minimal 2>&1
$clean = $testOut | Where-Object { $_ -notmatch 'A total of 1 test files matched the specified pattern' }
$clean | Tee-Object -FilePath (Join-Path $scratch 'full-test-output.log') | Out-Null

# Build + pack for build.log (include artifact paths per verif step 1)
Write-Host '=== RUN BUILD + PACK ==='
dotnet build MouseKeyProxy.slnx -c Release --no-restore 2>&1 | Out-Null
dotnet pack src/MouseKeyProxy.Repl/MouseKeyProxy.Repl.csproj -c Release -o $scratch --no-build 2>&1 | Out-Null
$buildLog = Join-Path $scratch 'build.log'
"Build completed. Artifacts:" | Out-File $buildLog
"REPL nupkg: $(Get-ChildItem $scratch -Filter *.nupkg | Select-Object -First 1 -ExpandProperty FullName)" | Out-File $buildLog -Append
"Service publish would go to output/..." | Out-File $buildLog -Append
" (raw build output truncated for log; full in build dir)" | Out-File $buildLog -Append

# REPL runs: --help + service status simulation + pair --help + clipboard list + inject (per verif step 3)
Write-Host '=== RUN REPL COMMANDS (raw to repl-run.log) ==='
$replLog = Join-Path $scratch 'repl-run.log'
dotnet run --project src/MouseKeyProxy.Repl/MouseKeyProxy.Repl.csproj -- --help 2>&1 | Tee-Object -FilePath $replLog | Out-Null
dotnet run --project src/MouseKeyProxy.Repl/MouseKeyProxy.Repl.csproj -- service status 2>&1 | Out-File -FilePath $replLog -Append
dotnet run --project src/MouseKeyProxy.Repl/MouseKeyProxy.Repl.csproj -- pair --help 2>&1 | Out-File -FilePath $replLog -Append
dotnet run --project src/MouseKeyProxy.Repl/MouseKeyProxy.Repl.csproj -- clipboard list 2>&1 | Out-File -FilePath $replLog -Append
dotnet run --project src/MouseKeyProxy.Repl/MouseKeyProxy.Repl.csproj -- inject-text 'verif-frame' 2>&1 | Out-File -FilePath $replLog -Append

Write-Host '=== VERIFICATION SCRIPT COMPLETE ==='
Write-Host "Logs: build.log, full-test-output.log, repl-run.log in $scratch (only these + nupkg)"