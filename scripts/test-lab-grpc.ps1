param([string[]]$Hosts = @('payton-legion2', 'payton-desktop'), [int]$Port = 50051)
foreach ($h in $Hosts) {
    $c = New-Object Net.Sockets.TcpClient
    try {
        $c.Connect($h, $Port)
        Write-Output "$h`:$Port REACHABLE"
    }
    catch {
        Write-Output "$h`:$Port UNREACHABLE ($($_.Exception.Message))"
    }
    finally {
        $c.Close()
    }
}