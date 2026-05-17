# AntKart — Skills Reference

Skill files are step-by-step procedural guides for common AntKart development and maintenance tasks. Each skill covers the full end-to-end workflow: code changes, tests, documentation updates, and verification steps.

Use these to avoid rework, missed impacts, and documentation drift.

---

## Skills Index

### New Development

| Skill | File | Use When |
|-------|------|----------|
| Add a New Microservice | [new-service.md](new-service.md) | Building a new bounded context as an independent deployable service |
| Add a New Endpoint | [new-endpoint.md](new-endpoint.md) | Adding an operation to an existing service (command or query) |
| Add a New Integration Event | [new-integration-event.md](new-integration-event.md) | A business action in one service needs to trigger effects in another |
| Add a New Consumer | [new-consumer.md](new-consumer.md) | An existing service needs to react to an already-defined integration event |
| Add an EF Core Migration | [add-migration.md](add-migration.md) | Domain entity or schema changed in Order, Payments, or Notification |

### Maintenance & Safety

| Skill | File | Use When |
|-------|------|----------|
| Impact Check | [impact-check.md](impact-check.md) | Before changing any shared code — BuildingBlocks, event contracts, ocelot.json, docker-compose |
| Add a Gateway Route | [add-gateway-route.md](add-gateway-route.md) | Exposing a new service or endpoint group through the Ocelot Gateway |
| Verify IDOR Safety | [verify-idor.md](verify-idor.md) | After adding or modifying any user-scoped endpoint |

### Quality & Verification

| Skill | File | Use When |
|-------|------|----------|
| Run Security Checks | [security-check.md](security-check.md) | Before any release, after auth changes, or on a schedule |
| Docker Health Check | [docker-health.md](docker-health.md) | After rebuilding, adding a service, or when containers are unhealthy |
| Run Tests | [run-tests.md](run-tests.md) | Before every commit — verify count, no failures, no dropped tests |

### Documentation

| Skill | File | Use When |
|-------|------|----------|
| Sync Documentation | [sync-docs.md](sync-docs.md) | After any code change — run before committing |

---

## Recommended Workflow

### Adding a new feature

```
1. /impact-check     ← understand what your change touches
2. /new-endpoint     ← implement with full CQRS stack
   or /new-service   ← if it's a new bounded context
3. /verify-idor      ← confirm no IDOR vulnerabilities introduced
4. /run-tests        ← all pass, count doesn't drop
5. /sync-docs        ← every affected doc updated
6. /docker-health    ← stack healthy end-to-end
7. /security-check   ← no regressions in security baseline
```

### Adding cross-service messaging

```
1. /impact-check              ← find all affected publishers and consumers
2. /new-integration-event     ← define contract + wire publisher + consumer + test
   or /new-consumer           ← if event already exists
3. /run-tests                 ← integration tests pass
4. /sync-docs                 ← EVENTBUS.md, both service design docs
5. /docker-health             ← RabbitMQ queues appear, messages flow
```

### Maintaining the platform

```
Before any shared change:     /impact-check
After any code change:        /run-tests  →  /sync-docs
Before shipping:              /security-check  →  /docker-health
```
