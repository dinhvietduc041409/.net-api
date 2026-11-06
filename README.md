# DFW API prototype

A small ASP.NET Core 10 API that implements the frontend contracts from
`dfw_frontend/packages/api-contracts`, backed by PostgreSQL 18 and EF Core.

Implemented contracts:

- `users.ts` — full CRUD, with `role` restricted to `admin`, `manager`, `viewer`.
- `products.ts` — full CRUD plus `archive`, with `status` restricted to `active`, `archived`.
- `metrics.ts` — read-only dashboard, KPI, chart and paged log endpoints.
- `routing.ts` — frontend-only, no server endpoint.
- Auth — JWT access tokens and rotating refresh tokens backed by
  `IDistributedCache` (in-memory by default, Redis optional).

Detailed auth documentation:

- [`docs/AUTH.md`](docs/AUTH.md) — verified auth contract: endpoints, response
  fields, JWT claims, token rotation, permissions and errors.
- [`docs/FRONTEND_INTEGRATION.md`](docs/FRONTEND_INTEGRATION.md) — frontend
  integration guide, including refresh rotation, proxy setup and common
  failure modes.

## Stack

- .NET 10 minimal API
- EF Core 10 + Npgsql
- PostgreSQL 18
- JWT bearer authentication
- `IDistributedCache` — in-memory by default, Redis-ready
- OpenAPI at `/openapi/v1.json`

## Project layout

One folder per layer:

- `Contracts/` — request/response DTOs and mappings
- `Data/` — EF Core context, migrations and seeding
- `Endpoints/` — minimal API route handlers
- `Infrastructure/` — cross-cutting auth, config and error types
- `Interfaces/` — service abstractions
- `Models/` — persistence entities
- `Services/` — service implementations

## API docs

- This README covers setup, the auth overview, roles, cache and examples.
- [`docs/AUTH.md`](docs/AUTH.md) is the canonical auth contract.
- [`docs/FRONTEND_INTEGRATION.md`](docs/FRONTEND_INTEGRATION.md) is the
  frontend/Auth.js integration guide.
- `DotnetApi/DotnetApi.http` contains ready-to-run login, refresh, logout and
  authenticated CRUD requests.
- The generated OpenAPI document is available at `/openapi/v1.json` in
  Development. It includes the `Bearer` JWT security scheme and marks every
  protected operation accordingly.
- An interactive OpenAPI UI is not bundled; point any OpenAPI viewer
  (Swagger UI, Scalar, Postman) at the JSON URL and use the Bearer token from
  `/api/auth/login`.

## Quick start

### 1. Start PostgreSQL

Use the helper script to create a dedicated cluster in `.postgres/` on port
`5434` (it does not touch any existing PostgreSQL service):

```powershell
./scripts/db-init.ps1
```

The script initializes the cluster, creates the `dfw_api` database, and starts
the server. Useful commands:

```powershell
./scripts/db-start.ps1   # initialize if needed, then start
./scripts/db-stop.ps1    # stop the local cluster
./scripts/db-reset.ps1   # stop, delete data, and re-initialize
```

Alternatively, if Docker is running:

```powershell
docker compose up -d
```

The connection string is configured in `DotnetApi/appsettings.json`:

```text
Host=localhost;Port=5434;Database=dfw_api;Username=postgres;Password=postgres
```

Override it with the `ConnectionStrings__Postgres` environment variable.

### 2. Run the API

```powershell
dotnet run --project DotnetApi --launch-profile http
```

The API listens on <http://localhost:5248>.

On startup it applies EF Core migrations, seeds demo users, products, metric
samples and metric logs when the relevant tables are empty, and backfills demo
passwords for existing users whose password hash is empty.

### 3. Log in

The seed creates 12 users; these are the accounts used in the examples. All
seeded users use the prototype-only password `Passw0rd!`:

| Email | Role |
| --- | --- |
| `john@example.com` | admin |
| `jane@example.com` | manager |
| `bob@example.com` | viewer |

```powershell
$login = Invoke-RestMethod -Method Post `
  -Uri http://localhost:5248/api/auth/login `
  -ContentType 'application/json' `
  -Body (@{ email = 'john@example.com'; password = 'Passw0rd!' } | ConvertTo-Json)

$login.accessToken
$login.refreshToken
$login.user
```

The response contains `accessToken`, `expiresAt`, `refreshToken` and `user`.
`user` contains `id`, `email`, `name`, `role`, `roles` and `permissions`; see
[`docs/AUTH.md`](docs/AUTH.md) for the complete shape.

> Windows PowerShell 5.1 can strip the double quotes from inline JSON passed to
> native `curl.exe`, producing a `400`. Use the `Invoke-RestMethod` form above
> (or `curl.exe --data-binary "@body.json"`); the `bash` examples later in this
> file use ordinary `curl`.

### 4. Verify

In the same PowerShell session:

