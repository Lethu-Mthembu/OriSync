# Dashboards and directories

Feature 9 adds role-specific landing dashboards and daily-attendance filters to
the existing student directories.

## Dashboard rules

The administrator dashboard shows the active orientation, the local attendance
date, total enrolled students, today's Present and Absent counts, attendance
rate, and active groups ranked by their current student assignments. Ties are
ordered by group name.

The mentor dashboard shows the same daily metrics only for the mentor's current
group. Students whose transfer takes effect tomorrow remain visible in the
destination directory with status `—`, but they are excluded from today's
register totals.

The attendance rate is `Present / (Present + Absent)`. Unassigned students and
future-effective transfers are not daily register rows and therefore do not
reduce the rate. If the daily register has no rows, the rate is `0.0%`.

Both dashboards refresh every 15 seconds. The backend derives the date in the
active orientation's configured timezone and lazily creates missing `Absent`
rows on an orientation weekday before counting. No client-supplied date or
group is trusted.

## Directory filters

Mentor and administrator student directories support:

- student-number or name search;
- `All`, `Present` and `Absent` attendance filters;
- page sizes of 10, 20, 50 or 100, defaulting to 10; and
- student-number ascending order.

The administrator can additionally filter by active group or `UNASSIGNED`.
Filters are executed in PostgreSQL before paging; a page never represents only
a client-side subset. Incoming transfers with status `—` appear only under
`All`.

## APIs

- `GET /api/admin/dashboard` requires the Admin role.
- `GET /api/mentor/dashboard` requires the Mentor role and uses the group from
  the authenticated account.
- `GET /api/admin/students` accepts `attendanceStatus=Present|Absent` in
  addition to the existing search, group, unassigned and paging parameters.
- `GET /api/mentor/students` accepts `attendanceStatus=Present|Absent` in
  addition to the existing search and paging parameters.

No database migration is required for this feature. Existing attendance and
assignment indexes support the queries.
