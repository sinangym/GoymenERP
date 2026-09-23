# Product PURCHASE selection — central handoff

Scope: Product owner implementation on main `34e666b508077f41c8ba7f949b8226b206afe8c4`.
DEC-PRD-001/006/010 and the explicit P-04 owner task. No migration, write, master release,
permission/grant, Purchasing aggregate or conversion policy change. Existing Sales/Stock code is unchanged.

## Ports and composition

Application.Contracts: `IProductPurchaseEligibilityV1.EvaluateAsync(ProductPurchaseSelectionV1, CancellationToken)`
and `IProductPurchaseCandidatesV1.SearchAsync(ProductPurchaseCandidateQueryV1, CancellationToken)`.
Infrastructure.Persistence: `ProductPurchaseRuntimeRegistration.AddProductPurchaseSelection`.
Central writer adds, after existing `AddProductRuntime`, exactly:

```csharp
builder.Services.AddProductPurchaseSelection(p => p.GetRequiredService<ProductRuntimeOptions>().IsEnabled);
```

Existing Product DbContext/ReadStore, released family source, ProductUnitProfileAdapter, company/current-user and
fresh authority services are reused. Disabled runtime or missing Settings/family provider gives
`ProductOperationException("product_runtime_unconfigured", 503)` on both ports. The new registration
is not invoked by this owner PR; final host publication remains central-owned.

## Purchasing adapter (central-owned)

Implement the existing Purchasing.Application.Contracts.IProductPurchaseEligibilityPortV1 in the API
composition layer, referencing both modules' Application contracts. Do not add Product -> Purchasing
Infrastructure or Application dependencies. Fully qualify the two ProductPurchaseSelectionV1 types.
Map ProductId/ExpectedProductVersion/UnitId/EffectiveOn unchanged.

- Snapshot with empty errors -> Eligible and snapshot.ProductVersion.
- Null snapshot + errors -> Refused; copy ProductFieldError.Field/Code into PurchasingFieldError.
  Preserve `expectedProductVersion/version_conflict` for the existing terminal stale-version refusal.
- ProductOperationException status403 or Settings `settings_access_denied` -> Denied, no product metadata.
- ProductOperationException status503 or other SettingsPrerequisiteException -> Unavailable, no positive fallback.
- Cancellation propagates. Unexpected corrupt/inconsistent source failures are not Eligible or Refused;
  preserve the consumer's fail-closed unavailable handling without exposing exception text.

Selection errors: productId invalid_reference/lifecycle_conflict/not_purchasable;
expectedProductVersion invalid_version/version_conflict; unitId invalid_reference/unsupported_purchase_unit;
references.units.purchase invalid_reference (missing or inactive/out-of-window Settings profile).
Accepted family/template/attribute checks are shared by both paths through ProductFamilyRequiredReferencesV2.
Purchase uses IProductPurchaseFamilyRequirementPolicyV1; Sales/Stock retain their existing policy and unit
conditions unchanged. Purchase requires no Sales binding and infers no conversion or alternative family rule.

## Candidate endpoint (central-owned)

Publish a Purchasing selector read beside existing Purchasing endpoints; enforce its existing consumer
read/write authorization as approved for P-04 AND let Product independently enforce company
operational-selection.read. The host chooses the API route; this handoff does not add a grant.
Query: Q (existing Product normalization), required EffectiveOn, Limit default25 range1..100.
Result: Items[{ProductId,ProductVersion,Code,Name,UnitId,UnitCode,ProfileId,ProfileVersion}], SearchWindowFull.
Example shape (synthetic only):

```json
{"items":[{"productId":"b6300000-0000-4000-8000-000000000401","productVersion":"2","code":"SYNS04-1","name":"SENTETIK purchase","unitId":"c5ef643e-a6e8-51f3-89f2-8e68b00cb983","unitCode":"kg","profileId":"4a790fdc-2a6b-5126-835f-d3a13d28b71c","profileVersion":"1"}],"searchWindowFull":false}
```

Limit bounds examined active+purchasable SQL rows, not promised eligible item count. Stable Product
code/id order and existing search normalization; refine search when SearchWindowFull. No rejection
identities/reasons, totals or master cards are returned. Each candidate is re-evaluated from the actual
stored row, family and effective Settings profile; the Settings label and profile come from that same
eligibility read (no later label/profile join). Save must revalidate selected version/unit after replay lookup.
Global Product has no company ownership column: isolation is server company membership, fresh narrow
authority and company-bound Settings data, never an invented company filter or global master grant.

## Evidence and remaining boundary

Unit coverage includes purchase-only role/unit, stale/non-active/missing references, unusable/foreign
Settings, family failure, provider absence, grant revocation, company/actor movement, bounded candidates.
SQL tests use the existing SqlServerRelationalApiFactory, actual Product/family/Identity persistence and
ApprovedSettingsPrerequisiteProvider; synthetic setup only, no real release/grant. Real SQL runs in CI,
not a local DB lifecycle. Product inventory adds three exact facts; central writer owns its reviewed-source pin.

DEC-PRD-006 purchase identity authoring is available in the existing draft POST/PATCH and technical
form. It uses the Settings-owned identity catalogue independently of stock and sales, preserves omitted
values and supports explicit null clearing. Company-effective profile validation still belongs to purchase
eligibility at EffectiveOn; authoring does not activate a draft or grant operational eligibility.
P-04 end-to-end acceptance still needs the central adapter/endpoint/UI and an eligible Active Product.
Synthetic SQL/browser fixtures are not real master acceptance. Q6 real release/UAT remains open.
