<#
.SYNOPSIS
  Fetches a live diagnostics snapshot from a running LogiSteam (in its Wheel PC or Game PC role)
  instance over the LAN (see RemoteDiagnosticsServer). Read-only -- this cannot change any state
  on the target machine, it only fetches whatever that role's BuildDiagnosticsSnapshotJson
  currently reports.

.EXAMPLE
  .\query-diagnostics.ps1 -TargetIp 192.168.1.133 -Side WheelSide
.EXAMPLE
  .\query-diagnostics.ps1 -TargetIp 192.168.1.100 -Side GameSide
#>
param(
    [Parameter(Mandatory)][string]$TargetIp,
    [Parameter(Mandatory)][ValidateSet("WheelSide", "GameSide")][string]$Side,
    [int]$TimeoutMs = 5000
)

$port = if ($Side -eq "WheelSide") { 35896 } else { 35897 }

$client = New-Object System.Net.Sockets.TcpClient
$client.ReceiveTimeout = $TimeoutMs
try {
    $client.Connect($TargetIp, $port)
    $stream = $client.GetStream()
    $ms = New-Object System.IO.MemoryStream
    $buffer = New-Object byte[] 65536
    while ($true) {
        $read = $stream.Read($buffer, 0, $buffer.Length)
        if ($read -le 0) { break }
        $ms.Write($buffer, 0, $read)
    }
    $json = [System.Text.Encoding]::UTF8.GetString($ms.ToArray())
    $json | ConvertFrom-Json | ConvertTo-Json -Depth 6
}
catch {
    Write-Error "Could not reach $Side diagnostics at $TargetIp`:$port -- $($_.Exception.Message)"
    exit 1
}
finally {
    $client.Close()
}
