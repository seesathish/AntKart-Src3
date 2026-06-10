# Cloud Migration Guide

**Purpose:** How the application is migrated from local infrastructure to managed cloud services — managed data stores, messaging, identity, and secret storage — and the patterns that make the services genuinely cloud-native.

The guide is built one migration step at a time. Each step follows the same rhythm — **Understand → Build → Execute → Verify** — and subsequent steps are added as they are delivered. For where this fits in the overall build, see the [Development Guide](../../DevelopmentGuide.md).

---

## Step 1 — Secret-less Configuration from Azure Key Vault

This is the foundation every later migration step builds on. Before a service can be pointed at a managed database, broker, or identity provider, it needs a way to read the connection details for those services **without storing any secret in source control**. The pattern is: secrets live in Azure Key Vault, and each service reads them at startup using its own Microsoft Entra identity — the developer's Azure CLI sign-in locally, a managed identity in the cloud.

### Understand

**`DefaultAzureCredential` — one credential, resolved by environment.** `DefaultAzureCredential` (from the `Azure.Identity` SDK) is a **credential chain**: an ordered list of credential sources it tries in turn, using the first that succeeds.

- **Locally**, the chain resolves to the **developer's Azure CLI sign-in** (`az login`). The code authenticates as *you*.
- **In the cloud**, it resolves to the **managed identity** of the resource the code runs on (an AKS workload identity, or a Function / App Service system-assigned identity).

The same code runs in both places — the environment decides which identity is used, and **no secret is ever stored** to make it work.

**The Key Vault configuration provider.** .NET configuration is layered from sources (appsettings, environment variables, …). The Azure Key Vault configuration provider adds the vault as **one more source**: at startup it uses `DefaultAzureCredential` to authenticate to the vault, reads every secret, and folds them into `IConfiguration`. The application then reads a value such as `MongoDbSettings:ConnectionString` by name exactly as before — but the value comes from the vault, and the **secret is never written into appsettings or committed**. (Key Vault secret names use `--` where configuration uses `:`, so a secret named `MongoDbSettings--ConnectionString` becomes the configuration key `MongoDbSettings:ConnectionString`.)

**Two different identities — do not confuse them.** This step is about the **service's own identity authenticating to Key Vault** (so the service can read its configuration). That is entirely separate from **end-user authentication** — the JWT a caller presents to the API — which is handled by the later identity-migration step. Here, the only identity in play is the service's: yours locally, a managed identity in the cloud.

**Local prerequisite — the role.** Authenticating proves *who* the identity is; it grants nothing until a role assignment says *what* it may do. To read secrets locally, the developer's identity must hold the **Key Vault Secrets User** role on the vault (a read-only data-plane role). Granting it is part of the Execute steps below.

For the foundations behind these ideas, see the [Identity concepts primer](identity-concepts.md).

### Build

The reusable piece is a single extension method in `AK.BuildingBlocks`, `AddAzureKeyVaultConfiguration`, plus two package references. Reading it line by line:

```csharp
public static IConfigurationBuilder AddAzureKeyVaultConfiguration(
    this IConfigurationBuilder builder,
    IConfiguration configuration)
{
    var keyVaultUri = configuration["KeyVault:Uri"];
    if (string.IsNullOrWhiteSpace(keyVaultUri))
        return builder; // No vault configured — keep running from local configuration only.

    var credential = new DefaultAzureCredential();

    builder.AddAzureKeyVault(new Uri(keyVaultUri), credential);
    return builder;
}
```

- **It reads `KeyVault:Uri` from the configuration already loaded** (appsettings / environment variables). The vault URI is **not a secret**, so it is safe to commit.
- **The provider is conditional.** If `KeyVault:Uri` is absent, the method returns without doing anything, so the service still runs purely from local configuration — useful for offline development or a test host with no vault. The same build therefore runs **both** with and without a vault; only the presence of the URI switches the vault on.
- **`DefaultAzureCredential` is constructed with no options.** The default chain already covers both target environments — a managed identity (cloud) and the Azure CLI login (local). For a **user-assigned** managed identity you would name it explicitly via `DefaultAzureCredentialOptions.ManagedIdentityClientId`; the current setup (developer CLI locally, a system-assigned identity in the cloud) needs no options.
- **`AddAzureKeyVault(uri, credential)`** registers the vault as a configuration source using that credential.

