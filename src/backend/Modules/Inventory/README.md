# Inventory module

Owner: GOYMEN-URUN (Product owner, assigned Inventory by the coordinator on 2026-09-18).
Source: integrated plan v2 (#275 `a5b8143b`) §5, §7.1, §13, R3 and R6; Sinan's decisions of
2026-09-18 Ö1–Ö3 (`docs/decisions/SINAN_KARARLARI_2026-09-18_O1_O8_VE_SON_ASAMA.md`). Both files
are on the #275 branch at `a5b8143b`, **not on main**; the decision text is Business's record on
#66 (5722637822).

## LOG-5 — definite-shipment Inventory boundary (contract predecessor)

`GoymenERP.Modules.Inventory.Application.IInventoryDefiniteShipmentParticipantV1` fixes the single-use,
Inventory-owned boundary for one definite shipment. The central host supplies an Inventory runtime participant
already enlisted in its open transaction; the participant stages allocation consumption, one label stock exit,
audit and outbox, and never commits or rolls back the host transaction. The application contract deliberately
exposes no database handle or independent transaction factory. The request carries CompanyId, shipment/instruction/
line/allocation/label/movement identities, exact allocation and label versions, lossless kg text and UTC time.
The stored allocation supplies the physical owner/custodian/location; Logistics cannot choose them.

This commit is deliberately **NOT READY for runtime DI**. Main has no Inventory allocation aggregate/store/
version or Inventory outbox persistence yet. Registering a positive implementation would therefore invent
success and violate DEC-STK-004 plus LOG-5 atomicity. `InventoryDefiniteShipmentContractV1.Validate` and its
tests close only the policy-free wire/participant shape. The next Inventory owner predecessor is the real
allocation persistence and transactional outbox implementation with SQL concurrency/replay evidence; only
then may central bind this factory into the Logistics host. No new permission or grant is introduced.

## R13 — draft parent-roll reference predecessor (Business #66/5778472429 A)

`GoymenERP.Modules.Inventory.Application.IInventoryParentRollCandidatesV1.SearchAsync` accepts
`InventoryParentRollQueryV1(Q, Limit=25)` (label-code search, max64 characters, examined window1..100)
and returns `Items/SearchWindowFull`. `IInventoryParentRollValidationV1.ValidateAsync` accepts
`InventoryParentRollSelectionV1(LabelId, ExpectedLabelVersion)`. There is deliberately no PlannedKg:
this read does not choose equality, partial-coil, upper-bound, reservation or availability policy.
`AddInventoryParentRollReferences()` registers only these owner ports. Central owns HTTP/host publication.
`InventoryParentRollApiV1` fixes that publication's owner contract at
`GET /api/v1/inventory/references/parent-rolls` / `GetInventoryParentRollCandidatesV1`; central maps and
documents it without changing the DTO or its stock semantics.

Each result preserves company, label ID/code/version, ProductId, actual lot and separate label/lot heat/coil,
document status/kinds, positions' owner/custodian/location, and lossless physical kg totals.
`UnblockedPhysicalKg` means the stored `BlockState.Free` buckets, **not available stock**: allocations and
loading holds are not deducted. `ProductionUseRequiresRevalidation` is always true. Missing coil is not
a type discriminator. Blocked/pending-document own-company stock can be a draft reference without being
released for consumption. Other-owner stock remains visible with `DraftSelectable=false` and context-required;
no stock means no invented owner/location and a nonselectable no-physical-position result.
Existing INV-1 `StockLedger.FromStoredBalances` validates the stored state: a label's nonzero buckets
share one physical place (owner/custodian/location/product), while Free/Blocked buckets remain separate.
A corrupt multi-place label is unavailable; this does not redefine or collapse legitimate positions.

Both existing company-wide `inventory.labels.read` and `inventory.balances.read` are freshly measured before
and after the awaited owner read, together with actor/company/membership fences. SQL retains tenant filters
and explicit captured-company predicates. Label version is compared before/after document/balance reads;
any concurrent version change rejects the observation. No tracking, save, migration, grant or initializer.

`InventoryParentRollException.StatusCode/Code` is the owner error channel:

| Status | Code suffix (full constants in InventoryParentRollCodesV1) | Consumer treatment |
|---|---|---|
|401/403|unauthenticated / forbidden|No facts; never persist a business refusal for lost authority|
|400|invalid_query / inventory_label_version_invalid|Canonical positive Int64 text, no leading zeros|
|404|invalid_reference|Unknown and foreign label are indistinguishable|
|409|inventory_label_version_conflict|Stale requested version; authorized terminal refusal after replay lookup|
|409|read_conflict|Concurrent capture; retryable/unrecorded, not terminal|
|422|context_required / no_physical_position|Nonselectable draft reference; no fabricated ownership or allocation|
|503|unavailable|Provider/store/setup failure; no empty/positive fallback|

Successful validation guarantees exact server CompanyId, requested LabelId and ExpectedLabelVersion,
`DraftSelectable=true`, `DraftRefusalCode=null`, `ProductionUseRequiresRevalidation=true`. A nonselectable
candidate throws422 on validation. Manufacturing owns the canonical input hash (including label version),
recorded replay lookup before fresh validation, its post-read write fence and terminal replay. Replaying a
recorded command must not re-check subsequently changed Inventory. This predecessor writes no command,
reservation or movement and does not bypass Manufacturing's approval/start/issue revalidation.

## INV-1 — first independent slice of T07 (domain + tests only)

What this slice holds, as real behaviour with tests:

| Rule | Where | Source |
|---|---|---|
| A stock movement is an immutable physical trace; a wrong one is corrected by a new linked movement later, never edited | `Movements/StockMovement` | §7.1 |
| Balances change only by applying a movement, and reconcile with the movements bucket by bucket | `Balances/StockLedger` (`Apply`, `Reconcile`) | §7.1 |
| No negative physical stock; a refused movement changes nothing (all lines checked together) | `StockLedger.Apply` | §7.1 |
| The same movement applies once, and a reconciliation refuses a movement given twice | `StockLedger.Apply`, `Reconcile` | R3 (domain side only) |
| Transaction company, owner of the goods, custodian who keeps them and physical location are FOUR separate facts, all in the balance key | `Stock/StockPosition` (`StockOwner`, `StockCustodian`) | §5 |
| Stock totals are per owner: a customer's goods kept by us never count as our stock (10 own + 5 customer ≠ 15 free) | `PhysicalTotal`/`FreeTotal(company, owner, product)` | §5, §7.1 |
| A label is one physical piece: its non-zero buckets share one company, owner, custodian, location and product, and may differ by block state only (`inventory_label_in_two_positions`); moving it is a transfer, a second piece a split (both T13) | `StockLedger.Apply` | §7.1 |
| A position's product is the label's own product; no position can claim a label is another product | `StockPosition` takes the `MaterialLabel` | §7.1 |
| Blocking is its own dimension: it moves stock out of free, never out of the physical total, and blocked stock cannot leave by `Issue` until it is released | `StockMovementKind.Block/Release/Issue`, `FreeTotal` vs `PhysicalTotal` | §7.1 |
| A label is the physical identity; a reprint is the same identity plus an audited print and creates no stock | `Labels/MaterialLabel` | §7.1 |
| Lot/label trace is mandatory; a missing mandatory document never hides the physical arrival — the piece is recorded and waits (`PendingDocuments`); attaching a document is recorded with kind, time and actor | `MaterialLabel`, every line carries a label | Ö3 |
| kg exact to at most 3 decimals; a 4th decimal is refused, never rounded or cut | `Quantities/Kilograms` | Ö2 |
| Cost is `NotCalculated` / `WaitingPolicy` / `Known`; an unknown cost is null, never 0 | `Costing/CostResult` | R6 |

Other modules are referenced by `Guid` identity only (company and location: Organization;
owner: Organization or Counterparty; product: Product; currency: Settings). No project
reference to another module, so the module-dependency allowlist does not change.

Not in this slice: transfer, count, return and reversal (T13); split/merge labels (new
identity, T13); scrapping blocked material (T13); allocation (T11); purchase receipt (T09);
any HTTP, permission or UI.

Deliberate limits of this slice:

- **Kilograms only.** Ö2 also fixes pieces as whole numbers, but Ö1's first path is kg→kg and
  this slice stores kg alone. Piece-based stock is a later slice; until then a quantity
  accepted in pieces elsewhere (Purchasing) has no Inventory counterpart.
- **Ö3 "documents pending" is a flag here.** `IsReleasableForProductionOrShipment` refuses no
  movement yet; enforcing it belongs to allocation (T11) and shipment, which are not built.
- **Actors are text** in the domain. Print and document-attachment actors stay free text on the
  label; since INV-2 every stored write also carries the ADR-009 audit envelope (below).

## INV-2 — persistence (first real layer of T07)

Decision this builds on: ADR-INV-001 option A (`docs/adr/ADR-INV-001-label-company-scope.md`).

| Rule | Where | Held by |
|---|---|---|
| A label belongs to the company holding its stock; that company never changes | `MaterialLabel.CompanyId`, `StockPosition` (`inventory_label_company_mismatch`) | domain + write guard |
| A label code is unique within its company only; no group-wide code index | `IX_Labels_CompanyId_Code` | SQL (collation: INV-3 below) |
| Every row is a tenant row: global filter on the server-resolved company; a row of another company, a company change or a write without a company is refused | `InventoryDbContext` | EF filter + write guard |
| A location is referenced by (company, location) against Organization's alternate key; a company by id | `FK_*_Locations_CompanyId_LocationId`, `FK_*_Companies_CompanyId` | SQL (547) |
| Movements, lines, prints and document attachments are append-only; a balance changes its quantity only; a label its version only; nothing is deleted | `InventoryDbContext` | write guard |
| The movement id is the idempotency identity: same id + same canonical content = replay with no second effect; other content = `inventory_movement_id_conflict` | `InventoryStore.RecordMovementAsync` (`PayloadHash`) | store + PK |
| One source (or source line) causes at most one movement of each kind (ORG #281 P3-1) | `UX_Movements_Source`, `UX_Movements_SourceLine` | store check + filtered unique indexes; whole vs line: INV-3 below |
| A label that holds stock is not received again; once empty it may be (ORG #281 P3-1) | `StockLedger` (`inventory_label_already_in_stock`) | domain, against stored balances |
| Two writes that read the same piece cannot both commit (the SQL race guard) | label `Version` + `RowVersion` bumped by every write touching the label | optimistic concurrency (`inventory_concurrent_change`) |
| A movement, its balances, the label versions and its audit event commit together or not at all | one `SaveChanges` | SQL transaction |
| kg scale is the company's approved Settings profile (`SettingsUnitIdentitiesV1.Kilogram`), never wider than Ö2's 3; every Settings failure is a refusal with the owner's code | `InventoryKilogramScale` | fail-closed |
| Every write carries the ADR-009 envelope (actor, permission, roles, correlation, causation, idempotency key, channel) from the caller | `InventoryCommandMetadata` → Audit collector | same save |

Deliberate limits of this slice:

- **No HTTP, permission, host DI or UI** (INV-3). `AddInventoryModule`/`InitializeInventoryModuleAsync` exist but the host does not call them; the permission code in the audit envelope is the caller's, and this module invents none.
- **No transfer (T13).** A label's company never changes; a transfer will close it and record a new label with a new id in the receiving company (ADR-INV-001).
- **Which product's stock unit is kilogram** is Product's binding (its reference catalogue), not checked here; only the kilogram profile's scale is.
- **Cost** is stored as the domain gives it; a known amount that needs more than 18 decimals or 20 integer digits is refused (`inventory_value_does_not_fit`), never rounded. No money policy is chosen.
- **Existence by id.** Primary keys are table-wide (ADR-002 pattern, ADR-INV-001 P3-2). Ids are the caller's new GUIDs; a transfer never reuses a source id.

## INV-3 (first part) — #293 review follow-ups

| Rule | Where | Held by |
|---|---|---|
| **Label code, one comparison (SİPARİŞ P3-1).** The code column's collation decides whether two spellings are one code. Another spelling of a stored code is `inventory_label_code_taken` both with a new id (the unique index) and with the same id (the store asks SQL the same question). A replay repeats the stored spelling exactly; another code under the same id is still `inventory_label_conflict` | `InventoryStore.SaveLabelAsync`, `IX_Labels_CompanyId_Code` | SQL collation, in both paths |
| **Canonical cost amount (SİPARİŞ P3-2).** The payload hash writes a known amount without trailing zeros, as it already did for kilograms: `5.00` and `5` are one content, so the same movement offered again is a replay | `InventoryStore.PayloadHash` | store |
| **Whole or line, never both (ORG P3-1).** For one source and one movement kind the first movement fixes the grain (whole = no source line, line = with a source line); a movement at the other grain is `inventory_source_grain_mismatch` | `StockLedger` (domain), store check, `inventory.SourceGrains` (key: company, kind, source kind, source) referenced by every movement (`FK_Movements_SourceGrains_*`, `CK_Movements_SourceGrain`) | domain + store + SQL (FK/PK; a race loses with `inventory_concurrent_change` and gets `inventory_source_grain_mismatch` when retried) |

**Collation.** `Labels.Code` and both `SourceKind` columns use the database's default collation. That is case-insensitive on SQL Server's default, and the relational test checks this before relying on it. No collation is chosen here: a binary or Turkish collation is part of the central collation decision (ERP-000M), and whichever collation that decision picks becomes the one comparison. The domain compares source kinds ignoring case, to match; for non-ASCII text the database is the authority.

**Not decided here.** Which source kinds are answered whole and which line by line belongs to the modules that own those sources (Purchasing, Logistics, Production). The store only holds each source to the grain its first movement chose.

**Hash change.** An amount written with trailing zeros hashes differently than it did under INV-2. No stored movement exists, because the host does not wire Inventory yet.

Notes carried from the #293 review (INFO; no change in this slice):

- **Audit actor type.** Every stored write records `AuditActorTypes.User` with the caller's actor. Integration writes (the Purchasing/Logistics outbox, T09/T14) will need the System actor type. That comes with the command surface, where the caller's metadata can carry it; it is not chosen here.
- **Replay identity.** A movement is identified by its id plus the canonical hash. The `IdempotencyKey` travels only in the audit envelope. A command surface has to bind its HTTP idempotency key to the movement id: the same key must give the same id.
- **Racing replay.** A replay that races its own first write gets `inventory_concurrent_change` (via `PK_Movements`); retried, it gets `Replayed`.
- **Unknown unique violations.** A 2601/2627 on an index the store does not name maps to `inventory_concurrent_change`: generic, and safe to retry.

## T07(a): read API (first user-path slice)

| Route | Permission (company-scoped) | Answer |
|---|---|---|
| `GET /api/v1/inventory/labels/{labelId}` | `inventory.labels.read` | `InventoryLabelViewV1`: identity, product, lot, heat/coil, required documents and status (Ö3), print/document history as sequence, time and kind, and (T07 lot record, below) `lot {id, heatNumber, coilNumber}`. Actor ids stay in audit. |
| `GET /api/v1/inventory/labels/{labelId}/balances` | `inventory.balances.read` | `InventoryLabelBalancesV1`: positions (owner, custodian, location, product, block) with exact kg text, and physical/free/blocked totals. |

- **Company.** Both routes need `X-Company-Id` and the actor's access to it. A label of another company is the same `404` as an unknown id; the tenant filter never shows it.
- **Separate permissions.** Seeing a label does not show its stock, and seeing stock does not show the label's facts.
- **Scope.** Reads are not filtered by site, so these permissions belong in `CompanyWideOnly` (central).
- **Evidence and outcome.** The evidence is real SQL through the real host with synthetic grants and synthetic stock written through the store. It is **not a user outcome**: no real product card and no accepted opening stock source exist yet (plan §14 T07, Ö1).
- **No write here.** The receipt (T07 write) waits for the opening-acceptance question on #66 (NEEDS-SINAN 5731613477). The lot record is the next section.

## T07 lot record: `MaterialLot`

Card: #66 5733503803 (ÜRÜN). Business accepted it as an independent core slice and set its boundary in #66 5734060341; that text is the contract of this section. It closes none of Q1–Q3 and chooses no policy.

| Rule | Where | Held by |
|---|---|---|
| A label belongs to a real lot record of its own company (DEC-PRD-003); a lot is one company and one product (the product by id only) | `Lots/MaterialLot`, `inventory.Lots` | domain + tenant filter + write guard |
| A label can never name another company's lot, and never exists without its lot | `FK_Labels_Lots_CompanyId_LotId`: (company, lot) → the lot's (company, id) alternate key | SQL (547), and the store first (`inventory_lot_not_found`) |
| A label and its lot are of the same product | `InventoryStore` (`inventory_lot_product_mismatch`) | store |
| A first arrival stores the lot, its labels and their audit events in one save, or nothing | `IInventoryLotStore.SaveLotAsync` | one `SaveChanges` (SQL transaction) |
| The lot id is the R3 identity: the same id with the same facts is a replay (labels given with it are saved as `SaveLabelAsync` would); other facts are `inventory_lot_conflict` | `InventoryStore.SaveLotAsync` | store + PK |
| Two first arrivals of one lot: one wins, the other stores nothing (`inventory_concurrent_change` via `PK_Lots`); retried, it is a replay | `PK_Lots` | SQL |
| A lot or label id another company holds collides on the key but is invisible here: `inventory_lot_conflict` / `inventory_label_conflict`, never `inventory_concurrent_change` — a retry would collide forever (#324 P3-1). The store re-reads the id SQL names as the duplicate key under the tenant filter; nothing of the other record is disclosed | `InventoryStore.SaveAsync` | store + PK |
| A label stored before the lot table, naming no lot, makes `AddMaterialLots` fail visibly (547 on `FK_Labels_Lots_CompanyId_LotId`); the migration's transaction takes the lot table back, the label is untouched, no lot is invented (#324 P3-2) | `AddMaterialLots` | SQL (real-SQL case) |
| Heat and coil are the lot's own real values; null = unknown, never empty or 0 (Ö3); a text longer than 64 is refused, never cut | `MaterialLot`, `CK_Lots_Trace` | domain + store + SQL |
| A lot is append-only: nothing changes its heat or coil afterwards, nothing deletes it | `InventoryDbContext` write guard | write guard |

Business #66 5734060341, held exactly (open until answered; fail-closed meanwhile):

- **Q1** — no equality or derivation between a label's heat/coil and its lot's: separate real values. The label view shows both, unrelated.
- **Q2** — the Ö3 document status (`PendingDocuments`) stays on the label; it is not moved to the lot.
- **Q3** — no endpoint or command changes a lot's heat or coil afterwards.
- **Not here:** origin (menşe) as free text (its dictionary is an open DATA item), fake heat/coil/MTC, quality release, a lot display number, cost; no new permission or grant, no `/lots` route, no UI, no production data. The label view's `lot` block is read under `inventory.labels.read`.

**Migration `AddMaterialLots`.** Creates `inventory.Lots` and adds the label foreign key. A stored label whose lot does not exist makes the migration fail when the key is added: the orphan is reported, never repaired with an invented lot. Labels are written only through the store and the SQL tests (there is no write route), so no deployed table is expected to hold one. `Down` drops the key, its index and the table and leaves labels untouched.

**Idempotency.** The store's R3 identity (lot id + facts) is what exists today. The ADR-011 command record (company, command, key hash; actor compared; first correlation; terminal replay; one exit for a race) comes with the HTTP receipt write, which waits for 5731613477; this slice has no HTTP write and no command table.

## T07 opening stock (INV-OPEN-1, part 1: domain + store + migration)

Decision A (#66 5738113623 §2): one narrow authorized user records a company's opening stock from a
physical count. There is no second approver; the recorder is the audited actor. The general stock-
adjustment and count-difference rules and their second approval are not relaxed.

| Rule | Where | Enforced by |
|---|---|---|
| The count document IS the source: every opening receipt's source is `("inventory.opening-count", countId, lineId)`; no receipt without a count line, and no line posted twice | `OpeningCount.ReceiptOf`, `FK_OpeningCountLines_Movements_CompanyId_LineId`, `IX_OpeningCountLines_CompanyId_LineId`, the per-source-line uniqueness | domain + store + SQL |
| Kept: company, location, counted-on date, the sheet reference. No personal name (the recorder is in the audit, ADR-009) | `OpeningCount`, `inventory.OpeningCounts` | domain + SQL |
| Quantity = counted kg exactly: Ö2 (at most 3 decimals, refused rather than cut) and the company's approved kg profile on the counted-on date (fail-closed) | `Kilograms.Positive`, `InventoryKilogramScale` | domain + store |
| Book = count at opening: each new piece's balance is exactly its counted kg (`StockLedger.Reconcile` is empty) | `InventoryStore.RecordAsync` | SQL |
| Opening is never an adjustment: a stored label is refused. With stock → `inventory_label_already_in_stock`; without → `inventory_label_conflict`. A book/count difference is ST-05 | `InventoryStore.RecordAsync` | store |
| Owner and custodian are the company itself; the location is the company's own (`FK_OpeningCounts_Locations_CompanyId_LocationId`); stock is free | `OpeningCount.PositionOf`, schema | domain + SQL |
| One save: the count, its lines, each lot (new or the same stored facts), each new label, one receipt and balance per piece, and the audit events commit together or not at all | `InventoryStore.RecordAsync` | SQL |
| R3: the count id is the identity; the same content is a replay, another content `inventory_opening_conflict` | `OpeningHash`, `OpeningCounts.ContentHash` | store |

## T07 opening stock (INV-OPEN-1, part 2: the command)

`InventoryOpeningCommandService` (`IInventoryOpeningCommandService`) records a count under ADR-011 over
the part-1 store. Every refusal writes no stock.

| Rule | Answer | Evidence |
|---|---|---|
| `inventory.opening-stock.record`, company-wide, measured fresh (#276) before any read and again after each owner read (the stored key, the location, each product). An unregistered name is a setup gap, never a grant | 403 `forbidden`, 503 `inventory_permission_unregistered` | SQL (no grant, revoked after the location read) |
| The location is Organization's ACTIVE internal location of the company, as it stands now (`IInventoryLocationStatusPort`, bound centrally to Organization's company-scoped module read, never a user-authorized read). Inactive = the location's own `!IsActive` (no site cascade); External = `IsExternal` (SINAN_KARARLARI_2026-08-31 §3). When several apply the first wins: not found → external → inactive | 422 `inventory_location_not_found` (unknown or another company's; terminal) / `_external` / `_inactive`; 503 `inventory_location_unavailable` | SQL (port double) |
| Each counted product may be stocked at the card version the counter read, on the counted-on date (`IInventoryProductStockPort` → Product's `IProductStockEligibilityV1`). Any refused line refuses the count | 422 `inventory_product_ineligible` with `lines[i].*` issues; 503 `inventory_product_eligibility_unavailable` | SQL (port double) |
| ADR-011: key = (company, `inventory.opening-count.record`, upper-case SHA-256 of the trimmed key) in `inventory.Commands`, written in the same save as the count. The same actor and payload replays with the first correlation (§5 fresh authority first); another actor or payload is a conflict that discloses nothing | 201 / replay (`IsReplay`, `OriginalCorrelationId`) / 409 `idempotency_conflict` | SQL |
| The same count under a new key: no second effect; the key answers the count's first recording — **200 replay** (`Idempotency-Replayed: true`, `Idempotency-Original-Correlation-Id` = the COUNT's first correlation, not this key's), and the new key records that answer | 200 replay | SQL |
| **Terminal or not (consumers such as LOG-4 / PUR copy this split).** Terminal, recorded and replayed for the key: `inventory_location_not_found` (422) and the store conflicts `inventory_opening_conflict`, `inventory_label_conflict`, `inventory_label_code_taken`, `inventory_lot_conflict`, `inventory_label_already_in_stock`, `inventory_duplicate_source`, `inventory_movement_id_conflict` (409). NOT terminal (a retry of the key may succeed once the owner's state changes): `inventory_location_inactive` / `_external`, `inventory_product_ineligible` (422), every 503, `inventory_concurrent_change`, and every 400 | — | SQL + unit |
| Authority is measured before each owner answer is used and right before the save; **nothing after the commit** (ORG #321 P3-1): a grant revoked after the commit never turns a stored count into a 403 | the stored answer | code (no test pins the absence) |
| §7 terminal: a conflict with what is stored (`inventory_opening_conflict`, `inventory_label_conflict`, `inventory_label_already_in_stock`, `inventory_label_code_taken`, `inventory_lot_conflict`, …) or a location not found is recorded alone and replayed | 409 / 422, replayed with the first correlation | SQL |
| §8: two requests with one key meet at the save; the loser stores nothing and answers the winner's record — the key is re-read after ANY refusal of the save, since the loser may collide on another index of the batch first | one count, one command row, both answer it | SQL (Barrier) |
| The problem codes are CLOSED per status (`InventoryOpeningCodesV1.Status400/409/422/503Codes`, `IssueCodes`); an unmapped store or domain code is an internal fault, never a guessed 4xx | exactly the listed codes | unit |
| A count id another company holds collides invisibly at `PK_OpeningCounts`; the id is read from that key's OWN error, as for lots and labels (#341, FİNANS INFO-3) | `inventory_opening_conflict`, no retry advice | SQL |
| Company B (no approved kg source): the Product port over the real eligibility chain passes Settings' `settings_provider_unavailable` through, never a field error; GDC's kg card is eligible (ORG #341 P3-1). The release is SYNTHETIC, in the disposable test DB only | 503 `inventory_product_eligibility_unavailable` | SQL |
| Forward fix: `AddInventoryCommands.Down` and `AddOpeningCounts.Down` refuse (THROW 51046) while commands, opening counts or count-sourced movements exist | the migration stays applied | SQL |
| Book = count at opening; a later book/count difference is ST-05 with its own second approval, never this command | — | SQL |

Not in this part (central, CARİ): the permission registration, `POST/GET /api/v1/inventory/opening-counts`
with the replay headers and problem mapping, OpenAPI, the DI of the service and the host adapters of the two
ports (Product stock eligibility, Organization location state), pins and the reviewed Sql entries of both Downs.
Not anywhere yet: the grant (human), the UI, and any real opening data (the positive product path needs the
family release, Q6).

## #333 follow-up: the duplicate key of its own error

A lot and its label can collide in ONE save (another company's lot and label ids in the same batch); the
combined SQL message then carries two GUIDs. The colliding id is read from the named key's OWN error in
`SqlException.Errors` (2601/2627, exactly one naming `'PK_Lots'` / `'PK_Labels'`, exactly one GUID in it);
anything else is null and the answer stays the retryable `inventory_concurrent_change` — never a made-up
conflict (`InventoryDuplicateKeyTests`; real SQL: company B offers A's lot and A's label in one call →
`inventory_lot_conflict`).

## dependency_wait

- **Central registration (CARİ, same branch, sequential):** the tenant guard's reviewed inventory (this DbContext's fingerprint, the design-time factory and the initializer's `Migrate`/`EnsureCreated` branch); the module-dependency allowlist edge `Inventory.Infrastructure → Audit.Infrastructure` (ADR-009); the `Api.IntegrationTests` project reference to Inventory; and the relational verifier's BROAD list and counts for `InventorySqlServerRelationalAcceptanceTests`.
- **INV-3 central follow-up (CARİ):** re-pin the tenant guard fingerprints of `InventoryDbContext.cs` and `InventoryStore.cs`, which changed here (no new raw SQL; the SqlClient use is unchanged); add the four new names of `InventorySqlServerRelationalAcceptanceTests` to the relational verifier's BROAD list and counts.
- **INV-3:** command/query surface, permissions (registered centrally, granted by a human), host wiring with the audit interceptor, and UI.
- **T07 lot record central follow-up (CARİ):** re-pin the tenant guard fingerprints of `InventoryDbContext.cs`, `InventoryStore.cs` (no new raw SQL, connection or command; the SqlClient use is unchanged) and `InventoryModuleExtensions.cs` (one more store registration; the provider branch is unchanged); add the five new `InventorySqlServerRelationalAcceptanceTests` names to the relational verifier's BROAD list and counts; regenerate the OpenAPI contract from CI (the label view's `lot` block) and the `OpenApiContractGenerationTests` schema expectations (`InventoryLabelViewV1.lot`, `InventoryLotV1`). No permission, `Program.cs` or allowlist change.
- **The first real product (Ö1's authorised source):** Ö1 fixes the first path as GDC band/coil kg→kg, but the real product, quality and size dictionaries come from its authorised card/label/document. Every identity in the tests is a synthetic fixture, never seeded anywhere.

```bash
dotnet test tests/GoymenERP.Inventory.UnitTests/GoymenERP.Inventory.UnitTests.csproj
# real SQL (needs GOYMEN_ERP_TEST_SQLSERVER_CONNECTION; the CI relational job runs it)
dotnet test tests/GoymenERP.Api.IntegrationTests/GoymenERP.Api.IntegrationTests.csproj --filter FullyQualifiedName~InventorySqlServerRelationalAcceptanceTests
```
