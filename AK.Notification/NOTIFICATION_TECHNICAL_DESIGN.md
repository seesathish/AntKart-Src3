# AK.Notification — Technical Design

## Overview

AK.Notification is an event-driven microservice that sends transactional notifications to users. It consumes integration events from RabbitMQ and dispatches messages through pluggable notification channels. Email is fully implemented; SMS and WhatsApp are stubbed with the full abstraction in place for future activation without architectural change.

- **Transport:** HTTP REST, port 5087 (dev) / 8087 (Docker)
- **Database:** PostgreSQL — `AKNotificationsDb` (audit trail + retry state)
- **Email (local dev):** Mailhog SMTP trap — `antkart-mailhog:1025`, web UI at `http://localhost:8025`
- **Email (production):** SMTP via `antkartadmin@gmail.com` (Gmail app password required)
- **Trigger mechanism:** MassTransit consumers on RabbitMQ — no polling, fully event-driven
- **Retention policy:** Notifications auto-deleted after 90 days by a background cleanup service

---

## Architecture

```
AK.Notification/
  AK.Notification.Domain/          Notification aggregate, enums, domain events
  AK.Notification.Application/     CQRS handlers, consumers, channel interfaces, template renderer
  AK.Notification.Infrastructure/  EF Core, email channel (MailKit), stub channels, cleanup service
  AK.Notification.API/             Minimal API endpoints, Program.cs, Dockerfile
  AK.Notification.Tests/           Unit tests — domain, handlers, consumers, template renderer
```

### Layer Dependencies

```
Domain          ← no dependencies
Application     ← Domain, AK.BuildingBlocks
Infrastructure  ← Application (transitive Domain)
API             ← Application, Infrastructure, AK.BuildingBlocks
Tests           ← Application, Infrastructure, Domain
```

---

## Domain Model

### Notification (Aggregate Root)

```csharp
// Inherits from AK.BuildingBlocks.DDD.Entity:
//   Guid Id, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt
public sealed class Notification : Entity, IAggregateRoot
{
    public string UserId { get; private set; }           // Keycloak UUID
    public NotificationChannel Channel { get; private set; }
    public NotificationTemplateType TemplateType { get; private set; }
    public NotificationStatus Status { get; private set; }
    public string RecipientAddress { get; private set; } // email or phone
    public string? Subject { get; private set; }
    public string Body { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public int RetryCount { get; private set; }

    public static Notification Create(...);
    public void MarkSent();
    public void MarkFailed(string error);
    public void IncrementRetry();
}
```

### Enums

```csharp
public enum NotificationChannel
{
    Email = 1,
    Sms = 2,
    WhatsApp = 3
}

public enum NotificationStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2
}

public enum NotificationTemplateType
{
    WelcomeEmail = 1,
    OrderConfirmation = 2,
    OrderConfirmed = 3,
    OrderCancelled = 4,
    PaymentSucceeded = 5,
    PaymentFailed = 6
}
```

---

## Channel Abstraction

All channel logic lives in the Application/Infrastructure boundary. Adding a new channel later (SMS, WhatsApp) requires only a new Infrastructure class — zero domain or application layer changes.

### Application layer (interfaces)

```csharp
// Application/Channels/INotificationChannel.cs
public interface INotificationChannel
{
    NotificationChannel Channel { get; }
    Task SendAsync(NotificationMessage message, CancellationToken ct = default);
}

// Application/Channels/INotificationChannelResolver.cs
public interface INotificationChannelResolver
{
    INotificationChannel Resolve(NotificationChannel channel);
}

// Application/Channels/NotificationMessage.cs
public sealed record NotificationMessage(
    string RecipientAddress,
    string? Subject,
    string Body,
    NotificationChannel Channel);
```

### Infrastructure implementations

| Class | Channel | Status |
|-------|---------|--------|
| `EmailNotificationChannel` | Email | Implemented (MailKit SMTP) |
| `SmsNotificationChannel` | Sms | Stub — logs and returns success |
| `WhatsAppNotificationChannel` | WhatsApp | Stub — logs and returns success |

```csharp
// Infrastructure/Channels/SmsNotificationChannel.cs (stub)
public sealed class SmsNotificationChannel : INotificationChannel
{
    public NotificationChannel Channel => NotificationChannel.Sms;

    public Task SendAsync(NotificationMessage message, CancellationToken ct = default)
    {
        // TODO: implement Twilio SMS
        _logger.LogInformation("SMS stub: would send to {Number}", message.RecipientAddress);
        return Task.CompletedTask;
    }
}
```