```powershell
Invoke-RestMethod http://localhost:5248/health

$headers = @{ Authorization = "Bearer $($login.accessToken)" }
Invoke-RestMethod http://localhost:5248/api/users -Headers $headers
Invoke-RestMethod 'http://localhost:5248/api/metrics-dashboard?range=7d' -Headers $headers
```

`/` and `/health` stay anonymous. All `/api/users`, `/api/products` and
`/api/metrics-dashboard*` endpoints require a bearer token; `/api/auth/login`,
`/api/auth/refresh` and `/api/auth/logout` are anonymous.

## Auth

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| `POST` | `/api/auth/login` | Anonymous | Exchange email/password for a token pair |
| `POST` | `/api/auth/refresh` | Anonymous | Rotate a refresh token and issue a new pair |
| `POST` | `/api/auth/logout` | Anonymous | Revoke one refresh token |
| `GET` | `/api/auth/me` | Bearer | Current user, roles and permissions |

The complete, verified contract is in [`docs/AUTH.md`](docs/AUTH.md). Key points:

- Access tokens are HS256 JWTs. The default lifetime is 60 minutes
  (`Jwt:AccessTokenMinutes`); issuer and audience default to `dfw-api` and
  `dfw-frontend`.
- Refresh tokens are opaque 32-byte random values, Base64URL encoded to 43
  characters. The raw token is never stored: the cache key is its SHA-256 hash
  and the cached value holds the user id and expiry. Lifetime is absolute,
  `Jwt:RefreshTokenDays` (default 7 days).
- Refresh is **single-use**. The old token is consumed before the new pair is
  issued; reusing it returns `401`. Concurrent refreshes for the same session
  must be serialized by the client.
- Logout revokes only the refresh token. Access tokens are stateless and remain
  valid until `expiresAt`.
- Login and refresh return the same shape:
  `{ accessToken, expiresAt, refreshToken, user }`.
- `user` contains `id`, `email`, `name`, `role`, `roles` and `permissions`.
  `roles` currently contains exactly one lowercase role; use `permissions` for
  UI gating.

### Role permissions

| Permission | admin | manager | viewer |
| --- | --- | --- | --- |
| `admin:access` | ✓ | ✓ | |
| `user:read` | ✓ | ✓ | ✓ |
| `user:write` | ✓ | | |
| `product:read` | ✓ | ✓ | ✓ |
| `product:write` | ✓ | ✓ | |

`admin:access` means "can access the admin shell"; it is **not** a wildcard that
grants `user:write` or `product:write`. Managers have `admin:access` but are
still read-only for users. Gate write actions on the specific permission.

Endpoint policies:

- Users: read for any authenticated role; create/update/delete admin only.
- Products: read for any authenticated role; create/update/archive/delete
  manager or admin.
- Metrics: any authenticated role.
- Auth `/me`: any valid access token.

The login response includes the frontend-compatible `roles` and `permissions`
arrays, so `@repo/auth-next` can call this API from its `authorize()` callback.
See [`docs/FRONTEND_INTEGRATION.md`](docs/FRONTEND_INTEGRATION.md) for the
Auth.js flow, refresh rotation and the `admin:access` gotcha.

## Cache and Redis

The API depends on `IDistributedCache`, so the provider is a configuration
choice. The default is an in-process memory cache:

```json
"Cache": {
  "Provider": "Memory",
  "Redis": {
    "InstanceName": "dfw:"
  }
}
```

To switch to Redis, add a connection string and change the provider:

```powershell
$env:Cache__Provider = "Redis"
$env:ConnectionStrings__Redis = "localhost:6379,abortConnect=false"
```

Or start the optional local Redis container:

```powershell
docker compose --profile redis up -d redis
```

With `Provider=Redis`, refresh tokens survive API restarts and work across
multiple instances. With `Provider=Memory`, they are lost on restart and only
work for a single API process — acceptable for local development.

### JWT configuration

`Jwt` lives in `appsettings.json`; the development signing key is in
`appsettings.Development.json`. For any other environment set:

```text
Jwt__SigningKey=<at-least-32-bytes>
```

Startup validates `Issuer`, `Audience`, `SigningKey` (at least 32 UTF-8 bytes),
`AccessTokenMinutes` and `RefreshTokenDays`, and fails fast when any value is
missing or invalid. The API validates incoming tokens for issuer, audience,
signature and lifetime, with a 30-second clock skew.

## Endpoints

### Users

| Method | Path | Description |
| --- | --- | --- |
| `GET` | `/api/users` | List users, newest first |
| `GET` | `/api/users/{id}` | Get one user |
| `POST` | `/api/users` | Create user |
| `PUT`, `PATCH` | `/api/users/{id}` | Update supplied fields |
| `DELETE` | `/api/users/{id}` | Delete user |

`role` is one of `admin`, `manager`, `viewer`. `password` is optional when
creating or updating a user; omitted create passwords fall back to
`Passw0rd!` for this prototype.

### Products

