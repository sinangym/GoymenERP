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
(Unit profiles later got an approved, non-persistent source — see "Approved unit
profiles" below.)

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

Outstanding approved-source data: real currency identities and effective money
profiles, and company/operation/date period eligibility source. Unit profiles now
have one approved source (GDC kg, below); Master 28.1's rounding (`AwayFromZero`)
is still a proposal, not approved data. No TRY2, default-open period, FX or Finance
OPEN-009 permission is inferred.

## Approved unit profiles (T02-a)

Source: Sinan's decisions of 2026-09-18, Ö1 and Ö2 (#66 comment 5722637822;
`docs/decisions/SINAN_KARARLARI_2026-09-18_O1_O8_VE_SON_ASAMA.md` on #275), and
Master §28.1 for the dimension. Task: coordinator T02-a.

`ApprovedSettingsPrerequisiteProvider` is the real provider for what is approved:
unit profiles. Money profiles and period eligibility stay exactly as the unavailable
adapter answers them — the Sales money operation identity is still an owner input
(#66 5715011050).

The one approved profile, `ApprovedUnitProfileCatalog.GdcKilogramV1`:

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
A unit identity is company-independent; the profile is per company. GSP and other
companies have no approved profile yet.

**Data source: versioned source code, not a table.** One profile is approved and
nothing may edit it at runtime; a new approval is a new version, reviewed like code.
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
`SettingsUnitIdentitiesV1.Kilogram` when it lists kg — ÜRÜN's follow-up), money and
period sources, any rounding method, any grant or production data. A returned profile is
reference metadata, not an authorization: the consuming use case still checks its own
permission.

**Host wiring.** The API host registers `ISettingsPrerequisiteProvider` as
`ApprovedSettingsPrerequisiteProvider` (previously the unavailable adapter). Its only
consumer there is the Sales reference read. Sales asks for unit profiles only after a
Product sales-eligibility snapshot, and no such runtime is composed in the host yet, so the
swap changes no reachable Sales answer until it is; money and period answers are unchanged.
Sales currently reports every Settings code other than `settings_access_denied` as `503
settings_provider_unavailable`, so an unknown unit will surface there as 503 — a Sales-side
mapping choice, not changed here. Only the reviewed catalog is reachable from outside
Settings: the catalog-taking constructors are internal and visible to
`GoymenERP.Settings.UnitTests` only.

Validation: Settings unit/contract tests are synthetic, not SQL or browser UAT.
No persistence changed, so new slice-specific SQL scenarios are not applicable;
the existing required CI relational suite remains required. Rollback consists of
removing unused project references; there is no data or migration rollback.
