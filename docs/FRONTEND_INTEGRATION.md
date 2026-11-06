# Frontend integration guide

This guide is for frontend apps that authenticate against the DFW API. The
backend contract is documented in [`AUTH.md`](AUTH.md); this file focuses on the
client flow, the common integration failures, and the exact response fields to
use.

## Recommended architecture

Use the backend login endpoint from a server-side auth handler (NextAuth/Auth.js
credentials provider, a BFF route, or a server action), keep the refresh token
server-side, and expose only the short-lived access token to the UI:

```text
Browser -> NextAuth / BFF -> POST /api/auth/login  -> DFW API
                          <- accessToken + refreshToken + user
Browser -> BFF proxy      -> Authorization: Bearer <accessToken> -> DFW API
```

Do not put the refresh token in `localStorage` or in a non-HttpOnly cookie if
you can avoid it. If you are building a browser-only SPA, the API does support
direct CORS requests, but a compromised browser can read the refresh token. In
that case, keep refresh-token lifetime short and treat the browser as a public
client.

## Environment

For the existing `dfw_frontend` workspace, the important server-side variable is:

```text
API_BASE_URL=http://localhost:5248
```

Notes:

- Set it in every app that performs auth or proxying (`admin-shell`,
  `web-shell`, and any other server-side app). A missing value makes login fail
  with "The authentication API is not configured. Set API_BASE_URL." in
  production.
- `@repo/auth-next` falls back to `http://localhost:5248` in non-production.
  Production must set `API_BASE_URL`.
- Do not expose this value as `NEXT_PUBLIC_*` unless you intentionally want
  browser code to call the API directly.
- The API's default local URL is `http://localhost:5248` with the `http` launch
  profile. The `https` profile uses `https://localhost:7025` and needs a
  trusted development certificate.
- Auth.js also needs `AUTH_SECRET`/`NEXTAUTH_SECRET` and a matching
  `AUTH_URL`/`NEXTAUTH_URL` for its own callbacks. These are separate from
  `API_BASE_URL`.

## Contract types

Use these shapes; they match the backend exactly.

```ts
export type Role = 'admin' | 'manager' | 'viewer';

export type Permission =
  | 'admin:access'
  | 'user:read'
  | 'user:write'
  | 'product:read'
  | 'product:write';

export interface AuthUser {
  id: string;
  email: string;
  name: string;
  role: Role;
  roles: Role[];
  permissions: Permission[];
}

export interface AuthTokens {
  accessToken: string;
  /** ISO 8601 string with an offset, e.g. 2026-11-06T03:56:12.0903071+00:00 */
  expiresAt: string;
  /** Opaque base64url value. Never use this as a bearer token. */
  refreshToken: string;
  user: AuthUser;
}
```

`roles` currently contains exactly one role. Use `permissions` for UI gating,
and keep `roles` for display and for the existing `@repo/auth-core` session
shape.

## Login

### NextAuth / Auth.js credentials provider

```ts
import CredentialsProvider from 'next-auth/providers/credentials';

const API_BASE_URL = process.env.API_BASE_URL!.replace(/\/+$/, '');

async function loginWithBackend(email: string, password: string) {
  const response = await fetch(`${API_BASE_URL}/api/auth/login`, {
    method: 'POST',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({ email, password }),
    cache: 'no-store',
  });

  if (response.status === 401) return null;
  if (!response.ok) {
    const body = await response.json().catch(() => null);
    throw new Error(body?.detail ?? body?.title ?? 'Login failed.');
  }

  return (await response.json()) as AuthTokens;
}

CredentialsProvider({
  name: 'Credentials',
  credentials: {
    email: { label: 'Email', type: 'email' },
    password: { label: 'Password', type: 'password' },
  },
  async authorize(credentials) {
    if (!credentials?.email || !credentials?.password) return null;

    const result = await loginWithBackend(
      String(credentials.email).trim().toLowerCase(),
      String(credentials.password),
    );

    if (!result) return null;

    return {
      id: result.user.id,
      email: result.user.email,
      name: result.user.name,
      roles: result.user.roles,
      permissions: result.user.permissions,
      accessToken: result.accessToken,
      refreshToken: result.refreshToken,
      accessTokenExpiresAt: Date.parse(result.expiresAt),
    };
  },
});
```

