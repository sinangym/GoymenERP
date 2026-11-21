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

PR #422 added the enlisted runtime, durable Inventory outbox row and real-SQL atomicity/replay coverage. Central
DI/route/dispatcher publication remains deliberately off; the outbox row is not proof of publication. No new
permission or grant was introduced.

## INV-AL-1 — physical sales allocation lifecycle predecessor

`IInventoryPhysicalAllocationWriterV1` persists one physical-label allocation bound to the exact approved Sales
order/line/product/version facts supplied by the future host adapter. Creation deducts existing active allocation
remainders from the label's single unblocked physical balance and bumps the label version in the same save. Two
writers that observed the same label version cannot both over-allocate it. Creation and explicit full release are
idempotent, audited and versioned; neither writes a stock movement. Release preserves the row and its immutable
demand/source facts, sets the remaining kg to zero and makes that remainder free again.

An active allocation reserves its remaining kg against every other consumer (Master v3 §7.6 INV-003
`Serbest Fiziki = Fiziki Mevcut − Aktif Satış Tahsisleri − …`; DEC-STK-004; Business #66/5778472429: active
allocations are never assumed zero). The Manufacturing input issue
(`InventoryManufacturingRealizationParticipant`) therefore takes only `StockAllocation.Unreserved` of the exact
Free position (the position predicate creation deducts) and otherwise refuses with its existing
409 `inventory_manufacturing_physical_stock_insufficient`, writing nothing; a recorded issue still replays. Only an
explicit release frees reserved kg. The R13 parent-roll read is unchanged: its `UnblockedPhysicalKg` stays a block
bucket, not availability.

`ReservedUntilUtc` is optional input only. This predecessor neither invents the Settings default nor auto-releases
expired allocations. It supports only the locked `sales.order-line` demand kind; future purchase/production/manual
sources and source conversion are separate accepted slices. Rows created before this lifecycle remain readable with
null demand facts, so the LOG-5 runtime is not relabelled.

Host publication: the allocation routes are published under `inventory.physical-allocation.write` (create,
release, and the INV-AL-1 change below) with their OpenAPI operations. The host reads `ISalesOrderLineReferencePortV1`,
compares the approved line's company/product/version, then revalidates authority/company/actor immediately before
the single owner save.

### INV-AL-1 change — revise an existing allocation (2026-09-28)

Decision basis: Master v3 §25.4 ("Tahsisi görüntüle/revize et/serbest bırak" from the stock row / label), AT-063
("Tahsis Revize … doğru satır bağlamıyla açılır"), DEC-STK-003/004 (allocation is not a movement; atomic, idempotent,
never above free stock), DEC-STK-005 (no silent release), DEC-SAL-005 (a cancelled order is no approved demand) and
AGENTS "silme yerine ters kayıt / kopya hareket yok". A change is composed ONLY of the two already-decided allocation
operations (release + reservation) in one owner save; it chooses no new policy.

