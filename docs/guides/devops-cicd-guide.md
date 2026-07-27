# DevOps CI/CD Guide

This guide explains how a code change becomes a running pod on the cluster, and how the pipelines are built. It is written for a reader learning CI/CD — every concept is explained rather than assumed. The design decisions behind it are recorded in [ADR-023](../adr/ADR-023-cicd-pipeline-design-and-repository-strategy.md) (pipeline design and repository strategy) and [ADR-022](../adr/ADR-022-cicd-github-actions-oidc.md) (GitHub Actions + OIDC to Azure).

The pattern is being established with **one service — Products — first**, then templated to the others. Today the **CI (pull-request) workflow** is in place; the **CD (merge) workflow** follows next.

---

## What CI/CD means here

- **CI — Continuous Integration.** Every proposed change is automatically built and checked before it can merge. The goal is that `master` is always in a known-good state: it compiles, its tests pass, and it clears the quality and security bars. CI produces a **verdict**, not a deployment.
- **CD — Continuous Delivery.** Once a change is merged, it is automatically turned into a deployable artifact (a container image) and delivered to the cluster. Here, "delivered" means **written into Git** for Argo CD to apply — not pushed directly.

The two are **separate workflows** with different triggers, purposes, and permissions (this is *Pattern B* in ADR-023). CI guards the door; CD walks through it after the merge.

---

## The end-to-end journey — from code change to running pod

```
 developer                GitHub                          Azure / cluster
 ─────────                ──────                          ───────────────
 1. push branch,   ──►   2. CI workflow runs on the PR
    open PR                 build → test(+coverage)
                           → SonarCloud → Trivy
                              │
                              ▼  all checks green (required)
 3. review + merge  ──►   4. CD workflow runs on master
    to master               build immutable image (tag = commit SHA)
                           → push image to ACR  (auth via OIDC, no secret)
                           → update image tag in Git (a commit)
                              │
                              ▼
                         5. Argo CD (in-cluster) sees Git changed
                           → syncs → rolling update  ──►  6. new pods live
```

1. **A developer opens a pull request.** The change lives on a branch; the PR is the proposal to merge it into `master`.
2. **CI runs as the quality gate.** The Products CI workflow builds the service, runs the unit and in-memory integration tests with coverage, runs SonarCloud static analysis, and runs Trivy security scanning. These appear as **status checks** on the PR.
3. **Review and merge.** A human reviews; branch protection requires the CI checks to be green before the merge button is enabled. Merging produces one commit on `master`.
4. **CD runs on the merge.** The CD workflow (next step of this build) builds an **immutable image** tagged with the commit SHA, pushes it to the Azure Container Registry (authenticating with an **OIDC token exchanged for an Entra token — no stored secret**), and then **updates the service's image tag in Git** (a commit to the values the cluster watches).
5. **Argo CD reconciles.** Argo CD runs *inside* the cluster, watches Git, notices the tag changed, and performs a rolling update to the new image. See the [GitOps Guide](gitops-guide.md).
6. **New pods are live.** The Deployment rolls the ReplicaSet to the new image; the old pods drain once the new ones are Ready.

No step 4–6 command runs against the cluster from GitHub. The only actor that changes the cluster is Argo CD, from within it.

---

## Why CD is GitOps, not a push deploy

A traditional pipeline *pushes*: it holds cluster credentials and runs `kubectl`/`helm` from the CI runner into the cluster. This platform does the opposite — CD's final act is a **Git commit that changes the desired image tag**, and the in-cluster Argo CD *pulls* that change and applies it. The benefits:

- **CI/CD holds no cluster credentials.** The runner never authenticates to the Kubernetes API. It needs only rights to push an image (via OIDC) and to commit to the repository. This keeps the platform's secret-less posture intact (ADR-022).
- **Every deployment is an auditable, revertible commit.** What is running is exactly what Git says; rolling back is a `git revert`, not a scramble to remember the last-known-good image.
- **One source of truth.** The same Git state drives Argo CD whether a human or a pipeline wrote it — no out-of-band cluster mutations to reconcile.

CD *builds and publishes*; Argo CD *deploys*. The handoff between them is Git.

---

## The per-service workflow layout

Workflows are **per service and path-filtered**, so a change to one service does not build the others. Each service has two workflow files (Pattern B):

