$exe = 'C:\ProgramData\MouseKeyProxy\MouseKeyProxy.Service.exe'
$log = Join-Path $env:TEMP 'mkp-service-crash.log'
Stop-Service MouseKeyProxy -Force -ErrorAction SilentlyContinue
Start-Sleep 1
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$p = [System.Diagnostics.Process]::Start($psi)
$stdout = $p.StandardOutput.ReadToEnd()
$stderr = $p.StandardError.ReadToEnd()
$p.WaitForExit(8000)
@(
    "ExitCode=$($p.ExitCode)",
    "--- stdout ---",
    $stdout,
    "--- stderr ---",
    $stderr
) | Set-Content -Path $log -Encoding utf8
Get-Content $log