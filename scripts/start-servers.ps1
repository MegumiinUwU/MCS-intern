# Starts the MCS Employee API on Kestrel, with Caddy in front of it serving the
# React app and reverse-proxying the API:
#
#   Client -> Caddy (:80) -> Kestrel (http://localhost:5000)
#
# Run from anywhere:  .\scripts\start-servers.ps1

$root = Split-Path $PSScriptRoot -Parent
$apiDir = Join-Path $root "deploy\api"
$webDir = Join-Path $root "deploy\web"

# Resolve caddy.exe (installed via winget, user scope)
$caddy = (Get-Command caddy -ErrorAction SilentlyContinue).Source
if (-not $caddy) {
    $caddy = "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\CaddyServer.Caddy_Microsoft.Winget.Source_8wekyb3d8bbwe\caddy.exe"
}
if (-not (Test-Path $caddy)) {
    Write-Error "caddy.exe not found. Install it with: winget install CaddyServer.Caddy --scope user"
    exit 1
}

if (-not (Test-Path (Join-Path $apiDir "MCS app.dll"))) {
    Write-Error "API not published. Build it with: dotnet publish `"$root\backend\MCS app.csproj`" -c Release -o `"$apiDir`""
    exit 1
}

if (-not (Test-Path (Join-Path $webDir "index.html"))) {
    Write-Error "Front end not built. Build it with: cd `"$root\frontend`"; npm ci; npm run build"
    exit 1
}

# 1. Kestrel (the API itself, listens on http://localhost:5000)
Write-Host "Starting API on Kestrel (http://localhost:5000)..."
$env:ASPNETCORE_ENVIRONMENT = "Production"   # inherited by the child process
Start-Process dotnet -ArgumentList "`"$apiDir\MCS app.dll`"" -WorkingDirectory $apiDir -WindowStyle Hidden

# 2. Caddy (serves the React app, proxies /api and /swagger to Kestrel).
#    Working directory is the repo root so the Caddyfile's relative
#    "root * deploy/web" and log path resolve correctly.
Write-Host "Starting Caddy (http://localhost:80)..."
Start-Process $caddy -ArgumentList "run", "--config", "`"$root\Caddyfile`"" -WorkingDirectory $root -WindowStyle Hidden

Write-Host ""
Write-Host "Done."
Write-Host "  Web app:    http://localhost/"
Write-Host "  API:        http://localhost/api/employees"
Write-Host "  Swagger UI: http://localhost/swagger"
