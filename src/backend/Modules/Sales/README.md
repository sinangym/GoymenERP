# Sales module

Source of truth: plan #275 `a5b8143b` T03 (§14), R3, §13; Business #254 §2–5 and #66 5653637829;
#257 `CONTRACT_V1.md` (draft wire contracts); #260 (frontend create/list/detail, NOT_ENV);
#262 `REFERENCE_READ_V1.md` (form reference reads); Ö2, Ö5, Ö8; ADR-002, ADR-009.

## What exists

| Project | Slice | Content |
|---|---|---|
| `GoymenERP.Modules.Sales.Application` | #257, #262, #285, #289 | wire contracts of the draft (create/list/detail/replay DTOs, strict JSON, exact delivery planning), the reference-read handler (Cari account, Settings currency/unit, Product eligibility) and, since SALES-P1, the **draft store contracts** |
| `GoymenERP.Modules.Sales.Domain` | **SALES-P1** | the `SalesOrderDraft` aggregate: header + lines + delivery slices, stable rule codes, `Version` ≠ `Rev.0`, Ö2 quantity |

There is still no persistence, HTTP, host wiring, permission or UI change in the backend; the
frontend of #260 mounts nothing (`SHARED_MOUNT_HANDOFF.md`).

## Domain (`GoymenERP.Modules.Sales.Domain`, SALES-P1)

