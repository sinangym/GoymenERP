# Product-only global command preparation

Sources: locked #98/#99; #103/#104; Business #181 at
`3f5deca6f1c338d44a4e9b52787c27dbab85846e`, section 19.
This is a code handoff, not a replacement business spec or accepted persistence ADR.

The current company authorization, AuditEventDraft and CommandExecution remain unchanged.
Product is the only declared shared-master kind. Service, wildcard and fake group/owner
CompanyId are unsupported. No route, DI registration, permission grant, role assignment,
migration or production activation is included. The unregistered terminal SQL adapter
below is source preparation; it is not schema admission or live Product acceptance.

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

The Identity provider, deployed global constraint, canonical global audit hash/read adapter
and shared transaction composition remain NOT_IMPLEMENTED. The terminal model/reader/session
now exist as unregistered source. Current replay
binding/resolver tests use controlled adapters, NOT live authorization, durable race, SQL
rollback, HTTP or browser acceptance. The resolver enforces call ordering; its authority
provider must supply fresh server state. The pure binding helper alone does not authorize.
AuthorizedMissing is not a reservation or transaction: the future writer must recheck
authority inside its owner-supported atomic boundary, uniquely constrain the global key,
and commit Product, canonical audit and the terminal together. The resolver alone still
does not stage/write or create global schema.

Only after the exact source contracts are accepted may Product persistence consume them.
Then real SQL must prove global same-key uniqueness across origins/actors, current denial
before result disclosure, stale-version races and Product+audit+terminal rollback. No table
owned by another module is written directly and no second financial/stock ledger is introduced.

## Runtime package admission contract (12 September 2026)

This section is the implementable next part of the same Product runtime package, submitted
for the existing #104 exact-source Business/security gate. It is not a new governance or
manifest family. The earlier #240/#243 reviews are NOT schema approval. Model/control tests
are NOT real SQL, HTTP or browser acceptance. No registration, migration or user grant is
activated by this source-preparation commit.

### Exact ownership and global uniqueness

Product source dependency: #245 `e635456b70e661e31f7f23e871cc48ca27141f43`.
The Product-only declaration is `GroupSharedMasterKind.Product`; unknown kinds fail closed.
`ProductDbContext` remains the exact source-reviewed architecture inventory entry. Its four
owned tables are `product.Products`, `product.CodeGroups`, `product.CodeCounters`, and
`product.CodeRegistrations`. The Product owner retains its UUID/code uniqueness, shared
prefix counter concurrency, retired-code reservation and version guards. None is replicated
per company. No other CompanyId-less business aggregate receives a wildcard exemption.

Idempotency owns `idempotency.GroupSharedCommandTerminals`, not Product. The new
`GroupSharedCommandDbContext` maps only that terminal. The primary key is exactly
`(Subject, CommandName, IdempotencyKeyHash)`, with Product-only command constraints and
binary collation. Actor/origin never partition this key. Nonempty actor/origin, canonical
uppercase SHA-256 hashes, JSON, HTTP 200..599, positive schema and UTC completion are
validated. Product success replay remains stricter: create201/edit200, response schema1,
and the existing validated Product mutation response.

`SqlGroupSharedCommandTerminalStore.ReadAsync(GroupSharedCommandKey, CancellationToken)`
uses a fresh untracked owner context and an exact complete key. It is privileged, not an
endpoint or Product consumer port. `RestoreStored` rehydrates data without manufacturing a
granted context. Only the existing fresh-authority replay resolver may disclose the result.

`GroupSharedCommandTerminalSession.EnlistAsync(DbConnection, DbTransaction,
GroupSharedMasterCommandContext, keyHash, requestHash, CancellationToken)` requires the
same open connection/transaction. `Stage(terminal)` accepts exactly one matching binding;
`FlushAsync(currentAuthority, CancellationToken)` checks actor/origin/operation/revision,
then performs only the owner's INSERT. Public DbContext SaveChanges overloads stay closed.
`IsFlushed` is transaction-local evidence, NOT a commit or successful HTTP result.
The session never starts, commits or rolls back the caller's transaction. A duplicate
terminal INSERT raises `GroupSharedCommandTerminalAlreadyExistsException`; it does not
return a winner or convert an arbitrary Product SQL failure into success.

### Canonical Audit subject extension to implement after admission

The existing `audit.AuditEvents` remains the ONLY audit ledger. Its next owned migration
will use an explicit discriminated subject, not origin-as-owner or a null-filter bypass:

| Supported branch | CompanyId | OriginCompanyId | Target and integrity |
| --- | --- | --- | --- |
| Company / V1 | required nonempty real owner | absent | existing entity and EXACT existing V1 envelope bytes/hash |
| GroupSharedProduct / V2 | absent only under this exact branch constraint | required nonempty server origin | exact Product UUID / Product.Product, subject+origin+authority revision included in V2 hash |

Domain factories and SQL CHECK constraints must reject unknown branch/version, empty UUID,
missing origin and cross-branch combinations. Nullable physical CompanyId is permitted
only as a consequence of the accepted explicit global subject, never as the discriminator
or proof of authorization. No CompanyId=Guid.Empty, arbitrary group company or copied origin
is allowed. The company-only draft, interceptor and v1 audit API keep their nonempty-company
contract; origin equality cannot expose global events through the company audit endpoint.

