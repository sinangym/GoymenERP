# Purchasing (PUR-1 … PUR-4a, goods receipt and AT-025 — the implemented slices of T08)

## ALC-S14 account contract v2 (source checkpoint)

`IPurchasingPartyAccountPortV2` extends the authorized Counterparty owner projection with its
actual `Tolling` flag while keeping v1 unchanged. It does not assign roles. B-16's payment-terms
item is governed by ALS-D24/D28: terms are chosen per order, never a party-level default or an
implied zero-day term. `PaymentTermsScope = OrderSpecific` makes that distinction explicit.
The caller/company fences and exact party-account ownership apply to both versions. Unit and
real SQL test sources cover role combinations, fresh stored changes and foreign-account refusal;
runtime acceptance is pending the final train verification. V2 host registration is recorded in
the W8 integration handoff.

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
  **A1-2b — `Sales` closed for new lines until MOD-03 (Sales SAT-D219, SAT-D220; fail-closed):** a new line with
  `source.kind` `Sales` is refused before any owner read with 422 `validation_failed`, field `lines[i].source` =
  `purchasing_request_source_sales_unavailable` (no attempt recorded, so the same key may be retried with a corrected
  body). In the domain the rule lives only in the new-line factory (`PurchaseRequestLineSource.ForNewLine` /
  `IsOpenForNewLines`), which the service uses for the lines it creates; `PurchaseRequest.AddLine` itself accepts any
  defined source, because the store rebuilds stored lines through it with `Of`, so a `Sales` line recorded before
  A1-2b stays readable. `Sales` is still a well-formed
  kind on the wire (strict JSON unchanged); `Stock`, `Production` and manual lines, their content hashes and the OpenAPI
  contract are unchanged; no migration. MOD-03 reopens it only together with reading `ISalesOrderLineReferencePortV1`
  (refusing every sales order line that is not `Approved`) and wiring the `ISalesOrderLinkedSupplyReadPortV1` adaptor in
  the same delivery — a link without the adaptor reads as `Unreadable` in Sales and blocks its cancellation/removal.
- **Order creation:** an order is created with or without a source request (`CreatePurchaseOrderRequest.SourceRequestId`
  is optional); when given, the request must be **Approved** (A1-1, ALS-D2/ALS-D16; 409 `purchasing_request_state`,
  field `sourceRequestId` = `not_approved`) and of the same company (`PurchasingService.ReadSourceRequestAsync`). An
  order opened for a request is approved only while that request is still Approved (store code
  `purchasing_source_request_not_approved`, answered as a terminal 409 `purchasing_request_state`), so a rejected
  request's draft order never becomes a commitment.
- **Order remainder cancellation (A1-3, plan API-10/UI-05; No.161 §2, Sinan 2026-10-27):** `POST orders/{id}/cancellation`
  (Approved → **Cancelled**, a terminal state) cancels the **whole** not-yet-accepted remainder of every line — even after a
  partial acceptance; there is no line- or quantity-level partial cancel — with `expectedVersion` and a required `reason`
  (≤ 4000 characters, never cut; 422 `purchasing_order_cancellation_reason_required` / `_too_long`) under its own name
  `purchasing.orders.cancel`. Each line's remaining becomes its cancelled share (`Cancelled`), so remaining =
  committed − accepted − cancelled (master INV-006); the accepted shares, the lines and the source request binding stay;
  nothing physical or financial is recorded (no stock, receipt or money event). Terminal refusals (§7): stale version,
  `purchasing_order_state` (field `status` = `purchasing_order_not_cancellable`: a Draft or an already Cancelled order)
  and `purchasing_order_nothing_to_cancel` (every line fully accepted; a cancellation that cancels nothing is not
  recorded). `GET orders/{id}/cancel-impact` (same name, reads only) previews the remainder to cancel per line, whether a
  partial acceptance exists and the blocker the command would answer. A cancelled order takes no further acceptance share
  or breakdown (`purchasing_order_cancelled`; the receipt participant answers `purchasing_acceptance_order_state_conflict`);
  `IPurchaseSupplyReferencePortV1` answers it `NotApproved` with status `Cancelled` and `IPurchaseOpenLineReadPortV1`
  lists none of its lines. It no longer blocks the source request's rejection or withdrawal (No.161 §1 counts only
  APPROVED bound orders). **ALS-D66…D68 (EK10, Sinan 2026-11-03):** a cancelled order keeps counting with its full ordered
  share in its request line's Σ-shares, so cancelling does not re-open the need for another supplier — a new request is
  opened instead (ALS-D68); the cancellation is one step with a reason and an audit record, with no second approval and no
  maker ≠ checker rule (ALS-D67); the name is proposed to the ALS-D53 approvers (Satın Alma Müdürü, Genel Müdür, Admin;
  ALS-D66) and stays registered only — the grant is a human gate (the PUR01 browser suite grants it to the SENTETIK
  approver). Migration `20261103053923_AddPurchaseOrderCancellation` adds
  `CancelledBy` / `CancelledAtUtc` / `CancellationReason`, `CK_PurchaseOrders_Cancellation`, `Status` 3 and the line's
  `Cancelled` (decimal(18,3), default 0) inside `CK_PurchaseOrderLines_Shares`; its Down refuses (`THROW 51168`) while any
  order is Cancelled or any line carries a cancelled share. The P-03-DETAIL screen shows the "İptal" column, the recorded
  cancellation and the "Kalanı iptal et" panel (impact preview, reason, unknown-result resend of the same key).
