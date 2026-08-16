# Project state

One file that tells a fresh reader — a person or an AI assistant with no prior context —
exactly where the AntKart platform stands: what is built, what runs, what is outstanding,
and what to do next. It carries no personal or financial information. Every figure below is
drawn from the source documents cited; where a document and reality disagree, the
disagreement is called out.

---

## Where the platform stands

AntKart is **built and delivered**, not a work in progress. The **`dev` environment exists in
Azure** — provisioned entirely from the Terraform/Terragrunt tree in
`infrastructure/environments/dev/` (18 units) — but it is **stopped between sessions** to
control cost. The cluster and database are brought up only when needed and stopped again
afterward.

A second environment, **`qa`, was built** from the same modules with different inputs
(`infrastructure/environments/qa/`, 18 units), **verified end to end** (the full Postman
saga passing over HTTPS), and then **destroyed to control cost**. Its Terraform tree remains
in the repository, and the [provisioning runbook](guides/environment-provisioning-runbook.md)
rebuilds it from an empty subscription. Building and verifying qa is what surfaced most of
the defects now in the register (see *The findings worth knowing*).

So: the code is complete and proven; `dev` is real but usually stopped; `qa` is
reproducible-on-demand from code. The active work is no longer construction — it is
**mastering the material** (the Architect's Playbook) and closing the tracked defects.

## What is proven

Verified against a live system, per the [provisioning runbook](guides/environment-provisioning-runbook.md)
(phases without a `⚠️ UNVERIFIED` marker) and [KNOWN_ISSUES.md](KNOWN_ISSUES.md):

| Capability | What proves it |
|---|---|
| Environment provisioning from an empty subscription (state backend → resource waves) | Runbook **Phases 0–4** run end to end (no markers) |
| GitOps delivery of all six services (Helm chart + Argo CD, secret-less workload identity) | Runbook **sections 5.1–5.7** run end to end against a real build |
| Full orchestrated saga end to end to the **`Paid`** state, and the **`PaymentFailed`** branch | Runbook **Phase 6**; the "AntKart Cloud E2E Saga" Postman run passing over HTTPS |
| Order state machine covers the transitions the saga drives (`Confirmed → Paid` / `PaymentFailed`) | **KI-009 (Resolved)** — found by the saga E2E, fixed in PR #8, re-verified |
| Telemetry correlates across services (a request's trace spans multiple roles) | Runbook **Phase 6.6** multi-role `OperationId` query |
| Public HTTPS entry point with a trusted Let's Encrypt **production** certificate | Runbook Phase 5.7 / [AKS Guide](guides/aks-guide.md); delivered per [ROADMAP](ROADMAP.md) |

## What is outstanding

Ordered by value, not by section. "Where" is where it is documented; "Needs" is the next action.

| Item | Where | Needs |
|---|---|---|
| **KI-014** — MassTransit lacks management-plane rights on Service Bus; messaging breaks **silently** (pods Healthy, Argo Synced) | [KNOWN_ISSUES.md](KNOWN_ISSUES.md) · `infrastructure/environments/*/role-assignments` | An ADR choosing Data Owner vs pre-created topology, and the grant moved **into code** (currently a manual grant lost on rebuild) |
| **KI-013** — configuration changes do not restart pods; every signal reports healthy while pods run **stale config** | [KNOWN_ISSUES.md](KNOWN_ISSUES.md) · `deploy/helm/antkart-service/templates/deployment.yaml` | A `checksum/config` annotation on the pod template (fixes every environment at once) |
| **KI-002** — Discount gRPC decodes the JWT but does not **verify** it (a forged `roles=admin` token would pass) | [KNOWN_ISSUES.md](KNOWN_ISSUES.md) · `AK.Discount/AK.Discount.Grpc/Interceptors/AuthInterceptor.cs` | Real Entra validation (signature/issuer/audience/lifetime), as the REST services do |
| **Runbook §5.8 — CD promotion** `⚠️ UNVERIFIED` | Runbook §5.8 | A live run to verify CD workflows target a new environment, then remove the marker |
| **Runbook §5.9 — Notification path** `⚠️ UNVERIFIED` | Runbook §5.9 | A live run to verify the Event Grid → Functions → ACS email path, then remove the marker |
| **KI-010** — hardcoded budget `start_date` has expired; **dev can no longer be provisioned from its own Terraform** | [KNOWN_ISSUES.md](KNOWN_ISSUES.md) · `infrastructure/environments/dev/governance/terragrunt.hcl` | Derive the date (`formatdate`/`timestamp`) with a `lifecycle` ignore rule |
| **KI-005** — no stock-release compensation on payment failure (reserved stock leaks) | [KNOWN_ISSUES.md](KNOWN_ISSUES.md) · `AK.Order` saga / `PaymentFailedConsumer` | Emit a stock-release/compensation event on `PaymentFailed` and cancellation |
| **KI-003** — gateway CORS allows any origin | [KNOWN_ISSUES.md](KNOWN_ISSUES.md) · `AK.Gateway/AK.Gateway.API/Program.cs` | An explicit allowed-origins list, when a real front-end / APIM edge lands |
| **Stale `InfrastructureAsCode` diagram** — still draws `qa` as a dashed "planned" box | `docs/C4Renders/renders/InfrastructureAsCode.svg` · prose in [1-infrastructure-as-code.md](development/1-infrastructure-as-code.md) | Redraw in the Structurizr source repo (the source is not in this repository, so the SVG cannot be regenerated here); the accompanying prose also still says qa is planned |
| **KI-007** — Key Vault purge protection blocks an early same-name rebuild (7-day name reservation) | [KNOWN_ISSUES.md](KNOWN_ISSUES.md) · `infrastructure/environments/dev/key-vault` | Update the rebuild runbook to wait out the window or use a fresh vault name |
| **KI-011** — two Key Vault secrets have no consumer (a Service Bus SAS string bypasses workload identity) | [KNOWN_ISSUES.md](KNOWN_ISSUES.md) · `kv-antkart-dev` | Confirm no consumer outside the service projects, then delete both |
| **KI-012** — provider lock files are stale in dev (azuread/random effectively unpinned in some units) | [KNOWN_ISSUES.md](KNOWN_ISSUES.md) · `infrastructure/environments/dev/*/.terraform.lock.hcl` | `terragrunt init` across the dev units, commit refreshed locks |
| **KI-004** — a mutable image tag can serve a stale cached image | [KNOWN_ISSUES.md](KNOWN_ISSUES.md) | Mitigated by immutable commit-SHA tags in the CI/CD pipeline |
| **Runbook §6A — API Management edge** `⚠️ UNVERIFIED` (optional, ephemeral) | Runbook §6A | A create-exercise-destroy run to verify, then remove the marker; every step is written from docs, never executed |
| **Architect's Playbook — 70 concepts to prove** (all `🟡`) | [ARCHITECT-PLAYBOOK.md](ARCHITECT-PLAYBOOK.md) | Study and prove each concept (see the section below) — the primary ongoing task |

## The Architect's Playbook — the active work

**[docs/ARCHITECT-PLAYBOOK.md](ARCHITECT-PLAYBOOK.md) is the primary ongoing task.** It holds
**70 concepts across eight sections** (1 Platform · 2 Infrastructure as code · 3 Azure
services · 4 Kubernetes · 5 Security and identity · 6 Observability · 7 GitOps · 8 DevOps).
Every concept is fully written to the same template and carries exactly one status tag:

| Status | Meaning | Count |
|---|---|---|
| 🟡 **To start** | Not yet studied or discussed | **70** |
| 🔵 **In progress** | Studied; some gaps remain | **0** |
| 🟢 **Proven** | Explained aloud without notes, gotchas recalled, code located | **0** |

**The rule is plain:** a concept moves to **🟢 only when it can be explained aloud, without
notes, with its gotchas recalled and its code located.** *Reading it does not move the tag.*

**Recommended study order** (recorded in the playbook's "Where to start" — front-loads the
concepts this platform has the most original material on, because they were learned by
hitting them):

1. **Security and identity** — workload identity, the separate permission planes, DefaultAzureCredential, Entra and PKCE.
2. **Infrastructure as code** — state as memory, remote state and key collisions, modules versus environments.
3. **GitOps** — Argo CD architecture, what Argo does not watch, sync and self-heal.
4. **Kubernetes** — the reconciliation loop, ConfigMaps and Secrets, Helm and what it is not.
5. **Observability** — OpenTelemetry, trace correlation.
6. **Platform** — the outbox, the saga, CQRS.
7. **DevOps**, then **Azure services** — broadest, and the easiest to speak to from existing experience.

## How to work on this

- **Sessions are short — pick one concept, not a section.** Prove it to 🟢, then stop.
- **Most playbook work needs only the repository** — no running cluster. The concept, its
  code location, and its gotcha are all in the tree.
- **To bring the platform up** (only when a live check is genuinely needed):
  `az aks start --name <aks> --resource-group <rg>` and
  `az postgres flexible-server start -g <rg> -n <pg>`. **ALWAYS stop both afterward:**
  `az aks stop …` and `az postgres flexible-server stop …`. A **stopped PostgreSQL Flexible
  Server auto-starts after 7 days**, so set a reminder if it stays down.
- **Changes go through a branch and a pull request, reviewed before merge** — never straight
  to the default branch.
- **Verify against the live system, never against the docs.** The docs are a starting point;
  the running platform is the source of truth (that is the whole point of the `⚠️ UNVERIFIED`
  markers).

## Context for a new reader

AntKart is a **cloud-native e-commerce platform** — six .NET 9 microservices plus a
serverless notifications app, running on Azure Kubernetes Service, provisioned with Terraform
and Terragrunt, and delivered by GitHub Actions and Argo CD, with no stored secrets anywhere.
It exists as a **working demonstration of enterprise cloud-native architecture on Azure** —
Clean Architecture, an event-driven saga, infrastructure as code, a secret-less identity
model, and a managed Kubernetes runtime, built end to end and proven against a live system.
A **public copy lives at [github.com/seesathish/AntKart-Cloud](https://github.com/seesathish/AntKart-Cloud)**.

## The findings worth knowing

The most valuable output of the project: real defects that building and verifying a live
platform exposed. They are what makes the documentation credible — each was found by running
the system, not by reading it.

- **Config changes don't restart pods** — everything reports healthy (Argo `Synced`, revision
  matches HEAD) while pods run stale configuration; only `printenv` in the pod reveals it. Found on the qa build. (**KI-013**)
- **MassTransit needs management-plane rights on Service Bus** — with data-plane roles only,
  topology reconciliation fails `401`, logged as a *warning*, so messaging breaks silently while pods stay Healthy. Found on qa's first startup. (**KI-014**)
- **A hardcoded budget start date silently expires** — dev could no longer be provisioned from
  its own Terraform; invisible until a rebuild. Found by building qa from the same modules. (**KI-010**)
- **Two Key Vault secrets have no consumer** — dormant pre-secret-less-migration residue, one a
  Service Bus SAS string that would bypass workload identity. Surfaced comparing dev's vault to qa's. (**KI-011**)
- **Provider lock files drift** — units locked before the shared `required_providers` block
  record only azurerm, leaving azuread/random effectively unpinned. Surfaced comparing dev to qa. (**KI-012**)
- **Key Vault purge protection blocks a same-name rebuild** — the name stays reserved for 7
  days after deletion, breaking the zero-to-AKS rebuild within that window. (**KI-007**)
- **A missing state-machine edge stalls a paid order** — the order could not advance
  `Confirmed → Paid`; found by the full cloud saga run, not by unit tests. Fixed. (**KI-009, Resolved**)
- **An over-broad CD path filter shipped on a docs-only commit** — a markdown-only change
  triggered five CD pipelines; the negation must be the last path pattern. Fixed. (**KI-006, Resolved**)

---

_Sources: [environment-provisioning-runbook.md](guides/environment-provisioning-runbook.md),
[KNOWN_ISSUES.md](KNOWN_ISSUES.md), [ARCHITECT-PLAYBOOK.md](ARCHITECT-PLAYBOOK.md),
[ROADMAP.md](ROADMAP.md), `docs/adr/` (25 ADRs), and `infrastructure/environments/`
(dev + qa). Counts current as of this file's authoring; re-check the sources if they have moved on._
