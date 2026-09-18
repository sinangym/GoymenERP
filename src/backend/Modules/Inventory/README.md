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
- **Actors are text.** Print and document-attachment actors are free text; the ADR-009 audit
  envelope (actor id, company, correlation) comes with persistence (INV-2).

## dependency_wait

- **Which company scopes a label (decide BEFORE INV-2's DbContext).** `MaterialLabel` has no
  company. ADR-002 requires a `CompanyId` and a global query filter on every tenant row, and a
  physical coil can move between companies by transfer (T13). Whether the label carries a
  company (and what a transfer does to it) or is a reviewed group-level identity is a model
  decision; it is to be brought as one narrow joint proposal by ÜRÜN and CARİ (central) before
  INV-2 starts, not coded in this slice.
- **Persistence (INV-2):** DbContext, migrations, the SQL race guard (rowversion/lock) and the
  central guard registration are CARİ's central work and INV-2. `StockLedger` is the in-memory
  domain rule; it does not claim a persistent race guard.
- **Settings kg unit validation (T02-a):** whether a Product's stock unit really is kilogram
  is the Settings owner's answer. T02-a is now on main (#279: the approved GDC kg profile, scale
  3, and `SettingsUnitIdentitiesV1.Kilogram`), but this branch does not consume it: this module
  still guarantees only the kilogram arithmetic (`Kilograms`). Binding a product's stock unit to
  that identity and the kg rule to the profile's scale (instead of the constant 3) is INV-2's.
- **The first real product (Ö1's authorised source):** Ö1 fixes the first path as GDC
  band/coil kg→kg, but the real product, quality and size dictionaries come from its
  authorised card/label/document. The test identities in `tests/GoymenERP.Inventory.UnitTests`
  are synthetic fixtures, not master data, and are never seeded anywhere.

```bash
dotnet test tests/GoymenERP.Inventory.UnitTests/GoymenERP.Inventory.UnitTests.csproj
```
