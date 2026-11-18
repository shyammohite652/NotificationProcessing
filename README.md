# Notification Processing API

A small ASP.NET Core Web API (.NET 10) that accepts email notification requests, validates and stores them in SQL Server, and sends them through a simulated email provider. High-priority notifications go first, scheduled ones wait until they're due, duplicate retries are ignored, and provider failures are recorded without stopping anything.

## How to run

**Requirements:** .NET 10 SDK, and SQL Server LocalDB (installed with Visual Studio) or Docker.

```bash
dotnet run --project src/NotificationApi --launch-profile http
```

- Swagger: **http://localhost:5080/swagger**
- Sample requests: `src/NotificationApi/NotificationApi.http`
- In Development the database is created automatically on startup (EF Core migration).

**Using Docker instead of LocalDB:**

```bash
cp .env.example .env        # set MSSQL_SA_PASSWORD
docker compose up -d
dotnet user-secrets --project src/NotificationApi set "ConnectionStrings:NotificationDb" "Server=localhost,1433;Database=NotificationDb;User Id=sa;Password=<password>;TrustServerCertificate=True"
```

**Migrations (manual):**

```bash
dotnet tool install --global dotnet-ef
dotnet ef database update --project src/NotificationApi
dotnet ef migrations add <Name> --project src/NotificationApi --output-dir Data/Migrations
```

> The `InitialCreate` migration was written by hand (`dotnet ef` couldn't run where this was built). If EF reports pending model changes, delete `Data/Migrations` and run `dotnet ef migrations add InitialCreate --project src/NotificationApi --output-dir Data/Migrations`.

**Tests:** `dotnet test`

## API

| Method | Route | Result |
|---|---|---|
| POST | `/api/notifications` | 201 created · 200 duplicate (header `X-Duplicate-Request: true`) · 400 invalid |
| GET | `/api/notifications/{id}` | 200 · 404 |
| GET | `/api/customers/{customerId}/notifications?page=1&pageSize=20` | 200, newest first |
| POST | `/api/notifications/process` | 200 `{ selected, sent, failed, skipped }` |

Example request:

```json
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

Errors are returned as standard ProblemDetails, for example:

```json
{ "status": 400, "title": "One or more validation errors occurred.",
  "errors": { "recipient": ["Recipient must be a valid email address."] } }
```

## Design

One API project, organised by folder, plus a test project:

```
src/NotificationApi/
  Controllers/   NotificationsController: HTTP only, no business rules
  Services/      NotificationService (create/read + duplicates), NotificationProcessor (sending), NotificationValidator
  Data/          AppDbContext, NotificationRepository, migrations
  Providers/     INotificationProvider + simulated EmailNotificationProvider
  Models/        Notification entity, enums, request/response records
  Middleware/    ErrorHandler: exceptions to ProblemDetails (400 / 500)
tests/NotificationApi.Tests/   xUnit + Moq
```

There are only two interfaces, each for a concrete reason:

- **`INotificationProvider`** is the external service the exercise asks to abstract. Tests use it to simulate success and failure.
- **`INotificationRepository`** lets the services be unit-tested without SQL Server.

### Validation

All rules live in `NotificationValidator`, and every error is returned together with its field name:

- Customer ID, type, recipient and message are required (whitespace counts as missing).
- The recipient must be a valid email address.
- Priority must be Low, Normal or High. It's optional and defaults to Normal.
- The delivery time can't be in the past. A time exactly equal to now is allowed. It's optional and defaults to now.

### Processing (`POST /api/notifications/process`)

1. Load up to 50 notifications that are **Pending and due** (`RequestedDeliveryTime <= now`), ordered **Priority DESC, then CreatedAt** (oldest first within a priority). Future notifications aren't selected, so they stay Pending.
2. For each one, **claim** it: `UPDATE ... SET Status = Processing WHERE Id = @id AND Status = Pending`. If 0 rows change, another run already took it, so skip it. This is what stops two simultaneous processing calls from sending the same email.
3. Call the provider. No database transaction is open during this call.
4. Save the result: **Sent**, or **Failed** with the error message. Exceptions are caught, so one failure doesn't stop the batch or crash the app.

Priority is stored as an int (Low = 0, Normal = 1, High = 2) so that sorting works. As text, it would sort alphabetically.

### Duplicate prevention

A retried request has the same content. The service builds a key from a hash of the request fields (customer, type, recipient, subject, message, priority, delivery time) plus the current 10-minute time slot.

- **Before saving:** look for that key in the current or previous slot. If it's found, return the **original** notification with `200 OK` and `X-Duplicate-Request: true`. The caller gets the ID it missed instead of an error to handle.
- **Database guarantee:** the key has a **UNIQUE index**. If two identical requests arrive at the same moment and both pass the check, SQL Server rejects the second insert and the service returns the first one.

## Assumptions

- `priority` is optional (default Normal). `requestedDeliveryTime` is optional (default now).
- Times must include an offset (`Z` or `+05:30`). They're stored as UTC.
- A duplicate means identical content within 10 minutes. After about 20 minutes, the same request counts as new.
- Processing is triggered by calling the endpoint. There's no background job, as the exercise describes.
- Failed notifications aren't retried automatically, because retrying without care risks double sends.
- Only `Email` is accepted. Other types return 400.

## Trade-offs and limitations

- **Not exactly-once.** If the app crashes after the email was sent but before `Sent` is saved, the notification stays in `Processing` and isn't resent. Guaranteed exactly-once delivery isn't possible with an external provider unless the provider de-duplicates on its side.
- Two identical requests arriving in the same instant, exactly on a 10-minute boundary, could both be stored. Normal retries are always caught.
- Strict priority means a constant stream of High notifications could delay Low ones.
- Sending is sequential: simple, and it keeps the exact order, but throughput is limited.
- The unit tests use an in-memory repository. The SQL behaviour (unique index, atomic claim) needs integration tests.

## What I would add for production

1. A background worker instead of calling `/process` manually.
2. Retries with backoff for failed sends, and a dead-letter state.
3. A job that recovers notifications stuck in `Processing`, plus a provider-side idempotency key (the notification ID).
4. An optional `Idempotency-Key` header for clients.
5. Integration tests with Testcontainers (real SQL Server).
6. Authentication, rate limiting, health checks and metrics.
7. More channels (SMS, push) behind `INotificationProvider`.
