# Stops the Kestrel API and the Caddy reverse proxy started by start-servers.ps1.

Get-Process caddy -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process dotnet -ErrorAction SilentlyContinue |
    Where-Object { ($_ | Select-Object -ExpandProperty Path -ErrorAction SilentlyContinue) -and $_.MainModule.FileName -like "*dotnet*" } |
    ForEach-Object {
        try {
            $cmdline = (Get-CimInstance Win32_Process -Filter "ProcessId = $($_.Id)").CommandLine
            if ($cmdline -like "*MCS app.dll*") { Stop-Process -Id $_.Id -Force }
        } catch {}
    }

Write-Host "Stopped Caddy and the MCS API (if they were running)."
