# Auth API contract

This is the authoritative auth contract for the DFW API. It was checked against
the implementation and exercised against a running API on 2026-11-06.

Source of truth:

- `DotnetApi/Endpoints/AuthEndpoints.cs`
- `DotnetApi/Contracts/AuthContracts.cs`
- `DotnetApi/Services/AuthService.cs`
- `DotnetApi/Services/TokenService.cs`
- `DotnetApi/Services/DistributedRefreshTokenStore.cs`
- `DotnetApi/Infrastructure/AuthPermissions.cs`
- `DotnetApi/Program.cs`

Local base URL with the `http` launch profile: `http://localhost:5248`.
Frontend integration guidance is in
[`FRONTEND_INTEGRATION.md`](FRONTEND_INTEGRATION.md).

## Endpoint summary

| Method | Path | Authentication | Success | Description |
| --- | --- | --- | --- | --- |
| `POST` | `/api/auth/login` | Anonymous | `200` | Exchange email/password for a token pair |
| `POST` | `/api/auth/refresh` | Anonymous | `200` | Rotate a refresh token and issue a new pair |
| `POST` | `/api/auth/logout` | Anonymous | `204` | Revoke one refresh token |
| `GET` | `/api/auth/me` | `Bearer` access token | `200` | Current user, roles and permissions |

All POST request bodies are JSON with `Content-Type: application/json`.
`/api/auth/logout` is idempotent: a syntactically valid unknown or expired
refresh token still returns `204`. Logging out does **not** invalidate the
access token.

## Response types

### `AuthTokenResponse`

Returned by both `/api/auth/login` and `/api/auth/refresh`.

| Field | Type | Notes |
| --- | --- | --- |
| `accessToken` | string | Signed JWT. Send as `Authorization: Bearer <accessToken>`. |
| `expiresAt` | string | ISO 8601 date-time with an offset, for example `2026-11-06T03:56:12.0903071+00:00`. This is the access-token expiry. |
| `refreshToken` | string | Opaque 43-character Base64URL token. Never send it as a bearer token. |
| `user` | object | `AuthUserResponse` for the authenticated user. |

Example:

```json
{
  "accessToken": "<jwt>",
  "expiresAt": "2026-11-06T03:56:12.0903071+00:00",
  "refreshToken": "<43-character-base64url-token>",
  "user": {
    "id": "<user-uuid>",
    "email": "john@example.com",
    "name": "John Doe",
    "role": "admin",
    "roles": ["admin"],
    "permissions": [
      "admin:access",
      "user:read",
      "user:write",
      "product:read",
      "product:write"
    ]
  }
}
```

### `AuthUserResponse`

| Field | Type | Notes |
| --- | --- | --- |
| `id` | UUID string | User id. Also the JWT `sub` claim. |
| `email` | string | Normalized lowercase email. |
| `name` | string | Display name. |
| `role` | string enum | `admin`, `manager`, or `viewer`. |
| `roles` | string[] | Currently always exactly one element, the same lowercase role. |
| `permissions` | string[] | Permission names granted to the role. Use this list for UI gating. |

The JWT itself contains the `role` claim but **does not contain a
`permissions` claim**. Permissions are returned in the login/refresh response
and by `GET /api/auth/me`. `/me` and `/refresh` read the current user from the
database, so a role change is reflected there; an already-issued access token
keeps its old `role` claim until it expires.

## JWT access token

| Property | Value |
| --- | --- |
| Algorithm | `HS256` |
| Issuer (`iss`) | `Jwt:Issuer`, default `dfw-api` |
| Audience (`aud`) | `Jwt:Audience`, default `dfw-frontend` |
| Subject (`sub`) | User UUID |
| `email` | User email |
| `name` | User display name |
| `role` | Lowercase role: `admin`, `manager`, or `viewer` |
| `jti` | Random token id |
| `iat`, `nbf`, `exp` | Unix timestamps in seconds |
| Default lifetime | 60 minutes (`Jwt:AccessTokenMinutes`) |

