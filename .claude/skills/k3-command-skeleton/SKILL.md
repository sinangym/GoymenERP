---
name: k3-command-skeleton
description: Use when writing or reviewing a backend critical command (create/submit/approve/reject/withdraw/cancel/record — anything that writes money, stock, production or an approval) or a cross-module read port / participant. Checklist of the house skeleton — permission fence, expected version, idempotency replay, guarded reads, single save with attempt + audit, terminal refusal recording, problem codes, endpoint metadata — with the canonical files in each module and the tests a K3 PR must carry.
---

# K3 critical command skeleton

AGENTS.md "Kritik commands: authorization, expected version, idempotency key, correlation/causation, audit". Every
module re-implements the same technical skeleton; copy the **shape** from a canonical file, keep business rules in
the module's domain. Canonical example (read it end to end):
`src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Application/PurchasingService.cs` →
`SubmitRequestAsync` (+ `PurchasingService.RequestDecisions.cs`), store
`src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingStore.cs` →
`MutateRequestAsync`, endpoint `src/backend/GoymenERP.Api/Endpoints/PurchasingEndpoints.cs`.
Same shape: `SalesDraftService.cs` (`RequireAsync` / `RequireStillAsync` / `GuardedWithScopeAsync`),
`LogisticsInstructionService.cs`, `DocumentService.cs`, `ManufacturingApiV1.cs` (result-object variant).

## Service order (do not reorder)

1. `ArgumentNullException.ThrowIfNull(request/metadata)`; `cancellationToken.ThrowIfCancellationRequested()`.
2. `var scope = await RequireAsync(permission, ct)` — 401 `unauthenticated`; 403 `forbidden` if company context is
   unresolved/`Guid.Empty`/not accessible; **503** with `<M>ProblemCodesV1.PermissionUnregistered`
   (`permission_unregistered`, some modules prefix it) if `!ErpPermissions.IsKnown(permission)` — a setup gap, never a
   grant; the endpoint answers it code-less like every 503; then
   `authority.RevalidatePermissionScopeAsync(permission, companyId, ct)` must be `IsGranted && IsCompanyWide` and
   the live user/company must still `Matches` the captured ones. Scope = `(CompanyId, ActorId, RoleCodes sorted)`.
3. `ParseVersion(request.ExpectedVersion)` — canonical positive int64 text, else 400 `validation_failed` on
   `expectedVersion`.
4. `requestHash = <M>RequestHash.Compute(<M>Commands.<X>V1, targetId, request)` (canonical JSON → SHA-256 hex).
   Command names: `<module>.<entity>.<verb>.v1` constants (`PurchasingCommands`).
5. `envelope = Envelope(scope, metadata, permission)` — actor, permission, role codes, correlation id, causation id,
   idempotency key, source channel, client IP, user agent (from `ICorrelationContext`).
6. `FindReplayAsync` (wrapped in `GuardedAsync`) → if found, return the stored response / re-throw the stored
   refusal with `isReplay: true` and the **original** correlation id. Replay comes before not-found/validation.
7. Cheap shape checks (e.g. `Guid.Empty` → 404 `not_found`).
8. `await RequireStillAsync(scope, permission, ct)` right before the write (the fence: same company + actor).
9. One store call: `store.Mutate…Async(envelope, requestHash, command, id, (aggregate, facts, now) => aggregate.<DomainMethod>(expectedVersion, actor, now), new <M>MutationAudit("<module>.<entity>.<past-verb>.v1", "<Operation>", "<Türkçe gerekçe>"), ct)`.
10. Catch ladder: `VersionConflictException` → `TerminalAsync(… 409 version_conflict …)`; domain rule codes →
    `TerminalAsync(409 <code>)` or 422 `validation_failed`; `ArgumentException` → 400; store `ConcurrentChange` →
    re-`FindReplayAsync` (the winner's answer) else 409 `concurrent_change`; other store codes → `Store(exception)`
    map (`same_key_payload_mismatch` 409, `CompanyContextMismatch` 403, unknown → 503 `service_unavailable`).
11. `TerminalAsync`: `RequireStillAsync` → `store.RecordRefusalAsync(...)` (the refusal is durable, same key replays
    it) → on `ConcurrentChange` return the winner's refusal/record as replay → throw the refusal.

## Store order (one transaction, one `SaveChanges`)

`RequireEnvelope/Hash/Command/Audit` → resolved company → `FindAttemptAsync(command, keyHash)` → if present:
`RequireSameContent(actor, requestHash)` (else `same_key_payload_mismatch`) and return `Replayed` → load row +
children (company query filter applies) → `Restore…` domain aggregate → snapshot before → domain mutates → invariant
"exactly one version step" → map back to row → add attempt row → `Audit(envelope, company, entityType, id, eventType,
operation, versionBefore, versionAfter, before, after, reason)` → `SaveAsync` once. Row version is the concurrency
token; a lost race surfaces as `ConcurrentChange`.

## Endpoint (GoymenERP.Api/Endpoints/<M>Endpoints.cs)

- Idempotency header: follow the **module's existing reader** — there are four behaviours today (Purchasing/Logistics/
  Sales/Inventory/Finance/ProductPriceList reject surrounding whitespace with 400 `command_metadata_invalid`;
  Accounting/Manufacturing trim and accept; Organization uses `TryCanonicalizeIdempotencyKey`; Documents uses
  `DocumentsApiV1.IdempotencyHeader`). For a new module copy group (a): one header, non-blank, canonical, ≤ 128 chars,
  no comma. Never change an existing module's behaviour in passing: it changes the HTTP contract and stored keys. Strict JSON body (unknown fields → 400).
