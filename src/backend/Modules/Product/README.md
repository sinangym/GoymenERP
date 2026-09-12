# U-01 / U-02 backend contract v1

Source: Business #181 `3dfe58e745f064a49d52b118361c757a0882807d`,
`PRODUCT_U01_U02_IDENTITY_LIFECYCLE_JIT_SPEC_v1.md` §19 and
[minimum-field arbitration](https://github.com/sinangym/GoymenERP/issues/100#issuecomment-5646263306).
DEC-PRD-003/004/010, Master §7.4 and locked #98/#99 remain binding.

## Exact module HTTP contract

| Operation | Route | Result |
|---|---|---|
| List/search | GET /api/v1/products | ProductPage: items + nullable nextCursor |
| Card | GET /api/v1/products/{id} | ProductCardView; 404 not_found |
| Code groups | GET /api/v1/products/code-groups | ProductCodeGroup[] from authorized real catalogue |
| Reference choices | GET /api/v1/products/reference-options?groupId=...&classId=... | ProductReferenceOptions from authorized catalogues |
| Draft create | POST /api/v1/products | 201 ProductMutationResponse + Location |
| Basic Draft edit | PATCH /api/v1/products/{id}/draft | 200 ProductMutationResponse |

The module endpoint source compiles and is exercised using ASP.NET TestServer with
an explicitly controlled service double. It is NOT mapped into the main host yet.
No production Product service, global grant, DbContext, migration, or SQL acceptance
is claimed. These routes are pinned for frontend integration, not live URLs.

List parameters: q (trimmed, max 200, no controls), lifecycle (exact Draft, Active,
Inactive), optional groupId/classId/familyId, roles (comma-separated distinct purchasable/sellable/productionInput/productionOutput), roleMatch (any/all; default any), sort (code/name/updatedAtUtc; default code), direction (asc/desc; default asc), completeness (NotEvaluated/Incomplete/Complete), pageSize (1..100, default 50), opaque cursor.
A role filter matches known true values only; any/all applies to the submitted distinct roles. Unknown is not false. nextCursor=null means the final page; a non-null cursor is the only continuation signal. No total count is advertised. Server ordering uses the selected key and immutable UUID tie-breaker in the selected direction. The future persistence
reader must apply the same authorized scope and all filters on every page. Cursor
binding/codec remains an explicit persistence dependency, not a client-created offset.

Create minimum: name (trimmed Unicode, 1..250, no controls) and non-nil groupId.
The group must resolve to a real authorized code group with a valid prefix.
classId/familyId are separate nullable references. Roles and references are optional;
provided unknown/incompatible values fail as a whole. Same name is not a duplicate
hard block. Group/code stay immutable in this basic edit; reassignment/code exceptions
require the later explicit alias/code command.

```json
{"name":"Siyah sac","groupId":"f4fc7672-0b46-41ba-a017-393401ba61dd","roles":{"sellable":{"value":false,"provenance":"manual"}}}
```

The UUID above is synthetic example input, not a seeded group.
Roles have purchasable/sellable/productionInput/productionOutput keys. Each read value
is {value: boolean|null, provenance: manual|derived|not-evaluated}. Not-evaluated
requires null. Explicit false is preserved. Write only accepts manual+boolean or
not-evaluated+null; derived provenance is server-only. Omitted create roles are
not-evaluated; omitted edit roles remain unchanged. Derived roles cannot be overwritten.

References: optional templateId + templateVersion (both together), attributes keyed
by FieldId with JSON values validated by the approved template profile, units with
independent stock/purchase/sales/price/production/package UnitIds, trackingPolicyId.
Stock UnitId must resolve to kilogram semantics. There is no assumed conversion,
weight calculation, lot identity, physical quality, stock quantity or price amount.
A missing catalogue must reject supplied references; no permissive provider exists.

```json
{"expectedVersion":"1","changes":{"name":"Siyah sac — düzeltilen taslak","familyId":null},"reason":"Ürün adını ve aile bağını düzeltme"}
```

Edit changes: name, classId, familyId, roles, references. Omitted fields at all levels
remain unchanged. Explicit null clears only nullable scalar references; name/roles/
reference container and attribute/unit collections cannot be null. Template/version
changes with existing attributes require explicit attribute input; hidden attributes
are not dropped. All submitted references are revalidated before mutation. Reason is
trimmed, required, max 1000, no controls. Frontend shows the global impact before save.
Create has no expectedVersion. Edit version is canonical positive decimal string up
to Int64.MaxValue, never a JS Number. Domain owns checked version increment; persistence
must also use a concurrency predicate, rehydrate the stored version and reject stale
writes atomically. Domain-only version is not a substitute for SQL concurrency proof.

Success shape is {card:{product:{id,code,name,groupId,classId,familyId,roles,lifecycle,
version,createdAtUtc,updatedAtUtc},references,completeness},isReplay}. GET card returns
card itself; list items use the same product core. Lifecycle wire values are exactly
Draft/Active/Inactive. Times are UTC ISO-8601 with AtUtc suffix. Completeness carries
evaluation + missingFields; NotEvaluated must not be rendered as complete or activatable.
The accepted provider must compute missing information; examples/tests are not registry
truth. No activation command is included.

Idempotency-Key: exactly one 1..128 printable ASCII token excluding whitespace/comma.
Same logical attempt keeps key/path/normalized payload/actor and accepted origin context.
Current global authorization is required before any read, mutation or replay. An
unknown outcome must not be retried with a new key. isReplay is an explicit response
indicator; replay remains the original successful operation status (create 201, edit 200).

ProblemDetails stable codes: validation_failed (400 + errors[{field,code}]), invalid_cursor
(400), not_found (404), version_conflict (409 + decimal-string expectedVersion/currentVersion),
idempotency_conflict (409), lifecycle_conflict (409). Host authentication/authorization
and unexpected errors retain canonical platform 401/403/500/503 handling. Unexpected
projection faults occur before writing IResult; the host handler redacts details.
Read/mutation responses are private,no-store. Unknown request fields are rejected.

## Code and materialization boundaries

Visible code format: canonical uppercase ASCII [A-Z0-9] prefix (1..32), hyphen,
positive Int64 unpadded sequence. This is an explicit technical format, not a hardcoded
real group list. Turkish display names remain Unicode; a catalogue prefix such as
ÇELİK is unsupported and must be corrected by its owner, not silently transliterated.
ProductCode.Parse rejects alternate/colliding noncanonical forms. Global code/alias
DB uniqueness and allocation are mandatory next persistence work.

No parameterless Product constructor remains. Construction goes through CreateDraft
with non-nil group and validated code/name. Reading default(ProductCode).Value yields
null, not an NRE; the old review's predicted NRE was inaccurate. Projection now calls
ProductCode.Parse so malformed code cannot leave a successful response. Internal Restore validates stored identity/lifecycle/version before projection; tests cover Active/Inactive history, invalid state and version overflow.

## Integration and remaining ownership

- Product Backend: domain/app/DTO/endpoint/reference validation and tests are here.
  IProductService is a narrow Product-owned port, with no runtime implementation yet.
  IProductReferenceCatalog is a reference-validation port, not a seeded catalogue.
- Existing platform Backend: authored solution delta fd20534894a6a4e64b9a2804a1430e766273f252
  registers the existing Domain/Application/test projects. ProductEndpoints.cs now belongs to the existing GoymenERP.Api/Endpoints surface; a separate Product.Api assembly was removed after the architecture guard rejected its extra FrameworkReference. The central Application project reference remains a Backend-authored delta.
- Platform #103/#104: current global authorization subject/scope, separate subject/origin
  canonical audit, durable global command identity and replay binding must be pinned.
  Product-specific candidate permissions are product.master.read, product.draft.create,
  product.draft.edit; they are NOT registry entries or grants. No capability is advertised
  by the main host. Product-owned static route/operation names are defined in source.
- After that source contract is accepted, Product Backend supplies Product persistence,
  code/alias registry, atomic create/edit, real SQL and early API acceptance. Platform
  owner supplies central DI/host/OpenAPI integration without changing the Cari candidate.
- The same Product Claude aligns its decoder/form to this contract and owns real browser
  acceptance. Reserved isolated ports: API26080/SQL26433/UI26173.

## Verification scope

The prior 07a0401 source ran 61 Product tests in CI; the current reference/list-query delta adds seven controlled HTTP cases. Updated final-head results are recorded in the PR. No SQL, real global-authority, browser or activation acceptance is
included. TestServer fixtures are explicit test doubles. The actual final CI must run
the registered Product test project; earlier green CI on e4fdf9 did not run it.

```powershell
dotnet test tests/GoymenERP.Product.UnitTests/GoymenERP.Product.UnitTests.csproj
```
## Frontend consumption completion

Reference options are a single authorized, group/class-filtered response containing
classes/families (id,displayName), units (id,displayName,symbol,isKilogram), trackingPolicies
(id,displayName), templates (id,version,displayName), and unavailableCatalogs. The latter
uses classes/families/units/trackingPolicies/templates keys. An unavailable catalogue
is different from a valid empty catalogue and must not produce made-up choices. Actual
catalogue providers remain an implementation dependency; no synthetic production seed.

Every list/card product projection now carries labels {group,class,family,primaryStockUnit}
and completeness {evaluation,missingFields}. Labels are server-resolved and nullable
when no value or unavailable source exists. The frontend does not fan out UUIDs into
other module reads. Card.completeness is a compatibility projection of product.completeness,
not an independently calculated status. NotEvaluated is not complete; a Complete reference
check still does not advertise an activation capability. Formula/package completeness
that has not been evaluated is not fabricated.

The exact v1 field-error set is ProductFieldCodesV1.All: invalid_name,
invalid_role_provenance, invalid_reference, invalid_code_prefix, template_pair_required,
explicit_attribute_change_required, null_not_allowed, required, invalid_metadata,
invalid_reason, invalid_version, derived_role_readonly, template_required. Frontend
maps these to Turkish resource strings and keeps a generic unknown-code fallback while
preserving the field binding. isReplay remains in the response body; no replay header
is advertised. Lifecycle conflict and reason max1000 remain unchanged.