# Verifies that the API (served by Caddy + Kestrel) is using the correct database:
# MCS_Employees on the full SQL Server instance (Server=localhost).
# It compares what the API returns with what is actually in that database.
# Run from the repository root:  .\verify-database.ps1

$ErrorActionPreference = "Stop"

Write-Host "1. Employees in SQL Server (localhost -> MCS_Employees):"
$dbCount = (sqlcmd -S localhost -E -d MCS_Employees -h -1 -W `
    -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM Employees;").Trim()
$dbDocs = (sqlcmd -S localhost -E -d MCS_Employees -h -1 -W `
    -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM EmployeeDocuments;").Trim()
Write-Host "   $dbCount employees, $dbDocs documents"

Write-Host "2. Employees returned by the API through Caddy (http://localhost/api/employees):"
$resp = Invoke-WebRequest "http://localhost/api/employees" -UseBasicParsing
$apiCount = ($resp.Content | ConvertFrom-Json).Count
$via = $resp.Headers["Via"]
$server = $resp.Headers["Server"]
Write-Host "   $apiCount employees  (Server: $server, Via: $via)"

Write-Host ""
if ("$apiCount" -eq "$dbCount" -and $via -like "*Caddy*" -and $server -eq "Kestrel") {
    Write-Host "OK: the frontend is served by Caddy + Kestrel using the correct database (localhost\MCS_Employees)." -ForegroundColor Green
} else {
    Write-Host "MISMATCH:" -ForegroundColor Red
    if ("$apiCount" -ne "$dbCount") { Write-Host " - API returned $apiCount employees but the database has $dbCount." -ForegroundColor Red }
    if ($via -notlike "*Caddy*")    { Write-Host " - Response did not pass through Caddy (Via header: '$via')." -ForegroundColor Red }
    if ($server -ne "Kestrel")      { Write-Host " - Response was not served by Kestrel (Server header: '$server')." -ForegroundColor Red }
    exit 1
}

# Warn if IIS crept back up.
$iis = Get-Service W3SVC -ErrorAction SilentlyContinue
if ($iis -and $iis.Status -eq "Running") {
    Write-Host "WARNING: IIS (W3SVC) is running again. Stop it with: Stop-Service W3SVC, WAS (elevated)" -ForegroundColor Yellow
}