The two packages, pinned to versions consistent with the repo's .NET 9 stack, are added to `AK.BuildingBlocks`:

```xml
<PackageReference Include="Azure.Identity" Version="1.13.1" />
<PackageReference Include="Azure.Extensions.AspNetCore.Configuration.Secrets" Version="1.3.2" />
```

A service opts in by calling the extension in `Program.cs` **before anything reads configuration**, and the vault URI is set in its `appsettings.json`:

```csharp
// before AddSerilogLogging and any service registration:
builder.Configuration.AddAzureKeyVaultConfiguration(builder.Configuration);
```

```json
"KeyVault": {
  "Uri": "https://kv-antkart-dev.vault.azure.net/"
}
```

In this step `AK.Products` is wired as the proof; the same one-line call is how every other service adopts the pattern.

### Execute

These steps grant the developer identity read access, store a test secret, and run the service so it loads that secret from the vault. Replace `<subscription-id>` and `<resource-group>` with the dev values.

**1. Grant your developer identity the Key Vault Secrets User role on the vault.**

```bash
# Your Entra object id (the signed-in developer)
OBJECT_ID=$(az ad signed-in-user show --query id -o tsv)

# The vault's resource id (scope for the role assignment)
VAULT_ID=$(az keyvault show --name kv-antkart-dev --query id -o tsv)

az role assignment create \
  --assignee "$OBJECT_ID" \
  --role "Key Vault Secrets User" \
  --scope "$VAULT_ID"
```

**2. Store a test secret in the vault.**

```bash
az keyvault secret set \
  --vault-name kv-antkart-dev \
  --name "Demo--Greeting" \
  --value "hello-from-key-vault"
```

(The `--` in the name maps to the configuration key `Demo:Greeting`.)

**3. Run the service locally** — with an active `az login` session — so it authenticates as your identity and loads the vault:

```bash
cd AK.Products/AK.Products.API && dotnet run
```

### Verify

Confirm the service started and resolved its configuration from Key Vault **without printing any secret value**. On startup the service logs a single non-secret confirmation line:

```
[HH:mm:ss INF] [AK.Products.API] Program: Key Vault configuration loaded from https://kv-antkart-dev.vault.azure.net/
```

- If the line reads `Key Vault configuration source not configured (KeyVault:Uri absent)…`, the URI is not being picked up — check `appsettings.json`.
- If startup fails with an authorization error from Key Vault, the developer identity is missing the **Key Vault Secrets User** role (step 1) or there is no active `az login` session.

The log line reports only the **vault URI** (a non-secret value) and never the secret contents — secrets remain in the vault and in memory only. The service is now reading configuration from Key Vault using its own Entra identity, with no connection strings committed: the foundation the remaining migration steps build on.

### Note on data seeding

Startup auto-seeding is **disabled by default** for cloud-native operation. A service must not crash on boot, nor mutate the data store as a side effect of starting, simply because a store is momentarily unavailable. The `AK.Products` seeder is therefore gated behind a `Seeding:RunOnStartup` flag (default `false`) and the startup call is wrapped so that a seed failure logs a warning and the application still starts. Routine data seeding is instead performed as a **deliberate, separate operation** by a dedicated loader (introduced in the test-enablement step), not on application start.

---

## Step 2a — JWT Validation with Microsoft Entra ID

This step migrates the platform's cross-cutting **token validation** from the previous identity provider to **Microsoft Entra ID**, and standardises authorization on the flat `roles` claim Entra emits. It is the authentication half of the identity migration; reworking the identity *service* (login, registration, admin) follows separately.

### Understand

**Authentication vs authorization.** Two distinct questions. *Authentication* asks "is this a genuine, current token from the right issuer, meant for this API?" *Authorization* asks "what is this caller allowed to do?" They are answered by different mechanisms and must not be conflated.

**The four validation checks (authentication).** A bearer token is accepted only if all four hold:

