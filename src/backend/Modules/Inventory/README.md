# Inventory module

Owner: GOYMEN-URUN (Product owner, assigned Inventory by the coordinator on 2026-09-18).
Source: integrated plan v2 (#275 `a5b8143b`) §5, §7.1, §13, R3 and R6; Sinan's decisions of
2026-09-18 Ö1–Ö3 (`docs/decisions/SINAN_KARARLARI_2026-09-18_O1_O8_VE_SON_ASAMA.md`).

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

## dependency_wait

- **Central registration (CARİ, same branch, sequential):** the tenant guard's reviewed inventory (this DbContext's fingerprint, the design-time factory and the initializer's `Migrate`/`EnsureCreated` branch); the module-dependency allowlist edge `Inventory.Infrastructure → Audit.Infrastructure` (ADR-009); the `Api.IntegrationTests` project reference to Inventory; and the relational verifier's BROAD list and counts for `InventorySqlServerRelationalAcceptanceTests`.
- **INV-3 central follow-up (CARİ):** re-pin the tenant guard fingerprints of `InventoryDbContext.cs` and `InventoryStore.cs`, which changed here (no new raw SQL; the SqlClient use is unchanged); add the four new names of `InventorySqlServerRelationalAcceptanceTests` to the relational verifier's BROAD list and counts.
- **INV-3:** command/query surface, permissions (registered centrally, granted by a human), host wiring with the audit interceptor, and UI.
- **The first real product (Ö1's authorised source):** Ö1 fixes the first path as GDC band/coil kg→kg, but the real product, quality and size dictionaries come from its authorised card/label/document. Every identity in the tests is a synthetic fixture, never seeded anywhere.

```bash
dotnet test tests/GoymenERP.Inventory.UnitTests/GoymenERP.Inventory.UnitTests.csproj
# real SQL (needs GOYMEN_ERP_TEST_SQLSERVER_CONNECTION; the CI relational job runs it)
dotnet test tests/GoymenERP.Api.IntegrationTests/GoymenERP.Api.IntegrationTests.csproj --filter FullyQualifiedName~InventorySqlServerRelationalAcceptanceTests
```
