# Logistics (LOG-1 + LOG-2 — the ready part of T12: loading instruction)

Source of truth: #275 v2 plan §5, §7.4, §13 (production example), R3 and Ö2 (`SINAN_KARARLARI_2026-09-18_O1_O8_VE_SON_ASAMA.md`), read at exact head `a5b8143b`; the approved GDC kg unit profile (#279, #284, #287) for unit verification.

## What is here

### Domain (`GoymenERP.Modules.Logistics.Domain`)
- `Quantity` — the Ö2 rule (kg ≤ 3 decimals, pieces whole, exact, no silent cut; trailing zeros dropped so equal quantities print equally). Repeated from Purchasing on purpose: a Domain project must not reference another module's Domain.
- `LoadingInstruction` (+ lines) — one company, one customer: Draft → Issued. **An instruction is not a definite shipment.**
  - **Customer = Party + CompanyPartyAccount (LOG-2, CARİ #282 P3-2):** the instruction carries both identities and both are required (§5: a Party alone is not a customer of a company; the account holds the commercial relationship and its active state). Each line carries the **sales order line** it serves (`SourceOrderLineId`, required) and its **Settings unit** (`UnitId`). All are identities only; that the account belongs to the Party and is active in this company, and that the order line exists, are Cari's and Sales' checks at integration (dependency_wait).
  - **Four separate shares per line** (§7.4): instructed → loaded → shipped → delivered. Each transition is capped by the share before it, so a loaded sub-share is never subtracted twice (§13: 6.000 allocated, 4.000 loaded, 3.800 shipped → 200 loaded-not-shipped stays visible).
  - **A delivery names its source shipment** (§7.4 "eksik/hasarlı/ret/iade kaynak sevke bağlı"): unknown id, load share or another line's shipment refused; capped by that shipment's undelivered remainder before the line-level cap. `LoadingShare.SourceShipmentId` keeps the link.
  - **Measurement kind** on every load: measured on the package, or allocated from a vehicle weighing.
  - **Blocked line refuses loads and definite shipments** (§7.4 "kalite bloku … engellenir", Ö3 "sevk serbestliği yok"), even when loaded before the block. Block placeholder: `BlockLine`/`UnblockLine` toggle a local flag with no source, reason or authority until the block is read from its owner (Inventory/Quality).
  - **Open question for Business — `MarkDelivered` under a block:** a delivery record concerns goods that have already left (§7.4 ties shortage/damage/rejection/return to the source shipment), so a later block does not refuse it today (pinned by test). Whether such a delivery should also be flagged is not decided here.
  - **Stable rule codes (LOG-2, ORG #282 P3-2):** every refusal is a `LogisticsRuleException` with a `LogisticsRuleCodes` code; tests assert the code. The aggregate is the single authority for every share bound; the line-level checks are a second guard that reports `logistics_internal_invariant`. The "shipment of another line" rule has its own code and its own test with a real shipment on the other line (ORG #282 P3-1).
  - **Read-only views:** `Lines` and `Shares` are wrappers; a cast cannot reach the backing lists (pinned).
  - `expectedVersion` on every mutation; duplicate load/shipment/delivery ids refused.
- **§7.4 "yanlış ürün/sahip/tahsis engellenir" is not enforceable in this slice:** `RecordLoad` carries no identity of the loaded goods; that check arrives with `LoadScan` (dependency_wait).

### Application (`GoymenERP.Modules.Logistics.Application`, LOG-2)
- `LoadingUnitVerifier` — verifies a line's unit and quantity against the Settings unit profile through `ISettingsPrerequisiteProvider` (Contracts.V1) before a line is added, the same pattern as Purchasing's verifier: fail-closed with the owner's codes kept verbatim (`settings_access_denied`, `settings_provider_unavailable`, `SettingsPrerequisiteCodesV1.UnitProfileNotFound`, `settings_profile_invalid`); server-resolved company, unit identity, validity and window checked; only kilogram profiles approved today (another dimension → `logistics_unit_unsupported`, no guessed count unit); precision from the profile's scale capped by Ö2's 3, never rounded.

## What this module is not
- No physical effect: no stock exit, no allocation consumption, no label scan, no weighing ticket. Definite shipment's stock exit + allocation consumption belong to Inventory (T11/T12) and are **one transaction** with the shipped share (§7.4); `MarkShipped` is never wired on its own.
- No vehicle/driver data (authorization and audit scope), no documents/invoices, no HTTP, DI, persistence or permissions.

## dependency_wait
- Definite shipment → stock exit + allocation consumption: Inventory T11/T12 on main.
- Block source (Inventory/Quality record) replacing the local flag.
- `LoadScan`: identity of the loaded goods and the wrong-product/owner/allocation refusal of §7.4.
- Cari: `CompanyAccountId` belongs to `CustomerPartyId` and is active in this company; Sales: `SourceOrderLineId` exists and is this customer's — both at integration, when those owners' read contracts are consumed.
- Count/piece units: a Settings-approved count dimension.
- Delivery under a later block: Business decision.
- Persistence: central writer (CARİ).
