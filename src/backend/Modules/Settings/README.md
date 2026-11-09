# Settings prerequisite contract V1

Source: DEC-SET-001; Master sections 9, 12 and 28.1; Business PR #254
at `6f0cd10259aeb2a44d32a92535464ed2ce096cf7`, Sales section 4 and Finance section 5;
issue #66 comment 5653637829. Base: `f6fa07d026403649d6c815b84fd850e7ceeaf092`.

The Application/Contracts/V1 port is an internal cross-module contract, not an
HTTP endpoint. No new OpenAPI route, DI registration, database, seed or grant is
introduced. Product, Sales and Finance reference the Application project and
adapt the port in their own module. Organization is not a monetary registry.

Company is resolved on the server, never supplied by a profile request. The
unavailable adapter checks authenticated actor and company membership before
reporting `settings_provider_unavailable`; unauthorized/unresolved context gives
`settings_access_denied`. These are internal error codes, not new platform HTTP
403 response guarantees. A future successful provider also requires approved
operation-specific authorization. Company membership alone grants no operation.

Units bind real unit ID/code, dimension and explicit kilogram identity to a
company, profile ID/version, active state, validity interval and quantity scale.
Stock/sales unit compatibility must use these owner identities; no conversion is
implied by shared dimension. Money binds currency and operation to the same
profile metadata. No rounding or exchange rate is supplied or implied.
Period results bind exact operation, company, occurredOn and nullable valueOn to
period/policy version and explicit eligibility or denial. They are not write
permissions. Missing source is an exception, never null, an empty catalog, zero
scale or an eligible period. DateOnly values do not use UTC record timestamps.

