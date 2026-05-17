# Skill: Add an EF Core Migration

**Purpose:** Correctly run a code-first EF Core migration for AK.Order, AK.Payments, or AK.Notification — the three services that use PostgreSQL via EF Core. Includes the right `--project` / `--startup-project` flags, verifying the migration, and ensuring it applies cleanly in Docker.

---

## When to Use
- Adding a new entity or table to a service's DbContext
- Changing a column type, adding an index, or altering a constraint
- After modifying an existing entity (new property, rename, relation change)

## Services with EF Core Migrations

| Service | DbContext | Database |
|---------|-----------|----------|
| AK.Order | `OrderDbContext` | `AKOrdersDb` (PostgreSQL) |
| AK.Payments | `PaymentsDbContext` | `AKPaymentsDb` (PostgreSQL) |
| AK.Notification | `NotificationDbContext` | `AKNotificationsDb` (PostgreSQL) |

AK.Discount uses SQLite — same commands, different project paths.  
AK.Products uses MongoDB — no EF migrations needed.  
AK.ShoppingCart uses Redis — no EF migrations needed.

---

## Step 1 — Make the Domain/Infrastructure Change First

Apply your change to:
1. The domain entity (new property with private setter)
2. The EF Core fluent configuration in `<Service>DbContext.OnModelCreating()` or a separate `IEntityTypeConfiguration<T>` class
3. Update the repository interface and implementation if a new query is needed

Do **not** run the migration until these compile cleanly.

---

## Step 2 — Run the Migration

```bash
# AK.Order
dotnet ef migrations add <MigrationName> \
  --project AK.Order/AK.Order.Infrastructure \
  --startup-project AK.Order/AK.Order.API

# AK.Payments
dotnet ef migrations add <MigrationName> \
  --project AK.Payments/AK.Payments.Infrastructure \
  --startup-project AK.Payments/AK.Payments.API

# AK.Notification
dotnet ef migrations add <MigrationName> \
  --project AK.Notification/AK.Notification.Infrastructure \
  --startup-project AK.Notification/AK.Notification.API

# AK.Discount (SQLite)
dotnet ef migrations add <MigrationName> \
  --project AK.Discount/AK.Discount.Infrastructure \
  --startup-project AK.Discount/AK.Discount.Grpc
```

**Migration naming convention:** PascalCase, describes the change, not the date:
- `AddTrackingNumberToOrder` ✅
- `Migration20260517` ❌

---

## Step 3 — Review the Generated Migration

Open `AK.<Service>/AK.<Service>.Infrastructure/Migrations/<timestamp>_<MigrationName>.cs`.

Check:
- `Up()` does what you expect (new column, index, table)
- `Down()` correctly reverts it (drops column, removes index)
- No unintended changes — EF sometimes adds extra constraints or renames if a property was renamed in the model
- Column types are correct for PostgreSQL (e.g. `text` not `nvarchar`, `uuid` not `uniqueidentifier`)

If the migration is wrong, delete both `<Timestamp>_<Name>.cs` and `<Timestamp>_<Name>.Designer.cs`, fix the model, and rerun Step 2.

---

## Step 4 — Generate and Review the SQL (optional but recommended)

```bash
# Preview the SQL that will run against the DB
dotnet ef migrations script \
  --project AK.Order/AK.Order.Infrastructure \
  --startup-project AK.Order/AK.Order.API \
  --idempotent
```

This is especially useful before applying to a production-like environment.

---

## Step 5 — Verify Auto-Migration in Program.cs

All three services call `ApplyMigrationsAsync()` in `Program.cs` on startup:

```csharp
// Check this exists in AK.<Service>.API/Program.cs
await app.ApplyMigrationsAsync();
```

This means migrations apply automatically when the container starts. No manual `dotnet ef database update` needed in Docker.

If `ApplyMigrationsAsync()` is missing, add it before `app.Run()`:

```csharp
// In Infrastructure layer (IApplicationBuilder extension)
public static async Task ApplyMigrationsAsync(this IApplicationBuilder app)
{
    using var scope = app.ApplicationServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    await db.Database.MigrateAsync();
}
```

---

## Step 6 — Test Locally

```bash
# Run the specific service locally to verify migration applies
cd AK.Order/AK.Order.API && dotnet run
# Watch the startup logs for "Applying migrations..."
# If it errors, the migration SQL has a problem
```

---

## Step 7 — Rebuild Docker

```bash
# Rebuild just the affected service
docker-compose up --build -d ak-order-api

# Watch logs for migration success
docker logs antkart-order-api --follow
```

Expected log line: `Applying pending EF Core migrations...` followed by the migration name and no errors.

If the migration fails in Docker (e.g. column already exists from a previous failed run), connect to the DB and check `__EFMigrationsHistory`:

```bash
docker exec -it antkart-postgres psql -U antkart -d AKOrdersDb \
  -c "SELECT * FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\";"
```

---

## Step 8 — Build and Test

```bash
dotnet build    # 0 errors
dotnet test     # all pass — update any tests that depended on the old schema
```

---

## Step 9 — Update the Design Doc

In `AK.<Service>/<SERVICE>_TECHNICAL_DESIGN.md`, update:
- Domain model entity diagram or table (new field)
- Database schema section (if present)
- Any query or filtering section that uses the new column

---

## Common Mistakes

| Mistake | Consequence | Fix |
|---------|-------------|-----|
| Running migration without `--startup-project` | EF can't resolve connection string → `InvalidOperationException` | Always include both `--project` and `--startup-project` |
| Renaming a C# property without `[Column]` or `.HasColumnName()` | EF generates drop+add instead of rename → data loss in production | Use `.HasColumnName("old_name")` or `migrationBuilder.RenameColumn()` in `Up()` |
| Not reviewing `Down()` | Rollback silently does nothing useful | Always check `Down()` reverses `Up()` completely |
| Forgetting to commit the migration files | Other developers get `No migrations found` errors | `git add` the `Migrations/` folder before pushing |
| Empty migration (`Up()` has no operations) | EF didn't detect a change — model and DB may be out of sync | Check `OnModelCreating()` config matches the entity; check `ModelSnapshot` for the entity |

---

## Checklist

- [ ] Entity changes compiled successfully
- [ ] EF fluent configuration updated in `OnModelCreating` or `IEntityTypeConfiguration<T>`
- [ ] Migration generated with correct `--project` + `--startup-project` flags
- [ ] Migration `Up()` reviewed — does the right thing
- [ ] Migration `Down()` reviewed — cleanly reverses `Up()`
- [ ] `ApplyMigrationsAsync()` exists in `Program.cs`
- [ ] Service starts locally without migration errors
- [ ] Docker container starts and migration applies automatically
- [ ] `dotnet test` passes
- [ ] Migration files committed to git
- [ ] Service design doc updated with schema change