### Channel resolver (Infrastructure/DI)

```csharp
// Infrastructure/Channels/NotificationChannelResolver.cs
public sealed class NotificationChannelResolver : INotificationChannelResolver
{
    private readonly IEnumerable<INotificationChannel> _channels;

    public INotificationChannel Resolve(NotificationChannel channel)
        => _channels.Single(c => c.Channel == channel);
}
```

---

## Template Renderer

Templates are code-based string interpolation in v1 — no external templating library, no DB storage. Subject and body are plain text (HTML styling is a future enhancement).

### Application layer

```csharp
// Application/Templates/INotificationTemplateRenderer.cs
public interface INotificationTemplateRenderer
{
    NotificationContent Render(NotificationTemplateType type, NotificationTemplateModel model);
}

public sealed record NotificationContent(string Subject, string Body);

// Base record — all template models extend this
public abstract record NotificationTemplateModel(string CustomerName);

// Per-template models
public sealed record WelcomeEmailModel(string CustomerName) 
    : NotificationTemplateModel(CustomerName);

public sealed record OrderConfirmationModel(
    string CustomerName,
    string OrderNumber,
    decimal TotalAmount,
    IReadOnlyList<string> ItemSummaries)  // e.g. "2x MEN-SHIR-001 @ ₹499"
    : NotificationTemplateModel(CustomerName);

public sealed record OrderConfirmedModel(
    string CustomerName,
    string OrderNumber,
    decimal TotalAmount)
    : NotificationTemplateModel(CustomerName);

public sealed record OrderCancelledModel(
    string CustomerName,
    string OrderNumber,
    string Reason)
    : NotificationTemplateModel(CustomerName);

public sealed record PaymentSucceededModel(
    string CustomerName,
    string OrderNumber,
    decimal Amount,
    string RazorpayPaymentId)
    : NotificationTemplateModel(CustomerName);

public sealed record PaymentFailedModel(
    string CustomerName,
    string OrderNumber,
    string Reason)
    : NotificationTemplateModel(CustomerName);
```

### Sample rendered output

**OrderConfirmation subject:** `Your AntKart order ORD-20260425-A1B2C3D4 is confirmed!`

**OrderConfirmation body:**
```
Hi Sathish,

Thank you for your order on AntKart!

Order Number : ORD-20260425-A1B2C3D4
Total Amount : ₹1,498.00

Items:
  2x MEN-SHIR-001 @ ₹499.00
  1x WOM-DRES-003 @ ₹500.00

We'll notify you when your order is confirmed and dispatched.

— The AntKart Team
```

---

## CQRS — Commands & Queries

### Commands

```
SendNotificationCommand
  UserId: string
  Channel: NotificationChannel
  TemplateType: NotificationTemplateType
  RecipientAddress: string
  Model: NotificationTemplateModel
```

This command is dispatched internally by each MassTransit consumer — never called directly from an endpoint.

### Queries

```
GetUserNotificationsQuery     → PagedResult<NotificationDto>
  Page, PageSize

GetNotificationByIdQuery      → NotificationDto
  Id: Guid

GetAllNotificationsQuery      → PagedResult<NotificationDto>   (admin only)
  Page, PageSize
```

### DTOs

```csharp
public sealed record NotificationDto(
    Guid Id,
    string Channel,
    string TemplateType,
    string Status,
    string RecipientAddress,
    string? Subject,
    DateTimeOffset? SentAt,
    DateTimeOffset CreatedAt);
```

---

## MassTransit Consumers

One consumer per integration event. Each consumer resolves the recipient address, builds the template model, and dispatches `SendNotificationCommand` via MediatR.

Registered via `AddRabbitMqMassTransit(configuration, "notification", cfg => { ... })`. The `"notification"` prefix ensures each consumer gets a uniquely-named RabbitMQ queue (e.g. `notification-payment-failed`) even when other services register a consumer with the same class name. Both queues bind to the same exchange — true fan-out delivery.

