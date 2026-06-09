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

---

*Subsequent steps — managed data store, messaging, identity, and serverless eventing migrations — are added to this guide as they are delivered.*
