# PWA and responsive UI

Feature 11 provides a responsive application shell without changing OriSync's
role permissions or making attendance available offline.

## Navigation

Both roles land on `Dashboard` after authentication. On desktop, the sidebar is
an icon rail that expands on hover or keyboard focus and can be pinned open. On
phones, a visible menu button opens the same navigation as a modal drawer.

Administrators receive:

- Dashboard;
- Students;
- Mentors;
- Registers; and
- Orientations and groups.

Mentors receive Dashboard and Students. The mentor dashboard puts the primary
`Scan attendance` action before the group's live metrics. Student search,
onboarding, editing and removal remain in Students. Change password and sign
out remain in the sidebar account area for both roles.

## Responsive data lists

Student, mentor and register tables share the same bordered table surface,
header styling, status badges, hover feedback and reduced-motion support.
Existing server-side filters and pagination remain authoritative. Columns are
fixed rather than user-configurable.

The mentor student directory switches to stacked cards at phone widths. Admin
tables remain horizontally scrollable where their operational columns cannot
fit without hiding data. The supported viewport range remains 360 px through
1440 px, with no separate tablet-only layout.

## Installation and caching

`manifest.webmanifest` declares the standalone OriSync application, theme and
icons. The production client registers `sw.js`; development deliberately does
not, which prevents a stale service worker from interfering with local work.

The service worker caches only the application shell and same-origin static
assets. It explicitly ignores `/api` and `/health` requests. Navigation is
network-first and falls back to the cached shell only when the network cannot
be reached. Authentication, attendance, student management, report generation
and every other API operation therefore continue to require the live backend.
