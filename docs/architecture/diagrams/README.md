# Architecture Diagrams

The cloud-native diagram set for AntKart, per the [Diagram Plan](../DIAGRAM-PLAN.md). Every diagram follows the plan's locked visual language and shared `classDef` block.

- **C4 diagrams (01–05, 11)** are authored in [`workspace.dsl`](../workspace.dsl) (Structurizr) and are **not yet redrawn** for the cloud-native platform — see the SUPERSEDED note in [C4Architecture.md](../C4Architecture.md).
- **Mermaid diagrams (06–10, 12–18)** render natively in GitHub Markdown and live in this folder, one file each.

| # | Name | Tool | Status | Source |
|---|------|------|--------|--------|
| 01 | L1 System Context | C4 | Planned | `workspace.dsl` |
| 02 | L2 Container — services + Azure PaaS + APIM | C4 | Planned | `workspace.dsl` |
| 03 | L3 Component — inside AK.Order | C4 | Planned | `workspace.dsl` |
| 04 | Dynamic — full saga through to Paid | C4 | Planned | `workspace.dsl` |
| 05 | Azure resource topology | C4 | Planned | `workspace.dsl` |
| 06 | Terragrunt unit dependency graph | Mermaid | Drafted | [06-terragrunt-dependencies.md](06-terragrunt-dependencies.md) |
| 07 | Network & traffic path | Mermaid | Drafted | [07-network-traffic-path.md](07-network-traffic-path.md) |
| 08 | Identity & trust chain | Mermaid | Drafted | [08-identity-chain.md](08-identity-chain.md) |
| 09 | Security posture & trust boundaries | Mermaid | Drafted | [09-security-posture.md](09-security-posture.md) |
| 10 | APIM edge & two-gateway model | Mermaid | Drafted | [10-apim-edge.md](10-apim-edge.md) |
| 11 | Cluster topology | C4 | Planned | `workspace.dsl` |
| 12 | Workload identity token flow | Mermaid (sequence) | Drafted | [12-workload-identity-token-flow.md](12-workload-identity-token-flow.md) |
| 13 | Helm chart & values precedence | Mermaid | Drafted | [13-helm-chart-values.md](13-helm-chart-values.md) |
| 14 | CI pipeline | Mermaid | Drafted | [14-ci-pipeline.md](14-ci-pipeline.md) |
| 15 | CD pipeline | Mermaid | Drafted | [15-cd-pipeline.md](15-cd-pipeline.md) |
| 16 | GitOps reconciliation loop | Mermaid | Drafted | [16-gitops-reconciliation.md](16-gitops-reconciliation.md) |
| 17 | Environment promotion — dev vs QA | Mermaid | Drafted | [17-env-promotion.md](17-env-promotion.md) |
| 18 | Observability pipeline | Mermaid | Drafted | [18-observability.md](18-observability.md) |

**Status vocabulary:** Planned (not started) · Drafted (produced, pending review) · (a later state records final acceptance).

> Note on numbering: the file numbers follow the [Diagram Plan](../DIAGRAM-PLAN.md) §4 table (12 = workload identity, 13 = Helm, 14 = CI, 15 = CD, 16 = GitOps loop), which is the canonical list.