| Method | Path | Description |
| --- | --- | --- |
| `GET` | `/api/products` | List products, newest first |
| `GET` | `/api/products/{id}` | Get one product |
| `POST` | `/api/products` | Create product |
| `PUT`, `PATCH` | `/api/products/{id}` | Update supplied fields |
| `POST` | `/api/products/{id}/archive` | Archive product |
| `DELETE` | `/api/products/{id}` | Delete product |

`status` is one of `active`, `archived`.

### Metrics

These paths match the Next.js mock routes already used by the admin shell.

| Method | Path | Description |
| --- | --- | --- |
| `GET` | `/api/metrics-dashboard?range=7d` | Dashboard: KPIs, chart, first log page |
| `GET` | `/api/metrics-dashboard/kpis?range=7d` | KPI list |
| `GET` | `/api/metrics-dashboard/chart?range=7d` | Chart series |
| `GET` | `/api/metrics-dashboard/logs` | Paged activity logs |

Metrics query parameters:

- `range`: `24h`, `7d` (default), `30d`, `90d`
- `level`: `all` (default), `debug`, `info`, `warn`, `error`
- `limit`: 1–50, default `10`
- `cursor`: numeric offset from the previous response's `nextCursor`

Example:

```powershell
$headers = @{ Authorization = "Bearer $($login.accessToken)" }
Invoke-RestMethod `
  'http://localhost:5248/api/metrics-dashboard/logs?range=30d&level=warn&limit=10' `
  -Headers $headers
```

## Request examples

The examples in this section are for `bash`/`curl`. On Windows PowerShell 5.1,
use `Invoke-RestMethod` as shown in the quick-start section; `curl.exe` can
mangle inline JSON quotes.

Login:

```bash
curl -X POST http://localhost:5248/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"john@example.com","password":"Passw0rd!"}'
```

Create a user as an admin:

```bash
curl -X POST http://localhost:5248/api/users \
  -H "Authorization: Bearer <accessToken>" \
  -H "Content-Type: application/json" \
  -d '{"email":"ada@example.com","name":"Ada Lovelace","role":"admin","password":"Secret123!"}'
```

Create and archive a product as a manager or admin:

```bash
curl -X POST http://localhost:5248/api/products \
  -H "Authorization: Bearer <accessToken>" \
  -H "Content-Type: application/json" \
  -d '{"name":"Prototype Widget","description":"A demo product.","price":42.5}'

curl -X POST http://localhost:5248/api/products/{id}/archive \
  -H "Authorization: Bearer <accessToken>"
```

Refresh a token pair:

```bash
curl -X POST http://localhost:5248/api/auth/refresh \
  -H "Content-Type: application/json" \
  -d '{"refreshToken":"<refreshToken>"}'
```

More examples are in `DotnetApi/DotnetApi.http`.

## Validation and errors

- Invalid request bodies or enum values return `400`.
- Validation failures return `400` with an `errors` object.
- Auth login: unknown email, wrong password or missing password hash returns
  `401` with `detail: "Invalid email or password."`.
- Auth refresh: unknown, expired, revoked or already-used refresh token returns
  `401` with `detail: "Invalid or expired refresh token."`.
- Auth logout: a syntactically valid token returns `204` even when it is
  unknown or already expired.
- Duplicate email returns `409`.
- Missing resources return `404`.
- Missing/invalid access tokens return `401`; insufficient role returns `403`.
- Successful deletes and logout return `204`.
- Errors use `application/problem+json`; validation errors include an `errors`
  object, other failures include `title`, `status` and `detail`.

## Database

Tables:

- `users` — unique normalized email, role check constraint and password hash
- `products` — numeric price, status check constraint
- `metric_samples` — hourly `users`, `orders`, `revenue` samples
- `metric_logs` — seeded activity logs
- `__EFMigrationsHistory` — EF Core migration history

Create a new migration after model changes:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations add <Name> --project DotnetApi --startup-project DotnetApi --output-dir Data/Migrations
```

## Notes

- CORS allows any origin, header and method, but does not enable credentials.
  Send the access token in `Authorization: Bearer ...`; the API does not use
  auth cookies.
- `UseHttpsRedirection` is enabled; run the `http` launch profile for local
  development without a certificate.
- Logout revokes only the refresh token. Access tokens cannot be revoked, so
  keep `Jwt:AccessTokenMinutes` short and clear client state on logout.
- Refresh tokens are single-use. Serialize concurrent refreshes; see
  [`docs/FRONTEND_INTEGRATION.md`](docs/FRONTEND_INTEGRATION.md).
- With `Cache:Provider=Memory`, refresh tokens are process-local and lost on
  restart. Use Redis for multiple API instances or restart-safe sessions.
- The local cluster under `.postgres/` is git-ignored. It listens only on
  localhost and uses trust authentication, so it is for development only.
- The metrics routes intentionally mirror the existing Next.js mock routes, so
  `/api/metrics-dashboard*` can be proxied to this API without changing the
  frontend hooks.
- The demo password and development signing key are for local development only.
- The Next.js API client still needs an `Authorization: Bearer` header before it
  can call the protected endpoints.