| Rule | Where |
|---|---|
| `POST /api/v1/inventory/physical-allocations/{id}/change` under the existing `inventory.physical-allocation.write` (no new permission); fresh company-wide authority before and after the Sales owner read | `InventoryPhysicalAllocationHostService.ChangeAsync` |
| The allocation's order line must STILL be approved (`ISalesOrderLineReferencePortV1`); a cancelled/unapproved line is 422 `inventory_allocation_invalid_reference` — the full release stays available. Product id/version come from the line, never the caller; the stored demand (order, line, product) must match | host + `InventoryStore.ChangeAsync` |
| **Resize** (target = own label): in place, same id (a loading-instruction line bound to it stays valid), consumed part kept (`Allocated − Remaining` unchanged); an increase must fit the position's unreserved free kg and pass the Sales ownership gate; same kg = 422 `inventory_allocation_change_unchanged`; to 0 kg = use release | `StockAllocation.Resize`, `RequireReservableIncreaseAsync` |
| **Move** (target = another label of the same product): the source is released with the change reason, a new allocation (client-supplied id) reserves the target's single company-owned-and-held Free position for the same order line — exactly the create rules | `ReservableTargetAsync` |
| Fences: expected allocation version (409 `inventory_allocation_version_conflict`), expected target label version (409 `inventory_label_version_conflict`), every touched label version is bumped (a pending stock count on it becomes `inventory_stock_count_stale`; a split/merge sees the new version), label RowVersion race = 409 `inventory_concurrent_change`; no stock movement | store + SQL |
| History: append-only `inventory.AllocationChanges` (kind 1 resize / 2 move, source version + kg → target kg, reason, actor, correlation); one change per source version (unique); `Down` refuses with 51053 while any exists; audit `inventory.physical-allocation.resized.v1` / `.moved.v1` (+ `.created.v1` for the move target), channel from the request (Web) | EF write guard + migration `AddInventoryAllocationChanges` |
| ADR-011: command `inventory.physical-allocation.change.v1`; the same key and content replays the stored answer (200 + `Idempotency-Replayed`), other content 409 `idempotency_conflict` | `FindChangeReplayAsync` |
| Read: `GET /api/v1/inventory/labels/{labelId}/physical-allocations` — the label's newest 50 sales allocations with release facts and change history, same authority; foreign/unknown label 404; a mixed-time read is 409 | `SqlInventoryPhysicalAllocationReader.ReadLabelAsync` |
| UI: ST-02 label detail "Satış tahsisleri" panel (shown with the server-advertised `inventory.physical-allocation.write` control, reached from ST-01's label link) | `LabelAllocationPanel.tsx` |

**Not decided (fail closed / parity with create, recorded — NEEDS-SINAN):**

- `INV-AL-CHANGE-LINE-QTY-01`: whether the total active allocation of a line may exceed the Sales line quantity. The
  create command does not cap it (the line quantity is in the order unit, not kg); a change keeps parity and does not
  invent a cap or a unit conversion.
- `INV-AL-CHANGE-INACTIVE-PRODUCT-01` (#98 item 11, in-flight documents): whether an allocation of an approved line
  whose product card was deactivated may still be changed or moved. Create does not read the product lifecycle either;
  no rule is invented here.
- `INV-AL-CHANGE-AUTHORITY-01`: whether revising needs a separate name or maker≠checker. Until decided, the existing
  write name (the one release and create use) applies; no new permission or grant.
- Changing `ReservedUntilUtc`, partial moves (split one allocation over two labels) and non-sales demand kinds are out
  of this slice.
- `INV-AL-CHANGE-CANCELLED-DECREASE-01` (#507 review P3, kept as is — a policy choice, not a hardening): a change
  on a line that is no longer approved (cancelled, or back in approval) is 422 for EVERY part, including a pure
  decrease that would only give stock back. Only the parts that ADD a reservation (an increase, a move's target) are
  covered by DEC-STK-003/004 and the create rules; whether a reservation may stay — even a smaller one — on demand
  that DEC-SAL-005 has cancelled is not decided (DEC-SAL-005 decides the cancellation's impact preview, not what an
  allocation of a cancelled line may still do). Allowing it would also change the host → writer contract (the product
  identity comes from the approved line today). The full release stays available and is the decided way to give the
  stock back.
- **Cross-module race (declared, not closed here):** the host reads the Sales line and then saves Inventory's rows; no
  transaction spans the two owners, so a cancellation or revision committed between that read and the save is not
  seen by this change. The same residual race is already declared on the Sales side — Sales README "Cancellation of an
  approved order (DEC-SAL-005, AT-007)" (next-module row) and "Quantity revision of an order (DEC-SAL-002, AT-023)"
  (downstream row) — and is not duplicated or changed by this slice.

## R13 — draft parent-roll reference predecessor (Business #66/5778472429 A)

`GoymenERP.Modules.Inventory.Application.IInventoryParentRollCandidatesV1.SearchAsync` accepts
`InventoryParentRollQueryV1(Q, Limit=25)` (label-code search, max64 characters, examined window1..100)
and returns `Items/SearchWindowFull`. `IInventoryParentRollValidationV1.ValidateAsync` accepts
`InventoryParentRollSelectionV1(LabelId, ExpectedLabelVersion)`. These public contracts remain context-free.
The separate in-process `IInventoryContextualParentRollReferencesV1` accepts only exact typed owner/custodian
kind and ID after the consumer has authorized its commercial context; party roles/accounts/versions and
location are not inputs. There is deliberately no PlannedKg:
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
supplying its exact stored owner/custodian context makes it draft-selectable without granting use or reserving it;
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

### ST-04 movement timeline on the label detail

`GET /api/v1/inventory/labels/{labelId}/detail` also returns `movementTimeline`: at most the
newest 100 stored physical movements that contain the requested label, ordered by occurrence time.
Each entry exposes only Inventory's immutable movement identity/kind/time, stable source identity
and that label's own direction, owner, custodian, location, block and exact kg lines. Cost and
source-module payloads are not disclosed. `windowFull=true` means older stored movements exist.

The existing `inventory.labels.read` plus `inventory.balances.read` company-wide fence is reused;
there is no new permission, route or grant. The detail service reads the bounded timeline twice,
with fresh authority checks after every awaited owner read, and refuses `409
inventory_label_detail_read_conflict` if the two captures differ. Unknown enum values, foreign
product facts, malformed block/release pairs or non-canonical quantities fail closed as `503`.
This is a read projection over existing `inventory.Movements`; it creates no movement and selects
no transfer, return, correction or quality policy.

This is the first ST-04 read slice, not the whole ST-04 card: the current ledger has Receipt,
Issue, Block, Release and Return records. A distinct transfer or reversal record is shown only
after its separately accepted Inventory writer and semantics exist; this reader does not infer
either event from unrelated rows.

The owner-side continuation declares `GET /api/v1/inventory/movements` as a bounded company
timeline. It pages the same immutable movement facts newest first with a protected cursor bound to
the company, actor and requested limit. The limit is 1–100 (default 50); each line includes the
stored label code and product id but no cost or source-module enrichment. SQL filters movement,
line and label reads by the resolved company. The same two company-wide permissions are measured
before and after the read. A missing owner reader has no positive fallback; an incoming or required
continuation fails closed when the cursor codec/key is unavailable. The Inventory module registers
the owner service; central host route, cursor-key binding and OpenAPI publication remain a separate
single-writer contribution.

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

## T15 manufacturing realization — customer-owned physical context

`InventoryManufacturingRealizationParticipant` chooses the input issue position from Inventory's single
free parent-label balance; Manufacturing does not send an owner, custodian or location for that issue.
The issue result returns the exact stored owner/custodian/location snapshot. An unused-input return posts
back to that complete issued position. An output may use its separately authorized active output location,
but its owner and custodian must exactly match the issued parent trace or Inventory refuses
`inventory_manufacturing_parent_label_context_conflict`. This permits customer-owned/fason material without
turning caller-supplied ownership into policy. Company filtering, expected label version, idempotent replay
and the shared transaction remain unchanged; no permission, grant, reservation or Manufacturing policy is
introduced here.

## T09 goods receipt immutable source and replay

`IInventoryGoodsReceiptSourceV1` reopens one committed Purchasing acceptance from Inventory's
company-filtered command, whole receipt movement, exact movement line, label and lot rows. It returns
the original physical `InventoryGoodsReceiptRequestV1` together with the stored
`InventoryGoodsReceiptResultV1`; a missing or foreign-company acceptance is the same `null`, while an
incomplete or internally inconsistent graph is `inventory_goods_receipt_unavailable` (503). The
ordinary read exposes no actor, idempotency key or payload hash and grants no authority: the Purchasing
consumer keeps its existing read permission and fresh company/actor fences around the awaited call.

For a retry, the already-enlisted `IInventoryGoodsReceiptParticipantV1.ReadReplayAsync` reads by the
current idempotency key before `StageAsync`. It returns `null` on the first attempt, requires the stored
actor and acceptance target on a committed key, and otherwise uses the existing
`inventory_goods_receipt_idempotency_conflict` (409). The host compares its current physical request
with `OriginalRequest` and still calls `StageAsync`; the participant's stored canonical payload hash is
the final same-key replay fence. No new endpoint, permission, grant, table or migration is introduced.

This source does not choose first-write receipt facts. In particular, the authorized server source for
`RequiredDocuments` remains a Purchasing/Business contract dependency; a browser array, empty default
or synthetic MTC fallback is not accepted as production input.

## ST-05 / AT-037 — stock count with difference reason and authority (DEC-STK-007)

Decision basis: DEC-STK-007 (KILITLI: a count difference needs a reason and authority; a second approval may
apply to a critical difference; history is never deleted; the reverse/correction chain is audited), AT-037 ("a
count difference is not final without a reason and an approval"), Master v3 §12.2 (the adjuster and the count
approver may be different people), DEC-SET-001 (no threshold in code; a missing policy stays fail-closed) and
#66/5738113623 §2 (the opening-stock exemption does not relax the count-difference second-approval rules).

| Rule | Where | Held by |
|---|---|---|
| A count names one label at the version the counter saw, with exactly one stored non-zero position (a label with free AND blocked parts is `inventory_stock_count_position_conflict`); book = that position's kg, difference = counted − book | `StockCount`, `InventoryStore.RecordStockCountAsync` | domain + store |
| **SAT-D330 (STK-S22): a label the system shows at 0 is countable.** With no stocked position, the count is taken at the zero balance row the label was last emptied at: its only stored row, or — when it was moved before — the one zero row named by the label's last movement (book = 0). A label never stocked (no balance row), a label a split/merge/manufacturing transformation consumed (its kg live on in its outputs; a surplus would count the same steel twice) and several candidate rows stay `inventory_stock_count_position_conflict`. An approved surplus puts the label back in stock through the same `CountAdjustment`. `CK_StockCounts_State` allows `BookKg >= 0` (`AllowInventoryZeroBookStockCounts`; its `Down` refuses (THROW 51748) while a zero-book count exists, forward fix) | `CountedPositionAsync`, `InventoryDbContext.StockCountStateCheck` | store + SQL |
| **SAT-D329:** a movement the ledger refuses (negative physical stock — an over-issue, a stopped transaction) leaves the label's kg and version untouched, so the label is never locked by it: the next issue, and a count at the unchanged version, go through | `StockLedger.Apply` (refuses before any write) | ledger + SQL test |
| A reason is mandatory (≤ 1000, trimmed); counted kg is canonical, ≥ 0, ≤ 3 decimals and on the company's approved kg profile at the count date | `ParseStockCount`, `InventoryKilogramScale` | store (400) |
| Recording changes no stock and bumps no label version; a zero difference closes at once (`NoDifference`) | `InventoryStore.StockCounts.cs` | store |
| The difference is final only after a decision under `inventory.stock-count.approve`; recording needs `inventory.stock-count.record`; both company-wide, measured fresh twice before the single owner transaction | `InventoryStockCountCommandService` | command (403/503) |
| **Fail-closed maker≠checker:** every non-zero difference is decided (approved or rejected) by another actor than its counter (`inventory_stock_count_self_approval`). The critical-difference threshold is undecided, so every difference is treated as critical | `DecideStockCountAsync` | store (409) |
| An approval posts exactly one `CountAdjustment` (kind 6) movement through the ledger — source `inventory.stock-count`/count id, one line In (surplus) or Out (shortage), free or blocked — and bumps the label version; only while the label is still at the counted version (`inventory_stock_count_stale`: count again) and never below the label's active allocations (`inventory_stock_count_allocation_conflict`) | `StockCount.Adjustment`, `StageMovementAsync` | domain + ledger + store |
| One decision per count: key (company, count); the same decision and note FROM ITS DECIDER is a replay; anything else `inventory_stock_count_already_decided` — the same content from another actor is not their replay: the counter gets `inventory_stock_count_self_approval`, anyone else `inventory_stock_count_already_decided` (#499 review P3; the UI also hides Approve/Reject on a count the viewer recorded in this browser session, the 409 stays authoritative). The same count id with other content is `inventory_stock_count_conflict`. ADR-011 key per command (`inventory.stock-count.record.v1` / `.decide.v1`) in `inventory.Commands` | `StockCountDecisions` PK, `FindDecisionReplayAsync` | SQL + store |
| Counts, decisions and the adjustment are append-only; `AddInventoryStockCounts.Down` refuses (THROW 51053) while any exists | write guard, migration | EF + SQL |
| Read: `GET …/labels/{labelId}/stock-counts` (newest 50, `windowFull`) behind the ST-02 fence (labels.read + balances.read, measured before and after); foreign = unknown = 404 | `InventoryStockCountQueryService` | service |
| After approval, ST-01, the ST-02 balance and timeline (`CountAdjustment`) and a new allocation read the counted kg | existing readers over `Balances` / `Movements` | SQL test |

**Not decided (recorded blockers, no invented value):**

- `INV-R21-CRITICAL-THRESHOLD-01` (relay INV-R20-R21-01 Q2, #66/5840984992, unanswered): which difference is
  "critical", and whether a smaller one may be self-approved (option B: kg/% threshold + owner). Until decided the
  store applies maker≠checker to every non-zero difference; option B would add a Settings policy key read at decision
  time (DEC-SET-001 snapshot), not a code constant.
- `INV-R21-REASON-LIST-01`: no closed reason list is decided (DEC-STK-007 names "sebep" only); the reason is free
  text. A closed list, if decided, is an additive `ReasonCode` beside the text.
- Out of this slice: a whole-location count sheet and transfer (R20, INV-R20-R21-01 Q1).
- **Cost of the adjustment (SAT-D331, SAT-D334; STK-S22 partial):** the pure rule is `StockCountCostCalculator`
  (domain). A surplus is valued at the location/product moving-average cost and leaves the average unchanged; a
  position at zero stock uses its last average (SAT-D296); an unreadable or never-recorded position leaves the cost
  "hesaplanamadı" while the surplus is still added — for every surplus, not only a label shown at 0. A shortage is
  valued at the current average; an unreadable or never-recorded average stops the deduction ("hesaplanamadı").
  SAT-D331 (5) makes this wait for the average-cost ledger to be wired to stock movements, which no slice has done
  yet: until then the approval posts its `CountAdjustment` with cost `NotCalculated` (as before) and does not apply the
  shortage stop, because every position would read "no source" and every shortage would be refused. Wiring the rule
  into `DecideStockCountAsync` (read port `IInventoryAverageCostReadPortV1`, base currency for `CostResult.Known`) is
  the follow-up of that ledger wiring.

**Central publication (coordinator):** register `inventory.stock-count.record` and `inventory.stock-count.approve`
(company-wide) in `ErpPermissions`; `builder.Services.AddInventoryStockCounts();` and `app.MapInventoryStockCountEndpoints();`
in the host; Inventory shell provider controls for both names under ST-01 and the authority re-pin; OpenAPI
regeneration; guard re-pins of `InventoryDbContext.cs` and the new migration; the three relational test names in
`tools/relational_inventory/inventory.txt`. Grants are the user's (disposable browser actors only, as R19).

## ST-02 — scrap write-off (hurda/fire çıkışı) and why stock return is not built

**Readiness (checked 2026-09-28 on main `27591d0d`).** No decision names an Inventory scrap write-off by itself:
DEC-STK-007 (KILITLI) says a stock difference needs a reason and an authority, a critical one may need a second
approval and history is never deleted; DEC-PRDCT-006/007 place scrap as a separate Manufacturing output class; no
AT covers an Inventory write-off. So this slice builds only what those decisions allow, on the AT-037 stock-count
pattern with its fail-closed rule applied to EVERY write-off, and records the rest as open questions.

| Rule | Where | Held by |
|---|---|---|
| A scrap names one stocked label at the version the recorder saw, with exactly one stored non-zero position (free or blocked; mixed or none is `inventory_stock_scrap_position_conflict`); book = that position's kg | `StockScrap`, `InventoryStore.RecordStockScrapAsync` | domain + store |
| The scrap is positive, ≤ 3 decimals, on the company's approved kg profile and never more than the book (`inventory_stock_scrap_exceeds_book`, and `CK_StockScraps_State` in SQL); a reason is mandatory (≤ 1000, trimmed) | `ParseStockScrap`, `InventoryKilogramScale`, `CK_StockScraps_State` | store + SQL |
| Only the company's own stock in its own custody is scrapped (owner and custodian = the transaction company); anything else is `inventory_stock_scrap_not_own_stock` (fail-closed) | `RecordStockScrapAsync` | store (409) |
| Recording changes no stock and bumps no label version; what would remain must still cover the label's active allocations (`inventory_stock_scrap_allocation_conflict`) | `InventoryStore.StockScraps.cs` | store |
| Stock leaves only after a decision under `inventory.stock-scrap.approve`; recording needs `inventory.stock-scrap.record`; both company-wide, measured fresh twice before the single owner transaction. The stock-count names grant nothing here | `InventoryStockScrapCommandService` | command (403/503) |
| **Fail-closed maker≠checker:** every scrap is decided (approved or rejected) by another actor than its recorder (`inventory_stock_scrap_self_approval`) | `DecideStockScrapAsync` | store (409) |
| An approval posts exactly one `Scrap` (kind 7) movement — source `inventory.stock-scrap`/scrap id, one Out line of the scrap kg, free OR blocked (scrapping blocked material needs no release, T13), cost `NotCalculated` — and bumps the label version; only while the label is still at the recorded version (`inventory_stock_scrap_stale`) and never below the active allocations | `StockScrap.WriteOff`, `StageMovementAsync` | domain + ledger + store |
| One decision per scrap: key (company, scrap); same decision and note FROM ITS DECIDER = replay, anything else `inventory_stock_scrap_already_decided` — the recorder sending the decider's content gets `inventory_stock_scrap_self_approval`, anyone else `inventory_stock_scrap_already_decided` (#512 review P3; the UI hides Approve/Reject on a scrap the viewer recorded in this browser session, the 409 stays authoritative); the same scrap id with other content is `inventory_stock_scrap_conflict`; ADR-011 key per command (`inventory.stock-scrap.record.v1` / `.decide.v1`) | `StockScrapDecisions` PK, `FindScrapDecisionReplayAsync` | SQL + store |
| Scraps, decisions and the movement are append-only; audit `Inventory.StockScrap` (record/approve/reject) + `Inventory.StockMovement`, channel from the caller (Web); `AddInventoryStockScraps.Down` refuses (THROW 51053) while any exists | write guard, migration | EF + SQL |
| Read: `GET …/labels/{labelId}/stock-scraps` (newest 50, `windowFull`) behind the ST-02 fence; foreign = unknown = 404. After approval ST-01, the ST-02 balance and timeline (`Scrap`) and a new allocation read what remains | `InventoryStockScrapQueryService`, existing readers | service + SQL test |

**Not decided (recorded, no invented value; NEEDS-SINAN candidates):**

- `INV-SCRAP-AUTHORITY-01`: who may approve a scrap, and whether a small scrap may be self-approved (a kg/% threshold
  would be a Settings policy, DEC-SET-001). Until decided every scrap needs a second actor.
- `INV-SCRAP-DISPOSITION-01`: whether scrapped kg leave stock (this write-off) or become a sellable scrap label/product
  (DEC-PRDCT-007: "hurda … ayrı kalite/üretim durumu; düzenli ticari satışta ayrı satış ürününe eşlenebilir"). If a
  scrap label is decided, it is an additive receipt of a new label linked to this scrap, not a change of this movement.
- `INV-SCRAP-REASON-LIST-01`: no closed reason list (free text), as `INV-R21-REASON-LIST-01`.
- Valuation of the written-off kg: OPEN-008 (deferred); the movement carries `NotCalculated`.
- `INV-SCRAP-OTHER-PARTY-01`: writing off a customer's goods (emanet) or our goods in a fason/external keeper's custody
  (DEC-STK-008: loss/damage there is its own event). Undecided, so refused fail-closed
  (`inventory_stock_scrap_not_own_stock`); only company-owned stock in the company's own custody is scrapped.

**Stock return (iade) is NOT built — blocked:** AT-028 ("İade kaynak sipariş, teslimat ve fatura bağını korur") is a
Sales/Logistics/Finance outcome whose source documents (return request, return delivery note, credit note) have no
owner contract yet; no decision fixes whether a returned piece re-enters as its original label (the manufacturing
`Return` kind only covers unused production input) or as a new label, under which owner, and whether it waits in a
quality hold (DEC-STK-006 names the block kinds, not a return rule). The separately accepted INV-R20 transfer
contract below does not decide return identity. Inventory will add a return receipt only when the owning module
publishes the source document.

**Central publication (coordinator):** register `inventory.stock-scrap.record` / `inventory.stock-scrap.approve`
(company-wide, not Critical) in `ErpPermissions`; `builder.Services.AddInventoryStockScraps();` in the host (the
routes are mapped by `MapInventoryEndpoints`); Inventory shell 1.5 (two ST-01 controls) and the InventoryV14 → V15
chain rows; OpenAPI regeneration (+3 operations); guard re-pins of `Program.cs`, `InventoryDbContext.cs`, the new
migration and `inventory.txt` (+5 facts); r14 disposable actors `r14-scrapper` / `r14-scrap-approver`.

## INV-R20 — same-company two-leg whole-label transfer

DEC-STK-008 and the accepted INV-R20 Q1 contract define plan → dispatch → in-transit → target acceptance. The
Inventory-owned writer stores planned and actual shipped kg, their signed difference, outgoing/incoming document
references and line-level acceptance. Dispatch removes every non-zero Free/Blocked bucket of a label in one atomic
movement; acceptance restores the same owner, product, label and block buckets at the validated target location and
custodian. Ownership and company never change.

A material label remains one physical piece. Partial acceptance means accepting a subset of dispatched whole-label
lines. This v1 contract exposes no partial kg for one label; the existing label split must happen first. Active
allocations block dispatch, every command uses exact transfer/label versions and idempotency/audit metadata, and
tenant filters plus composite foreign keys keep company scope fail closed. The owner registration publishes no
destination adapter, route or grant. The destination-owner port is checked at plan and again at acceptance, with a
fresh authority fence after each awaited owner read. The transactional writer repeats that captured company/actor
fence after persistence reads, before any replay disclosure and immediately before the single save; a refused final
fence clears every staged movement/transfer/audit row. Dispatch keeps the initially validated label/balance
concurrency tokens tracked through the save, so an intervening allocation or stock write fails closed instead of
moving stale stock; acceptance likewise keeps its validated label token through the target movement save. Owner-read
denials and provider errors are disclosed only after the captured company/actor/permission fence is measured again.
Central publication must register
`inventory.stock-transfer.write`, provide the destination-owner adapter, route/OpenAPI mapping and authority
classification before runtime activation.

### I01.1 — typed transfer-document proof boundary (contract only)

Before a later dispatch package may create its stock movement, Inventory must receive a current-effective proof from
the Logistics outgoing-document owner. The proof binds the context company, transfer, outgoing document, exact
document version and the complete ordered transfer-line identity set. Before a later target-acceptance package may
create its stock movement, Inventory must receive the distinct Inventory target-acceptance document proof bound to
the context company, transfer, exact transfer line, document and document version. The two ports are typed separately;
callers cannot select an owner with a free-form document kind.

Only `CurrentEffective` carries its exact binding. Missing, stale, draft, cancelled, reversed, foreign-company,
wrong-transfer/line and unavailable reads fail closed; binding mismatches share the non-disclosing `BindingMismatch`
outcome and carry no proof body. Proof reads create no stock, audit or outbox effect. The contract package registers no
producer, permission, route or host binding and changes neither the existing transfer DTO/runtime nor its whole-label
grain. A caller request cannot authorize itself: a positive proof is created only when the matcher compares it with a
separate owner-authoritative snapshot and every company/transfer/document/version and ordered-line or target-line
field matches exactly. A later runtime package must keep its existing company/actor/permission fence around the
awaited proof and must consume an owner implementation that reads that snapshot from its authoritative source.

I02.1 remains separate: the accepted partial-acceptance invariant does not by itself decide per-line quantity grain.
No quantity, migration or stock-effect wiring is introduced by this contract-only slice.

### B-22a — dispatch bound to the Logistics outgoing-document proof

The host now wires `AddInventoryTransfers()` with the destination adapter (`OrganizationInventoryTransferDestinationPort`:
an external location's custodian must be its operator party, an internal location's custodian the company itself —
DEC-ORG-004; an unreadable owner answers 503) and maps plan, dispatch and get-by-id (accept is B-22b).
`inventory.stock-transfer.write` is registered centrally; no grant is added. The dispatch request names the outgoing
document only by `outgoingDocumentId` and `expectedOutgoingDocumentVersion` (canonical positive integer text); the
stored kind is the fixed `logistics.transfer-outgoing-document`. Inside the permission fence the service replays the
key first, reads the transfer, then asks `IInventoryTransferOutgoingDocumentProofPortV1` with the transfer's complete
ordered line set (full-label v1). Only `CurrentEffective` moves stock; the store re-checks the proved binding against the
stored company, transfer, requested version and ordered lines and stores `DocumentVersion` on the dispatch row.

| Proof outcome | Answer | Recorded for the key |
|---|---|---|
| `Missing` | 422 `inventory_transfer_outgoing_document_not_found` | no |
| `BindingMismatch` | 422 `inventory_transfer_outgoing_document_binding_mismatch` | no |
| `Draft` | 409 `inventory_transfer_outgoing_document_not_final` | yes (terminal, audited) |
| `Cancelled` | 409 `inventory_transfer_outgoing_document_cancelled` | yes |
| `Reversed` | 409 `inventory_transfer_outgoing_document_reversed` | yes |
| `Stale` | 409 `inventory_transfer_outgoing_document_version_conflict` | yes |
| `Unavailable`, no proof port, port error | 503 (code-less) | no |

Migration `AddInventoryTransferDispatchDocumentVersion` adds nullable `TransferDispatches.DocumentVersion` with
`CK_TransferDispatches_DocumentVersion`; its `Down` refuses (THROW 51272) while a proved dispatch row exists (forward
fix). Not decided here and left fail-closed: reversing an accepted transfer, planned/actual difference or tolerance
resolution, and subcontract-order binding. Dispatch cancellation is B-22b below.

### B-22b — dispatch cancellation, "kantardan geçer" setting and departure moment (STK-S5)

Refs: KLN-D20, KLN-D25 (6)/(7), KLN-D27, LOJ-D10, LOJ-D11.

- **Command.** `POST /api/v1/inventory/transfers/{transferId}/cancel-dispatch` with `expectedTransferVersion`
  (canonical positive integer text), `cancelledAtUtc` (UTC, not before the dispatch) and `reason` (trimmed, 1-500,
  no control characters). Fenced by the service on the new `inventory.stock-transfer.cancel` (registered centrally,
  company-wide only; no grant, role or Critical is added). Same idempotency/replay/audit boundary as dispatch.
- **Effect.** Only a fully in-transit transfer (no accepted line) is cancelled. One kind 11
  `TransferDispatchCancellation` movement brings every dispatched bucket back to its exact source bucket (In), the
  labels return to the source position with a version bump, the transfer and every line become `DispatchCancelled`,
  and one `TransferDispatchCancellations` row stores the movement, the reason and the owner confirmation versions.
  The dispatch row and its kind 8 movement are never changed (append-only, KLN-D20: one reverse record, no copy).
- **Source warehouse setting (LOJ-D11).** Dispatch reads the Settings-owned value through
  `IInventoryTransferSourceScaleSettingPortV1` and stores it on `TransferDispatches.SourceScaleSettingAtDispatch`
  (1 Off, 2 On, 3 Unreadable). An absent port, failing read or undefined answer is stored as Unreadable, never as a
  chosen value. Dispatches recorded before this migration get Off (the column default; KLN-D27 (7)). Inventory does
  not own or choose the setting; the port is unregistered until the Settings owner ships it.
- **Window (pure rule `InventoryTransferCancelWindow`).** Stored and current settings plus a fresh owner reading of
  the e-İrsaliye link, GİB send, exit weighing and vehicle exit (`IInventoryTransferDispatchCancelGatePortV1.ReadAsync`)
  give Open, Closed (basis and moment) or Unreadable. Not linked and the more-closed setting Off: closed at dispatch.
  Not linked and On: open until exit weighing or vehicle exit, needs a Logistics "no exit and none later"
  confirmation. Linked: open until the GİB send, needs an e-İrsaliye cancel confirmation; if either setting is On or
  unreadable, the earliest of the three departure facts closes it and both confirmations are needed (KLN-D27).
- **Answers.** A closed window persists `TransferDispatchCancelClosures` once (first closure is final, never
  reopened, KLN-D25 (7)(g)/KLN-D27 (5)) and records the terminal 409 `inventory_transfer_cancel_window_closed` under
  the key. An unreadable reading is 409 `inventory_transfer_cancel_window_unreadable` and a missing required owner
  confirmation 409 `inventory_transfer_cancel_confirmation_missing`; both are not recorded (the key may retry). An
  open window asks `ConfirmAsync` only for the parts it needs and re-evaluates the confirmation-time facts before
  writing. No gate port, a failing read or a malformed answer is a code-less 503.
- **Migration.** `AddInventoryTransferDispatchCancellation` adds the column (default Off) with
  `CK_TransferDispatches_SourceScaleSetting`, both tables, widens `CK_Transfers_State` to status 5 and
  `CK_Movements_Kind` to 11. Its `Down` refuses (THROW 51823) while a kind 11 movement, a cancellation or closure row,
  a non-Off stored setting or a cancelled transfer exists (forward fix).
- **Not decided here (NEEDS-SINAN, fail-closed):** the cost golden of the reverse movement (transfers post no cost
  today), whether an unreadable reading should ever be persisted, whether dispatch should refuse an unreadable
  setting, and the disposition snapshot bucket of a cancellation (counted as other inbound kg today). The real
  Settings and Logistics/e-İrsaliye producers are those owners' integration work.

### STK-S7 — reverse transfer of an accepted transfer (KLN-D22)

- **Rule:** an accepted transfer is never cancelled. `POST /api/v1/inventory/transfers/{transferId}/reverse` plans a NEW
  transfer from the original destination (location and custodian) back to the original source, under
  `inventory.stock-transfer.write` (no new permission name). The reason (max 500, no control characters) and the link to
  the original transfer and to each accepted original line are mandatory; `inventory.TransferReversals` and
  `inventory.TransferReversalLines` hold that link. The reverse transfer moves stock only through the ordinary dispatch
  and acceptance commands; planning writes no balance, movement or average cost row.
- **Refusals:** original not PartiallyAccepted/Accepted/Closed → 409 `inventory_transfer_reverse_original_not_accepted`;
  an original line that was not accepted → 409 `_line_not_accepted`; a receipt excess still waiting for its decision →
  409 `_overage_pending`; a line already being brought back by a dispatched, not cancelled reverse transfer → 409
  `_line_already_reversed` (an undispatched reverse plan is inert and does not block); missing reason → 422
  `_reason_required`; the original source no longer an active destination → 422 `inventory_transfer_destination_*`.
- **Idempotency:** command row `inventory.transfer.reverse.v1`; the same key or the same reverse transfer id with the
  same payload replays the stored result, a different payload is 409 `inventory_transfer_idempotency_conflict`. Audit
  `Inventory.Transfer` (Reverse, `inventory.transfer.reverse-planned.v1`).
- **Read:** `GET /api/v1/inventory/transfers/{transferId}/reversal-facts` (balances read) answers the link this transfer
  reverses and the reverse transfers that point at it, with their status.
- **Migration:** `AddInventoryTransferReversals` creates the two link tables. Its `Down` refuses (THROW 51722) while any
  link row exists, then drops (forward fix).
- **Not covered:** returning a pending label by reverse transfer (KLN-D23 (2)(b)).

### STK-SC34-a — receipt follow-ups (KLN-D90, KLN-D91, KLN-D92, KLN-D97, KLN-D98)

- **Shortage cost (KLN-D90).** `AverageCostCalculator.ValueTransferReceiptShortage` values the KLN-D21 fire at the
  dispatch unit cost, never the moving average at receipt; an unknown or zero dispatch cost has no value, and the
  value is left unrounded for the Finance accounting-entry spec. Transfers post nothing to the cost ledger yet
  (KLN-D64 (2)) and the dispatch unit cost is not stored, so no movement path calls the rule; no accounting entry is
  written until ledger posting opens (account mapping is NEEDS-SPEC, Finance).
- **Multi-block label (KLN-D91).** A label dispatched in more than one block/lot bucket is received only with its full
  dispatched kilograms; any difference is 409 `inventory_transfer_receipt_multi_block_full_only` (replaces
  `inventory_transfer_receipt_split_undecided`), checked before the excess-reason rules, never split over buckets.
- **Personas (KLN-D92).** `inventory.stock-transfer.loss-record`: Depo Sorumlusu; `overage-approve` and `loss-approve`:
  Stok/Depo Müdürü and Genel Müdür. A declaration for the grant-time human gate, not a grant: no role, no user, no
  seed. Maker≠approver stays enforced by the service.
- **Renewed loss request (KLN-D97).** Loss requests and decisions are numbered per line (`RequestNumber`, from 1). A
  rejected request stays as it was; `RequestLoss` on the same line opens the next number (not before the rejection,
  maker≠approver and audit as before). An undecided request blocks a second one (409
  `inventory_transfer_loss_already_requested`; one undecided request per line is the reversible implementation reading
  of KLN-D97); an approved one is terminal. `DecideLoss` takes an optional `requestNumber` (absent = 1, so a stale
  decision never lands on a newer request; the hash includes it only when sent, so old keys still replay). A line is
  written off at most once (filtered unique index on `[Decision] = 1`). The overage side is not renewed here: a
  corrected overage request needs a permission and a quantity bound that are not decided (NEEDS-SINAN).
- **Cancel while a loss request is pending (KLN-D98).** `cancel-dispatch` answers 409
  `inventory_transfer_cancel_loss_request_pending` before any owner is asked, and the writer re-checks it in its own
  transaction; the cancel never withdraws the request. An approved write-off still answers
  `inventory_transfer_state_conflict`. A withdraw command does not exist yet (its permission is NEEDS-SINAN).
- **Migration.** `AddInventoryTransferLossRequestNumbers` adds `RequestNumber` (existing rows 1) to both loss tables,
  widens both keys, adds `CK_TransferLossRequests_RequestNumber` and the one-approval-per-line filtered unique index.
  Its `Down` refuses (THROW 51811) while a renewed request or its decision exists (forward fix).

### STK-SC36-b — overage re-request, loss withdrawal (KLN-D107, KLN-D108) and the first-receipt rate port (FNS-D77/D78)

- **Overage re-request (KLN-D107).** `POST .../lines/{lineId}/receive/overage-renewal` (`inventory.stock-transfer.write`)
  records a corrected re-request of a rejected receipt excess. The receipt's own excess is request 1; re-requests are
  numbered from 2 (`TransferOverageRenewals`, `CK_TransferOverageRenewals_RequestNumber`). It follows only a rejection of
  the line's latest request and never predates it (409 `inventory_transfer_state_conflict`); an undecided request
  answers 409 `inventory_transfer_overage_already_requested`; more than the declared excess is 422
  `inventory_transfer_overage_renewal_exceeds_declared`. `DecideOverage` takes an optional `requestNumber` (absent = 1)
  under `overage-approve`; neither the receiver nor the re-requester decides a re-request (409 self-approval). A line's
  excess is approved at most once (filtered unique index on `[Decision] = 1`). A re-request changes no stock and no
  transfer version; audit `inventory.transfer.overage-renewed.v1`.
- **Loss withdrawal (KLN-D108).** `POST .../lines/{lineId}/loss-withdrawal` (`inventory.stock-transfer.loss-record`)
  withdraws an undecided loss request by number (`TransferLossWithdrawals`). A decided request answers 409
  `inventory_transfer_already_decided`, an unknown one `inventory_transfer_state_conflict`. A withdrawn request is never
  decided (409 `inventory_transfer_loss_withdrawn`) and no longer blocks `cancel-dispatch`. Stock and version do not
  change; audit `inventory.transfer.loss-withdrawn.v1`. A new request after a withdrawal takes the line's next number like
  a KLN-D97 renewal (reversible implementation reading; whether that is wanted is NEEDS-SINAN).
- **First-receipt rate port (FNS-D77/D78).** `IInventoryFirstReceiptExchangeRateReadPortV1` answers, per label, the
  approved rate stored on the goods acceptance where the material first entered stock (split lineage followed to a single
  parent; merge/manufacturing is `first_receipt_lineage_ambiguous`). No goods receipt records a rate yet, so the port is
  dry: a goods-acceptance first entry answers `Unavailable` / `first_receipt_rate_not_recorded` and the caller shows
  "hesaplanamadı" (FNS-D83). It never falls back to an invoice, bulletin or current rate. Company-scoped, read-only.
- **Migration.** `AddInventoryTransferOverageRenewalsAndLossWithdrawals` adds `RequestNumber` (existing rows 1) to
  `TransferOverageDecisions`, widens its key, adds the one-approval filtered index and creates both tables. Its `Down`
  refuses (THROW 51836) while a re-request, a decision on one or a withdrawal exists (forward fix).

## Movement kinds and rollback signal (reference)

`StockMovementKind` (stored as `inventory.Movements.Kind`, `CK_Movements_Kind` = `[Kind] BETWEEN 1 AND 11`):

| Kind | Value | Written by | Lines |
|---|---|---|---|
| `Receipt` | 1 | goods receipt (T09), opening stock (INV-OPEN-1), manufacturing output (T15) | In |
| `Issue` | 2 | definite shipment (LOG-5), manufacturing input issue (T15); FREE stock only | Out |
| `Block` | 3 | Free → Blocked on the same position, physical total unchanged; only the store's ledger write, no user command yet | one Out + one In |
| `Release` | 4 | Blocked → Free on the same position, physical total unchanged; only the store's ledger write, no user command yet | one Out + one In |
| `Return` | 5 | unused manufacturing input back to the same label and position (`AddInventoryManufacturingReturn`) | In |
| `CountAdjustment` | 6 | an approved stock count difference (ST-05, `AddInventoryStockCounts`) | one In or Out |
| `Scrap` | 7 | an approved scrap write-off (ST-02, `AddInventoryStockScraps`); free or blocked | one Out |
| `TransferDispatch` | 8 | one INV-R20 transfer dispatch; every source bucket leaves stock atomically | Out |
| `TransferAccept` | 9 | one accepted INV-R20 line; the captured buckets enter the validated target | In |
| `ManufacturingOutputReversal` | 10 | the reversal of a manufacturing output receipt (F-009 D1, `AddInventoryManufacturingOutputReversal`); free unallocated kg at the output position only, no cost | one Out |
| `TransferDispatchCancellation` | 11 | the cancellation of one in-transit INV-R20 dispatch (B-22b, `AddInventoryTransferDispatchCancellation`); every dispatched bucket back to its exact source bucket | In |

A new kind widens `CK_Movements_Kind` in its own migration and its `Down` narrows it back only when no row of that kind
exists. An allocation change, a split/merge and a label version bump write NO movement.

**`THROW 51053` is a shared rollback signal, not an Inventory-owned number.** Every Inventory migration that must not
be rolled back once its facts exist (`AddInventoryLabelGenealogy`, `AddInventoryManufacturingReturn`,
`AddInventoryStockCounts`, `AddInventoryAllocationChanges`, `AddInventoryStockScraps`, `AddInventoryTransfers`) raises 51053 in `Down`, and
Finance's `AddManualBankPayments` uses the same number for the same meaning ("facts exist; forward fix"). It is kept
as is — existing migrations are never renumbered — and the message text names the owner and the facts.

## Manufacturing output genealogy and participant V2 (B-03, API-22/27)

Refs: DEC-PRDCT-003, DEC-PRDCT-007, URT-D8, URT-D14.

- **Genealogy (V1 and V2 output):** staging a Manufacturing output also records one `LabelTransformation` of kind
  `Manufacturing` (3). It links the output label to every input label of the same `ManufacturingOrderId` whose net
  issued kilograms (issued minus returned) are above zero, with the raw net kg on each input edge. The transformation
  id is a name-based UUID over company and output id, so a replay returns the stored edges and never re-derives them.
  An order with no net issued input refuses the output (`409 inventory_manufacturing_parent_input_not_issued`), so a
  Manufacturing transformation always has at least one input. Split/merge still refuse kind 3, and the label detail
  read returns kind `Manufacturing` without the split/merge kg balance check (scrap and loss make the totals differ).
- **Participant V2:** `IInventoryManufacturingRealizationParticipantV2` keeps the V1 issue/return shapes. Its output
  maps the output class before any read or write: `GoodProduct` -> `Standard`, `SecondQuality` -> `SecondQuality`,
  `Scrap` -> `Scrap` (URT-D43). `ProcessLoss` and any other token are refused with
  `400 inventory_manufacturing_output_class_not_stockable`. The label is created classified, and the result echoes
  both the output class and the classification. V1 stays unchanged until the Manufacturing consumer moves to V2 in its
  own slice.
- **Usable remnant (STK-S9, BS-5/BS-14):** `UsableRemnant` is stockable. Its classification is the operator's choice
  sent as `RemnantClassification` (`Standard` or `SecondQuality`; null means the production default `SecondQuality`).
  `Scrap`, an undefined value, or a `RemnantClassification` on any other output class is refused with
  `400 inventory_manufacturing_remnant_classification_invalid` before any read or write. The label also carries the
  separate production status `UsableRemnant` (`inventory.Labels.ProductionStatus` = 1, null for every other label);
  the result echoes it as `ProductionStatus`. It is descriptive only (no movement, block, disposal or sellability
  effect; sellability stays with DEC-PRDCT-007), written once at label creation and not in the label's modifiable
  columns, so a later manual reclassification keeps it. Split/merge outputs inherit it by KLN-D46 (in force per KLN-D63
  and KLN-D86, which voids the SC21 "inheritance stays open" wording): every split child of a remnant label is
  `UsableRemnant`; a merge result is `UsableRemnant` only when every source is, and null for mixed sources
  (`InventoryLabelTransformation.InheritedProductionStatus`, read from the stored sources). A non-remnant output
  hashes its idempotency payload exactly as before; a remnant output also binds the effective classification. The
  field is not on the HTTP label reads yet.
- **Migration:** `WidenInventoryLabelTransformationKindToManufacturing` widens `CK_LabelTransformations_Kind` to
  `IN (1, 2, 3)`. Its `Down` refuses (THROW 51262) while any kind 3 row exists (forward fix).
  `AddInventoryLabelProductionStatus` adds nullable `Labels.ProductionStatus` with `CK_Labels_ProductionStatus`
  (`IS NULL OR IN (1)`); its `Down` refuses (THROW 51291) while any label carries a production status (forward fix).

## Manufacturing output reversal, label disposition and Free kg ports (F-009 D1/D5, STK-S20)

Refs: `docs/delivery/URETIM_EMRI_DUZELTME_ANALIZ_v1.md` §5 D1 and §6; URT-D28, URT-D31 (correction is a new linked
movement, never an edit of the old one), URT-D34 (Free kg read before allocation).

- **Reversal participant:** `InventoryManufacturingRealizationParticipant` also implements
  `IInventoryManufacturingOutputReversalParticipantV1.StageOutputReversalAsync`, enlisted in Manufacturing's open
  transaction like the issue/output/return stages. It writes exactly one `ManufacturingOutputReversal` (10) movement:
  one Out line at the output receipt's original position, Free block, source kind `manufacturing.output-reversal`,
  source id = order, source line = Manufacturing's reversal fact id. The old `Receipt` stays unchanged; net output =
  receipt − reversals. Command name `inventory.manufacturing-output-reversal.stage.v1`; idempotency replay, payload
  conflict, label expected version (bumped by the movement) and the single audit/command save follow the participant's
  existing skeleton.
- **Decision source inside the transaction:** the reversal re-reads the label's disposition with the same pure
  computation the read port shows (`InventoryLabelDispositionReader`), under the label version fence. Stored figures
  that do not reconcile are refused 503 `Unavailable` (fail-closed). A request for another order/output than the
  label's output origin is 404 `InvalidReference`.
- **Refusals (all 409, `InventoryManufacturingOutputReversalCodesV1`):** `exceeds_produced` (earlier reversals +
  this one > received kg) first; then, when the kg are not free and unallocated at the output position, the first
  reason in this fixed order: `shipped` (net of customer return), `issued_to_manufacturing` (net of manufacturing
  return), `transferred`, `allocated`, `blocked`, otherwise `not_free` (scrap, count, …). Every reason blocks alike; the
  order is a reversible presentation choice. Releasing the hold is the owning module's own command. The V1 realization
  code set is unchanged; the reversal-only codes live only in the reversal contract.
- **No average-cost impact:** a manufacturing receipt is recorded without cost (`CostResult.NotCalculated`) and never
  enters the average while its order is open (§4.8), so the reversal is also `NotCalculated` and touches no
  `AverageCostLedger` row.
- **`IInventoryLabelDispositionReadPortV1`:** `Get(CompanyId, LabelId)` → Value / NotFound / Unreadable. Value carries
  the label version, `InStock`/`OutOfStock`, the output origin (order, output, receipt movement, produced, reversed,
  free-unallocated and blocked kg at the output position; null for a non-output label) and gross kg per business reason
  (shipped, customer-returned, manufacturing issued/returned, transfer dispatched/accepted, scrapped, count in/out,
  other in/out), physical, blocked and allocated kg. A company other than the resolved one reads as NotFound. It is
  never the decision source (TOCTOU) and carries no authority.
- **`IInventoryFreeQuantityReadPortV1`:** `Get(CompanyId, LabelId)` → `FreeKg` (Free balance minus active allocations,
  summed over Free positions), `FreePositionCount` and the label version. Same status, company and side-effect rules.
- **`OrderCancelled = 3`:** `InventoryManufacturingPlanReleaseReasonV1` gains `OrderCancelled` (wire code
  `manufacturing.order-cancelled`) for Manufacturing's cancel path (URT-S4).
- **Migration:** `AddInventoryManufacturingOutputReversal` widens `CK_Movements_Kind` to `BETWEEN 1 AND 10`. Its `Down`
  refuses (THROW 51420) while any kind 10 movement exists, then narrows the check back (forward fix).
- **Not here:** no permission, grant or role (the participant and the ports carry no authority; Manufacturing fences its
  own command), no HTTP endpoint and no OpenAPI change. The frontend movement-kind decoders do not know kind 10 yet.

## Average purchase cost ledger and read port V1 (SAT-D262..D267, slice 1)

Decision: `docs/decisions/SINAN_KARARLARI_2026-11-10_SATIS_EK52.md`. Inventory owns the moving-average purchase cost
(key CompanyId + location + product, internal scale 4, `MidpointRounding.AwayFromZero`) and hands it out only through
`IInventoryAverageCostReadPortV1` (Application; Value / Unreadable / NoSource). Sales never reads this table.

- **Core:** `Domain/Costing/AverageCostCalculator` is deterministic. A purchase receipt recomputes the average; a transfer
  enters the target at the source average; a production receipt never enters the average (the position becomes
  Unreadable `mixed_source_undecided`). Negative stock, unit conversion, foreign currency, returns and stock the ledger
  never costed have no figure: each answers Unreadable with its own code. Unreadable is sticky (leaving it would be a
  policy choice).
- **Ledger:** `inventory.AverageCostLedger` (`AddInventoryAverageCostLedger`), company-filtered, unique per position,
  `Version` + `RowVersion` concurrency, state check `CK_AverageCostLedger_State`. `Down` refuses (THROW 51250) while any
  row exists (forward fix).
- **Sinan 2026-11-11 (SAT-D296/D297, `docs/decisions/SINAN_KARARLARI_2026-11-11_SATIS_EK59_ORTALAMA_MALIYET.md`):** at
  zero stock the last average stays readable (Value). A purchase receipt with a zero unit cost is "cost unknown": the
  position becomes sticky Unreadable `zero_cost_receipt` (reason 9) and the zero never enters the average. Migration
  `RelaxInventoryAverageCostReasonToZeroCostReceipt` widens the reason check to 1..9; its `Down` refuses (THROW 51258)
  while a reason 9 row exists. Purchase-line price input, USD/TL and unit conversion stay open (NEEDS-SINAN, Issue #66).
- **Sinan 2026-11-11 (SAT-D302, `docs/decisions/SINAN_KARARLARI_2026-11-11_STOK_MALIYET_VE_TRANSFER.md`):**
  additional cost (freight, customs) never enters the v1 average. The receipt entering at its own purchase price, with
  `AverageCostMovement.AdditionalCostPerStockKg` kept apart, is the implementation reading of that decision (reversible);
  reason 4 / `additional_cost_undecided` is no longer produced, its number and wire code stay reserved for the later
  setting (ALS-D80). No migration: the reason check and the read contract V1 are unchanged. The domain field does not
  decide the Purchasing→Inventory contract: per the ALS-D80 implementation reading, that cross-module K3 slice decides
  how additional cost is carried (a separate field, a flag or nothing), and so that SAT-D302 (1) holds it must make sure
  `UnitCostPerStockKg` is the purchase price alone.
- **Sinan 2026-11-13 (SAT-D318, SAT-D323, `docs/decisions/SINAN_KARARLARI_2026-11-13_URETIM_MALIYETI_VE_EKSI_STOK.md`):**
  stock never goes below zero, so `negative_stock` (reason 1) marks an invariant breach; SAT-D301 (2) ("giriş gelince
  düzelir") is superseded by SAT-D323 (1) and no recovery formula is written. Keeping the breach fail-closed and sticky
  is the decision file's author reading; the safety behaviour for a violated record is NEEDS-SINAN.
- **No price input source is connected yet** (Purchasing price input is NEEDS-SINAN, Issue #66 comment 6760990097), so
  nothing writes the ledger in this slice and the port answers NoSource. No permission or grant is added; the port carries
  no authority (the Sales caller fences around it).

## Future supply allocation (F-017 F017-b, STOK_F017 §3.2/§4, KLN-D26)

- **Tables:** `inventory.FutureSupplyAllocations` (one allocation of a Sales order line's demand onto an approved purchase
  order line; Active 1 / Consumed 2 / Released 3; quantities `decimal(18,3)`, refused never rounded beyond three decimals)
  and `inventory.FutureSupplyLines` (the per purchase line ledger: `ActiveAllocated <= AllocatableSnapshot`, the snapshot
  being the allocatable amount read under the lock). Checks: `CK_FutureSupplyAllocations_Identity/_Kinds/_Quantity/_Release`
  and `CK_FutureSupplyLines_Ledger`; the allocation's FK to its ledger row is `Restrict`. Both tables carry `CompanyId` and
  the company query filter.
- **Writer (F017-b2 / STK-S17):** replay → transaction → Sales line lock, then
  purchase line lock → fence → demand and supply reads → product match → ledger reserve (insufficient 409, other rule
  422) → demand cap → allocation + command attempt + audit + outbox in one save. The Sales line lock
  (`goymen:sales-order-line:{company:N}:{line:N}`, `sp_getapplock` Exclusive/Transaction) is a host adapter until Sales
  owns it.
- **User API:** POST `/api/v1/inventory/future-allocations` creates; POST `/{allocationId}/release` explicitly releases.
  Both require `inventory.future-allocation.write`; GET
  `/api/v1/inventory/sales-orders/{salesOrderId}/lines/{salesOrderLineId}/future-allocations` requires
  `inventory.future-allocation.read` and returns a bounded 100-row window. The two names are registered only,
  company-wide, absent from Critical and shipped grants. The module derives actor and roles from fresh authority,
  fences the write under the locks, and revalidates successful and refused replays. Future allocations remain separate
  from physical stock. F017-c, purchase cancellation protection and Sales reduction are an integration prerequisite;
  the draft train does not authorize a production activation.
- **Ports:** `IInventoryFutureAllocationReadPortV1` (active allocated sum in the purchase unit; Unreadable when the ledger
  disagrees with Σ active remaining, never the ledger's figure) and `IInventoryFutureAllocationReleasePortV1` (a session
  on the caller's open connection and transaction; the caller commits; no Inventory idempotency row). Defaults are
  fail-closed until the host replaces them (identity-only unit conversion, purchase source unavailable).
- **Migration:** `AddInventoryFutureSupplyAllocations` creates both tables. Its `Down` refuses (THROW 51460) while any
  allocation or ledger row exists, then drops (forward fix).
- **Open (NEEDS-SINAN):** the demand open quantity definition (ordered − shipped, cancelled → 0), unit conversion beyond
  the identity, and the release reason code list are the author's reading or format-only; no grant is added.

## Future supply conversion at goods receipt (F-017 F017-c, STOK_F017 §3.5, ALS-D84/D89)

- **Order (ALS-D84):** the goods receipt host stages the purchase acceptance, then the Inventory label
  (`IInventoryGoodsReceiptParticipantV1`, unchanged), then enlists `IInventoryGoodsReceiptFutureSupplyPortV1` on the same
  connection and transaction and converts. The purchase line lock is the receipt's own lock (re-entrant, same
  transaction); `IPurchaseGoodsReceiptTransactionV1` and the participant contract do not change. No route, OpenAPI entry or
  permission is added; only the receipt host calls the port after its own permission check.
- **Consumption order:** the host's optional `AllocationOrder` (F17-3) first, then the manual sequence, the Sales line due
  date, the creation time and the id (`InventoryFutureSupplyConsumptionOrderV1`). A repeated or unknown id refuses 422
  (`inventory_future_allocation_order_invalid`) and the receipt rolls back.
- **Converted share:** the allocation is consumed by the share (Active → Consumed when nothing remains), the ledger
  returns the share, a physical `sales.order-line` allocation is created on the received label's Free position and
  `inventory.FutureSupplyConversions` links the two (Outcome 1). Audit `Inventory.FutureSupplyAllocation` (Convert) and
  `Inventory.StockAllocation` (Create); outbox `inventory.future-allocation.converted.v1`. A share that takes the whole
  accepted quantity becomes the label's measured kilograms; a partial share needs the central kg conversion.
- **Link failure (§3.5):** label not Free, owner not the company, unit conversion unavailable, Sales line not approved,
  product mismatch or not enough unheld Free kg → the whole remainder is released with `acceptance-link-failed` (reason
  text names the link), released event + audit, link row Outcome 2. The receipt and the label stay.
- **Idempotency:** one `inventory.future-allocation.convert-on-receipt.v1` command row keyed by the receipt's key; a
  replay receipt answers the stored result and writes nothing. A receipt line with no active allocation writes nothing.
- **Migration:** `AddInventoryFutureSupplyConversions` creates the link table (`CK_FutureSupplyConversions_Identity`,
  `_Outcome`; unique per acceptance + future allocation and per physical allocation). Its `Down` refuses (THROW 51711)
  while any link row exists, then drops (forward fix).
- **Open (NEEDS-SINAN):** releasing the whole remainder on a link failure, treating not enough unheld kg as a link failure,
  and the partial non-kg share without a central conversion source (released) are the author's reading.

## Receipt reversal history (STK-S19, F017-e, ALS-D86/D91/D92, 7B)

- `IInventoryGoodsReceiptReversalParticipantV1` borrows the host transaction. Purchasing is flushed first; its post-write source must use that same connection and transaction. The host owns Sales-line locks in ascending order, then the full purchase-line union, fresh subset validation, authority and commit/rollback.
- Original receipt and conversion rows remain immutable. A separate reversal ledger records exact original conversion shares, reopened amounts, overflow released with `acceptance-reversed-no-supply`, and unheld amounts. Prior released shares never return; closed Sales demand or an inactive physical hold does not reopen a future allocation. Physical and purchase-unit quantities remain separate facts.
- `AddInventoryGoodsReceiptReversals` adds two company-scoped history tables, same-company original links, and movement kind 16 for a pure acceptance undo. Down first refuses with **THROW 51950** while either history table or a kind-16 movement exists; empty storage rolls back freely (forward fix).
- Evidence: unit/model and real-SQL fact sources are present. Runtime tests are **NOT_RUN**; the complete Purchasing undo/correction and supplier-return host chain remains required before this slice can be called usable.

## dependency_wait

## Immutable movement cost and Manufacturing generations (SC36 / URT-D67 / URT-D79)

- `AddInventoryMovementCostProvenance` is EF-generated. Its Down first refuses with **THROW 51959** while any new stamp, independent-currency ledger, pending entry, recognition or generation/history row exists (forward fix). It never backfills old movement prices.
- New movement stamps preserve the actual event-time TL/USD and original currency proof. Canonical invariant decimal text plus strict SQL checks preserve all System.Decimal digits without database rounding; public owner fields remain decimals. Moving averages still use the existing 4-decimal calculator.
- A borrowed-transaction `InventoryManufacturingCostGenerationParticipant` records Pending/Unreadable without a new physical-close prerequisite; actual recognition preserves authoritative label amounts and the exact before/after average-pool effect. Reopening reverses that actual immutable effect. Average rounding differences are provenance, never an automatic accounting posting.
- Missing balances are not assumed sold: only actual net definite shipment proves the consumed SMM proposal. Unresolved transit/downstream lineage remains explicitly unreadable. USD is production display only. Accounting proposal acceptance, open-period/date selection, different approver and dispatcher activation remain separate owner/human gates.
- Sources include model/unit and real-SQL rollback, replay, precision/reversal and Down-guard facts. Final Inventory and API integration test-project compilation is exit 0; runtime tests/SQL/browser/CI are **NOT_RUN**. Committed outbox source and all physical-return cost propagation are still pending.

## Existing central dependencies

- `AddInventoryExactCostPools` preserves the private priced mass as canonical exact ratios, with the existing average at 4dp. Recognition keeps nullable 3dp projections plus exact ratio provenance; no physical kg is rounded into a new quantity. Down first refuses with **THROW 51968** while a private pool or exact recognition mass exists (forward fix). Old movement stamps remain unchanged. Split/merge, partial issues, original partial returns and late Manufacturing recognition have source tests; runtime is **NOT_RUN**.

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

## Immutable first-entry FX (STK-SC36-b, FNS-D103/D104)

- `AddInventoryFirstEntryExchangeRateProof` is EF-generated (`20261117010119`). Nullable receipt-time snapshots and exact issue-rate ratios are append-only with the original movement cost stamp. Its first Down operation refuses with **THROW 51972** while either new proof exists (forward fix); no old movement, price or FX is backfilled.
- The actual Purchasing proof borrows the receipt transaction. Its approved order rate wins; only an explicitly unspecified rate permits the actual entry-business-day TCMB selling answer. An unreadable order owner never becomes a day-rate fallback. Functional currency is a distinct no-conversion fact, never a fabricated rate of one. This first-entry snapshot does not replace the official supplier-invoice FX used by the TL stock-cost pool.
- The owner reader follows bounded immutable split/merge lineage. Merges preserve exact kg-weighted rates, actual receipt components and material shares. Non-terminating quotients retain canonical ratios; no intermediate rate is rounded. A missing input rate or different directed currency pair stays unreadable. Manufacturing remains a new-material boundary. Cached subtrees retain their height, so graph limits apply on every path.
- Source includes host/codec/calculator/model and seven real-SQL facts for original capture, invoice-FX separation, merge/split/diamond provenance, missing/pair refusal, rollback, depth and Down guards. Runtime unit/SQL/browser/CI evidence is **NOT_RUN**. Manufacturing order/non-order priority still needs the actual first-entry port bound at its owning consumer; a registered owner reader alone is not a complete user flow.


## Negative physical stock attempts (STK-S21 / SAT-D328,D336..339)

- `AddInventoryNegativeStockAttempts` (`20261117091723`, Down guard **51986**) adds append-only genuine outbound refusal history and immutable corrective-count resolutions. It never writes the refused movement or permanently locks a label. Existing stock-report permissions expose the company-filtered bounded history screen; no new permission, grant, retention deletion or Work task is activated.
- `AddInventoryNegativeStockCountBoundaries` (`20261117095345`, Down guard **51988**) adds the transaction-owned Inventory validation mutex and immutable product/location approved-count boundary proof, plus nullable legacy observations. Physical validation, count approval and fresh post-rollback recording use one company mutex after any host Sales then Purchasing locks. A delayed attempt is resolved only by a real matching later approved count, never by time or current-stock inference. The borrowed guard context owns neither connection nor transaction and flushes only its own technical row. Both Down guards precede every destructive operation and require a reviewed forward fix while protected history/proof exists; neither migration backfills old facts.
- Same-key company/actor/payload binding, independent attempt+Audit atomicity, count-wide resolution, count/rollback races, stale reused contexts and borrowed transaction behavior have unit/contract/real-SQL test sources. Runtime .NET/SQL/Vitest/browser/CI is **NOT_RUN**. The coordinator owns generated contracts, recovery/release and relational/group inventories; context construction and the company-wide lock are explicit final K3 review surfaces.

## Original shipment TL cost evidence (STK-SC36-a dependency)

- `IInventoryShipmentCostReadPortV1` reads one company/shipment/movement through its genuine Issue, original command result and definite-shipment outbox envelope. It returns the immutable TL cost stamp and factual line/label/product/loading-instruction identities. It does not read today's average, invoice price, current Product accounting class or USD to reconstruct historical TL.
- An `Available` factual read can carry an explicitly `Unavailable` legacy TL stamp. Consumers must validate their own fresh user authority, actual Logistics document binding and exact TL/accounting admission. Rational evidence is preserved when a finite scalar/amount cannot represent it; no rounding or financial effect is introduced by this read.
- The actual provider is module-registered. No HTTP endpoint, permission, schema change, proposal producer or SMM trigger is added. Source tests include original-stamp immutability after a later receipt, missing TL despite an actual USD stamp, tenant isolation and corrupt/unknown envelope refusal. Runtime unit/SQL/browser/CI remains **NOT_RUN**; the consuming financial workflow and pending trigger decision remain separate.

## Transfer planning and target receipt documents (STK-S8, B-22, KLN-D23/D24/D107/D108)

- Planning uses actual authorized label versions and actual active Organization destinations, including the external operator identity where applicable. Command-purpose transfer candidates and reads require only the existing permission for dispatch, receipt, overage decision, loss request or loss decision; they do not infer a stock-report grant. Both candidate and document continuation cursors are signed and bound to company, actor and purpose.
- `AddInventoryTransferReceiptDocuments` (`20261117122205`) is EF-generated. It adds company-scoped append-only draft and effective receipt-document versions with same-company transfer and predecessor foreign keys. Its first Down operation refuses with **THROW 51999** while any document history exists, before dropping the table. There is no price, stock, old-document or permission backfill.
- Draft creation and explicit finalization keep the expected transfer version, genuine lines, date, kilograms and overage reasons. The physical receipt validates that exact effective document version and line in its own transaction. The document write and its Audit owner record are atomic; the original actor/company/payload-bound replay remains immutable. Null optional document-version fields are omitted from old request hashes and JSON.
- The actual frontend covers planning, explicit draft/finalization, receipt, overage decision/renewal and loss request/decision/withdrawal. Unknown outcomes retain the original frozen intent and key. Read-only stock lists are independently capability-gated, so command-only authority does not require a report grant. Generic record references are not accepted as owner proof.
- Source compilation is exit 0 for API, Inventory/Counterparty/Organization UnitTests and Api.IntegrationTests; runtime unit/SQL/Vitest/browser/CI and database update are **NOT_RUN**. Genuine linked GIB send evidence is still unavailable and its path remains 503. That missing owner and runtime acceptance keep STK-S8 **PARTIAL**; source and compilation alone do not establish an operational transfer workflow.

STK-S3 source checkpoint: EF-generated `20261117140912_AddInventorySubcontractDeliverySources` adds immutable company-scoped customer-delivery allocation evidence and a distinct subcontract demand kind. Down first refuses with THROW 51934 before evidence loss or constraint narrowing. Borrowed reserve/ship participant source is present; real Sales/Manufacturing proof composition and the Logistics host are still unfinished. No normal Sales definite-shipment or SMM event is emitted by this participant. This is PARTIAL; runtime/SQL NOT_RUN.

### Late purchase invoice cost source (STK-SC36-a/b, SAT-D385/D404/D413)

`20261117165518_AddPurchaseInvoiceCostApplications` is EF-generated and adds append-only invoice application and acceptance-share effect proofs. Company/invoice and company/admission identities are unique; the same Serializable transaction caps all invoiced shares against the immutable accepted quantity and updates only the current original-location TL cost pool. Original receipt and issue stamps and physical movements are unchanged. Actual Purchasing finalized-event document, receipt and quantity proofs are required; missing legacy evidence, mismatched invoice/matched quantity and unknown first receipt cost remain explicit refusals.

Down first refuses with **THROW 51926** while either proof table contains rows. Database UPDATE/DELETE guards use **51927** (application) and **51928** (receipt effect); the immutable child FK also prevents parent deletion. Two Up trigger SQL operations and one Down guard require K3 review; no guard allowlist has been broadened. Exact unabsorbed position differences are persisted once in `inventory.purchase-invoice-cost-difference.proposed.v1`; they are not rounded into or treated as a posted journal. Accounting source proposal, first recognition decisions, central composition/pins and real SQL acceptance remain separate work. Runtime/SQL **NOT_RUN**.

### AK-1 / AK-2 — piece count on the label (KLN-D119)

- AK-1: `IInventoryKilogramsPerUnitSourceV1` now receives the policy date; the host registers
  `ProductOwnerInventoryKilogramsPerUnitSource` (over Product's `IProductTheoreticalKilogramsPerUnitReadPortV1`) in place of
  the module's `NoSourceInventoryKilogramsPerUnitSourceV1` default, refusing a figure computed with another policy density.
  The Sales margin boundary keeps a converted (non-kg) cost `UnitConversionUndecided` until the SAT-D438 golden (AK-6).
- AK-2: a label either tracks pieces from its first stock or never (`PieceCount`, `StockLedger`). Receipt
  (`InventoryGoodsReceiptRequestV1.PieceCount`), definite shipment (`InventoryDefiniteShipmentRequestV1.PieceCount`, refusal
  400 `inventory_definite_shipment_quantity_invalid`), split/merge outputs (`pieceCount`) and stock counts
  (`countedPieceCount`) carry it; every other movement of a piece-tracking label (block/release, transfer, scrap, supplier
  return, manufacturing, reversal) is refused fail-closed until its slice sends pieces. Piece-less labels stay piece-less
  (no backfill) and every piece-less request keeps its stored idempotency hash.
- `20261121214213_AddInventoryLabelPieceCounts` adds nullable piece columns and checks on `Balances`, `MovementLines`,
  `LabelTransformationInputs/Outputs` and `StockCounts` (pure DDL). Down first refuses with **THROW 52021** while any piece
  count exists (forward fix). Real SQL acceptance: `InventorySqlServerRelationalAcceptanceTests.PieceCounts.cs`, **NOT_RUN**
  locally.
