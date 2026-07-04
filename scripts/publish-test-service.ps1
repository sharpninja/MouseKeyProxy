$out = Join-Path $env:TEMP 'mkp-service-publish-test'
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
Set-Location (Split-Path -Parent $PSScriptRoot)
dotnet publish src/MouseKeyProxy.Service/MouseKeyProxy.Service.csproj -c Release -o $out -r win-x64 --self-contained true -p:PublishSingleFile=true
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Stop-Service MouseKeyProxy -Force -ErrorAction SilentlyContinue
$p = Start-Process -FilePath (Join-Path $out 'MouseKeyProxy.Service.exe') -PassThru -WindowStyle Hidden
Start-Sleep 5
$listen = netstat -an | Select-String '50051.*LISTENING'
Write-Output "FreshPublish PID=$($p.Id) HasExited=$($p.HasExited) Listen=$listen"
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
Start-Service MouseKeyProxy -ErrorAction SilentlyContinue
if (-not $listen) { exit 1 }
exit 0