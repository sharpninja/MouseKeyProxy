$exe = 'C:\ProgramData\MouseKeyProxy\MouseKeyProxy.Service.exe'
Stop-Service MouseKeyProxy -Force -ErrorAction SilentlyContinue
Start-Sleep 2
$p = Start-Process -FilePath $exe -PassThru -WindowStyle Hidden
Start-Sleep 6
$listen = netstat -an | Select-String '50051.*LISTENING'
Write-Output "DeployedExe PID=$($p.Id) HasExited=$($p.HasExited) Listen=$listen"
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
Start-Service MouseKeyProxy -ErrorAction SilentlyContinue
if (-not $listen) { exit 1 }
exit 0