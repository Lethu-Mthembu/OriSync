# Authentication and sessions

## Boundary

OriSync uses same-origin, database-backed authentication. React never receives a
bearer token and never accesses PostgreSQL directly. The browser receives only
an opaque `__Host-OriSync.Session` cookie with these properties:

- `HttpOnly`
- `Secure`
- `SameSite=Strict`
- `Path=/`
- no `Domain` attribute

Only the SHA-256 hash of the opaque token is stored in `sessions`. Passwords are
hashed with ASP.NET Core's versioned `PasswordHasher<Account>` format.

All state-changing authentication endpoints also require the antiforgery token
returned by `GET /api/auth/csrf` in the `X-CSRF-TOKEN` header. The antiforgery
cookie is also host-only, secure, HttpOnly and strict same-site. Cross-origin
API access is not enabled; the React application and API must remain on the
same origin.

ASP.NET Core Data Protection keys are stored in PostgreSQL under the fixed
application name `OriSync`. Antiforgery tokens therefore survive Render restarts,
idle spin-downs and deployments instead of depending on the container's
ephemeral filesystem. Render's edge proxy is trusted for one forwarded protocol
hop only; forwarded client IP addresses are ignored.

## Session rules

- A successful login revokes the account's previous active session.
- A session expires after 30 minutes without recorded user interaction.
- A session expires eight hours after login regardless of activity.
- Background reads and polling do not update `last_seen_at`.
- The browser posts `/api/auth/activity` only after pointer, keyboard or touch
  interaction and at most once per minute.
- Password changes, password resets, account disabling and later mentor-group
  moves revoke active sessions.
- Accounts with a temporary password are blocked from operational API routes
  until the password is changed.

## Endpoints

| Method | Route | Authentication | Purpose |
| --- | --- | --- | --- |
| GET | `/api/auth/csrf` | Anonymous | Create an antiforgery request token |
| POST | `/api/auth/login` | Anonymous | Sign in with login email and password |
| GET | `/api/auth/session` | Required | Restore the current account summary |
| POST | `/api/auth/activity` | Required | Record real user interaction |
| POST | `/api/auth/logout` | Required | Revoke the current session |
| POST | `/api/auth/change-password` | Required | Change password and revoke all sessions |
| POST | `/api/auth/reset-mentor-password` | Anonymous | Reset a mentor using the mentor number |
| POST | `/api/auth/recover-admin` | Anonymous | Recover the sole admin using a one-time code |

Every POST requires `X-CSRF-TOKEN`, including anonymous POSTs.

## Accepted security exception

Mentor password reset deliberately requires only a nine-digit mentor number and
a new password. It has no email verification, MFA, lockout or rate limit. Anyone
who learns a mentor number can take over that account. Hiding mentor numbers from
mentor-facing responses reduces exposure but does not make this reset secure.
This is an explicitly accepted product risk, not a recommended authentication
design.

## One-time admin bootstrap

No credentials are committed. From a trusted machine with the production
database connection configured, set temporary process environment variables and
run:

```powershell
$env:ConnectionStrings__OriSync = '<ADO.NET PostgreSQL connection string>'
$env:Bootstrap__AdminEmail = '<admin email>'
$env:Bootstrap__AdminPassword = '<password with at least 8 characters>'
$env:Bootstrap__AdminFirstName = '<first name>'
$env:Bootstrap__AdminSurname = '<surname>'

dotnet run --project src/OriSync.Api -- --bootstrap-admin
```

The command refuses to run if an admin already exists. It prints a random admin
recovery code once and stores only its hash. Remove the temporary environment
variables after the command and store the recovery code outside OriSync.

If the recovery code is lost, a developer with database access can rotate it:

```powershell
$env:ConnectionStrings__OriSync = '<ADO.NET PostgreSQL connection string>'
$env:Bootstrap__AdminEmail = '<admin email>'

dotnet run --project src/OriSync.Api -- --rotate-admin-recovery
```

Rotation revokes the admin's active session and prints the replacement code
once.

## Database migration

`AddAuthenticationRecovery` adds the optional admin recovery-code hash and its
issue time to `accounts`. `PersistDataProtectionKeys` adds the shared Data
Protection key ring. Apply migrations before enabling the authentication UI
against an existing database:

```powershell
dotnet ef database update --project src/OriSync.Api --startup-project src/OriSync.Api
```
