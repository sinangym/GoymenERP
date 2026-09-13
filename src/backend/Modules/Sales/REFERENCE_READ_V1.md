# Sales reference read integration package

Owner: existing Sales backend. Base main cb11d5de6f718f3f1389f41628b543ba0581ad7f.
Business: #254 at6f0cd10259aeb2a44d32a92535464ed2ce096cf7 sections2-4.
Consumer: FE99ee918, src/frontend/goymen-erp-web/src/sales/referenceSource.ts
and orderDraftForm.ts. This package adds module contracts/handler only; central
Backend owns routes, DI, capability and OpenAPI. No shared file is edited here.

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

Every operation checks server company/user/current permission before reads and
again after awaited successful reads. Changed company/user or permission loss
discards the response. No query/body companyId or client actor is trusted.

## Proposed HTTP adapter, for the sole central writer

| Request | Handler | Success body |
|---|---|---|
| GET /api/v1/sales/references/accounts?q=...&pageSize=50&cursor=... | GetAccountsAsync(query, serverSessionBinding, ct) | SalesAccountOptionsV1 |
| GET /api/v1/sales/references/currency?currencyId=...&effectiveOn=2026-09-13 | GetCurrencyAsync(query, ct) | SalesCurrencyReferenceV1 |
| GET /api/v1/sales/references/product?productId=...&expectedProductVersion=7&orderUnitId=...&effectiveOn=2026-09-13 | GetProductAsync(query, ct) | SalesProductReferenceV1 |

These are proposed GET adapter paths, not implemented endpoints or catalogue-list
claims. Map result.StatusCode=200 to result.Value only, not the internal result
wrapper. Map non200 to existing platform ProblemDetails; Code is optional and
Value is null. Do not serialize a cached positive body for an error. Anonymous401;
unresolved/foreign company, denied/missing binding403 without required code;
invalid IDs/query400; invalid cursor400 invalid_cursor; owner-invalid selection422;
changed Product selection409; unavailable runtime/provider503. Settings denial
preserves settings_access_denied/403, unavailable preserves
settings_provider_unavailable/503. Unexpected exceptions remain platform-redacted
500; cancellation propagates. No provider internals or arbitrary error text leaks.

Central parser: strict UUID and YYYY-MM-DD dates, required product version/unit,
pageSize1-100, q up to200 characters, cursor up to8192; reject unknown/repeated
query fields. Context/company header and current request/session follow the existing
platform boundary. Response headers should prevent shared caching of these
company/actor-sensitive selections. No side effects, idempotency key or mutation
capability is introduced for GET.

Physical examples: tests/GoymenERP.Sales.UnitTests/Examples/reference-http.json.
Example IDs, accepted test profiles and successful snapshots are synthetic only.
They explain the wire, not available production options or permissions.

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

After the central-owner-directed consumer adapter boundary correction, the same
18 changed-handler tests passed (exit0,108ms). The exact failed architecture test
Platform_non_counterparty_bypass_surfaces_match_the_reviewed_inventory passed
separately (1PASS,exit0). The earlier CI failure at d2d024e remains evidence of
the forbidden direct Sales-to-Counterparty edge, not a waived guard. The API
adapter's actual query/HTTP behavior is still central integration work.

`dotnet test tests/GoymenERP.Sales.UnitTests/GoymenERP.Sales.UnitTests.csproj --nologo --verbosity minimal --filter FullyQualifiedName~SalesReferenceReadHandlerTests`

Actual18PASS/0FAIL/0SKIP,exit0,112ms. Tests use controlled doubles of real owner
interfaces: active roles0/company filtering, owner query binding, authentic empty
page versus forbidden/invalid-cursor/unavailable, anonymous/foreign/default-deny,
late company/permission changes, exact code/profile labels, absent operation,
inactive/expired currency, absent Product runtime/empty errors, three unit profile
revalidations, stale Product/profile versions and no global authorization fallback.
This is local handler behavior evidence, not real owner data or HTTP/browser/SQL.
