# Starts the MCS Employee API on Kestrel and Caddy as a reverse proxy in front of it.
# Run from the repository root:  .\start-servers.ps1

$root = $PSScriptRoot
$apiDir = Join-Path $root "deploy\api"

# Resolve caddy.exe (installed via winget, user scope)
$caddy = (Get-Command caddy -ErrorAction SilentlyContinue).Source
if (-not $caddy) {
    $caddy = "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\CaddyServer.Caddy_Microsoft.Winget.Source_8wekyb3d8bbwe\caddy.exe"
}
if (-not (Test-Path $caddy)) {
    Write-Error "caddy.exe not found. Install it with: winget install CaddyServer.Caddy --scope user"
    exit 1
}

# 1. Kestrel (the API itself, listens on http://localhost:5000)
Write-Host "Starting API on Kestrel (http://localhost:5000)..."
$env:ASPNETCORE_ENVIRONMENT = "Production"   # inherited by the child process
Start-Process dotnet -ArgumentList "`"$apiDir\MCS app.dll`"" -WorkingDirectory $apiDir -WindowStyle Hidden

# 2. Caddy (reverse proxy, listens on http://localhost:80 -> Kestrel)
Write-Host "Starting Caddy reverse proxy (http://localhost:80)..."
Start-Process $caddy -ArgumentList "run", "--config", "`"$root\Caddyfile`"" -WorkingDirectory $root -WindowStyle Hidden

Write-Host ""
Write-Host "Done. API is reachable through Caddy at http://localhost/api/employees"
Write-Host "Swagger UI:                            http://localhost/swagger"
