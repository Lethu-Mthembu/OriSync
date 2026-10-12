# Student management

Feature 6 gives mentors a group-scoped student directory and gives the
administrator a read-only directory across the active orientation.

## Student identity

Students are added individually. Each student stores:

- a globally unique nine-digit student number matching `^2[0-9]{8}$`;
- full names in one field and surname in a separate field;
- generated initials from every whitespace-separated full name;
- a globally unique student email and optional globally unique personal email;
- age group `C1` through `C5`;
- gender `Male`, `Female` or `Other`; and
- ethnicity `White`, `African`, `Coloured`, `Indian` or `Other`.

Names are normalized to single spaces and limited to 100 characters. They
accept Unicode letters (including the combining marks required by decomposed
Unicode text), spaces, apostrophes and hyphens. Straight and typographic
apostrophes and the common Unicode hyphen variants are accepted; digits and
other punctuation are rejected.

Student, personal and mentor login email addresses share one uniqueness
boundary. Student numbers and mentor numbers also share one uniqueness boundary
because both are stored on the same person identity table.

## Registration and onboarding

Registration and onboarding are available only on an active orientation
weekday, inclusively between that orientation's attendance opening and closing
times. There is no registration queue outside those hours.

The mentor first performs an exact student-number lookup:

- a new number opens the full registration form;
- an `UNASSIGNED` student requires confirmation before onboarding;
- an assigned student reports the group that owns the identity; and
- a number used by a non-student identity is rejected without exposing that
  person's details.

A new registration or an unassigned student with no attendance record for the
day is immediately assigned to the mentor's group and marked `Present` using
the `Registration` capture method.

## Removal and group changes

Removal is not permanent deletion. It immediately removes the student from the
mentor's directory and leaves the student as `UNASSIGNED`. Today's attendance
remains attached to the group that owned it.

Re-adding the student to the same group on the same date is immediate and
preserves today's status. Onboarding that student into a different group on the
same date creates a locked next-day assignment:

- the destination sees the student immediately with status `—`;
- the source group retains today's attendance;
- destination mentors cannot edit, remove or redirect the student that day;
- the assignment becomes current at 00:00 on the next calendar day; and
- the student then starts the new day as `Absent` until attendance is marked.

Transfer activation is reconciled atomically by the backend on the first
student-directory operation at or after the effective date. This avoids a cron
dependency, but it means the database row is updated lazily rather than by a
process running exactly at midnight. Every API response still observes the
correct effective assignment.

## Editing and concurrency

Any mentor in the current group may edit every student field. Changing any
student detail increments the QR version, so the previous signed QR becomes
invalid after Feature 7 generates the replacement. Group-only movement does
not change the QR version.

Concurrent edits use last-write-wins, as specified. This avoids conflict UI,
but one mentor can silently overwrite another mentor's recent edit. Creation,
editing and removal write audit events retained for 14 days.

## APIs

Mentor endpoints under `/api/mentor/students` provide directory paging, exact
lookup, profile retrieval, creation, onboarding, editing and removal. They
require an authenticated mentor and enforce the mentor's current group on the
server.

The administrator has one read-only endpoint at `/api/admin/students`. It
supports text search, group filtering, an `UNASSIGNED` filter and page sizes of
10, 20, 50 or 100 rows. The default is 10.
