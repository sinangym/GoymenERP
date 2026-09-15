# Sales reference read integration package

Owner: existing Sales backend. Base main cb11d5de6f718f3f1389f41628b543ba0581ad7f.
Business: #254 at6f0cd10259aeb2a44d32a92535464ed2ce096cf7 sections2-4.
Consumer: FE99ee918, src/frontend/goymen-erp-web/src/sales/referenceSource.ts
and orderDraftForm.ts. Module contracts and the handler live in Modules/Sales.
#262 also wires the three GET routes, the DI factory, OpenAPI and the Counterparty
consumer adapter in central API files (SalesEndpoints.cs, Program.cs, the OpenAPI
contract); later changes to those files belong to the sole central writer. No
capability, permission or role is registered.

## Actual owner connections

ISalesReferenceReadService is implemented by SalesReferenceReadHandler. DI inputs
are existing ICompanyContext, ICurrentUser, IErpAuthorizationService,
ISalesAccountSelectionSourceV1, ISettingsPrerequisiteProvider and nullable
IProductSalesEligibilityV1, plus SalesReferenceReadBindings. Nullable Product
dependency explicitly represents its absent runtime, not a permissive fallback.

Bindings are server composition values from accepted Identity/Settings sources.
No accepted Sales selector permission or money-operation name exists at this base.
Default bindings therefore deny before owner reads. UI-proposed sales.drafts.* IDs
are not permissions. Central must not pass those IDs as assumed accepted policy.
No permission/role/operation is registered by this package. A controlled binding
in a test is not a user grant. No global Product permission is borrowed.

Accounts use a Sales-owned consumer boundary implemented by central API over
real ICounterpartyReadService.GetAuthorizedPageAsync. Direct Sales-to-Counterparty
ProjectReference is deliberately absent: the existing guard allows only API's
exact two composition-root edges. No guard/allowlist is expanded. The adapter
receives server-selected CompanyId/ActorId/SessionBinding/Q/PageSize/Cursor, verifies
context matches, and maps owner result without catalogue caching or table writes.
It returns id/code/kind/legal/trade/active/version and visible account
id/company/roles/active/version using invariant string versions. Adapter queries
GetAuthorizedPageAsync with authenticated actor and server
session binding, active Party/account filters and roles=None. Roles0 is valid.
Response projection includes only active selected-company accounts and labels
from owner Code/LegalName. Owner cursor is forwarded unchanged; it retains its
owner company/actor/session/query protection. No new cursor codec or count is made.
A filtered page may be empty with nextCursor present; continue paging normally.
Provider failure is never an empty200. No read creates a Party/account/link.

Currency resolution calls GetMoneyProfileAsync(currencyId, accepted operation,
effectiveOn). Label is the actual CurrencyCode: the provider has no currency name.
Product resolution calls EvaluateAsync with exactly ProductId,
ExpectedProductVersion, OrderUnitId, EffectiveOn. A nonnull owner snapshot with
no errors is necessary. Stock/sales/order Settings profiles are each reread and
matched by company, unit, profile ID, invariant string version and effective date.
Only the validated selected order unit is returned, labelled with owner UnitCode.
No conversion, kg assumption, Active/Draft inference or candidate enumeration.

Product owner outcomes map as follows. An owner field error with Product's
published `version_conflict` or `lifecycle_conflict` code, or a
ProductOperationException 409, is 409 product_selection_changed: the known
selection is stale, so this wins over other field errors in the same result. Any
other owner field error, with or without a snapshot, ProductValidationException
and ProductOperationException 400/422 are 422 unsupported_product.
ProductOperationException 401/403/404 keep their status without a code, and
UnauthorizedAccessException is 403. A missing runtime, a result with neither
snapshot nor errors, and every other owner status are 503. A snapshot that differs
from the requested product, version, order unit or date is 409. An inactive or
out-of-range unit profile is 422 unsupported_unit; a unit binding or profile of
another company is 403. Product.Domain's ProductVersionConflictException is not
caught: Sales code uses no Product.Domain type, the eligibility port reports
conflicts as field errors, and a Domain exception crossing that Application port
stays a platform-redacted 500.

Every operation checks server company/user/current permission before reads and
again after awaited successful reads. Changed company/user or permission loss
discards the response. No query/body companyId or client actor is trusted.

## Implemented HTTP boundary

| Request | Handler | Success body |
|---|---|---|
| GET /api/v1/sales/references/accounts?q=...&pageSize=50&cursor=... | GetAccountsAsync(query, serverSessionBinding, ct) | SalesAccountOptionsV1 |
| GET /api/v1/sales/references/currency?currencyId=...&effectiveOn=2026-09-13 | GetCurrencyAsync(query, ct) | SalesCurrencyReferenceV1 |
| GET /api/v1/sales/references/product?productId=...&expectedProductVersion=7&orderUnitId=...&effectiveOn=2026-09-13 | GetProductAsync(query, ct) | SalesProductReferenceV1 |

These routes are implemented in SalesEndpoints.cs; they are not catalogue listings.
Status 200 serializes result.Value only, never the internal result wrapper. Every
other status is ProblemDetails with no value, and each route's allowlist decides
which handler code reaches the client:

