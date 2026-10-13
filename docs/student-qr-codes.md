# Student QR codes

Feature 7 generates a student's QR image when a mentor requests it. PNG files
are returned to the browser and are never stored in PostgreSQL or on the web
service filesystem.

## Access and endpoints

Only an authenticated, active mentor can use these endpoints, and only for a
student whose current group matches the mentor's current group:

- `GET /api/mentor/students/{enrollmentId}/qr` returns an inline PNG preview.
- `GET /api/mentor/students/{enrollmentId}/qr/download` returns the same bytes
  as an attachment named `{studentNumber}-qr.png`.

Incoming next-day transfers are not current members of the destination group,
so destination mentors cannot download their QR until the transfer becomes
effective. Both responses use `private, no-store` caching rules because the
image contains personal data.

## Signed URL

The QR encodes an HTTPS URL whose fragment contains a Base64URL-encoded JSON
payload and an HMAC-SHA256 signature. The payload fields are:

- `studentNumber`
- `fullNames`
- `surname`
- `studentEmail`
- `version`

Personal email is deliberately excluded. A URL fragment is used so browsers do
not send the student payload in normal HTTP requests or Render access logs. The
QR itself still exposes those fields to anyone who scans it; this is required
product behaviour, not confidentiality.

The database remains authoritative. Feature 8's scanner must verify the
signature, find the student by number and reject the QR when its version or
signed details no longer match the current record. Any student-detail edit,
including a personal-email-only edit, increments `QrVersion`. Group movement
does not increment it.

## Image contract

Every PNG is deterministic for an unchanged record:

- 1024 × 1024 pixels
- white background and black QR modules
- medium error correction
- at least four quiet modules
- the student's full names and surname below the QR
- no student number, email, institution logo or group colour in the readable
  label

The app bundles Noto Sans under the SIL Open Font License so rendering does not
depend on host-installed fonts. QRCoder creates the matrix and the MIT-licensed
SkiaSharp runtime draws the PNG on Windows and Linux.

## Configuration

`QrCodes:PublicBaseUrl` is the public app origin. Non-development environments
require an HTTPS origin without credentials, a path, a query or a fragment.

`QrCodes:SigningKey` is a Base64-encoded secret containing at least 32 random
bytes. Render creates it once through `generateValue: true`; it must not be
committed or casually replaced. Rotating this value immediately invalidates
every existing student QR and therefore requires all QR images to be
redistributed.
