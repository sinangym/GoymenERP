# Product-only global command preparation

Sources: locked #98/#99; #103/#104; Business #181 at
`3dfe58e745f064a49d52b118361c757a0882807d`, section 19.
This is a code handoff, not a replacement business spec or accepted persistence ADR.

The current company authorization, AuditEventDraft and CommandExecution remain unchanged.
Product is the only declared shared-master kind. Service, wildcard and fake group/owner
CompanyId are unsupported. No route, DI registration, permission grant, role assignment,
migration, collector/store implementation or production activation is included.

## Implemented seams

- `IGroupSharedMasterAuthorizationService` resolves a server decision per request.
- The unconfigured adapter always denies, including reads and unknown enum values.
- The mutation context validates the service result, Product operation, local actor,
  server-resolved origin and authority revision. It is not an HTTP input or authority source.
- `GroupSharedMasterReadContext` separately validates Product/Read and the same required
  actor/origin/revision. A read decision cannot become a mutation context.
- Initial pilot commands require an origin for causation only. No-origin commands remain
  unavailable; no Product owner company is fabricated.
- `GroupSharedCommandKey` is globally `(Product, versioned command name, key hash)`.
  Actor/origin are intentionally outside that uniqueness namespace.
- Binding compares actor, origin and request hash after a fresh server decision. A context
  switch cannot reuse an old result or create a second global terminal effect under the key.
- Product consumes `IGroupSharedCommandReplayResolver.ResolveAsync(operation, keyHash,
  requestHash)`, not the privileged Idempotency-owner terminal reader. The implementation
  obtains current authority before lookup and rechecks after it. Denial or actor/origin/
  revision drift discloses no terminal. Results are AuthorizedMissing, MatchingReplay or
  Conflict; Conflict carries no old terminal, body or correlation. Unknown stored response
  schema fails closed. No authority is cached across requests.
- `GroupSharedAuditEventDraft` separates global Product subject from server origin and
  carries request provenance for the canonical Audit owner. It is not persisted here.

Example command names are pinned in code: `product.draft.create.v1` and
`product.draft.basic-edit.v1`. Hash inputs use the existing uppercase SHA-256 representation.
The Product owner must bind these names to its current service implementation, not silently
reuse the company-keyed store or the Cari client compatibility header.

## Remaining hard boundary

Company-scoped SYSTEM_ADMINISTRATOR assignment alone is not implemented as global eligibility.
The exact authoritative global eligibility source/provider must undergo the existing #104
security and Business read-only review. Do not replace the denying adapter with a role-name,
JWT, UI flag or any-company assignment check. #99's grantee decision is not reopened.

The Identity provider, global durable unique constraint/terminal store, canonical global audit
hash/read adapter and shared transaction enlistment remain NOT_IMPLEMENTED. Current replay
binding/resolver tests use controlled adapters, NOT live authorization, durable race, SQL
rollback, HTTP or browser acceptance. The resolver enforces call ordering; its authority
provider must supply fresh server state. The pure binding helper alone does not authorize.
AuthorizedMissing is not a reservation or transaction: the future writer must recheck
authority inside its owner-supported atomic boundary, uniquely constrain the global key,
and commit Product, canonical audit and the terminal together. No store write/stage adapter
or global schema is implemented by the terminal-reader/resolver boundary.

Only after the exact source contracts are accepted may Product persistence consume them.
Then real SQL must prove global same-key uniqueness across origins/actors, current denial
before result disclosure, stale-version races and Product+audit+terminal rollback. No table
owned by another module is written directly and no second financial/stock ledger is introduced.