| Route | Public statuses | Public codes |
|---|---|---|
| accounts | 400, 401, 403, 503 | validation_failed, invalid_cursor |
| currency | 400, 401, 403, 422, 503 | validation_failed, unsupported_currency |
| product | 400, 401, 403, 404, 409, 422, 503 | validation_failed, product_selection_changed, unsupported_product, unsupported_unit |

The platform writer owns 401/403/500/503 bodies and replaces any handler code with
authentication_required, forbidden, internal_error or service_unavailable. So
settings_access_denied, settings_provider_unavailable, settings_operation_unavailable,
product_provider_unavailable, counterparty_provider_unavailable and
reference_provider_invalid are internal handler codes: the client sees forbidden
(403) or service_unavailable (503), and 403 still differs from 503. A code outside a
route allowlist is dropped; a status outside it fails as a redacted 500.
Anonymous 401; unresolved/foreign company or denied/missing binding 403; invalid
IDs/query 400 validation_failed; invalid cursor 400 invalid_cursor. Cancellation
propagates. No provider internals or arbitrary error text leak.

Parser: strict UUID and YYYY-MM-DD dates, required product version/unit,
pageSize1-100, q up to200 UTF-16 code units, cursor up to8192; reject
unknown/repeated query fields. The handler rechecks the same q and cursor bounds.
Accounts q is forwarded as received. Normalization and a 200-rune limit are the
central writer's separate item-5 change (7063843); when it lands, the handler's own
q bound must count runes too, or a search of at most 200 runes but more than 200
UTF-16 units, which the route then accepts, is refused with 400. Context/company
header and current request/session follow the existing
platform boundary. The route handlers set `Cache-Control: private, no-store` and
`Pragma: no-cache`. No side effects, idempotency key or mutation capability is
introduced for GET.

Physical examples: tests/GoymenERP.Sales.UnitTests/Examples/reference-http.json.
Example IDs, accepted test profiles and successful snapshots are synthetic only.
They explain the wire, not available production options or permissions. Error
examples carry only the public status and code; SalesReferenceHttpAcceptanceTests
compares them with responses of the real handler behind the routes.

## FE mapping and command revalidation

- accounts response.accounts maps partyId/companyAccountId/label directly to
  OrderDraftOptions.accounts; retain returned company and current request generation.
- Currency resolved currencyId/label can represent a previously owner-offered
  currency. It cannot bootstrap choices without an authorized enumeration source.
- Product resolved productId/productVersion/label/orderUnits maps the same fields
  of OrderDraftOptions.products. Query requires an already known owner candidate
  and offered unit. Resolve is not product search or offered-unit discovery.
- Scope all results to committed company AND effectiveOn. Refetch on business-date
  changes; FE's current hook accepts only companyId, so date must become part of
  the real resolver's key. A blank business date must not become server today.
- Do not set the overall reference source ready merely because accounts loaded.
  Currency/product listing/runtime gaps keep the full create picker unavailable.
- Every create attempt, including retry, independently revalidates current actor,
  selected company/account activity/version, Product selection and real Settings
  profiles inside the command consistency boundary. These GET results authorize
  no write and do not implement durable snapshots/audit/transaction/idempotency.

## Exact missing sources, for current owners

1. Accepted Identity selector permission mappings and Settings Sales money-operation
   identifier. Missing mapping is default-deny, not a request for a guessed policy.
2. Settings currency enumeration and currency display name; unit label currently
   only UnitCode. Known-ID profile ports do not provide a picker catalogue.
3. Product authorized operational candidate/offered-unit enumeration and concrete
   IProductSalesEligibilityV1 runtime. Existing IProductService reads require global
   authorization and cannot be a company-user selector permission workaround.
4. Real effective unit/currency profiles and the Product owner's family/lifecycle
   prerequisites. Existing empty errors/profile adapter are not proof of usability.

No second owner catalogue/port, table read/write or fabricated options were added.

## Actual targeted evidence

Earlier state: after the consumer adapter boundary correction the then 18 handler
tests passed (exit0,108ms) and
Platform_non_counterparty_bypass_surfaces_match_the_reviewed_inventory passed
(1PASS,exit0). The CI failure at d2d024e remains evidence of the forbidden direct
Sales-to-Counterparty edge, not a waived guard.

#262 review patch items 1-4 and 6 (issuecomment-5667129884), LOCAL runs with
Windows dotnet.exe on the WSL worktree, not CI:

- Sales.UnitTests: 81 passed, 0 failed (37 in SalesReferenceReadHandlerTests).
  Against the handler without this change, 7 of them fail.
- Api.IntegrationTests filtered to SalesReferenceHttpAcceptanceTests: 25 passed,
  0 failed. Build-time OpenAPI document generation was switched off on the command
  line; no OpenAPI input changed.
- ArchitectureTests: 278 passed, 0 failed.
- Temporary mutations, each restored from git, turned named tests red: removing
  each new owner-outcome mapping, the errors-with-snapshot rejection, the unit
  active check or the unit-binding company check; reading unit or money profiles
  with a default date, or the order unit three times; dropping the cursor in the
  handler or the adapter; skipping the adapter's post-read context check; mapping
  InvalidCursor to 503; rethrowing the owner's UnauthorizedAccessException; and
  restoring the old error example codes.

Tests use controlled doubles of real owner interfaces and test-host permission
bindings. This is not real owner data, SQL, browser or environment acceptance.
