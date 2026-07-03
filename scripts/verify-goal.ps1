param()

$ErrorActionPreference = 'Stop'

$scratch = 'C:\Users\kingd\AppData\Local\Temp\grok-goal-8dcf4780924b\implementer'
New-Item -ItemType Directory -Force $scratch | Out-Null

# Delete all prior scratch test-*.log / VERIF-*.log etc as per strategist
Get-ChildItem $scratch -Filter 'test-*.log' -File -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem $scratch -Filter 'VERIF*.log' -File -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem $scratch -Filter '*verification*.log' -File -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem $scratch -Filter 'full-test-*.log' -File -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem $scratch -Filter 'repl-run.log' -File -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host '=== CLEANED PRIOR SCRATCH LOGS ==='

# Unfiltered dotnet test on slnx (no --filter)
Write-Host '=== RUN UNFILTERED TEST ==='
dotnet test MouseKeyProxy.slnx -c Debug --no-build --verbosity minimal 2>&1 | Tee-Object -FilePath (Join-Path $scratch 'full-test-output.log')

# Build for build.log
Write-Host '=== RUN BUILD ==='
dotnet build MouseKeyProxy.slnx -c Release --no-restore 2>&1 | Tee-Object -FilePath (Join-Path $scratch 'build.log') | Out-Null

# REPL run: mkp --help + one inject command
# Use dotnet run since not global installed in this env; capture to repl-run.log
Write-Host '=== RUN REPL --help + inject ==='
$replLog = Join-Path $scratch 'repl-run.log'
dotnet run --project src/MouseKeyProxy.Repl/MouseKeyProxy.Repl.csproj -- --help 2>&1 | Tee-Object -FilePath $replLog | Out-Null
dotnet run --project src/MouseKeyProxy.Repl/MouseKeyProxy.Repl.csproj -- inject-text 'verif-frame' 2>&1 | Out-File -FilePath $replLog -Append

Write-Host '=== VERIFICATION SCRIPT COMPLETE ==='
Write-Host "Logs: build.log, full-test-output.log, repl-run.log in $scratch"