| Consumer | RabbitMQ Queue | Event | Template |
|----------|---------------|-------|----------|
| `UserRegisteredConsumer` | `notification-user-registered` | `UserRegisteredIntegrationEvent` | WelcomeEmail |
| `OrderCreatedConsumer` | `notification-order-created` | `OrderCreatedIntegrationEvent` | OrderConfirmation |
| `OrderConfirmedConsumer` | `notification-order-confirmed` | `OrderConfirmedIntegrationEvent` | OrderConfirmed |
| `OrderCancelledConsumer` | `notification-order-cancelled` | `OrderCancelledIntegrationEvent` | OrderCancelled |
| `PaymentSucceededConsumer` | `notification-payment-succeeded` | `PaymentSucceededIntegrationEvent` | PaymentSucceeded |
| `PaymentFailedConsumer` | `notification-payment-failed` | `PaymentFailedIntegrationEvent` | PaymentFailed |

All consumers are registered in the Application layer. They use `IMediator` — no direct dependency on Infrastructure.

---

## REST Endpoints

Thin read-only API — notifications are created by consumers, not by endpoint callers.

| Method | Route | Auth | Handler |
|--------|-------|------|---------|
| `GET` | `/api/notifications` | `user` | `GetUserNotificationsQuery` |
| `GET` | `/api/notifications/{id}` | `user` | `GetNotificationByIdQuery` (ownership check) |
| `GET` | `/api/notifications/admin` | `admin` | `GetAllNotificationsQuery` |
| `GET` | `/health` | none | BuildingBlocks health check |

---

## Infrastructure

### Email — MailKit SMTP

```csharp
// Infrastructure/Channels/EmailNotificationChannel.cs
public sealed class EmailNotificationChannel : INotificationChannel
{
    public NotificationChannel Channel => NotificationChannel.Email;

    public async Task SendAsync(NotificationMessage message, CancellationToken ct)
    {
        var email = new MimeMessage();
        email.From.Add(new MailboxAddress(_settings.DisplayName, _settings.From));
        email.To.Add(MailboxAddress.Parse(message.RecipientAddress));
        email.Subject = message.Subject ?? string.Empty;
        email.Body = new TextPart("plain") { Text = message.Body };

        // Explicit SecureSocketOptions — never pass a bare bool to ConnectAsync.
        // Port 465 = implicit SSL (SslOnConnect); 587 = STARTTLS; 1025 (Mailhog) = plain.
        var socketOptions = _settings.EnableSsl
            ? (_settings.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls)
            : SecureSocketOptions.None;

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(_settings.Host, _settings.Port, socketOptions, ct);
        if (!string.IsNullOrEmpty(_settings.Username))
            await smtp.AuthenticateAsync(_settings.Username, _settings.Password, ct);
        await smtp.SendAsync(email, ct);
        await smtp.DisconnectAsync(true, ct);
    }
}
```

**Port → SSL mode mapping:**

| Port | `EnableSsl` | `SecureSocketOptions` | Use case |
|------|-------------|----------------------|----------|
| 1025 | false | `None` | Mailhog (local dev) |
| 587  | true | `StartTls` | Gmail SMTP (production) |
| 465  | true | `SslOnConnect` | Implicit SSL (alternative) |

### EF Core — NotificationsDbContext

```csharp
// Single table: Notifications
// Indexes: UserId, Status, CreatedAt (for cleanup query)
```

### Cleanup Background Service

```csharp
// Infrastructure/BackgroundServices/NotificationCleanupService.cs
public sealed class NotificationCleanupService : BackgroundService
{
    // Runs daily at 02:00 UTC
    // DELETE FROM Notifications WHERE CreatedAt < UtcNow - 90 days
    // Logs count of deleted rows
}
```

Retention period is configurable via `NotificationSettings__RetentionDays` env var (default 90).

---

## Configuration

### Local development (docker-compose)

```yaml
EmailSettings__From: "antkartadmin@gmail.com"
EmailSettings__DisplayName: "AntKart"
EmailSettings__Host: "mailhog"      # Mailhog SMTP trap
EmailSettings__Port: "1025"
EmailSettings__EnableSsl: "false"
EmailSettings__Username: ""         # no auth needed for Mailhog
EmailSettings__Password: ""
NotificationSettings__RetentionDays: "90"
```

Email UI in local dev: `http://localhost:8025` (Mailhog web interface — all outbound emails captured here).

### Production (Gmail SMTP)

Use the `docker-compose.gmail.yml` compose override (gitignored — contains credentials):

