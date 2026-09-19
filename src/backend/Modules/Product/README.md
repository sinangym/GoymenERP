# U-01 / U-02 backend contract v1

Source: Business #181 `3f5deca6f1c338d44a4e9b52787c27dbab85846e`,
`PRODUCT_U01_U02_IDENTITY_LIFECYCLE_JIT_SPEC_v1.md` §19 and
[minimum-field arbitration](https://github.com/sinangym/GoymenERP/issues/100#issuecomment-5646263306).
DEC-PRD-003/004/010, Master §7.4 and locked #98/#99 remain binding.
The September 12 pilot grant/revocation decision is Sinan's alone; company admin
membership never grants global Product authority. This is not a named user grant,
Identity ID mapping, schema/runtime acceptance or production authorization.

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
No production Product service, global grant, applied migration, or SQL acceptance
is claimed. These routes are pinned for frontend integration, not live URLs.

List parameters: q (trimmed, max 200, no controls), lifecycle (exact Draft, Active,
Inactive), optional groupId/classId/familyId, roles (comma-separated distinct purchasable/sellable/productionInput/productionOutput), roleMatch (any/all; default any), sort (code/name/updatedAtUtc; default code), direction (asc/desc; default asc), completeness (NotEvaluated/Incomplete/Complete), pageSize (1..100, default 50), opaque cursor.
A role filter matches known true values only; any/all applies to the submitted distinct roles. Unknown is not false. nextCursor=null means the final page; a non-null cursor is the only continuation signal. No total count is advertised. Server ordering uses the selected key and immutable UUID tie-breaker in the selected direction. The persistence
reader applies the same authorized scope and all filters on every page. Cursor
binding/codec is implemented below; host registration and real SQL acceptance remain open.

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

## Product persistence preparation after #237

`Product.Infrastructure/Persistence/ProductCommandIntent` freezes the submitted
request, binds edit target/version/reason, preserves omitted/null and array order,
and sorts JSON object properties before SHA-256 hashing. Numeric lexemes are retained
without floating-point conversion; a differently written numeric value may conflict
rather than being silently treated as equivalent. Duplicate nested attribute members
are rejected. Raw idempotency keys are not retained. Input snapshots are sensitive and
must not be logged or used as an authority decision.

`ProductStoredRecord` captures/restores Product identity, lifecycle/version, tagged
roles and an independent reference JSON snapshot. Unknown/corrupt stored role pairs
and missing reference payloads fail rather than being shown as false/empty.
The snapshot/fingerprint tests themselves do not prove SQL schema, migration,
unique constraints, resolver registration or a transaction. The accepted owner replay
resolver and atomic writer must still reauthorize and commit Product + audit + terminal
together. `AuthorizedMissing` is never a reservation. No global eligibility or
production grant is inferred from this code.

The same continuation now contains `ProductDbContext` / `ProductRow` with SQL Server
mapping for Products and one CodeRegistrations namespace, a version concurrency token,
role-pair/lifecycle/identity/JSON checks, and stable-order indexes. No migration or DDL
has run. SaveChanges fails explicitly until the shared atomic writer enrollment exists;
an arbitrary SQL transaction alone must not enable a partial Product-only commit.

`ProductCommandGateway` consumes the merged #243 resolver (no privileged reader access),
maps conflict without old data, and validates replay schema, operation status, required
body members and Product identity/lifecycle/version. Read authority uses the strict
shared read context. OriginalCorrelationId is retained internally for the owner's host
metadata mapping; no new header or response field is invented. SQL provider/model and
controlled resolver tests are not real SQL/bootstrap acceptance. The central owner must
admit the exact DbContext and Idempotency.Domain edge described by ADR-PRD-001 before
the architecture gate can pass. Global grants and production activation remain closed.

`ProductCodeGroupRow` is an explicitly provisioned catalogue, without a fabricated
default. `ProductCodeCounter` is keyed by prefix so groups sharing a prefix also share
the sequence. Allocation stages counter changes and skips the current/retired code
namespace; the future atomic writer must retry the entire command on a sequence,
code or terminal-key race. Allocation alone never saves or returns an HTTP result.

`ProductReader` composes SQL reads with fresh global authorization before and after
the query. `ProductReadStore` applies all list filters and deterministic keyset ordering;
an EXISTS alias search avoids duplicates. Unavailable completeness evaluation remains
NotEvaluated. Code/name/updatedAtUtc continuation in both directions is verified by SQL
Server provider translation, which is not real SQL execution evidence.
`ProductCursorCodec` authenticates position, normalized query (including page size),
actor, origin and authority revision. The host must supply signing material and a
technical lifetime. No key is committed, generated as a production default or registered
in the host. Reference labels outside the available code-group catalogue remain null.

`ProductCodeOnlyReferenceCatalog` validates the actual SQL code group for the minimum
Draft. Missing optional class/family/unit/template/tracking values remain permitted;
supplied references without an accepted catalogue adapter fail validation. The read
options endpoint explicitly lists all five catalogues as unavailable, rather than
claiming that they are valid empty catalogues. This does not complete the separate
selected-family/unit/template sub-slice. No default group or fake reference is seeded.

## Draft transaction participant (source; schema/SQL admission still pending)

`ProductService` implements the existing application interface. It delegates reads
to `ProductReader`; writes use `ProductCommandGateway` and the host implementation
of `IProductDraftCommandRunner.RunAsync(ProductPreparedCommand, CancellationToken)`.
Prepared replays also reach the host for fresh authority and original-correlation
metadata. There is no permissive/default runner or host registration in this module.

`ProductDraftParticipant.EnlistAsync(DbConnection, DbTransaction,
GroupSharedMasterCommandContext, CancellationToken, TimeProvider? = null)` borrows the
owner's already-open handles. It rejects a foreign transaction/closed connection and
creates only a fresh Product context. `ExecuteAsync(ProductCommandIntent, CT)` consumes
the frozen envelope, validates catalogue/version/lifecycle, stages exactly one Product
change and flushes that context. Create also stages exactly one code registration and
prefix counter; edit cannot change the catalogue/counter/code tables. A participant
executes once; every retry needs a new participant/context. Ordinary SaveChanges stays
closed, including all public overloads. Disposal never owns commit, rollback or close.

`ProductDraftWriteResult` contains Response, StatusCode, ResponseJson, ProductId,
VersionBefore/After, BeforeJson/AfterJson, Reason and EventType. Create returns201 and
`product.draft.created.v1` with the system action label `Ürün taslağı oluşturma`;
edit returns200 and `product.draft.basic-edited.v1` with the normalized submitted reason.
The host supplies canonical source/actor/origin/correlation/causation/AI metadata.
No audit or terminal table is mapped or written by Product.

The returned participant result is **uncommitted**. The accepted shared host must
flush canonical audit and the matching global terminal before its sole commit;
any exception rolls back the entire attempt. Product-row concurrency becomes409
version_conflict; counter concurrency and unique-key races remain whole-attempt
host retries, with a fresh resolver after rollback. Borrowed SQL handles alone are
not evidence of audit/idempotency atomicity. Public persistence and real SQL execution
remain closed until the #104 source/schema gate and shared implementation are accepted.

## Sales eligibility runtime (operational selection)

`ProductSalesEligibilityService` implements `IProductSalesEligibilityV1` (#255 port) for an
operational Sales selection. It is not the global master read and borrows no global Product
grant. K-1(a) (Sinan's decision, Business #66 5719251344): it requires an authenticated actor
with access to the server-resolved company AND the narrow `product.operational-selection.read`
permission for that same company, measured through the central fresh-authority port
`IErpAuthorityRevalidationService` (#276) for the freshly read company and never from request
data. The consuming module's own selector permission is still
required on its side (Sales keeps a default-deny binding).

What each of those three means in one request, measured rather than assumed:

- **Company and actor are live.** They are re-read on every fenced revalidation, so a change
  mid-flight turns the result in hand into 403.
- **The permission is measured again at every fence.** The service asks the central
  revalidation port (#276), which re-reads the stored authority and replaces the request's
  cached answer. A grant revoked by another request while this one is in flight is therefore
  seen at the next fence and turns the result in hand into 403; it cannot come back later in the
  same request. This closes the per-request limit measured in #265 5719822744. The price is one
  authority read per fence (five for a full evaluation). The answer is used as the cached path
  uses it (`PermissionScope.IsGranted`); this service decides no scope semantics. A grant added
  mid-request is seen the same way; it opens nothing the stored authority does not say.
- **The guarantee is per awaited read.** A read's result, success or failure, is released only
  after the check at that point. Values derived locally afterwards — preconditions, the
  order-unit comparison, the restore integrity fault, the final snapshot — are returned
  without a further check; the window after the last fence cannot be closed here.

The narrow name is declared in `ProductPermissionManifestV1` and registered centrally in
`ErpPermissions` (#277) — registered, not granted: no seeded role or assignment carries it. This
slice does not touch `ErpPermissions`, the Identity seed, the shell capability manifest, DI/host
wiring or OpenAPI. Granting it is a human's step; until then every caller is denied — the
decision's default-closed rule, not an implicit grant.

Order and outcomes:

| Step | Outcome |
|---|---|
| Scope missing/foreign | `ProductOperationException` 403 `product_access_denied`, before any read |
| Narrow selection permission missing for the server company | `ProductOperationException` 403 `product_access_denied`, before any read; same code, no separate signal |
| Company or actor changed during any read | `ProductOperationException` 403 `product_access_denied`, replacing that read's result or failure |
| Permission revoked while the request is in flight | `ProductOperationException` 403 `product_access_denied` at the next fence, replacing the result or failure in hand (re-measured through #276's port) |
| Null selection from a caller without the permission | `ProductOperationException` 403 `product_access_denied`: the gate runs before the argument is looked at |
| Stored record cannot be restored | `InvalidOperationException` |
| Product missing / empty id | error `productId invalid_reference` |
| Draft or Inactive, unknown/false sellable, stale version, missing stock/sales unit | `ProductSalesPreconditions` errors |
| Order unit is not the Product's own sales unit | error `orderUnitId unsupported_order_unit` |
| Family-required rules | `IProductFamilyRequirementPolicyV1`. GDC band/coil (`GdcBandCoilFamilyRequirementPolicyV1`) returns its field errors; a card of any other family, or missing authorised data, is 503 `product_family_policy_unavailable`; a null answer or a malformed definition is 503 `product_family_policy_invalid` |
| Stock/sales unit profile inactive, not effective on the date, or stock not kilogram | error `references.units.stock` / `references.units.sales` `invalid_reference` |
| Settings provider failure | `SettingsPrerequisiteException` with the provider's code (`settings_access_denied`, `settings_provider_unavailable`, …); `settings_profile_invalid` only for malformed, foreign or self-contradictory profile data |
| All pass | `ProductSalesSnapshotV1` with the owner's exact profile ids/versions; order unit = sales unit |

### Error channel

The channel is part of the port contract (`IProductSalesEligibilityV1` documents it and
`ProductSalesEligibilityServiceTests` pins it). A result with `Errors` is the owner's answer about
this selection; an exception is not.

Codes that arrive in `Errors` at this base:

| Field | Codes |
|---|---|
| `productId` | `invalid_reference`, `lifecycle_conflict`, `not_sellable` |
| `expectedProductVersion` | `version_conflict`, `invalid_version` |
| `references.units.stock`, `references.units.sales` | `invalid_reference` (binding missing, or the owner's profile cannot be used on `EffectiveOn`) |
| `orderUnitId` | `invalid_reference`, `unsupported_order_unit` |
| `references.attributes.{fieldId}` (GDC band/coil) | `required`, `family_invalid_value`, `family_precision_exceeded`, `family_not_controlled` |
| `references.units.stock`, `references.units.sales` (GDC band/coil) | `required`; `family_unit_path_mismatch` (sales only) |

- `version_conflict` and `lifecycle_conflict` arrive in `Errors`; neither is thrown, and
  Product.Domain's `ProductVersionConflictException` never crosses this read path.
- Several errors can arrive together and their order carries no precedence. The consumer maps
  them; Product does not publish a separate unit code.
- `SettingsPrerequisiteException` reaches the consumer unchanged, after the same company/actor
  fence as every other read: Product neither reinterprets nor renames a Settings code (including
  `settings_unit_profile_not_found`). A profile the owner does return but that cannot be used on
  `EffectiveOn` is an answer about this selection, so it is an error, not this exception.
- How a consumer turns these channels and codes into its own public answers is that consumer's
  contract, not Product's. For Sales it is defined in `src/backend/Modules/Sales/REFERENCE_READ_V1.md`
  (and `CONTRACT_V1.md`); this README does not restate it.
- `InvalidOperationException` (corrupt stored record) and `OperationCanceledException` are not
  owner outcomes; the platform handles them.

Fail-closed limits at this base, not policy choices:

- The first family's rules are accepted and implemented; its **data** is not there yet. See
  "GDC band/coil family policy" below: until the authorised family, template-field and
  dictionary source exists, the shipped definition source answers 503 ("veri eksik"), so an
  otherwise valid Active selection is still never usable. No other family is accepted, and
  nothing here infers one.
- No offered-unit or conversion policy is accepted (U-08), so only the sales unit is offered.
- Approval (Draft -> Active) exists since the activation slice below; until a person holds the
  global `Activate` grant, real rows stay Draft and are rejected with `lifecycle_conflict`.
- The runtime is not yet composed in the API host. Host wiring (DI, the reviewed-source
  fingerprint and its composition test) is the central writer's; the name is registered (#277)
  and a real grant is a human gate. Until then the Sales product
  read never reaches this runtime: with its default binding (no product selector permission) it
  answers 403 before looking for a runtime, and only once that permission is bound would a
  missing runtime answer 503 `product_provider_unavailable`. K-1(a) is not complete until the
  code, the independent reviews and CI are done; this slice is the Product-side part.

Candidate/offered-unit enumeration for a company user is not implemented: without an accepted
family policy its only honest answer would be 503.

## Stock eligibility runtime (T07, operational selection for a stock operation)

`ProductStockEligibilityService` implements `IProductStockEligibilityV1`, the owner's answer to "may this Product at this version be stocked on this date, and under which stock-unit profile?" It follows the sales port's pattern and channels. Only the stock rules are asked:

| Rule | Source | Answer |
|---|---|---|
| Only an Active card | DEC-PRD-010 ("a draft card cannot be used in a definitive transaction") | `productId` `lifecycle_conflict` |
| The expected version | port pattern | `expectedProductVersion` `version_conflict` / `invalid_version` |
| A stock unit binding exists, and its Settings profile is usable on the date **and kilogram** | DEC-PRD-007 ("the main stock quantity is kg") | `references.units.stock` `invalid_reference` |
| The card's family-required policy | as for sales: a card is a valid card of its family or not | the policy's own field errors; `503` while the family's data is missing |
| **No role** | no source ties stocking to Purchasable / Sellable / ProductionInput / ProductionOutput | nothing is invented; a card that is not sellable can still be stocked |

- **Not read.** The sales unit and the sales role are sales' questions; for stock they are neither checked nor read. A family whose accepted rules require a sales binding still refuses a card without one, through the family policy (the GDC band/coil rule does).
- **Authority.** The same narrow `product.operational-selection.read` permission, measured afresh through `IErpAuthorityRevalidationService` before anything is read and after every awaited read: four measurements for a full evaluation. The refusal is the one `product_access_denied` 403. Inventory's own permission is still required on Inventory's side.
- **Code.** The fence code is repeated from the sales runtime on purpose, so the reviewed sales runtime is not touched. Merging the two is a later refactor with its own review.
- **Not wired yet.** The host registration is a central step, together with C3. The port is **not registered** and no Inventory command calls it yet.
- **Tests.** `ProductStockEligibilityServiceTests` reuses the sales tests' synthetic world.

## Shell capabilities (the normal screens)

`ProductShellCapabilityProvider` publishes Product's four screens to the shell, so an actor holding the global Product authority reaches them in the normal runtime:

| Capability | Route | Screen | Granted by (global Product authority) |
|---|---|---|---|
| `product.items.list` | `/urun` | U-01 | `Read` |
| `product.items.detail` | `/urun/:productId` | U-01-360 | `Read` |
| `product.items.create` | `/urun/yeni` | U-02 | `DraftCreate` |
| `product.items.edit` | `/urun/:productId/duzenle` | U-02-EDIT | `DraftBasicEdit` |

- **No company permission and no new permission name.** The central registry accepts a Product capability only with its exact operation, owner `PRODUCT`, single-company context and no company permission. The shell grants it from the explicit global Product assignments, the same authority every Product API call already enforces.
- **Unconfigured means nothing is shown.** Without the Product runtime configuration there is no Product authority, so no screen is granted.
- **C-01 stays list-only.** The disposable C-01 browser stack replaces the providers and remains Counterparty list-only.
- **Evidence.** A registry test, a host test, the C-01 activation test and a real-SQL grant/revoke test through the real provider. The browser evidence comes with the U-02 suite (BROWSER-CI stage 2).

## GDC band/coil family policy (first accepted family)

Source: Business #265 5722701085 (Sinan Ö1/Ö2 of 2026-09-18, #66 5722637822). The rule set is
accepted; `GdcBandCoilFamilyRequirementPolicyV1` encodes it exactly:

| Rule | Field | Answer when broken |
|---|---|---|
| Nominal thickness > 0 mm, at most 3 decimals | `references.attributes.{thickness field}` | missing/null → `required`; not a number or ≤ 0 → `family_invalid_value`; 4th decimal → `family_precision_exceeded` (refused, never rounded) |
| Nominal width > 0 mm, at most 3 decimals | `references.attributes.{width field}` | same |
| Controlled quality | `references.attributes.{quality field}` | missing/null → `required`; not an owner-listed reference → `family_not_controlled` |
| Controlled coating status; "kaplamasız" is a valid stated status, unknown/null is not | `references.attributes.{coating field}` | missing/null → `required`; not listed → `family_not_controlled` |
| kg→kg, no conversion: the sales unit is the stock unit | `references.units.stock`, `references.units.sales` | a missing binding → `required` (never a silent pass); different units → `family_unit_path_mismatch` on `references.units.sales` |

- Colour is meaningful only for a painted family; nothing is required of this one.
- Every broken rule is reported together; order carries no precedence.
- Draft/Inactive, the sellable role, the Settings profiles (stock must be kilogram) and the
  authority fences stay the eligibility read's: a positive answer needs all of them together.
- **Data, not policy, is missing.** Which family the rules cover, which template fields hold the
  measures, and the controlled quality and coating lists come from the authorised owner through
  `IGdcBandCoilFamilyDefinitionSourceV1` (`GdcBandCoilFamilyDefinitionV1`). None exists yet, so
  the shipped `UnavailableGdcBandCoilFamilyDefinitionSourceV1` answers 503
  `product_family_policy_unavailable`. The policy is not reopened; the data is brought.
- A card of another family, or with no family, is 503 `product_family_policy_unavailable`, never
  "no errors". A malformed definition (empty family, blank or duplicate field, empty list,
  uncoated status not listed) is 503 `product_family_policy_invalid`.
- The codes are the family policy's own (`ProductFamilyPolicyCodesV1`), not the draft edit set
  (`ProductFieldCodesV1`). Sales maps them like any Product field error.
- Not in this slice: the authorised definition source, host registration (C3) of the eligibility
  runtime or of this policy, and any grant. Tests use an isolated synthetic definition only;
  nothing is seeded.

```powershell
dotnet test tests/GoymenERP.Product.UnitTests/GoymenERP.Product.UnitTests.csproj --filter FullyQualifiedName~ProductSalesEligibilityServiceTests
```

## Card approval (Draft -> Active, DEC-PRD-010)

Card: #66 5735046942, approved by the coordinator. DEC-PRD-010 (KİLİTLİ): a Draft card is not used in a
firm transaction; "Stok/planlama rolü onaylayabilir; yetki ayarlanabilir."

| Rule | Where |
|---|---|
| Only Draft becomes Active, at the version the approver read; one version up; every card value kept | `Product.Activate`, `ProductRow.ApplyActivation` |
| A stale version is `409 version_conflict`; a card that is not a Draft is `409 lifecycle_conflict`; an unknown card `404`; an Active card is not edited as a draft (`409 lifecycle_conflict`) | `ProductDraftOperation` |
| Authority is the global Product operation `Activate`, measured fresh on every attempt and again before commit (a revocation in flight is `403`, nothing written); no company permission | `ProductCommandGateway`, the host runner |
| `POST /api/v1/products/{id}/activation` with `{expectedVersion}` and `Idempotency-Key`: the same key and request replays the stored `200` with `Idempotency-Replayed` and the first correlation; another request under the key is `409 idempotency_conflict`; two racing callers of one key leave one transition, the loser replays it | the shared command boundary (`product.card.activate.v1`) |
| One canonical audit event `product.card.activated.v1`, before/after card, in the same transaction | the host's audit session |

- **No completeness precondition.** No source makes one; the eligibility reads (sales/stock) still judge
  the whole card at the moment of use, and without the GDC family data they stay `503`.
- **Not here:** maker != checker (not in the sources), Excel import, deactivation (Active -> Inactive),
  bulk approval, the UI and its shell control (the registry's global-Product table and control rule are
  central; they come with the U-01-360 control slice), the role mapping and any grant (a human decision).
- **ADR-011 §7 terminal replay** of a refusal (stale version, not found) is not stored by the shared
  Product command boundary: only a success is a terminal there, as for the draft writes. A retried refusal
  is computed again (and still refused). This slice does not change the shared boundary.

**Central (CARİ), before this compiles:** `GroupSharedMasterOperation.Activate` (BuildingBlocks); Identity
`ProductMasterOperation.Activate = 4`, the `CK_ProductMasterAssignments_Operation` migration and the explicit
mapping in `SqlProductMasterAuthorizationService` (its allowlist admits `Activate` and its default branch
becomes an explicit case — today `_ => DraftBasicEdit` would read an Activate check as a DraftBasicEdit grant);
`UnconfiguredProductService.ActivateAsync` in `ProductRuntimeExtensions`; guard pins; the relational
verifier's five `ProductActivationSqlServerRelationalAcceptanceTests` names; OpenAPI (`ActivateProductV1`).
