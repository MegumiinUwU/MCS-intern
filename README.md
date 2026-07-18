# MCS-intern

This repository holds a mini task we worked on during the MCS group internship: an
employee management web app (React front end + ASP.NET Core API) that a
completely non-technical user can install and run on their own machine with a
single double click.

## The problem

The app was originally hosted on **IIS**. That worked for us as developers, but
not for the people who actually use it: inspectors working on oil sites, running
the app **locally on their own devices**. For them IIS is a dead end:

- It is heavy on the kind of laptops used in the field.
- Setting it up (bindings, app pools, certificates, permissions) is far beyond
  what a normal client user can deal with.
- When anything breaks, there is nobody on site who can fix it.

So the goal became: **replace IIS with something lighter, and make the entire
setup happen with one double click on an installer.** No prerequisites, no
configuration screens, no IT knowledge. After that first click the app should
just always be there, running 24/7, surviving reboots, reachable from the
browser at any time.

## The solution

We researched the options and compared hosting models (IIS vs Kestrel behind a
reverse proxy, and several proxies: nginx, YARP, Caddy) and landed on this
architecture:

```
Internet ──HTTPS──► Cloudflare edge ──tunnel──► cloudflared ──► Caddy (:80) ──┬─► Kestrel (:5000)
                                                                              └─► React SPA
```

Day to day, everything runs **locally**: Caddy serves the React build and
reverse-proxies API calls to Kestrel on the same machine. The Cloudflare tunnel
is an optional layer on top, used only when the client wants to share results
with someone over the internet.

### Why this architecture

- **Light.** Kestrel is the ASP.NET Core built-in server and Caddy is a single
  small executable. Together they use a fraction of what IIS needs, which
  matters on field laptops.
- **Zero configuration for the user.** Caddy is driven by one generated
  Caddyfile and needs no server administration. There is nothing to click
  through and nothing to maintain.
- **One origin, no CORS.** The SPA and the API are served from the same
  address, so the front end calls the API with plain relative URLs and the
  whole class of CORS problems disappears.
- **Works fully offline.** An oil site does not always have internet. The local
  path (browser -> Caddy -> Kestrel/SPA) has no external dependency at all.
- **Internet access on demand, safely.** When the client wants to send results
  to someone, the Cloudflare tunnel exposes the app over HTTPS **without opening
  any ports and without revealing the machine's IP**. The connection is
  outbound-only; stopping the tunnel removes the exposure completely.
- **Easy to automate.** Because both servers are just processes with a working
  directory, the installer can register them as Windows boot tasks and get true
  24/7 behavior, something that is much harder to package with IIS.

## Proof: one file, one double click, raw Windows

To prove the installer really needs nothing, we created a brand new VM,
installed a raw copy of Windows on it (no .NET, no SQL Server, nothing), and ran
the single-file installer downloaded from this repo's
[Releases](../../releases) page. (Ignore the installer UI, we have not worked on
its look yet.)

The result, end to end:

![Demo: one-click install on a clean Windows VM](docs/demo.gif)

What the installer does on that first double click:

1. Silently installs the bundled **SQL Server 2022 Express** if the machine does
   not have it (first run only, 10-15 minutes), sets the service to start with
   Windows, and grants `SYSTEM` admin rights on SQL so the app can create its
   database.
2. Copies the self-contained API (no .NET runtime needed), the React build, and
   Caddy to `C:\DeployedApps\<name>\`.
3. Adds a firewall rule for Caddy so the app is reachable from the LAN.
4. Registers two **Task Scheduler** tasks (`<name>-Api`, `<name>-Caddy`) that
   run as `SYSTEM` on **system boot**, with no time limit and automatic restart
   on failure. This is what makes the app run 24/7: it starts before anyone
   logs in and comes back by itself after every reboot or crash.
5. Puts a shortcut on the desktop pointing to `http://<machine>/<name>/` and
   opens the browser on the employees page.

To stop the app: open Task Scheduler, end and disable the `<name>-Api` and
`<name>-Caddy` tasks (just killing the processes is not enough, they restart).
Re-running the installer with the same name redeploys in place.

## Repository layout

```
backend/     ASP.NET Core API (EF Core + SQL Server), the "MCS app" project
frontend/    React 19 + Vite single-page app
installer/   WPF one-click deployer (WebAppInstaller) and its bundled assets
scripts/     start/stop/tunnel/verify PowerShell scripts for development
docs/        architecture and migration reports, demo gif
Caddyfile    reverse proxy + static file config used in development (repo root)
deploy/      build output: deploy/api (published API), deploy/web (built SPA), git-ignored
```

## Development

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

Then browse `http://localhost/` for the app, or `http://localhost/swagger` for
the API docs.

### Front-end development

```powershell
.\scripts\start-servers.ps1   # or just the API: dotnet run --project "backend\MCS app.csproj"
cd frontend; npm run dev      # http://localhost:5173, proxies /api to Kestrel on :5000
```

The Vite dev server proxies `/api` to Kestrel, mirroring what Caddy does in
production, so the same relative URLs work in both places.

### Internet access (Cloudflare Tunnel)

```powershell
.\scripts\start-servers.ps1   # Kestrel + Caddy must be running first
.\scripts\start-tunnel.ps1    # prints a random https://xxxx.trycloudflare.com URL
```

Anyone with that URL gets the full web app over HTTPS. The `trycloudflare.com`
URL is random and changes on every run, which is fine for demos; a permanent URL
needs a domain added to the Cloudflare account. The app has no authentication,
so stop the tunnel when done (`Ctrl+C` or `.\scripts\stop-servers.ps1`).

## Building the installer package

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

# 4. Publish the installer itself (one self-extracting exe, everything embedded):
dotnet publish installer\WebAppInstaller\WebAppInstaller.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o installer\dist
```

The output is a single `WebAppInstaller.exe` (about 584 MB) that embeds the API,
the front end, Caddy, and the SQL Server Express setup. That one file is what
gets attached to a GitHub release, so the user's flow is: click the release,
download, double click, done.

## Database

All environments use the same database: `MCS_Employees` on the local SQL Server
instance, Windows auth. Development machines use `Server=localhost`
(`backend/appsettings.json`); the installer's bundled config uses
`Server=localhost\SQLEXPRESS`, the instance its silent SQL setup creates. The
API creates the database and seed data on first start (`EnsureCreated`).

To confirm the frontend is hitting the right stack and database during
development:

```powershell
.\scripts\verify-database.ps1
```

It checks that the API responds through Caddy + Kestrel (`Via: 1.1 Caddy`,
`Server: Kestrel` headers), that the employee count returned by the API matches
the `localhost` database, and warns if IIS has started again.

## API endpoints

- `GET /api/employees`
- `GET /api/employees/{id}`
- `POST /api/employees/{id}/documents`
- `GET /api/employees/{id}/documents`
- `GET /api/employees/{id}/documents/{documentId}`
