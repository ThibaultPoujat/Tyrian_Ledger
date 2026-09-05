# Local Application Runtime

## Development start

Prerequisites are .NET 10, Node.js 22, and npm. Install the locked frontend
dependencies once:

```bash
npm --prefix frontend ci
```

Then start these two processes from the repository root:

```bash
dotnet run --project src/Gw2Tp.Web/Gw2Tp.Web.csproj
```

```bash
npm --prefix frontend run dev
```

Open `http://localhost:5173`. Vite proxies relative `/api` requests to the
ASP.NET Core host at `http://127.0.0.1:5080`, so React uses the same relative
API contract in development and production. The host health response is also
available directly at `http://127.0.0.1:5080/api/health`.

The development launch profile enables only the exact origins declared in
`appsettings.Development.json`. A direct browser request from another origin
does not receive CORS permission. The Vite target may be changed with
`VITE_LOCAL_API_ORIGIN`, but the host origin allowlist must be changed
separately and explicitly if direct cross-origin development calls are needed.

No ArenaNet API key or database is required for this runtime foundation.

## Local production start

Publish the host; its publish target performs a locked frontend install/build
and places the generated assets in the host output:

```bash
dotnet publish src/Gw2Tp.Web/Gw2Tp.Web.csproj --configuration Release --output out/TyrianLedger
```

Run the published application:

```bash
ASPNETCORE_ENVIRONMENT=Production dotnet out/TyrianLedger/Gw2Tp.Web.dll
```

Open `http://localhost:5080`. The host serves React and `/api/health` from that
same origin. The generated `out/` directory is ignored and can be recreated.

## Network and browser security configuration

Normal configuration lives under `TyrianLedger:Host`:

- `Port` defaults to `5080`;
- `ListenAddresses` defaults to the explicit IPv4 and IPv6 loopback addresses
  `127.0.0.1` and `::1`;
- `AllowedHosts` defaults to `localhost`, `127.0.0.1`, and `[::1]`;
- `TrustedDevelopmentOrigins` is empty outside Development and contains only
  the exact local Vite origins in Development.

Environment-variable overrides use the standard double-underscore form, such
as `TyrianLedger__Host__Port=5081`. Wildcard and non-loopback listen addresses
are rejected at startup. Generic `ASPNETCORE_URLS`/`--urls`, HTTP/HTTPS port,
and `Kestrel:Endpoints` overrides are rejected so they cannot bypass the
loopback-only policy. Expanding network exposure is an owner architecture and
security decision, not a normal configuration change.

The host rejects unapproved `Host` headers. In production it does not enable
cross-origin access. Independently of CORS, every unsafe HTTP method requires
an exact same-origin `Origin` header, or an explicitly trusted development
origin while running in Development, and the application-request header
`X-Tyrian-Ledger-Request: 1`. No state-changing product endpoint exists yet,
but the policy is placed before endpoint routing so future endpoints inherit
the boundary.