Consumers must check active/valid profile and exact identity/company/operation,
then revalidate with their command transaction and preserve the applied snapshot.
This slice does not implement a persistent provider or transaction locking.
It therefore cannot enable a real Sales draft, Product activation or money write.
(Unit and money profiles later got approved, non-persistent sources — see "Approved unit
profiles" and "Approved money profiles" below.)

ExactPositiveDecimalV1 preserves a positive digit string byte-for-byte, including
trailing zeros. It rejects signs, exponent/group separators, whitespace, zero,
invalid characters and excessive fractional digits. The explicitly supplied scale
must come from an approved profile. No numeric range, DB precision, rounding,
currency default or arithmetic policy is selected here. Consumers must separately
reject unsupported storage/calculator ranges without rounding or truncation.

Examples using synthetic scales only: Parse("1000", 0) returns Text "1000";
Parse("1.001", 2) throws settings_decimal_scale_exceeded. These examples do not
approve kg scale 0 or currency scale 2. Profile versions are positive server-owned
versions; any future HTTP adapter must encode them as lossless strings.

## Money currency candidates (read-only)

Source: Business issue #66 comment 5702138790 (SALES-FS1 minimal owner input) and
coordinator comment 5704482629 (owner: central Backend).

`ISettingsMoneyCandidateReader` is a separate V1 port, so `ISettingsPrerequisiteProvider`
and the test doubles other modules keep for it do not change. It lists money currency
candidates for the server-resolved company. The company is never a request input.

Each row is only `CurrencyId` (the owner's stable identity and the sole reference for
selection and revalidation), `Code` (the owner's real code, a label, never an identity)
and `DisplayLabel`. A row carries no scale, profile, version, operation or effective
range: being listed is not validity. Before any write, a consumer revalidates the
chosen `CurrencyId` through its known-id resolution (`GetMoneyProfileAsync` for its
operation and business date, e.g. the #262 Sales currency reference read); only that
result decides scale/profile/effective validity.

No currency list is hard-coded anywhere. Outcomes (Business #66 comment 5713672616,
error 1):

- A reachable source with no candidates for the company answers an empty list, which
  means "no candidates".
- A missing, unreachable, forbidden or malformed source is an exception
  (`settings_provider_unavailable`, or `settings_access_denied` after the same actor and
  company-membership check), never an empty list or a default currency: unavailable is
  not empty.
- A list is malformed when a row is missing, a `CurrencyId` is empty, a `Code` or
  `DisplayLabel` is empty or whitespace, or a `CurrencyId` appears twice. The whole list
  is rejected, not filtered. `MoneyCurrencyCandidatesV1.EnsureWellFormed` is that one
  check, for a provider before it answers and a consumer before it shows candidates; it
  reports `settings_provider_unavailable` because Business treats a malformed source as
  unavailable.
- Candidate order is undefined. The first row is not a default, and no candidate, not
  even a single one, is selected automatically.

The consumer owns the context fence: candidates read for one actor/company are discarded
when that context changes and are never shown under another.

Open question, not decided here: whether candidates are narrowed by operation (money
profiles bind currency and operation) or by business date. No approved source defines
it, so the port takes neither; if a source decides it, an additional V1 method can take
those inputs without changing this one.

The unavailable adapter implements this port too, with the same behaviour as the
prerequisite port: it always throws, and never answers an empty list. No HTTP route,
OpenAPI, DI registration, persistent provider, database, seed, currency data or grant
is introduced.

Outstanding approved-source data: the company/operation/date period eligibility
source. Unit and money profiles now have one approved source each (kg and TRY for
GDC, and since SAT-D18 for GSP and GIN, below), and since T02-b the approved provider also serves this candidate
port from the money catalog (see "Approved money profiles"); Master 28.1's rounding (`AwayFromZero`) is still a proposal, not
approved data. No default-open period, FX or Finance OPEN-009 permission is inferred.

## Approved unit profiles (T02-a)

Source: Sinan's decisions of 2026-09-18, Ö1 and Ö2 (#66 comment 5722637822;
`docs/decisions/SINAN_KARARLARI_2026-09-18_O1_O8_VE_SON_ASAMA.md` on #275), and
Master §28.1 for the dimension. Task: coordinator T02-a.

`ApprovedSettingsPrerequisiteProvider` is the real provider for what is approved:
unit profiles, and since T02-b money profiles (next section). Period eligibility
stays exactly as the unavailable adapter answers it.

The approved profiles: `ApprovedUnitProfileCatalog.GdcKilogramV1`, and since SAT-D18
(Sinan, 2026-10-26, `docs/decisions/SINAN_KARARLARI_2026-10-26_SATIS_EK.md`) `GspKilogramV1` and
`GinKilogramV1` with the same values, each its own per-company version. GDC's:

| Field | Value | Source |
|---|---|---|
| Company | GDC `10000000-0000-0000-0000-000000000001` | Organization seed and its migration guard (permanent GDC identity) |
| Unit | kg `c5ef643e-a6e8-51f3-89f2-8e68b00cb983` (`SettingsUnitIdentitiesV1.Kilogram`) | Ö1 (GDC band/coil, kg→kg); id is technical (Ö2) |
| Code / dimension | `kg` / `weight`, kilogram identity true | Master §28.1 "Ağırlık → kg" |
| Scale | 3 | Ö2 "kg … en fazla 3 ondalık" |
| Profile | `4a790fdc-2a6b-5126-835f-d3a13d28b71c`, version 1 | technical (Ö2) |
| Window | from 2026-09-18, no end (explicitly open-ended) | Ö2 "onaydan itibaren sürümlü, bitişsizse açıkça süresiz" |

Technical ids are UUIDv5 in namespace `18b5cbb9-98ca-5999-b418-aa53c41f0ffb`
(= UUIDv5(URL, "https://github.com/sinangym/GoymenERP/settings/unit-profiles")),
names `unit:kg` and `unit-profile:<company id>:kg:v1`; assigned once, never reused.
A unit identity is company-independent; the profile is per company.

| Company | Profile (version 1, kg, weight, scale 3, no end) | Effective from | Source |
|---|---|---|---|
| GDC `10000000-0000-0000-0000-000000000001` | `4a790fdc-2a6b-5126-835f-d3a13d28b71c` | 2026-09-18 | Ö1/Ö2 |
| GSP `10000000-0000-0000-0000-000000000002` | `0002612c-0804-5b12-a3bc-d94d1e84f25a` | 2026-10-26 | SAT-D18 |
| GIN `10000000-0000-0000-0000-000000000003` | `220904ee-a6ff-5cc4-9169-336c66328cd5` | 2026-10-26 | SAT-D18 |

Any other company has no approved profile (`settings_provider_unavailable`). A GSP/GIN date
before 2026-10-26 gets the real window and the consumer rejects it. GIN stays invoice-closed
(DEC-ORG-005); a unit profile changes nothing about that.

**Reach (SAT-D19).** A unit profile carries no operation scope, so the GSP/GIN kg profile has
exactly GDC's reach: every consumer of `GetUnitProfileAsync` for kg — Sales, Inventory (receipt,
opening count), Manufacturing, Purchasing (order line), Logistics (loading) and Product kg
checks. Each operation still needs its own grant, a human gate (`OPEN-013`). SAT-D19 opens
nothing else for GSP/GIN: the Manufacturing tolerance catalog and the stock tolerance policy
(`OPEN-004` stock side) have no GSP/GIN entries and stay `settings_provider_unavailable`, and
the TRY money profile stays Sales-only (SAT-D18).

**Data source: versioned source code, not a table.** Profiles are approved per company and
nothing may edit them at runtime; a new approval is a new version, reviewed like code.
So there is no migration, seed write or DbContext, and every value is visible in the
diff that introduces it; the only host change is the registration described under
"Host wiring" below. When profiles become
user-maintained, a persistent provider replaces the catalog behind the same port.

Outcomes of `GetUnitProfileAsync`, in order:

| Case | Result |
|---|---|
| cancelled | `OperationCanceledException` |
| no authenticated actor / actor id, unresolved or empty company, not a member | `settings_access_denied`, before anything about the source is revealed |
| no approved source for the server-resolved company | `settings_provider_unavailable` (unreachable) |
| source exists but no profile for this unit (including an empty id) | `settings_unit_profile_not_found` (reachable but empty) |
| malformed catalog | `settings_profile_invalid`, raised when the catalog is built; nothing repaired or skipped |
| otherwise | the exact profile version in force on the date |

A date outside every window (for example before the approval) returns the real
version with its real window; the provider never widens a window to fit the
question. Consumers check activity, window and kilogram identity themselves, as the
port requires, so such a date is rejected by them — the Product adapter on main
reports it as `settings_profile_invalid`. A later approved version takes over from
its own start date and never rewrites an earlier answer.

`settings_unit_profile_not_found` is new. It exists because a reachable source that
has no profile for an identity is neither unavailable nor malformed; consumers that
pass Settings codes through (Product) carry it unchanged.

Not in this slice: Product's own unit catalogue (it must use
`SettingsUnitIdentitiesV1.Kilogram` when it lists kg — ÜRÜN's follow-up), the
period source, any rounding method, any grant or production data. A returned profile is
reference metadata, not an authorization: the consuming use case still checks its own
permission.

**Host wiring.** The API host registers `ISettingsPrerequisiteProvider` as
`ApprovedSettingsPrerequisiteProvider` (previously the unavailable adapter). Its only
consumer there is the Sales reference read. Sales asks for unit profiles only after a
Product sales-eligibility snapshot, and no such runtime is composed in the host yet, so the
swap changes no reachable Sales answer until it is; money and period answers were unchanged by that swap (money: see T02-b below).
On its product path Sales maps `settings_unit_profile_not_found` to `422 unsupported_unit`
(#285), the same permanent refusal an inactive or out-of-range profile gets; every other
Settings code keeps Sales' earlier mapping (`settings_access_denied` → 403, the rest → `503
settings_provider_unavailable`). The code is published as
`SettingsPrerequisiteCodesV1.UnitProfileNotFound`; consumers map that constant, not the
provider class. Only the reviewed catalog is reachable from outside
Settings: the catalog-taking constructors are internal and visible to
`GoymenERP.Settings.UnitTests` only.

## Approved money profiles (T02-b)

Source: Sinan's Ö2 of 2026-09-18 (#66 comment 5722637822): "TL/USD/EUR nihai tutar 2,
birim fiyat gerektiğinde 6 ondalığa kadar … İlk kabul profili GDC/TRY; profil
geçerliliği açık onaydan itibaren sürümlü ve bitişsizse açıkça süresiz". Task:
coordinator T02-b.

The approved profiles: `ApprovedMoneyProfileCatalog.GdcTurkishLiraV1`, and since SAT-D18
`GspTurkishLiraV1` and `GinTurkishLiraV1` (below). GDC's:

| Field | Value | Source |
|---|---|---|
| Company | GDC `10000000-0000-0000-0000-000000000001` | Ö2 "ilk kabul profili GDC/TRY" |
| Currency | TRY `9e60dcf0-f4bd-5bca-b464-f9a2db98e31a` (`SettingsCurrencyIdentitiesV1.TurkishLira`) | Ö2; id is technical |
| Operations | `accounting.journal-amount.v1`, `finance.manual-bank-payment.v1`, `finance.manual-bank-receipt.v1`, `purchasing.order.v1`, `sales.order-draft.v1` | the Accounting, Finance and Sales owners' keys (Business #254 5654539215); a journal amount is a final TRY amount (ACC-V0a, Ö2 "nihai tutar 2"); the Purchasing owner's key since A2a-1b (ALS-D63 narrow exception, Sinan #66 6704170096) |
| Scale | 2 (final amount) | Ö2 "nihai tutar 2" |
| Unit-price scale | 4, for `purchasing.order.v1` only; none (null) for every other operation | Sinan, A2a-1b: purchasing unit price "4 ondalık" |
| Profile | `7f836492-a54f-512f-a8f4-5f556b3f0f9d`, version 1 | technical (Ö2) |
| Window | from 2026-09-18, no end (explicitly open-ended) | Ö2 |

Technical ids are UUIDv5 in namespace `9df84417-8c25-59c8-a67a-959ac880a890`
(= UUIDv5(URL, "https://github.com/sinangym/GoymenERP/settings/money-profiles")),
names `currency:TRY` and `money-profile:<company id>:TRY:v1`; assigned once, never
reused. The data source is versioned source code, as for units, for the same reasons.

**GSP and GIN (SAT-D18).** Each has its own TRY profile, version 1, scale 2 (final amount,
Ö2), effective from 2026-10-26 with no end, answering **only** `sales.order-draft.v1` and, since A2a-1b, `purchasing.order.v1`. SAT-D18
leaves the Accounting (`accounting.journal-amount.v1`) and Finance
(`finance.manual-bank-payment.v1`, `finance.manual-bank-receipt.v1`) operations for GSP/GIN to a
separate decision, so those keys answer `settings_provider_unavailable` there: the operation
check is per company. GIN stays invoice-closed (DEC-ORG-005).

| Company | Profile | Operations | Effective from |
|---|---|---|---|
| GDC | `7f836492-a54f-512f-a8f4-5f556b3f0f9d` | Accounting, Finance (payment, receipt), Sales draft | 2026-09-18 |
| GSP | `5b863ed2-e21a-5c43-b219-fa113dcc50b3` | `sales.order-draft.v1`, `purchasing.order.v1` (A2a-1b) | 2026-10-26 |
| GIN | `d78eee0d-4940-52e3-bd80-3b5ebdb6d760` | `sales.order-draft.v1`, `purchasing.order.v1` (A2a-1b) | 2026-10-26 |

**Operations belong to their owners.** A profile answers only for the operation keys
listed on it. Settings references neither Finance nor Sales; the API integration test
`The_money_catalog_answers_exactly_the_owners_published_operation_keys` pins the
literals to `FinanceMoneyOperations.ManualBankReceiptV1` and
`SalesMoneyOperations.OrderDraftV1`. The profile answers for the operation asked, so
consumers' exact-operation check holds.

**Unit-price scale (A2a-1b).** `MoneyProfileV1.UnitPriceScale` (optional, additive; null by
default) carries the unit-price decimals approved for the operation asked, from the entry's
per-operation `UnitPriceScales`. Only `purchasing.order.v1` has one (4, on GDC, GSP and GIN);
every other operation answers null, because Ö2's general "birim fiyat gerektiğinde 6 ondalığa
kadar" is not narrowed by any owner decision for them. A key that the entry does not list, or a
value outside 0..28, makes the catalog `settings_profile_invalid`.

Not published, because `MoneyProfileV1` has no field for them and nothing is guessed:
a rounding method, exchange rates, tax, and USD/EUR profiles (Ö2
names their final-amount scale, but approves only GDC/TRY as a profile).

Outcomes of `GetMoneyProfileAsync`, in order:

| Case | Result |
|---|---|
| cancelled | `OperationCanceledException` |
| no authenticated actor / actor id, unresolved or empty company, not a member | `settings_access_denied` |
| no approved money source for the company, or an operation key no approved profile of that company answers for (exact, ordinal; e.g. Accounting/Finance keys for GSP/GIN) | `settings_provider_unavailable` |
| approved operation, but no profile for the currency (USD, EUR, an empty or unknown id) | `settings_money_profile_not_found` (`SettingsPrerequisiteCodesV1.MoneyProfileNotFound`) |
| malformed catalog | `settings_profile_invalid`, raised when the catalog is built |
| otherwise | the exact version in force on the date, with its real window |

As for units, a date before the approval returns the real window and the consumer
rejects it; a later version takes over from its own start date.

**Candidates.** The same provider implements two candidate reads from the same catalog, one
candidate per currency, labelled by the approved code itself (no approved source names a longer
one). Access first (`settings_access_denied`); every list passes
`MoneyCurrencyCandidatesV1.EnsureWellFormed`; never an empty list or GDC's TRY in place of
"unavailable".

- `ISettingsMoneyCandidateReader.GetMoneyCurrencyCandidatesAsync()` (operation-less, used by
  Finance FS0 #312 to check a bank account's currency) lists a currency only when its profile is
  **unrestricted** — it answers every operation key the catalog knows — because the caller cannot
  be told a restriction. GDC: `(TRY id, "TRY", "TRY")`. GSP/GIN's TRY answers only the Sales draft and Purchasing order keys (SAT-D18, A2a-1b), so
  they have no unrestricted profile and keep the answer they had before any profile:
  `settings_provider_unavailable`. A GSP/GIN TRY bank account therefore stays refused, as SAT-D18
  requires.
- `ISettingsMoneyCandidateReaderV2.GetMoneyCurrencyCandidatesAsync(operation)` (operation-aware)
  lists each currency whose profile answers the operation (exact, ordinal); a company without a
  money source, or an operation no profile of the company answers, is
  `settings_provider_unavailable`. GDC, GSP and GIN for `sales.order-draft.v1`: TRY. The Sales
  currency list (`SalesReferenceReadHandler.GetCurrenciesAsync`) uses this read with the host's
  bound Sales operation, falling back to the operation-less read only when V2 is not composed.

Being listed is not validity: consumers still resolve the chosen currency with
`GetMoneyProfileAsync` for their operation and date.

**Host wiring.** `ISettingsMoneyCandidateReader` is registered as the approved provider,
and `SalesReferenceReadBindings` binds the Sales owner's money operation
(`SalesMoneyOperations.OrderDraftV1`, a Sales decision). Reachable effect today: none. The
host's Finance draft read context checks its permission port first, and that port is the
unavailable one; the Sales currency route binds no currency permission, so it answers 403
before asking Settings. `SettingsHostWiringTests` shows the Sales read, with only a
synthetic permission and the host's own operation, provider and account source, reaching
the approved TRY profile, while any other currency stays fail-closed. `settings_money_profile_not_found` is new: consumers that map Settings
codes must decide its public answer. Today Finance's draft read reports any other code as
Unavailable carrying the code, and Sales' reference read as `503 settings_provider_unavailable`.

Validation: Settings unit/contract tests are synthetic, not SQL or browser UAT.
No persistence changed, so new slice-specific SQL scenarios are not applicable;
the existing required CI relational suite remains required. Rollback consists of
removing unused project references; there is no data or migration rollback.

## Approved Sales policies (MOD-03 slice 1)

Source: Sinan's Sales decisions of 2026-10-25, SAT-D2, SAT-D5 and SAT-D6
(`docs/decisions/SINAN_KARARLARI_2026-10-25_SATIS.md`), and of 2026-10-26, SAT-D14 (GSP and GIN
start with GDC's Sales policy values, each as its own versioned entry) and SAT-D15–D17
(tolerance bands, measurement and line closing)
(`docs/decisions/SINAN_KARARLARI_2026-10-26_SATIS_EK.md`); plan items MOD-03 and API-50…52 of
`docs/delivery/SATIS_HAZIRLIK_PLANI_v1.md`.

`ISettingsSalesPolicyProviderV1` is a separate V1 port with three reads. The company is always
the server-resolved one, never a request input. `ApprovedSettingsPrerequisiteProvider` serves it
from `ApprovedSalesPolicyCatalog`, versioned source code like the unit and money catalogs (no
table, migration, seed or DbContext); the catalog-taking constructors are internal and visible to
`GoymenERP.Settings.UnitTests` only. The unavailable adapter implements the port too and always
answers `settings_access_denied` or `settings_provider_unavailable`.

The same values for all three seed companies (SAT-D14):

| Read | Value (GDC, GSP, GIN) | Source |
|---|---|---|
| `GetSalesDeliveryToleranceAsync(effectiveOn)` | warn 1 %, approval 2 %, block 3 % | SAT-D5; OPEN-004 sales part; SAT-D14 for GSP/GIN |
| `GetSalesDiscountLimitAsync(limitGroupId, effectiveOn)` | **none approved** — every group answers `settings_sales_policy_not_found` | SAT-D2 (no limit value approved); SAT-D14 |
| `GetSalesApprovalAmountThresholdAsync(currencyId, effectiveOn)` | TRY (`SettingsCurrencyIdentitiesV1.TurkishLira`): `IsEnabled = false`, amount `null` | SAT-D6; OPEN-009 KISMEN; SAT-D14 for GSP/GIN |

Profiles, all version 1 with no end date:

| Company | Delivery tolerance profile | TRY threshold profile | Effective from |
|---|---|---|---|
| GDC `10000000-0000-0000-0000-000000000001` | `c088f8d5-26c4-5ed9-ae6f-f47e73a26b11` | `5c0ec7f4-2116-59c6-82fc-9c2a737a4342` | 2026-10-25 (SAT-D5/D6) |
| GSP `10000000-0000-0000-0000-000000000002` | `e5ba2cba-1f1e-5ba0-b801-7b66d21190af` | `320e06e3-0408-51f1-92e6-f8905e7b674f` | 2026-10-26 (SAT-D14) |
| GIN `10000000-0000-0000-0000-000000000003` | `c5fd5cfa-b37f-5464-a920-152eb96c8c84` | `3cd01981-8511-5dc1-b21b-6c632425d915` | 2026-10-26 (SAT-D14) |

SAT-D14's "same permissions" covers these policy values only; it assigns no role or grant to
GSP/GIN users.

Technical ids are UUIDv5 in namespace `eb68fb11-2146-5285-9cc5-6ce5f3981184`
(= UUIDv5(URL, "https://github.com/sinangym/GoymenERP/settings/sales-policies")), names
`sales-delivery-tolerance:<company id>:v1` and
`sales-approval-amount-threshold:<company id>:TRY:v1`; assigned once, never reused.

Outcomes of each read, in order:

| Case | Result |
|---|---|
| cancelled | `OperationCanceledException` |
| no authenticated actor / actor id, unresolved or empty company, not a member | `settings_access_denied`, before anything about the source is revealed |
| empty `limitGroupId` / `currencyId` | `settings_sales_policy_request_invalid` (`SettingsPrerequisiteCodesV1.SalesPolicyRequestInvalid`), after the access check and before any lookup — never "not found" |
| no approved Sales policy source for the company (any company other than GDC, GSP and GIN) | `settings_provider_unavailable` |
| source reachable, but no policy for the identity (any non-empty discount limit group, a non-TRY currency) | `settings_sales_policy_not_found` (`SettingsPrerequisiteCodesV1.SalesPolicyNotFound`) |
| malformed catalog | `settings_profile_invalid`, raised when the catalog is built; nothing repaired or skipped |
| otherwise | the exact version in force on the date, with its real window |

A company is "served" when it has any approved Sales policy; today GDC, GSP and GIN all are
(SAT-D14), so the unserved, fail-closed path remains only for any other company. As for units and money, a date
before the approval returns the real version with its real window; the consumer checks
activity and window and rejects it.

The catalog is refused whole when an identity is empty, a source blank, a version non-positive,
a window reversed, a version repeated or two windows of one key overlap; also when a tolerance
breaks 0 ≤ warn ≤ approval ≤ block ≤ 100, a discount limit is outside 0…100, an enabled
threshold lacks a lossless positive amount (`ExactPositiveDecimalV1` lexical rules, in canonical
form without a leading zero: `0100` is refused, `0.50` is not) or a disabled one carries an
amount. The threshold amount's scale is not checked here: the consumer checks it
against the currency's money profile.

**Consumer obligations.**

- Discount limit: `settings_sales_policy_not_found` is SAT-D2's "no limit defined for the role"
  and means **every discount requires approval** — never "unlimited", never a zero limit that
  blocks. This holds for GDC, GSP and GIN alike (SAT-D14). `settings_provider_unavailable`
  (a company with no Sales policy source at all — none of the three seed companies),
  `settings_profile_invalid`, `settings_sales_policy_request_invalid` and
  `settings_access_denied` are not that answer: the consumer refuses on them (DEC-SET-001
  fail-closed).
- What the discount percent is relative to (its price base) is defined by the consuming
  contract (API-12), not by this port.
- `limitGroupId` is an opaque id. Settings never receives a role name. SAT-D63 (2026-10-27): limit
  groups are separate from roles and persons are bound to them; both the groups and the bindings are
  Settings records (see "Sales discount limit groups" below), entered by an authorized person, never
  seeded.
- Approval threshold: `IsEnabled = false` means no amount-based routing; SAT-D6's single
  approver (Müdür) applies. An enabled threshold is a later approved version, not a runtime
  switch. `settings_sales_policy_not_found` on this read (a currency with no threshold profile,
  e.g. USD/EUR for GDC today) is **not** "disabled": per DEC-SET-001 fail-closed the consumer
  must not silently skip amount routing for that currency. What it does instead is
  **NEEDS-SINAN (#66 comment 6404405195 follow-up)**, an open consumer question.
- Tolerance (SAT-D15): bounds are inclusive and over and under delivery use the same
  thresholds. Deviation = |delivered − ordered| / ordered × 100, and a bound value falls into
  the band it starts:

  | Deviation | Result |
  |---|---|
  | < warn (1 %) | within tolerance ("Tamamlandı" may count on the realization axis) |
  | warn ≤ d < approval (1 % ≤ d < 2 %) | warn |
  | approval ≤ d < block (2 % ≤ d < 3 %) | approval required |
  | ≥ block (3 %) | block |

  Measurement and line closing (consumer rules, no calculator in this slice):

  - SAT-D16: per order line, in the line's order unit (e.g. kg); delivered = the sum of all the
    line's shipments. Over-delivery is checked on every shipment. Under-delivery is measured
    only when the line is closed or its last shipment is marked; the open remainder is not a
    shortfall.
  - SAT-D17 (shortfall at close): within tolerance (< warn) the line becomes "Tamamlandı"
    directly (SAT-D5); in the warn band it becomes "Tamamlandı" directly; in the approval band
    after approval. In each case the remainder is a cancelled remainder. In the block band the
    line is not closed this way: the rest is shipped, or force-close (No 30, SAT-D12) applies.
  - Over-delivery additionally triggers delegation No 57: the line goes to the sales owner's
    review queue, and no invoice is created automatically.

  The percents come from this port, never from code constants; the deviation and band are
  computed by the consuming backend calculator, which UI and reports reuse. The read takes no product or operation input: SAT-D5's per-product/operation
  overrides need a new versioned method or interface later, not a change to this one.
- A returned policy is reference metadata, not an authorization: the consuming use case still
  checks its own permission and persists the applied profile id/version with its decision.

**Host wiring.** The API host registers `ISettingsSalesPolicyProviderV1` as
`ApprovedSettingsPrerequisiteProvider`, and `AddSettingsModule` replaces it (`services.Replace`, the Manufacturing
tolerance precedent) with `SettingsSalesPolicyProviderV1`: the delivery tolerance and the threshold are the
catalog's, unchanged; the discount limit adds the limit-group store (SAT-D61…D64, below).
`SettingsHostWiringTests` proves the registration and the GDC/GSP/GIN, unserved-company and
denied answers through the host.

Not in this slice (MOD-03 slice 1): per-product or per-operation tolerance overrides (SAT-D5), an enabled
threshold amount. The discount limit values and the person → limit group bindings are the next section's.

Validation: Settings unit tests (synthetic catalogs for invariants and windows; the approved
values asserted against their SAT sources) and the host wiring tests. No persistence changed;
rollback is removing the registration and the files, with no data rollback.

## e-Fatura scenario policy (SAT-D102)

Sources: SAT-D102 (`docs/decisions/SINAN_KARARLARI_2026-10-28_SATIS_EK13.md`): the e-Fatura scenario default is a
Settings value, changeable per customer on the Cari card; its starting value is **empty** and Sinan enters it.
SAT-D120 (`…_2026-10-31_SATIS_EK17.md`): the scenario **code list** starts with the standard GİB/UBL-TR set.

`ISettingsEInvoiceScenarioPolicyProviderV1` has two effective-dated keys, served by `ApprovedSettingsPrerequisiteProvider`
from `ApprovedEInvoiceScenarioCatalog` (reviewed source code, no table or seed; catalog-taking constructors internal):

| Read | Value today (GDC, GSP, GIN) | Answer |
|---|---|---|
| `GetEInvoiceScenarioCodesAsync(effectiveOn)` | **TEMELFATURA, TICARIFATURA, EARSIVFATURA, IHRACAT** (SAT-D120 standard set; before its effective date: not found) | the code list, or `settings_einvoice_scenario_code_list_not_found` |
| `GetEInvoiceScenarioDefaultAsync(effectiveOn)` | **empty** — Sinan enters it (SAT-D102) | `settings_einvoice_scenario_default_not_found` |

Access is checked first (`settings_access_denied`). Every accessible company has both keys, so an empty key is "not
found", never `settings_provider_unavailable` and never a default. A served default is always a code of the list in
force on the same date (the catalog refuses a default its list does not carry on its first day; a later list that drops
it makes the read `settings_profile_invalid`). Codes are lexical UBL-TR profile tokens
(`EInvoiceScenarioCodeFormatV1`: `^[A-Z][A-Z0-9_]{0,31}$`). Entering a value is a new reviewed catalog version (as the
other Settings policies); a user-maintained Settings screen is a later slice. Consumer: Counterparty (Cari card override
validation and `ICounterpartyAccountEDocumentProfileSourceV1`).

## Sales discount limit groups (SAT-D61…SAT-D70)

Sources: `docs/decisions/SINAN_KARARLARI_2026-10-27_SATIS_EK10.md` (SAT-D61…D64), `…_EK11.md` (SAT-D65…D70) and
`…_2026-10-28_SATIS_EK12.md` (SAT-D78, D79, D81, D84), applying SAT-D2, SAT-D34, SAT-D36 and SAT-D37 (`…_EK4.md`). Consumer: API-12 (Sales README, "Discount limit and its exception
approval"). **No value is seeded and no role or grant is opened**: groups, limits and bindings are data an authorized
person enters (EK10 implementation notes); until they are entered, today's fail-closed behaviour stays (every positive
discount goes to approval).

| Concern | Rule |
|---|---|
| Groups (SAT-D63, SAT-D117, SAT-D118) | Company-scoped `settings.SalesDiscountLimitGroups`, separate from roles: a stable `Code` (`A-Z 0-9 _ -`, ≤ 32, unique per company — the key the multi-company binding matches), a `Name` (≤ 100, unique per company), `IsActive`. Create, rename, deactivate, **reactivate** (SAT-D118, Sinan 2026-10-29: one person + mandatory reason + audit, `POST …/groups/{groupId}/activation`, 409 `…_group_active` when already active); **never deleted**. SAT-D117 (Sinan 2026-10-29, as SAT-D77 for customer groups): a passive group is closed only to NEW bindings and new links; the people already bound to it and the limits linked to it keep counting (the calculator and the Sales port read it), a person already bound to it keeps it when their binding is saved again, and its own limit and name cannot be changed until it is reactivated. Decisions to be recorded as SAT-D117/SAT-D118 (EK16). |
| Limits (SAT-D36, SAT-D61, SAT-D62) | Immutable records in `settings.SalesDiscountLimitVersions`: `Fixed` (percent 0…100) or `Linked` (another ACTIVE group of the same company + extra points 0…100). A change is a new version, never an update; the same values again (from the latest record's date on) are a no-op. CHECK constraints hold the kind, the shape, the ranges and no self-link; composite foreign keys keep both groups in the record's company. |
| Withdrawal and correction (SAT-D84) | ANY record whose date is still ahead (also when later records are scheduled) can be **withdrawn** (`POST …/groups/{groupId}/limit/withdrawal`) or **corrected** (`POST …/limit/correction`: withdrawn and replaced by a new record in ONE save and ONE audit event) before it takes effect, with a reason. The record row never changes: a withdrawal is an insert-only row in `settings.SalesDiscountLimitWithdrawals` (`ReplacedByVersion` names the correcting record), and the calculator ignores a withdrawn record. A record in force (its date is today or earlier) is refused with 409 `…_record_in_effect` (a new record is entered instead, SAT-D84); a withdrawn one with 409 `…_record_withdrawn`. A withdrawal can close a cycle too (it extends the earlier linked record) and is checked the same way. |
| Calculation | ONE calculator, `SalesDiscountLimitCalculatorV1` (Application), used by the policy read and the screen's read. Linked = linked group's limit on the same date + points, **capped at 100**; chains are followed. **None** (fail-closed → approval; SAT-D79: "approval", never "0 + points") when the group has no record in force, or the linked group is unknown or has no limit itself (a passive group still counts, SAT-D117). A cycle or a malformed record is `settings_profile_invalid` (Sales 503) — never a guess; the screen shows it as `Invalid`, not as "no limit". A change that would close a cycle on ANY date from its start on — the link graph is checked at the change's date and at every later record's date, since records can be scheduled ahead (A→B→A, self-link) — is refused at write (422 `settings_discount_limit_link_cycle`), and the save fences every group reached by its version, so two concurrent changes cannot close one together. |
| Effective dating (SAT-D78, SAT-D84) | One key, ascending versions, non-overlapping windows. A record takes effect from its `effectiveFrom` business date (optional; omitted = **today's Europe/Istanbul business date**, SAT-D10; a time-zone failure is 503 `settings_provider_unavailable`) and applies **by itself** from then on until the next later-dated record starts; a date before today is 422 `…_effective_from_past`. The timeline is ordered by **date, then version** (review A P2-1): a record may be entered for a date before an already scheduled one, and ANY not-yet-effective record can be corrected keeping its own date or withdrawn (SAT-D84). A new record on the same date as a not-yet-effective one REPLACES it — that record is withdrawn in the same save and the change is audited as a correction — so a later withdrawal never brings a hidden record back (review B P2-1). The group list shows the record in force today and the scheduled ones, each with the limit on its first day. Sales reads the limit on the day the order is entered or commercially revised (SAT-D81, Sales README), so a scheduled limit never applies to an order entered before its date. The Sales check snapshots the limit, profile id and version at create/revision, so later changes never rewrite a recorded check. Before the first record nothing is in force ("not found"). The returned window of a linked limit is the intersection of both records' windows. |
| Source precedence (`SettingsSalesPolicyProviderV1`) | Access → `settings_access_denied`; empty id → `settings_sales_policy_request_invalid`. A group the store does not define → the approved catalog answers exactly as before (today: "not found" for every group of GDC/GSP/GIN, `settings_provider_unavailable` elsewhere). A store group that the catalog ALSO defines → `settings_profile_invalid` (contradiction, fail-closed; group ids are server-minted). Otherwise the calculator: in force → the record's profile id + version; none → `settings_sales_policy_not_found`. A store that cannot be read → `settings_provider_unavailable`. |
| Bindings (SAT-D63, SAT-D66) | Per company: `settings.SalesDiscountLimitMemberships` (person, version) + immutable `…MembershipGroups` rows of each version (history kept, nothing deleted). **Owner: Settings** (EK11 proposed Identity; chosen here by module boundaries): the binding refers to Settings' own company-scoped groups, so group existence/activity, the version fence and the all-or-nothing multi-company write stay inside one store and one transaction, and Identity's write surface stays closed (OPEN-013) — the binding grants no permission. Identity is asked only "is this person an active member of these companies" through its port (`IUserCompanyMembershipReadPortV1`, host adapter `SettingsPersonDirectoryHost`), never through its tables. |
| Multi-company write (SAT-D66, SAT-D116) | `PUT …/bindings/{userId}` writes the same group CODES to every company the actor can act in **where the person is an active member now** (SAT-D116, `SINAN_KARARLARI_2026-10-29_SATIS_EK15.md`), in ONE SQL transaction (one Settings context per company over the same connection, each with that company's company context, write fence and audit interceptor). The other companies are skipped and untouched; `GET …/bindings/{userId}` marks them `isMember: false` and the screen shows them as skipped. The request names exactly the member companies with the version seen in each (`"0"` = none): another set (membership changed meanwhile) is 422 `…_binding_companies_mismatch`; a stale one is 409 naming the company. The actor must hold the name in the server company and in EVERY member company (403, nothing written). A person who is a member of none of the actor's companies: 422 `…_binding_person_not_member`. A code missing or passive in a member company: 422 `…_binding_group_not_found` / `…_inactive` naming company and code. All or nothing. A company the actor cannot access is not part of the command; a later membership neither extends nor removes a binding by itself (re-bind; EK15). |
| Sales port (API-12) | `ISettingsSalesDiscountLimitGroupMembershipReaderV1` → the ACTIVE groups of the current actor's binding in the server company; the host's `SalesDiscountLimitGroupHost` registers it as Sales' `ISalesDiscountLimitGroupSourceV1`. An actor id that is not an ERP user id has no binding (empty); a refusal is passed on (denied → 403, unavailable → 503), never an empty answer. |
| Authority (SAT-D65, D68, D69, D70) | ONE name, `settings.sales-discount-limits.manage` (`SettingsPermissionManifestV1.SalesDiscountLimitsManage` = `ErpPermissions.SettingsSalesDiscountLimitsManage`): company-wide only, **not Critical** (SAT-D69: the Admin persona's general rule covers it), assigned to **no role** and seeded to nobody — granting it to the board members and Admin is a human gate. Reads and commands measure it first and re-measure it fresh before every save. One person decides (no second approval, SAT-D68); the editor may change the limit of a group they belong to and their own binding (SAT-D70, no self-block). |
| Commands | Idempotency-Key (stored terminal answers replay; another body under the key is 409 `…_idempotency_conflict`), the version as decimal text (409 `…_version_conflict`), a mandatory reason ≤ 4000 (400 `…_reason_required` / `…_too_long`, not stored), correlation/causation, and ONE audit event per real change in the company it changes (`settings.sales-discount-limit-group.created/renamed/deactivated/reactivated.v1`, `settings.sales-discount-limit.recorded/withdrawn/corrected.v1`, `settings.sales-discount-limit-binding.changed.v1`): actor, permission, roles, before/after and the person's whole reason in the after-image (`reason`); the audit row's own reason column carries the fixed description (≤ 1000, the Sales precedent). No-ops write no event. Another company's group is the same code-less 404 as an unknown one; as a link target it is 422 `…_linked_group_not_found`. |
| Routes | `GET/POST /api/v1/settings/sales-discount-limits/groups`, `PUT …/groups/{groupId}/name`, `POST …/groups/{groupId}/deactivation`, `POST …/groups/{groupId}/activation`, `PUT …/groups/{groupId}/limit`, `POST …/groups/{groupId}/limit/withdrawal`, `POST …/groups/{groupId}/limit/correction`, `GET …/people?q=&limit=` (≤ 50, the Identity directory's active members, id + name), `GET/PUT …/bindings/{userId}`. Private, no-store. A store that is not relational (the broad in-memory host) answers the binding routes 503. |
| Persistence and rollback | Migration `20261027221309_AddSalesDiscountLimitGroups` (generated with `dotnet ef`): five tables (with the SAT-D84 withdrawals), keys, indexes, CHECKs and composite foreign keys. `Down` refuses (THROW **51095**) while any group or binding exists (a withdrawal cannot exist without its group), before any drop (forward fix); on an empty store it drops the tables. The platform `audit.AuditEvents` and `idempotency.CommandExecutions` are mapped excluded from Settings' migrations, under the CompanyId filter. Manufacturing tolerance policies stay read-only in this context. |
| Evidence | Unit: `SalesDiscountLimitCalculatorV1Tests` (Settings). Host/port: `SettingsSalesDiscountLimitPortTests`, `SettingsSalesDiscountLimitPermissionRegistrationTests`, `SettingsHostWiringTests`. Real SQL: `SettingsSalesDiscountLimitSqlServerRelationalAcceptanceTests` (real routes, Identity directory, audit, migration rollback refusal). Sales: `SalesDraftServiceTests.DiscountException`. |

Closed by EK12 (2026-10-28) and EK15 (2026-10-29): SAT-D116 (bindings only where the person works), SAT-D78 (future-dated limit records), SAT-D79 (a linked group without a limit → approval),
SAT-D81 (the limit of the day the order is entered or revised) and SAT-D84 (withdraw/correct before the date). Open (not
chosen here): from which screen bindings are managed long-term (a Settings screen is provided); the first value of the
Müdür's extra points; company-specific bindings (the schema allows them; only the all-companies write is exposed).

## Tax master — default tax per item type, UBL-TR code lists and the VUK 232 threshold (MOD-03)

Sources: `docs/decisions/SINAN_KARARLARI_2026-10-31_SATIS_EK19.md` (SAT-D133…D140, the re-decision that replaces the
EK12 class model for this slice), `…_EK12.md` (SAT-D75, D76, D83) and `…_EK13.md` (SAT-D93, D100, D105, D110; codes need the
mali müşavir's confirmation: "teyit edilene kadar Ayarlar değeri yoksa ilgili alan 'kaynak yok' kalır"; UBL-TR code lists are
effective-dated Settings records, never hard-coded).

| Concern | Rule |
|---|---|
| Model (SAT-D134/D139) | The product card holds only an ITEM TYPE — `Steel` (demir), `Scrap` (hurda), `Service` (hizmet) — owned by Product (see the Product README). Settings holds the DEFAULT tax per item type: KDV applies with a percent above 0, or "KDV yok" with an optional exemption code; withholding applies with a percent > 0…100 (the withheld share of the VAT, 50 = 5/10) and an optional UBL-TR code, or not. The order line may change the default (SAT-D93, Sales follow-up). |
| Reviewed default catalog (SAT-D139) | `ApprovedTaxItemTypeDefaultCatalog` (Application): one reviewed version (record version **0**, effective 2026-10-31) in each of GDC, GSP and GIN (each company its own entries, per-company tables, SAT-D133): demir → KDV %20 + tevkifat 5/10 (50); hurda → KDV yok, tevkifat yok; hizmet → KDV %20, tevkifat yok. Ids are deterministic UUIDv5. It carries **no code**: the hurda exemption code and the demir withholding code stay "kaynak yok" (the invoice waits) until a Settings record names one from the list. The catalog is the base of each timeline; the calculator never hard-codes a rate. |
| Settings records | Company-scoped `settings.TaxItemTypeDefaultSeries` (fence per item type, `[ItemType] IN ('Steel','Scrap','Service')`) + immutable `TaxItemTypeDefaultRecords` (version ≥ 1; CHECKs hold the shape: a VAT percent above 0 iff VAT applies (zero VAT is only "KDV yok", behind the exemption code gate), exemption code only without VAT, withholding only with VAT and percent > 0) + insert-only `TaxItemTypeDefaultWithdrawals`. Percents are lossless text, ≤ 4 decimals, never rounded. A code must be in the list of its kind in force on the record's date (422 `…_exemption_code_unknown` / `…_withholding_code_unknown`). |
| Code lists | `Exemption` and `Withholding` lists as whole, immutable versions (`TaxCodeLists` fence, `TaxCodeListVersions`, `TaxCodeListEntries`, `TaxCodeListWithdrawals`; codes `A-Z 0-9`, ≤ 16; ≤ 500 entries). Nothing is seeded. |
| Withholding threshold (SAT-D135) | The VUK 232 amount threshold is a dated Settings value that **starts empty**: `TaxWithholdingThresholds` (fence) + immutable `TaxWithholdingThresholdRecords` (amount > 0 as lossless text, ≤ 15 integer digits and 4 decimals, and the ISO 4217 currency it is in — entered, never assumed) + insert-only withdrawals. `GetWithholdingThresholdAsync(on)` answers `Resolved` or `Unresolved` `withholding_threshold_missing` — the invoice (API-32) then waits; the comparison against the whole transaction total (KDV dahil, never split) is the consumer's. |
| Effective dating | The SAT-D78/D84 precedent (same withdraw/replace rules for defaults, lists and the threshold): a record takes effect on its business date (omitted = today, Europe/Istanbul); a date before today is 422 `…_effective_from_past`; in force = the latest-dated on or before the date, then the highest version (a Settings record over the catalog on the same date wins). A record on the same date as a not-yet-effective one REPLACES it (audited `…corrected.v1`), so a later withdrawal never resurrects it. A not-yet-effective record can be withdrawn; a record in force is 409 `…_record_in_effect`, a withdrawn one 409 `…_record_withdrawn`; the catalog version cannot be withdrawn. |
| Buyer situation (SAT-D140/D147/D158) | Chosen PER ORDER by Sales (follow-up) and passed to the resolver as an INPUT code. **SAT-D158 (EK21): the situations are an effective-dated Settings list per company** (`TaxBuyerSituationLists` fence, whole immutable `TaxBuyerSituationListVersions` with `TaxBuyerSituationEntries`, insert-only `TaxBuyerSituationListWithdrawals`; same timeline rules as the code lists). Each entry carries its tax effect as DATA: `withholdingApplies` (false removes the default's withholding, SAT-D74), `vatMode` `Normal` or `ZeroWithExemptionCode` (KDV %0, SAT-D147; a CHECK refuses it with withholding), `documentRequired`. The reviewed version-0 catalog `ApprovedTaxBuyerSituationCatalog` (GDC/GSP/GIN, effective 2026-10-31) holds the five SAT-D140 situations: `Normal` (withholding kept), `FinalConsumer` / `NotSubjectToWithholding` (no withholding), `ExportRegistered` / `ExemptionCertificate` (KDV %0 with the exemption code the order chooses from the exemption list, no withholding, document required). Müdür/Admin add or change situations by publishing a version (SAT-D145, `settings.tax-master.manage`; Muhasebe's SAT-D146 proposal flow is a later slice). An unknown or withdrawn code resolves `Unresolved` `buyer_situation_unknown`, never the normal default. A zero-rated answer is VAT `ZeroRated` (0 %), withholding `NotApplied`, the chosen exemption code `Resolved` only when listed on the date (none chosen → `exemption_code_missing`, no list → `code_list_missing`, not listed → `code_not_in_list`: the invoice waits, SAT-D147/D148). `BuyerDocumentRequired` flags that the invoice also waits for the order's document (no, date). |
| Calculator | ONE calculator, `TaxMasterCalculatorV1` (Application): timelines (`DefaultInForce`, `ListInForce`, `ThresholdInForce`, scheduled), `Resolve(data, itemType, buyerSituation, on, exemptionCode)` (unknown situation → `buyer_situation_unknown`; no item type → `Unresolved` `product_item_type_missing`; no default → `item_type_default_missing`; codes `Resolved` / `Unresolved` `exemption_code_missing` / `withholding_code_missing` / `code_not_in_list` / `code_list_missing`) and `WithholdingShare` (percent / 100, exact). Amounts are NOT computed here: the consumer passes the VAT percent and the share to its single amount calculator — Sales `SalesOrderTotalsCalculator` (line amount rounded once, SAT-D76; VAT per line rounded; withholding = the ROUNDED line VAT × share, rounded, SAT-D75). Golden cases: `SalesTaxMasterGoldenTests` (incl. the SAT-D139 demir line 1000 × 25.505). |
| Line tax port | `ISettingsLineTaxResolverV1` (`SettingsTaxMasterReader`): `ResolveAsync(productId, buyerSituation, on, exemptionCode?)`, `GetItemTypeDefaultAsync(itemType, buyerSituation, on, exemptionCode?)` ("the default for this item type"), `GetBuyerSituationsAsync(on)` (SAT-D158), `GetCodeListAsync(kind, on)`, `GetWithholdingThresholdAsync(on)`; a malformed situation or exemption code is `settings_tax_master_request_invalid`. The product's item type is the shared card's (SAT-D149). Company and actor are the server's; access first (`settings_access_denied`); the product's item type comes from the Product owner through the host adapter `SettingsProductItemTypeHost` (`ISettingsProductItemTypeSourceV1` → Product `IProductItemTypeReaderV1`; Settings reads no Product table); an absent or failing source is `settings_provider_unavailable` (503), never a default. Not wired into the Sales order line in this slice (follow-up). |
| Authority (SAT-D83) | ONE name, `settings.tax-master.manage` (`SettingsTaxMasterPermissionManifestV1.TaxMasterManage` = `ErpPermissions.SettingsTaxMasterManage`): company-wide, not Critical, granted to NO role (Muhasebe/Finans + Admin personas are the intended holders — a human grant gate). Measured first and fresh before every save. |
| Commands | One person (SAT-D68 precedent): Idempotency-Key (stored terminal answers replay; another body is 409 `…_idempotency_conflict`), the series' / list's / threshold's version as decimal text (409 `…_version_conflict`; "0" while only the catalog answers), a mandatory reason ≤ 4000, correlation/causation, ONE audit event per real change (`settings.tax-item-type-default.recorded/corrected/withdrawn.v1`, `settings.tax-code-list.published/corrected/withdrawn.v1`, `settings.tax-withholding-threshold.recorded/corrected/withdrawn.v1`, `settings.tax-buyer-situation-list.published/corrected/withdrawn.v1`) with the whole reason in the after-image; a no-op writes none. Another company's record is the same code-less 404. |
| Routes | `GET /api/v1/settings/tax-master`, `POST …/item-type-defaults/{Steel|Scrap|Service}`, `POST …/item-type-defaults/records/{recordId}/withdrawal`, `POST …/code-lists/{Exemption|Withholding}/versions`, `POST …/code-lists/{kind}/versions/{listVersion}/withdrawal`, `POST …/withholding-threshold`, `POST …/withholding-threshold/records/{recordId}/withdrawal`, SAT-D158 `GET …/buyer-situations`, `POST …/buyer-situations/versions`, `POST …/buyer-situations/versions/{listVersion}/withdrawal`. Private, no-store. Screen: Ayarlar > Vergi (`/ayarlar/vergi`, shell provider 1.1, `AY-VRG-01`) shows the default per item type with its source (catalog / Settings record). |
| Persistence and rollback | Migration `20261031194631_AddTaxMaster` (`dotnet ef`): ten tables, CHECKs and composite company foreign keys. `Down` refuses (THROW **51131**) while any default record, code list or threshold exists, before any drop (forward fix); on an empty store it drops the tables. Migration `20261101143556_AddTaxBuyerSituationLists` (`dotnet ef`, SAT-D158): `Down` refuses (THROW **51153**) while any buyer situation list exists. |
| Evidence | Unit: `TaxMasterCalculatorV1Tests`; Sales golden: `SalesTaxMasterGoldenTests`; host: `SettingsHostWiringTests`, `SettingsTaxMasterPermissionRegistrationTests`, `SettingsShellAuthorityTests`; real SQL: `SettingsTaxMasterSqlServerRelationalAcceptanceTests` (routes, 403, isolation, replay, catalog base, same-date replacement + withdrawal without resurrection, threshold, audit, the Product item type → line tax port end to end with buyer situations, both rollback refusals); web: `TaxMasterPage.test.tsx`, `taxMasterOpenapiContract.test.ts`. |

Decided (Sinan, 2026-10-31, EK19): per-company tables (SAT-D133); item type on the product, no tax class (SAT-D134); the VUK
232 threshold is a dated Settings value starting empty (SAT-D135); the per-item-type defaults (SAT-D139); the buyer situation
is an order input (SAT-D140). Open (NEEDS-SINAN, not chosen here): the exemption / withholding codes and the threshold value
(mali müşavir); the export-registered and exemption-certificate rules; who receives `settings.tax-master.manage` and
`product.item-type.manage`. Follow-ups: the order's buyer situation and the per-line override (Sales); the system's per-line
suggestion from documents (SAT-D93).

## Sales quote policies (SAT-D25, SAT-D39)

Source: `docs/decisions/SINAN_KARARLARI_2026-10-27_SATIS_EK3.md` (SAT-D25) and `…_EK4.md` (SAT-D39).

`ISettingsSalesQuotePolicyProviderV1` is a separate V1 port beside `ISettingsSalesPolicyProviderV1` (so no existing implementer
changes): the company's closed **quote loss reason list** (`GetSalesQuoteLossReasonsAsync`: per entry a stable id, code, Turkish
label, the list's own `RequiresExplanation` flag — SAT-D25's "Diğer seçilirse açıklama zorunlu" is a flag of the entry, never a
recognised label — and `IsActive`) and the **default quote validity** in days (`GetSalesQuoteDefaultValidityAsync`), both versioned
and company-scoped with a window. `ApprovedSettingsSalesQuotePolicyProvider` serves `ApprovedSalesQuotePolicyCatalog` in the Sales
policy port's order: access → `settings_access_denied`; a company without an approved Sales policy source →
`settings_provider_unavailable`; no record → `settings_sales_policy_not_found`; otherwise the exact version with its real window.

The reviewed catalog is **empty on purpose**: the list content and the day count are Settings records Sinan enters (EK4 "açık
kalanlar"); no value is seeded or a code constant. The served companies (GDC, GSP, GIN — SAT-D14) therefore answer "not found":
Sales refuses a loss (fail-closed) and suggests no validity. A malformed catalog (empty ids, blank or untrimmed code / label, too
long for the consumer's snapshot, repeated id or code, a list without entries, a non-positive day count, overlapping windows) is
refused as a whole with `settings_profile_invalid`. Host registration: `Program.cs`, scoped. Evidence:
`tests/GoymenERP.Settings.UnitTests/ApprovedSalesQuotePolicyProviderTests.cs`.

## Manufacturing parameter and tolerance profiles (Üretim U1-a: API-01/02, MOD-01)

Decisions: URT-D6, URT-D7, URT-D9, URT-D10 (`docs/decisions/SINAN_KARARLARI_2026-11-01_URETIM.md`), delegation No 86,
D-MFG-PROFILE-AUTH-01=A (the existing `ManufacturingToleranceProfileLifecyclePolicyV1`, reused for both families).

- **Parameter profiles** — `settings.ManufacturingParameterProfiles` (one header per version of a company × process ×
  optional work center; lifecycle Draft → Approved → Published, `StateVersion` fence) and
  `settings.ManufacturingParameterProfileValues` (one typed row per key: number `decimal(18,6)` or choice, with the unit). The
  keys are the engines' own input names, declared in `ManufacturingParameterKeysV1` with unit, kind (Positive, NonNegative,
  RatioUpToOne, WholeCount, Choice), scale and allowed processes; a new key needs no migration.
- **Read port** — `ISettingsManufacturingParameterProviderV1.GetManufacturingParametersAsync(process, workCenterId?, on)`
  serves only published versions and the reviewed version-0 catalog: a work center's own covering version wins, else the
  process version; within a scope the latest `EffectiveFrom`, then the highest version. Codes: `settings_access_denied`,
  `settings_manufacturing_parameter_request_invalid`, `settings_provider_unavailable`,
  `settings_manufacturing_parameter_profile_not_found`, `settings_profile_invalid`. Since U1-b Manufacturing consumes it in plan
  preview, save, read and approval and snapshots it at approval (Manufacturing README, "Current state").
- **Catalog** — `ApprovedManufacturingProfileCatalog` (GDC/GSP/GIN, from 2026-11-01, version 0): density 7850 kg/m³
  (Slitting, ColdRolling, CutToLength), Paint line speed 20 m/min, 80 µm, solids 0.55, loss factor 1.10, 3 simultaneous
  profiles; kerf 0 mm (CutToLength) and edge trim 0 mm (Slitting). Not shipped (undecided, unknown — never 0): kerf mode,
  head/tail trim, coil diameters, metre tolerance, slitting/tube line speeds (entered on the plan).
- **Tolerance write path** — `settings.ManufacturingToleranceProfileVersions` holds the lifecycle; publication inserts the
  immutable `settings.ManufacturingTolerancePolicies` row (gains nullable `MassBalanceDifferenceTolerancePercent`, URT-D6)
  in the same transaction.
- **U1-b (URT-D6, URT-D23) — line scope, catalog live, supersession.** A tolerance version may carry an optional `WorkCenterId`
  (request and view, additive; versions are still numbered per process across its scopes). `GetScopedManufacturingToleranceAsync
  (process, workCenterId?, on)` (a default interface member over the process-only read, overridden by
  `SettingsManufacturingToleranceProviderV1`) serves only a window covering the date: the line's published version, else the
  process's, else the reviewed version-0 catalog (`Source = Catalog`, fire 5 %, mass balance ± 2 %, from 2026-11-01). The
  process-only read is unchanged (it ignores line versions and never serves the catalog). The registry trigger keeps windows of
  one scope from overlapping. **Supersession:** publishing a successor that starts after an open-ended published predecessor of
  the same scope closes the predecessor's window on the day before, in the same save — the ONLY permitted change of a published
  registry row (the trigger checks every other column unchanged and `NULL → date ≥ EffectiveFrom`; any other update or delete
  is still 51002; the context's write fence admits the update only beside the successor's publication). Chosen over a new
  "superseded" table or reader-side implicit windows because the stored window then always equals the served window, the
  close decision snapshots it as is, and immutability stays one trigger rule. The lifecycle row keeps its prepared window; the
  list/get view shows the registry's window for published versions. A bounded predecessor that overlaps, or a successor that
  starts on or before the predecessor, is still refused (`settings_manufacturing_profile_window_overlap`).
- **Routes** — `GET/POST /api/v1/settings/manufacturing-parameter-profiles`, `GET …/{id}`, `POST …/{id}/approval`,
  `POST …/{id}/publication`; the same under `/api/v1/settings/manufacturing-tolerance-profiles`. Names (registered, granted to
  no role, company-wide, not Critical): `settings.manufacturing-parameter-profiles.read|prepare|approve|publish`,
  `settings.manufacturing-tolerance-profiles.read|prepare|approve|publish`. Approver ≠ preparer, publisher ≠ preparer; no
  version may take effect before today. Idempotency-Key, expected version text, reason ≤ 500, one audit event per change.
- **Migration** `AddManufacturingParameterProfiles` (dotnet ef + hand-written SQL): guard triggers (51181 header, 51182 values,
  51184 tolerance versions), the registry trigger re-created with the new column, `Down` refuses with `THROW 51183` while any
  profile or mass-balance value exists (forward fix). U1-b `AddManufacturingToleranceLineScope`: `WorkCenterId` on both
  tolerance tables, the registry trigger (scope windows, window-close update) and the lifecycle guard re-created; `Down`
  refuses with `THROW 51185` while any profile exists or a registry row carries a line, then restores the U1-a triggers.
- **Open points (after U1-b).** Plan-level authorized tolerance override (URT-D6 "planda yetkili değiştirebilir") needs a new
  permission and a plan field — not built (human grant gate). The close's tolerance date is the UTC close date while profile
  dates are Europe/Istanbul business dates (the same as before U1-b). Work center ids are not validated against Organization
  (as for parameter profiles). A predecessor whose window was closed by a successor shows the registry window in the view but
  its lifecycle row keeps the prepared one.

## Exchange-rate record (DEC-FIN-010; KUR_GUNU SAT-D54/D55; EK24 SAT-D169…D172; OPEN-012 partial)

Owner: Settings (master report §16 "Ana Veriler: … kur …", AV-02 "Birim / Para / Kur / Vergi"; Settings already owns the
currency identities and money profiles). Company-scoped; nothing is seeded.

| Part | What |
|---|---|
| Tables | `ExchangeRateDays` (one row per company and calendar day, the edit fence), `ExchangeRateRecords` (append-only: official bulletin answer `Published` / `NoBulletin` / `Unverified` / `NotFound` (a single 404, not yet final), manual correction `Manual`+`Published` with a reason, official half-day answer `FullDay` / `HalfDay` / `Undetermined`, SAT-D198 manual half-day mark `Manual`+`FullDay`/`HalfDay` with a reason), `ExchangeRateRecordRates` (forex selling per ONE unit in TRY, `decimal(28,10)`, never rounded). Records and rate lines are immutable (write fence); the day's current answer of a kind is its highest record version. |
| Migration | `20261102143743_AddExchangeRateRecords` (dotnet ef). `Down` refuses with `THROW 51201` while any record exists (forward fix); an empty store rolls back freely. `20261102192125_AmendExchangeRateRecordStates` (dotnet ef) widens the state check (`NotFound`, manual half-day mark); `Down` refuses with `THROW 51202` while such a record exists. |
| Calculator | `RateDayExchangeRate` (BuildingBlocks/Monetary): SAT-D54/D55 + SAT-D170/D171, fail-closed. |
| Read port | `ISettingsExchangeRateResolverV1.ResolveAsync(rateDay, source, target)` → `ExchangeRateResolutionV1`: the rate as exact text with its provenance (bulletin day, source `Tcmb`, `Official`/`Manual`, record id and version, half-day rule applied, previous full business day) — what an invoice snapshots (SAT-D172); or `Unavailable` with the code and the day that stopped it. Needs an authenticated actor with company access; computed fresh on each call. |
| Names | `settings.exchange-rates.read`, `settings.exchange-rates.correct` (SAT-D169/D188 intended holders: Muhasebe, Muhasebe Müdürü, Genel Müdür, Admin), `settings.exchange-rates.import` (also the re-verification), `settings.exchange-rates.half-day.mark` (SAT-D198; intended holders Muhasebe, Muhasebe Müdürü, Genel Müdür, Admin). Registered, company-wide, not Critical, **granted to no role** (human gate). |
| Routes | `GET /api/v1/settings/exchange-rates?from&to` (≤ 62 days), `GET …/resolution?rateDay&currencyCode`, `POST …/days/{date}/corrections` (204), `POST …/days/{date}/half-day-marks` (204), `POST …/days/{date}/reverification` (204), `POST …/imports` (200 with a per-day summary; ≤ 31 past days). |

**Correction (SAT-D169).** One currency on a day TCMB officially published (any other day is 422
`exchange_rate_day_not_published`: a manual record never makes or unmakes a business day, SAT-D55). Reason (≤ 1000),
Idempotency-Key, the day's expected version, one audit event `settings.exchange-rate.corrected.v1` (before/after value);
the same value again is a no-op. It is a new record next to the official one, so it reaches only documents that read the
rate afterwards. The size limit of a correction is open (OPEN-012).

**Import.** `IExchangeRateBulletinSourceV1` / `IExchangeRateHalfDaySourceV1`. Only past days are asked (today and later are
refused, so the open cut-off hour never matters). An unverified answer never overwrites a definite one; a different definite
answer is reported as `Conflict` and nothing is written; one audit event per run that recorded something.

**Sources — human gate.** Both are **disabled by default** and the repository ships no configuration for them:
- TCMB (`TcmbExchangeRateBulletinSource`): enabled only when `Settings:ExchangeRates:TcmbEnabled=true`; then
  `Settings:ExchangeRates:TcmbBaseAddress` must be an https URL ending in `/` (anything else fails at startup; optional
  `TcmbTimeoutSeconds`). It reads only the dated archive `kurlar/yyyyMM/ddMMyyyy.xml` (404 → "no bulletin" candidate; other
  status, timeout, another day's bulletin or unreadable XML → `Unverified`); `today.xml` is not used because it could fill a
  day with another day's bulletin. No secret is needed. While disabled the import answers 503 (on the wire the platform's
  `service_unavailable`; the list read's `bulletinSourceEnabled` says why) and records nothing.
- A 404 is first recorded as `NotFound` (the resolver treats it as unverified). It becomes `NoBulletin` only when a LATER run
  sees the 404 again while a later bulletin exists (received in that run or already recorded); one transient 404 never turns
  a business day into a holiday.
- **Re-verification** (`POST …/days/{date}/reverification`, under the import name, reason + version + key + audit
  `settings.exchange-rate.reverification-requested.v1`): reopens a day whose final official answer may be wrong (Published or
  NoBulletin) by appending an official `Unverified` record. It chooses nothing — no rate, no business-day decision; the
  resolver stops on the day (fail-closed; a manual rate on it is a manual conflict until then) and the next import asks TCMB
  again under the rules above. The import name, not the correction name: it only re-opens the source's question, which is
  what an importer does; deciding a value stays with the correction. Refused with 503 while the source is disabled (nothing
  could answer the reopened question). The reopening record is an official `Unverified` record carrying the person
  (`recordedBy`), the reason and the reference "Yeniden doğrulama istendi", with its own audit event; the Kurlar screen (UI
  slice) should show it as a person's request, not a source answer.
- **Runbook (review B P3-2):** because a 404 needs a second run, a weekend or holiday becomes `NoBulletin` only on the second
  import that sees it after the next business day's bulletin exists. When the source is opened, schedule the import to run at
  least twice after the next business day's 15:30 announcement (e.g. Monday 15:30 for a weekend); until then the days after
  the weekend resolve to "kur bulunamadı" (fail-closed).
- A day TCMB ever published never becomes `NoBulletin` automatically (review A P2-2): repeated 404s keep it `NotFound`
  (fail-closed) until a bulletin is read again.
- A day whose official answer is already final (`Published` / `NoBulletin`; a definite half-day answer) is never asked again.
- Arife / half day (SAT-D171): no reviewed automatic official source is wired; every day stays `Undetermined`, so the
  resolver chooses no rate ("kur belirlenemedi") until a human enables a reviewed source or a person marks the day (SAT-D198).
- **SAT-D198 manual half-day mark** (`POST …/days/{date}/half-day-marks`, `settings.exchange-rates.half-day.mark`): `FullDay`
  or `HalfDay` with a reason, version, key and audit `settings.exchange-rate.half-day-marked.v1`, only for today or a past day
  (422 `exchange_rate_mark_future_day`) and only while the automatic source has no definite answer for the day (409
  `exchange_rate_half_day_determined` otherwise). A later definite automatic
  answer wins over the mark. Issued documents keep the rate they snapshotted (SAT-D172).

**Follow-ups:**
- **SAT-D199** (EK31): who reads the rates (Satış Uzmanı, Satış Müdürü, Muhasebe, Muhasebe Müdürü, Genel Müdür, Admin, Finans
  Uzmanı, Finans Müdürü) — the persona card for `settings.exchange-rates.read`; the real grant stays a human gate.
- **Rounding of risk conversions**: decided by SAT-D206/D207 (EK34) — the intermediate TL value unrounded, each final
  limit-currency amount rounded half-up to the currency's approved scale, then the rounded amounts summed (the sum is not
  rounded again); implemented in one place (`SalesRiskCurrencyConversion.RoundForRisk`).
- The invoice snapshot (API-32) and the Ayarlar > Kurlar screen.

**Consumers.**
- Sales risk (SAT-D47/D200/D203) converts with the check day's rates through the port (never frozen): foreign → TL ×, TL →
  foreign ÷, foreign → other foreign via TL; a missing rate is "hesaplanamadı" (SAT-D80).
- The invoice-candidate groups (SAT-D90/D101) take the rate of the shipment day from the port and group by bulletin day and
  value; an unresolved rate keeps the shipment day apart.
- Purchasing uses the same rule (ALS-D6).
