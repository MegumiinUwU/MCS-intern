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