```yaml
# docker-compose.gmail.yml  — never commit; gitignored
services:
  ak-notification-api:
    environment:
      - EmailSettings__Host=smtp.gmail.com
      - EmailSettings__Port=587
      - EmailSettings__EnableSsl=true
      - EmailSettings__Username=antkartadmin@gmail.com
      - EmailSettings__Password=<gmail-app-password>
      - EmailSettings__From=antkartadmin@gmail.com
      - EmailSettings__DisplayName=AntKart
```

Start with: `docker-compose -f docker-compose.yml -f docker-compose.override.yml -f docker-compose.gmail.yml up -d`

**Gmail app password setup:**
1. Enable 2-Step Verification on `antkartadmin@gmail.com`
2. Google Account → Security → 2-Step Verification → App passwords
3. Generate password for "Mail / Other (AntKart)"
4. Use the 16-character code (spaces optional) as `EmailSettings__Password`
5. Regular Gmail account passwords are rejected by SMTP — only App Passwords work

### Typed settings records (Application)

```csharp
public sealed record EmailSettings
{
    public string From { get; init; } = string.Empty;
    public string DisplayName { get; init; } = "AntKart";
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 587;
    public bool EnableSsl { get; init; } = true;
    public string? Username { get; init; }
    public string? Password { get; init; }
}

public sealed record NotificationSettings
{
    public int RetentionDays { get; init; } = 90;
}
```

---

## Docker

### Mailhog (added to docker-compose.yml)

```yaml
mailhog:
  image: mailhog/mailhog:latest
  container_name: antkart-mailhog
  restart: unless-stopped
  ports:
    - "1025:1025"   # SMTP
    - "8025:8025"   # Web UI → http://localhost:8025
```

No healthcheck needed — `ak-notification-api` uses `condition: service_started`.

### AK.Notification service

```yaml
ak-notification-api:
  image: ak-notification-api
  build:
    context: .
    dockerfile: AK.Notification/AK.Notification.API/Dockerfile
  container_name: antkart-notification-api
  restart: unless-stopped
  ports:
    - "8087:8080"
  environment:
    ASPNETCORE_ENVIRONMENT: Production
    ConnectionStrings__NotificationsDb: "Host=postgres;Port=5432;Database=AKNotificationsDb;Username=postgres;Password=postgres"
    EmailSettings__From: "antkartadmin@gmail.com"
    EmailSettings__DisplayName: "AntKart"
    EmailSettings__Host: "mailhog"
    EmailSettings__Port: "1025"
    EmailSettings__EnableSsl: "false"
    Keycloak__Authority: "http://keycloak:8080/realms/antkart"
    Keycloak__Audience: "antkart-client"
    Keycloak__RequireHttpsMetadata: "false"
    Keycloak__AdminUrl: "http://keycloak:8080"
    Keycloak__Realm: "antkart"
    Keycloak__ClientId: "antkart-client"
    Keycloak__ClientSecret: "antkart-secret"
    RabbitMq__Host: "rabbitmq"
    RabbitMq__VirtualHost: "/"
    RabbitMq__Username: "guest"
    RabbitMq__Password: "guest"
    Elasticsearch__Url: "http://elasticsearch:9200"
    NotificationSettings__RetentionDays: "90"
  depends_on:
    postgres:
      condition: service_healthy
    keycloak:
      condition: service_healthy
    rabbitmq:
      condition: service_healthy
    elasticsearch:
      condition: service_healthy
    mailhog:
      condition: service_started
```

---

## NuGet Packages

| Package | Version | Layer |
|---------|---------|-------|
| `MailKit` | 4.x | Infrastructure |
| `MimeKit` | 4.x | Infrastructure (transitive via MailKit) |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 9.x | Infrastructure |
| `Microsoft.EntityFrameworkCore.Design` | 9.x | Infrastructure |
| `MassTransit.RabbitMQ` | 8.3.6 | Infrastructure |
| `MassTransit.EntityFrameworkCore` | 8.3.6 | Infrastructure |
| `Swashbuckle.AspNetCore` | 7.x | API |

---

## Tests

Target: ~40 unit tests.

