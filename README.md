# Notification Processing API

A small ASP.NET Core Web API that accepts customer notification requests, validates and stores them in SQL Server, and processes them through a (simulated) email provider. Processing is priority-aware and scheduling-aware, safe to run concurrently, and resilient to provider failures. Duplicate client retries don't create duplicate notifications.

Only **Email** is supported for now, as the exercise asks.

---

## Contents

1. [Technology stack](#technology-stack)
2. [Running the application](#running-the-application)
3. [API reference and examples](#api-reference-and-examples)
4. [Architecture](#architecture)
5. [How processing works](#how-processing-works)
6. [Duplicate prevention](#duplicate-prevention)
7. [Priority handling](#priority-handling)
8. [Failure handling](#failure-handling)
9. [Database design](#database-design)
10. [Testing](#testing)
11. [Assumptions and interpretations](#assumptions-and-interpretations)
12. [Trade-offs](#trade-offs)
13. [Known limitations](#known-limitations)
14. [What I would improve before production](#what-i-would-improve-before-production)

---

## Technology stack

| Concern | Choice |
|---|---|
| Runtime | .NET 10, ASP.NET Core Web API (controllers) |
| Persistence | SQL Server + EF Core 10 (code first, migrations) |
| Validation | Hand-written validator in the Application layer (no FluentValidation, see trade-offs) |
| Errors | `IExceptionHandler` + RFC 7807 `ProblemDetails` |
| API docs | `Microsoft.AspNetCore.OpenApi` document + Swagger UI |
| Time | `TimeProvider` (injected; faked in tests) |
| Tests | xUnit + Moq |
| Logging / DI | Built-in `ILogger<T>` (structured) and `Microsoft.Extensions.DependencyInjection` |

---

## Running the application

### Prerequisites

- .NET 10 SDK: `dotnet --version` should print `10.x`
- SQL Server: **either** LocalDB (installed with Visual Studio) **or** Docker
- EF Core CLI (for migration commands): `dotnet tool install --global dotnet-ef`

### 1. Database setup

**Option A: LocalDB (default, nothing to configure)**

`appsettings.json` already points at:

```
Server=(localdb)\MSSQLLocalDB;Database=NotificationProcessing;Trusted_Connection=True;TrustServerCertificate=True
```

**Option B: SQL Server in Docker**

```bash
cp .env.example .env            # then set a strong MSSQL_SA_PASSWORD in .env
docker compose up -d
```

Then point the API at it. Don't put the password in `appsettings.json`; use user secrets or an environment variable:

```bash
cd src/NotificationProcessing.API
dotnet user-secrets set "ConnectionStrings:NotificationDb" "Server=localhost,1433;Database=NotificationProcessing;User Id=sa;Password=<your password>;TrustServerCertificate=True"
```

or (PowerShell)

```powershell
$env:ConnectionStrings__NotificationDb = "Server=localhost,1433;Database=NotificationProcessing;User Id=sa;Password=<your password>;TrustServerCertificate=True"
```

### 2. Migrations

In **Development**, the API applies migrations automatically at startup (`Database:ApplyMigrationsOnStartup = true` in `appsettings.Development.json`). To apply them manually:

```bash
dotnet ef database update --project src/NotificationProcessing.Infrastructure --startup-project src/NotificationProcessing.API
```

Add a new migration after changing the model:

```bash
dotnet ef migrations add <Name> --project src/NotificationProcessing.Infrastructure --startup-project src/NotificationProcessing.API --output-dir Persistence/Migrations
```

Generate a SQL script (useful for DBA review / production deployments):

```bash
dotnet ef migrations script --idempotent --project src/NotificationProcessing.Infrastructure --startup-project src/NotificationProcessing.API -o migrate.sql
```

> **Note on the included migration.** `InitialCreate` was written by hand because the environment I built this in couldn't reach NuGet to run `dotnet ef`. It matches the entity configuration exactly. If `dotnet ef` reports pending model changes on your machine, regenerate it: delete `src/NotificationProcessing.Infrastructure/Persistence/Migrations` and run the `migrations add InitialCreate` command above.

### 3. Run

```bash
dotnet run --project src/NotificationProcessing.API --launch-profile http
```

- Swagger UI: **http://localhost:5080/swagger**
- OpenAPI JSON: http://localhost:5080/openapi/v1.json
- Ready-made requests: `src/NotificationProcessing.API/NotificationProcessing.API.http` (works in Visual Studio / VS Code REST Client / Rider)

### 4. Tests

```bash
dotnet test
```

### Configuration reference

| Key | Default | Meaning |
|---|---|---|
| `ConnectionStrings:NotificationDb` | LocalDB | SQL Server connection |
| `Notifications:DuplicateDetectionWindow` | `00:10:00` | How long an identical request counts as a retry |
| `Notifications:ProcessingBatchSize` | `50` | Max notifications per `/process` call |
| `EmailProvider:FailureRate` | `0.0` | Random failure probability (0–1) of the simulated provider |
| `EmailProvider:AlwaysFailDomains` | `["fail.example.com"]` | Recipients in these domains always fail (demo) |
| `EmailProvider:SimulatedLatency` | `00:00:00.050` | Fake network latency per send |
| `Database:ApplyMigrationsOnStartup` | `false` (`true` in Development) | Run `Migrate()` at startup |

---

## API reference and examples

| Method | Route | Purpose | Responses |
|---|---|---|---|
| POST | `/api/notifications` | Create a notification | 201 created · 200 duplicate · 400 invalid |
| GET | `/api/notifications/{id}` | Get one notification | 200 · 404 |
| GET | `/api/customers/{customerId}/notifications?status=&page=&pageSize=` | A customer's notifications, newest first | 200 · 400 |
| POST | `/api/notifications/process` | Process due notifications | 200 with a summary |

All errors use `application/problem+json` with a `traceId`.

### Create: `201 Created`

```http
POST /api/notifications
Content-Type: application/json

{
  "customerId": "CUST-101",
  "type": "Email",
  "recipient": "customer@example.com",
  "subject": "Order Confirmed",
  "message": "Your order has been confirmed.",
  "priority": "Normal",
  "requestedDeliveryTime": "2026-12-06T12:30:00Z"
}
```

```http
HTTP/1.1 201 Created
Location: /api/notifications/ef6f1ef9-009f-48d2-92ca-e599c422c7f3

{
  "id": "ef6f1ef9-009f-48d2-92ca-e599c422c7f3",
  "customerId": "CUST-101",
  "type": "Email",
  "recipient": "customer@example.com",
  "subject": "Order Confirmed",
  "message": "Your order has been confirmed.",
  "priority": "Normal",
  "requestedDeliveryTime": "2026-12-06T12:30:00+00:00",
  "createdAt": "2026-11-18T05:57:04.6842213+00:00",
  "status": "Pending",
  "attemptCount": 0,
  "processedAt": null,
  "failureReason": null
}
```

### Same request again (client retry): `200 OK`

```http
HTTP/1.1 200 OK
X-Duplicate-Request: true
Location: /api/notifications/ef6f1ef9-009f-48d2-92ca-e599c422c7f3

{ "id": "ef6f1ef9-009f-48d2-92ca-e599c422c7f3", ... same body as above ... }
```

### Invalid request: `400 Bad Request`

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "customerId": ["Customer ID is required."],
    "recipient": ["Recipient must be a valid email address."],
    "message": ["Message is required."],
    "priority": ["Priority must be one of: Low, Normal, High."],
    "requestedDeliveryTime": ["Requested delivery time cannot be in the past."]
  },
  "traceId": "00-b82a4c6b9fd4aeabb6f7dde12369e3a9-4b3102b5887e6b82-00"
}
```

### Unknown id: `404 Not Found`

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5", "title": "Not Found", "status": 404, "traceId": "00-..." }
```

### Process: `200 OK`

```json
{ "selected": 4, "sent": 3, "failed": 1, "skipped": 0 }
```

`skipped` counts notifications that another concurrent run had already claimed.

### Customer notifications: `200 OK`

```http
GET /api/customers/CUST-101/notifications?status=Failed&page=1&pageSize=20
```

```json
{
  "items": [ { "id": "...", "status": "Failed", "failureReason": "Simulated provider rejected recipient domain 'fail.example.com'.", ... } ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

---

## Architecture

```
src/
  NotificationProcessing.Domain          Entity, enums, state-machine exception. No dependencies.
  NotificationProcessing.Application     Use cases (NotificationService, NotificationProcessor), validation,
                                         DTOs, fingerprinting, ports (INotificationRepository, INotificationProvider).
  NotificationProcessing.Infrastructure  EF Core DbContext, configuration, migrations, repository,
                                         simulated EmailNotificationProvider.
  NotificationProcessing.API             Controllers, global exception handler, Program.cs, configuration.
tests/
  NotificationProcessing.UnitTests       xUnit + Moq tests of Domain and Application.
```

Dependencies point inward: `API → Application → Domain`, and `Infrastructure → Application`. The API references Infrastructure only to wire up DI.

**Why each abstraction exists** (and only these):

| Abstraction | Why it earns its place |
|---|---|
| `INotificationProvider` | Required by the exercise. It isolates the unreliable external system and lets tests simulate success, failure results and exceptions. One implementation per channel (`Channel` property), so SMS can be added without touching the processor. |
| `INotificationRepository` | Lets the dedupe, claim and ordering logic be unit-tested without SQL Server. It's a purpose-built repository (`TryMarkProcessingAsync`, `TryAddAsync`), not a generic `IRepository<T>`, because the atomic set-based transitions don't fit a generic Update(entity). |
| `TimeProvider` | "Now" drives three rules (past delivery time, due for processing, duplicate window). Injecting it makes those tests deterministic. |
| `IOptions<T>` | Window, batch size and provider simulation are configuration, validated at startup. |

**Deliberately not used:** MediatR/CQRS (two services with a handful of methods don't need a mediator), a generic repository, AutoMapper (one `From` mapping method), FluentValidation, and interfaces for the two application services (only one implementation each, and nothing mocks them; easy to extract later).

**Thin controllers:** controllers translate HTTP to service calls and results to status codes and headers. They contain no business rules and no try/catch.

**Validation in one place:** `CreateNotificationRequestValidator`. The request DTO uses nullable strings so model binding doesn't reject fields early with generic messages; every rule produces a field-level message from the same class.

---

## How processing works

`POST /api/notifications/process` calls `NotificationProcessor.ProcessPendingAsync`:

1. **Select eligible**, with no locks and no transaction:
   `Status = Pending AND RequestedDeliveryTime <= now`, ordered `Priority DESC, CreatedAt ASC, Id ASC`, `TOP (batchSize)`.
2. For each candidate, in that order:
   1. **Claim it atomically:**
      ```sql
      UPDATE Notifications
      SET Status = 1 /*Processing*/, ProcessingStartedAt = @now, AttemptCount = AttemptCount + 1
      WHERE Id = @id AND Status = 0 /*Pending*/
      ```
      If 0 rows are affected, another run claimed it first: **skip**.
   2. **Call the provider** with no database transaction open.
   3. **Record the outcome:** `UPDATE ... SET Status = Sent|Failed ... WHERE Id = @id AND Status = Processing`.
3. Return `{ selected, sent, failed, skipped }`.

**Why concurrent runs can't send the same notification twice:** the claim is one `UPDATE` statement whose `WHERE` re-checks `Status = Pending`. SQL Server takes an update lock on the row, so if two runs race, the second waits, re-evaluates the predicate after the first commits, sees `Processing`, and updates 0 rows. Only the run that got `1 row affected` calls the provider. No explicit transaction or application lock is needed, and nothing is held open during the slow external call.

**Eligibility for scheduled notifications:** a future `RequestedDeliveryTime` simply doesn't match `<= now`, so it stays `Pending` until a later run. "Equal to now" counts as due.

**Delivery guarantee (stated honestly):** this is **at-most-once per claim**, effectively at-most-once overall, because there's no automatic retry. It is **not exactly-once**. If the process crashes after the provider accepted the email but before `Sent` is written, the row stays in `Processing`. We don't automatically resend it, because we can't know whether it was delivered. Exactly-once across an external email provider isn't achievable without the provider's cooperation. In production I'd pass the notification `Id` as an idempotency key to the provider (SES/SendGrid support message IDs or custom headers), and add a "stuck in Processing" sweeper that reconciles with the provider's delivery status. See [improvements](#what-i-would-improve-before-production).

---

## Duplicate prevention

**Goal:** a client that retries the *same* request after a timeout must not cause a second email.

**Identity of a request: content fingerprint.** SHA-256 over the normalized:
`CustomerId | Type | Recipient (lower-case) | Subject | Message | Priority | RequestedDeliveryTime (UTC, or "asap" if omitted)`.
If any of these differ, it's a genuinely different notification. The recipient is compared case-insensitively; subject and message are compared exactly.

I chose a fingerprint over a client-supplied `Idempotency-Key` header because the exercise says "the exact same request", and existing callers may not send a key. Supporting an optional `Idempotency-Key` would be my first production addition (see improvements).

**Two layers:**

1. **Application check (sliding window).** `WHERE Fingerprint = @f AND CreatedAt >= now - 10 min` (index `IX_Notifications_Fingerprint_CreatedAt`). If found, return the existing notification.
2. **Database guarantee (concurrency).** Two simultaneous retries can both pass step 1. Each row therefore stores `DeduplicationKey = Fingerprint + ":" + floor(now / window)`, which has a **UNIQUE index**. The second insert fails with SQL error 2601/2627. The repository turns that into `false`, and the service loads and returns the winner. Nothing is stored twice and the client never sees a 500.

**Response for a duplicate:** `200 OK` with the **original** notification, its `Location`, and `X-Duplicate-Request: true`. I didn't use `409 Conflict`: a client retrying after a timeout wants the result it missed (the ID), not an error it has to special-case. This mirrors how idempotent APIs like Stripe behave.

**Window:** 10 minutes by default (configurable). After the window, an identical request is treated as a new notification. This matters when `requestedDeliveryTime` is omitted, e.g. a legitimate "your OTP" message repeated later.

**Edge case acknowledged:** the unique key uses fixed buckets, so two *perfectly concurrent* identical requests that straddle a bucket boundary (same millisecond, either side of it) could both be inserted. Layer 1 still catches every *sequential* retry across the boundary, which is the real-world retry pattern. A fully exact sliding-window guarantee would need a serializable range lock (`UPDLOCK, HOLDLOCK` on the fingerprint index) or a client idempotency key, which I judged not worth the complexity here.

---

## Priority handling

- `Priority` is stored as an **int** (`Low = 0, Normal = 1, High = 2`), so `ORDER BY Priority DESC` gives High → Normal → Low. Stored as strings, it would sort alphabetically (High, Low, Normal), which is wrong.
- Ties are **FIFO by `CreatedAt`**, with `Id` as a final tie-breaker for deterministic ordering.
- The rule is written once (`NotificationQueryExtensions`) as an `IQueryable` expression. EF translates it to SQL, and the unit-test fake applies the same expression in memory, so the tests exercise the real rule.
- Processing within a batch is sequential, so the send order matches the priority order exactly.
- **Starvation:** strict priority means a constant stream of High could delay Low indefinitely. That's acceptable for this exercise. In production I'd add aging (boost priority after N minutes) or weighted fair queuing.

---

## Failure handling

| Situation | Result |
|---|---|
| Provider returns a failure result | `Failed`, `FailureReason` = provider message (truncated to 1000 chars) |
| Provider throws | Caught and logged with the exception. `Failed`, reason = `Provider error: <message>`. The batch continues. |
| No provider registered for the channel | `Failed`, with a clear reason |
| Request cancelled mid-send | `Failed`, "cancelled before the provider confirmed delivery". The outcome is still recorded (`CancellationToken.None` for the status write). Unclaimed items stay `Pending`. |
| Database error | Propagates to the global handler → `500` ProblemDetails. EF's `EnableRetryOnFailure` retries transient SQL errors. |
| Any unhandled exception in a request | `GlobalExceptionHandler` logs it and returns a generic 500 without internal details |

The simulated provider (`EmailNotificationProvider`) fails deterministically for `@fail.example.com` recipients and randomly at `FailureRate`. It logs only the recipient's domain, not the full address, to keep personal data out of logs.

There is **no automatic retry** of `Failed` notifications. The exercise doesn't ask for it, and blind retries risk duplicate sends. See improvements.

---

## Database design

Table `Notifications`:

| Column | Type | Notes |
|---|---|---|
| Id | uniqueidentifier PK | Generated in the domain so it's known before save |
| CustomerId | nvarchar(100) | |
| Type | int | `Email = 1` |
| Recipient | nvarchar(254) | RFC 5321 practical max |
| Subject | nvarchar(200) NULL | Optional |
| Message | nvarchar(4000) | |
| Priority | int | 0/1/2 so it sorts correctly |
| RequestedDeliveryTime | datetimeoffset | Always stored as UTC. Defaults to CreatedAt when omitted. |
| CreatedAt | datetimeoffset | Server time from `TimeProvider` |
| Status | int | 0 Pending, 1 Processing, 2 Sent, 3 Failed |
| AttemptCount | int | Incremented on each claim |
| ProcessingStartedAt / ProcessedAt | datetimeoffset NULL | Audit and "stuck" detection |
| FailureReason | nvarchar(1000) NULL | |
| Fingerprint | varchar(64) | SHA-256 hex |
| DeduplicationKey | varchar(100) | **UNIQUE** |

Indexes, each tied to a specific query:

| Index | Serves |
|---|---|
| `UX_Notifications_DeduplicationKey` (unique) | Concurrent duplicate protection |
| `IX_Notifications_Fingerprint_CreatedAt` | Sliding-window duplicate lookup (seek) |
| `IX_Notifications_CustomerId_CreatedAt DESC` | Customer list, newest first, with paging |
| `IX_Notifications_PendingQueue (Priority DESC, CreatedAt, Id) INCLUDE (RequestedDeliveryTime) WHERE Status = 0` | The processing query. **Filtered** to Pending, so it stays small however many Sent rows accumulate. Key order equals the `ORDER BY`, so there's no sort, and `TOP (n)` stops early. EF inlines the enum constant `Status = 0`, so the filtered index matches. |

The primary key is a random GUID (clustered), which fragments under heavy insert load. At this scale that's fine. See improvements.

---

## Testing

`dotnet test` runs the unit tests. 55 test cases, counting theory rows. **What and why:**

| # | Test(s) | Why it matters |
|---|---|---|
| 1 | `Valid_request_is_stored_as_pending` | The happy path, including server-side `CreatedAt` from the fake clock |
| 2–3 | `Missing_customer_id…`, `Missing_recipient…` (null/empty/whitespace) | Whitespace counts as missing, a common bug |
| 4 | `Invalid_email_is_rejected` (6 cases) | Includes forms `MailAddress` accepts on its own: `John <john@x.com>`, `user@localhost` |
| 5 | `Invalid_priority_is_rejected` incl. `"5"` | `Enum.TryParse("5")` succeeds, a classic trap |
| 6–7 | Past rejected, **equal to now accepted** | The boundary the exercise explicitly calls out |
| 8 | Duplicate within window, case-insensitive recipient, different content, after window, **unique-index race** (Moq) | All dedupe paths, including the concurrent path that only the DB can catch |
| 9 | `High_priority_is_processed_before…` | High created *last* is still sent *first* |
| 10 | `Equal_priority_is_processed_fifo…` | Seeded out of order to prove ordering isn't insertion order |
| 11 | `Future_notifications_are_not_processed_until_due` | Stays Pending, then processed exactly at due time |
| 12 | `Successful_send_marks_notification_sent` | Status, ProcessedAt, AttemptCount |
| 13 | Failure result **and** provider exception | Failed with reason, and **the rest of the batch continues** |
| 14 | `Unknown_id_returns_null` | Controller maps it to 404 |
| 15 | `Concurrent_runs_never_send_the_same_notification_twice` | 4 parallel runs, 40 notifications, each sent exactly once |
| + | Claimed-by-someone-else skip, batch size, no re-processing, paging, state-machine guards | Supporting rules |

The repository fake (`InMemoryNotificationRepository`) mimics the two database behaviours the logic depends on: the unique `DeduplicationKey` and the atomic Pending→Processing compare-and-set. It uses the real ordering and eligibility expressions. **What the unit tests can't prove** is that SQL Server enforces those behaviours. That needs integration tests against a real database (see improvements).

---

