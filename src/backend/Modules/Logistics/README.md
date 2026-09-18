# Logistics (LOG-1 — the ready part of T12: loading instruction)

Source of truth: #275 v2 plan §7.4, §13 (production example), R3 and Ö2 (`SINAN_KARARLARI_2026-09-18_O1_O8_VE_SON_ASAMA.md`), read at exact head `a5b8143b`.

## What this slice is

Domain records and their invariants only:

- `Quantity` — the Ö2 rule (kg ≤ 3 decimals, pieces whole, exact, no silent cut). Repeated from Purchasing on purpose: a Domain project must not reference another module's Domain (`MODULE_DEPENDENCY_ALLOWLIST`).
- `LoadingInstruction` (+ lines) — one company, one customer Party: Draft → Issued. **An instruction is not a definite shipment.**
  - **Four separate shares per line** (§7.4): instructed → loaded → shipped → delivered. Each transition is capped by the share before it, so a loaded sub-share is never subtracted twice (§13: 6.000 allocated, 4.000 loaded, 3.800 shipped → 200 loaded-not-shipped stays visible; loading never decrements the allocation again).
  - **Measurement kind** on every load: measured on the package, or allocated from a vehicle weighing (§7.4).
  - Blocked line refuses loads (the block's source is Inventory/Quality; only the refusal lives here).
  - Duplicate load/shipment/delivery ids are refused; `expectedVersion` on every mutation (`LogisticsVersionConflictException`).
- Customer Party, Product and the optional Inventory allocation are `Guid` identities only.

## What this slice is not

- No physical effect: no stock exit, no allocation consumption, no label scan, no weighing ticket. Definite shipment's stock exit + allocation consumption belong to Inventory (T11/T12); stock and allocation never get a second engine here.
- No vehicle/driver data (authorization and audit scope), no documents/invoices, no HTTP, DI, persistence or permissions.

## dependency_wait

- Definite shipment → stock exit + allocation consumption: Inventory T11/T12 on main.
- Real dispatch / weighing device, `VehicleVisit`, `WeighingTicket`, `DeliveryConfirmation`: later slices.
- Persistence: central writer (CARİ).