In the Auth.js JWT/session callbacks, copy `roles`, `permissions`,
`accessToken`, `refreshToken`, and `accessTokenExpiresAt` onto the JWT, then
expose `roles`, `permissions`, `accessToken` and `accessTokenExpiresAt` on the
session. Keep `refreshToken` inside the encrypted Auth.js cookie and do not add
it to the session object that is sent to the browser.

Role/permission changes are read from the database by `/api/auth/me` and
`/api/auth/refresh`, but an already-issued access token keeps its old `role`
claim until it expires. If an admin changes a user's role, call `/me` or wait
for the next refresh to update that user's session permissions.

The existing frontend implementation of this flow is in
`dfw_frontend/packages/auth-next/src/backend-auth.ts` and
`dfw_frontend/packages/auth-next/src/authOptions.ts`.

### Plain fetch (non-Next app)

```ts
const response = await fetch(`${API_BASE_URL}/api/auth/login`, {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email, password }),
});

if (response.status === 401) {
  // Show "invalid email or password". Do not retry.
} else if (!response.ok) {
  // Parse validation errors (400) or ProblemDetails (5xx).
} else {
  const tokens = (await response.json()) as AuthTokens;
  // Persist tokens according to your client security model.
}
```

## Refresh before expiry

Use `expiresAt` from the response, not a hard-coded 60-minute timer. Parse it
with `Date.parse`; the backend may return `+00:00` rather than a trailing `Z`.

Refresh about 60 seconds before expiry. Refresh requests rotate the token, so
concurrent requests must share one in-flight refresh per refresh token:

```ts
const refreshInFlight = new Map<string, Promise<AuthTokens | null>>();

export function refreshTokens(refreshToken: string): Promise<AuthTokens | null> {
  const existing = refreshInFlight.get(refreshToken);
  if (existing) return existing;

  const request = fetch(`${API_BASE_URL}/api/auth/refresh`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken }),
    cache: 'no-store',
  })
    .then(async (response) => {
      // 400/401 means the token is expired, revoked or already consumed.
      if (response.status === 400 || response.status === 401) return null;
      if (!response.ok) throw new Error('Transient refresh failure');
      return (await response.json()) as AuthTokens;
    })
    .finally(() => {
      refreshInFlight.delete(refreshToken);
    });

  refreshInFlight.set(refreshToken, request);
  return request;
}
```

Rules:

- **Persist the rotated pair immediately.** The old refresh token is invalid
  after a successful refresh.
- **`400`/`401` from refresh is terminal.** Clear the session and redirect to
  login; retrying the same token cannot work.
- **Network errors and `5xx` are transient.** Keep the session and retry, unless
  the refresh token has also expired. Do not sign the user out on a temporary
  API outage.
- **Single-flight is per process.** The existing `dfw_frontend` implementation
  stores its refresh state on `globalThis` so route handlers in one Next.js
  server process share it. If you run multiple Next.js instances, add a
  distributed lock (for example Redis) or sticky sessions; otherwise two
  instances can consume the same refresh token and one will receive `401`.
- If a refresh returns `401` while another tab/process may have already rotated
  the token, re-read the session cookie once before giving up. Do not loop
  refreshing the same token.

## Calling protected endpoints

Send the access token as a bearer token:

```ts
const response = await fetch(`${API_BASE_URL}/api/users`, {
  headers: {
    Accept: 'application/json',
    Authorization: `Bearer ${accessToken}`,
  },
  cache: 'no-store',
});
```

### Next.js route-handler proxy

For the existing admin shell, protected browser requests go to same-origin
route handlers, and the route handler adds the bearer token:

