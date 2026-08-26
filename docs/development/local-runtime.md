# Local runtime

Tyrian Ledger is a personal, local application. The Web API binds to
`http://127.0.0.1:5000` by default. The Vite development server also binds to
`127.0.0.1`, and its default API proxy target is the Web API address above.

## Intentional local overrides

Use an explicit override only when a local development workflow requires a
different loopback port or address:

```sh
dotnet run --project src/Gw2Tp.Web -- --urls http://127.0.0.1:5050
ASPNETCORE_URLS=http://127.0.0.1:5050 dotnet run --project src/Gw2Tp.Web
API_URL=http://127.0.0.1:5050 npm run dev
npm run dev -- --host 127.0.0.1 --port 5174
```

These overrides are developer-controlled escape hatches, not LAN-hosting
support. Do not use `0.0.0.0`, a wildcard address, a LAN address, or a public
address for normal use: doing so exposes local account-related data and the
local API to other devices or processes. Any future public or multi-user
deployment requires a separate security, architecture, and owner review.

## HTTP baseline

The local HTTP API sends these response headers on every response:

- `Content-Security-Policy: default-src 'none'; base-uri 'none'; frame-ancestors 'none'`
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Referrer-Policy: no-referrer`

HSTS and HTTPS redirection are intentionally absent because this M1 runtime
does not configure HTTPS.

Future Minimal API request DTOs must use DataAnnotations as appropriate. The
Web host registers .NET 10's built-in validation mechanism, which applies
validation to eligible Minimal API request parameters without per-endpoint
custom middleware.