1. **Issuer** — the token's `iss` is our tenant's **Entra v2 issuer** (`https://login.microsoftonline.com/<tenant-id>/v2.0`).
2. **Audience** — the token's `aud` is **this API's app registration** (its identifier URI `api://antkart-api-dev`), so a token minted for another API cannot be replayed here.
3. **Signature** — the token is signed by one of **Entra's published OIDC signing keys**, which the middleware fetches (and rotates) automatically from the tenant's OIDC metadata — no key is stored.
4. **Lifetime** — the token is **not expired** and not used before its valid-from time.

**The flat roles claim (authorization).** Once authenticated, authorization is driven by **app roles**. Entra issues them in a **flat, top-level `roles` claim** (a JSON array of role names). The previous provider nested roles under a `realm_access.roles` structure that each service had to unpack itself. Consuming the **flat `roles` claim** — by mapping the framework's role-claim type to `roles` — is what makes role checks **identical across every service**: the same `RequireRole("admin")` works in the REST APIs and in the gRPC interceptor. Standardising on this claim is also what **resolves KI-001**, the gRPC interceptor that previously read the nested structure and would have failed admin authorization under Entra tokens.

**This is the service's identity boundary, not the user's login.** Validating an incoming token is separate from *issuing* one. Token issuance and the login/registration/admin flows are the identity-service rework, handled in the next step.

### Build

The change is centred on one shared component, `AK.BuildingBlocks.Authentication`, with teaching comments:

- **`EntraSettings`** — a typed, **non-secret** settings record (`Instance`, `TenantId`, `Audience`) bound from the committed `Entra` configuration section. It derives the authority and the expected issuer as `{Instance}/{TenantId}/v2.0`.
- **`AddEntraAuthentication`** — configures JWT bearer validation:
  - `Authority` points at the Entra v2 OIDC endpoint, so signing keys are downloaded and rotated automatically.
  - `TokenValidationParameters` turns on the four checks above (`ValidateIssuer` + `ValidIssuer`, `ValidateAudience` + `ValidAudience`, `ValidateLifetime`, `ValidateIssuerSigningKey`).
  - `RoleClaimType = "roles"` makes `[Authorize(Roles=…)]`, `RequireRole("admin")`, and the `admin` policy read the **flat** claim directly — no nested parsing.
  - `MapInboundClaims = false` keeps claims under their original JWT names.
- **`UseEntraAuth`** — registers `UseAuthentication()` then `UseAuthorization()` in the correct order.
- **Per service** — every API (`Products`, `ShoppingCart`, `Order`, `Payments`, `Notification`, `Gateway`, `UserIdentity`) calls `AddEntraAuthentication` / `UseEntraAuth` and carries an `Entra` settings section in place of the old provider section. Business logic is unchanged.
- **gRPC interceptor (KI-001)** — `AK.Discount.Grpc`'s `AuthInterceptor` now reads the flat `roles` claim, so admin write RPCs authorize correctly under Entra tokens; read-only RPCs are unchanged.

### Execute

The Entra settings are **non-secret and committed** (`Entra:Instance`, `Entra:TenantId`, `Entra:Audience`); any value not yet finalized is marked "(to be updated)". To obtain a token for local testing, request one for the API's identifier URI:

```bash
az account get-access-token --resource api://antkart-api-dev --query accessToken -o tsv
```

For the token to carry `roles`, the **caller must be assigned the relevant app role** on the API app registration (for example, the `admin` app role for admin-only operations); without an assignment the token authenticates but carries no `roles` claim.

### Verify

- Calling a **protected endpoint** with a valid Entra token (correct issuer, audience, lifetime, signature) **succeeds**.
- An **admin-only** operation succeeds **only** when the token's flat claim carries `roles: ["admin"]`; a token without it is rejected with 403.
- A **missing, expired, or wrong-audience** token is rejected with 401.

You can inspect a token's claims by decoding it (for example at a JWT decoder) and confirming `aud` is `api://antkart-api-dev`, `iss` ends in `/v2.0`, and `roles` contains the expected role — without sharing the token, which is a credential.

---

