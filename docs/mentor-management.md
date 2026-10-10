# Mentor management

Feature 5 provides administrator-only mentor administration and public,
token-protected activation. Mentor numbers are returned only by administrator
endpoints.

## Invitation flow

1. The administrator selects an active group in the active orientation and
   enters one email address.
2. OriSync stores a hashed activation token plus a Data Protection-encrypted
   delivery copy. The raw token is never stored.
3. The durable dispatcher sends the link through Resend. Its 24-hour validity
   begins only after successful delivery; retries use a stable idempotency key.
4. The mentor supplies first name, surname, a unique nine-digit mentor number
   matching `^2[0-9]{8}$`, and a compliant password. The invited email is locked.
5. Acceptance is transactional and single use. It creates the mentor account in
   the invitation's current group.

Resending rotates the token and invalidates the old link. An administrator may
change the invitation group before acceptance or cancel the invitation. If the
group or orientation is no longer active, acceptance is rejected until the
invitation is reassigned. Terminal invitation records are retained for 14 days.

## Directory and lifecycle

The centralized directory defaults to 20 rows, with 50 and 100-row options. It
supports search plus group and status filters and combines mentor accounts with
pending, delivered, expired, failed and recently cancelled invitations.

An administrator can edit a mentor's name, surname, mentor number, login email
and active group. Identity changes revoke active sessions. Disabling also
revokes the session immediately but deliberately preserves the group. A disabled
mentor can be reactivated only while that group remains active in the active
orientation. Permanent deletion is rejected until the account has remained
disabled for six months; related attendance history retains the row and clears
only its optional `marked_by_account_id` reference.

## Endpoints

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/admin/mentors` | Filtered and paginated directory |
| `POST` | `/api/admin/mentor-invitations` | Queue an email/group invitation |
| `PUT` | `/api/admin/mentor-invitations/{id}/group` | Reassign an invitation |
| `POST` | `/api/admin/mentor-invitations/{id}/resend` | Rotate and resend its link |
| `POST` | `/api/admin/mentor-invitations/{id}/cancel` | Cancel an invitation |
| `PUT` | `/api/admin/mentors/{id}` | Edit identity, email and group |
| `POST` | `/api/admin/mentors/{id}/disable` | Disable and revoke sessions |
| `POST` | `/api/admin/mentors/{id}/reactivate` | Reactivate an eligible mentor |
| `DELETE` | `/api/admin/mentors/{id}` | Delete after six months disabled |
| `POST` | `/api/auth/mentor-activation/validate` | Validate a public activation link |
| `POST` | `/api/auth/mentor-activation/accept` | Create the invited mentor account |

All mutating endpoints require antiforgery protection. Administrator routes also
require the Admin role. Tokens are placed in the URL fragment so browsers do not
send them in HTTP request targets or referrer headers; the UI removes the
fragment from the address bar immediately after reading it.
