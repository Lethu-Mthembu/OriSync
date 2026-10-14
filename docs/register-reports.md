# Register reports

Feature 10 generates printable attendance registers directly from stored daily
attendance rows. Generated files are returned to the browser and are never
stored by OriSync.

## Access and selection

Mentors can download only the current date for their authenticated group. The
attendance filter on the student directory controls whether the PDF contains
`All`, `Present` or `Absent` rows. The button is disabled when that selection
has no daily register rows.

Administrators can open the Registers workspace and select:

- any orientation still retained in the database;
- any non-future weekday in that orientation's configured date range;
- `All`, `Present` or `Absent`.

The selected day displays one table row per orientation group with total,
present, absent and filter-matching counts. Each non-empty row has its own PDF
download. A separate button downloads one ZIP for every group with matching
rows.

An all-groups ZIP contains one PDF for each group that has at least one row
matching the selected filter. Empty group files are omitted. If the entire
selection is empty, download is disabled and the backend also rejects direct
download attempts.

Historical reports use the group stored on each attendance row. A later group
transfer therefore does not move the earlier row to the destination group.
The backend does not fabricate missing historical rows because the current
student assignment is not reliable evidence of a past assignment. Current-day
requests materialize missing `Absent` rows before counting or generating files.

## PDF format

Every PDF uses A4 landscape dimensions and repeats the following header on each
page:

- the supplied Tshwane University of Technology emblem;
- `First Year Orientation Attendance Register`;
- the group name; and
- the attendance date in `dd/MM/yyyy` format.

Rows are ordered by student number ascending. The columns are:

1. initials and surname, formatted as `JM Doe`;
2. student number;
3. age group;
4. gender;
5. ethnicity; and
6. the actual attendance status, `Present` or `Absent`.

Student email addresses, mentor student numbers, capture methods and timestamps are not
printed. Long names are reduced to a smaller font within their cells rather
than allowed to overlap adjacent columns.

## File names

Group PDFs follow:

`Red-2027-02-01-14h30-Present.pdf`

All-group archives follow:

`OriSync-2027-02-01-14h30-Present.zip`

The timestamp is the generation time in the orientation timezone. It appears
only in the filename, not in the document.

## APIs

- `GET /api/mentor/registers/today/summary`
- `GET /api/mentor/registers/today`
- `GET /api/admin/registers/summary`
- `GET /api/admin/registers/groups`
- `GET /api/admin/registers/pdf`
- `GET /api/admin/registers/zip`

All responses are role protected. File responses use `private, no-store` cache
headers. No schema migration is required.
