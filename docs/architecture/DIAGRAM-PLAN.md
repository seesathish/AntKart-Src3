# Architecture Diagram Plan

> **The plan and contract for the cloud-native diagram set** (visual language, tooling, the list, source of truth). The **12 Mermaid diagrams are now Drafted** in [`diagrams/`](diagrams/); the **6 C4 diagrams remain Planned** (awaiting the `workspace.dsl` redraw). Statuses are tracked in §4.

## 1. Purpose

A single, coherent diagram set for the **cloud-native AntKart platform** — the delivered system of six services on AKS with GitOps delivery, secret-less identity, managed data/messaging, and a managed edge. The set exists so a reader (or interviewer) can move from a one-glance system context down to component internals, and across the operational planes — network, identity, CI/CD, GitOps, observability — with one consistent visual language.

**The existing C4 model and PNG renders in `docs/architecture` are SUPERSEDED.** [`C4Architecture.md`](C4Architecture.md), [`workspace.dsl`](workspace.dsl), [`workspace.json`](workspace.json), and the `c4-*.png` files describe the **earlier Phase 1 platform running locally** (Docker Compose, local RabbitMQ/Keycloak/Mailhog, no cloud managed services, no AKS/GitOps). **All diagrams are being redrawn** for the cloud-native platform. The old assets are **retained until replaced** — each is removed only as its cloud-native successor is produced (see §4), so the reference is never empty.

## 2. Locked visual language

Every element type has one fixed encoding, used identically across **all** diagrams (C4 and Mermaid). This is the whole point of a set — a teal box means the same thing in diagram 02 and diagram 16.

| Element type | Colour | Shape / border |
|---|---|---|
| People / external actors | Slate grey | Person shape |
| External systems (Razorpay, Microsoft Entra ID, GoDaddy, Azure Communication Services) | Mid grey | Rounded, **dashed** border |
| AntKart services | Teal | Rounded box |
| Azure PaaS | Azure blue `#0078D4` | Box with Azure icon |
| Data stores (Cosmos DB, PostgreSQL, Redis) | Deep blue | Cylinder |
| Identity & security (managed identities, federated credentials, RBAC, Key Vault secrets) | Amber | Hexagon |
| Edge & network (Azure API Management, ingress-nginx, TLS/cert-manager) | Purple | Box |
| CI/CD & GitOps (GitHub Actions, Argo CD, ACR image flow) | Green | Box |
| Known issues & gaps | Red | **Dashed** annotation |

**Every diagram carries three things:**
- a **title**;
- a **one-line statement of the question it answers** (placed directly under the title);
- a **legend** mapping the colours/shapes used in that diagram back to this table.

**Orientation.** Any Mermaid flowchart with **more than three nodes MUST use a vertical layout (`flowchart TD`)**. Horizontal layouts push wide on a monitor and force the reader to zoom or scroll sideways before they can grasp the shape, which defeats the purpose of a first-level diagram. Only use `flowchart LR` when the diagram has **three nodes or fewer**, or when **left-to-right IS the meaning** (a strictly linear pipeline of **at most four stages**). `sequenceDiagram` is **exempt** — it is vertical by nature.

## 3. Tooling split — and why

Two tools, chosen per diagram type so each renders where it is read best.

- **C4 via Structurizr DSL** for the **structural** views (context, container, component, deployment, and the dynamic saga). Authored in `workspace.dsl` and rendered with **Structurizr Lite** so the official **Microsoft Azure** and **Kubernetes** icon themes apply. **Caveat:** those icon themes target the **Structurizr viewer** and are **not fully supported by the PlantUML / Mermaid export paths** — so the C4 diagrams are viewed/exported from Structurizr Lite, not re-implemented in Mermaid.
- **Mermaid** for **flows, sequences, and pipelines**, because it renders **natively in Markdown on GitHub** with no image assets to generate, commit, or keep in sync — the operational diagrams live inline in the guides.

**Shared style, so the visual language cannot drift:**
- a single **`classDef` block** (encoding the §2 table) is defined once and **reused in every Mermaid diagram**;
- a **`styles` block** plus **both icon themes** (Azure + Kubernetes) live in **`workspace.dsl`** for the C4 views.

## 4. The diagram set (18)

The **12 Mermaid diagrams (06–10, 12–18) are now Drafted** — produced in `docs/architecture/diagrams/`, pending review. The **6 C4 diagrams (01–05, 11) remain Planned** (authored in `workspace.dsl`, not yet redrawn). "Source of truth in this repo" is where the diagram is authored/kept.

