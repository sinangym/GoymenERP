# ADR-PRD-001 — Product global replay consumer

Status: implementation review; no schema/runtime activation.
Sources: locked #98/#99, Business #181 §19, merged #240 and #243.
Product.Infrastructure consumes Idempotency.Domain's reviewed `IGroupSharedCommandReplayResolver`.
It does not call `IGroupSharedCommandTerminalReader` or map/write Idempotency tables.
The resolver owns fresh authority and binding checks; Product validates its operation's status/body.
ProductDbContext owns only Product rows and the unified code/alias namespace.
All writes stay closed until the shared owner supplies Product + canonical audit + terminal atomic enrollment.
The exact project edge and DbContext inventory entry require the central architecture owner's authored change.
No company assignment implies global authority, and this ADR grants no user access or migration approval.
