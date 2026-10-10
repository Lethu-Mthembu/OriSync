# Database schema

OriSync uses thirteen PostgreSQL tables. Entity Framework Core owns the migration
history; generated register PDFs are never stored in the database.

| Table | Responsibility |
| --- | --- |
| `orientations` | Annual orientation dates, attendance hours, timezone and retention deadline |
| `groups` | Colour-named groups scoped to one orientation |
| `people` | Shared admin, mentor and student identity, including the student demographic fields |
| `person_emails` | Student, personal and login email addresses with database-wide uniqueness |
| `accounts` | Admin and mentor credentials, status and the mentor's current group |
| `mentor_invitations` | Durable single-use mentor activation tokens, delivery attempts and retention state |
| `student_enrollments` | A student's orientation membership, current or pending group and current QR version |
| `attendance_records` | One attendance row per enrolled student and date, with a historical group snapshot |
| `sessions` | Hashed server sessions, activity and revocation state |
| `password_reset_otps` | Hashed mentor recovery codes, delivery state, expiry and failed attempts |
| `password_reset_email_outbox` | Durable, retryable Resend work without plaintext OTP storage |
| `audit_events` | Short-lived records for sensitive changes, expiring after the configured 14-day period |
| `data_protection_keys` | Shared ASP.NET Core key ring for antiforgery and protected server payloads |

## Enforced invariants

- Institution numbers are text, match `^2[0-9]{8}$` and are unique across both
  mentors and students because both use `people.institution_number`.
- Student demographic fields are mandatory for a person whose type is
  `Student`: initials, age group, gender and ethnicity.
- Age group is limited to C1-C5; gender is Male, Female or Other; ethnicity is
  White, African, Coloured, Indian or Other.
- Normalized email addresses are unique across student, personal and login
  email types.
- Only one orientation may be active at a time. Activation is explicit and an
  orientation must have at least one active group before it can be activated.
- Orientation dates must fall in the selected year. The operating calendar
  includes weekdays only; it deliberately does not exclude public holidays.
- Group names are stored in uppercase and are case-insensitively unique inside
  an orientation. Badge colours use six-digit hexadecimal values.
- An unused group may be deleted. Once a mentor, student enrollment or
  attendance row references it, its name is locked and it can only be
  deactivated. A referenced group's badge colour remains editable.
- A student has at most one enrollment in an orientation.
- Current and pending student groups must belong to the enrollment's
  orientation.
- Attendance has exactly one row per enrollment and calendar date. Its group
  foreign key is a historical snapshot constrained to the same orientation.
- Only one admin account can exist.
- Only one unrevoked session can exist per account; creating a replacement
  session must revoke the old session in the same transaction.

Some cross-table rules cannot be expressed as ordinary PostgreSQL constraints.
The application services implemented in later features must still verify that
an account's role matches its person type, that student email types are present,
and that attendance dates fall inside the configured weekday calendar.

The orientation-management service additionally prevents deactivating a group
while an active mentor or current/pending student assignment still points to it.
After attendance exists, the orientation year, start date and opening time are
locked. Future end dates and the closing time remain correctable while the
orientation has not ended; historical dates cannot be rewritten.

## Neon boundary

All tables have PostgreSQL row-level security enabled and no browser-client
policies. The React application must never connect to Neon directly. The ASP.NET
Core backend uses the private database connection and is the sole data-access
boundary.

## Applying the migration

Store the PostgreSQL connection string outside source control and run:

```powershell
$env:ConnectionStrings__OriSync = '<postgresql-connection-string>'
dotnet tool restore
dotnet ef database update --project src/OriSync.Api --startup-project src/OriSync.Api
```

For Render, use the Neon pooled connection for normal application traffic and a
direct connection for schema migrations when available. Require TLS in both
connection strings.