| Workflow | Trigger | Purpose | Touches the cluster? |
|----------|---------|---------|----------------------|
| `<service>-ci.yml` | `pull_request` affecting that service | Quality gate: build, test, SonarCloud, Trivy | No |
| `<service>-cd.yml` | push to `master` affecting that service | Build + push image, update image tag in Git | No — writes Git; Argo CD deploys |

The Products CI workflow is [`.github/workflows/products-ci.yml`](../../.github/workflows/products-ci.yml). Its `pull_request` trigger is **path-filtered** to the paths that can affect Products:

- `AK.Products/**` — the service itself
- `AK.BuildingBlocks/**` — the shared library every service compiles against
- `AK.IntegrationTests/**` — the in-memory integration tests that exercise Products (and related) flows
- `nuget.config` — package-restore behaviour affects the build
- the workflow file itself — so a change to the gate is validated by the gate

The workflow is structured as **three jobs**, so a reviewer sees the gate stages distinctly:

1. **`build-test`** — restores, builds the deployable API in Release, and runs both test tiers with coverage. This is the fast compile-and-test gate; it fails early on a broken build or a failing test.
2. **`sonar`** — runs SonarCloud analysis (see below). It `needs: build-test`, so analysis is not spent on a commit that does not even compile.
3. **`trivy`** — security scanning (see below). Independent of the build, so it runs in parallel as its own stage.

### The two test tiers in CI

CI runs the tiers that need **no live infrastructure**, so the gate is fast and deterministic:

- **Unit tests** — `AK.Products.Tests` (pure, mocked dependencies).
- **In-memory integration tests** — `AK.IntegrationTests`, using the **MassTransit in-memory harness**: the event-driven flows are exercised with no broker, no database, and no running host.

Live end-to-end verification against the running cluster is a **separate, post-deploy concern** (see the [Developer Test Guide](../test/DevTestGuide.md)), not a PR gate — it needs a deployed environment and would make the gate slow and flaky.

### .NET version and coverage format

- **.NET SDK 9.0.x.** Detected from the repository, not guessed: there is no `global.json`, and every project sets `<TargetFramework>net9.0</TargetFramework>`. The workflow pins `DOTNET_VERSION: '9.0.x'`.
- **Coverage in OpenCover format.** The test projects already reference `coverlet.collector`; the workflow collects coverage with `--collect:"XPlat Code Coverage;Format=opencover"`. **OpenCover** is chosen because SonarCloud consumes it natively for .NET via `sonar.cs.opencover.reportsPaths` — no format conversion step.

---

## How SonarCloud fits as a PR gate

**SonarCloud** is a hosted static-analysis service: it inspects the code for bugs, code smells, security hotspots, duplication, and — wired correctly — **test coverage**, and reports back on the pull request.

For .NET, analysis uses the **SonarScanner for .NET** in a three-step wrapper around the build:

```
dotnet sonarscanner begin /k:<projectKey> /o:<org> /d:sonar.token=$SONAR_TOKEN \
       /d:sonar.host.url=https://sonarcloud.io \
       /d:sonar.cs.opencover.reportsPaths="**/coverage.opencover.xml"
dotnet build   … (Release)
dotnet test    … (produces the OpenCover coverage reports)
dotnet sonarscanner end /d:sonar.token=$SONAR_TOKEN
```

- **`begin`** starts analysis and registers where to find coverage (`sonar.cs.opencover.reportsPaths`). The scanner hooks into MSBuild, so it must wrap a real **build** — it cannot analyse a prebuilt output. That is why the `sonar` job runs its own build rather than reusing `build-test`'s.
- **`build` + `test`** compile the code the scanner observes and produce the OpenCover reports.
- **`end`** uploads the analysis and coverage to SonarCloud, which posts the result on the PR.

Configuration in the workflow: organization **`seesathish`**, project key **`seesathish_AntKart-Src3`** (the repository-level SonarCloud project; a single project key per repo — per-service Sonar projects are a future option in ADR-023), and the coverage path wired to the OpenCover reports so **SonarCloud shows real coverage**, not zero.

