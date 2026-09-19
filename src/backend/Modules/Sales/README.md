# Sales module

Source of truth: plan #275 `a5b8143b` T03 (§14), R3, §13; Business #254 §2–5 and #66 5653637829;
#257 `CONTRACT_V1.md` (draft wire contracts); #260 (frontend create/list/detail, NOT_ENV);
#262 `REFERENCE_READ_V1.md` (form reference reads); Ö2, Ö5, Ö8; ADR-002, ADR-009.

## What exists

| Project | Slice | Content |
|---|---|---|
| `GoymenERP.Modules.Sales.Application` | #257, #262, #285, #289 | wire contracts of the draft (create/list/detail/replay DTOs, strict JSON, exact delivery planning), the reference-read handler (Cari account, Settings currency/unit, Product eligibility) and, since SALES-P1, the **draft store contracts** |
| `GoymenERP.Modules.Sales.Domain` | **SALES-P1** | the `SalesOrderDraft` aggregate: header + lines + delivery slices, stable rule codes, `Version` ≠ `Rev.0`, Ö2 quantity |
| `GoymenERP.Modules.Sales.Infrastructure` | **SALES-P2** | the `sales` schema (drafts, lines, slices, attempts, number counter), `SalesStore : ISalesOrderDraftStore`, hand-written migration + snapshot, audit sidecar in the same save |

Since the Sales API slice the three draft routes exist (`SalesEndpoints.MapDraftEndpoints`) over
`SalesDraftService`; the host wiring (`AddSalesModule`, the Cari port, the permission registration,
the shell provider) is the central writer's sequential step, and the frontend of #260 mounts nothing
until the shell owner applies `SHARED_MOUNT_HANDOFF.md`. See "Draft API" below.

## Domain (`GoymenERP.Modules.Sales.Domain`, SALES-P1)