The API validates issuer, audience, signature and lifetime, with a 30-second
clock skew. `NameClaimType` is `name` and `RoleClaimType` is `role`.

Access tokens are stateless. There is no access-token denylist, so logout cannot
make an already-issued access token invalid. Keep access-token lifetimes short
and treat logout as "revoke the refresh token and clear the client session".

## Refresh tokens

- Generated from 32 cryptographically random bytes and Base64URL encoded, which
  produces a 43-character opaque string. It is not a JWT.
- The raw token is never stored. The cache key is
  `auth:refresh:<sha256-hex>` (uppercase hexadecimal); the cached value contains
  the user id and expiry.
- Cache expiry is absolute and equals `Jwt:RefreshTokenDays` (default 7 days)
  from the time the token is issued. There is no sliding expiration.
- Refresh is **single-use**. The old token is removed before the new pair is
  issued. Reusing the old token returns `401`.
- If two clients refresh the same token concurrently, only one succeeds. The
  other receives `401` and the client must treat the session as expired unless
  it can recover the winning token pair.

Storage behavior:

| `Cache:Provider` | Behavior |
| --- | --- |
| `Memory` (default) | Process-local. Tokens are lost when the API restarts and are not shared between instances. |
| `Redis` | Shared across instances and survives API restarts. Set `ConnectionStrings:Redis` and `Cache:Provider=Redis`. |

## `POST /api/auth/login`

Request:

```json
{
  "email": "john@example.com",
  "password": "Passw0rd!"
}
```

The email is trimmed and lowercased before lookup. The password is verified
against the stored hash.

Success: `200` with `AuthTokenResponse`.

Failures:

| Status | When | Body |
| --- | --- | --- |
| `400` | Missing/invalid email or password | `ValidationProblemDetails` with `errors` |
| `401` | Unknown email, wrong password, or no password hash | `ProblemDetails` with `detail`: `Invalid email or password.` |

Bash:

```bash
curl -s -X POST http://localhost:5248/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"john@example.com","password":"Passw0rd!"}'
```

PowerShell:

```powershell
$login = Invoke-RestMethod -Method Post `
  -Uri http://localhost:5248/api/auth/login `
  -ContentType 'application/json' `
  -Body (@{ email = 'john@example.com'; password = 'Passw0rd!' } | ConvertTo-Json)
```

## `POST /api/auth/refresh`

Request:

```json
{
  "refreshToken": "<43-character-base64url-token>"
}
```

Success: `200` with the same `AuthTokenResponse` shape as login. The response
contains a new access token, a new refresh token, and the current user record.

Failures:

| Status | When | Body |
| --- | --- | --- |
| `400` | Missing token, token longer than 512 characters | `ValidationProblemDetails` with `errors` |
| `401` | Unknown, expired, revoked, or already-used token | `ProblemDetails` with `detail`: `Invalid or expired refresh token.` |

Important client rules:

1. Persist the new refresh token immediately. The old one is already invalid.
2. Serialize concurrent refreshes for the same session. Two parallel requests
   using the same refresh token will race; one gets `401`.
3. Treat `400`/`401` from refresh as an invalid session. Do not retry it.
4. Treat network errors and `5xx` as transient and retry later. Do not destroy a
   valid session because the API was temporarily down.

## `POST /api/auth/logout`

Request:

```json
{
  "refreshToken": "<43-character-base64url-token>"
}
```

Success: `204 No Content` for any syntactically valid token, even if the token
is unknown or already expired. Missing or over-long tokens return `400` with a
validation problem.

Logout only removes the refresh token from the cache. The current access token
remains valid until its `expiresAt` time.

## `GET /api/auth/me`

Requires `Authorization: Bearer <accessToken>`. Returns `AuthUserResponse`
directly, for example:

