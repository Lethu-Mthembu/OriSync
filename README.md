# OriSync

OriSync is a progressive web application for tracking first-year orientation
attendance at Tshwane University of Technology.

## Repository status

The project is being delivered as tested vertical features. Each feature is
implemented on a `feature/*` branch and merged into `dev` after review. Render
tracks `dev`; `main` remains the production release branch.

The current foundation contains:

- an ASP.NET Core 10 API;
- a React 19 and TypeScript client built with Vite;
- backend and frontend automated tests;
- a multi-stage production container;
- a Render Blueprint that deploys the `dev` branch; and
- a health endpoint at `/health`.

## Prerequisites

- .NET SDK 10.0.400 or a compatible 10.0 feature-band update
- Node.js 24 LTS
- npm 11+
- Docker 27+ for container verification

Node.js 25 is unsupported and must not be used for project builds.

## Local development

Start the API:

```powershell
dotnet run --project src/OriSync.Api
```

Start the React development server in a second terminal:

```powershell
Set-Location src/OriSync.Web
npm install
npm run dev
```

Vite proxies `/api` and `/health` to the API at `http://localhost:5211`.

## Verification

```powershell
dotnet restore OriSync.sln
dotnet build OriSync.sln --configuration Release --no-restore
dotnet test OriSync.sln --configuration Release --no-build

Set-Location src/OriSync.Web
npm ci
npm run lint
npm run test
npm run build
```

Build the production container from the repository root:

```powershell
docker build --tag orisync:dev .
```

The container reads Render's `PORT` variable and otherwise listens on port
`10000`.

## Deployment

`render.yaml` defines the `orisync-dev` Docker web service. It tracks `dev`,
deploys after every commit to that branch and checks `/health` before accepting
the release. Runtime secrets will be added only when the relevant features are
implemented; secrets must never be committed to this repository.

See [the implementation plan](docs/implementation-plan.md) for the feature
sequence and branch policy.
