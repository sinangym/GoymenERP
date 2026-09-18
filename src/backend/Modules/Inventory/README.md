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
| The same movement applies once | `StockLedger.Apply` | R3 (domain side only) |
| Transaction company, owner of the goods and physical location are three separate facts | `Stock/StockPosition` | §5 |
| Blocking is its own dimension: it moves stock out of free, never out of the physical total | `StockMovementKind.Block/Release`, `FreeTotal` vs `PhysicalTotal` | §7.1 |
| A label is the physical identity; a reprint is the same identity plus an audited print and creates no stock | `Labels/MaterialLabel` | §7.1 |
| Lot/label trace is mandatory; a missing mandatory document never hides the physical arrival — the piece is recorded and waits (`PendingDocuments`) | `MaterialLabel`, every line carries a label | Ö3 |
| kg exact to at most 3 decimals; a 4th decimal is refused, never rounded or cut | `Quantities/Kilograms` | Ö2 |
| Cost is `NotCalculated` / `WaitingPolicy` / `Known`; an unknown cost is null, never 0 | `Costing/CostResult` | R6 |

Other modules are referenced by `Guid` identity only (company and location: Organization;
owner: Organization or Counterparty; product: Product; currency: Settings). No project
reference to another module, so the module-dependency allowlist does not change.

Not in this slice: transfer, count, return and reversal (T13); split/merge labels (new
identity, T13); allocation (T11); purchase receipt (T09); any HTTP, permission or UI.

## dependency_wait

- **Persistence (INV-2):** DbContext, migrations, the SQL race guard (rowversion/lock) and the
  central guard registration are CARİ's central work and INV-2. `StockLedger` is the in-memory
  domain rule; it does not claim a persistent race guard.
- **Settings kg unit validation (T02-a):** whether a Product's stock unit really is kilogram
  is the Settings owner's answer. Until T02-a is on main this module only guarantees the
  kilogram arithmetic (`Kilograms`), not that a given product is weighed in kg.
- **The first real product (Ö1's authorised source):** Ö1 fixes the first path as GDC
  band/coil kg→kg, but the real product, quality and size dictionaries come from its
  authorised card/label/document. The test identities in `tests/GoymenERP.Inventory.UnitTests`
  are synthetic fixtures, not master data, and are never seeded anywhere.

```bash
dotnet test tests/GoymenERP.Inventory.UnitTests/GoymenERP.Inventory.UnitTests.csproj
```