## Step 2b — Retire the Dedicated Identity Service

With token validation delegated to Entra (Step 2a), the application no longer needs a service of its own to handle identity. This step removes it. The decision is recorded in [ADR-021](../adr/ADR-021-retire-identity-service-for-entra.md).

### Understand

The application baseline shipped a dedicated identity microservice — a thin proxy over a self-hosted identity provider for login, registration, token refresh, `/me`, and basic user/role administration. Under Microsoft Entra ID, every responsibility it held now lives elsewhere:

- **Token issuance** — Entra issues access tokens directly to clients through standard OAuth 2.0 / OpenID Connect flows. The application issues nothing.
- **Token validation** — already cross-cutting after Step 2a: each service validates Entra tokens through the shared building-blocks authentication.
- **User lifecycle and app-role assignment** — operational concerns managed in **Entra (portal or Microsoft Graph)**, exercised during test enablement — not application endpoints.

With issuance handled by Entra, validation handled in every service, and administration handled operationally, a dedicated identity service has no remaining purpose. Removing it is a **deliberate simplification** of the architecture — one fewer service to build, deploy, secure, and operate.

### Build

What is removed, and why:

- **The identity service projects (API and tests)** and their solution entries — the service is obsolete.
- **The gateway route** that forwarded to it — there is nothing downstream to route to.
- **The provider-specific settings type** that only the identity service consumed, and any code left dead solely by its removal.
- **References across the solution** to the identity service and the former provider, so the codebase reflects the Entra-native model. (Rendered C4 architecture diagrams are intentionally **not** regenerated here — they are updated after the migration round and still show the pre-migration topology.)

The notification "welcome" consumer is **retained** as a forward-compatible seam: in the Entra-native model its trigger is sourced externally (an Entra/Graph signal on user provisioning) rather than from an application identity service.

### Execute / Verify

- The solution **builds cleanly** without the identity service (`dotnet build` → 0 warnings).
- The **gateway starts** without its route to the identity service.
- The **remaining test suite passes**; the total decreases by the retired service's tests.
- No references to the former identity provider remain in the codebase.

---

## Step 3 — Messaging on Azure Service Bus

This step moves the messaging transport from the self-hosted broker to **Azure Service Bus**, authenticated with **Microsoft Entra** (no SAS key or connection string) and using the topology **provisioned by infrastructure-as-code**. The decision is recorded in [ADR-015](../adr/ADR-015-messaging-migration-to-service-bus.md).

### Understand

**MassTransit abstracts the transport.** The consumers, the order saga, and every `Publish`/`Send` call are written against MassTransit's API, not the broker's. Changing the transport therefore touches **only the bus configuration** — the business logic, the saga state machine, and the integration-event contracts are unchanged. (This is exactly what keeps the in-memory `ITestHarness` integration tests transport-agnostic and green.)

**Topology is owned by infrastructure-as-code.** The queues, the topic, and its subscriptions are provisioned by the platform's IaC — the single source of truth — not created by the application at runtime. The application's managed identity is granted only **Azure Service Bus Data Sender / Data Receiver**, never **Manage**, so it *cannot* create or alter entities. MassTransit is configured to **send to and receive from the existing provisioned entities and to not deploy or manage topology**. This keeps a clear separation: the platform owns *what* exists; the application only *uses* it, with least privilege.

**Entra authentication, no secret.** The bus connects to the namespace by its fully-qualified hostname (`sb-antkart-dev.servicebus.windows.net` — non-secret, committed) using `DefaultAzureCredential`: the developer's Azure CLI sign-in locally, the resource's managed identity in the cloud. There is no connection string, consistent with the secret-less posture established in the earlier steps.

### Build

The change is centred on one shared helper, `AddAzureServiceBusMassTransit` in `AK.BuildingBlocks.Messaging`, which mirrors the previous broker helper one-for-one:

- It reads the non-secret `ServiceBus:FullyQualifiedNamespace` setting.
- It keeps the same kebab-case endpoint-name formatter (per-service prefix), so endpoint naming — and the business wiring — is unchanged; only the transport differs.
- It configures `UsingAzureServiceBus` with `host.TokenCredential = new DefaultAzureCredential()` (Entra auth) and a transport-agnostic message-retry policy, then binds the registered consumers/saga to the provisioned entities.

