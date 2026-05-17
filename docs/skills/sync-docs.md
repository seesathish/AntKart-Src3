# Skill: Sync Documentation After a Code Change

**Purpose:** After any code change, run through the complete documentation sync checklist to ensure all docs stay accurate and in sync with the code. Documentation drift is as harmful as a bug — it misleads future developers and causes incorrect integration assumptions.

---

## When to Use
- After adding or changing an endpoint
- After adding or changing an integration event or consumer
- After adding a new service
- After changing a shared BuildingBlocks component
- After changing auth, security, or routing
- Before every commit (treat this as a pre-commit checklist)

---

## The Five Documentation Surfaces

| Surface | File(s) | Updated When |
|---------|---------|--------------|
| Service design doc | `AK.<Service>/<SERVICE>_TECHNICAL_DESIGN.md` | Any change to that service |
| Solution overview | `CLAUDE.md` | Service added, pattern changed, test count changed |
| README | `README.md` | Service added, port changed, diagram changed |
| Architecture decisions | `docs/adr/ADR-00X-*.md` | A key architectural decision changes |
| API contract | `AntKart.postman_collection.json` | Endpoint added, removed, or path changed |
| Event bus map | `EVENTBUS.md` | Integration event or consumer added/changed |
| Security guide | `SECURITY_TESTS.md` | Auth pattern changed, vulnerability fixed |
| Observability | `OBSERVABILITY.md` | New service added (new log streams) |
| Gateway guide | `AK.Gateway/API_GATEWAY.md` | Route added or changed |

---

## Step 1 — Service Design Doc (`<SERVICE>_TECHNICAL_DESIGN.md`)

For every change to a service, check these sections in its design doc:

| Section | Update needed when |
|---------|--------------------|
| **API Endpoints table** | Endpoint added, removed, path changed, auth changed |
| **Domain Model** | Entity fields added/removed, value object changed |
| **Integration Events** | Event published or consumed added/removed |
| **Database / Schema** | EF migration applied (new table, column, index) |
| **Security** | Auth policy changed, IDOR pattern changed |
| **Tests** | Test count changed (update the count in the doc) |
| **Patterns** | New pattern introduced (e.g. first use of Result<T>) |
| **Related ADRs** | New ADR created that applies to this service |

**Quick check:**
```bash
# Find the design doc for a service
ls AK.<Service>/*_TECHNICAL_DESIGN.md
```

---

## Step 2 — CLAUDE.md

CLAUDE.md is the canonical reference for all conventions. Update when:

- **Completed Services section:** Test count changed, new endpoint added, new pattern used
- **BuildingBlocks section:** New extension method, new shared type, existing type changed
- **Architecture Rules / Coding Conventions:** Any new rule or exception agreed by the team
- **NuGet packages table:** New package approved
- **Build & Test Commands:** New command documented

```bash
# Check current test counts in CLAUDE.md
grep -A 2 "Tests:" CLAUDE.md | grep -E "[0-9]+ passing"
```

Update counts after running `dotnet test`. The count is in the Completed Services section for each service.

---

## Step 3 — README.md

Update when:

- **New service added:** Solution Structure tree + Microservices table + Tests table
- **Port changed:** Microservices table Docker port column
- **Authorization changed:** Authorization table row
- **Diagram change:** Topology diagram or SAGA+Notifications flow diagram (Mermaid)
- **New cross-cutting doc added:** Cross-Cutting table row + solution structure tree
- **Swagger URL changed:** Any URL in the table

```bash
# Spot-check key sections
grep -n "| AK\." README.md          # microservices table
grep -n "| 20[0-9][0-9]\|Tests" README.md  # test count table
```

---

## Step 4 — Architecture Decision Records (`docs/adr/`)

Create or update an ADR when:
- A **new technology** is adopted (new DB, new framework, new messaging pattern)
- A **significant pattern** is introduced or changed (e.g. switching from Result<T> to exceptions for a service)
- A design decision was debated and a direction chosen

ADR format:
```markdown
# ADR-00X — Title

**Status:** Accepted  
**Date:** YYYY-MM-DD  
**Author:** Name

## Context
Why this decision was needed.

## Decision
What was decided.

## Consequences
Trade-offs, what becomes easier, what becomes harder.
```

After creating an ADR:
1. Link it from the affected service design doc ("Related ADRs" table)
2. Link it from CLAUDE.md (if it affects a cross-cutting concern)

---

## Step 5 — Postman Collection (`AntKart.postman_collection.json`)

Update when:
- **New endpoint added:** Add a request to the service's folder
- **Route path changed:** Update the URL in existing requests
- **Request body changed:** Update the body schema
- **Auth changed:** Update the auth type on the request

```bash
# Validate the collection parses as JSON
python3 -c "import json; json.load(open('AntKart.postman_collection.json')); print('JSON valid')"
```

For each new endpoint, a Postman request needs:
- Name matching the endpoint's `.WithName("...")` value
- Method + URL (use `{{<service>Url}}` collection variable)
- Headers: `Content-Type: application/json`, `Authorization: Bearer {{accessToken}}`
- Sample request body (for POST/PUT)
- Expected response example in the description

---

## Step 6 — EVENTBUS.md

Update when any of these change:
- Integration event added → Exchanges table
- Consumer added → Queues table
- SAGA step changed → SAGA flow section
- Dead-letter queue handling changed

```bash
# Check current exchange and queue tables
grep -A 3 "exchange\|queue" EVENTBUS.md | head -40
```

---

## Step 7 — SECURITY_TESTS.md

Update when:
- New endpoint added that involves user data → add to test 4/4b/15 examples
- Auth policy changed → update test 3 expected results
- Rate limit changed → update test 9 table
- Vulnerability fixed → update Vulnerability History table and test result baseline

---

## Step 8 — OBSERVABILITY.md

Update when a new service is added:
- Add the service to the "Per-service log events" table
- Add its index prefix if different from `antkart-logs-*`

---

## Step 9 — API_GATEWAY.md

Update when a route is added or changed:
- Routing table entry
- Rate limits section if a limit changed

---

## Step 10 — This Checklist Itself

If the team adopts a new documentation surface, add it to this file and to the skill's checklist.

---

## Quick Checklist (Run Before Every Commit)

**Service-level changes:**
- [ ] `<SERVICE>_TECHNICAL_DESIGN.md` → API endpoints, domain model, events, test count
- [ ] `AntKart.postman_collection.json` → request added/updated for any new/changed endpoint

**If events changed:**
- [ ] `EVENTBUS.md` → Exchanges table, Queues table

**If auth or security changed:**
- [ ] `SECURITY_TESTS.md` → baseline table, vulnerability history

**If a new service was added:**
- [ ] `CLAUDE.md` → Completed Services section
- [ ] `README.md` → Solution Structure tree, Microservices table, Tests table, Authorization table
- [ ] `OBSERVABILITY.md` → Per-service log events table
- [ ] `AK.Gateway/API_GATEWAY.md` → routing table

**If an architectural decision was made:**
- [ ] `docs/adr/` → new ADR file
- [ ] Link the ADR from affected service design doc(s)
- [ ] Link the ADR from `CLAUDE.md` if cross-cutting

**Always:**
- [ ] `dotnet build` → 0 errors
- [ ] `dotnet test` → all pass, count ≥ baseline
