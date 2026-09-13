# Sales draft consumer contract preparation

Status: local DTO/decoder/example preparation, not an implemented HTTP API.
Business source: #254 `6f0cd10259aeb2a44d32a92535464ed2ce096cf7`, sections 2-5,
and issue #66 comment 5653637829.
Product source: #255 `089208647e5ae65aac12d50c5fef661f48ccacc1`.
Settings source: #256 `5e0c5502ff4d715f5f3e0579beb3a49f8e5b13e0`.

## Create and identity

POST `/api/v1/sales/drafts`, required `Idempotency-Key` header.
The JSON examples live in `tests/GoymenERP.Sales.UnitTests/Examples`.
All UUIDs, product names, versions and document numbers there are synthetic schema
examples. None proves an existing master, grant, valid profile or persisted order.

Required body: kind=`Order`, partyId, companyAccountId, businessDate, currencyId,
and 1-500 lines. Each line requires lineId, productId, expectedProductVersion,
orderUnitId and positive quantity text. Empty UUIDs and duplicate lineIds fail.
Client generates each line UUID once when adding the line; retries preserve it.
The server creates order UUID and company-scoped document number on first commit.
The sample number format is illustrative; consumers treat it as opaque display text.
Rev.0 is commercial revision, independent of string concurrency version. Create
does not accept version, companyId, actor, state or approval fields in its body.
Authenticated server scope is the only company/actor/session source.

Optional create fields: header requestedDeliveryDate/note and line unitPrice,
priceUnitId, requestedDeliveryDate/note. Missing and null both mean unknown on
create. A supplied price requires its separate priceUnitId; a price unit without
a price is rejected in this preparation. A literal zero is distinguishable from
unknown; shape acceptance does not approve a free-of-charge commercial policy.
Business and delivery dates are date-only, not UTC timestamps; no default today.
Notes have a 4000-character technical limit. Decimal and selected-version strings
have a 128-character technical bound. These are not owner scale/precision rules.
Decimal text is retained verbatim, never converted to floating point or rounded.

Response constructor members are all required, including explicit nulls for
nullable fields. Request optionality does not make response members optional.
The local strict JSON options reject unknown fields, wrong case and numeric
values substituted for decimal/version strings. Consumers must additionally
validate nonempty server IDs, positive version text, company/attempt binding,
kind/state/revision and line identities before treating a response as success.

Delivery slices and nullable commercial references are represented as detailed
below. Their real owner validation and value snapshots are still prerequisites
for a command implementation. No update/revision route is provided;
future update omission leaves values unchanged and nullable null clears only
the named field. Missing collections must not delete lines.

## Section 2 field reconciliation: delivery and commercial preparation

- Header requestedDeliveryDate and line requestedDeliveryDate are independent.
  Each line also has nullable requestedDeliverySlices. Each supplied slice requires
  client-owned stable sliceId, orderUnitId, positive quantity text and a date-only
  requestedDeliveryDate. Missing/null means unknown plan; empty means no planned
  slices. Neither means a delivery occurred. Maximum500 slices is a technical bound.
- SalesDraftDeliveryPlanV1 validates exact same-unit totals with integer coefficients
  and powers of ten. 1000=400+600 leaves0; 400+700 is rejected; 400+400 leaves200
  unplanned, never delivered. Different units are rejected without conversion.
  This local arithmetic has no default profile scale and does not approve a unit;
  current owner profile/scale validation remains required for every supplied value.
- deliveryAddress, contact and responsible are nullable versioned owner references.
  The actual provider must distinguish party address from company-account address
  and validate scope. No address or responsible person is auto-selected.
- paymentTerms, sourceStrategy and package are nullable versioned owner references.
  Missing/null is unknown, never cash terms, a physical lot, ownership or allocation.
  A supplied reference requires nonempty id and expectedVersion; this is only wire
  validation. Authorized owner value/version snapshots must be retained on write.
- Line discountPercent is nullable lossless decimal text; missing/null is unknown,
  not zero. No discount range, ordering, VAT, exchange rate, total or margin is
  inferred. A provided discount still needs accepted commercial policy validation.
- unitPrice remains nullable with separate priceUnitId. Settings positive-only
  parsing is used neither for nullable prices nor as proof of priced-write readiness.
- All new nullable detail members are explicitly present in response examples.
  Header/slice dates, client identities and selected owner versions survive retries.

Remaining implementation gaps are one owner-binding list, not removed scope:
authorized address/contact/responsible read; payment/source/package eligibility
and value snapshots; pricing/discount/unit policy; real unit/currency profiles;
current Sales/selector/price-read permissions. The DTO now represents these inputs,
but no synthetic owner ID or unknown value makes a real write valid.