Per service, the Infrastructure `ServiceCollectionExtensions` swaps the single call from the broker helper to `AddAzureServiceBusMassTransit(...)`; the registered consumers and saga are untouched. The packages move from `MassTransit.RabbitMQ` to `MassTransit.Azure.ServiceBus.Core` (the broker transport package is removed entirely), while `MassTransit` and `MassTransit.EntityFrameworkCore` (outbox/saga) stay. Each service's `appsettings` drops its old broker block and gains `ServiceBus:FullyQualifiedNamespace`.

**Removed:** the orphaned welcome-notification flow. The `UserRegisteredIntegrationEvent` contract and the Notification `UserRegisteredConsumer` were published only by the retired identity service and are now dead; both are deleted, and the affected test is adjusted so the remaining live consumers stay covered.

### Execute

Grant your developer identity the Service Bus data roles on the namespace (Send + Receive), set the non-secret namespace, and run a service locally so it connects over Entra:

```bash
OBJECT_ID=$(az ad signed-in-user show --query id -o tsv)
SB_ID=$(az servicebus namespace show --name sb-antkart-dev --resource-group rg-antkart-dev-eastus --query id -o tsv)

az role assignment create --assignee-object-id "$OBJECT_ID" --assignee-principal-type User \
  --role "Azure Service Bus Data Sender"   --scope "$SB_ID"
az role assignment create --assignee-object-id "$OBJECT_ID" --assignee-principal-type User \
  --role "Azure Service Bus Data Receiver" --scope "$SB_ID"
```

The namespace is configured in each service's `appsettings.json`:

```json
"ServiceBus": {
  "FullyQualifiedNamespace": "sb-antkart-dev.servicebus.windows.net"
}
```

Then run a service with an active `az login` session, e.g. `cd AK.Order/AK.Order.API && dotnet run`.

### Verify

- A service **starts and connects to Service Bus over Entra** — and there is **no broker (RabbitMQ) connection warning**, because the transport is now Service Bus.
- The **in-memory integration tests pass** (`dotnet test`) — they use the `ITestHarness` and require no Service Bus.
- Full **end-to-end SAGA over Service Bus** (publish → reserve stock → payment → notification across the provisioned entities) is verified during the **test-enablement step**.

---

## Step 4 — Product Catalogue on Azure Cosmos DB (MongoDB API)

This step moves the **AK.Products** data store from a self-hosted MongoDB to **Azure Cosmos DB using its MongoDB API**, with the connection string held in **Key Vault** (no secret in config) and a single-field, hashed **shard (partition) key** on the product id. The decision is recorded in [ADR-016](../adr/ADR-016-data-migration-cosmosdb-and-workload-identity.md).

### Understand

**The MongoDB API is wire-compatible.** Cosmos DB's MongoDB API speaks the MongoDB wire protocol, so the existing **MongoDB driver and almost all of the data-access code are unchanged**. The migration is primarily a **connection-string change**, plus one Cosmos-specific design decision: the **shard (partition) key**.

**Partitioning, in plain terms.** Cosmos physically distributes a collection's documents across partitions by the shard key. Choosing it well is what keeps the database fast and cheap:

- A **high-cardinality** key spreads documents evenly and avoids a **hot partition** (one partition taking most of the traffic while others sit idle). The **product id** has one distinct value per product — ideal.
- **Point-reads by id** land on a **single partition** and are cheap (≈1 RU).
- **Category / sub-category browse** is a **low-cost cross-partition query** at this catalogue scale, served by **secondary indexes** on those fields.

The key is therefore the **product id, hashed**. Per the current Cosmos DB for MongoDB documentation, the **RU-based (serverless) API requires a single-field, hashed shard key** (range and compound shard keys are a vCore-tier feature), which matches this choice. In a document the product id is stored as `_id`, so the shard key is `{ "_id": "hashed" }`.

**The connection string is a secret.** It is **retrieved from Key Vault** via the configuration foundation built in Step 1, so it **never appears in committed config**. `appsettings` carries only non-secret values — the database and collection names.