- `Quantity` — the Ö2 rule (kg ≤ 3 decimals, pieces whole, exact, no silent cut; trailing zeros dropped). Repeated from Purchasing on purpose: a Domain project must not reference another module's Domain.
- `SalesOrderDraft` — one company (server-resolved, immutable), one customer (`PartyId` + `CompanyAccountId`, both required), one currency, one business date; born `Kind=Order`, `State=Draft`, `Revision=0` (`Rev.0` on the wire).
  - **`Version` ≠ revision:** `Version` is the optimistic-concurrency token every mutation names (`AddLine(expectedVersion, …)`), starting at 1 and advancing by one per recorded change — the Purchasing / Logistics / Manufacturing / Product pattern (`SalesVersionConflictException`). `Revision` is the commercial revision and does not move in this slice (T33).
  - **Owner snapshots on every line** (#257 "persist owner snapshot … all StockUnit/SalesUnit/OrderUnit bindings"): `ProductSnapshot` (product id/version/code/name/effectiveOn + three `UnitBindingSnapshot`s) as the Product owner answered, `UnitProfileSnapshot` (unit, profile id/version, `IsKilogram`, owner's `ProfileScale`, applied `Scale` = min(profile, 3) — the PUR-3/LOG-3/MFG-3 shape) as the Settings owner answered. The draft refuses a snapshot of another company (`sales_snapshot_company_mismatch`), of another date than the business date (`sales_snapshot_date_mismatch`), and an order unit the Product snapshot does not bind as the order unit (`sales_product_unit_mismatch`). Nothing is rebuilt from Draft/Sellable/Complete; the snapshots come from the ports at command time and are stored with the draft.
  - **Money snapshot on the header** (`MoneyProfileSnapshot`, the Settings money profile of the currency on the business date): mandatory even for an unpriced draft (#257). There is no draft without one, and its `EffectiveOn` must be the business date (`sales_snapshot_date_mismatch`; ÜRÜN #306 P3-2).
  - **Ö2 on every quantity:** the dimension must be the unit profile's (`sales_unit_mismatch`); decimals beyond the applied scale are refused, never rounded (`sales_quantity_scale_exceeded`). **The applied scale is derived in the domain** — `SalesOrderDraft.AppliedScale` = min(owner's `ProfileScale`, Ö2's cap) — and a snapshot whose stated `Scale` disagrees is malformed (`ArgumentException`), so no snapshot can widen Ö2 (ÜRÜN #306 P3-1). The client's quantity text is kept verbatim (`QuantityText`) and must name the same decimal value in plain invariant notation (ÜRÜN #306 P3-3).
  - **Unit profile validity and dimension:** the snapshot carries the Settings owner's `IsActive` / `EffectiveFrom` / `EffectiveThrough`, and the draft refuses a profile that is inactive or not effective on the business date (`sales_unit_profile_not_in_effect`; CARİ #306 P2-2, the PUR-3 verifier's rule enforced where the snapshot is consumed). Only a weight (kilogram) or a count dimension has a Ö2 rule; any other dimension is `sales_unit_unsupported` — Sales maps nothing to pieces by guess, and only kilogram is approved in Settings today. A kilogram flag that contradicts the dimension is a malformed snapshot.
  - **Delivery slices:** client-owned stable `SliceId`, the line's unit only (no conversion), positive, unique, at most 500, never adding up to more than the line (`sales_slices_exceed_quantity`); the shortfall is `UnplannedQuantity`, never "delivered" (#257: 1000 = 400 + 600 → 0; 400 + 700 refused; 400 + 400 → 200 unplanned). Each slice keeps its quantity text verbatim, bound to its value like the line's (CARİ #306 P2-3).
  - **Commercial fields** (`unitPrice`, `priceUnitId`, `discountPercent`) are retained as text and **not validated** (T28); the only rule is #257's pairing (`sales_price_unit_pairing`). Six owner references (delivery address, contact, responsible, payment terms, source strategy, package) are stored as given and **not validated by their owners** in this slice.
  - Bounds: 1–500 lines (`sales_draft_without_lines` at store time via `RequireStorable`, `sales_too_many_lines`), unique `LineId` (`sales_duplicate_line_id`), notes ≤ 4000 (`sales_note_too_long`), document number ≤ 64.
  - **Stable rule codes:** every rule refusal is a `SalesRuleException` with a `SalesRuleCodes` code (all `sales_`), tests assert the code; the aggregate is the single authority, the line-level checks are a second guard reporting `sales_internal_invariant`; caller errors (empty id, null, non-positive quantity) stay `ArgumentException`s. `Lines` and `RequestedDeliverySlices` are read-only wrappers (pinned).
- **Document number:** a technical, company-scoped display identifier assigned by the store on first commit (SALES-P2). It is **not an official document number** (Ö8: official document production is closed); its format is display only and no gap-free guarantee is given or claimed. #257 treats it as opaque text.

### Application store contracts (`ISalesOrderDraftStore`, SALES-P1; implemented by SALES-P2)

- `SalesDraftWriteEnvelope` — the ADR-009 audit envelope (actor, permission, roles, correlation, causation, idempotency key, channel), the Inventory/Accounting shape; every value from the server-side context of the HTTP slice.
- `StoreDraftAsync(envelope, requestHash, build)` — one save: header, lines, snapshots, slices, the attempt record and the audit event together. **Replay (R3):** (company, idempotency key) is the attempt identity; the same key with the same `SalesDraftRequestHash` answers the first stored draft (`Replayed`, original correlation); another hash is `sales_same_key_payload_mismatch`; a key of another company is invisible — a company change is not a rollback. `FindDraftAsync` and `ListDraftsAsync` (newest first, opaque company-bound cursor, #257) are company-scoped; another company's draft is null, never 403.
- `SalesDraftRequestHash` — the canonical content of a create (the strict wire shape serialised and hashed); line order matters because the client keeps it across retries, and the quantity **text** is content: "5.00" and "5" are different attempts, so the second under the same key is a payload mismatch (409), never a replay (tested).

## Deliberate limits of this slice (not policy choices)

- **No real positive create today.** #257 makes the currency mandatory and validated through the Settings money profile with the central owner's accepted Sales money operation identifier; the approved Settings provider answers **Unavailable** for money and that operation identifier does not exist, so a contract-faithful create is 503 with every real provider. Product eligibility is not host-registered (C3) and the GDC family data is missing (#300), so every line is 503 as well. Only the kilogram unit path is real (GDC kg). SALES-P2's real-SQL tests therefore prove the negative path with the real providers and the positive path only with **isolated synthetic owner sources**; no PR claims a positive runtime, and no currency, family, profile or grant is invented.
- **No price, discount, tax, exchange rate or total** (T28); values are text and unvalidated.
- **No approval, revision or cancellation** (T33); `Rev.0` is fixed.
- **No HTTP, permission, host DI, OpenAPI, capability** (next slices); no host registration of Product eligibility (C3, grant pending).
- **No shipment, invoice, collection or stock effect** — separate owners (T11/T12/T32/T29).

## dependency_wait

- **SALES-P2:** `Sales.Infrastructure` — `sales` schema (drafts, lines, slices, attempts), (CompanyId, IdempotencyKey) attempt key + canonical hash, rowversion, audit envelope, six real-SQL cases; CARİ's central registrations (tenant guard inventory, allowlist `Sales.Infrastructure → Audit.Infrastructure`, integration csproj, verifier counts).
- **Settings money profile + Sales money operation identifier** (T02/T28): the real positive create. The operation identifier the port asks for is an open question (#66 5731075396); `MoneyProfileSnapshot` therefore carries no operation, and the Settings adapter of SALES-P2 passes it once it is decided.
- **C3:** Product eligibility host registration and the GDC family definition data; a human grant.
- **Cari:** the account belongs to the Party and is active in this company (#262 read, at command time).
- **Owner references** (address, contact, responsible, payment terms, source strategy, package): authorised reads and value snapshots (#257's owner-binding list).
- **HTTP slice:** the create/list/detail endpoints of `CONTRACT_V1.md`, permissions (registered centrally, granted by a human), 201/200 + `isReplay`, cursor codec.

```bash
dotnet test tests/GoymenERP.Sales.UnitTests/GoymenERP.Sales.UnitTests.csproj
```
