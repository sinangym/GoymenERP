# Purchasing (PUR-1 + PUR-2 — first independent slices of T08)

Source of truth: #275 v2 plan §7.2, §13, R3 and Ö2 (`SINAN_KARARLARI_2026-09-18_O1_O8_VE_SON_ASAMA.md`), read at exact head `a5b8143b`; the approved GDC kg unit profile (#279, #284) for unit verification.

## What is here

### Domain (`GoymenERP.Modules.Purchasing.Domain`)
- `Quantity` — exact, non-negative, one unit; kilograms carry at most three decimals, pieces are whole (Ö2). Extra digits are refused, never rounded or cut; trailing zeros are dropped so equal quantities print equally.
- `PurchaseRequest` (+ lines) — the demand: Draft → Submitted. Not a commitment.
- `PurchaseOrder` (+ lines) — the commitment towards one supplier Party of one company: Draft → Approved.
  - **General/technical breakdown:** a general line can be carved into technical lines while Draft and after approval (§7.2: the technical breakdown becomes known later); general remainder + technical slices never exceed the first commitment, an accepted general share cannot be sliced again, a technical line cannot be broken down, and a technical line keeps the general line's unit — only its product may differ (§13: 1.000 → 925 general + 75 technical). Whether a post-approval breakdown needs re-approval is §8 policy and is not chosen here.
  - **Acceptance shares:** an accepted share never exceeds the line's remaining commitment; remaining = committed − accepted is future supply (§13: 100 ton, 40 accepted → 40 + 60, never 140). A duplicate acceptance id is refused.
  - `expectedVersion` on every mutation (`PurchasingVersionConflictException`).
- **Stable rule codes (PUR-2, ORG #278 P3-1):** every refusal is a `PurchasingRuleException` with a `PurchasingRuleCodes` code; tests assert the code, not only the type. The aggregate is the single authority for every share bound; the line-level checks are a second guard that reports `purchasing_internal_invariant`, so a test can tell which guard held.
- **Read-only views (PUR-2, ÜRÜN #278 P3-1):** `Lines` and `Acceptances` are read-only wrappers; a cast cannot reach the backing lists.
- Supplier Party, Product and the line's **Settings unit** (`UnitId`) are `Guid` identities only.

### Application (`GoymenERP.Modules.Purchasing.Application`, PUR-2)
- `PurchaseUnitVerifier` — verifies a line's unit and quantity against the Settings unit profile through `ISettingsPrerequisiteProvider` (Contracts.V1) before a line is added (ÜRÜN #278 P3-2; Ö2):
  - fail-closed: every Settings failure is a refusal with the owner's code kept verbatim (`settings_access_denied`, `settings_provider_unavailable`, `settings_unit_profile_not_found`, `settings_profile_invalid`) — "denied", "unavailable" and "no profile" stay distinct;
  - the profile must belong to the server-resolved company the caller passes, name the requested unit, be internally valid and active on the effective date;
  - only kilogram profiles are approved today (Settings README, Ö1): a profile of another dimension is refused as `purchasing_unit_unsupported`, never mapped to a guessed count unit;
  - precision comes from the profile's scale, capped by Ö2's kilogram rule of 3; more decimals are refused (`purchasing_quantity_scale_exceeded`), never rounded. The result carries both the owner's `ProfileScale` and the applied `Scale` = min(profile, 3), so an input limit built from the result matches what the domain accepts (PUR-3, ÜRÜN #288 P3-2).
  - The "no profile" code is the owner's `SettingsPrerequisiteCodesV1.UnitProfileNotFound` (Contracts.V1, #287), passed through from the port error's `Code`; Purchasing keeps no copy of it (PUR-3).
- **What the domain does not enforce (PUR-3, ÜRÜN #288 P3-1 / ORG INFO-1):** `AddLine` takes a `UnitId` and a `Quantity` as given; it does not check that the id is a verified Settings unit or that `Quantity.Unit` matches the id's dimension (`UnitMismatch` compares quantity units only). The verifier is the single entry for both, and it can only be made mandatory by the first command handler, which does not exist yet (see `dependency_wait`). Until then "verifier before line" is a contract, not an invariant.
- **`BreakDown` has no status guard (PUR-3, ORG #288 P3-1):** it is open in both existing statuses (Draft, Approved; pinned by tests). The former guard was unreachable and would have reported `purchasing_order_not_approved` for a closed order. The slice that adds a third status adds the guard with its own code and test.

## Behaviour kept on purpose — Business decision pending
- **One submitted request can source several full-quantity orders** (`FromRequest` twice). §7.2 does not settle whether that is a supplier split with partial quantities or a mistake; the question is open with Business (#66). The behaviour is pinned by a test and will change only by that decision.

## What this module is not
- No physical effect: no lot, label, location or stock movement; not a second allocation engine (Inventory T09).
- No price, tax or currency (T28); no approval policy (`Approve` is a transition; §8).
- No offers, delivery schedules, invoice matching, HTTP, DI, persistence or permissions.

## dependency_wait
- Acceptance → stock/label: Inventory T09 on main.
- Price/commercial snapshot: T28.
- Persistence: central writer (CARİ); the verifier's result is retained with the line at that point.
- First command handler (Application): the only place that can make the verifier mandatory before `AddLine` (takes the line from `VerifiedPurchaseUnit` only, company from `ICompanyContext`, never from the client) and pin it with a test. Until then nothing enforces "verifier before line" (ORG #288 INFO-1).
- Product purchase eligibility (Purchasable role + Active lifecycle + the unit being that product's purchase unit): a Product-owned port, not yet published (ÜRÜN #288 P3-3). The verifier checks the unit against Settings only; nobody checks that the product may be bought.
- Count/piece units: a Settings-approved count dimension. Until then the domain's `Piece` arithmetic exists but no verified path produces it.
- Request → several orders: Business decision (#66).
