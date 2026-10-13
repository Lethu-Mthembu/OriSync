# Attendance

Feature 8 gives mentors a group-scoped QR scanner and explicit manual
Present/Absent corrections. Only today's attendance can be changed, and only
during the active orientation's configured weekday hours.

## Daily rows

Every currently assigned student has one attendance row per orientation day.
The default is `Absent` with capture method `Automatic`. Because a sleeping
Render Free service cannot run reliably at midnight, rows are materialized
atomically when a mentor or administrator first opens that day's directory or
when attendance is first marked. A PostgreSQL `ON CONFLICT DO NOTHING` guard
prevents concurrent mentors from creating duplicate rows.

The visible result is still a day boundary: after midnight in
`Africa/Johannesburg`, the 15-second directory refresh loads the new day's
Absent statuses. Previous dated rows are never reset or overwritten.

## QR scanner

The dedicated mentor scanner uses `BrowserQRCodeReader` from
`@zxing/browser`, which decodes QR codes only. It requests the rear camera with
`facingMode: environment`, waits roughly 500 milliseconds between unsuccessful
decode attempts and stops the camera immediately after finding a code.

The scanner and ZXing are emitted as a separate Vite bundle. The dashboard
quietly preloads that bundle while the browser is idle and also preloads it when
the mentor hovers over, focuses or touches the scanner button. Clicking the
button starts the rear-camera request at the same time as any remaining module
download. Fingerprinted `/assets` responses use a one-year immutable browser
cache, so an unchanged scanner bundle is not downloaded again on later visits.

Only the decoded URL is posted to `POST /api/mentor/attendance/scan`. Camera
frames never leave the phone. The backend then:

1. validates the URL structure and HMAC signature;
2. verifies the QR version and every signed student field against PostgreSQL;
3. confirms that the student currently belongs to the mentor's group;
4. confirms that the server's Johannesburg date and time are inside the active
   orientation calendar and attendance hours; and
5. changes today's row to `Present` with capture method `Qr`.

A successful scan displays the student's name for five seconds. Duplicate,
expired, invalid and wrong-group scans show a result dialog and do not write a
second row. Scanning resumes only after the result dialog closes. Closing or
navigating away from the scanner stops both ZXing controls and all active
camera tracks. The scanner page remains mounted while successive students are
processed; it requests a fresh rear-camera stream after each stopped scan.

When Android backgrounds the browser, OriSync immediately stops the active
camera tracks so another camera or QR application can use the hardware. Returning
to OriSync requests a fresh stream. If Android has not released the camera yet,
the scanner identifies the camera as busy and allows the mentor to retry; it does
not incorrectly describe that condition as a permission failure.

Some Android phones expose several rear lenses and fail when `facingMode`
selects an unusable lens. After a startup failure, the scanner enumerates the
available video inputs, prefers a device labelled as rear-facing and lets the
mentor explicitly choose another camera. The underlying browser error code is
shown for diagnosis. Camera labels and device identifiers remain in the browser
and are not sent to the OriSync backend.

## Manual correction

`PUT /api/mentor/attendance/{enrollmentId}` accepts either `Present` or
`Absent`. Both actions require a browser confirmation dialog. The student must
currently belong to the authenticated mentor's group, and the same calendar
and operating-hour restrictions apply. The latest accepted manual submission
overwrites the current status and records capture method `Manual`.

## Synchronization

The mentor directory updates its own manual changes immediately and refreshes
from the server every 15 seconds so changes made by another mentor appear
without reloading the page. PostgreSQL remains authoritative.