### Build

- **Configuration / secret.** The Cosmos connection string is stored as the Key Vault secret **`ProductsCosmosConnectionString`**. The Step 1 Key Vault configuration provider loads it into `IConfiguration`; the Products Infrastructure registration reads `configuration["ProductsCosmosConnectionString"]` and sets it on `MongoDbSettings.ConnectionString` (via `PostConfigure`). `appsettings` keeps only `DatabaseName` (`antkart-products`, the provisioned Cosmos database) and `ProductsCollection` (`products`) — **no connection string**. When the secret is absent (offline development), the non-secret local default (`mongodb://localhost:27017`) applies.
- **Shard key.** On first construction, `MongoDbContext` ensures the collection is sharded on `{ "_id": "hashed" }` via the `shardCollection` command. The command is a no-op if the collection is already sharded, and is safely skipped on a standalone MongoDB (which does not support sharding) so local development still runs.
- **Indexes.** The category, sub-category, and status secondary indexes are kept (supported single-field indexes). Two Mongo-only constructs are adjusted for Cosmos: the SKU index is made **non-unique** (a unique index on a sharded collection must include the shard key, so SKU uniqueness moves to the data/seed layer), and the **text index is removed** (Cosmos DB for MongoDB API does not support `$text` indexes). The MongoDB driver, the repository, the class map, and all query code are otherwise unchanged.

### Execute

Store the connection string in Key Vault (so it never touches the repo), then run Products so it resolves the secret from the vault:

```bash
# Get the Cosmos account's primary MongoDB connection string and store it as a Key Vault secret.
CONN=$(az cosmosdb keys list --name cosmos-antkart-dev --resource-group rg-antkart-dev-eastus \
  --type connection-strings --query "connectionStrings[0].connectionString" -o tsv)

az keyvault secret set --vault-name kv-antkart-dev --name "ProductsCosmosConnectionString" --value "$CONN"
```

The app reads it from the configuration key **`ProductsCosmosConnectionString`** (loaded from Key Vault). Run with an active `az login` session that can read the vault:

```bash
cd AK.Products/AK.Products.API && dotnet run
```

### Verify

- Products **starts and connects to Cosmos DB** using the vaulted connection string — there is **no localhost MongoDB** involved.
- A **create** and a **read** of a single product succeed against Cosmos (a point-read by id is single-partition).
- Startup seeding remains **opt-in** (`Seeding:RunOnStartup` default `false`); the **bulk seed of the full catalogue** is the later **test-enablement step**, not this one.

---

## Step 5 — Serverless Side-Effects with Event Grid and Azure Functions

This step brings the provisioned **Azure Event Grid** custom topic (`evgt-antkart-dev`) and **Azure Function App** (`func-antkart-notifications-dev`) into use for lightweight, **fire-and-forget side-effects** — starting with notification. It is a deliberate counterpart to Step 3: the durable Service Bus saga stays exactly as it is, and Event Grid is added *alongside* it for a different class of work. The decision is recorded in [ADR-019](../adr/ADR-019-serverless-notification-functions-eventgrid.md).

### Understand

**Two eventing mechanisms, two jobs.** The platform now runs two transports on purpose, and the distinction is the whole point of this step:

- **Service Bus + the order saga — the durable backbone.** This carries the *transaction-critical* workflow: reserve stock → take payment → confirm. It is **ordered**, **pull-based** (each consumer processes at its own pace), and **guaranteed** — messages are retried and dead-lettered until handled. Correctness of the order depends on every step completing.
- **Event Grid + a serverless Function — fire-and-forget side-effects.** This carries *discrete reactions* that must **not fail or delay** the core transaction — sending a confirmation email is the canonical example. Event Grid **pushes** each event to the Function (the Function is the registered handler), the Function App **scales to zero** when idle, and it is **billed per execution**. A customer's order is correct whether or not the confirmation email is sent.

**The decoupling guarantee.** The side-effect path is wired so that **a failure in Event Grid publishing, or in the Function itself, cannot roll back or block the saga.** Three properties enforce this:

