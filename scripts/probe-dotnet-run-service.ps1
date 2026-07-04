Set-Location (Split-Path -Parent $PSScriptRoot)
dotnet build src/MouseKeyProxy.Service/MouseKeyProxy.Service.csproj -c Release | Out-Null
$p = Start-Process -FilePath 'dotnet' -ArgumentList @(
    'run', '--project', 'src/MouseKeyProxy.Service/MouseKeyProxy.Service.csproj', '-c', 'Release', '--no-build'
) -PassThru -WindowStyle Hidden
Start-Sleep 6
$listen = netstat -an | Select-String '50051.*LISTENING'
Write-Output "PID=$($p.Id) Listen=$listen"
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
if (-not $listen) { exit 1 }
exit 0