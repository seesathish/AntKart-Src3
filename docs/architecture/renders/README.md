# C4 diagram renders

This folder holds **exported PNG images of the six C4 views** defined in [`../workspace.dsl`](../workspace.dsl). The repository root [`README.md`](../../../README.md) embeds these images for the C4 diagrams (01–05, 11); the Mermaid diagrams (06–10, 12–18) render natively and are not exported here.

> **These images are produced during the C4 redraw and are not committed yet.** The cloud-native C4 views are still being (re)drawn in `workspace.dsl` (the current DSL is the superseded Phase-1 model — see [`../C4Architecture.md`](../C4Architecture.md) and the [Diagram Plan](../DIAGRAM-PLAN.md)). **Until the PNGs are exported, the root README will show broken image links for 01–05 and 11 — that is expected.**

## How to produce them

Run **Structurizr Lite** against the `docs/architecture` folder (which contains `workspace.dsl`), from the repository root:

```bash
docker run -it --rm -p 8080:8080 -v ${PWD}/docs/architecture:/usr/local/structurizr structurizr/lite
```

Then open **http://localhost:8080**, open each view, and use the diagram's **export control to save a PNG**. Structurizr Lite renders the official Azure and Kubernetes icon themes referenced by the workspace.

## View key → filename mapping

Structurizr names each export after the **view key** (typically `structurizr-<ViewKey>.png`), so **each export must be renamed** to the exact filename the root README expects:

| View key (in `workspace.dsl`) | Rename export to | Diagram |
|-------------------------------|------------------|---------|
| `SystemContext` | `01-system-context.png` | 01 · System context (L1) |
| `Containers` | `02-containers.png` | 02 · Container view (L2) |
| `OrderComponents` | `03-order-components.png` | 03 · Component view (L3) |
| `OrderFlow` | `04-saga-flow.png` | 04 · Order saga (dynamic) |
| `AzureTopology` | `05-azure-topology.png` | 05 · Azure resource topology |
| `ClusterTopology` | `11-cluster-topology.png` | 11 · Cluster topology |

The first four view keys exist in `workspace.dsl` today but describe the **Phase-1** model and are being redrawn for the cloud-native platform; `AzureTopology` and `ClusterTopology` are the two **deployment views to be added** during that redraw. Keep the view keys above so the exported filenames match, or adjust both the DSL keys and this table together.
