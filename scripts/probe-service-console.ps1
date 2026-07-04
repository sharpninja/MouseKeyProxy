$exe = 'C:\ProgramData\MouseKeyProxy\MouseKeyProxy.Service.exe'
$log = Join-Path $env:TEMP 'mkp-service-console.log'
if (Get-Service MouseKeyProxy -ErrorAction SilentlyContinue) {
    Stop-Service MouseKeyProxy -Force -ErrorAction SilentlyContinue
    Start-Sleep 2
}
$p = Start-Process -FilePath $exe -RedirectStandardOutput $log -RedirectStandardError $log -PassThru -WindowStyle Hidden
Start-Sleep 5
$listen = netstat -an | Select-String '50051.*LISTENING'
"PID=$($p.Id) HasExited=$($p.HasExited)" | Out-File $log -Append -Encoding utf8
"LISTEN=$listen" | Out-File $log -Append -Encoding utf8
Get-Content $log
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }