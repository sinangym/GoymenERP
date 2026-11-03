# Purchasing (PUR-1 … PUR-4a, goods receipt and AT-025 — the implemented slices of T08)

## Current state (2026-11-02)

This section is the current summary; the sections below it are the slice history and are kept as written, with
superseded statements marked **Superseded (2026-11-02)**. Where they disagree, this section and the code win.

- **Plan and decisions:** the module's roadmap is `docs/delivery/ALIS_HAZIRLIK_PLANI_v1.md` (waves A0…A6); the
  business decisions are `docs/decisions/SINAN_KARARLARI_2026-11-02_ALIS.md` (ALS-D1…D13), `_ALIS_EK1.md`
  (ALS-D14…D20), `_ALIS_EK2.md` (ALS-D21…D28), `_ALIS_EK3.md` (ALS-D29…D43), `_ALIS_EK5.md` (ALS-D46), `_ALIS_EK6.md` (ALS-D47) and
  `SINAN_KARARLARI_2026-10-27_NO161_TALEP_RED_SIPARIS_IPTAL.md` (request rejection, cancellation of an approved
  order's unaccepted remainder). Those decisions are **not implemented yet** unless a line below says so; the target
  states are listed in `docs/domain/DURUM_MAKINELERI.md` ("Satın Alma") and the ownership split in
  `docs/domain/SISTEM_OF_RECORD_MATRISI.md`.
- **Statuses in code:** `PurchaseRequestStatus` = Draft, Submitted, Approved, Rejected, Withdrawn (`Domain/Requests/PurchaseRequest.cs`);
  `PurchaseOrderStatus` = Draft, Approved (`Domain/Orders/PurchaseOrder.cs`). There is no order cancellation, closure,
  revision or supplier confirmation yet.
- **Request approval and rejection (A1-1, plan API-01; ALS-D2, ALS-D16, No.161 §1):** `POST requests/{id}/approval`
  (Submitted → Approved) and `POST requests/{id}/rejection` (Submitted | Approved → Rejected, `reason` required, at most
  4000 characters, never cut) under the new name `purchasing.requests.approve` (`PurchasingService.RequestDecisions.cs`;
  commands `purchasing.purchase-request.approve.v1` / `.reject.v1`, audit `purchasing.purchase-request.approved.v1` /
  `.rejected.v1`). The creator (`CreatedBy`, the preparer) and the submitter (`SubmittedBy`, recorded since A1-1) are the
  makers; neither may decide (`purchasing_request_same_actor`, ordinal comparison); a request whose submitter is not
  recorded cannot be decided at all (`purchasing_request_maker_unknown`, fail-closed). A rejection is refused while an
  APPROVED order is bound to the request (`purchasing_request_has_approved_order`; the store reads the fact inside the
  same save and the request row is the write's concurrency token, which every binding order approval also moves).
  Other terminal refusals: `purchasing_request_decision_state`, `version_conflict`; the reason's 422s:
  `purchasing_request_rejection_reason_required` / `_too_long`. The detail view carries `approval` and `rejection`
  (decided by, at, reason). Migration `20261102125710_AddPurchaseRequestDecisions` adds the columns, the
  `CK_PurchaseRequests_Decision` and `CK_PurchaseRequests_MakerChecker` checks, records the submitter of already
  submitted requests from their own stored submit attempt, and its Down refuses (`THROW 51164`) while a request is
  Approved or Rejected. Grant of the name is a human gate: no real role, seed or user carries it (only the SENTETIK
  `pur01` browser-suite approver in its disposable stack).
- **Legacy `purchasing_request_maker_unknown` (#900 review P3):** a request submitted before A1-1 whose submit attempt
  is not in `purchasing.PurchaseAttempts` keeps `SubmittedBy = NULL` after the migration's backfill (nothing is
  guessed). Such a request stays Submitted and visible, but no one can approve or reject it — every decision answers
  the terminal 409 `purchasing_request_maker_unknown`; only its recorded preparer may withdraw it (A1-1b, ALS-D47). There is no repair command; a forward fix (a
  reviewed data correction recording the real submitter) is an operations decision, not a code default. In a fresh
  database every submission records its submitter, so the code is reachable only for such legacy rows.
- **Request withdrawal (A1-1b, plan API-01b; ALS-D46, ALS-D47):** `POST requests/{id}/withdrawal` (Submitted →
  **Withdrawn**, a separate terminal outcome from Rejected so reports never mix a maker's withdrawal with a manager's refusal;
  `reason` required, at most 4000 characters, never cut, the rejection's blank rule) under its own name
  `purchasing.requests.withdraw` (`PurchasingService.RequestDecisions.cs`; command `purchasing.purchase-request.withdraw.v1`,
  audit `purchasing.purchase-request.withdrawn.v1` in the same save). **Only the recorded preparer (`CreatedBy`) or the
  recorded submitter (`SubmittedBy`)** withdraws, ordinal comparison (ALS-D47, Sinan 2026-11-02: "oluşturan veya
  gönderen"); anyone else is `purchasing_request_not_preparer`; an unrecorded maker never matches, and when neither is
  recorded nobody withdraws (`purchasing_request_maker_unknown`);
  Draft / Approved / Rejected / Withdrawn are `purchasing_request_decision_state` (field code
  `purchasing_request_not_withdrawable`); an APPROVED order bound to the request (possible only for orders approved
  before A1-1, when Submitted requests could source orders) refuses it (`purchasing_request_has_approved_order`,
  No.161 §1's guard). The reason's 422s: `purchasing_request_withdrawal_reason_required` / `_too_long`. The detail view
  carries `withdrawal` (by, at, reason) and the recorded `preparedBy` and `submittedBy`. Migration
  `20261102161658_AddPurchaseRequestWithdrawal` adds `WithdrawnBy` / `WithdrawnAtUtc` / `WithdrawalReason`, the
  Withdrawn branch of `CK_PurchaseRequests_Decision`, `CK_PurchaseRequests_Withdrawal` (withdrawer = preparer or
  recorded submitter, binary comparison; no withdrawal facts on any other status) and re-creates `CK_PurchaseRequests_MakerChecker` with a binary
  comparison (`Latin1_General_100_BIN2`, the domain's ordinal rule; #900 review P3 — under the database's
  case-insensitive default a decider differing from a maker only by case passed the domain and failed the check). Its
  Down refuses (`THROW 51165`) while a request is Withdrawn. Grant of the name is a human gate: no real role, seed or
  user carries it (only the SENTETIK `pur01` browser-suite creator in its disposable stack).
- **Down guard `51166` (#907 review P3, CODE-REVIEW-A):** `AddPurchaseRequestWithdrawal`'s Down also refuses
  (`THROW 51166`, before any drop) while a stored approval or rejection differs from its preparer or submitter **only by
  case** (`ApprovedBy`/`RejectedBy` = `CreatedBy`/`SubmittedBy` under the column's default, case-insensitive collation).
  Since A1-1b the domain (ordinal) and `CK_PurchaseRequests_MakerChecker` (binary) treat such an actor as a different
  person, so the decision is valid; the A1-1 check that Down would restore compares case-insensitively and would reject
  the row with a raw 547 halfway through the rollback. The refusal is the clear, early form of that failure. There is
  no automatic repair: rolling back past A1-1b with such rows needs a reviewed forward fix (an operations decision), not
  a code default. A fresh database never meets it unless actor identities differing only by case exist.
- **Request line source (A1-2, plan API-02; Üretim A-2, Satış S-A1, ALS-D16):** every request line may carry an
  optional `source` on `POST requests` — `kind` exactly `Stock`, `Sales` or `Production` plus the source record's opaque
  identity `documentId` and `documentLineId` (both non-empty); a line without one is **manual** (`Manual` is not accepted
  on a line, so manual has one spelling). The source is fixed when the line is created (request lines are insert-only)
  and plays no part in any decision: a Production-sourced request waits for the manager's approval like any other
  (ALS-D16). The identities are **opaque**: Purchasing does not read Sales, Manufacturing or Inventory tables and does
  not check that the record exists (no producer port yet; ALS-D58 forbids new ports in deferred modules) — validation
  belongs to the producing module's future port and is a follow-up. A null source is never serialised into the content
  hash, so the ADR-011 attempts recorded before A1-2 keep replaying. No new permission: the request is created under the
  existing creation name. The detail line carries `source` (null = manual); the summary (list and detail) carries
  `sourceKinds`, the distinct kinds of its lines in the fixed order `Manual`, `Stock`, `Sales`, `Production`. The
  create audit record names each line's source. Migration `20261103012142_AddPurchaseRequestLineSource` adds the
  nullable `SourceKind` (1 Stock, 2 Sales, 3 Production) / `SourceDocumentId` / `SourceDocumentLineId` columns,
  `CK_PurchaseRequestLines_Source` (none of the three, or a defined kind with both identities non-empty) and the filtered
  index `IX_PurchaseRequestLines_Source` (`CompanyId, SourceKind, SourceDocumentId`) for the reads by source record the
  Sales adaptor (S-A1/MOD-03) and the Üretim action (A-2/U5-c) will need; its Down refuses (`THROW 51167`) while any line
  carries a source (dropping the columns would silently turn it manual). The P-01 form opens manual requests only; the
  detail and list show the source read-only. The plan's "istenen tarih" (requested date) of API-02 is not in this slice.
- **Order creation:** an order is created with or without a source request (`CreatePurchaseOrderRequest.SourceRequestId`
  is optional); when given, the request must be **Approved** (A1-1, ALS-D2/ALS-D16; 409 `purchasing_request_state`,
  field `sourceRequestId` = `not_approved`) and of the same company (`PurchasingService.ReadSourceRequestAsync`). An
  order opened for a request is approved only while that request is still Approved (store code
  `purchasing_source_request_not_approved`, answered as a terminal 409 `purchasing_request_state`), so a rejected
  request's draft order never becomes a commitment.
- **Product purchase eligibility is wired:** the host registers `ProductPurchaseEligibilityPort`
  (`GoymenERP.Api/Endpoints/PurchasingProductSelectionHostPorts.cs`) over Product's `IProductPurchaseEligibilityV1`
  (`Program.cs`: `AddScoped<IProductPurchaseEligibilityPortV1, ProductPurchaseEligibilityPort>()`). Owner failures stay
  fail-closed (Denied / Refused / Unavailable), never a positive answer. The service still answers 503
  `product_eligibility_unavailable` when no port is registered (`PurchasingModuleExtensions` resolves it with
  `GetService`). Product candidates for the order form are read through
  `GET /api/v1/purchasing/references/products` (`PurchasingProductCandidateEndpoints.cs`, measured with
  `purchasing.orders.create`).
- **Goods receipt exists** (lines of an APPROVED order): `PurchasingService.GoodsReceipt.cs` +
  `GoymenERP.Api/PurchasingRuntime/PurchaseGoodsReceiptHost.cs`. One host database transaction enlists Purchasing's
  `IPurchaseAcceptanceParticipantV1` (`PurchaseAcceptanceParticipant`: the domain `RecordAcceptance`, the line's
  accepted total, a `PurchaseAcceptanceShares` row with idempotency and an audit event) and Inventory's
  `InventoryGoodsReceiptParticipant` (lot, label, location, stock movement — Inventory's rows, not Purchasing's).
  The request carries accepted quantity, measured kg, location, lot, label, optional heat/coil and movement id
  (`RecordPurchaseGoodsReceiptV1`). New receipts enter stock with `InitialBlock = "Free"`
  (`PurchaseGoodsReceiptApiV1.InitialBlock`; Sinan #66/5829087145, ALS-D4); replays keep the stored original block.
  An acceptance above the line's remaining commitment is refused (`purchasing_acceptance_quantity_exceeds_remaining`):
  there is **no delivery tolerance band yet** (OPEN-004, ALS-D10/D12, plan A3). Migration
  `20260924102017_AddPurchaseAcceptanceParticipant` adds the shares table.
- **Receipt reads:** options (locations), list, detail and the read-only accepted-vs-measured kg comparison
  (`PurchaseGoodsReceiptComparisonContractsV1.cs`: `SameKilogramUnit` / `Unavailable` are emitted; `ApprovedConversion`
  is declared but intentionally not emitted; no tolerance, approval or stock effect).
- **HTTP (17 routes):** requests — create, list, detail, `submission`, `approval`, `rejection`, `withdrawal`; orders — create, list, detail, `approval`;
  goods receipts — record (`POST orders/{id}/lines/{lineId}/goods-receipts`), options, list, detail,
  quantity comparison (`PurchasingEndpoints.cs`); product candidates (`PurchasingProductCandidateEndpoints.cs`).
- **Permissions (6):** `purchasing.orders.create`, `.read`, `.approve`, `purchasing.goods-receipts.record`,
  `purchasing.requests.approve` (A1-1), `purchasing.requests.withdraw` (A1-1b) (`PurchasingPermissionManifestV1`,
  `ErpPermissions`; company-wide, not critical, registered only).
- **Shell and screens:** `PurchasingShellCapabilityProvider` (owner contract `PURCHASING/1.6`) declares P-01 request
  list/new/detail (+ submit, since A1-1 the `purchasing.requests.approve` decision child and since A1-1b the
  `purchasing.requests.withdraw` withdrawal child), P-03 order list, **P-04 new order**, P-03-DETAIL (+ approve and goods-receipt
  children), all `SingleCompany` and `ImplementationAvailable`. Frontend `src/frontend/goymen-erp-web/src/purchasing/`:
  `PurchaseRequestListPage`, `PurchaseRequestCreatePage`, `PurchaseRequestDetailPage` (+ `PurchaseRequestDecisionPanel`:
  approve / reject with reason, unknown-result resend of the same key; `PurchaseRequestWithdrawalPanel`: "Geri çek"
  with reason, shown only to the recorded preparer or submitter), `PurchaseOrderListPage`,
  `PurchaseOrderCreatePage`, `PurchaseOrderDetailPage`, `GoodsReceiptPanel`, `GoodsReceiptDetail`,
  `GoodsReceiptQuantityComparison` (routes in `purchasingRoutes.tsx`).
- **Ports out:** `IPurchaseSupplyReferencePortV1` (expected supply, never stock), `IPurchaseOpenLineReadPortV1`
  (AT-025, below), `IPurchaseAcceptanceSourceV1`; the Document Vault reads purchase-order ownership through the host
  adapter `GoymenERP.Api/DocumentsRuntime/PurchasingDocumentOwnerLinkPort.cs`.
- **Still absent:** price/tax/currency (T28; ALS-D5/D6), order approval policy and the order's preparer ≠ approver rule (ALS-D3),
  revision, cancellation/closure, delivery schedule and "in transit", service lines and service acceptance, outgoing
  subcontracting (A-1), incoming invoice and three-way match, supplier return, outbox events, breakdown over the API
  (domain only), count/metre units (Settings approves kilograms only).
- **Known stale comments outside this README:** in `GoymenERP.Api/Program.cs` the PUR-4a comment above the
  Purchasing registrations still says no purchase-eligibility port is registered (the next line registers it), and a
  later comment still calls Purchasing's order screens "staged". They are code comments and are left to a code PR
  (plan FZL-03).

## History

Source of truth: #275 v2 plan §7.2, §13, R3 and Ö2 (`SINAN_KARARLARI_2026-09-18_O1_O8_VE_SON_ASAMA.md`), read at exact head `a5b8143b`; the approved GDC kg unit profile (#279, #284) for unit verification.

## What is here

### Domain (`GoymenERP.Modules.Purchasing.Domain`)
- `Quantity` — exact, non-negative, one unit; kilograms carry at most three decimals, pieces are whole (Ö2). Extra digits are refused, never rounded or cut; trailing zeros are dropped so equal quantities print equally. Since TEK-02 the rule lives once in `GoymenERP.BuildingBlocks.Quantities.Quantity<TRefusals>`; Purchasing spells it `Quantity` through a project `global using` alias over `PurchasingQuantityRefusals`, which keeps the Purchasing codes.
- `PurchaseRequest` (+ lines) — the demand: Draft → Submitted. Not a commitment.
- `PurchaseOrder` (+ lines) — the commitment towards one supplier Party of one company: Draft → Approved.
  - **General/technical breakdown:** a general line can be carved into technical lines while Draft and after approval (§7.2: the technical breakdown becomes known later); general remainder + technical slices never exceed the first commitment, an accepted general share cannot be sliced again, a technical line cannot be broken down, and a technical line keeps the general line's unit — only its product may differ (§13: 1.000 → 925 general + 75 technical). Whether a post-approval breakdown needs re-approval is §8 policy and is not chosen here.
  - **Acceptance shares:** an accepted share never exceeds the line's remaining commitment; remaining = committed − accepted is future supply (§13: 100 ton, 40 accepted → 40 + 60, never 140). A duplicate acceptance id is refused.
  - `expectedVersion` on every mutation (`PurchasingVersionConflictException`).
- **Stable rule codes (PUR-2, ORG #278 P3-1):** every refusal is a `PurchasingRuleException` with a `PurchasingRuleCodes` code; tests assert the code, not only the type. The aggregate is the single authority for every share bound; the line-level checks are a second guard that reports `purchasing_internal_invariant`, so a test can tell which guard held.
- **Read-only views (PUR-2, ÜRÜN #278 P3-1):** `Lines` and `Acceptances` are read-only wrappers; a cast cannot reach the backing lists.
- Supplier Party, Product and the line's **Settings unit** (`UnitId`) are `Guid` identities only.

### Application (`GoymenERP.Modules.Purchasing.Application`, PUR-2)
- `PurchaseUnitVerifier` — verifies a line's unit and quantity against the Settings unit profile through `ISettingsPrerequisiteProvider` (Contracts.V1) before a line is added (ÜRÜN #278 P3-2; Ö2):
  - fail-closed: every Settings failure is a refusal with the owner's code kept verbatim (`settings_access_denied`, `settings_provider_unavailable`, `settings_unit_profile_not_found`, `settings_profile_invalid`) — "denied", "unavailable" and "no profile" stay distinct;
  - the profile must belong to the server-resolved company the caller passes, name the requested unit, be internally valid and active on the effective date;
  - only kilogram profiles are approved today (Settings README, Ö1): a profile of another dimension is refused as `purchasing_unit_unsupported`, never mapped to a guessed count unit;
  - precision comes from the profile's scale, capped by Ö2's kilogram rule of 3; more decimals are refused (`purchasing_quantity_scale_exceeded`), never rounded. The result carries both the owner's `ProfileScale` and the applied `Scale` = min(profile, 3), so an input limit built from the result matches what the domain accepts (PUR-3, ÜRÜN #288 P3-2).
  - The "no profile" code is the owner's `SettingsPrerequisiteCodesV1.UnitProfileNotFound` (Contracts.V1, #287), passed through from the port error's `Code`; Purchasing keeps no copy of it (PUR-3).
- **What the domain does not enforce (PUR-3, ÜRÜN #288 P3-1 / ORG INFO-1):** `AddLine` takes a `UnitId` and a `Quantity` as given; it does not check that the id is a verified Settings unit or that `Quantity.Unit` matches the id's dimension (`UnitMismatch` compares quantity units only). `PurchasingService` makes the verifier the mandatory command entry for both.
- **`BreakDown` has no status guard (PUR-3, ORG #288 P3-1):** it is open in both existing statuses (Draft, Approved; pinned by tests). The former guard was unreachable and would have reported `purchasing_order_not_approved` for a closed order. The slice that adds a third status adds the guard with its own code and test.

## Multi-sourcing (decided: Sinan #66 5738113623 §1, PUR-2 = A)
- A request line may split across several orders at partial quantities; **the sum of the approved shares never exceeds the line's need**. PUR-4a binds an order line to at most one line of the order's source request (`PurchaseOrder.CreateForRequest` + `AddLine(..., sourceRequestLineId, now)`; codes `purchasing_source_line_without_request`, `purchasing_duplicate_source_line`) and the store measures the sum at approval (below). `FromRequest` (full quantities) stays as the older shape; the README's former "several full-quantity orders" question is settled as a mistake by that decision.

## What this module is not
- No physical effect: no lot, label, location or stock movement; not a second allocation engine (Inventory T09). **Superseded in part (2026-11-02):** Purchasing itself still writes no lot, label, location or stock movement; a goods receipt now stages Inventory's lot/label/movement in the same host transaction, through Inventory's own participant (`PurchaseGoodsReceiptHost`); those rows remain Inventory's.
- No price, tax or currency (T28); no approval policy (`Approve` is a transition; §8). *(Still true 2026-11-02; decided as ALS-D3/D5/D6, not implemented.)*
- No offers, delivery schedules, invoice matching, HTTP, DI, persistence or permissions. **Superseded (2026-11-02):** HTTP, DI, persistence and permissions exist since PUR-4a (see below and "Current state"); offers, delivery schedules and invoice matching are still absent.


## PUR-4a — purchase order persistence + authorized API + the supply port

Card: SİPARİŞ PUR card (coordinator-approved 2026-09-19; chain link "satış → mevcut stok VEYA alış/üretim arzı → tahsis", Business 5743412166). Sources: #275 v2 §7.2/§13, DEC-PUR-001, Sinan 5738113623 §1 (multi-sourcing), `SINAN_KARARLARI_2026-08-31` §4 (OPEN-003 kantar manual — 4b), the LOG-4a / SALES-P2 persistence shape, ADR-009/011, #276 fences.

| Layer | What |
|---|---|
| Application (`Contracts/`) | `PurchasingContractsV1` (strict wire shapes, `PurchasingJson`, `PurchasingProblemCodesV1`, `PurchasingApiV1` paths), `PurchasingStoreContracts` (`IPurchasingStore`, ADR-011 envelope, commands `purchasing.purchase-request.create.v1` / `.submit.v1`, `purchasing.purchase-order.create.v1` / `.approve.v1`), `IPurchasingService`, `PurchasingPermissionManifestV1` (`purchasing.orders.create` / `.read` / `.approve` — **superseded (2026-11-02):** plus `purchasing.goods-receipts.record`; company-wide, not critical; two personas — enter vs approve), `IPurchasingPartyAccountPortV1` (Cari supplier: party + account + the account's **Supplier role**; host adapter proposal `CounterpartyPurchasingPartyAccountPortV1`), `IProductPurchaseEligibilityPortV1` (the Product owner's purchase eligibility — **not published yet (ÜRÜN #288 P3-3)**: the host registers nothing, the service answers 503 `product_eligibility_unavailable` fail-closed, exactly as Sales before `IProductSalesEligibilityV1`; **Superseded (2026-11-02):** the host now registers `ProductPurchaseEligibilityPort`, see "Current state"), `IPurchaseSupplyReferencePortV1` (published for INV-AL-1: an APPROVED line's remaining commitment = **expected supply, never stock**). |
| Application (`PurchasingService`) | The first command handler: #276 fences (before anything, after every awaited owner read, right before the single save, none after the commit); replay first (ADR-011 §2–§5); owners in order — Cari supplier account (409 `account_state`: company_mismatch / passive_company_account / **not_supplier** / invalid_reference) → the SUBMITTED source request of this company, whose line a bound line must match in product and unit (409 `purchasing_request_state`: invalid_reference / not_submitted / source_line_mismatch) → Product purchase eligibility (503 unavailable, 403 denied, 422 `unsupported_product`, stale version = terminal 409 `version_conflict`) → `PurchaseUnitVerifier` (422 `unsupported_unit`; the verifier-before-line contract is now an invariant, PUR-3 dependency_wait closed). The quantity TEXT is validated first with Settings' `ExactPositiveDecimalV1` at the kg cap of 3 decimals and at most 15 integer digits, and only the validated text is converted — a text longer than a decimal holds is refused, never rounded (FİNANS #350 P2-1/P3-2 applied here too); a same-key race lost on a terminal path answers the winner's stored record as a replay (§8, P3-1). Terminal refusals (§7): stale version, wrong state (`purchasing_order_state`, `purchasing_request_not_draft`), stale product version, **Σ-shares** (`purchasing_request_line_over_sourced`). |
| Infrastructure (new project) | Schema `purchasing`: `PurchaseRequests`, `PurchaseRequestLines`, `PurchaseOrders`, `PurchaseOrderLines` (kind general/technical, parent, source request line, ordered/technical/accepted totals; CK technical + accepted ≤ ordered), `PurchaseAttempts` (ADR-011: (company, command, key hash) PK under BIN2; **two create-only filtered unique indexes** `UX_PurchaseAttempts_RequestId` / `UX_PurchaseAttempts_OrderId` — the #338 lesson: state commands share the aggregate id). Company query filters and write guards (no delete, no company change, only Status/UpdatedAtUtc(/SourcingVersion) on headers and Technical/Accepted on lines). `PurchasingStore`: one save per command, rebuild through the domain in recording order, replay/refusal records, opaque company-bound cursor, 2601/2627 → `concurrent_change`. **Σ-shares at approval:** for every general line bound to a request line, Σ ordered of the APPROVED orders' bound general lines + this line ≤ the request line's quantity, measured inside the approval's save; the request row's `SourcingVersion` moves with each binding approval so two approvals against one request collide on its row version (the loser measures again). `PurchaseSupplyReferencePort` over the store. Hand-written migration `20260919170000_InitialPurchasing` + snapshot (no dotnet locally). Shell provider **STAGED** (**Superseded (2026-11-02):** every declaration is `ImplementationAvailable`, owner contract 1.4), master v3 §27.3: list **P-03** `/satinalma/siparisler`, new **P-04** `/satinalma/siparisler/yeni`, detail **P-03-DETAIL** `/satinalma/siparisler/:orderId` (the read view of one P-03 row; the F-02-DETAIL precedent; P-01 Talep Havuzu unused — requests have no screen in 4a; **Superseded (2026-11-02):** P-01 list/new/detail exist). Approval is a server-measured command on the detail. |
| API | `Api/Endpoints/PurchasingEndpoints.cs`: `POST /api/v1/purchasing/requests` (201 + Location; replay 201 + headers), `GET` list/detail, `POST {id}/submission` (200); `POST /api/v1/purchasing/orders` (201), `GET` list/detail, `POST {id}/approval` (200). Platform 401/403/404 and every 503 code-less. |
| Tests | Unit: `PurchaseOrderSourceBindingTests`, `PurchasingServiceTests` (fences, replay, owner refusals, terminal refusals), `PurchasingPersistenceModelTests` (model, filtered indexes, guards, shell rows). Real SQL ×4 (`tools/relational_inventory/purchasing.txt`): one save each + same-key replay; Σ-shares across orders terminal + the supply port answers only the approved line's remaining; stale version terminal + same-key race one record; company B sees nothing + a second create attempt refused by the filtered index. HTTP acceptance over a stub service. |

**Assumptions (reversible, stated for the coordinator/ChatGPT):** ASSUMPTION-PUR4-1 — purchase requests are created, submitted and read under the three approved order names (same persona), no fourth name. ASSUMPTION-PUR4-2 — Draft orders reserve nothing; the Σ-shares rule is measured at approval across APPROVED orders.

**PUR-4a-FE owner slice:** P-03 list and P-03-DETAIL are implemented over the existing reads; the detail exposes the existing approve command only through its own `purchasing.orders.approve` child capability. The added declaration makes the owner contract `PURCHASING/1.1`; the child keeps the parent's route/context/scope and the backend still re-authorizes every company-bound mutation. It keeps an unresolved actor/company/order/key/expected-version snapshot for an explicit same-payload resend and re-reads the order after a definite success or conflict. P-04 creation remains staged: the Product-owned purchase eligibility/candidate source is still absent, and Sales E1 is not an alias for Purchasing. **Superseded (2026-11-02):** P-04 is published (`PurchaseOrderCreatePage.tsx`, `ImplementationAvailable: true`) over the product candidate read and the Cari supplier read. Central router/capability publication remains the Cari owner's single-writer handoff.

The published P-03 reads and approval control are currently `SingleCompany`: the HTTP contract requires an explicit company context and has no authorized group-read endpoint. A future group view is a backend predecessor plus its own acceptance evidence; shell metadata alone must not imply that behavior.

**Not in PUR-4a** (**Superseded in part (2026-11-02):** goods receipt, stock-in through the goods-receipt host transaction, the acceptance command over the API (`POST …/goods-receipts`), P-04 creation and the Product eligibility implementation have since landed — see "Current state"; the breakdown command over the API, kantar/irsaliye kg, tolerance, price/tax/currency and approval policy are still absent)**:** goods receipt, kantar/irsaliye kg, tolerance (OPEN-004 → profile/Unavailable), stock-in (PUR-4b + Inventory T09 + the D3 seam decision shared with LOG-5), price/tax/currency (T28), approval policy (§8), breakdown/acceptance commands over the API (domain only), P-04 creation publication, a Product purchase-eligibility implementation (ÜRÜN).

**Central (CARİ):** Program.cs wiring, the three `ErpPermissions` names (**Superseded (2026-11-02):** four — `purchasing.goods-receipts.record` added), the shell authority, OpenAPI, `INVENTORY_MODULES += purchasing`, Api csproj, guard pins, adoption of the party port proposal.

## dependency_wait
- ~~Acceptance → stock/label: Inventory T09 on main.~~ **Superseded (2026-11-02):** done through the goods-receipt host transaction (see "Current state").
- Price/commercial snapshot: T28.
- Persistence: central writer (CARİ); the verifier's result is retained with the line at that point.
- ~~First command handler (Application)~~: `PurchasingService` (PUR-4a) takes every line's quantity from `VerifiedPurchaseUnit` only and the company from `ICompanyContext`; "verifier before line" is an invariant there (ORG #288 INFO-1 closed).
- ~~Product purchase eligibility (Purchasable role + Active lifecycle + the unit being that product's purchase unit): a Product-owned implementation of `IProductPurchaseEligibilityPortV1`, not yet published (ÜRÜN #288 P3-3).~~ **Superseded (2026-11-02):** the host adapter `ProductPurchaseEligibilityPort` translates Product's `IProductPurchaseEligibilityV1`; the 503 `product_eligibility_unavailable` answer remains only for a host without the port.
- Count/piece units: a Settings-approved count dimension. Until then the domain's `Piece` arithmetic exists but no verified path produces it.
- ~~Goods receipt → stock (PUR-4b): Inventory T09 receipt write (ÜRÜN), the D3 seam decision (with LOG-5), NEEDS-BUSINESS #66 5743483383 (received stock blocked until quality release?).~~ **Superseded (2026-11-02):** the goods receipt writes Purchasing's share and Inventory's receipt in one host transaction; new receipts are `Free` (Sinan #66/5829087145, ALS-D4). Delivery tolerance and the weighbridge slip number remain open (OPEN-004, plan A3).

## AT-025 — the open-line read for the operations work list

`IPurchaseOpenLineReadPortV1` (Contracts) / `PurchaseOpenLineReadPort` (Infrastructure, registered in `AddPurchasingModule`): the context company's APPROVED purchase order lines whose remaining (committed − accepted, where committed = ordered − technical) is above zero, as a LINE page (1–100), newest order first then by line number, with an opaque keyset cursor (created time, order id, line number) bound to the company that read it (a foreign or malformed cursor is `ArgumentException`). Draft and fully received lines are not listed; another company's orders are invisible under the tenant filter; no company context is refused before any read. Quantities are the owner's exact canonical texts (Ö2). Price-free (Purchasing holds no price, T28). The port carries no authority: its consumer (Logistics's operations work list, L-09) measures its own name. Evidence: `PurchaseOpenLineReadPortTests`, `LogisticsOperationsWorkSqlServerRelationalAcceptanceTests`.
