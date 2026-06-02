# Known Issues

This file tracks known technical debt and deferred fixes. Each item is acknowledged and has a planned resolution point — items here are scheduled, not overlooked.

| ID | Severity | Component | Description | Planned Fix |
|----|----------|-----------|-------------|-------------|
| KI-001 | High | AK.Discount.Grpc | The gRPC `AuthInterceptor` reads a nested role-claim structure specific to the current identity provider. When the platform moves to a token format that carries roles in a flat `roles` claim, the admin write RPCs (create/update/delete discount) will fail authorization until the interceptor is updated to read the flat claim, consistent with the other services. Read-only RPCs are unaffected. | Address as part of the identity-provider migration. |

## Notes

- Resolved items are removed from the table once verified, with the resolution recorded in the change history (commit message and, where relevant, the migration notes).
- New deferred issues should be added here with a unique `KI-NNN` id, a severity, the affected component, a clear description, and a planned resolution point.
