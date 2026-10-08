# Orientation and group management

Feature 4 gives the administrator a settings workspace for yearly orientation
calendars and colour groups. Mentors cannot access these endpoints.

## Orientation rules

- More than one future, inactive orientation may exist, but years are unique.
- A blank name is generated as `First Year Orientation {year}` and remains
  editable.
- Start and end dates must fall inside the selected year and contain at least
  one weekday.
- Operating dates include Monday to Friday. Public holidays are intentionally
  ignored.
- Attendance hours use `Africa/Johannesburg` and must have an opening time
  before the closing time.
- Activation is manual, requires at least one active group, and atomically
  replaces the previously active orientation. The administrator may also leave
  the system with no active orientation.
- The retention deadline is midnight in Johannesburg exactly six calendar
  months after the end date.

Once attendance exists, the year, start date and attendance opening time are
locked. A future end date may still be corrected provided it does not exclude
existing attendance, and the closing time may be corrected until the
orientation has ended. This preserves historical registers but means an
incorrect historical configuration requires a deliberate data-repair process,
not a settings-page edit.

## Group rules

- Names are stored in uppercase and are unique within one orientation.
- Recognised colour names receive a standard badge colour automatically.
  Unrecognised names require an explicit six-digit hexadecimal colour.
- Unused groups may be renamed or deleted.
- Once referenced by a mentor, student enrollment or attendance row, the name
  is immutable and deletion is rejected. The badge colour remains editable.
- A group cannot be deactivated while an active mentor or current/pending
  student assignment points to it. Those assignments must be moved or removed
  first.

Keeping referenced groups instead of deleting them adds inactive records to the
database and admin interface. That is intentional technical debt: historical
attendance needs a stable group identity.

## Administrative API

All routes require an authenticated `Admin` role and a valid CSRF token.

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/admin/orientations` | List orientations and groups |
| `POST` | `/api/admin/orientations` | Create an inactive orientation |
| `PUT` | `/api/admin/orientations/{id}` | Edit allowed settings |
| `POST` | `/api/admin/orientations/{id}/activate` | Make one orientation active |
| `POST` | `/api/admin/orientations/{id}/deactivate` | Leave it inactive |
| `POST` | `/api/admin/orientations/{id}/groups` | Add an active group |
| `PUT` | `/api/admin/orientations/{id}/groups/{groupId}` | Edit allowed group fields |
| `POST` | `/api/admin/orientations/{id}/groups/{groupId}/activate` | Reactivate a group |
| `POST` | `/api/admin/orientations/{id}/groups/{groupId}/deactivate` | Deactivate an unused group |
| `DELETE` | `/api/admin/orientations/{id}/groups/{groupId}` | Delete a never-used group |

The database enforces the single-active-orientation rule with a partial unique
index. Activation also uses a serializable transaction so two concurrent admin
requests cannot leave multiple active orientations.
