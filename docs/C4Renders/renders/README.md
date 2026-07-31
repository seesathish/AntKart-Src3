# C4 diagram renders

This folder holds the **exported SVG images** of the eight hero diagrams. The repository root [`README.md`](../../../README.md) embeds them (light + dark). Each hero diagram is authored in its own Structurizr workspace under `docs/C4Renders/hero-*/`, so the diagrams can be edited and rendered independently.

## The eight hero workspaces

| Folder | View key | Exported files | Status |
|--------|----------|----------------|--------|
| [`../hero-system/`](../hero-system/) | `SystemOverview` | `SystemOverview.svg` · `SystemOverview-dark.svg` | Drawn |
| _hero-platform (folder removed — source in git history)_ | `PlatformArchitecture` | `PlatformArchitecture.svg` · `PlatformArchitecture-dark.svg` | Drawn |
| [`../hero-infrastructure/`](../hero-infrastructure/) | `InfrastructureAsCode` | `InfrastructureAsCode.svg` · `InfrastructureAsCode-dark.svg` | Scaffolded (DSL only) |
| [`../hero-azure/`](../hero-azure/) | `AzureServices` | `AzureServices.svg` · `AzureServices-dark.svg` | Scaffolded (DSL only) |
| [`../hero-kubernetes/`](../hero-kubernetes/) | `Kubernetes` | `Kubernetes.svg` · `Kubernetes-dark.svg` | Scaffolded (DSL only) |
| [`../hero-devops/`](../hero-devops/) | `DevOps` | `DevOps.svg` · `DevOps-dark.svg` | Scaffolded (DSL only) |
| [`../hero-observability/`](../hero-observability/) | `Observability` | `Observability.svg` · `Observability-dark.svg` | Scaffolded (DSL only) |
| [`../hero-security/`](../hero-security/) | `Security` | `Security.svg` · `Security-dark.svg` | Scaffolded (DSL only) |

"Scaffolded" means `workspace.dsl` exists with `autoLayout` still ON — the diagram has not been hand-arranged or exported yet.

## The working loop (per hero folder)

Do this one folder at a time. Each `hero-*/workspace.dsl` header carries the exact `docker run` command for that folder.

1. **Create or edit the DSL** in `hero-<topic>/workspace.dsl`.
2. **Run Structurizr Lite against that hero folder** (not the parent) — from the repository root, mounting the single folder:
   ```bash
   docker run -it --rm -p 8080:8080 \
     -v ${PWD}/docs/C4Renders/hero-<topic>:/usr/local/structurizr \
     structurizr/structurizr local
   ```
   Open **http://localhost:8080**.
3. **Iterate with F5.** After every save, refresh the browser with **F5**. **Never restart the container** — it watches the file.
4. **Tune with autoLayout ON** to get the rough shape (the flow and spacing).
5. **Comment out `autoLayout` before dragging.** While `autoLayout` is present the engine recalculates on every load and discards hand placement. Comment out the `autoLayout` line, refresh, and only then start arranging.
6. **Arrange by hand.** Placement autosaves into `workspace.json` in that hero folder every few seconds.
7. **Export SVG in both themes.** Export the view once in the **light** theme and once in the **dark** theme.
8. **Name the files** after the view key and drop them in this folder:
   - light → `renders/<ViewKey>.svg`
   - dark → `renders/<ViewKey>-dark.svg`
9. **Commit the set together:** the hero folder's `workspace.dsl` **and** `workspace.json`, plus both SVGs in `renders/`.

## What is and isn't committed

- **Commit:** `hero-*/workspace.dsl`, `hero-*/workspace.json` (generated when you hand-arrange — commit it so the layout is reproducible), and the two SVGs per diagram in this folder.
- **Do not commit:** `hero-*/.structurizr/` — that is Structurizr Lite's generated cache (Lucene index + thumbnails). It is **gitignored** (`**/.structurizr/`).
