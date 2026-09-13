# Sales contract test result

## Updated-fixture consumer checks, 2026-09-13

All commands use the same test project and `--nologo --verbosity minimal`.

First filter:
`FullyQualifiedName~SalesOrderDraftContractTests.Retry_preserves_line_identity_and_unknown_price|FullyQualifiedName~SalesOrderDraftContractTests.Response_requires_version_and_explicit_nullable_members|FullyQualifiedName~SalesOrderDraftContractTests.Create_replay_list_and_detail_examples_share_identity`

Actual exit0: 3 passed, 0 failed, 0 skipped; duration92ms. This exercises changed
retry list value comparison and updated create/replay/detail response fixtures.

Second filter selects the remaining request-fixture consumers without repeating
those three or the unchanged error examples:
`FullyQualifiedName~SalesOrderDraftContractTests&FullyQualifiedName!~Error_examples_include_unavailable_and_denied_separately&FullyQualifiedName!~Retry_preserves_line_identity_and_unknown_price&FullyQualifiedName!~Response_requires_version_and_explicit_nullable_members&FullyQualifiedName!~Create_replay_list_and_detail_examples_share_identity`

Actual exit0: 22 passed, 0 failed, 0 skipped; duration114ms.
Current changed-behavior evidence is12 new delivery/commercial cases plus these25
fixture consumer cases, each executed once for the current source/fixtures.
The7 unchanged error cases retain their earlier result; no fresh full44-test run
is claimed. No HTTP/SQL/browser/runtime acceptance is implied.

## Delivery-slice increment, 2026-09-13

Command: `dotnet test tests/GoymenERP.Sales.UnitTests/GoymenERP.Sales.UnitTests.csproj --nologo --verbosity minimal --filter FullyQualifiedName~SalesDraftDeliveryPlanTests`

Actual exit0: 12 passed, 0 failed, 0 skipped; reported duration231ms.
Both assemblies built. Only the new delivery/commercial tests were executed.
The earlier32 PASS below belongs to the previous contract state; it was not rerun
and is not presented as a combined44-test current-state result.

New evidence: 1000=400+600 accepted; 400+700 rejected; partial plan leaves200;
fractional and beyond-decimal exact arithmetic; foreign units and duplicate slice
IDs rejected; unknown/empty plans; create rejection of excess/invalid dates;
versioned commercial-reference shape; updated physical request/create/replay/detail
examples decode; slice identity retained through retry serialization.
Existing retry equality assertion now compares serialized line values because
the added slice list has reference equality in a positional record.

No profile-scale approval, owner reference eligibility, delivery/stock/production
write, SQL or browser acceptance was performed.

## Earlier base contract execution

Run date: 2026-09-13, local .NET 10.

Command: `dotnet test tests/GoymenERP.Sales.UnitTests/GoymenERP.Sales.UnitTests.csproj --nologo --verbosity minimal`

Initial execution: build failed with CS1737 in SalesDraftContractsV1.cs:13.
No tests ran in that initial execution.

After source reconciliation and corrections: exit code 0.
Passed: 32. Failed: 0. Skipped: 0. Total: 32. Reported test duration: 328 ms.
Application and test assemblies both built; one test assembly was discovered.

Actual coverage: required/null create members, server-field injection rejection,
empty/null/duplicate lines, optional price and separate price unit, lexical quantity
errors, long decimal text retention, invalid calendar dates, response required
members, create/replay/list/detail example consistency, Product selection field
mapping, and seven error example variants including denied versus unavailable.

13 physical JSON files are input to these tests and copied to test output.
No baseline suites repeated. No Product/Settings runtime tests, real SQL, HTTP,
browser, migration, permission or production acceptance claimed.

Product/Settings mapping is schema preparation only. See
`src/backend/Modules/Sales/CONTRACT_V1.md` for source pins and unresolved integration
requirements, including provider availability, Identity mapping and price policy.