```ts
import { NextResponse } from 'next/server';
import { getBackendAccessToken } from '@repo/auth-next/server';

export async function GET() {
  const accessToken = await getBackendAccessToken();

  if (!accessToken) {
    return NextResponse.json(
      { error: 'Authentication required. Please sign in again.' },
      { status: 401 },
    );
  }

  const upstream = await fetch(`${process.env.API_BASE_URL}/api/users`, {
    headers: {
      Accept: 'application/json',
      Authorization: `Bearer ${accessToken}`,
    },
    cache: 'no-store',
  });

  return new NextResponse(await upstream.text(), {
    status: upstream.status,
    headers: {
      'Content-Type':
        upstream.headers.get('content-type') ?? 'application/json',
      'Cache-Control': 'no-store',
    },
  });
}
```

`getBackendAccessToken()` in the existing package refreshes the pair when the
access token is near expiry and persists the rotated values back into the
Auth.js session. The browser never sees the refresh token.

### Direct browser requests

If the browser calls the API directly, `Authorization` is allowed by the API's
CORS policy. Do not send cookies; the API does not use them for auth and CORS is
configured without credentials. If the frontend is served over HTTPS, calling an
HTTP API from the browser will be blocked as mixed content, so use HTTPS for the
API or proxy through your server.

The stock `@repo/api-contracts` client does not add an `Authorization` header.
If you use it directly, wrap `fetchImpl`:

```ts
const api = createApiClient({
  baseUrl: API_BASE_URL,
  fetchImpl: (url, init) => {
    const headers = new Headers(init?.headers);
    headers.set('Authorization', `Bearer ${accessToken}`);
    return fetch(url, { ...init, headers });
  },
});
```

This is only appropriate for a protected direct-client setup. Hosted remotes
should keep using the shell's same-origin proxy.

## Logout

Send the refresh token to the API, then clear the client session regardless of
the API result:

```ts
export async function logout(refreshToken: string) {
  try {
    await fetch(`${API_BASE_URL}/api/auth/logout`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken }),
      cache: 'no-store',
    });
  } catch {
    // Best effort: the local session must still be cleared.
  }
}
```

Important:

- Logout returns `204` even for an unknown/expired refresh token.
- It revokes **only** the refresh token. The access token remains valid until
  its `expiresAt`; clear it from the client and keep its lifetime short.
- If the API is down, still clear the local Auth.js/cookie session. The next
  login will issue a new pair.

## Error handling

| Status | Meaning | Frontend action |
| --- | --- | --- |
| `400` | Validation failure | Show `errors` field messages next to inputs. |
| `401` | Bad credentials, missing/invalid access token, or invalid/expired/consumed refresh token | For login, show invalid credentials. For API calls, try one refresh; if refresh also fails, sign out. |
| `403` | Authenticated but role/permission denied | Show the unauthorized page. Do not retry. |
| `404` | Resource not found | Show a not-found state. |
| `409` | Duplicate email or other conflict | Show the server message. |
| `5xx` | API failure | Show a retryable error; keep the session. |

The API returns `application/problem+json` for `ProblemDetails` and
`ValidationProblemDetails`. Parse in this order:

```ts
const body = await response.json().catch(() => null);
const message =
  body?.errors
    ? Object.entries(body.errors)
        .flatMap(([field, messages]) =>
          (messages as string[]).map((m) => `${field}: ${m}`),
        )
        .join(' ')
    : body?.detail ?? body?.title ?? `Request failed (${response.status})`;
```

Some metrics validation errors return `{ "error": "..." }` instead of
`ProblemDetails`; handle `body.error` as well.

## Existing `dfw_frontend` apps

| App | Auth path | Important setting |
| --- | --- | --- |
| `admin-shell` | `@repo/auth-next` credentials provider; browser calls same-origin `/api/*` routes; route handlers add the bearer token | `API_BASE_URL` must point at the DFW API |
| `web-shell` | Same `@repo/auth-next` flow | Set `API_BASE_URL` explicitly; do not rely on the development fallback in production |
| `users-remote` hosted in the shell | Calls the shell's same-origin proxy, so Auth.js attaches the bearer token | No `VITE_API_BASE_URL` needed in hosted mode |
| `users-remote` standalone | `createApiClient` calls `VITE_API_BASE_URL` directly, but the client does **not** add an `Authorization` header | Protected backend calls return `401`; use mock data or add login/token handling |

