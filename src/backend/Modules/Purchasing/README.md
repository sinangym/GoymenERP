# Purchasing (PUR-1 — first independent slice of T08)

Source of truth: #275 v2 plan §7.2, §13, R3 and Ö2 (`SINAN_KARARLARI_2026-09-18_O1_O8_VE_SON_ASAMA.md`), read at exact head `a5b8143b`.

## What this slice is

Domain records and their invariants only:

- `Quantity` — exact, non-negative, one unit; kilograms carry at most three decimals, pieces are whole (Ö2). Extra digits are refused, never rounded or cut.
- `PurchaseRequest` (+ lines) — the demand: Draft → Submitted. Not a commitment.
- `PurchaseOrder` (+ lines) — the commitment towards one supplier Party of one company: Draft → Approved.
  - **General/technical breakdown:** a general line can be carved into technical lines; general remainder + technical slices never exceed the first commitment, an accepted general share cannot be sliced again, a technical line cannot be broken down (§13: 1.000 → 925 general + 75 technical).
  - **Acceptance shares:** an accepted share never exceeds the line's remaining commitment; remaining = committed − accepted is future supply (§13: 100 ton, 40 accepted → 40 accepted + 60 remaining, never 140). A duplicate acceptance id is refused.
  - `expectedVersion` on every mutation (`PurchasingVersionConflictException`).
- Supplier Party and Product are `Guid` identities only.

## What this slice is not

- No physical effect: no lot, label, location or stock movement. Inventory owns those (T09); Purchasing is not a second allocation engine (§7.2).
- No price, tax or currency (T28); no approval policy — `Approve` is a transition, which approvals a purchase needs is §8.
- No offers (SupplierOffer), delivery schedules, invoice matching, HTTP, DI, persistence or permissions.

## dependency_wait

- Acceptance → stock/label: Inventory T09 on main.
- Price/commercial snapshot: T28.
- Persistence: central writer (CARİ); no DbContext or migration here.
