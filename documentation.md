# EventPilot API Documentation

EventPilot is a .NET 10 API for event discovery, event management, and seat registration.
The MVP uses Clean Architecture, MediatR, FluentValidation, SQL Server/EF Core,
ASP.NET Core Identity and JWT, xUnit, Serilog, OpenTelemetry, and optional Redis caching.

The [complete project handbook](docs/PROJECT-HANDBOOK.md) covers architecture, entities,
business rules, all API endpoints, configuration, concurrency, caching, monitoring,
testing, and manual releases.

## Role system

The MVP has two roles, stored in Identity's existing role and membership tables:

| Role | Permissions |
|---|---|
| User | Browse events, manage own profile, register/cancel own attendance, and view own registration history |
| Organizer | Retains User capabilities; can create events, list own events, edit/delete own events, and view own events' attendees |

An Admin role is not introduced. No public endpoint accepts an arbitrary role assignment.
Anonymous visitors can still browse published events and view public event details.

### Signup and organizer enrollment

Both `POST /api/auth/register` and the compatibility route `POST /api/users` assign
User on the server. Signup request fields such as `role`, `roles`, or `isOrganizer`
do not grant permissions. Account creation and default membership are committed together.

To become an organizer in Swagger:

1. Register an account and log in.
2. Paste `data.accessToken` into Swagger's Authorize dialog.
3. Call `POST /api/users/me/organizer` with no request body.
4. After `requiresLogin: true`, log in again. Previous sessions on all devices are revoked.
5. Replace the old Swagger token with the new token.
6. Call `GET /api/users/me` to confirm User and Organizer roles, then create/manage events.

The enrollment response uses the standard API envelope:

```json
{
  "isSuccess": true,
  "message": "Organizer role granted. Log in again to use organizer features; previous sessions were revoked.",
  "data": {
    "roleChanged": true,
    "requiresLogin": true
  },
  "errors": []
}
```

The caller's account is derived from the validated JWT, never a supplied user ID.
Organizer is added alongside User. No manual approval is required for this self-service
MVP. Repeating enrollment with a fresh Organizer token returns both flags as false
and leaves the token valid. A concurrent request may receive 401 or 409; retrieve
fresh authentication/state before retrying.

### Authorization and ownership

The `CanManageEvents` policy requires an authenticated Organizer for:

- `POST /api/events`
- `PUT /api/events`
- `DELETE /api/events/{id}`
- `GET /api/events/mine`
- `GET /api/events/{eventId}/registrations`

For existing-event mutations and attendee lists, the application also checks that
`Event.OrganizerUserId` equals the authenticated user ID. An Organizer cannot manage
another organizer's events. Attendee registration, cancellation, and history require
authentication but do not require Organizer.

Missing/invalid authentication returns 401. A valid User token on an organizer
endpoint returns 403. An Organizer attempting another owner's management operation
also receives 403. Hidden or deleted event details retain the existing 404 rules.

### JWT freshness and role changes

JWTs include Identity role claims. Organizer enrollment changes the security stamp
in the same transaction as membership assignment, invalidating previous tokens.
Each authenticated request also checks that token roles match current SQL membership.
This prevents a removed role from remaining usable through an old JWT, even after a
maintenance change that omitted stamp rotation. Authentication/roles are not cached.

Any future role-changing operation must preserve transactional membership changes,
security-stamp rotation, and these ownership checks. There is currently no public
demotion, admin assignment, or arbitrary role-management API.

### Existing accounts and deployment

Apply `AddUserAndOrganizerRoles` before serving the updated API. It:

1. Creates User and Organizer if absent without assuming fixed role IDs.
2. Adds User to existing accounts.
3. Adds Organizer to existing event owners, including owners of soft-deleted events.
4. Preserves unrelated roles and memberships.
5. Rotates security/concurrency stamps for accounts receiving memberships.

Affected existing accounts must log in again. The ten demo organizers receive both
roles. Pending password-reset links tied to a rotated stamp must be requested again.
The ninety demo attendees receive User. The backfill refuses automatic Down
migration because it cannot safely infer which memberships to remove later. Use a
reviewed data migration or backup restore for role-data rollback.

```powershell
.\.tools\dotnet-ef database update --project EventPilot.Infrastructure --startup-project EventPilot.Web
```

### Implementation map

| Component | Responsibility |
|---|---|
| `Application/Abstractions/Auth/AppRoles.cs` | Canonical role names |
| `Application/Users/Commands/BecomeOrganizer` | Trusted-user enrollment command and response |
| `Infrastructure/Identity/IdentityService.cs` | Atomic signup/enrollment and session revocation |
| `Web/Auth/AuthorizationPolicies.cs` | Organizer policy name |
| `Web/Program.cs` | Policy registration |
| `Web/Auth/JwtAccountValidation.cs` | Account, stamp, and current-role validation |
| `Infrastructure/Migrations/*_AddUserAndOrganizerRoles.cs` | Existing-account backfill |
| `Tests/RoleWorkflowTests.cs` | Signup, role policies, enrollment, races, rollback, and migration tests |

Paths in the map are relative to their corresponding EventPilot projects. See the
[handbook](docs/PROJECT-HANDBOOK.md) for the complete API and operational contract.