| Test Class | What It Tests |
|------------|---------------|
| `NotificationTests` | Aggregate: Create, MarkSent, MarkFailed, IncrementRetry |
| `NotificationTemplateRendererTests` | All 6 templates render correct subject + body |
| `SendNotificationCommandHandlerTests` | Resolves channel, renders template, persists, marks sent; marks failed on channel exception |
| `OrderCreatedConsumerTests` | Maps event fields to command correctly |
| `PaymentSucceededConsumerTests` | Maps event fields to command correctly |
| `UserRegisteredConsumerTests` | Maps event fields to command correctly |
| `GetUserNotificationsQueryHandlerTests` | Returns paged results for correct userId only |
| `NotificationCleanupServiceTests` | Deletes notifications older than retention days |

---

## Error Mapping

Inherits standard BuildingBlocks mapping:

| Exception | HTTP Status |
|-----------|-------------|
| `FluentValidation.ValidationException` | 400 |
| `UnauthorizedAccessException` | 403 |
| `KeyNotFoundException` | 404 |
| `Exception` (catch-all) | 500 |

Channel send failures (`SendAsync` throws) are caught in `SendNotificationCommandHandler`, written to `Notification.ErrorMessage`, status set to `Failed`, and **not** re-thrown — the handler returns gracefully so the consumer does not NACK the message and trigger a retry loop.

---

## Gateway Routes (ocelot.json addition)

```json
{
  "UpstreamPathTemplate": "/api/notifications/{everything}",
  "UpstreamHttpMethod": ["GET"],
  "DownstreamPathTemplate": "/api/notifications/{everything}",
  "DownstreamScheme": "http",
  "DownstreamHostAndPorts": [{ "Host": "ak-notification-api", "Port": 8080 }],
  "AuthenticationOptions": { "AuthenticationProviderKey": "Bearer" },
  "RateLimitOptions": { "EnableRateLimiting": true, "Period": "1s", "PeriodTimespan": 1, "Limit": 20 }
}
```

---

## Changes Required to Existing Services

### 1. AK.BuildingBlocks — Integration Events

Six events need new fields; one new event added.

#### Updated events

```csharp
// OrderCreatedIntegrationEvent — add CustomerEmail, CustomerName, OrderNumber
public sealed record OrderCreatedIntegrationEvent(
    Guid OrderId,
    string UserId,
    string CustomerEmail,          // NEW
    string CustomerName,           // NEW
    string OrderNumber,            // NEW
    IReadOnlyList<OrderItemPayload> Items,
    decimal TotalAmount) : IIntegrationEvent { ... }

// OrderConfirmedIntegrationEvent — add CustomerEmail, CustomerName, OrderNumber, TotalAmount
public sealed record OrderConfirmedIntegrationEvent(
    Guid OrderId,
    string UserId,
    string CustomerEmail,          // NEW
    string CustomerName,           // NEW
    string OrderNumber,            // NEW
    decimal TotalAmount) : IIntegrationEvent { ... }  // NEW

// OrderCancelledIntegrationEvent — add UserId (was missing!), CustomerEmail, CustomerName, OrderNumber
public sealed record OrderCancelledIntegrationEvent(
    Guid OrderId,
    string UserId,                 // NEW (was missing)
    string CustomerEmail,          // NEW
    string CustomerName,           // NEW
    string OrderNumber,            // NEW
    string Reason) : IIntegrationEvent { ... }

// PaymentSucceededIntegrationEvent — add CustomerEmail, CustomerName, OrderNumber, Amount
public sealed record PaymentSucceededIntegrationEvent(
    Guid PaymentId,
    Guid OrderId,
    string UserId,
    string CustomerEmail,          // NEW
    string CustomerName,           // NEW
    string OrderNumber,            // NEW
    decimal Amount,                // NEW
    string RazorpayPaymentId) : IIntegrationEvent { ... }

// PaymentFailedIntegrationEvent — add CustomerEmail, CustomerName, OrderNumber
public sealed record PaymentFailedIntegrationEvent(
    Guid PaymentId,
    Guid OrderId,
    string UserId,
    string CustomerEmail,          // NEW
    string CustomerName,           // NEW
    string OrderNumber,            // NEW
    string Reason) : IIntegrationEvent { ... }
```

#### New event

```csharp
// UserRegisteredIntegrationEvent (NEW)
public sealed record UserRegisteredIntegrationEvent(
    string UserId,
    string CustomerEmail,
    string CustomerName) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
```

#### New HttpContext helpers (HttpContextExtensions.cs)