1. The event is published **after** the durable database commit — the order is already `Confirmed` and saved before any side-effect is attempted.
2. The publish goes through a **never-throws** helper (`TryPublishAsync`): any failure is swallowed and logged, returning `false`; the consumer carries on. There is no transactional coupling between the commit and the publish.
3. The Function runs in a **separate process** (the Function App) reached by a push from Event Grid. If it throws, only that one notification is affected; the order remains confirmed.

**Entra authentication, no key.** The Order service publishes to the topic using `DefaultAzureCredential` against the topic's endpoint hostname (non-secret, committed) — there is **no topic access key**. The publisher identity is granted only **EventGrid Data Sender**. The Function, in turn, reaches any Azure resource it needs (Key Vault, an email service) via its **managed identity**. This continues the secret-less posture from the earlier steps.

### Build

- **A reusable fire-and-forget publisher** lives in `AK.BuildingBlocks.Messaging.EventGrid`: `IEventGridSideEffectPublisher` with a single `TryPublishAsync(eventType, subject, data)` whose contract is that **it never throws**. The implementation builds an `EventGridPublisherClient` from the non-secret `EventGrid:TopicEndpoint` setting and `DefaultAzureCredential`; if the endpoint is missing or invalid the publisher becomes a **safe no-op** (logged once at startup), so a service starts cleanly offline. `AddEventGridSideEffectPublisher()` registers it. The package `Azure.Messaging.EventGrid` is added to BuildingBlocks.
- **The publish happens at the natural domain moment.** The existing `OrderConfirmedConsumer` (AK.Order) already updates the order to `Confirmed` and commits; *after* that commit it calls `TryPublishAsync("AntKart.Order.Confirmed", "orders/{id}", …)`. No new consumer, no change to the saga, no change to the Service Bus wiring — the side-effect is bolted on after the durable work is done. `appsettings` gains a non-secret `EventGrid:TopicEndpoint`.
- **The serverless handler** is a new **.NET 9 isolated** Functions project, `AK.NotificationFunctions`, deployed to `func-antkart-notifications-dev`. `OrderConfirmedNotificationFunction` is an `[EventGridTrigger]` function that, for now, **records** the side-effect (logs the event); actual email delivery is deferred to the test-enablement step. The project carries heavy teaching comments explaining where it sits in the two-mechanism model and why a failure here is harmless to the order.
- **Tests.** The new behaviour is unit-tested with the publisher mocked (no live Azure): the consumer confirms the order **and** publishes the side-effect; and — proving the decoupling — when the publish returns `false`, the order is **still confirmed** and nothing throws. The transport-agnostic in-memory integration harness registers a no-op publisher so the existing saga/consumer tests stay green.

### Execute

Grant your developer identity the publisher role on the topic and set the non-secret endpoint, then run the Order service so it publishes over Entra after a confirmation:

```bash
OBJECT_ID=$(az ad signed-in-user show --query id -o tsv)
TOPIC_ID=$(az eventgrid topic show --name evgt-antkart-dev --resource-group rg-antkart-dev-eastus --query id -o tsv)

az role assignment create --assignee-object-id "$OBJECT_ID" --assignee-principal-type User \
  --role "EventGrid Data Sender" --scope "$TOPIC_ID"
```

The topic endpoint is configured in the Order service's `appsettings.json` (non-secret — committed):

```json
"EventGrid": {
  "TopicEndpoint": "https://evgt-antkart-dev.eastus-1.eventgrid.azure.net/api/events"
}
```

Then run the Order service with an active `az login` session: `cd AK.Order/AK.Order.API && dotnet run`.

### Verify

- The Order service **starts and publishes to Event Grid over Entra** when an order is confirmed — and if the topic is unreachable, the order is **still confirmed** (the publish failure is logged, not thrown).
- The **unit and in-memory integration tests pass** (`dotnet test`) with the publisher mocked — no Event Grid or Function host is required.
- End-to-end delivery — Event Grid **pushing** the event to the deployed Function and the Function sending a real email — is exercised during the **test-enablement step**, not this one.

---

*Subsequent steps are added to this guide as they are delivered.*
