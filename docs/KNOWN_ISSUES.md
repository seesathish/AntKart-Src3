# Known Issues Register

The authoritative record of known defects and deferred fixes. Each item is acknowledged and has a planned resolution — items here are scheduled, not overlooked. Each has a unique `KI-NNN` id, a **severity** (High / Medium / Low), its **impact**, the **current mitigation**, and the **planned resolution**.

Related: the [Platform Roadmap](ROADMAP.md) tracks delivered/in-progress/planned work; this register tracks defects specifically.

---

## Open

### KI-002 · Discount gRPC accepts unverified tokens · Severity: High

- **Component:** `AK.Discount/AK.Discount.Grpc` — `Interceptors/AuthInterceptor.cs`, `Program.cs`.
- **Impact:** The interceptor calls `JwtSecurityTokenHandler.ReadJwtToken`, which **decodes** the bearer token **without verifying its signature, issuer, audience, or expiry**, then authorizes the admin write RPCs (create/update/delete discount) on a `roles=admin` claim. `Program.cs` registers no authentication middleware. A **forged, unsigned token** containing `roles=admin` would therefore be accepted for those RPCs.
- **Current mitigation:** The service is **`ClusterIP`-only** (no ingress, never exposed outside the cluster) and is reached solely by the Products service over cluster DNS, so the vulnerable surface is not externally reachable. Read-only RPCs (the normal discount lookup) do not depend on this check.
- **Planned resolution:** Replace the decode-only check with **proper token validation** (signature, issuer, audience, lifetime — the same Entra validation the REST services use via `AK.BuildingBlocks`) as part of the security workstream (see the [Roadmap security programme](ROADMAP.md#planned--future)).

### KI-003 · Gateway CORS allows any origin · Severity: Medium

- **Component:** `AK.Gateway/AK.Gateway.API/Program.cs` — the `AllowAll` CORS policy (`AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()`).
- **Impact:** The gateway accepts cross-origin requests from **any** origin. Acceptable for development, but too permissive for production, where the browser same-origin protections it relaxes should be scoped to the known front-end origins.
- **Current mitigation:** Authentication and authorization are still enforced (Entra JWT at the edge and in each service), so permissive CORS does not by itself grant access; and no production front end is yet served from this edge.
- **Planned resolution:** Replace `AllowAnyOrigin` with an explicit allowed-origins list sourced from configuration, applied when the managed edge (Azure API Management, [ADR-020](adr/ADR-020-api-management-managed-edge-gateway.md)) and a real front-end origin are in place.

### KI-004 · Mutable image tag can serve a stale image · Severity: Low

- **Component:** Container image delivery (all services) — mutable tag (e.g. `dev`) with `imagePullPolicy: IfNotPresent`.
- **Impact:** A node that has already cached a tag keeps serving the **old** image after a new image is pushed to the **same** tag, so a code change appears not to deploy.
- **Current mitigation:** Delete the pod (or roll the Deployment) to force a fresh pull; documented in the [AKS Guide troubleshooting](guides/aks-guide.md#troubleshooting).
- **Planned resolution:** Tag images with the **immutable commit SHA** so every rollout references a distinct image; adopted with the CI/CD delivery pipeline ([ADR-022](adr/ADR-022-cicd-github-actions-oidc.md)).

---

## Resolved

| ID | Component | Resolution |
|----|-----------|------------|
| KI-001 | AK.Discount.Grpc | The gRPC `AuthInterceptor` read a nested, provider-specific role-claim structure, so the admin write RPCs would fail authorization once tokens carried roles in a flat `roles` claim. **Resolved in the identity migration to Microsoft Entra ID:** the interceptor now reads the flat top-level `roles` claim, consistent with the shared `AK.BuildingBlocks` authentication. (Note: correct role *reading* was restored; cryptographic token *validation* remains open — see KI-002.) |
| — | AK.Gateway | **Ocelot downstream host drift resolved.** `ocelot.json` used Docker-era hostnames (`ak-*-api`) while the deployed Helm ConfigMap used the Kubernetes Service names (`ak-*`); `ocelot.json` was reconciled to the `ak-*` Service names so source and deployed config agree. |
| — | AK.Gateway | **Development-profile startup failure resolved.** `Program.cs` loaded `ocelot.json` and `ocelot.Development.json` via `AddJsonFile`, which merged their `Routes` arrays by index and produced a duplicate upstream route so Ocelot refused to start. The gateway now loads exactly one ocelot file selected by environment; verified to start and route correctly in both Development and Production. |
| — | Documentation | **Broken anchor links resolved.** `DevelopmentGuide.md` linked `README.md#architecture-overview`, which did not resolve (the heading is "Architecture"); corrected, and a repo-wide anchor-link sweep fixed any others. |

## Notes

- Resolved items are recorded here (and in the change history) once verified.
- New deferred issues are added under **Open** with a unique `KI-NNN` id, a severity, the affected component, the impact, the current mitigation, and a planned resolution.
