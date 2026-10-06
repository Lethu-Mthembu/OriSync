# OriSync implementation plan

## Delivery rule

Every feature starts from the latest accepted `dev` branch. A feature is built,
tested and pushed on its own branch, then merged into `dev`. Render automatically
deploys accepted `dev` commits. Features are not stacked on unmerged branches.

## Feature sequence

| Order | Branch | Scope |
| --- | --- | --- |
| 1 | `feature/project-foundation` | API, client, tests, container, CI and Render development deployment |
| 2 | `feature/database-schema` | PostgreSQL model, constraints, indexes and EF Core migrations |
| 3 | `feature/authentication-sessions` | Admin and mentor authentication, secure cookies, CSRF and session rules |
| 4 | `feature/orientation-groups` | Orientation configuration, operating calendar and groups |
| 5 | `feature/mentor-management` | Mentor accounts, assignments, moves, disabling and directory |
| 6 | `feature/student-management` | Student onboarding, demographics, editing, removal and transfers |
| 7 | `feature/student-qr-codes` | Signed QR URLs, versioning and PNG downloads |
| 8 | `feature/attendance` | Scanning, manual correction, daily status and synchronization |
| 9 | `feature/dashboards-directories` | Role dashboards, metrics, search, filters and pagination |
| 10 | `feature/register-reports` | Current and historical registers, PDFs and ZIP exports |
| 11 | `feature/pwa-responsive-ui` | Installable PWA, TUT visual system and mobile mentor experience |
| 12 | `feature/data-retention` | Six-month automated operational-data deletion |
| 13 | `feature/production-deployment` | Production hosting, secrets, monitoring and release hardening |

## Merge gate

A feature may be merged into `dev` only when:

1. backend compilation and tests pass;
2. frontend linting, tests and production build pass;
3. the container builds successfully when deployment files change;
4. no secrets or generated build output are committed; and
5. documentation reflects any new operational requirement.
