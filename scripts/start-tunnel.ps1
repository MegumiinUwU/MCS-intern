# Exposes the whole web app (React front end + API) to the internet through a
# Cloudflare quick tunnel.
# Requires Kestrel + Caddy to be running first (.\scripts\start-servers.ps1).
#
# Security notes:
#  - Outbound-only connection: no ports are opened on this PC and your IP stays hidden.
#  - Only http://localhost:80 (Caddy) is exposed; nothing else on this machine.
#  - The https://xxxx.trycloudflare.com URL is random and changes every run.
#  - The app has no authentication, so keep the tunnel running only while you need it
#    (Ctrl+C here, or .\scripts\stop-servers.ps1, stops it).

$cloudflared = (Get-Command cloudflared -ErrorAction SilentlyContinue).Source
if (-not $cloudflared) {
    $cloudflared = "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\Cloudflare.cloudflared_Microsoft.Winget.Source_8wekyb3d8bbwe\cloudflared.exe"
}
if (-not (Test-Path $cloudflared)) {
    Write-Error "cloudflared not found. Install it with: winget install Cloudflare.cloudflared --scope user"
    exit 1
}

Write-Host "Starting Cloudflare quick tunnel -> http://localhost:80 (Caddy -> React app + Kestrel)"
Write-Host "Look for the https://....trycloudflare.com URL in the banner below."
Write-Host "Press Ctrl+C to stop the tunnel."
Write-Host ""

& $cloudflared tunnel --url http://localhost:80