| # | Name | Question it answers | Tool | Source of truth in repo | Status |
|---|------|---------------------|------|-------------------------|--------|
| 01 | L1 System Context | Who uses AntKart and what external systems does it depend on? | C4 | `workspace.dsl` (Structurizr) | Planned |
| 02 | L2 Container — services + Azure PaaS + **APIM** | What are the deployable pieces and the managed services behind the edge? | C4 | `workspace.dsl` | Planned |
| 03 | L3 Component — inside AK.Order | How is AK.Order structured internally (API → application → domain → infrastructure)? | C4 | `workspace.dsl` | Planned |
| 04 | Dynamic — full saga through to Paid | How does an order flow through the orchestrated saga to a Paid state? | C4 | `workspace.dsl` | Planned |
| 05 | Azure resource topology | What Azure resources exist and how are they grouped/related? | C4 (deployment view) | `workspace.dsl` | Planned |
| 06 | Terragrunt unit dependency graph (18 units) | In what order do the IaC units apply, and what depends on what? | Mermaid | `docs/architecture/DIAGRAM-PLAN.md` → the guide it lands in (`infrastructure/README.md`) | Drafted |
| 07 | Network & traffic path — DNS → TLS → **APIM** → ingress → gateway | How does a request physically reach a service, and where is TLS terminated? | Mermaid | `docs/guides/aks-guide.md` | Drafted |
| 08 | Identity & trust chain — Terraform SP → GitHub OIDC → workload identity → RBAC | How does trust flow from provisioning to runtime, secret-lessly? | Mermaid | `docs/guides/identity-concepts.md` | Drafted |
| 09 | Security posture & trust boundaries — secret-less chain, public vs ClusterIP, **KI-002** | What is exposed vs internal, and where do the known gaps sit? | Mermaid | `docs/test/SECURITY_TESTS.md` / `docs/KNOWN_ISSUES.md` | Drafted |
| 10 | **APIM** edge — policy chain, JWT validation, two-gateway model (ADR-020) | What does the managed edge do before traffic reaches the cluster ingress? | Mermaid | `docs/adr/ADR-020-api-management-managed-edge-gateway.md` | Drafted |
| 11 | Cluster topology | How are namespaces, workloads, and ingress laid out inside AKS? | C4 (deployment view) | `workspace.dsl` | Planned |
| 12 | Workload identity token flow | How does a pod get an Entra token with no stored secret? | Mermaid (sequence) | `docs/guides/identity-concepts.md` | Drafted |
| 13 | Helm chart & values precedence | How does one generic chart become six services, and which values win? | Mermaid | `deploy/helm/README.md` | Drafted |
| 14 | CI pipeline | What runs on a pull request, and what gates the merge? | Mermaid | `docs/guides/devops-cicd-guide.md` | Drafted |
| 15 | CD pipeline | What happens on merge — build, push, tag-bump — and with what identity? | Mermaid | `docs/guides/devops-cicd-guide.md` | Drafted |
| 16 | GitOps reconciliation loop | How does a Git change become a running pod via Argo CD? | Mermaid | `docs/guides/gitops-guide.md` | Drafted |
| 17 | Environment promotion — dev vs QA | How does a change move from dev to QA, and what differs between them? | Mermaid | `docs/ROADMAP.md` (until an environments guide exists) | Drafted |
| 18 | Observability pipeline | How do logs, metrics, and traces flow to their sinks and dashboards? | Mermaid | `docs/design/OBSERVABILITY.md` | Drafted |

## 5. Status

**12 Drafted, 6 Planned.** The 12 Mermaid diagrams (06–10, 12–18) are drafted in [`diagrams/`](diagrams/) (indexed in [diagrams/README.md](diagrams/README.md)); the 6 C4 diagrams (01–05, 11) remain Planned pending the `workspace.dsl` redraw. This file is updated as each diagram advances (Planned → Drafted → produced) and as each superseded old asset is removed.

## See also

- [ROADMAP](../ROADMAP.md) — the diagram set is a near-term planned item.
- [C4Architecture.md](C4Architecture.md) — the **superseded** Phase-1 diagrams (retained until replaced).
- [ADR-020](../adr/ADR-020-api-management-managed-edge-gateway.md) — the managed-edge decision reflected in diagrams 02, 07, and 10.
