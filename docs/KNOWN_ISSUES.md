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

### KI-005 · No stock-release compensation on payment failure · Severity: Medium

- **Component:** `AK.Order` saga / payment outcome path — `PaymentFailedConsumer` and the order saga.
- **Impact:** When a payment **fails**, the order is moved to `PaymentFailed`, but the **stock reserved earlier by the saga is not released**. The reservation is retained indefinitely, so inventory stays held for an order that will not be paid — a slow leak of available stock as failed payments accumulate.
- **Current mitigation:** None automated. The reservation can be released manually if needed; failed-payment volume is low in the current usage. Note this is separate from the order **state-machine** fix (Confirmed → Paid / PaymentFailed), which is resolved — this item is only about the **compensation** that should follow a failure.
- **Planned resolution:** A deliberate design decision to defer to a dedicated **compensation workstream** — emit a stock-release/compensation event on `PaymentFailed` (and on cancellation) so the reservation is returned. Not implemented yet; tracked here so it is scheduled, not overlooked.

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

### KI-007 · Key Vault purge protection blocks early teardown/rebuild · Severity: Low

- **Component:** `infrastructure/environments/dev/key-vault` — the `kv-antkart-dev` vault (`purge_protection_enabled = true`, `soft_delete_retention_days = 7`).
- **Impact:** Purge protection was enabled **out of band** on the live vault and Azure **cannot disable it once on**. With it enabled and a **7-day** soft-delete retention, the vault **cannot be purged early**, and after deletion its **globally-unique name (`kv-antkart-dev`) stays reserved for 7 days**. Any teardown-and-rebuild that recreates the vault under the **same name** inside that window fails — which blocks the planned **zero-to-AKS rebuild runbook** (a clean destroy → re-apply of the dev environment).
- **Current mitigation:** The Terraform config now records reality (`purge_protection_enabled = true`), so routine applies no longer fail trying to disable it. For a rebuild within the retention window, either **wait out the 7 days** before recreating, or recreate the vault under a **different name**. **QA is unaffected:** no Azure Policy enforces purge protection (the governance module only creates a budget), so the QA vault can be created with purge protection **disabled** and stays freely disposable.
- **Planned resolution:** Accept purge protection as the standing posture for `kv-antkart-dev` (the safer, production-like default) and update the zero-to-AKS rebuild runbook to account for the name-reservation window (wait it out, or use a fresh vault name). No code change is pending; recorded so the rebuild constraint is scheduled, not overlooked.

---

## Resolved

### KI-006 · CD path filters lacked a markdown exclusion · Resolved

- **Component:** `.github/workflows/*-cd.yml` (all six) and `*-ci.yml` — the `on: push`/`pull_request` **path filters**.
- **Cause:** each workflow filtered on whole service folders (`AK.<Service>/**`) and the shared library (`AK.BuildingBlocks/**`) with **no exclusion for documentation**. Any file under those folders — including `*.md` — matched, so a documentation-only change triggered a full CD run (build image → push to ACR → bump `.image.tag` in Helm values). Because `AK.BuildingBlocks/**` is present in **all six** workflows, a single change to `AK.BuildingBlocks/BUILDING_BLOCKS.md` matched every service.
- **Proof (not theoretical):** commit **`5261027`** changed **only markdown** (diagram docs + Phase-1 SUPERSEDED banners, touching `AK.*/…​.md` and `AK.BuildingBlocks/BUILDING_BLOCKS.md`). It triggered **five** CD pipelines, which each built and pushed an image and bumped the tag — commits **`c2ad36e`** (shoppingcart), **`0f97859`** (gateway), **`7b5280d`** (order), **`1be49cd`** (discount), **`5685cf2`** (payments), all `-> 5261027`.
- **Fix:** append **`- '!**/*.md'`** as the **last** entry of every workflow's `paths` list (later patterns take precedence in GitHub Actions path filters, so the negation must come last). Documentation changes inside a service folder no longer trigger CD or the CI gate. The five already-pushed tag bumps were **left in place** — the images are functionally identical to what preceded them, and reverting would trigger another round of syncs.
- **Lesson:** a positive path filter over a folder is over-broad by default; pair it with a negative pattern (`!**/*.md`, or narrower globs) so non-shipping files cannot drive delivery.

| ID | Component | Resolution |
|----|-----------|------------|
| KI-001 | AK.Discount.Grpc | The gRPC `AuthInterceptor` read a nested, provider-specific role-claim structure, so the admin write RPCs would fail authorization once tokens carried roles in a flat `roles` claim. **Resolved in the identity migration to Microsoft Entra ID:** the interceptor now reads the flat top-level `roles` claim, consistent with the shared `AK.BuildingBlocks` authentication. (Note: correct role *reading* was restored; cryptographic token *validation* remains open — see KI-002.) |
| — | AK.Gateway | **Ocelot downstream host drift resolved.** `ocelot.json` used Docker-era hostnames (`ak-*-api`) while the deployed Helm ConfigMap used the Kubernetes Service names (`ak-*`); `ocelot.json` was reconciled to the `ak-*` Service names so source and deployed config agree. |
| — | AK.Gateway | **Development-profile startup failure resolved.** `Program.cs` loaded `ocelot.json` and `ocelot.Development.json` via `AddJsonFile`, which merged their `Routes` arrays by index and produced a duplicate upstream route so Ocelot refused to start. The gateway now loads exactly one ocelot file selected by environment; verified to start and route correctly in both Development and Production. |
| — | Documentation | **Broken anchor links resolved.** `DevelopmentGuide.md` linked `README.md#architecture-overview`, which did not resolve (the heading is "Architecture"); corrected, and a repo-wide anchor-link sweep fixed any others. |

## Notes

- Resolved items are recorded here (and in the change history) once verified.
- New deferred issues are added under **Open** with a unique `KI-NNN` id, a severity, the affected component, the impact, the current mitigation, and a planned resolution.