- **`SONAR_TOKEN` is a GitHub Actions secret** — a SonarCloud token, not a cloud credential. It already exists in the repository secrets. It is the only secret CI needs.
- **Fork PRs.** GitHub does not pass secrets to workflows triggered by PRs from forks, so the `sonar` job cannot authenticate on a fork PR. For an internal, same-repo branch flow this does not arise; handling fork contributions (e.g. a `pull_request_target` variant) is a later refinement.

---

## How Trivy fits as a PR gate

**Trivy** is an open-source security scanner. In this gate it runs two scans and **fails the job** on anything at or above a configurable severity — the single knob `TRIVY_SEVERITY`, set to **`HIGH,CRITICAL`** (so LOW/MEDIUM findings are reported by tooling but do not block a merge):

1. **Filesystem / dependency scan** of the Products tree (`scan-type: fs`, scanners `vuln,secret,misconfig`) — vulnerable dependencies, accidentally-committed secrets, and IaC/Dockerfile misconfiguration.
2. **Dockerfile scan** (`scan-type: config`) of `AK.Products/AK.Products.API/Dockerfile` — image-build misconfiguration (e.g. running as root, missing pinned bases).

Both use `exit-code: 1`, so a finding at the threshold fails the check and blocks the merge. `ignore-unfixed` is a commented switch to optionally suppress vulnerabilities that have no fix available yet.

> **Action supply chain (ADR-022).** Every third-party Action in the Products CI workflow (including Trivy) is **pinned to an immutable commit SHA** — a version tag can be repointed, a commit SHA cannot — with the release version kept as a trailing `# vX.Y.Z` comment for readability. When bumping an Action, look up the SHA its new release tag points to and update both the SHA and the comment.

---

## Branch protection — making CI a required gate

Branch protection is what turns "the checks ran" into "the checks *must pass* before merge". Configure it on `master` (GitHub → **Settings → Branches → Add branch ruleset / protection rule**, or **Settings → Rules**):

1. **Branch name pattern:** `master`.
2. **Require a pull request before merging** — no direct pushes to `master`; changes arrive only via PR. (Optionally require at least one approving review.)
3. **Require status checks to pass before merging**, and mark these checks **required** (they appear once the workflow has run at least once so GitHub knows their names):
   - `build-test`
   - `sonar`
   - `trivy`
4. **Require branches to be up to date before merging** — the PR must include the latest `master` so the checks reflect the post-merge state.
5. **(Recommended)** Include administrators, and block force-pushes and deletions of `master`.

With this in place, a PR cannot merge until `build-test`, `sonar`, and `trivy` are all green — CI is a genuine gate, not just a report.

### Secrets and OIDC

- **`SONAR_TOKEN`** — a GitHub Actions repository secret (SonarCloud token). It **must exist** for the `sonar` job to authenticate; it already does. This is the only secret CI uses.
- **Azure authentication uses OIDC — no stored secret.** When the CD workflow lands, it authenticates to Azure (to push the image) by exchanging a short-lived **GitHub OIDC token** for an Entra token via a **federated credential**, scoped to this repository/branch/environment. Nothing long-lived is stored in GitHub (ADR-022). CI, being a pure quality gate, needs no Azure access at all.

---

## What comes next (CD)

The CD workflow (`products-cd.yml`) will trigger on push to `master` affecting Products and will:

1. Build an **immutable image** tagged with the commit SHA.
2. Authenticate to ACR via **OIDC** and push it.
3. **Update the Products image tag in Git**, confined to the values the cluster watches and guarded against retriggering the pipeline.
4. Leave the deployment to **Argo CD**, which reconciles the change into a rolling update.

It will be documented here as it is delivered.

---

## See also

- [ADR-023 — CI/CD pipeline design and repository strategy](../adr/ADR-023-cicd-pipeline-design-and-repository-strategy.md) — the decisions behind this guide.
- [ADR-022 — CI/CD on GitHub Actions with OIDC](../adr/ADR-022-cicd-github-actions-oidc.md) — the platform and authentication choice.
- [GitOps Guide](gitops-guide.md) — how Argo CD turns a Git change into a rolling update.
- [Operations Command Reference](operations-command-reference.md) — the `az`/`kubectl`/`helm`/`argocd` commands with flags explained.
- [`.github/workflows/products-ci.yml`](../../.github/workflows/products-ci.yml) — the Products CI workflow this guide describes.
