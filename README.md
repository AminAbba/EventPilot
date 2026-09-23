# EventPilot

Event discovery, event management, and seat registration API built with .NET 10,
Clean Architecture, MediatR, EF Core/SQL Server, Identity JWT, and xUnit.

The MVP focuses on API correctness and Swagger workflows. It includes optimistic
capacity concurrency, ownership checks, registration history, structured Serilog
logs, OpenTelemetry traces/metrics, health checks, and optional Redis event caching.
Payments and Razor UI improvements are outside the current scope.

## Documentation

- [Documentation and role system](documentation.md): access rules and organizer enrollment.
- [Complete project handbook](docs/PROJECT-HANDBOOK.md): architecture, business rules,
  API contracts, setup, security, caching, observability, testing, and manual release guidance.

## Quick start

Install the .NET 10 SDK and SQL Server. From the repository root:

```powershell
dotnet restore EventPilot.slnx
$eventPilotKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
dotnet user-secrets set "Jwt:Key" "$eventPilotKey" --project EventPilot.Web
dotnet user-secrets set "App:BaseUrl" "https://localhost:7287/" --project EventPilot.Web
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=.;Database=EventPilotDb;Integrated Security=true;TrustServerCertificate=true" --project EventPilot.Web
dotnet tool install dotnet-ef --tool-path .tools --version 10.0.2
.\.tools\dotnet-ef database update --project EventPilot.Infrastructure --startup-project EventPilot.Web
dotnet dev-certs https --trust
dotnet run --project EventPilot.Web --launch-profile https
```

If `.tools` already contains dotnet-ef, omit installation. Open
`https://localhost:7287/swagger`, register, log in, and paste the returned access
token into Authorize. Readiness is at `/health/ready` and liveness at `/health/live`.

New accounts have the User role. To create/manage events, call
`POST /api/users/me/organizer`, log in again, and replace your Swagger token.
Organizer permissions never bypass event ownership. The role migration preserves
existing owners' Organizer access and requires affected accounts to log in again.

Keep secrets outside source control. Signing/SMTP credentials previously committed
to repository history must remain rotated; removing a value from appsettings does
not remove its history. Live SMTP verification is deferred. The API can run without
Redis or an OTLP collector; enable them as described in the handbook.

## Build and test

```powershell
dotnet build EventPilot.slnx --no-restore
dotnet test EventPilot.Tests/EventPilot.Tests.csproj --no-restore
```

SQL tests use unique disposable databases. Override the SQL connection with
`EVENTPILOT_TEST_SQL`. Set `EVENTPILOT_TEST_REDIS` to include the real Redis test;
otherwise that test is explicitly skipped. Provision the test services manually.

## Optional local services

On a machine with Docker:

```powershell
docker compose -f ops/compose.yml up -d
$env:Cache__Enabled = "true"
$env:ConnectionStrings__Redis = "localhost:6379"
$env:Telemetry__OtlpEndpoint = "http://localhost:4317"
```

Redis and the local telemetry dashboard bind only to loopback. The dashboard UI
is available at `http://localhost:18888`. These containers are development helpers,
not a production deployment.

## Demo data

```powershell
dotnet run --project EventPilot.Web --launch-profile http -- --seed
```

Development-only seeding applies migrations and populates an empty database with
100 users, 250 upcoming published events, and demo registrations. It skips an
already-populated database and exits without starting the server.

Demo accounts: `organizer01@example.com`–`organizer10@example.com` and
`attendee01@example.com`–`attendee90@example.com`. Demo password: `EventPilot2026!`.
Do not use demo accounts or seeding in production.

## Releases

Build, test, publish, and deploy manually using the handbook's release procedure.
No GitHub Actions workflows are included.
