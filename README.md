# MCS-intern

Employee management web app: a React front end and an ASP.NET Core API, served
together on one origin by Caddy.

## Layout

```
backend/     ASP.NET Core API (EF Core + SQL Server) - "MCS app" project
frontend/    React 19 + Vite single-page app
installer/   WPF one-click deployer (WebAppInstaller)
scripts/     start/stop/tunnel/verify PowerShell scripts
docs/        architecture and migration reports (PDF)
Caddyfile    reverse proxy + static file config (repo root)
deploy/      build output: deploy/api (published API), deploy/web (built SPA) - git-ignored
```

## Hosting (Kestrel + Caddy)

The app runs on **Kestrel** behind a **Caddy** reverse proxy. Caddy serves the
React build and forwards API calls to Kestrel, so the front end and the API
share one origin and no CORS is involved:

```
Client ──► Caddy (:80) ──┬─► /api*, /swagger*  ──► Kestrel (http://localhost:5000)
                         └─► everything else   ──► React SPA (deploy/web)
```

- Kestrel configuration: `backend/appsettings.Production.json`
- Caddy configuration: `Caddyfile` (repo root)

### Build

```powershell
cd frontend; npm ci; npm run build          # -> deploy/web
cd ..
dotnet publish "backend\MCS app.csproj" -c Release -o deploy\api
```

### Run

```powershell
.\scripts\start-servers.ps1   # starts Kestrel + Caddy
.\scripts\stop-servers.ps1    # stops both (and the tunnel)
```

Then browse `http://localhost/` for the app, or `http://localhost/swagger` for the API docs.

### Front-end development

```powershell
.\scripts\start-servers.ps1   # or just the API: dotnet run --project "backend\MCS app.csproj"
cd frontend; npm run dev      # http://localhost:5173, proxies /api to Kestrel on :5000
```

The Vite dev server proxies `/api` to Kestrel, mirroring what Caddy does in
production, so the same relative URLs work in both places.

## Internet access (Cloudflare Tunnel)

To expose the **whole app** (front end + API) to the internet without opening any
ports or revealing this machine's IP, a **Cloudflare quick tunnel** sits above Caddy:

```
Internet ──HTTPS──► Cloudflare edge ──tunnel──► cloudflared ──► Caddy (:80) ──┬─► Kestrel (:5000)
                                                                              └─► React SPA
```

```powershell
.\scripts\start-servers.ps1   # Kestrel + Caddy must be running first
.\scripts\start-tunnel.ps1    # prints a random https://xxxx.trycloudflare.com URL
```

Anyone with that URL gets the full web app over HTTPS. Notes:

- The connection is **outbound-only**: nothing on this PC is directly reachable,
  and only `localhost:80` (Caddy) is forwarded — nothing else.
- The `trycloudflare.com` URL is **random and changes on every run** — fine for
  demos. For a permanent URL you need a domain added to the Cloudflare account
  (then a named tunnel replaces the quick one).
- The app has **no authentication**, so stop the tunnel when you are done
  (`Ctrl+C` or `.\scripts\stop-servers.ps1`).

## Installer (one-click deploy, runs 24/7)

`installer/WebAppInstaller` is a WPF app for non-technical users: double-click,
enter an application name, press **Deploy**, done. The target machine needs
**nothing pre-installed** — the package bundles a self-contained API (no .NET
runtime needed), Caddy, the React build, and the SQL Server 2022 Express setup.

What Deploy does:

1. If the `SQLEXPRESS` instance is missing, silently installs the bundled
   **SQL Server 2022 Express** (first deploy only; takes 10-15 minutes, a
   progress bar is shown). The instance is set to start automatically with
   Windows and `NT AUTHORITY\SYSTEM` is made a SQL admin so the boot task can
   create the `MCS_Employees` database. Then starts the service if stopped.
2. Copies the API, the React build, and Caddy to `C:\DeployedApps\<name>\`.
3. Adds a firewall rule for Caddy (port 80 reachable from the LAN).
4. Registers two **Task Scheduler** tasks — `<name>-Api` and `<name>-Caddy` —
   that run as `SYSTEM` on **system boot**, with no time limit and automatic
   restart every minute on failure. This is what makes the app run 24/7: it
   starts before anyone logs in and comes back by itself after every reboot
   or crash.
5. Starts both tasks immediately and health-checks them (API on :5000,
   Caddy on :80).
6. Puts a `<name>` shortcut on the (all-users) desktop pointing to
   `http://<machine>/<name>/`, and opens the browser.

The installer exe requires administrator rights (UAC prompts on launch) because
of steps 1, 3 and 4.

To stop the app: open **Task Scheduler**, end and disable the `<name>-Api` and
`<name>-Caddy` tasks (just killing the processes is not enough — they restart).
Re-running the installer with the same name redeploys in place.

### Building the installer package

```powershell
# 1. Refresh the bundled API (self-contained, no .NET needed on target):
dotnet publish "backend\MCS app.csproj" -c Release -r win-x64 --self-contained true -o installer\WebAppInstaller\Assets\Api
#    then restore Assets\Api\appsettings.json (it points at localhost\SQLEXPRESS,
#    while backend\appsettings.json points at localhost).

# 2. Refresh the bundled front end (if it changed):
cd frontend; npm ci; npm run build   # then copy deploy\web -> installer\WebAppInstaller\Assets\Web

# 3. Fetch the SQL Server Express media (266 MB, git-ignored, one time per clone):
curl.exe -L -o "installer\WebAppInstaller\Assets\Sql\SQLEXPR_x64_ENU.exe" "https://download.microsoft.com/download/3/8/d/38de7036-2433-4207-8eae-06e247e17b25/SQLEXPR_x64_ENU.exe"
#    verify it: (Get-AuthenticodeSignature "installer\WebAppInstaller\Assets\Sql\SQLEXPR_x64_ENU.exe").Status  ->  Valid

# 4. Publish the installer itself:
dotnet publish installer\WebAppInstaller\WebAppInstaller.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o installer\dist
```

Ship the whole `installer\dist` folder (`WebAppInstaller.exe` + `Assets\`) —
the Assets folder must stay next to the exe.

## Database

All environments (development and published/production) use the **same** database:
`MCS_Employees` on the full SQL Server instance (`Server=localhost`, Windows auth).
This is the database that holds the real data the frontend works with,
including the uploaded PDF documents.

> Historical note: development used to point at `(localdb)\MSSQLLocalDB`, which
> created a second, stale copy of the database with only seed data. That copy is
> no longer used — `appsettings.Development.json` now points at `localhost` too.

To confirm the frontend is hitting the right stack and database:

```powershell
.\scripts\verify-database.ps1
```

It checks that the API responds through Caddy + Kestrel (`Via: 1.1 Caddy`,
`Server: Kestrel` headers), that the employee count returned by the API matches
the `localhost` database, and warns if IIS has started again.

## Endpoints

- `GET /api/employees`
- `GET /api/employees/{id}`
- `POST /api/employees/{id}/documents`
- `GET /api/employees/{id}/documents`
- `GET /api/employees/{id}/documents/{documentId}`
