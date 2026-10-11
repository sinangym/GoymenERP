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
  - **Ö2 on every quantity:** the quantity's dimension follows the unit profile's (`sales_unit_unsupported` for a dimension that is neither weight nor count; a kilogram flag contradicting the dimension is a malformed snapshot); decimals beyond the applied scale are refused, never rounded (`sales_quantity_scale_exceeded`). **The applied scale is derived in the domain** — `SalesOrderDraft.AppliedScale` = min(owner's `ProfileScale`, Ö2's cap) — and a snapshot whose stated `Scale` disagrees is malformed (`ArgumentException`), so no snapshot can widen Ö2 (ÜRÜN #306 P3-1). The client's quantity text is kept verbatim (`QuantityText`) and must name the same decimal value in plain invariant notation (ÜRÜN #306 P3-3). **The text is validated before it is converted** (FİNANS #350 P2-1 applied to Sales): `SalesDraftService.Decimal` runs Settings' `ExactPositiveDecimalV1` at the kg cap of 3 decimals and refuses more than 15 integer digits (`sales_value_does_not_fit`, the decimal(18,3) column) before the one `decimal.Parse`, so a text longer than a decimal holds ("2.9999999999999999999999999999999") is refused, never rounded to 3 with the verbatim text kept beside it; lines and delivery slices alike.
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
| ADR-011 §7 | A **terminal refusal** — the Product owner's stale `version_conflict` (409), and since SALES-A2a a delivery address / contact named at another version than the Cari owner's (409 `version_conflict`, field `deliveryAddress` / `contact`) — is recorded under the attempt key (`DraftAttempts.Outcome = 2`, `RefusalJson`; migration `20260919100000_AddAttemptOutcome`) and replayed as the same 409 with the replay headers; a key holding a refusal never stores a draft. Non-terminal refusals (422 owner answers, 503 outages) are not recorded: the next attempt asks the owners again. |
| Fences and the commit | The authority is measured before anything is read (§5, also before the replay), after every awaited owner read and right before the single save. **There is no fence after the commit** (S03; ORG #321 P3-1): a grant revoked between the last fence and the commit cannot undo the stored draft, and answering 403 would only hide it — the caller would drop its key and create a second draft. The committed result is answered; the next request of the key meets the fence before the replay is read. Unit: `An_authority_lost_after_the_commit_does_not_hide_the_stored_draft`. |
| ADR-011 §8 | Two callers of the same key, content and actor may pass the replay lookup together; the store's unique attempt key decides. The **loser answers the winner's result**, read afresh (the store clears its tracker on the lost race, `FindReplayAsync` again in the same scope): the winner's draft as the 201 replay, the winner's recorded refusal as the same 409 replay. The same holds when a terminal record loses its race. Only a race that left nothing under the key (the number counter, a draft id) is the caller's 409 `concurrent_change` to retry. Proven with a real barrier race in `SalesDraftApiSqlServerRelationalAcceptanceTests` (one draft, one attempt, one audit row, both callers the same draft). |
| Owners, in order | Cari (`ISalesPartyAccountPortV1`, the host's `CounterpartySalesPartyAccountPortV1` over the authorized party read): party + account of that party + this company + both active, else 409 `account_state`. **SALES-A2a** — when the request names a `deliveryAddress` / `contact`: the Cari contact information of that account (`ISalesPartyContactInformationPortV1`, host adapter over the authorized contact-information read; registered centrally, 503 `counterparty_provider_unavailable` until then), read ONCE after the account: the id must be an ACTIVE operational address / contact of the account → else 422 `owner_reference_invalid` (field; not recorded); its `expectedVersion` must equal the owner's version → else the terminal 409 `version_conflict` (§7); the draft stores the identity + the owner's version, never the address text or the person. Settings money profile for `sales.order-draft.v1` on the business date: not found → 422 `unsupported_currency`; not in effect → domain `sales_money_profile_not_in_effect` (422); access → 403; else 503. Per line the Product eligibility (three bindings) and the order unit's Settings profile: field errors → 422 `unsupported_product` / `unsupported_unit`; runtime absent or outage → 503 `product_eligibility_unavailable`. A malformed owner answer (another company, another unit, empty identity) is 503 `reference_provider_invalid`. Nothing is cached, defaulted or copied. **Consistency of "another company":** the Cari port is asked for an account the *user* chose (`companyAccountId`), so an account of another company is the user's input problem — 409 `account_state` / `company_mismatch`; the Settings and Product ports are asked for *this company's* profile of a currency or unit (the company is the context, not an input), so a profile of another company is the provider breaking its contract — 503 `reference_provider_invalid`. |
| Contract deltas (fail-closed) | Decision 2: any `unitPrice` / `priceUnitId` / `discountPercent` → 422 `commercial_not_available` (T28; nothing of a price is stored). Decision 3: an owner reference whose owner port does not exist yet (responsible, payment terms, source strategy, package) → 422 `owner_reference_unavailable`; the delivery address and the contact left this delta with SALES-A2a (row "Owners, in order"). Both are marked deltas on #257; the #260 form sends the four as null. |
| List / detail | `pageSize` 1–100 (default 50), opaque company-bound cursor → 400 `invalid_cursor`; another company's draft → 404 (never 403). Kind/state/revision on the wire: `Order` / `Draft` / `Rev.0`; `version` is the concurrency token text. |
| Problems | `SalesDraftProblem`: status/title, `code`, `correlationId`, field `errors`; platform 401/403/404 code-less (#257). **Every 503 is code-less at the route** and the platform writer answers `service_unavailable`: the service's setup/provider codes (`permission_unregistered`, `counterparty_provider_unavailable`, `settings_provider_unavailable`, `reference_provider_invalid`, `product_eligibility_unavailable`) never reach the wire — the same boundary as the reference routes (`SalesDraftHttpAcceptanceTests`). A 201 carries the absolute `Location` of the detail route. |
| Shell | `SalesShellCapabilityProvider` (Infrastructure/Shell) declares `sales.drafts.list` / `create` / `detail` with the frontend's routes, **published** (`ImplementationAvailable: true`, S03-c). The registry never publishes a staged declaration (`ShellCapabilityService` filters on availability), so the flip precedes the `s03` browser run and that run is its acceptance (Product #316 precedent; Sales documents carry no activation envelope rule). A declaration grants nothing: an actor without `sales.drafts.read` / `sales.drafts.create` sees no Sales screen, and the grant is a human gate. The host registers the provider with its accepted revision (the central authority constant re-measured for the published set) and the C-01 envelope removes it as before. Real SQL: `SalesShellCapabilitySqlServerRelationalAcceptanceTests` (synthetic isolated grants: read → list + detail, read + create → all three, none → none). |
| Honest runtime today | With the real owners on main a create answers the **first owner that cannot**: an unknown customer is 409 `account_state`; with a real customer, the money profile (T02-b, GDC/TRY) answers and the Product runtime — composed by C3 but unconfigured in this host — is 503 (`product_eligibility_unavailable` in the service, `service_unavailable` on the wire). Measured by `SalesDraftApiSqlServerRelationalAcceptanceTests`; no green is painted. The positive path (a stored draft, the race, every replay) is proven in the same real host with SENTETIK owner ports for Cari, Settings and Product only. |

**Owner references (SALES-A2a, Business 5745076917 item 2).** The `deliveryAddress` and `contact` of a create are
the Cari owner's operational address and contact person of the draft's party account, read through
`ISalesPartyContactInformationPortV1` (the same Cari adapter class as the party port; registered centrally — until
then a named reference is 503 `counterparty_provider_unavailable`) ONCE, after the account answered and under the
same fence as every owner read. Listed and active at the given version → the draft stores `{id, expectedVersion}` =
the owner's identity and version text, never the address text or the person (the screens re-read the owner for the
presentation); not listed or inactive → 422 `owner_reference_invalid` (field `deliveryAddress` / `contact`, not
recorded); listed at another version → the terminal 409 `version_conflict` (ADR-011 §7, recorded and replayed under
the key, the product's pattern, the §8 race included). The other four references (responsible, payment terms,
source strategy, package) keep the 422 `owner_reference_unavailable` delta: their owners have no port and their
policies are open. Evidence: `SalesDraftServiceTests` (the `FakeContactInformation` cases) and the real-host case
`A_create_with_a_delivery_address_and_contact_stores_the_owners_versions_and_a_stale_reference_is_the_keys_terminal_refusal`.

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
| Domain | `Submit(expectedVersion, actor, now)`: Preparing/Rejected → PendingApproval (needs ≥1 line); `Approve`: PendingApproval → Approved; `Reject(…, reason)`: PendingApproval → Rejected (reason optional, ≤4000, `sales_approval_reason_too_long`). Any other transition, and any line change of a submitted or approved draft, is `sales_state_conflict` (DEC-SAL-002: an approved draft changes only through a revision, SALES-A2). Each transition names the expected version and advances it by one; `ApprovalFacts` (who/when submitted and decided, the reason) travel with the draft. `RestoreApproval` rebuilds a stored axis only when the facts fit the state and the version accounts for the transitions (a Preparing draft: exactly the lines' version); it runs once per rebuilt draft, restoring is not a transition. |
| Persistence | `Drafts.ApprovalState` + `SubmittedBy/SubmittedAtUtc/DecidedBy/DecidedAtUtc/RejectionReason` (migration `AddDraftApproval`; backfill 1 then no default, the `Outcome` pattern); `CK_Drafts_Approval` binds the facts to the state; `IX_Drafts_CompanyId_ApprovalState_CreatedAtUtc_Id` for the filter. The write guard lets the header move only along these columns (+ `UpdatedAtUtc`; `Version` is the token). `Down` refuses with THROW 51045 while any draft has left Preparing (forward fix). |
| Store | `UpdateDraftStateAsync(envelope, hash, command, draftId, mutate, audit)`: ONE save — the header's state/facts/version, the attempt row under `(company, command, key hash)` and the ADR-009 event (`sales.order-draft.submitted.v1` / `approved.v1` / `rejected.v1`, version before/after, before/after JSON, reason). Another company's draft is null (404, never 403). |
| ADR-011 | Commands `sales.order-draft.submit.v1` / `approve.v1` / `reject.v1`; content = canonical `{command, draftId, expectedVersion, reason}`; the same key + content + actor answers the persisted record AS IT IS NOW with `Idempotency-Replayed` and the first correlation (200; the Counterparty / Manufacturing pattern); another content or actor is one 409 `same_key_payload_mismatch` (§3). **§7 terminal refusals:** a stale version (409 `version_conflict`, field `expectedVersion`) and a wrong state (409 `sales_state_conflict`, field `approvalState`) are recorded under the key and replayed. **§8:** a same-key race loses on the attempt key and answers the winner; a lost draft-version race (another key moved the draft) is 409 `concurrent_change` to retry with a fresh read. Fences: before the replay (§5), after the replay read, right before the single save; none after the commit (S03). |
| Routes | `POST /api/v1/sales/drafts/{id}/submission` (create permission), `…/approval` and `…/rejection` (`sales.orders.approve`, company-wide; class and grant are central/human): one canonical `Idempotency-Key` (≤128) → else 400 `command_metadata_invalid`; strict `{expectedVersion}` (+ `reason` on the rejection only) → else 400 `validation_failed`; 200 `SalesOrderDraftResponse`; 404 code-less; 409 as above; 422 `validation_failed` (a line-less draft) / `sales_approval_reason_too_long`; 503 code-less. `GET /api/v1/sales/drafts?approvalState=<exact name>` narrows the list (400 otherwise). |
| Line reference port | `ISalesOrderLineReferencePortV1.GetApprovedLineAsync(orderId, lineId)` (Sales-owned; `SalesOrderLineReferencePort` registered in `AddSalesModule`): `Approved` + the line's stored identity facts (company, order, line, party, account, product + version, order unit, quantity text, Rev, version) only while the draft is Approved; otherwise `NotApproved` + the state; unknown or another company's draft/line → `NotFound`. It creates nothing: the consumer's (INV-AL-1 / LOG-4) own command creates its record under its own authority, idempotency and audit; a replay of that command creates no second need. |
| Shell | `sales.orders.approve` is a CONTROL of the S-04 detail (`ParentCapabilityId` = `sales.drafts.detail`; the detail's route; `sales.drafts.read` + `sales.orders.approve`; the Finance bank-account approval pattern). The submit button follows `sales.drafts.create`. |
| Frontend | S-04 shows the axis and the facts ("not yet" for what did not happen), the linked effects read from their owners (never assumed), and the commands by capability and state. **Linked effects:** per line, the count of `Active` physical allocations from Inventory's `GET /api/v1/inventory/physical-allocations` (company-bound: `X-Company-Id` of the page's bound company, the answer decoded against that company/order/line scope; the allocation panel's query key, so its create/release refreshes the count); the count is shown only after a successful read — loading, no permission, an error or an unverifiable answer show text, never an assumed 0; a full result window shows a lower bound ("en az N"), never an exact count. Logistics loading facts are now published by Sales's `GET …/{id}/loading-progress`; consuming that route in S-04 is a separate frontend slice. Each command is one idempotent attempt (the create's rules); a recorded transition re-reads the detail. S-02 shows the axis in its state column and filters on the server. |
| Not here | The DEC-SAL-005 cancellation is its own section below; allocation and loading records (INV-AL-1 / LOG-4 own them); price (T28); customer confirmation (Q01: internal approval suffices); an edit API. |
| Browser acceptance (`s04`) | The user flow in a real browser on the disposable stack (`browser:s04:ci`, `tests/e2e/sales-s04`, `normal` profile, synthetic actors from `infra/browser-suites/s04.override.yml`). The existing gated Sales fixture still supplies the approval journeys. E1 additionally materializes one Product-owned sales candidate only behind the complete fixture gate, resolves it through the real bounded Product-backed Sales reference endpoint, and has the preparer create and reopen one real Sales draft through the UI. The candidate store grants nothing and is absent from the normal host. Every transition in the suite is a real command through the screens and is re-read through the real API. Real SQL: `SalesDraftFixtureStoreSqlServerRelationalAcceptanceTests`; Product fixture materialization is isolated acceptance data, not GDC/Q6 data. |

## Delivery-schedule update (DEC-SAL-003)

DEC-SAL-003 (KILITLI, verbatim): "Termin üst bilgi, satır ve miktar dilimleriyle izlenir; termin güncellemesi
planlama/sevkiyat rolündedir ve yeniden onay gerektirmez, ancak auditlidir."

| Concern | Rule |
|---|---|
| Scope | Dates only: the header's requested date, every line's requested date and the date of every slice the line already carries. Every line is named exactly once and, per line, exactly its current slice ids — no slice is added, removed or re-split and no quantity changes (`sales_schedule_lines_mismatch` / `sales_schedule_slices_mismatch`, 422 on `lines` / `lines[i].slices`). Re-splitting slice quantities is NOT in the decision's text: a technical scope limit here, not a policy. A change that moves no date is refused (`sales_schedule_no_change`). |
| No re-approval | The approval axis, its facts and the commercial revision do not move; the concurrency version advances by one. Consumers keyed on the revision (Logistics loading, Inventory allocation) are untouched; the line port answers the NEW line date. Any approval state except Withdrawn (terminal, D2) accepts the update. |
| Authority | Its own name `sales.orders.delivery-schedule.write` (manifest v7; the technical literal is the owner's, Business #66 5808295853 / 5811361336). The create, approve and price names do not confer it and it confers none of them. It is registered centrally (`ErpPermissions.SalesOrdersDeliveryScheduleWrite`, company-wide, not Critical); an unregistered binding would answer 503 `permission_unregistered` (never a grant). Which persona holds it is a human gate. |
| Idempotency / audit | Command `sales.order-draft.reschedule-delivery.v1`; the commercial revision's ordering: fence → version shape → replay (§4/§7) → store reads the draft → last fence → ONE save; stale version / withdrawn = the key's terminal 409; §8 race answers the winner; no fence after the commit. Audit event `sales.order-draft.delivery-rescheduled.v1` with the dates before and after. |
| Persistence | APPEND-ONLY: migration `20260926090000_AddDeliverySchedules` adds `sales.DraftDeliverySchedules` (one row per recorded change: who, when, the version it produced, the header date) and `sales.DraftDeliveryScheduleDates` (one row per line, `SliceNo` 0, and per existing slice). The draft's own rows keep the dates as created (schedule 0) and are never rewritten; the write guard admits no column of either new table. The store rebuilds the current dates from the last schedule, before the approval axis (the version floor counts one step per schedule). Down refuses with 51054 while any schedule exists (forward fix). |
| Routes | `POST /api/v1/sales/drafts/{id}/delivery-schedule` (strict body: `expectedVersion`, `requestedDeliveryDate` (null allowed), `lines[{lineId, requestedDeliveryDate, slices[{sliceId, requestedDeliveryDate}]}]`; 200 + replay headers) and `GET …/{id}/delivery-schedules` (schedule 0 then every change, under `sales.drafts.read`). |
| Frontend | S-04 "Termin güncelle" behind the `sales.orders.delivery-schedule.write` control; the "Termin geçmişi" history under the detail read. |
| Central step (not in this slice) | permission registration, the shell control declaration + authority re-measure, OpenAPI pin, guard fingerprints (SalesStore, SalesDbContext, the migration's reviewed `Sql`), relational inventory names, the s04 disposable actor grant (human decision). |

## Delivered / remaining per line (AT-024)

AT-024 (verbatim): "Kısmi teslimat sipariş kalanını doğru tutar." DEC-SAL-001 keeps fulfilment a separate axis of the
order; the delivery facts are the delivery owner's (Logistics), never a Sales copy.

| Concern | Rule |
|---|---|
| Source | `ISalesOrderDeliveryReadPortV1` (Sales-declared, `Contracts/ISalesOrderDeliveryReadPortV1.cs`), answered by Logistics (`LogisticsSalesOrderDeliveryReadPort`, registered in `AddLogisticsModule`): per Sales order line and unit, the SHIPPED and DELIVERED totals Logistics recorded over every instruction of the context company (the stored share totals, delivered ≤ shipped ≤ loaded ≤ instructed). Sales never reads Logistics tables; the port carries no authority (LOG-4 D6) and creates nothing. Not composed → 503 `delivery_provider_unavailable` (code-less on the wire), never an assumed zero. |
| Route | `GET /api/v1/sales/drafts/{id}/delivery-progress` (`GetSalesOrderDraftDeliveryProgressV1`) under `sales.drafts.read`: `{draftVersion, lines[{lineId, lineNo, orderUnitId, ordered, shipped, delivered, remaining, excess}]}`, every line once in line order, exact decimal text in the order unit. Private, no-store; 404 code-less for an unknown or another company's draft (the owner is then never asked); 503 code-less. |
| Arithmetic | `remaining` = ordered − delivered, never below zero; `excess` = delivered above the order, else 0. Ö2 in the order unit's own dimension — a total in another unit (no conversion), above the scale, negative or delivered above shipped is a malformed owner answer (503 `reference_provider_invalid`), so no wrong number is shown. A line only instructed/loaded (shipped 0) adds nothing. |
| No tolerance | OPEN-004 (over/under-delivery tolerance) is open: no completion, warning, approval or "closed line" state is derived; the wire has no such member (pinned by a unit test). |
| Fences / effects | The draft read is measured before, after the draft read and after the owner's answer; the read writes nothing in either module (no attempt, audit, share or version effect — pinned on real SQL). |
| Replay | A replayed Logistics delivery (same key) is answered from its stored attempt and a repeated delivery id is the domain's duplicate refusal: neither appends a share, so neither is counted twice (real route, real SQL). |
| Frontend | S-04 "Teslimat durumu" (`SalesDraftDeliveryProgress`): mounted for an order that is or was approved (Approved, or a revision above Rev.0 — Logistics serves only approved lines); read afresh on every open (`gcTime` 0) so a delivery recorded in Logistics shows on the next open; decoded only as the progress of THAT detail (version, lines, units, ordered text), else "unexpected answer", never numbers. |
| Evidence | Unit: `SalesDraftServiceTests.DeliveryProgress` (Sales), `LogisticsSalesOrderDeliveryReadPortTests` (Logistics). HTTP: `SalesDraftHttpAcceptanceTests.Delivery_progress_is_a_private_read_…`. Real SQL + real host: `SalesDeliveryProgressSqlServerRelationalAcceptanceTests` (two cases). Frontend: `SalesDraftDeliveryProgress.test.tsx`. |
| Central step (not in this slice) | OpenAPI op `GetSalesOrderDraftDeliveryProgressV1`; guard fingerprints of `SalesModuleExtensions.cs` and `LogisticsModuleExtensions.cs`; relational inventory `sales.txt` += the two SQL cases. No new permission, shell capability or host DI line. |

## Loading facts on the Sales order (DEC-SAL-005)

| Concern | Rule |
|---|---|
| Source | The accepted `ISalesOrderLoadingReadPortV1`, answered by `LogisticsSalesOrderLoadingReadPort`: every loading-instruction line linked to this Sales order line, grouped in its stored unit. Sales reads no Logistics table and stores no copy. |
| Route | `GET /api/v1/sales/drafts/{id}/loading-progress` (`GetSalesOrderDraftLoadingProgressV1`) under `sales.drafts.read`: `{draftVersion, lines[{lineId, lineNo, orderUnitId, ordered, loadingInstructionLines, instructed, loaded, shipped, delivered}]}`. Every Sales line appears once; a zero is emitted only after the owner answered successfully. Private/no-store, company-bound; 404/503 are code-less. |
| Exactness | No conversion or rounding. The owner invariant `delivered ≤ shipped ≤ loaded ≤ instructed`, the order-line unit and Ö2 scale are validated; absent, failing or malformed owners fail closed with 503 instead of showing a plausible number. |
| Effects | Read-only in both modules. Existing `sales.drafts.read` is the only authority; the route creates no Logistics permission, Work command, attempt, audit or stock movement. |
| Evidence | Sales unit and HTTP wire tests; the existing two inventoried `SalesDeliveryProgressSqlServerRelationalAcceptanceTests` cases additionally exercise this route through the real composed host and real Logistics SQL records. |

## Cancellation of an approved order (DEC-SAL-005, AT-007)

DEC-SAL-005 (KILITLI, K1, verbatim): "Sipariş iptalinde aktif tahsis, bağlı satın alma/üretim, irsaliyesiz yükleme,
faturalanmamış irsaliye ve eşleşmemiş avans/dekont etkisi gösterilir." — "Etki önizlemesi olmadan iptal yok." AT-007
(verbatim): "İptal ve ters işlem geçmişi silmez." DEC-SAL-001: the lifecycle is its own axis (DURUM_MAKINELERI "Yaşam
döngüsü: Taslak / Aktif / Kapalı / İptal").

**What is decided and what is not.** Decided: the preview is mandatory and names the six categories; cancellation deletes no
history; changes of party/product/quantity/price/discount/currency/term/risk go through a revision (DEC-SAL-002, K3 for
price/discount). NOT decided anywhere in `docs/decisions` or the acceptance set: who may cancel, whether a cancellation needs
a second approval, what blocks it (e.g. a delivered quantity, an active loading), what happens to the owners' records
(allocation release, loading void), whether a partly delivered order can be cancelled for its remainder, and whether an order
in re-approval (Rev.N back in preparation) can be cancelled. The fail-closed reading is implemented and each gap is listed as an
open question in the PR; nothing below invents a release or reversal of another module's record.

| Concern | Rule |
|---|---|
| Axis | Lifecycle `State`: `Draft` → `Cancelled` (terminal). The approval axis keeps `Approved` and its facts (who approved stays who approved); the revision, lines, archived revisions and schedules do not move (AT-007). Every mutation entry refuses a cancelled order (`sales_state_conflict`), the population test pins it. |
| Who | Its own name `sales.orders.cancel` (manifest v8), company-wide, not conferred by create/approve/price/schedule and conferring none of them. Which persona holds it is a human gate; if a cancellation must need two people, the Critical class (grant-time dual control) is the central lever — not decided here. |
| Only approved | `Approved` only. A never-approved Rev.0 draft is withdrawn by its preparer (D2); a draft in re-approval has no approved revision in force and is refused (open question). |
| Preview | `GET …/{id}/cancellation-impact` under the cancel name: each category read afresh from its OWNER through a Sales-declared port the owner registers in its own module — `ISalesOrderAllocationReadPortV1` (Inventory: ACTIVE physical allocations per line, count + kg) and `ISalesOrderLoadingReadPortV1` (Logistics: every loading-instruction line per line and unit — instructed/loaded/shipped/delivered; main has no dispatch note, so every loading is "without a dispatch note"; the AT-024 port answers only definite totals by design, so it is not reused). Purchasing, production, invoicing/dispatch notes and advances have NO link to a Sales order on main: `NoSource` ("kaynak yok"), never zero. An owner not composed, failing or answering malformed: `Unreadable`, its numbers null. The ports carry no authority (LOG-4 D6); the cancel name is measured before, after the draft read and after each owner. The token is SHA-256 of the canonical preview (draft, version, revision, states, categories, lines). |
| Fail-closed | `cancellable` only when Approved, not cancelled, and no category is `Present` or `Unreadable`. An active allocation or ANY loading-instruction line (Logistics has no instruction void on main) blocks: Sales releases nothing — the user resolves the record in its owner (e.g. releases the allocation in Inventory) and previews again. `NoSource` does not block: no module on main can hold such a record; when an owner publishes the link its port joins the preview. |
| Command | `POST …/{id}/cancellation` `{expectedVersion, reason, impactToken}` (strict; reason required ≤ 4000). Ordering: fence → version shape → the key's replay (§4/§7; asks no owner) → the store reads the draft → fence → the domain's preconditions (version, state, reason) → each owner (fence after each) → token equality → blockers → last fence → ONE save. Terminal 409s recorded and replayed: `version_conflict`, `sales_state_conflict` (field `state`), `sales_cancellation_impact_changed` (field `impactToken`), `sales_cancellation_blocked` (fields = blocking sources). An unreadable owner is 503 (code-less, not recorded). |
| Persistence | APPEND-ONLY `sales.DraftCancellations` (one per draft: revision, who, when, reason, version produced, impact token + JSON of the categories); the header's `State` moves 1 → 2 (the write guard admits only that direction) beside its version. `CK_Drafts_State`: `[State] IN (1,2)` and Cancelled only with `[ApprovalState] = 3`. Migration `20260927120000_AddDraftCancellations`; Down refuses with 51055 while any cancellation exists (forward fix). The store rebuilds the cancellation before the approval axis (one version step). |
| Audit | `sales.order-draft.cancelled.v1` with the state/version before and after, the facts and the impact; SourceChannel from the request (Web). |
| Next module | The line port answers a cancelled order's lines `NotApproved` (`Cancelled`) and the approved-line list (the Logistics picker) skips them; Inventory's allocation create and Logistics' instruction create refuse new links. Residual race (declared): no transaction spans Sales and the owners — a link committed between the last owner read and the save is not seen. |
| Read | `GET …/{id}/cancellation` (draft read): the recorded facts or `null`. The detail wire is unchanged apart from `state` = `Cancelled`. |
| Frontend | S-04 "Sipariş iptali" behind the `sales.orders.cancel` control (shell 1.3): the preview is read only when asked, statuses per category ("kaynak yok"/"okunamadı" never 0), reason + one keyed attempt; a cancelled order shows "İptal edildi", its record, and no schedule/price/allocation/approval action. |
| Evidence | Unit: `SalesOrderDraftCancellationTests`, `SalesDraftServiceTests.Cancellation`, `SalesPersistenceModelTests` (guard/table/migration), `SalesShellCapabilityProviderContractTests`, `InventorySalesOrderAllocationReadPortTests`, `LogisticsSalesOrderLoadingReadPortTests`. HTTP: `SalesDraftHttpAcceptanceTests.Cancellation_…`. Real SQL: `SalesOrderCancellationSqlServerRelationalAcceptanceTests` (4) and `InventorySqlServerRelationalAcceptanceTests.Sales_order_cancellation_is_blocked_by_an_active_allocation_until_it_is_released_on_real_SQL`. Frontend: `SalesDraftCancellation.test.tsx`. Browser: `s04-cancellation.spec.ts`. |
| Central step (not in this slice) | `ErpPermissions.SalesOrdersCancel` (All + CompanyWideOnly); shell chain re-derive for Sales 1.3 (+1 control); OpenAPI +3 ops (`GetSalesOrderDraftCancellationImpactV1`, `CancelSalesOrderDraftV1`, `GetSalesOrderDraftCancellationV1`) and the `state` enum widening; guard fingerprints (SalesDbContext, SalesStore, SalesModuleExtensions, InventoryModuleExtensions, LogisticsModuleExtensions, the migration's reviewed `Sql`); relational inventory `sales.txt` +4, `inventory.txt` +1; the s04 disposable canceller actor (human grant decision). No host DI line: both ports are registered by their owners' modules. |

## Quantity revision of an order (DEC-SAL-002, AT-023)

DEC-SAL-002 (KILITLI, K0, verbatim): "Cari, ürün, miktar, fiyat, iskonto, para, vade ve risk istisnası değişirse yeni revizyon
ve yeniden onay gerekir." — "Rev.0 korunur; iç not/etiket açıklaması yeniden onay istemeyebilir." AT-023 (verbatim): "Onaylı
siparişte kritik satır değişikliği eski onayı geçersizleştirir." The price and the discount already have the K3 commercial
revision; this slice adds the ORDERED QUANTITY — the one other decided field the draft model carries and can revise without
another decision.

**What is decided and what is not.** Not revised here, each for a stated reason (fail-closed; nothing is guessed): *payment
term* — the draft never stores one (`paymentTerms` is refused at create, 422 `owner_reference_unavailable`: no owner port);
*risk exception* — not modelled anywhere (OPEN-010 risk/collateral and OPEN-009's policy registry are KISMEN, no runtime),
so no field is invented; *currency* — the only approved money profile is GDC/TRY, and a currency change would need a second profile and a
restated price (a price in the old currency does not carry over); *party* and *product* — a party change re-binds the Cari
account, the delivery address and the contact, a product change the product snapshot, order unit, price unit and the
delivery plan's unit: each its own decision. Undecided and implemented fail-closed: who may change an approved order's
quantity (its own name, granted to nobody — NEEDS-SINAN), and what a quantity change does to the owners' records (refused
while any exists — the cancellation's reading).

| Concern | Rule |
|---|---|
| Domain | `ReviseQuantities(expectedVersion, lines, actor, reason, now)`: every current line exactly once (`sales_revision_lines_mismatch`); Ö2 in the line's OWN order unit — kg ≤ 3 decimals, a piece whole, never rounded, never converted (`sales_quantity_scale_exceeded`, per line); the text must be the value; the delivery plan is NOT re-split, so a quantity below the line's planned slices is `sales_slices_exceed_quantity` on that line (a technical scope limit); a reason is required ≤ 4000 (`sales_revision_reason_required` / `_too_long`); no value change → `sales_revision_no_change` (a value-equal text keeps the stored text). From `Approved`: the approved revision is archived FIRST (approval facts, money profile, every line's quantity text and commercial values, and the reason), then `Rev.N+1`, back to `Preparing` with no approval facts. From `Preparing`/`Rejected`: the quantities change, no archive. `PendingApproval`, `Withdrawn` and a cancelled order: `sales_state_conflict`. `RequireQuantityRevisable` runs the preconditions alone (the service asks it before any owner). |
| Re-approval | The new revision is approved through the EXISTING submit/approve commands; nothing new. Until then the line port answers `NotApproved` (+ the state) and the approved-line list skips the order: no consumer acts on unapproved figures. After the approval the port answers the new quantity at `Rev.N`. |
| Downstream (fail-closed) | The owners that can link a record to an order line are read afresh through the cancellation's ports — Inventory's ACTIVE allocations (`ISalesOrderAllocationReadPortV1`), Logistics' loading-instruction lines (`ISalesOrderLoadingReadPortV1`) — for EVERY line of the order (the whole order re-enters approval). Any record → 409 `sales_revision_blocked` (fields = the blocking sources; recorded, replayed). An owner not composed, failing or malformed → 503 (code-less, not recorded). Sales releases nothing: the user resolves the owner's record in its own screen, then revises. A loading line exists before any delivery, so AT-024 progress is never left beside a changed quantity. Residual race (declared): no transaction spans Sales and the owners. |
| Authority | Its own name `sales.orders.quantity.write` (manifest v9), company-wide; create, approve, price, schedule and cancel do not confer it and it confers none of them. An unregistered name is 503 `permission_unregistered` (never a grant). Which persona holds it is a human gate. |
| Idempotency / audit | Command `sales.order-draft.revise-quantity.v1`; content = `{command, draftId, expectedVersion, reason, lines}`. The cancellation's ordering: fence → version shape → the key's replay (§4/§7; asks no owner) → the store reads the draft → fence → the domain's preconditions (stale / wrong state asks no owner) → each owner (fence after each) → blockers → last fence → ONE save. Terminal 409s recorded and replayed: `version_conflict`, `sales_state_conflict`, `sales_revision_blocked`. Audit `sales.order-draft.quantity-revised.v1` with the quantities before and after and the superseded revision's reason; SourceChannel from the request (Web). |
| Persistence | The line row's `Quantity` + `QuantityText` become writable (the write guard admits exactly those beside the three commercial columns — a declared widening of "lines never change"; identity, product, unit and slice columns stay immutable, pinned). The archived approved revision (`sales.DraftRevisions`) gains `Reason` (nullable: null for a price revision). Migration `20260928090000_AddRevisionReasons`; Down refuses with 51057 (51056 is Logistics') while any reason exists (forward fix). The store's shared line-revision core writes the archive, the header, the command's own line columns, the attempt and the audit in ONE save, and checks that a quantity revision moved no price and a price revision no quantity. |
| Routes | `POST /api/v1/sales/drafts/{id}/quantity-revision` (strict `{expectedVersion, reason, lines[{lineId, quantity}]}`; 200 + replay headers; 400/404/409/422/503 as above). The revisions route (`GET …/revisions`) carries each archived revision's `reason` (contract delta: new member). |
| Frontend | S-04 "Revizyon oluştur" behind the `sales.orders.quantity.write` control (shell 1.4): per line the new quantity (the current one shown), a required reason, one keyed attempt; an approved order warns that it re-enters approval; a blocked refusal names the owner's record. The revision history shows each archived revision's reason and, per line, the quantity of the revision that followed it (the next archive, or the current detail). |
| Evidence | Unit: `SalesOrderDraftQuantityRevisionTests`, `SalesDraftServiceTests.QuantityRevision`, `SalesDraftQuantityRevisionJsonTests`, `SalesPersistenceModelTests` (guard + migration Down). HTTP: `SalesDraftHttpAcceptanceTests.Quantity_revision_…`. Real SQL: `SalesOrderQuantityRevisionSqlServerRelationalAcceptanceTests` (4). Frontend: `SalesDraftQuantityRevision.test.tsx`. Browser: `s04-quantity-revision.spec.ts`. |
| Central step (not in this slice) | `ErpPermissions.SalesOrdersQuantityWrite` (All + CompanyWideOnly, not Critical); shell chain re-derive for Sales 1.4 (+1 control); OpenAPI +1 op (`ReviseSalesOrderDraftQuantityV1`) and the `reason` member of `SalesDraftRevisionV1`; guard fingerprints (SalesDbContext, SalesStore, the migration's reviewed `Sql`); relational inventory `sales.txt` +4; the s04 preparer's grant of the name (human decision). |

## List price reference and adjustments on the order line (AT-018 / AT-021)

User decision 2026-09-30: the Product module owns price lists; on an order line the list price is shown as a REFERENCE and
is the default until manually changed; the person may overwrite the price, give a discount on it as an amount or a percent,
and add a maturity surcharge (vade farkı) as an amount or a percent; the reference and the adjustments stay visible. Master
§10.2: "Liste fiyatı, sıralı ayarlamalar/iskontolar ve net fiyat ayrı gösterilir." Built on the K3 commercial revision (the
same command, permission, idempotency, archive and re-approval) — no new command.

| Concern | Rule |
|---|---|
| Wire (contract delta, all optional) | Commercial revision line: `priceReference {priceListId, versionNo}` (ids only; the client never sends the list price), `discountAmount` (instead of `discountPercent`), `surchargePercent` / `surchargeAmount`. Reads: `GET …/{id}/line-pricing` (draft read AND price read, else 403) → per line `unitPrice`, `priceSource` (`Reference` / `Manual`), `priceReference`, the adjustments and `netUnitPrice`; `GET …/{id}/lines/{lineId}/price-references` (price write) → the Product owner's candidates. The detail wire is unchanged (the S-04 exact decoder keeps working). |
| Reference | Read from Product's `IProductPriceReferenceReaderV1` under the price-write fence (after every awaited read), in the draft's currency (the Settings money profile's code for the draft's currency and operation) on the draft's business date; copied onto the line (`PriceReferenceSnapshot`: list, version, validity, currency, VAT basis, price unit, list price, floor) — a later list version never changes a line. A version that no longer prices the line → 422 `price_reference_unavailable`; another currency → 422 `price_reference_currency_mismatch` (not recorded); the owner not composed → 503. |
| Default and overwrite | The line's `unitPrice` is the reference price by default (the screen fills it); a different value is an overwrite (`priceSource = Manual`) and the reference stays visible. The reference must be in the line's price unit (`sales_price_reference_mismatch`). |
| Adjustments | Discount: percent (0..100) OR amount (≤ price, money scale); surcharge: percent (0..100) OR amount (money scale). Both kinds at once → `sales_adjustment_kind_conflict`; an adjustment or reference without a price → `sales_adjustment_without_price`. |
| Net | `SalesLinePriceCalculator` (Sales.Domain, the one calculator): base − discount (percent of base, or amount) + surcharge (percent of the discounted price, or amount); exact decimal, NEVER rounded (the rounding policy of totals/invoices is not decided). The net is not stored. |
| Floor (AT-021) | When the reference carries a floor, a net below it is refused: 422 `sales_price_below_floor` on `lines[i].unitPrice` — fail-closed, because no exception approver is decided (ASSUMPTION-PRL-5); it is never passed silently. |
| Persistence | `sales.DraftLines.PricingJson` and `sales.DraftRevisionLines.PricingJson` (nullable JSON beside the three commercial columns, `CK_*_Pricing`: only beside a price, valid JSON); the write guard admits `PricingJson` with the commercial columns; strict restore (unknown member / empty object → corrupt row, refused). Migration `20260930120000_AddLinePricing`; `Down` refuses with `THROW 51064` while any pricing exists. The audit event of the commercial revision carries the reference, the adjustments and the net. |
| Screens | S-04: "Satır fiyatları" (reference, source, discount, surcharge, net — the server's) and the price form's "Liste fiyatını getir" → "Bu fiyatı kullan", discount kind + value, surcharge kind + value. |
| Evidence | Unit: `SalesLinePricingTests`, `SalesDraftServiceTests.LinePricing`, `SalesPersistenceModelTests` (snapshot = model). Frontend: `SalesDraftLinePricing.test.tsx`, `commercialRevision.test.ts`. Browser (written, not run here): `tests/e2e/sales-s04/specs/s04-price-list.spec.ts`. |

Open (NEEDS-SINAN): who approves a below-floor price (AT-021 "doğru onaya gider"); whether a discount/surcharge limit applies per
role; the rounding of the net for totals; whether vade farkı should derive from the payment term once payment terms exist.

## Deliberate limits of this slice (not policy choices)

- **No real positive create today.** #257 makes the currency mandatory and validated through the Settings money profile with the central owner's accepted Sales money operation identifier; the approved Settings provider answers **Unavailable** for money and that operation identifier does not exist, so a contract-faithful create is 503 with every real provider. Product eligibility is not host-registered (C3) and the GDC family data is missing (#300), so every line is 503 as well. Only the kilogram unit path is real (GDC kg). SALES-P2's real-SQL tests therefore prove the negative path with the real providers and the positive path only with **isolated synthetic owner sources**; no PR claims a positive runtime, and no currency, family, profile or grant is invented.
- **No price, discount, tax, exchange rate or total** (T28); values are text and unvalidated.
- **No party/product/currency/payment-term/risk revision**; the commercial revision (K3), the quantity revision (DEC-SAL-002) and the cancellation (DEC-SAL-005) are above.
- **No HTTP, permission, host DI, OpenAPI, capability** (next slices); no host registration of Product eligibility (C3, grant pending).
- **No shipment, invoice, collection or stock effect** — separate owners (T11/T12/T32/T29).

## dependency_wait

- **SALES-P2 central registrations (CARİ, single writer of central files):** solution entry, `Api.IntegrationTests` project reference, module dependency allowlist (Sales.Infrastructure → Audit.Infrastructure), tenant-bypass guard pins (`SalesDbContext`, `SalesStore` SqlClient 1, `SalesModuleExtensions` provider branch, design-time factory), RCSI verifier names (+6). Until they land, the guard inventory and the integration-test build are red by design.
- **Host registration of `AddSalesModule` / `InitializeSalesModuleAsync`:** the API slice (next), with the permission names and the endpoints.
- **SALES-P2 (this slice):** `Sales.Infrastructure` — `sales` schema (drafts, lines, slices, attempts, counter), (CompanyId, CommandName, IdempotencyKeyHash) attempt key (ADR-011) + canonical hash + actor comparison, rowversion, audit envelope, six real-SQL cases; CARİ's central registrations (tenant guard inventory, allowlist `Sales.Infrastructure → Audit.Infrastructure`, integration csproj, verifier counts).
- **Settings money profile** (T02-b, CARİ: the approved GDC/TRY money profile): the real positive create. The Sales money operation identifier is **not** waited for: it is the Sales owner's technical mapping (Business #254 5654539215) — `SalesMoneyOperations.OrderDraftV1` = `sales.order-draft.v1`, the Finance pattern — carried on `MoneyProfileSnapshot.Operation` and required by the domain (`sales_snapshot_operation_mismatch`). The key chooses no rate, scale or date. The only thing a real draft waits for on the money side is the Settings provider's approved profile.
- **C3:** Product eligibility host registration and the GDC family definition data; a human grant.
- **Cari:** the account belongs to the Party and is active in this company (#262 read, at command time).
- **Owner references** (responsible, payment terms, source strategy, package): authorised reads and value snapshots (#257's owner-binding list). The delivery address and the contact are the Cari contact information since SALES-A2a (`ISalesPartyContactInformationPortV1`; the host adapter and its registration are central).
- **HTTP slice:** the create/list/detail endpoints of `CONTRACT_V1.md`, permissions (registered centrally, granted by a human), 201 (+ replay headers and `isReplay` on the same attempt; ADR-011 §4 delta), cursor codec.

```bash
dotnet test tests/GoymenERP.Sales.UnitTests/GoymenERP.Sales.UnitTests.csproj
```
