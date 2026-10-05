# ADR-WORK-001 - Atomic Work desired-state storage and audit mapping

- Date: 2026-10-05
- Status: Technical source decision for the JV03 storage slice; review and merge pending
- Scope: Work desired-state snapshot, observation inbox, outbox, and canonical audit insert

## Context

The Work desired-state protocol records one company-scoped observation at a time. An
applied observation changes the snapshot and appends an inbox row, an outbox row, and
an audit event. A partial commit would make replay and repair evidence disagree with
the stored effective state.

## Decision

`WorkDesiredStateStoreV2` stages the snapshot, inbox, and outbox in `WorkDbContext`,
enqueues an `AuditEventDraft` through `IAuditEventCollector`, and calls one EF
`SaveChangesAsync`. `WorkDbContext` includes Audit-owned entity mapping with
`ConfigureAuditEvent(excludeFromMigrations: true)` so the audit insert participates
in that same save and SQL transaction. Work owns only its `work` schema tables and
does not own the Audit table or its migration.

The Work-to-Audit Infrastructure project reference is limited to this mapping:

`Work.Infrastructure -> Audit.Infrastructure`

It is recorded as one exact edge in
`docs/architecture/MODULE_DEPENDENCY_ALLOWLIST.json`. This decision does not extend
the Organization-only exception in ADR-009 to other modules or grant access to
unrelated Audit Infrastructure services.

`WorkDbContext` applies the resolved `CompanyId` filter to the snapshot, inbox,
outbox, and AuditEvent. `WorkAuditEventTenantGuard` rejects AuditEvent update or
delete and refuses an added event for a different or unresolved company. Work row
write guards reject foreign-company rows, deletes, and inbox/outbox mutation.
Snapshot identity and version remain constrained. The store checks the company
again before saving and uses the existing optimistic-concurrency and unique-key
handling for races. It does not issue raw SQL commands.

## Boundaries

The Audit module still owns audit schema, lifecycle, read authorization, and
retention. This ADR does not authorize a new permission, actor policy, business
default, provider, API endpoint, or production activation. Work source discovery,
owner policy, and future AI consumption are separate slices.

## Evidence and rollback

The JV03 storage source has five focused real-SQL acceptance cases for atomic
rollback, replay, version races, key collisions, and tenant isolation. The local
five-case run passed; the PR #561 CI relational job passed 492/492 with zero skips.
The PR's architecture gate remains open until this exact dependency and source
inventory is reviewed and passes on a new head.

The Work migration refuses destructive `Down` while snapshot, inbox, or outbox
rows remain. A populated production store requires a reviewed forward fix; this
ADR does not authorize deleting or retrospectively rewriting stored evidence.