- `Metadata(idempotencyKey, correlation)` → `<M>CommandMetadata(key, correlationId, causationId, UtcNow)`.
- Replay → headers `Idempotency-Replayed: true` and `Idempotency-Original-Correlation-Id`.
- `Problem(...)`: 401/403/404/503 are **code-less** (`Results.Problem(statusCode)`); other statuses carry `code` and
  field `errors`. Problem codes live in `<M>ProblemCodesV1` (e.g. `PurchasingContractsV1.cs`); platform codes only
  in `src/backend/GoymenERP.Api/Errors/ErpProblemCodes.cs`.

## Cross-module ports and participants

- **Read port** `I<Consumer><Thing>ReadPortV1`: declared by the consumer in its `Application/Contracts`, implemented
  and registered by the owner (e.g. `ISalesPartyRiskReadPortV1` ← `FinanceSalesPartyRiskReadPortV1`, registered in
  `FinanceModuleExtensions.cs`). It carries **no authority** (the consumer fences before/after), reads only the
  server-resolved company, creates nothing, and answers per figure `Unavailable` (fail-closed) / `NoSource` /
  `Available` — never a silent zero. Consumers take it optional (`ISalesPartyRiskReadPortV1? partyRisk = null`) or
  register a default `Unavailable…Port` with `TryAddScoped` (`OrganizationModuleExtensions.cs`).
- **Participant** (one host transaction across modules): `<Owner>…Participant.EnlistAsync(connection, transaction,
  companyId, …)` builds the owner DbContext on the host's open connection with `CompanyContext` +
  `AuditSaveChangesInterceptor`, `UseTransactionAsync`; one `StageAsync` per instance; the host owns commit/rollback
  (`PurchaseAcceptanceParticipant.cs`, `InventoryGoodsReceiptParticipant.cs`, host
  `GoymenERP.Api/PurchasingRuntime/PurchaseGoodsReceiptHost.cs`). Never write another module's tables directly.
- External effects: outbox rows in the owner's save (`InventoryDefiniteShipmentOutboxV1.cs`); consumers dedup.

## Tests a K3 command PR carries

- Domain: `tests/GoymenERP.<M>.UnitTests/<Aggregate>Tests.cs` (state machine, version step, maker ≠ checker).
- Service: `<M>ServiceTests.<Slice>.cs` — unauthenticated/forbidden/unregistered permission, stale version
  (terminal + same-key replay of the refusal), same key different payload, replay returns original correlation,
  company isolation, authority lost between read and write.
- HTTP: `tests/GoymenERP.Api.IntegrationTests/<M>HttpAcceptanceTests.cs` — header rules, strict body, code-less
  401/403/404/503, replay headers.
- Real SQL: `<M>SqlServerRelationalAcceptanceTests.<Slice>.cs` — one save (row + attempt + audit), race leaves one
  record, checks hold, migration Down guard (`ef-migration-forward-fix`). Add FQNs to the relational inventory.
- OpenAPI expectations (`openapi-regenerate`), permission registration (`permission-capability-registration`).

## Pitfalls

- Never compute money/tolerance/formula in the service or endpoint; call the module's deterministic calculator.
- No hard-coded role names, thresholds or CompanyIds; role checks go through permissions and `ErpPersonaRoleCodes`.
- Do not add fields to the request-hash input that are `null` for old clients without keeping old hashes stable.
- `ErpPermissions.IsKnown` failing is a 503, not 403 — reviewers check this.
- A rule refusal that the user can fix by retrying with new input is 422 (not recorded); a state refusal is a
  recorded terminal 409.
