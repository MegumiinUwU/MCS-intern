# MCS-intern

## Hosting (Kestrel + Caddy)

The API is no longer hosted on IIS / IIS Express. It runs on **Kestrel**
(the built-in ASP.NET Core web server) behind a **Caddy** reverse proxy:

```
Client ──► Caddy (:80) ──► Kestrel (http://localhost:5000)
```

- Kestrel configuration: `MCS app/appsettings.Production.json`
- Caddy configuration: `Caddyfile` (repo root)
- Publish output: `deploy/api` (`dotnet publish -c Release -o deploy\api`)

### Run

```powershell
.\start-servers.ps1   # starts Kestrel + Caddy
.\stop-servers.ps1    # stops both
```

Then browse `http://localhost/swagger` or call the endpoints below on `http://localhost`.

## Internet access (Cloudflare Tunnel)

To expose the API to the internet without opening any ports or revealing this
machine's IP, a **Cloudflare quick tunnel** sits above Caddy:

```
Internet ──HTTPS──► Cloudflare edge ──tunnel──► cloudflared ──► Caddy (:80) ──► Kestrel (:5000)
```

```powershell
.\start-servers.ps1   # Kestrel + Caddy must be running first
.\start-tunnel.ps1    # prints a random https://xxxx.trycloudflare.com URL
```

Anyone with that URL can then call the API over HTTPS (TLS, DDoS protection and
Cloudflare's edge come for free). Notes:

- The connection is **outbound-only**: nothing on this PC is directly reachable,
  and only `localhost:80` (Caddy → the API) is forwarded — nothing else.
- The `trycloudflare.com` URL is **random and changes on every run** — fine for
  demos. For a permanent URL you need a domain added to the Cloudflare account
  (then a named tunnel replaces the quick one).
- The API has **no authentication**, so stop the tunnel when you are done
  (`Ctrl+C` or `.\stop-servers.ps1`).

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
.\verify-database.ps1
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