```json
{
  "id": "<user-uuid>",
  "email": "john@example.com",
  "name": "John Doe",
  "role": "admin",
  "roles": ["admin"],
  "permissions": [
    "admin:access",
    "user:read",
    "user:write",
    "product:read",
    "product:write"
  ]
}
```

Failures:

| Status | When |
| --- | --- |
| `401` | Missing/invalid/expired bearer token, malformed `sub`, or user no longer exists |

## Roles and permissions

| Permission | admin | manager | viewer |
| --- | --- | --- | --- |
| `admin:access` | yes | yes | no |
| `user:read` | yes | yes | yes |
| `user:write` | yes | no | no |
| `product:read` | yes | yes | yes |
| `product:write` | yes | yes | no |

`admin:access` means "can access the admin shell". It is **not** a wildcard that
grants every other permission. In particular, a manager has `admin:access` but
does not have `user:write`. Gate write actions on the specific permission
(`user:write`, `product:write`) instead of treating `admin:access` as "admin
role".

Endpoint authorization:

| Endpoint group | Read | Write / archive / delete |
| --- | --- | --- |
| `/api/users` | Any authenticated role | `admin` only |
| `/api/products` | Any authenticated role | `manager` or `admin` |
| `/api/metrics-dashboard*` | Any authenticated role | Read-only |
| `/api/auth/me` | Any authenticated role | n/a |

## Configuration

`Jwt` from `appsettings.json`:

```json
{
  "Jwt": {
    "Issuer": "dfw-api",
    "Audience": "dfw-frontend",
    "AccessTokenMinutes": 60,
    "RefreshTokenDays": 7
  }
}
```

The development signing key lives in `appsettings.Development.json`. For any
other environment set a key of at least 32 UTF-8 bytes:

```text
Jwt__SigningKey=<at-least-32-bytes>
```

Startup validates `Issuer`, `Audience`, `SigningKey`, `AccessTokenMinutes` and
`RefreshTokenDays`, and fails fast when they are missing or invalid.

Redis:

```powershell
$env:Cache__Provider = "Redis"
$env:ConnectionStrings__Redis = "localhost:6379,abortConnect=false"
```

## Error format

Validation failures use `application/problem+json` with an `errors` object:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "email": ["Email must be a valid email address."],
    "password": ["Password is required."]
  },
  "traceId": "..."
}
```

Other auth failures use `ProblemDetails`:

```json
{
  "title": "Unauthorized",
  "status": 401,
  "detail": "Invalid email or password.",
  "instance": "/api/auth/login"
}
```

Protected endpoints return `401` with `detail: "Authentication is required."`
when the bearer token is missing or invalid. Role-protected endpoints return
`403` with `detail: "You do not have permission to access this resource."`.

## CORS and cookies

CORS allows any origin, any header and any method. Credentials are not enabled,
so browser clients should send the access token in the `Authorization` header,
not cookies. The auth flow does not set or read auth cookies itself.

## Quick verification

PowerShell:

```powershell
$login = Invoke-RestMethod -Method Post `
  -Uri http://localhost:5248/api/auth/login `
  -ContentType 'application/json' `
  -Body (@{ email = 'john@example.com'; password = 'Passw0rd!' } | ConvertTo-Json)

$headers = @{ Authorization = "Bearer $($login.accessToken)" }
Invoke-RestMethod http://localhost:5248/api/auth/me -Headers $headers

$refreshed = Invoke-RestMethod -Method Post `
  -Uri http://localhost:5248/api/auth/refresh `
  -ContentType 'application/json' `
  -Body (@{ refreshToken = $login.refreshToken } | ConvertTo-Json)

Invoke-WebRequest -UseBasicParsing -Method Post `
  -Uri http://localhost:5248/api/auth/logout `
  -ContentType 'application/json' `
  -Body (@{ refreshToken = $refreshed.refreshToken } | ConvertTo-Json)
```

Expected: login and refresh return token pairs, `/me` returns the user, and
logout returns `204`.