## HTTP result and recovery pin for central integration

First commit: 201 with `SalesOrderDraftResponse`, isReplay=false, and Location
`/api/v1/sales/drafts/{id}`. Same accepted attempt: 200 with the original stored
detail/originalCorrelationId and isReplay=true, subject to current authorization.
`detail-response.json` is GET detail; `list-response.json` is GET collection.
No 204 is proposed. No amount totals or downstream financial/stock effects exist.

400 malformed/invalid shape; 401 unauthenticated; 403 denied; 404 absent or
invisible detail; 409 company/account state or same-key different-payload conflict;
422 invalid owner product/unit reference; 503 unavailable prerequisite provider.
Sales problems carry status/title, optional code/correlationId/errors. Field errors
carry field/code. Platform 401/403/404 need not include code or field errors.
Settings errors preserve `settings_provider_unavailable` versus
`settings_access_denied`; neither becomes an empty selection or success.
Examples validate wire decoding, not real HTTP status or business enforcement.

Unknown result keeps the same key, path, exact request, line IDs, actor, company
and session attempt. Never generate a replacement key after timeout or malformed
2xx. A 409 alone does not prove no write. A late A-company result must not display
success in B or unlock a new write. Server re-reads current authority even before
returning replay; authorization loss must not leak a stored response. Original
response financial fields require current amount-read permission; until redaction
is specified, deny the response rather than leak or turn hidden prices into null.

GET list takes pageSize (default50, valid1-100) and optional opaque cursor. Stable
ordering is createdAtUtc descending then id descending, scoped to authenticated
company. Cursor is server-protected and bound to company and ordering; invalid
or mismatched cursor returns400. No totalCount promise. nextCursor=null is end;
next-page failure must not erase prior rows. This is an integration requirement;
cursor codec/query and bounds enforcement are not implemented here.

## Existing owner ports, no replacement catalogues

For every line, map ProductSalesSelectionV1 exactly:
ProductId=line.ProductId, ExpectedProductVersion=line.ExpectedProductVersion,
OrderUnitId=line.OrderUnitId, EffectiveOn=request.BusinessDate.
Call existing IProductSalesEligibilityV1.EvaluateAsync in authenticated scope at
command time. Selection is not authorization. Missing snapshot or owner errors
cannot produce a usable line. Do not infer eligibility from Draft/Sellable/Complete.
Persist owner snapshot ProductId/ProductVersion/Code/Name/EffectiveOn and all
StockUnit/SalesUnit/OrderUnit CompanyId/UnitId/ProfileId/Version bindings inside
the same consistency boundary as the order, audit and durable command result.
The current DTO exposes the selected order unit/product snapshot only; storage
and a typed Product adapter remain central dependency integration work.

Use existing Settings ISettingsPrerequisiteProvider.GetUnitProfileAsync for the
actual order unit and GetMoneyProfileAsync for currency with BusinessDate and the
central owner's accepted Sales operation identifier. No local operation-policy
name is invented. Currency is mandatory even for an unpriced draft. Validate
company, IDs, active/effective range and profile identity/version; use returned
Scale with existing ExactPositiveDecimalV1.Parse for quantity. Never supply a
default scale or a fake kg profile. No Sales period requirement is inferred from
the existence of GetPeriodEligibilityAsync; draft records no financial event.

Concrete integration differences for central/Product owners:

- Product profile Version is string; Settings ProfileVersionV1.Version is long.
  Adapter must compare against invariant lossless string representation of the
  returned Settings version, never a JavaScript Number or guessed version.
- Settings ExactPositiveDecimalV1 excludes zero. It is suitable for positive
  quantity, not sufficient for nullable/nonnegative unit-price validation.
  A priced write still needs accepted pricing-unit and monetary policy validation.
- Exact Sales money operation identifier and Identity permission bindings remain
  unresolved. No runtime adapter or grant is supplied by these source pins.

Cari selection uses existing authorized Party read, then a separate company-account
active/version check. roles=0 is valid; no automatic account/link or Customer flag.
Organization supplies company/location context, not unit/currency registry.
List, detail, create, price-read and each owner selector need distinct current
authority checks with the existing Identity owner. No Sales permission names are
invented, no Product global grant is borrowed. Absent mappings default deny.
There is deliberately no fabricated Sales selection endpoint/second owner port.

## Evidence boundary

Local tests exercise strict request decoding, required/null semantics, duplicate
line rejection, retry serialization, lossless text retention, example consistency
and owner input field mapping. They do not run Product or Settings implementations,
test SQL atomicity/idempotency, authorize users, register endpoints or prove a
browser flow. Shared API/DI/OpenAPI/permissions/migrations remain central-owner
work. Successful old suites are not evidence for this new flow.
