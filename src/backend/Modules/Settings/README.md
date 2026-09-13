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

Outstanding approved-source data: real unit/currency identities and effective
profiles (quantity/money scale), and company/operation/date period eligibility
source. Master 28.1's kg scale/rounding is a proposal, not approved data. No TRY2,
kg3, default-open period, FX or Finance OPEN-009 permission is inferred.

Validation: Settings unit/contract tests are synthetic, not SQL or browser UAT.
No persistence changed, so new slice-specific SQL scenarios are not applicable;
the existing required CI relational suite remains required. Rollback consists of
removing unused project references; there is no data or migration rollback.
