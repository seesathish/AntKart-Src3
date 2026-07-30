# 12 · Workload identity token flow

> **Question:** How does a pod get an Entra token with no stored secret?

A sequence — so this diagram is a `sequenceDiagram` and, unlike the flowcharts in this set, does not carry the shared `classDef` block (Mermaid sequence diagrams don't support it). Participants are named by their role in the visual language instead.

```mermaid
sequenceDiagram
    autonumber
    participant Pod as Pod ak-service
    participant SA as ServiceAccount
    participant Kubelet as kubelet
    participant Entra as Microsoft Entra ID
    participant PaaS as Azure PaaS
    Note over Pod,SA: SA annotated azure.workload.identity/client-id = id-ak-service-dev
    Kubelet->>Pod: project short-lived SA token (OIDC JWT)
    Pod->>Entra: exchange SA token via federated credential<br/>(trusts AKS OIDC issuer + subject)
    Entra-->>Pod: Entra access token — no stored secret
    Pod->>PaaS: call with Entra token
    PaaS-->>Pod: data (RBAC-authorized)
```

## What to notice

- **The ServiceAccount annotation is the anchor:** `azure.workload.identity/client-id` on the pod's ServiceAccount ties it to a specific `id-ak-<service>-dev` managed identity.
- **The projected token is short-lived** and is a Kubernetes-issued OIDC JWT — never a long-lived Azure credential.
- **Entra does the exchange, not the app:** the federated credential (created by the `workload-identity` module) trusts the **AKS OIDC issuer** for the pod's exact subject, and only then returns an Entra access token.
- **Nothing is stored:** no client secret, connection string, or key sits in the pod — this is what `DefaultAzureCredential` resolves to in-cluster.
- The returned token is RBAC-scoped to the roles that identity holds (see diagram 08).
