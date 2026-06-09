# Known Issues

This file tracks known technical debt and deferred fixes. Each item is acknowledged and has a planned resolution point — items here are scheduled, not overlooked.

_No open issues._

## Resolved

| ID | Component | Resolution |
|----|-----------|------------|
| KI-001 | AK.Discount.Grpc | The gRPC `AuthInterceptor` read a nested, provider-specific role-claim structure, so the admin write RPCs (create/update/delete discount) would fail authorization once tokens carried roles in a flat `roles` claim. **Resolved in the identity migration to Microsoft Entra ID:** the interceptor now reads the flat top-level `roles` claim, consistent with the shared `AK.BuildingBlocks` authentication. Read-only RPCs are unchanged. |

## Notes

- Resolved items are recorded here (and in the change history) once verified.
- New deferred issues should be added here with a unique `KI-NNN` id, a severity, the affected component, a clear description, and a planned resolution point.