- `Quantity` — the Ö2 rule (kg ≤ 3 decimals, pieces whole, exact, no silent cut; trailing zeros dropped). Repeated from Purchasing on purpose: a Domain project must not reference another module's Domain.
- `SalesOrderDraft` — one company (server-resolved, immutable), one customer (`PartyId` + `CompanyAccountId`, both required), one currency, one business date; born `Kind=Order`, `State=Draft`, `Revision=0` (`Rev.0` on the wire).
  - **`Version` ≠ revision:** `Version` is the optimistic-concurrency token every mutation names (`AddLine(expectedVersion, …)`), starting at 1 and advancing by one per recorded change — the Purchasing / Logistics / Manufacturing / Product pattern (`SalesVersionConflictException`). `Revision` is the commercial revision and does not move in this slice (T33).
  - **Owner snapshots on every line** (#257 "persist owner snapshot … all StockUnit/SalesUnit/OrderUnit bindings"): `ProductSnapshot` (product id/version/code/name/effectiveOn + three `UnitBindingSnapshot`s) as the Product owner answered, `UnitProfileSnapshot` (unit, profile id/version, `IsKilogram`, owner's `ProfileScale`, applied `Scale` = min(profile, 3) — the PUR-3/LOG-3/MFG-3 shape) as the Settings owner answered. The draft refuses a snapshot of another company (`sales_snapshot_company_mismatch`), of another date than the business date (`sales_snapshot_date_mismatch`), and an order unit the Product snapshot does not bind as the order unit (`sales_product_unit_mismatch`). Nothing is rebuilt from Draft/Sellable/Complete; the snapshots come from the ports at command time and are stored with the draft.
  - **Money snapshot on the header** (`MoneyProfileSnapshot`, the Settings money profile of the currency on the business date, answered for the Sales operation `sales.order-draft.v1`): mandatory even for an unpriced draft (#257). There is no draft without one; its `EffectiveOn` must be the business date (`sales_snapshot_date_mismatch`; ÜRÜN #306 P3-2), its `Operation` must be the Sales draft operation (`sales_snapshot_operation_mismatch`; CARİ #306 P3), and it carries the owner's `IsActive` / `EffectiveFrom` / `EffectiveThrough` / `Scale`: a profile not in effect on the business date is `sales_money_profile_not_in_effect` (CARİ #306 P3-R1). The scale is kept as published and applied to nothing here (T28).
  - **Ö2 on every quantity:** the quantity's dimension follows the unit profile's (`sales_unit_unsupported` for a dimension that is neither weight nor count; a kilogram flag contradicting the dimension is a malformed snapshot); decimals beyond the applied scale are refused, never rounded (`sales_quantity_scale_exceeded`). **The applied scale is derived in the domain** — `SalesOrderDraft.AppliedScale` = min(owner's `ProfileScale`, Ö2's cap) — and a snapshot whose stated `Scale` disagrees is malformed (`ArgumentException`), so no snapshot can widen Ö2 (ÜRÜN #306 P3-1). The client's quantity text is kept verbatim (`QuantityText`) and must name the same decimal value in plain invariant notation (ÜRÜN #306 P3-3).
  - **Unit profile validity and dimension:** the snapshot carries the Settings owner's `IsActive` / `EffectiveFrom` / `EffectiveThrough`, and the draft refuses a profile that is inactive or not effective on the business date (`sales_unit_profile_not_in_effect`; CARİ #306 P2-2, the PUR-3 verifier's rule enforced where the snapshot is consumed). Only a weight (kilogram) or a count dimension has a Ö2 rule; any other dimension is `sales_unit_unsupported` — Sales maps nothing to pieces by guess, and only kilogram is approved in Settings today. A kilogram flag that contradicts the dimension is a malformed snapshot.
  - **Delivery slices:** client-owned stable `SliceId`, the line's unit only (no conversion), positive, unique, at most 500, never adding up to more than the line (`sales_slices_exceed_quantity`); the shortfall is `UnplannedQuantity`, never "delivered" (#257: 1000 = 400 + 600 → 0; 400 + 700 refused; 400 + 400 → 200 unplanned). Each slice keeps its quantity text verbatim, bound to its value like the line's (CARİ #306 P2-3).
  - **Commercial fields** (`unitPrice`, `priceUnitId`, `discountPercent`) are retained as text and **not validated** (T28); the only rule is #257's pairing (`sales_price_unit_pairing`). Six owner references (delivery address, contact, responsible, payment terms, source strategy, package) are stored as given and **not validated by their owners** in this slice.
  - Bounds: 1–500 lines (`sales_draft_without_lines` at store time via `RequireStorable`, `sales_too_many_lines`), unique `LineId` (`sales_duplicate_line_id`), notes ≤ 4000 (`sales_note_too_long`), document number ≤ 64.
  - **Stable rule codes:** every rule refusal is a `SalesRuleException` with a `SalesRuleCodes` code (all `sales_`), tests assert the code; the aggregate is the single authority, the line-level checks are a second guard reporting `sales_internal_invariant`; caller errors (empty id, null, non-positive quantity) stay `ArgumentException`s. `Lines` and `RequestedDeliverySlices` are read-only wrappers (pinned).
- **Document number:** a technical, company-scoped display identifier assigned by the store on first commit (SALES-P2). It is **not an official document number** (Ö8: official document production is closed); its format is display only and no gap-free guarantee is given or claimed. #257 treats it as opaque text.

### Application store contracts (`ISalesOrderDraftStore`, SALES-P1; implemented by SALES-P2)

- `SalesDraftWriteEnvelope` — the ADR-009 audit envelope (actor, permission, roles, correlation, causation, idempotency key, channel), the Inventory/Accounting shape; every value from the server-side context of the HTTP slice.
- `StoreDraftAsync(envelope, requestHash, build)` — one save: header, lines, snapshots, slices, the attempt record and the audit event together. **Replay (ADR-011, R3):** (company, `SalesDraftCommands.CreateV1`, uppercase SHA-256 of the trimmed idempotency key) is the attempt identity — the raw key is never stored; the same key with the same `SalesDraftRequestHash` **and the same actor** answers the first stored draft (`Replayed`, original correlation); another hash **or another actor** is `sales_same_key_payload_mismatch` — one definite 409 that discloses nothing of the stored result nor that the key is someone else's (the platform Idempotency module's single conflict kind); a key of another company is invisible — a company change is not a rollback. Authority is checked by the caller before the replay is read. `FindDraftAsync` and `ListDraftsAsync` (newest first, opaque company-bound cursor, #257) are company-scoped; another company's draft is null, never 403.
- `SalesDraftRequestHash` — the canonical content of a create (the strict wire shape serialised and hashed); line order matters because the client keeps it across retries, and the quantity **text** is content: "5.00" and "5" are different attempts, so the second under the same key is a payload mismatch (409), never a replay (tested).

## Persistence (`GoymenERP.Modules.Sales.Infrastructure`, SALES-P2)

Tables in the `sales` schema, all tenant rows (ADR-002): a global query filter on the server-resolved
company and a write guard in `SaveChanges` that refuses a row of another company, a company change,
a write without a resolved company and every delete. Companies are Organization's, referenced by
foreign key only (`organization.Companies`, excluded from Sales's migrations); the audit sidecar is
Audit's table, written through `IAuditEventCollector` in the same save (ADR-009).

| Table | Key | Content |
|---|---|---|
| `DraftNumbers` | `CompanyId` | the technical number counter (`Next`, concurrency token `Version`) |
| `Drafts` | `Id`; alternate `(CompanyId, Id)`; `UX_Drafts_DocumentNo` | header, owner references (id + expected version, paired by check), money profile snapshot, `Version` token + `rowversion`, creator and correlation |
| `DraftLines` | `(CompanyId, DraftId, LineNo)`; `UX_DraftLines_LineId` | Product snapshot, the three unit bindings, the order unit's Settings profile (scale = min(profile, 3)), `Quantity decimal(18,3)` + the client's `QuantityText`, commercial text |
| `DraftDeliverySlices` | `(CompanyId, DraftId, LineNo, SliceNo)`; `UX_DraftDeliverySlices_SliceId` | the requested delivery plan |
| `DraftAttempts` | `(CompanyId, CommandName, IdempotencyKeyHash)` under `Latin1_General_100_BIN2`; `UX_DraftAttempts_DraftId` (filtered to stored drafts) | the ADR-011 attempt record: no raw key — uppercase SHA-256 of the trimmed key (`varchar(64)`), canonical request hash (`varchar(64)`), actor (compared on replay), correlation; `Outcome` 1 = stored draft (`DraftId`) / 2 = recorded terminal refusal (`RefusalJson`, §7 — API slice) |

- **Replay (ADR-011 §2–§5, R3):** the attempt is read first, by (company, command name, key hash).
  The same key with the same hash **and the same actor** answers the stored draft (`Replayed`,
  original correlation); another actor or another hash is `sales_same_key_payload_mismatch` — one
  definite refusal that discloses nothing of the stored result nor that the key is someone else's
  (the platform Idempotency module's single conflict kind); a key of another company is invisible. The raw key is never stored: the store hashes the
  trimmed key (uppercase SHA-256) and still refuses a key with surrounding whitespace
  (ArgumentException), so the hash the caller reasons about is the hash stored. `"K-1"` hashes
  differently from `"k-1"` and is another attempt. Authority and the company context are the
  caller's checks before the replay is read (§5); the replay headers (`Idempotency-Replayed`,
  `Idempotency-Original-Correlation-Id`) are the HTTP slice's.
- **Number:** the company's counter row gives `SO-<UTC year>-<n:D6>` under its concurrency token.
  Technical display text, not an official document (Ö8); no gap-free guarantee — a lost race leaves
  its number unused.
- **Race:** a unique-key violation (2601/2627) or the counter's token after `SaveChanges` is
  `sales_concurrent_change`; nothing of the loser is stored and its retry answers from the winner.
  This is the only use of the SqlClient namespace in the module.
- **Restore:** a stored draft is rebuilt through `SalesOrderDraft.Create` + `AddLine`, so every rule
  of a new draft holds again; a stored version or line order the rebuild does not reach is an
  `InvalidOperationException` (a corrupt row is refused, not repaired).
- **List:** newest first (`CreatedAtUtc desc, Id desc`), page size 1–100, an opaque base64url cursor
  bound to the company and the ordering; a foreign or malformed cursor is `sales_invalid_cursor`.
- **Append-only:** only the counter's `Next`/`UpdatedAtUtc` and a draft's `UpdatedAtUtc` may change;
  approval and revision are T33.
- **Migration:** `20260918210000_InitialSales` (hand-written, Inventory/Finance format) and
  `SalesDbContextModelSnapshot`, pinned by `HasPendingModelChanges` in `SalesPersistenceModelTests`.
- **Evidence:** `SalesPersistenceModelTests` (model, guards, cursor, number — no database) and
  `SalesDraftSqlServerRelationalAcceptanceTests` (six `[SqlServerFact]` cases against a real SQL
  Server: store + replay + audit + schema inventory, same-key mismatch + race, company boundary,
  domain round-trip + paging, foreign-key rollback, canonical key). The SQL cases build drafts from
  **synthetic owner snapshots**: they prove the store, not any owner's rule, and claim no user outcome.

## Draft API (Sales API slice; card #66 with decisions 1–6)

The user path of #257 over HTTP: `POST /api/v1/sales/drafts`, `GET /api/v1/sales/drafts`,
`GET /api/v1/sales/drafts/{id}` (`SalesEndpoints.MapDraftEndpoints`, the Sales lane's host
file) over `SalesDraftService : ISalesDraftService` (Application). The route binds only the
server context; the service decides.

| Concern | Rule |
|---|---|
| Authority | A fresh central measurement (#276, `IErpAuthorityRevalidationService`) of the Sales name before anything is read and after every awaited owner read; granted **and** company-wide scope of the request's company and actor. Names are `SalesPermissionManifestV1`: `sales.drafts.create` (POST and the form's reference reads), `sales.drafts.read` (list + detail). Registration and classification are the central writer's; the grant is a human gate; selecting a product also needs the Product owner's `product.operational-selection.read` (measured by that owner). An unregistered name is a 503 setup problem (`permission_unregistered` in the service and its tests; the wire carries the platform's `service_unavailable`), never a grant. |
| Idempotency (ADR-011) | Exactly one canonical `Idempotency-Key` (≤128, no comma) → else 400 `command_metadata_invalid`; content hash `SalesDraftRequestHash`; the attempt key of SALES-P2. Authority comes before the replay is read (§5); the stored draft replays as **201 + `Location` + `Idempotency-Replayed: true` + `Idempotency-Original-Correlation-Id`** (§4: the persisted result is replayed; Counterparty and Manufacturing do the same) — **#257 delta:** the contract said 200 for the replay, the ADR wins (coordinator decision on #321); `isReplay` stays in the body as information; another content or actor is one 409 `same_key_payload_mismatch` (§3). |
| ADR-011 §7 | A **terminal refusal** — the Product owner's stale `version_conflict` (409) — is recorded under the attempt key (`DraftAttempts.Outcome = 2`, `RefusalJson`; migration `20260919100000_AddAttemptOutcome`) and replayed as the same 409 with the replay headers; a key holding a refusal never stores a draft. Non-terminal refusals (422 owner answers, 503 outages) are not recorded: the next attempt asks the owners again. |
| Fences and the commit | The authority is measured before anything is read (§5, also before the replay), after every awaited owner read and right before the single save. **There is no fence after the commit** (S03; ORG #321 P3-1): a grant revoked between the last fence and the commit cannot undo the stored draft, and answering 403 would only hide it — the caller would drop its key and create a second draft. The committed result is answered; the next request of the key meets the fence before the replay is read. Unit: `An_authority_lost_after_the_commit_does_not_hide_the_stored_draft`. |
| ADR-011 §8 | Two callers of the same key, content and actor may pass the replay lookup together; the store's unique attempt key decides. The **loser answers the winner's result**, read afresh (the store clears its tracker on the lost race, `FindReplayAsync` again in the same scope): the winner's draft as the 201 replay, the winner's recorded refusal as the same 409 replay. The same holds when a terminal record loses its race. Only a race that left nothing under the key (the number counter, a draft id) is the caller's 409 `concurrent_change` to retry. Proven with a real barrier race in `SalesDraftApiSqlServerRelationalAcceptanceTests` (one draft, one attempt, one audit row, both callers the same draft). |
| Owners, in order | Cari (`ISalesPartyAccountPortV1`, the host's `CounterpartySalesPartyAccountPortV1` over the authorized party read): party + account of that party + this company + both active, else 409 `account_state`. Settings money profile for `sales.order-draft.v1` on the business date: not found → 422 `unsupported_currency`; not in effect → domain `sales_money_profile_not_in_effect` (422); access → 403; else 503. Per line the Product eligibility (three bindings) and the order unit's Settings profile: field errors → 422 `unsupported_product` / `unsupported_unit`; runtime absent or outage → 503 `product_eligibility_unavailable`. A malformed owner answer (another company, another unit, empty identity) is 503 `reference_provider_invalid`. Nothing is cached, defaulted or copied. **Consistency of "another company":** the Cari port is asked for an account the *user* chose (`companyAccountId`), so an account of another company is the user's input problem — 409 `account_state` / `company_mismatch`; the Settings and Product ports are asked for *this company's* profile of a currency or unit (the company is the context, not an input), so a profile of another company is the provider breaking its contract — 503 `reference_provider_invalid`. |
| Contract deltas (fail-closed) | Decision 2: any `unitPrice` / `priceUnitId` / `discountPercent` → 422 `commercial_not_available` (T28; nothing of a price is stored). Decision 3: any owner reference (delivery address, contact, responsible, payment terms, source strategy, package) → 422 `owner_reference_unavailable` until its owner port exists. Both are marked deltas on #257; the #260 form sends these as null. |
| List / detail | `pageSize` 1–100 (default 50), opaque company-bound cursor → 400 `invalid_cursor`; another company's draft → 404 (never 403). Kind/state/revision on the wire: `Order` / `Draft` / `Rev.0`; `version` is the concurrency token text. |
| Problems | `SalesDraftProblem`: status/title, `code`, `correlationId`, field `errors`; platform 401/403/404 code-less (#257). **Every 503 is code-less at the route** and the platform writer answers `service_unavailable`: the service's setup/provider codes (`permission_unregistered`, `counterparty_provider_unavailable`, `settings_provider_unavailable`, `reference_provider_invalid`, `product_eligibility_unavailable`) never reach the wire — the same boundary as the reference routes (`SalesDraftHttpAcceptanceTests`). A 201 carries the absolute `Location` of the detail route. |
| Shell | `SalesShellCapabilityProvider` (Infrastructure/Shell) declares `sales.drafts.list` / `create` / `detail` with the frontend's routes, **published** (`ImplementationAvailable: true`, S03-c). The registry never publishes a staged declaration (`ShellCapabilityService` filters on availability), so the flip precedes the `s03` browser run and that run is its acceptance (Product #316 precedent; Sales documents carry no activation envelope rule). A declaration grants nothing: an actor without `sales.drafts.read` / `sales.drafts.create` sees no Sales screen, and the grant is a human gate. The host registers the provider with its accepted revision (the central authority constant re-measured for the published set) and the C-01 envelope removes it as before. Real SQL: `SalesShellCapabilitySqlServerRelationalAcceptanceTests` (synthetic isolated grants: read → list + detail, read + create → all three, none → none). |
| Honest runtime today | With the real owners on main a create answers the **first owner that cannot**: an unknown customer is 409 `account_state`; with a real customer, the money profile (T02-b, GDC/TRY) answers and the Product runtime — composed by C3 but unconfigured in this host — is 503 (`product_eligibility_unavailable` in the service, `service_unavailable` on the wire). Measured by `SalesDraftApiSqlServerRelationalAcceptanceTests`; no green is painted. The positive path (a stored draft, the race, every replay) is proven in the same real host with SENTETIK owner ports for Cari, Settings and Product only. |

Evidence: `SalesDraftServiceTests` (synthetic ports; registered platform names stand in for the Sales
names until they are in the catalogue), `SalesShellCapabilityProviderContractTests` (pinned to
`src/sales/capabilities.ts`), `SalesDraftSqlServerRelationalAcceptanceTests` (§7 refusal replay),
`SalesDraftApiSqlServerRelationalAcceptanceTests` (real host + real owners, synthetic grants), the
frontend's problem codes/texts (`contract.ts`, `salesText.ts`), and the browser suite `s03`
(`tests/e2e/sales-s03`). **s03 v2a (S03 slice)** runs against the real API on the `normal` profile and is
*explicitly negative* evidence: the granted actor reads the real empty list; a create attempt ends in the
honest refusal and writes nothing; the ungranted profile is closed out. The create form reads its customer
options from the real `/api/v1/sales/references/accounts` list (`useHttpReferenceOptions`, S03) and the
suite measures that answer; the currency and product reference routes on main validate one chosen id
and offer no list, so the form is withheld naming those sources (`no-list-route`) until their owners
publish lists (v2b) — nothing is guessed. It is not a positive sales delivery — v2b (create → detail → list → same key 201 +
`Idempotency-Replayed` → reopen) waits for the Product activation path (DEC-PRD-010) and the GDC family
data. The suite needs the shell mount and the published (`ImplementationAvailable: true`) Sales
capabilities: `ShellCapabilityService` never publishes a staged declaration, so the flip is a
precondition of the run, not its consequence.

Central (the central writer's sequential commit): `Program.cs` — `AddSalesModule` + `InitializeSalesModuleAsync`
(after Organization), `ISalesPartyAccountPortV1` → `CounterpartySalesPartyAccountPortV1`, the reference-read
bindings' three permissions → `sales.drafts.create`; `ErpPermissions` registration of the two names (company-wide,
not critical, no seeded role); the shell provider registration + authority; OpenAPI pinned document; guard pins;
verifier names (+3 `SalesDraftApi…`); the browser workflow's `suite` option `s03` (read-only).

## Internal approval (SALES-A1; DEC-SAL-001/002, Business #66 5737989917 = B)

The INTERNAL APPROVAL axis of a draft — `InternalApprovalState` Preparing / PendingApproval / Approved /
Rejected — beside the lifecycle (`Draft`), the commercial revision (`Rev.0`) and the concurrency version.
It opens no other axis (lifecycle, sourcing, fulfilment, customer confirmation) and chooses no policy:
no threshold, no role, no maker≠checker, no second person; the submitter approving their own draft is not
refused (Business 5737989917). **Contract deltas** (marked): `approvalState` on every summary, `approval`
facts on the detail, the `approvalState` list filter, three commands, two codes.

| Concern | Rule |
|---|---|
| Domain | `Submit(expectedVersion, actor, now)`: Preparing/Rejected → PendingApproval (needs ≥1 line); `Approve`: PendingApproval → Approved; `Reject(…, reason)`: PendingApproval → Rejected (reason optional, ≤4000, `sales_approval_reason_too_long`). Any other transition, and any line change of a submitted or approved draft, is `sales_state_conflict` (DEC-SAL-002: an approved draft changes only through a revision, SALES-A2). Each transition names the expected version and advances it by one; `ApprovalFacts` (who/when submitted and decided, the reason) travel with the draft. `RestoreApproval` rebuilds a stored axis only when the facts fit the state and the version accounts for the transitions. |
| Persistence | `Drafts.ApprovalState` + `SubmittedBy/SubmittedAtUtc/DecidedBy/DecidedAtUtc/RejectionReason` (migration `AddDraftApproval`; backfill 1 then no default, the `Outcome` pattern); `CK_Drafts_Approval` binds the facts to the state; `IX_Drafts_CompanyId_ApprovalState_CreatedAtUtc_Id` for the filter. The write guard lets the header move only along these columns (+ `UpdatedAtUtc`; `Version` is the token). `Down` refuses with THROW 51045 while any draft has left Preparing (forward fix). |
| Store | `UpdateDraftStateAsync(envelope, hash, command, draftId, mutate, audit)`: ONE save — the header's state/facts/version, the attempt row under `(company, command, key hash)` and the ADR-009 event (`sales.order-draft.submitted.v1` / `approved.v1` / `rejected.v1`, version before/after, before/after JSON, reason). Another company's draft is null (404, never 403). |
| ADR-011 | Commands `sales.order-draft.submit.v1` / `approve.v1` / `reject.v1`; content = canonical `{command, draftId, expectedVersion, reason}`; the same key + content + actor answers the persisted record AS IT IS NOW with `Idempotency-Replayed` and the first correlation (200; the Counterparty / Manufacturing pattern); another content or actor is one 409 `same_key_payload_mismatch` (§3). **§7 terminal refusals:** a stale version (409 `version_conflict`, field `expectedVersion`) and a wrong state (409 `sales_state_conflict`, field `approvalState`) are recorded under the key and replayed. **§8:** a same-key race loses on the attempt key and answers the winner; a lost draft-version race (another key moved the draft) is 409 `concurrent_change` to retry with a fresh read. Fences: before the replay (§5), after the replay read, right before the single save; none after the commit (S03). |
| Routes | `POST /api/v1/sales/drafts/{id}/submission` (create permission), `…/approval` and `…/rejection` (`sales.orders.approve`, company-wide; class and grant are central/human): one canonical `Idempotency-Key` (≤128) → else 400 `command_metadata_invalid`; strict `{expectedVersion}` (+ `reason` on the rejection only) → else 400 `validation_failed`; 200 `SalesOrderDraftResponse`; 404 code-less; 409 as above; 422 `validation_failed` (a line-less draft) / `sales_approval_reason_too_long`; 503 code-less. `GET /api/v1/sales/drafts?approvalState=<exact name>` narrows the list (400 otherwise). |
| Line reference port | `ISalesOrderLineReferencePortV1.GetApprovedLineAsync(orderId, lineId)` (Sales-owned; `SalesOrderLineReferencePort` registered in `AddSalesModule`): `Approved` + the line's stored identity facts (company, order, line, party, account, product + version, order unit, quantity text, Rev, version) only while the draft is Approved; otherwise `NotApproved` + the state; unknown or another company's draft/line → `NotFound`. It creates nothing: the consumer's (INV-AL-1 / LOG-4) own command creates its record under its own authority, idempotency and audit; a replay of that command creates no second need. |
| Shell | `sales.orders.approve` is a CONTROL of the S-04 detail (`ParentCapabilityId` = `sales.drafts.detail`; the detail's route; `sales.drafts.read` + `sales.orders.approve`; the Finance bank-account approval pattern). The submit button follows `sales.drafts.create`. |
| Frontend | S-04 shows the axis and the facts ("not yet" for what did not happen), the honest linked-effects line (D4: allocation 0 / loading 0, no consumer), and the commands by capability and state; each command is one idempotent attempt (the create's rules); a recorded transition re-reads the detail. S-02 shows the axis in its state column and filters on the server. |
| Not here | Revision / cancellation and the DEC-SAL-005 preview (SALES-A2); allocation and loading records (INV-AL-1 / LOG-4 own them); price (T28); customer confirmation (Q01: internal approval suffices); an edit API. |

## Deliberate limits of this slice (not policy choices)

- **No real positive create today.** #257 makes the currency mandatory and validated through the Settings money profile with the central owner's accepted Sales money operation identifier; the approved Settings provider answers **Unavailable** for money and that operation identifier does not exist, so a contract-faithful create is 503 with every real provider. Product eligibility is not host-registered (C3) and the GDC family data is missing (#300), so every line is 503 as well. Only the kilogram unit path is real (GDC kg). SALES-P2's real-SQL tests therefore prove the negative path with the real providers and the positive path only with **isolated synthetic owner sources**; no PR claims a positive runtime, and no currency, family, profile or grant is invented.
- **No price, discount, tax, exchange rate or total** (T28); values are text and unvalidated.
- **No revision or cancellation** (T33 / SALES-A2); `Rev.0` is fixed. Internal approval is SALES-A1 (above).
- **No HTTP, permission, host DI, OpenAPI, capability** (next slices); no host registration of Product eligibility (C3, grant pending).
- **No shipment, invoice, collection or stock effect** — separate owners (T11/T12/T32/T29).

## dependency_wait

- **SALES-P2 central registrations (CARİ, single writer of central files):** solution entry, `Api.IntegrationTests` project reference, module dependency allowlist (Sales.Infrastructure → Audit.Infrastructure), tenant-bypass guard pins (`SalesDbContext`, `SalesStore` SqlClient 1, `SalesModuleExtensions` provider branch, design-time factory), RCSI verifier names (+6). Until they land, the guard inventory and the integration-test build are red by design.
- **Host registration of `AddSalesModule` / `InitializeSalesModuleAsync`:** the API slice (next), with the permission names and the endpoints.
- **SALES-P2 (this slice):** `Sales.Infrastructure` — `sales` schema (drafts, lines, slices, attempts, counter), (CompanyId, CommandName, IdempotencyKeyHash) attempt key (ADR-011) + canonical hash + actor comparison, rowversion, audit envelope, six real-SQL cases; CARİ's central registrations (tenant guard inventory, allowlist `Sales.Infrastructure → Audit.Infrastructure`, integration csproj, verifier counts).
- **Settings money profile** (T02-b, CARİ: the approved GDC/TRY money profile): the real positive create. The Sales money operation identifier is **not** waited for: it is the Sales owner's technical mapping (Business #254 5654539215) — `SalesMoneyOperations.OrderDraftV1` = `sales.order-draft.v1`, the Finance pattern — carried on `MoneyProfileSnapshot.Operation` and required by the domain (`sales_snapshot_operation_mismatch`). The key chooses no rate, scale or date. The only thing a real draft waits for on the money side is the Settings provider's approved profile.
- **C3:** Product eligibility host registration and the GDC family definition data; a human grant.
- **Cari:** the account belongs to the Party and is active in this company (#262 read, at command time).
- **Owner references** (address, contact, responsible, payment terms, source strategy, package): authorised reads and value snapshots (#257's owner-binding list).
- **HTTP slice:** the create/list/detail endpoints of `CONTRACT_V1.md`, permissions (registered centrally, granted by a human), 201 (+ replay headers and `isReplay` on the same attempt; ADR-011 §4 delta), cursor codec.

```bash
dotnet test tests/GoymenERP.Sales.UnitTests/GoymenERP.Sales.UnitTests.csproj
```