- **Order line price (A2a-1, plan TEK-01/API-20; ALS-D5, ALS-D18, ALS-D51/D58/D60, PRS-02):** `POST orders` takes an
  optional `currencyId` (the order's one currency) and, per line, an optional `price` (`unitPrice`, optional `priceUnitId`
  defaulting to the line's unit, and at most one of `discountPercent` / `discountAmount`), all exact decimal text. The
  currency is verified through the existing `ISettingsPrerequisiteProvider.GetMoneyProfileAsync` for the operation key
  `purchasing.order.v1` (`PurchasingMoneyOperationsV1`) on the order's business date and recorded as a snapshot
  (`CurrencyId`, `CurrencyCode`, `MoneyProfileId`, `MoneyProfileVersion`, `MoneyScale`, and since A2a-1c `UnitPriceScale`).
  Since A2a-1b the approved Settings money catalog answers `purchasing.order.v1` (ALS-D69, within ALS-D63's narrow exception)
  for GDC, GSP and GIN with TRY: amount scale 2 and unit-price scale 4 (ALS-D72), so a priced order opens in TRY;
  `GET references/currencies` (under `purchasing.orders.create`) lists TRY. A currency without an approved profile is
  `unsupported_currency` (422); an owner that answers unavailable is still a 503, and an unpriced order never asks.
  Rules (domain, 422 on `lines[i].price`): a currency is required (`purchasing_price_currency_required`); the price is never negative
  (`purchasing_price_negative`; an explicit 0 is a price with a 0 line amount, apart from no price = "fiyat bekliyor", ALS-D18 —
  Sinan #66 6704170096; on a 0 price a discount amount above 0 is refused as exceeding the price); the price unit is the line's entry
  unit, because the Product conversion port is deferred (`purchasing_price_unit_conversion_unavailable`); the unit price and a
  discount amount carry at most min(unit-price scale — or, without one, the amount scale — and 6) decimals (4 for TRY, see below), never rounded (`purchasing_price_scale_exceeded`); a discount is a
  percent 0..100 (≤ 6 decimals) or an amount 0..unit price, never both. No minimum price, margin or discount ceiling is decided for
  purchasing and none is invented. The net unit price (exact, SAT-D76) and the line amount (ordered quantity × net, rounded once
  half away from zero to the profile's scale, SAT-D11) come from the shared `BuildingBlocks/Monetary/CommercialLinePriceCalculator`
  (the Sales rules, written once for the new consumer; the Sales move is a TEK-03 hand-off). A technical slice carries no price
  (its quantity is priced on its general line). Prices are written once with the line (a later price is a revision, A2b) and
  are audited with the order. An unpriced order is approved and received as usual (ALS-D18) and reads `pricePending: true`.
  **PRS-02:** every order detail (read and command answer) carries `priceDisclosure`; prices are `Visible` only for a caller with
  the new, registered-only name `purchasing.orders.price.read` (company-wide, not critical, granted to nobody; Sinan #66 6704170096
  names its holders for the persona card: Satın Alma Uzmanı/Müdürü, Genel Müdür, Admin, Muhasebe, Muhasebe Müdürü, Satış Destek,
  Satış Uzmanı — not the Sevkiyatçı), otherwise every
  price field is null beside `Withheld` (the persona card's Sevkiyatçı holds `purchasing.orders.read` and "fiyatları göremez").
  The goods-receipt reads and both supply ports carry no price. Migration `20261107063820_AddPurchaseOrderLinePrice` adds the
  nullable columns, `CK_PurchaseOrders_Money` (all five money facts or none) and `CK_PurchaseOrderLines_Price` (unpriced, or a
  general line with a price ≥ 0 and at most one bounded discount); its Down refuses (`THROW 51169`) while any order has a
  currency or any line a price. P-04 gains the optional price fieldset (currency, unit price, discount; the net and the amount
  are shown only after the save), P-03-DETAIL the price columns, "fiyat bekliyor" and the withheld notice.
- **Unit-price scale (A2a-1b, A2a-1c; ALS-D69, ALS-D72):** since A2a-1b the approved Settings money catalog answers
  `purchasing.order.v1` (GDC, GSP, GIN; TRY, amount scale 2) with `MoneyProfileV1.UnitPriceScale` 4. A2a-1c snapshots that scale with the order (`UnitPriceScale`, nullable) and holds a unit price and a discount
  amount to min(unit-price scale, 6) decimals — 4 for TRY (`41.2575`; `10.12345` is `purchasing_price_scale_exceeded`). Without a
  unit-price scale (a profile that answers none, or an order recorded before A2a-1c) the A2a-1 rule stays: min(amount scale, 6).
  The line amount is still rounded once to the amount scale (SAT-D11) and the net is exact (SAT-D76). An answered scale outside
  0..28 is `reference_provider_invalid`. The wire is unchanged (`currency.scale` stays the amount scale). Migration
  `20261107131321_AddPurchaseOrderUnitPriceScale` adds the column and `CK_PurchaseOrders_UnitPriceScale` (null, or beside a
  currency within 0..28; `CK_PurchaseOrders_Money` unchanged, no backfill); its Down refuses (`THROW 51170`) while any order has a
  unit-price scale.
- **Maturity surcharge, totals and "fiyat bekliyor" on the list (A2a-2, plan API-20; ALS-D38, ALS-D74…D77, EK13 #66
  6708738330 / 6708953113):** a line price may carry at most one maturity surcharge (vade farkı) — `deferralPercent` of the
  DISCOUNTED unit price (0..100, ≤ 6 decimals) or `deferralAmount` per unit (≥ 0, the unit-price scale) — added after the discount,
  as Sales does (ALS-D75 "Satış'la aynı"); never on a 0 price (`purchasing_deferral_on_zero_price`); two at once is
  `purchasing_deferral_kind_conflict`, a percent out of bounds `purchasing_deferral_percent_out_of_range`, a negative amount
  `purchasing_deferral_amount_negative` (all 422 on `lines[i].price`). A deferred price typed directly is a plain price with no flag
  (ALS-D76). The detail's price carries `deferralPercent` / `deferralAmount`; the net unit price includes the surcharge (exact).
  `GET orders/{id}/totals` (under `purchasing.orders.read`) answers the general lines' amounts and their sum, the VAT per line and its
  sum, and the gross amount through the shared `BuildingBlocks/Monetary/CommercialTaxTotalsCalculator` (the Sales totals rules —
  VAT per line rounded once, withholding from the rounded VAT, SAT-D75 — pinned to `SalesOrderTotalsCalculator` by a golden parity
  test; moving Sales onto it is a TEK-03 hand-off). The VAT rate is the item type's default from the existing Settings
  `ISettingsLineTaxResolverV1` for buyer situation `Normal` on the order's business date (ALS-D74; `Exempt`/`ZeroRated` = 0 %); a
  product without an item type or default, or an unreadable port, makes that line's VAT `NotComputable` (with the Settings reason)
  and the document VAT `NotComputable` with the count — never guessed. VAT is computed on the line amount INCLUDING the surcharge
  (ALS-D77 "Vade farkı dahil hesaplansın"; it replaces ALS-D38's EK3 note). Purchase withholding follows Sales S5 exactly (ALS-D78, EK14, Sinan 2026-11-11 "Satış gibi hemen açılsın"; no
  Purchasing-only switch): the share is the item type's Settings withholding default (`NotApplied`/`Exempt` = none), taken from the
  ROUNDED line VAT (SAT-D75); the VUK 232 threshold is NOT a gate and is never read here. A share outside (0, 100] makes the line, and
  the document, `NotComputable` (`tax_source_invalid`; the document names the first line's reason), never none. Accountant confirmation
  and production activation stay human gates. Supplier exemptions are not modelled yet. An
  unpriced line makes the totals `Incomplete`. PRS-02: without `purchasing.orders.price.read` every state is `Withheld`, every amount
  null and no line is listed. The order list carries `pricePending` per item for a holder of the price read only (never written
  otherwise). Migration `20261107151838_AddPurchaseOrderLineDeferral` adds `DeferralPercent` decimal(9,6) and `DeferralAmount`
  decimal(24,6) and `CK_PurchaseOrderLines_Deferral` (none, or one in bounds beside a unit price above 0; `CK_PurchaseOrderLines_Price`
  unchanged, no backfill); its Down refuses (`THROW 51171`) while any line carries a surcharge.
- **Order approval separation of duties (A2a-5a, ALS-D3, Fable F-015):** the order's preparer is the create envelope's actor
  (`CreatedBy`; the store stamps it on the order and refuses a different one) and never approves the same order: the approval of
  the preparer is the terminal, recorded and replayed 409 `purchasing_order_same_actor` (field `actor`; ordinal — the database
  agrees in binary collation); an order whose preparer is unknown is never approved (409 `purchasing_order_maker_unknown`,
  fail-closed). The approval records who and when (`ApprovedBy`, `ApprovedAtUtc`) on the order, in the audit and on reload;
  an order approved before A2a-5a keeps no approver (none is invented, no backfill). P-03-DETAIL names both refusals in user
  words and re-reads the order. Migration `20261111182351_AddPurchaseOrderApproval` adds both columns and
  `CK_PurchaseOrders_Approval` (both or neither, only on Approved/Cancelled, approver ≠ `CreatedBy`); its Down refuses
  (`THROW 51172`) while any approver is recorded. The approval amount threshold (API-96) stays in A2a-5.
- **Order line delivery schedule (A3 API-30, ALS-D33, ALS-D21, master §7.3):** `POST /api/v1/purchasing/orders/{id}/lines/{lineId}/schedule`
  (`purchasing.orders.revise`, ALS-D94 / koordinatör aktarımı #66 6824883445 — the create name alone is 403; ADR-011 replay; the line id is in
  the replay identity) replaces a GENERAL line's termin dilimleri
  (hubvogal's "sıra") whole: 1..500 slices, each a distinct `sliceId`, a quantity above zero in the line's unit and a
  `deliveryDate`, numbered 1..n in the given order, summing exactly to the line's ordered quantity (422 `validation_failed` on
  `slices` with the rule code otherwise, so the ordered total never changes through API-30). Draft and Approved orders are
  scheduled; on an Approved order a termin-only change keeps it Approved and asks no re-approval (ALS-D94); a cancelled order (409
  `purchasing_order_state`) and a stale version (409 `version_conflict`) are terminal and replayed; an unknown line is a 404. Each
  call appends the new set as a revision at the next order version (append only, history kept) and books nothing. The detail's
  line carries `deliverySchedule` (the latest revision). Migration `20261112014446_AddPurchaseOrderLineDeliverySchedule` adds
  `purchasing.PurchaseOrderLineDeliverySlices` (bound to the line; checks on identity, quantity, slice number, version, text); its
  Down refuses (`THROW 51173`) while any slice exists. Delay flagging (API-31/32) and the schedule screen are not part of it.
- **Order line closure (API-11, ALS-D10/ALS-D12, F-017 §2/§3.6, ALS-D87):** `POST /api/v1/purchasing/orders/{id}/lines/{lineId}/closure`
  and `.../reopening` (`purchasing.orders.close`, registered only; body `{ expectedVersion, reason }`, reason required; ADR-011 replay;
  the line id is in the replay identity). Closing an APPROVED order's GENERAL line records its (and its technical slices')
  not-yet-accepted remainder as `closed`, a separate share from `cancelled`: remaining = committed − accepted − cancelled − closed.
  A cancelled or Draft order (409 `purchasing_order_state`), a line already closed / not closed (409 `purchasing_line_closure_state`),
  a line with nothing left (409 `purchasing_line_nothing_to_close`) and a stale version (409 `version_conflict`) are terminal and
  replayed; a technical slice id or a bad reason is a 422, an unknown line a 404. Each closure and reopening appends one
  `purchasing.PurchaseOrderLineClosureEvents` row at the order version it produced and one `purchasing.OutboxMessages` row
  (`purchasing.order-line.closed.v1`, payload `{ companyId, purchaseOrderId, purchaseOrderLineId, closed, version }`) in the same
  save; no dispatcher yet. The future-supply port carries `ClosedText` and subtracts it; the open-line port leaves a fully closed
  line out. Migration `AddPurchaseOrderLineClosure` adds the `Closed` column (default 0), the shares and closed-or-cancelled checks
  and both tables; its Down refuses (`THROW 51174`) while a closure, a message or a closed share exists. Releasing Inventory future
  allocations at closure (F17-4) and the ALS-D10 in-tolerance suggestion (OPEN-004 / API-95) are not part of it.
- **Order exchange rate (ALC-SC33; FNS-D73/D79/D80/D83/D91):** a foreign-currency order is opened with the automatic default
  rate, which the Settings resolver gives as the TCMB forex selling rate (SAT-D54/D170) for the creation business day. It is
  stored as a number with its kind and rate day, never recomputed from a later bulletin. If the resolver cannot answer, the
  order is still created with no rate. Before approval the preparer can change the rate with
  `POST /api/v1/purchasing/orders/{id}/exchange-rate`, giving the expected version, an optional reason and an idempotency key.
  Approval of a foreign-currency order needs a rate (409 `purchasing_order_exchange_rate_required`, recorded as a terminal refusal) and locks it (409 after
  approval). `GET …/{id}/exchange-rate` returns the recorded rate, and the screen never computes one. Each rate (the opening
  default and every change) appends one `purchasing.PurchaseOrderExchangeRateChanges` row at the order version it produced, and
  the audit record carries the old rate, the new rate and the reason. `IPurchaseOrderExchangeRateReadPortV1` answers `Rated`
  for an approved order that has a rate, and a legacy approved order without one still answers `ExchangeRateNotSpecified`.
  Migration `AddPurchaseOrderExchangeRate` adds the table; its Down refuses (`THROW 51833`) while any rate is recorded (forward
  fix). Not built yet: a rate revision after approval with re-approval, and the frontend.
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
- **HTTP (22 routes):** requests — create, list, detail, `submission`, `approval`, `rejection`, `withdrawal`; orders — create, list, detail, `approval`,
  `cancel-impact`, `cancellation` (A1-3), line `closure` and `reopening` (API-11); `references/currencies` (A2a-1);
  goods receipts — record (`POST orders/{id}/lines/{lineId}/goods-receipts`), options, list, detail,
  quantity comparison (`PurchasingEndpoints.cs`); product candidates (`PurchasingProductCandidateEndpoints.cs`).
- **Permissions (10):** `purchasing.orders.create`, `.read`, `.approve`, `purchasing.goods-receipts.record`,
  `purchasing.requests.approve` (A1-1), `purchasing.requests.withdraw` (A1-1b), `purchasing.orders.cancel` (A1-3),
  `purchasing.orders.price.read` (A2a-1, disclosure only), `purchasing.orders.revise` (A3 API-30 termin change, ALS-D94; since
  F-027 UI-07 the `purchasing.orders.line-schedule.write` detail child), `purchasing.orders.close` (API-11 line closure; no shell
  control yet) (`PurchasingPermissionManifestV1`,
  `ErpPermissions`; company-wide, not critical, registered only).
- **Shell and screens:** `PurchasingShellCapabilityProvider` (owner contract `PURCHASING/1.8`) declares P-01 request
  list/new/detail (+ submit, since A1-1 the `purchasing.requests.approve` decision child and since A1-1b the
  `purchasing.requests.withdraw` withdrawal child), P-03 order list, **P-04 new order**, P-03-DETAIL (+ approve, goods-receipt
  and, since A1-3, the `purchasing.orders.cancel` remainder cancellation children; since F-027 the `purchasing.orders.line-schedule.write`
  line termin child, measured with read + `purchasing.orders.revise`), all `SingleCompany` and `ImplementationAvailable`. Frontend `src/frontend/goymen-erp-web/src/purchasing/`:
  `PurchaseRequestListPage`, `PurchaseRequestCreatePage`, `PurchaseRequestDetailPage` (+ `PurchaseRequestDecisionPanel`:
  approve / reject with reason, unknown-result resend of the same key; `PurchaseRequestWithdrawalPanel`: "Geri çek"
  with reason, shown only to the recorded preparer or submitter), `PurchaseOrderListPage`,
  `PurchaseOrderCreatePage`, `PurchaseOrderDetailPage`, `GoodsReceiptPanel`, `GoodsReceiptDetail`,
  `GoodsReceiptQuantityComparison` (routes in `purchasingRoutes.tsx`).
- **Ports out:** `IPurchaseSupplyReferencePortV1` (expected supply, never stock), `IPurchaseOpenLineReadPortV1`
  (AT-025, below), `IPurchaseAcceptanceSourceV1`; the Document Vault reads purchase-order ownership through the host
  adapter `GoymenERP.Api/DocumentsRuntime/PurchasingDocumentOwnerLinkPort.cs`.
- **Still absent:** tax/withholding, the informational exchange rate (A2a-KUR), payment plan, totals, expense lines and the
  Settings lists (later A2a slices), the order approval amount threshold (API-96, A2a-5),
  revision, line closure after a short delivery (API-11, ALS-D10/D12; A3), delivery schedule and "in transit", service lines and service acceptance, outgoing
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
- No tax or exchange rate (later A2a slices); no approval policy (`Approve` is a transition; §8). *(A2a-1 added the manual line price, the discount and the order's currency; see "Current state".)* *(A2a-5a added the preparer ≠ approver rule; the amount threshold is still absent.)*
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

## A4 / MOD-03 — sales-order-linked supply (SAT-D219/SAT-D220)

`IPurchaseSalesLinkedSupplyReadPortV1` (Contracts) / `PurchaseSalesLinkedSupplyReadPort` (Infrastructure, registered in `AddPurchasingModule`): the context company's open purchase order lines whose request line came from a given sales order (through the `PurchaseRequestLines` source index), optionally narrowed to sales order line ids (at most 500). `OpenQuantity` is the remaining quantity: a general line is ordered − technical − accepted − cancelled, technical slices add ordered − accepted − cancelled, and a technical line carries its general line's request line so it is never listed or counted twice. Purchasing does not reference Sales, so the Sales contract is adapted in the Api host (`PurchasingSalesLinkedSupplyHostPorts.cs`): `ISalesOrderLinkedSupplyReadPortV1` (Present / None / Unreadable with `purchasing_linked_supply_unreadable`; any failure is Unreadable, fail-closed) and `ISalesOrderLinkedSupplyRecorderV1` (the notice that a source exists). The ports carry no authority and create nothing.

SAT-D220 (A4b): `PurchasingService.CreateRequestAsync` reads every Sales-sourced new line through `IPurchaseRequestSalesSourcePortV1` (host adapter over `ISalesOrderLineReferencePortV1.GetApprovedLineAsync`) before any product read: Approved is recorded; NotApproved / NotFound → 422 `validation_failed` with `lines[i].source` = `purchasing_request_source_sales_not_approved` / `_not_found`; owner failure → service code `purchasing_request_source_sales_unavailable` (on the wire a code-less 503, like every Purchasing 503); no adapter composed → 422 `purchasing_request_source_sales_unavailable` (still closed). Nothing is stored on a refusal. Evidence: `PurchasingServiceTests.RequestSource`. Real-SQL acceptance of the Present path of the linked-supply read is a follow-up.

## ALC-S5 / B-19 — return to supplier (SAT-D306, SAT-D392, SAT-D393, SAT-D396)

`IPurchaseSupplierReturnServiceV1` records a return of one received label to the supplier. The exit cost is the order line's
invoice unit cost when `IPurchaseSupplierReturnInvoiceCostReadPortV1` answers (SAT-D392; no reader is registered until supplier
invoices exist, so every return takes the exception path today). Otherwise a manual unit cost waits as `PendingCostException`
and no stock leaves (SAT-D393) until a holder of `purchasing.orders.approve` approves it (SAT-D396); a rejection needs a reason.
The stock exit is staged through Inventory's `InventorySupplierReturnExitParticipant` in the host's one transaction
(`PurchaseSupplierReturnHost`). Same-person decision is `PurchaseSupplierReturnPolicyV1.AllowSameActorCostExceptionDecision`
(default `false`; NEEDS-SINAN), not a CHECK. Replacement returns (ALS-D91) answer 422 in this version. The new permission
`purchasing.supplier-returns.record` is registered without any grant.

ALS-D116 and W5-5.1 register `purchasing.goods-receipts.approve-overdelivery` separately
from order approval and receipt variance approval. It requires company-wide scope and is
not Critical. This registers a name only; the overdelivery decision workflow and its
acceptance remain separate work. No role, persona or user is granted the permission.

Migration `20261116113129_AddPurchaseSupplierReturns` creates `PurchaseSupplierReturns` and `PurchaseSupplierReturnAttempts`;
Down guard **51893** refuses while any return or attempt exists (forward fix). API-44 receipt reversal is not in this slice.
# W8 receipt adjustment migrations (ALS-D86/D91/D92/D93)

The forward-fix guards run before any schema drop. `20261116213347_AddPurchaseReceiptAdjustments`
uses **51949** for immutable receipt/cancellation history; its recorded-cancellation backfill awaits K3 review.
`20261116215806_AddPurchaseSupplierReturnResponseSnapshots` uses **51953** for original response snapshots.
`20261116222957_AddPurchaseReceiptAdjustmentBatches` uses **51956** for grouped receipt history or any outbox
cursor history. Batch identity, detail ordering and first response remain immutable. The outbox's fixed
`MIN_ACTIVE_ROWVERSION` read protects commit ordering and awaits K3 review; tenant filters remain in place.
Runtime SQL acceptance is not yet run; these migrations are generated source, not deployment evidence.

## KIM-S19 / N-12 purchase-order numbering

KIM-D53 and the later decision replacing ALS-D36 include purchase orders in prefix/year/annual-sequence numbering.
New orders require the company's explicitly configured Settings series; missing configuration fails closed rather
than inventing a prefix. The real Settings owner reserves the number within the order's Serializable transaction.
The number, format version and sequence are immutable order facts. Replay preserves the stored number.
Cancellation records the Settings gap in the same transaction and does not release that number for reuse.
Existing orders remain unnumbered; no historical backfill or artificial document number is produced.
The EF-generated `20261117002600_AddPurchaseOrderDocumentNumbers` additive nullable snapshot has a company-scoped
filtered unique index and an all-or-none check; its Up contains only DDL and does not backfill old orders.
Down starts with **THROW 51957** while any numbering fact exists. SQL test sources cover annual reset, replay,
missing configuration, cancellation/non-reuse, and permission loss after save rolling back the entire unit.
The direct-store fixtures use the real Settings/Audit owners with explicitly synthetic fixture authority; HTTP
authorization remains real. Runtime SQL/HTTP/Unit/Vitest/CI acceptance is **NOT_RUN**.

## ALC-SC47 / ALS-D112 and ALS-D115 supplier revision

An approved order can change supplier through the existing order-revision permission only when no receipt history
or invoice binding exists. The supplier and company account are selected again through the actual Cari owner.
The new revision returns to Draft, clears current prices, preserves original prices and approval in an append-only
history, and requires price entry and another actor's approval. The original order creator is never overwritten.
The order, attempt and audit commit together; company and authority are checked again before and after save.
The invoice owner must record immutable order-line bindings under the shared order-line locks when recording invoices;
this binding table alone does not implement incoming invoice production. Legacy orders without a money snapshot
fail closed before changing supplier or approval, because required replacement prices cannot be entered without it.
No historical currency/profile is guessed; that missing legacy workflow remains separate work.
EF-generated `20261117072202_AddPurchaseSupplierRevisions` adds nullable revision history and the tenant-filtered
invoice binding table. Down starts with **THROW 51977** while either history exists. No backfill, grant or database
application occurs in this slice. Test sources cover replay, maker/checker, tenant isolation, historical restoration,
post-save authority loss rollback and the Down guard. Runtime acceptance is **NOT_RUN**.

## FIN-S13 / ALC-SC47 incoming invoice owner prerequisite

ALS-D7/D8/D14/D15/D41/D44/D59/D64: the Purchasing-owned incoming invoice preserves official source identity,
amounts, rate and installments; matching records actual receipt/order bindings, both favorable and unfavorable
variances and the exact Settings policy version. Approval cannot come from the preparer. Finalization appends one
`purchasing.invoice.finalized.v1` event and freezes one Finance admission identity. Finance acknowledgement is a
separate revision and cannot change the finalized payload. No Finance tables are written from Purchasing.
Receipt shares require exact storage scale; no quantity rounding is allowed. Matching takes all affected general-line
locks in one global order. Writes reject ambient/borrowed transactions: Purchasing commits independently before
the ALS-D64 Finance call; a failed save cannot leave partial invoice writes inside another owner's transaction.
EF generated `20261117084001_AddPurchaseIncomingInvoices`; Up is only DDL, Down first refuses with **THROW 51980**
while invoice identity, history, receipt bindings or terminal attempts exist. The migration was not applied.
This checkpoint is PARTIAL: actual import/document readers, Settings policy composition, receipt comparison facts,
authorization services/HTTP/UI and the separate Finance admission/retry still need wiring. Service/additional-cost
invoice lines need their own real owner references; no artificial goods receipt substitutes for them.
Unit/model and real SQL test sources cover immutable source/history, tenant isolation, replay, actor/payload conflicts,
quantity scale, late authority loss, transaction ownership, one outbox event and the forward-fix guard. Runtime NOT_RUN.

## ALC-S8 / ALS-D23, ALS-D27, ALS-D80 and ALS-D107 additional expenses

An immutable commercial expense references an actual company-scoped goods or service order and optionally its
actual acceptance/line. Separate composite foreign keys preserve each owner's real acceptance identity. The Cari
may be the order supplier or another active Supplier. Actual Settings expense types, effective VAT list and money
profile/version are retained; an empty active expense catalog uses the required description, while a nonempty active
catalog requires an explicit current type. Historical untyped facts are not rewritten. Shared tax calculation provides
the frozen VAT/gross amounts. No quantity, Inventory movement, average cost or cost allocation is produced.
The direct ALC-S8 answer (Issue #1189, 6946921285) selects existing `purchasing.orders.revise` for recording;
price-read and company-wide authority are revalidated. No permission grant is added. Its command owns a clean
Serializable transaction, expected source order version, idempotency, terminal source conflict replay and actual
Audit append; failure rolls back all rows. Reads remain company/actor/price fenced.
EF generated `20261117114517_AddPurchaseAdditionalCosts` with additive DDL. Down first refuses with **THROW 51998**
while expense facts or command attempts exist; an empty store can roll back. No DB migration was applied.
Partial invoice binding was approved separately (6947000365) and is the next source followup; no artificial receipt
or physical quantity substitutes for a monetary expense share. Runtime acceptance remains NOT_RUN.

## ALC-S7 / ALS-D29, ALS-D40 and KIM-D49 service orders

Service purchase and outgoing own-material subcontract share actual Settings service types and units.
Cari supplies the active Supplier account and, for subcontract, its Tolling role. Product and Inventory prove
material/output identities; customer-owned labels cannot authorize an outgoing own-material order.
The commercial order reserves its actual Settings purchase-order number in its own Serializable transaction.
Approval keeps the existing purchase maker/checker boundary; actual service acceptance is append-only and creates
no goods receipt or stock movement. API72 exposes price-free operational order facts to trusted in-process consumers.
Service invoice matching binds actual service acceptances, under the shared line locks, in its own table with
company-scoped composite foreign keys. Normal goods receipt foreign keys remain intact. Typed ServiceTypeId is
additive and omitted when null, preserving legacy goods JSON. Service lines never enter material-cost order bindings.
EF generated 20261117110054_AddPurchaseServiceOrders; Up is additive DDL. Down starts with THROW 51992 while
any service order, revision, acceptance, invoice binding or terminal attempt exists. No migration was applied.
Unit/model, HTTP, real SQL, strict transport and UI regression sources are written. API, Purchasing.UnitTests,
Api.IntegrationTests and frontend tsc source builds passed; all runtime/SQL/Vitest/browser/CI evidence is NOT_RUN.
ALS-D18/D9 later price entry creates a new draft snapshot and requires another actor's reapproval. Actual service
acceptance, quantities, identities and the old approval/price revisions remain intact; price-only reapproval does
not repeat or reinterpret a past physical operation. FNS/Inventory facts are untouched.
ALS-D106 outgoing subcontract price units require the actual kilogram unit profile. ALS-D105 defaults read only
the actual Purchasing-side Settings tax source and preserve its version per line; no default means 503, never
zero VAT, a Sales default or an assumed tax code. Purchasing tax-default administration and line overrides remain
closed pending the explicit permission decision. Central Program/shell/OpenAPI/inventory composition is pending.
# FIN-S13 / ALS-D7 — alış faturası dosyası önizlemesi

XML/UBL 2.1, açık sütun eşlemeli CSV ve XLSX okuyucuları aynı kaynak sözleşmesini
üretir; genel tablo eşlemesi Mikro'nun yerel biçim adaptörü olarak adlandırılmaz.
Özgün tutar, para birimi, vergi kodu, tevkifat, kur amacı ve ödeme tarihi bilgileri
korunur; tanımsız veya fazla hassasiyetli değerler yuvarlanarak kabul edilmez.
Taranmış Documents sürümü/hash'i, şirket ve `purchasing.invoices.import` ile fiyat
okuma yetkisi doğrulanır. Önizleme muhasebe kaydı veya Finance olayı üretmez.

`20261117112436_AddPurchaseInvoiceImportReservations` EF üretimidir. Şirket kapsamlı,
denetlenen ve idempotent rezervasyon belge sahibini oluşturur; kayıtlar değişmezdir.
Down, kaynak kimliği varken önce `THROW 51996` ile durur. Birim, HTTP sözleşme,
kalıcılık modeli ve gerçek SQL test kaynakları vardır. Cari/ürün/birim/vergi ve
ödeme planı eşlemesiyle kayıt akışı tamamlanana kadar FIN-S13 PARTIAL kalır.


### EK24 FNS-D105 - finalized invoice outbox constraint forward fix

`20261119012425_AllowPurchaseIncomingInvoiceFinalizedOutboxEvents` was generated with the pinned EF tool.
It adds only the already contracted `purchasing.invoice.finalized.v1` event to `CK_OutboxMessages_EventType`;
the two existing order-line events remain valid and arbitrary event names remain refused.
Down first refuses with `THROW 51815` while finalized invoice outbox history exists, before restoring the prior check.
The migration does not dispatch an event, post a journal, grant access or activate production.
Persistence-model and real SQL guard tests accompany the existing invoice finalization acceptance facts;
final CI and independent K3 acceptance remain required.

### AK-8b — atomic multi-receipt supplier return from another label (SAT-D433, SAT-D440–SAT-D445, K3-B2)

`20261122142515_AddPurchaseSupplierReturnLineageParts` was generated with the pinned EF tool. It adds two nullable
columns to `PurchaseSupplierReturns` — `LineagePartsJson` (the per-receipt parts of the ONE return document: receipt,
order line, exit movement, kg, pieces, the preparer's invoiced / invoice-pending choice, cost basis and unit cost) and
`PieceCount` — and `CK_PurchaseSupplierReturns_LineageParts` (both only with a source label). Earlier returns keep
both null. Down first refuses with `THROW 52072` while any return carries parts or a piece count (forward fix); an
empty store rolls back freely.

A return that names a source label is planned from Inventory's read port (lineage, SAT-D440/SAT-D442 distribution,
each receipt's open, invoiced and pending kilograms). Lineage mode writes one stock exit per receipt under one
Inventory command in the host transaction; quantity mode (SAT-D441/SAT-D444) takes the whole return from the chosen
receipt. `PurchaseSupplierReturnQuantityPriceRuleV1` is the single rule point of "the chosen order's unit price";
SAT-D451: it is the net price after the discount, without the deferral; a net price with more than four decimals is refused 422 (never rounded). The migration grants no access and
activates nothing; real-SQL acceptance is **NOT_RUN** and independent K3 acceptance remains required.

## AK-3b (adet/kg — alışı tamamlama)

- Migration `20261122141554_AddPurchaseGoodsReceiptBatchPieceFacts`: toplu mal kabulde adetli satır (batch `KilogramsPerUnit`, etiket `PieceCount`,
  varyans check'inin adet dalı). Down guard **52042** (forward fix): adetli toplu kabul varken geri alınmaz.
- ALS-D119/D120 ve SAT-D436/D439 anahtarları açık (`PurchaseCostGoldenAccepted`, `PurchaseKgPriceGoldenAccepted`). ALS-D122 anahtarı
  (`PurchaseInvoiceKilogramShare.PurchaseInvoiceRemainingShareGoldenAccepted`, Inventory; ALS-D123 güncel kalan ve ALS-D124 3 ondalık + son fatura kalanı alır aynı anahtarda) golden EK1 (E22–E29) ile AÇIK; kapalı durum testte parametreyle sabit — kapalıyken oransız iadeli
  adetli kabulün faturası `purchase_invoice_cost_piece_reversal_basis_unavailable` ile bekler.
- SAT-D452: gerçekleşmeye yalnız kesinleşmiş faturalar girer (`PurchaseInvoiceRealizationRule`).
- ASSUMPTION-AK3B-1 (geri alınabilir sunum ayrıntısı): sipariş gerçekleşme tablosunda sunucu yalnız gerçek kg fiyatı için neden kodu verir;
  diğer boş hücrelerin metni ekranda türetilir (`NotInvoiced` → "Henüz faturalanmadı", aksi halde alanın kendi nedeni).
- Toplu mal kabul gönderim formu ayrı dilimdir; bu dilimde yalnız arka uç ve okuma paneli vardır.