The standalone `users-remote` case is a common false positive: setting
`VITE_API_BASE_URL=http://localhost:5248` makes the remote hit the real API, but
without a bearer token every `/api/users` request is rejected. The API is
protected; direct calls need an access token.

## RBAC gotcha: `admin:access` is not a wildcard

The backend gives both `admin` and `manager` the `admin:access` permission.
`admin:access` means "can enter the admin shell"; it does **not** grant
`user:write` or `product:write`.

This matters if you reuse the current `@repo/rbac` helper:

```ts
// In dfw_frontend/packages/rbac/src/permissions.ts
if (permissions.includes('admin') || permissions.includes('admin:access'))
  return true;
```

That branch treats `admin:access` as "has every permission". A manager has
`admin:access`, so a check such as
`canWithPermissions('user:write', managerPermissions)` incorrectly returns
`true`, while the backend correctly returns `403`.

For writes, check the exact permission instead:

```ts
const canWriteUsers = permissions.includes('user:write');
const canWriteProducts = permissions.includes('product:write');
```

If you keep the helper, remove the `admin:access` short-circuit or rename the
permission to something like `admin:shell` so it cannot be confused with a
superuser flag.

Current backend permissions:

| Permission | admin | manager | viewer |
| --- | --- | --- | --- |
| `admin:access` | yes | yes | no |
| `user:read` | yes | yes | yes |
| `user:write` | yes | no | no |
| `product:read` | yes | yes | yes |
| `product:write` | yes | yes | no |

## Troubleshooting checklist

| Symptom | Likely cause | Fix |
| --- | --- | --- |
| "Could not reach the authentication API" | API not running or wrong `API_BASE_URL` | Start the API and check `http://localhost:5248/health`. |
| Login always fails in one Next app | `API_BASE_URL` missing in that app | Set it for both `admin-shell` and `web-shell`; default only applies in development. |
| Standalone `users-remote` shows "Authentication required" or `401` | `VITE_API_BASE_URL` points at the protected API, but the remote's `createApiClient` sends no bearer token | Clear `VITE_API_BASE_URL` to use mocks, or add login/token handling to standalone mode. Hosted mode uses the shell proxy. |
| `401` on a protected API call | Missing/expired access token, or refresh token sent as bearer | Send `Authorization: Bearer <accessToken>`. Refresh first if near expiry. |
| Refresh works once, then returns `401` | Old refresh token reused, or concurrent refreshes raced | Persist the new token and use single-flight refresh. |
| Manager sees admin-only buttons and gets `403` | `admin:access` treated as a wildcard | Gate writes with `user:write` / `product:write`. |
| Login works but redirects loop | `AUTH_URL`/`NEXTAUTH_URL` does not match the app URL | Set it to the exact origin, including port. |
| Logout succeeds but the old API call still works | Access tokens are stateless | Expected; clear client state and keep access-token TTL short. |
| Refresh tokens disappear after an API restart | `Cache:Provider=Memory` | Use Redis for restart/multi-instance persistence, or log in again. |
| Browser direct calls fail after deployment | HTTPS page calling HTTP API, or proxy missing | Serve the API over HTTPS or use the server-side proxy. |
| `expiresAt` comparison is off | Parsed as a local time or compared to the wrong clock | Use `Date.parse(expiresAt)` and refresh with a 60-second skew. |

## Minimum integration test

1. `POST /api/auth/login` with `john@example.com` / `Passw0rd!`; verify all four
   top-level fields and `user.permissions`.
2. Call `GET /api/auth/me` with `Authorization: Bearer <accessToken>`; verify
   `200` and the same user.
3. Call `POST /api/auth/refresh` with the refresh token; verify a different
   refresh token and a new access token.
4. Reuse the old refresh token; verify `401`.
5. Call `POST /api/auth/logout` with the new refresh token; verify `204`.
6. Try the new refresh token again; verify `401`.
7. Log in as `bob@example.com` (viewer) and verify `POST /api/users` returns
   `403`.
8. Log in as `jane@example.com` (manager) and verify `POST /api/users` returns
   `403` while `POST /api/products` succeeds.