```csharp
// Add to existing HttpContextExtensions — used by Order and Payments endpoints
public static string GetUserEmail(this HttpContext http)
    => http.User.FindFirst("email")?.Value
    ?? http.User.FindFirst(ClaimTypes.Email)?.Value
    ?? throw new UnauthorizedAccessException("Email claim not found in token.");

public static string GetUserDisplayName(this HttpContext http)
    => http.User.FindFirst("name")?.Value
    ?? http.User.FindFirst("preferred_username")?.Value
    ?? "Customer";
```

---

### 2. AK.Order

#### Domain: Order entity
Add `CustomerEmail` and `CustomerName` — stored at creation, denormalized from JWT claims.

```csharp
public string CustomerEmail { get; private set; }
public string CustomerName { get; private set; }
```

#### Application: CreateOrderCommand
```csharp
public sealed record CreateOrderCommand(
    string UserId,
    string CustomerEmail,     // NEW
    string CustomerName,      // NEW
    IReadOnlyList<CreateOrderItemDto> Items) : IRequest<OrderDto>;
```

#### API: OrderEndpoints.cs
Extract from JWT at endpoint layer (same pattern as `GetUserId()`):
```csharp
var userId = http.GetUserId();
var customerEmail = http.GetUserEmail();     // NEW
var customerName = http.GetUserDisplayName(); // NEW
```

#### Publishers: update all three events
`CreateOrderCommandHandler`, `OrderStatusChangedEvent` consumer, and cancellation handler all need to include `CustomerEmail`, `CustomerName`, `OrderNumber` when publishing.

#### Tests: update affected tests
- `CreateOrderCommandHandlerTests` — add email/name to test data
- Integration event assertion tests — assert new fields present

---

### 3. AK.Payments

#### Domain: Payment entity
Add `CustomerEmail` and `CustomerName`:

```csharp
public string CustomerEmail { get; private set; }
public string CustomerName { get; private set; }
```

#### Application: InitiatePaymentCommand
```csharp
public sealed record InitiatePaymentCommand(
    string UserId,
    string CustomerEmail,     // NEW
    string CustomerName,      // NEW
    Guid OrderId,
    string OrderNumber,       // NEW
    decimal Amount) : IRequest<PaymentDto>;
```

#### API: PaymentEndpoints.cs
```csharp
var userId = http.GetUserId();
var customerEmail = http.GetUserEmail();      // NEW
var customerName = http.GetUserDisplayName(); // NEW
```

#### Publishers: update PaymentSucceededIntegrationEvent and PaymentFailedIntegrationEvent
Include `CustomerEmail`, `CustomerName`, `OrderNumber`, `Amount` from the Payment entity.

#### Tests: update affected tests
- `InitiatePaymentCommandHandlerTests` — add email/name to command
- Event assertion tests — assert new fields

---

### 4. AK.UserIdentity

#### Add MassTransit publisher
UserIdentity currently has no event bus. Add:
- `MassTransit.RabbitMQ` package to `AK.UserIdentity.API`
- `AddRabbitMqMassTransit()` in `Program.cs` (publish-only, no consumers)
- In `KeycloakService.RegisterAsync`: after successful Keycloak registration, publish `UserRegisteredIntegrationEvent`

```csharp
// In KeycloakService.RegisterAsync — after successful registration:
await _publishEndpoint.Publish(new UserRegisteredIntegrationEvent(
    UserId: newUserId,
    CustomerEmail: request.Email,
    CustomerName: $"{request.FirstName} {request.LastName}"));
```

#### Tests: update KeycloakServiceTests
- Add `Mock<IPublishEndpoint>` to existing test setup
- Assert `UserRegisteredIntegrationEvent` is published on successful registration

---

## Implementation Order

When building, follow this sequence to keep the solution in a buildable state at every step:

1. **BuildingBlocks** — update 5 integration events + add `UserRegisteredIntegrationEvent` + add `GetUserEmail()`/`GetUserDisplayName()` helpers
2. **AK.Order** — update domain, command, endpoint, publishers, tests
3. **AK.Payments** — update domain, command, endpoint, publishers, tests
4. **AK.UserIdentity** — add MassTransit publisher, update tests
5. **AK.Notification** — build full service (Domain → Application → Infrastructure → API → Tests)
6. **docker-compose.yml** — add Mailhog + ak-notification-api
7. **ocelot.json** — add notification routes
8. **CLAUDE.md + README.md** — update docs
9. **test_all.sh** — add notification endpoint smoke tests
10. **Commit:** `Add AK.Notification microservice`