`AuditEventEnvelope` and `AuditEventIntegrity.Compute(AuditEventEnvelope)` V1 serialization
must remain byte-for-byte unchanged. A separate explicit V2 envelope covers the typed
Product subject, original request metadata and authority revision. Unknown versions fail
closed, without V1 fallback. Existing hashes/JSON are not rewritten. Global history is an
Audit-owner read behind fresh Product global authority, not an unfiltered DbSet or inferred
company-audit permission. No global audit HTTP endpoint is activated by this preparation.

The existing `IGroupSharedAuditEventCollector.Enqueue(GroupSharedAuditEventDraft)` is
implemented by the enlisted Audit owner session. It accepts the same actor/origin/revision,
Product UUID/version transition, event and original correlation/idempotency binding as the
Product write/terminal. Replay enqueues nothing. The owner flush is append-only and shares
the same transaction. No independent audit commit or Product mapping of AuditEvents exists.
After global events exist, an automatic downgrade to CompanyId NOT NULL must fail closed;
use a data-preserving forward fix, never delete events or turn origin into ownership.

### Explicit Identity assignment, revocation and fresh authority

The closed user decision is Sinan-only pilot assignment/revocation. The exact existing
SYSTEM_ADMINISTRATOR grantee stays unchanged. This is not a grant to any actual person or a
mapping of Sinan to a guessed local ID. Company administration in one/all companies grants
no global eligibility. No automatic bundle/backfill can recreate a revoked assignment.

The Identity owner will persist an explicit Product assignment per local user/operation,
referencing the actual canonical system role, authorized deciding identity and decision
provenance. The supported operations are Read, DraftCreate, DraftBasicEdit only; no implicit
operation expansion. Current state plus durable decision history change atomically with
expected-version checks. A database-generated rowversion and canonical contributing values
form the authority revision; a timestamp of observation is not a revision.

The normal writer requires the configured, verified local/external identity binding for
Sinan and authenticated decision provenance; request-supplied DecidedByUserId is not proof.
Missing/ambiguous mapping stays default-deny. No real mapping/secret, assignment, management
endpoint or production activation is supplied by this commit. An isolated fixture may use
explicit synthetic decision identities only behind the exact source/run/database host gate;
they are not labelled as Sinan or a normal production grant.

Every `IGroupSharedMasterAuthorizationService.GetSnapshotAsync` call executes an uncached
untracked Identity SQL projection over active actor, explicit operation assignment and
exact active system-role state. Fresh company membership validates ONLY the server origin.
The existing request-cached company authorization service is not a fresh global adapter.
Revoked/missing/expired or unsupported assignments deny before data/count/code/alias/replay
disclosure. SQL/readiness errors are not AuthorizedMissing. The existing RCSI readiness
guard remains required; no cached or long-lived snapshot is reused for the second check.
Revision inputs include contributing row contents, not only application-managed role versions.

### Single transaction composition and recovery

The API composition root implements the Product-owned `IProductDraftCommandRunner` and
owns one open DbConnection/DbTransaction. Product owns
`ProductDraftParticipant.EnlistAsync(connection, transaction, authority, cancellationToken)`
and `ExecuteAsync(ProductCommandIntent, cancellationToken)`. It validates references,
lifecycle and expected version in SQL, stages and flushes only Product-owned rows, and
returns ProductMutationResponse/status/exactJSON/ProductId/before-after versions and JSON/
reason/event. Each retry creates fresh contexts and allocator; no .Local/Find shortcut.

The root freshly resolves authority/replay, enlists each owner with the exact pair, executes
Product, stages the corresponding canonical audit and exact durable terminal, rechecks
authority and flushes both owners. The ONLY commit path requires all three successful
flushes and a final fresh matching actor/origin/operation/revision. Raw transaction presence
or AuthorizedMissing alone cannot release a successful result. Product performs no foreign
table mapping/write; no new generic transaction framework, gateway or second lifecycle.

Any validation, stale version, authority change, owner flush failure or cancellation rolls
back the complete attempt. Unique terminal losers roll back/dispose BEFORE a fresh resolver
reloads the committed winner. Conflicting actor/origin/payload returns non-disclosing409;
matching success replays the existing validated original response without a second product,
code allocation or audit. Commit/transport ambiguity is recovered with the SAME immutable
intent/key, never a new key or a claim that an unknown commit rolled back. Technical bounded
retry exhaustion remains a failure, not a hidden partial success.

Required post-admission SQL evidence: global key/code races across actors/origins, stale
edit versions, current grant revocation around lookup/write, injected Product/audit/terminal
failure and cancellation rollback, same-key unknown-outcome recovery, legacy V1 hash and
company audit invisibility of V2. Use the existing Product isolated profile API26080 /
SQL26433 / UI26173 and current test lifecycle. No new environment/manifest family.

Current source evidence: `GroupSharedCommandPersistenceTests` 10 PASS / 0 FAIL / 0 SKIP
(12 September 2026), model/controlled only. Runtime/SQL/browser and new-head mandatory CI
remain NOT_RUN until the corresponding source/schema/runtime gates and integration exist.
