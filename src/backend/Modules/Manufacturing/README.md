# Manufacturing (MFG-1 … MFG-5 — independent slices of T15, Dilme/Sarım)

Source of truth: #275 plan v2 §7.3, §13, R3, R6 and `SINAN_KARARLARI_2026-09-18_O1_O8_VE_SON_ASAMA.md` (Ö2, Ö7), read at exact head `a5b8143b`; coordinator decisions of 2026-09-18; the approved GDC kg unit profile (#279, #284) and `Settings.Application.Contracts.V1` for mass verification.

## What is here

### Domain (`GoymenERP.Modules.Manufacturing.Domain`)

- `ProductionPlan` — parent rolls and planned mass, an optional **declared** target (`InputMass` or `GoodOutputMass`); approval records a decision and consumes nothing.
- `ProductionOrder` — created only from an approved plan, with its own snapshot of the plan's inputs and target.
- **Realization facts**, each with its own id and UTC time (R6), with the totals derived from them:
  - `InputIssue`, `InputReturn`: the same id twice is refused (`manufacturing_input_already_issued`, `manufacturing_return_already_recorded`); a return never exceeds what was issued.
  - `OutputDeclaration`: bound to an issued parent roll of this order; a class out of good product, second quality, usable remnant, scrap or process loss; the same id twice is refused.
  - `WipMeasurement`: the latest reading is the roll's WIP; a reading that is not strictly later than the latest is refused (`manufacturing_wip_measurement_not_latest`); the same id twice is refused.
- `MassBalance` per parent roll: `issued − returned = Σ outputs + WIP + unexplained`. The unexplained difference keeps its sign and is never folded into any class ("açıklanamayan fark otomatik fire olmaz").
- `Close` (MFG-4; Business #66 [5728619644](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5728619644), [5730092974](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5730092974), #299 [5730573197](https://github.com/sinangym/GoymenERP/pull/299#issuecomment-5730573197), on DEC-PRDCT-006/007 and Master v3 §28.9):
  - **every parent roll reconciles on its own**: `issued − returned = good + second quality + usable remnant + scrap + process loss + remaining WIP + unexplained`; rolls never offset each other;
  - refused while ANY roll has a non-zero unexplained difference (`manufacturing_unexplained_difference`; no Ö7 tolerance is approved, none is invented);
  - **remaining WIP does not block the close** when it is explicit, never negative and bound to an issued parent roll of this order, and the roll's unexplained difference is 0. The WIP stays on the closed order as its own position (`BalanceOf(roll).WorkInProgress`): never zeroed, scrapped or turned into good output;
  - WIP is never assumed: with no reading, mass nobody measured or declared stays an unexplained difference and the close is refused; writing WIP down to 0 without declaring or returning the mass does the same; WIP of a roll outside the order or of a roll nothing was issued from cannot be recorded;
  - a closed order takes no new realization (`manufacturing_order_closed`).
  - Real WIP stock/label persistence is proven later in the Inventory integration (T15); this slice proves the domain semantics.
- `RemainingTarget`: only in the basis the plan declared; `Unavailable` otherwise.
- `Kilograms`: non-negative, at most three decimals (Ö2); a fourth decimal is refused (`manufacturing_kilogram_precision_exceeded`), never rounded or cut.
- `CostResult`: R6 status only; every output is `NotCalculated`, an unknown cost has no amount (never 0), and no known cost can be built in this slice.
- **Optimistic concurrency (MFG-3, SİPARİŞ #292 P3-a):** `ProductionPlan` and `ProductionOrder` carry a `Version`. Every mutation (`Approve`; `IssueInput`, `ReturnUnusedInput`, `DeclareOutput`, `RecordWorkInProgress`, `Close`) takes the `expectedVersion` its caller read. A stale one is refused whole with `ManufacturingVersionConflictException` (`manufacturing_version_conflict`, expected and current versions) before any rule runs; a recorded change advances the version by one; a refused rule leaves it unchanged. Same pattern as Purchasing and Logistics.
- Other modules are `Guid` identities only (company, work center, parent roll/label, output label, user).
- **Stable rule codes (MFG-2):** every rule refusal is a `ManufacturingRuleException` whose `Code` is a `ManufacturingRuleCodes` constant, all prefixed `manufacturing_` like the other modules' codes (MFG-1's unprefixed codes were renamed; nothing consumed them). Tests assert the code, not only the type. Caller errors (empty id, undefined enum value, zero or negative mass where a positive one is required) stay `ArgumentException`s.
- **Read-only views (MFG-2, ÜRÜN #280 P3-1):** `PlannedInputs`, `Issues`, `Returns`, `WipMeasurements`, `Outputs`, `ProductionPlan.Inputs` and `MassBalance.OutputsByClass` are read-only wrappers; a cast cannot reach the collections behind them, so the duplicate, roll and closed-order rules cannot be bypassed.

### Application (`GoymenERP.Modules.Manufacturing.Application`, MFG-2)
- `ManufacturingMassVerifier` — verifies a measured mass against the company's kilogram unit profile through `ISettingsPrerequisiteProvider` (Contracts.V1 only; no concrete Settings provider) before a fact is recorded with it:
  - fail-closed: every Settings failure is a refusal with the owner's code kept verbatim (`settings_access_denied`, `settings_provider_unavailable`, `SettingsPrerequisiteCodesV1.UnitProfileNotFound`, `settings_profile_invalid`) — "denied", "unavailable" and "no profile" stay distinct;
  - the unit asked about is always `SettingsUnitIdentitiesV1.Kilogram`; the profile must belong to the server-resolved company the caller passes (`…_profile_company_mismatch`), be a well-formed kilogram profile for that unit (`…_profile_invalid`) and be active on the effective date (`…_profile_not_in_effect`);
  - precision = min(profile scale, Ö2's 3); more decimals are refused (`manufacturing_mass_scale_exceeded`), never rounded; trailing zeros are not precision;
  - a negative mass is refused (`manufacturing_mass_negative`); a zero mass is a valid reading (WIP), and whether a fact needs a positive mass stays the order's rule;
  - the result carries the profile snapshot the port asks consumers to keep with what they record: unit, profile id, profile version as invariant text (the Purchasing/Logistics shape, SİPARİŞ #292 P3-b), the owner's raw `ProfileScale` and the applied `Scale` = min(profile, 3) — the one a consumer may offer for input or display (ÜRÜN #292 P3-1, PUR-3's shape). It authorizes nothing and is not a production policy.

## MFG-5 — persistence (`GoymenERP.Modules.Manufacturing.Infrastructure`)

Pattern: INV-2 (#293) and ADR-002/009/011. Tables in the `manufacturing` schema: `Plans`, `PlanInputs`, `Orders`, `OrderInputs`, `Facts`.

| Rule | Where | Held by |
|---|---|---|
| Every row is a tenant row: global filter on the server-resolved company; a row of another company, a company change or a write without a company is refused | `ManufacturingDbContext` | EF filter + write guard |
| A work center is referenced by (company, work center) against Organization's alternate key; a company by id; a plan/order row by (company, id) | `FK_*_WorkCenters_CompanyId_WorkCenterId`, `FK_*_Companies_CompanyId`, `FK_*_Plans_CompanyId_PlanId`, `FK_*_Orders_CompanyId_OrderId` | SQL (547) |
| Plans and orders keep their content; only approval / close, the version and the update time change. Inputs and facts are append-only; nothing is deleted | `ManufacturingDbContext` | write guard |
| The stored order is rebuilt through the domain: `ProductionPlan.Restore` / `ProductionOrder.Restore` replay the stored facts through the same methods, so a stored state the rules would refuse is refused (`manufacturing_stored_record_invalid`); close is judged on stored facts | domain | domain |
| The plan / fact id is the idempotency identity: same id + same canonical content = replay with no second effect; other content = `manufacturing_plan_id_conflict` / `manufacturing_fact_id_conflict`; an order id with another plan = `manufacturing_order_id_conflict` | `ManufacturingStore` (`PayloadHash`: SHA-256 of invariant text, kg normalized so `5.00` and `5` are one content) | store + PK |
| A fact id is one fact per company and kind, even for racing writers | `PK_Facts (CompanyId, Kind, Id)` | SQL (2627 → `manufacturing_concurrent_change`) |
| Two writes that read the same order cannot both commit | `Version` (domain `expectedVersion`) + `RowVersion` | optimistic concurrency (`manufacturing_concurrent_change`) |
| A fact, the order's version and its audit event commit together or not at all | one `SaveChanges` | SQL transaction |
| Every write carries the ADR-009 envelope (actor, permission, roles, correlation, causation, idempotency key, channel) from the caller | `ManufacturingCommandMetadata` → Audit collector | same save |

Deliberate limits of this slice:

- **No HTTP, permission, host DI or UI.** `AddManufacturingModule`/`InitializeManufacturingModuleAsync` exist but the host does not call them; the permission code in the audit envelope is the caller's, and this module invents none.
- **No Inventory integration (T15).** Parent rolls and outputs stay label identities without a foreign key; no stock moves.
- **Cost** is stored as a status only; every output is `NotCalculated` and a stored known cost is refused.
- The migration and model snapshot were written by hand (no local `dotnet ef`, memory gate closed); CI's `HasPendingModelChanges` check is the verification.

## MFG-API — authorized HTTP surface (#313, approved card)

`src/backend/GoymenERP.Api/Endpoints/ManufacturingEndpoints.cs` → `ManufacturingCommandService` (Application) → the MFG-5 store. All endpoints are under `/api/v1/manufacturing`, need the company context (`X-Company-Id`) and their own **company-wide** permission. The names are registered by the central writer; the grant is a human gate.

| Endpoint | Permission | Answer |
|---|---|---|
| `POST /plans` | `manufacturing.plans.write` | 201 `PlanViewV1` (a slitting plan, Draft) |
| `POST /plans/{planId}/approval` | `manufacturing.plans.approve` | 200 `PlanViewV1` |
| `POST /orders` | `manufacturing.orders.create` | 201 `OrderViewV1` |
| `POST /orders/{orderId}/input-issues` · `input-returns` · `wip-measurements` · `outputs` | `manufacturing.facts.record` | 200 `OrderViewV1` |
| `GET /orders/{orderId}` | `manufacturing.orders.read` | 200 `OrderViewV1`: each roll's issued − returned = outputs by class + WIP + unexplained (signed), cost `NotCalculated` |

Rules held here:

- **Company:** only from the context; no request carries one. Another company's plan or order is the same code-less 404 as an unknown id.
- **Kilograms (Ö2):** plain invariant decimal text (no sign, exponent or grouping), checked against the company's kilogram profile (`ManufacturingMassVerifier`) before anything is written. Facts use their own UTC date; plan masses use the server's date. More decimals than the profile allows is `manufacturing_mass_scale_exceeded` (422), never rounded. Settings `settings_access_denied` → 403; `settings_provider_unavailable` / `settings_profile_invalid` → 503 (for example a company with no approved kilogram source); any other Settings or mass refusal → 422 with its code.
- **Authority (#276):** the endpoint policy answers first. The permission is then measured fresh with `IErpAuthorityRevalidationService` twice: before any stored answer is read (ADR-011 §5), and again after the Settings read. Only a company-wide grant writes. Its role codes and the endpoint's permission go into the ADR-009 audit envelope, together with the request's correlation, causation and channel and the `Idempotency-Key` as its hash.
- **Idempotency (ADR-011 §2–§8):** every write is one command attempt in the module's own log (`manufacturing.Commands`).
  - **Key:** unique `(CompanyId, CommandName, IdempotencyKeyHash)`. The raw `Idempotency-Key` is never stored, only the uppercase SHA-256 of the trimmed key.
  - **Request hash:** the versioned command name plus the canonical payload, with the path id included and kilograms normalized (`10.000` = `10`). The actor is stored beside it.
  - **Order of checks:** the fresh authority check runs first. Then:
    - the same key, request and actor answers the stored result with `Idempotency-Replayed: true` and `Idempotency-Original-Correlation-Id`;
    - another request or actor under the key is 409 `idempotency_conflict`, disclosing nothing.
  - **Atomicity:** a success is recorded in the same save as the business change and its audit event (`ManufacturingCommandMetadata.Completion`).
  - **Terminal answers:** a terminal not-found or version conflict is recorded on its own (§7). Validation, Settings and rule refusals are not recorded, so a retry is evaluated again.
  - **Races:** two attempts racing on one key leave one record, and the loser answers from it (§8).
  - **The approval replays too.** A lost answer retried is its 200, not a version conflict.
- **R3 underneath:** the store still recognises the caller's plan / order / fact id with the same canonical content as the same record, whatever the key (the ledger's own identity, MFG-5). Other content under that id is 409 (`*_id_conflict`). Under a new key such a success is recorded as that key's answer.
- **Concurrency:** `expectedVersion` → 409 `manufacturing_version_conflict` with `expectedVersion` / `currentVersion`; a lost race → 409 `manufacturing_concurrent_change`.
- **Refusals:** a domain rule → 422 with its code; a work center that is not this company's → 422 `manufacturing_work_center_not_found`; a malformed request → 400 `manufacturing_request_invalid` with `field`.
- **Read under concurrent writes (FİNANS #313 INFO-4):** the store re-reads an order whose row and facts were committed apart (RCSI reads each statement on its own), up to three times. If it still meets a commit, `GET /orders/{orderId}` answers 503 `service_unavailable`: a retryable consistency failure, never a 500. It is not a 409, because 409 belongs to commands. Any other store refusal keeps its own path.
- **409 / 422 codes are one declared superset:** every write endpoint declares the same 409 (6) and 422 (23) `manufacturing_*` codes (`WithWriteProblems`). For example, the plan save also declares `manufacturing_output_already_declared`.
  - It is a true superset: no endpoint returns a code it does not declare.
  - A client branches on the code, not on the endpoint. A code a screen does not handle is shown as a generic refusal.
  - Narrowing per endpoint is a separate change: the endpoint, the regenerated OpenAPI and the contract expectations change together.

Not here: closing an order, a list or search, cost, Inventory movements (T15), or any role assignment.

## MFG-UI — the order screen (approved card, D1–D5)

- **Shell provider** `Infrastructure/Shell/ManufacturingShellCapabilityProvider`: `manufacturing.orders.detail` → `/uretim/emir/:orderId` (`manufacturing.orders.read`) and its control `manufacturing.orders.facts` (+ `manufacturing.facts.record`). Company-wide, single company.
  - **Staged:** `ImplementationAvailable: false` until the `mfg` browser suite accepts the screen.
  - `MFG-01` is a technical sub-view id. The canonical production screens (PR-09/13/14/15) are `DECISION_ONLY_REFERENCE_MISSING`, and this is not their locked reference.
  - The host registers the provider in the central writer's round (`src/frontend/goymen-erp-web/src/manufacturing/SHARED_MOUNT_HANDOFF.md`).
- **Frontend** `src/frontend/goymen-erp-web/src/manufacturing/`:
  - The order's roll balances, as read from the server. The unexplained difference is signed; cost shows as a status.
  - Four fact forms, drawn only with the facts capability.
  - One Idempotency-Key and one fact id per frozen submit.
  - After an accepted fact the order is re-read; nothing is predicted.
  - An unknown outcome freezes the forms until the same snapshot is re-sent.
  - A 401/403 on a re-send does not settle it, because authority is measured before the stored answer.

## MFG-6 slice 1 — the Slitting and Winding engines (approved card, Business 5737790175)

`Domain/Engines`: pure, deterministic, versioned calculators of master v3 §28.4 (`SlittingEngineV1`, SLT-001…010) and §28.5 (`WindingEngineV1`, WND-001…009), exactly as written there. There is no I/O, no HTTP and no persistence yet; the preview API, the plan snapshot and the plan screen are the next slices of the card.

- **Never a guess:**
  - a missing parameter is `EngineOutcome.Unavailable` and is named;
  - a broken rule is `EngineOutcome.Refused` with an `EngineCodes` code;
  - a refusal wins over missing parameters, because a supplied value is wrong.
- **No built-in parameter:** density, line speed and the metre tolerance are inputs (OPEN-007, OPEN-004/005, Ö7).
  - Their values come from the Settings / Policy Registry (DEC-SET-001), and an approved plan snapshots them (DEC-PRDCT-008).
  - A tolerance may be a stated zero; a missing one is unavailable, never zero.
- **Ö2:** kilogram and nominal millimetre inputs take at most three decimals and are refused beyond.
- **No rounding.** Results are unrounded decimals, because the rounding policy (§28.1) is not decided. π and the square root are decimal too, so a result is the same on every platform.
- **Invariants:**
  - the used width cannot exceed the net usable width (`engine_used_width_exceeds_net_width`);
  - SLT-010's line time is the parent coil's walking metres over the line speed, never the output bands' metres.
- **Evaluation order (SLT-001…007), algebraically identical to §28.4:**
  - Line kg (SLT-003, `(HamKg / NetEn) × KullanılanEn`) is evaluated as `HamKg × KullanılanEn / NetEn`: multiply first, divide once.
  - Plan kg (SLT-006, `ΣSatırKg`) is evaluated once as `HamKg × ToplamKullanılanEn / NetEn`, not as a sum of separately rounded line values.
  - Remaining kg (SLT-007) is one subtraction.
  - SLT-001's kg per mm is **reported only**, never a multiplicand. It is often non-terminating (`28750 / 1500`), and multiplying its 28-digit rounding back would leave a residue: a full-width plan showed −1e-24 kg remaining (#340 CI).
  - No epsilon and no rounding are applied. §28.1 is not decided, so either would be a policy.
- **What holds exactly vs within decimal precision** (Ö2 inputs; tested over a seeded set of plans):
  - **Exact:** widths (SLT-002/004/005); remaining kg ≥ 0 whenever used ≤ net; plan kg + remaining kg == parent kg; a full-width plan leaves exactly 0.
  - **Within decimal precision only:** Σ line kg == plan kg. Each line is its own single division, so they agree to about 1e-23 kg, not bit for bit.
- **Versions:** every result carries `FormulaVersion`. A changed formula is a new version, never an edit (DEC-PRDCT-004).
- **Not in version 1** (each needs a sourced rule before a new version can carry it):
  - WND-010 (break-down into target-mass coils plus a remainder "above tolerance"). Its tolerance rule is not stated precisely enough to encode without choosing it.
  - WND-008's "**or** kg tolerance" branch. Version 1 encodes only the metre form, `YürüyenMetre + ToleransMetre ≥ GerekliMetre`. Whether a kg tolerance replaces the metre one, adds to it, and against which mass it is measured is not stated.
- **Golden tests** (`tests/GoymenERP.Manufacturing.UnitTests/Engines`): the master's examples at the precision it prints (±0.0005), plus an independent 40-digit reference (±1e-18). The density 7850 kg/m³ there is the test input the examples imply, not a production parameter.

- **Change log of `GDC-WND/1`** (§28.17 "yeni davranışta yeni sürüm"; FİNANS #342 INFO-1):
  - **2026-09-19, #342:** `Fit` (WND-008/009) takes WND-003's computed outer diameter as a positive computed value. It no longer takes it as an Ö2 nominal millimetre, which refused its own engine's result (e.g. 1371.1401108… mm) for "more than 3 decimals".
  - The input domain widened; every previously computed output is unchanged, and no engine result had been stored. So it stays `GDC-WND/1`. The line's maximum stays a nominal millimetre.

## MFG-6 slice 2 — the plan's slitting spec and the fail-closed approval gate (approved)

- **Spec as plan content** (`Domain/Plans/SlittingSpec`):
  - the slit lines (band width, a nominal Ö2 millimetre; count);
  - an optional winding target (kg).
  - It is set when the plan is created and never changed, like the plan's inputs and target.
  - Stored in the same save as the plan: `manufacturing.PlanSlitLines` (bound by company + plan) and `Plans.WindingTargetKg`.
  - The content hash is extended **only** for a plan that has a spec, so every plan stored before keeps its hash and replay identity (pinned by a test on the MFG-5 canonical string).
  - The same id with another spec is `manufacturing_plan_id_conflict`.
- **Scope of the gate:** only a plan that carries a spec. A plan without one (the MFG-5 plan: inputs and target) claims no engine computation and is approved as before. The live MFG-API and the `mfg` browser suite create such plans; there is no HTTP surface for a spec yet (the API slice).
- **Approval gate** (`SlittingApprovalGate`, inside `ProductionPlan.Approve`):
  - Order: the version fence first, then "only a draft", then the gate.
  - Per planned roll: the label must be known usable; SLT-001…007 and SLT-010 must compute with the roll's planned mass as the parent mass.
  - With a winding target, per roll and band width: WND-001…004 and the WND-008/009 fit.
  - Nothing is approved with gaps:
    - a missing input → `manufacturing_plan_parameters_unavailable`, naming every one (sorted, distinct);
    - an engine refusal → `manufacturing_plan_engine_refused` (the engine's code + parameter);
    - a label known not usable → `manufacturing_plan_label_not_usable`;
    - a fit that computes and fails → `manufacturing_plan_winding_does_not_fit` (`metresSufficient` / `diameterFits`). Fail-closed; an authorized exception path would be a Business decision.
  - A refusal wins over missing inputs.
  - Nothing changes on a refusal: no version, no approval, no audit.
  - A stored approval is restored without re-running the gate.
- **The outside inputs today** (`SlittingPlanParameters`): all unknown, so every approval of a plan with a spec is refused with the full list. None of these is defaulted or entered on the plan:
  - `labelState`: Inventory's module read of the label (queued, ÜRÜN);
  - `thicknessMm`, `parentWidthMm`: Product's role read (ÜRÜN; data waits on Q6 #66 5740511161);
  - `netUsableWidthMm`, `densityKgPerCubicMetre`, `lineSpeedMetresPerMinute`, `innerDiameterMm`, `maximumOuterDiameterMm`, `toleranceMetres`: the Settings / Policy Registry (DEC-SET-001, OPEN-007, OPEN-004/005).
- **No parameter snapshot yet.** Its source-version shape is the registry's and lands with the registry's first reader. The gate means no approval can happen without it.
- **Engine fix carried here:** WND-009's outer diameter is WND-003's computed value. `WindingEngineV1.Fit` now takes it as a positive computed value, not an Ö2 nominal millimetre, which a computed diameter never is. The line's maximum stays nominal. The formula is unchanged, so this is not a new version.
- **Migration** `20260919130000_AddPlanSlittingSpecs`: its Down refuses with `THROW 51047` (Manufacturing's one module number) while any spec is stored. Forward fix. Real SQL: it rolls back and forward cleanly with no spec, and refuses (51047) with one.
- **Open item** (#66 NEEDS-BUSINESS 5741547443): whether a slitting plan's parent mass is the whole coil (the label's balance) or may be a part of it. Until it is answered, the engine takes the planned mass (the kg that passes the line) and **no rule** ties it to the label's balance either way. The balance itself also needs Inventory's module read.

- **Carried (FİNANS #342 INFO-3):** the store's decimal(18,3) column bound `KilogramColumnLimit` also bounds the band width. It is to get a neutral name (e.g. `Decimal18Scale3Limit`) in the next slice that touches the guard-pinned `ManufacturingStore.cs` anyway. Deferred so that pure-domain slices stay non-central (coordinator ruling).

## MFG-7 slice 1 — the Cold Rolling engine (Business 5737790175, DEC-PRDCT-001 order)

`Domain/Engines/ColdRolling/ColdRollingEngineV1` (`GDC-CR/1`) is master v3 §28.8 (CR-001…005) exactly, on the same core (`EngineResult` / `EngineInputs` / `EngineMath`) and under the same rules as the MFG-6 engines: pure, decimal, versioned, no I/O.

- **Formula card:**

| ID | Master §28.8 | Engine (evaluation order) |
|---|---|---|
| CR-001 input metres | `HamKg × 1000 / (GirişKalınlık × En × Yoğunluk[kg/dm³])` | `inputKg × 1 000 000 / (inputThicknessMm × widthMm × density[kg/m³])` |
| CR-002 output kg | `HamKg − FireKg` | `inputKg − scrapKg` (exact) |
| CR-003 elongation | `GirişKalınlık / HedefKalınlık` | the same, one division |
| CR-004 output metres | `ÇıkışKg × 1000 / (HedefKalınlık × En × Yoğunluk[kg/dm³])` | `outputKg × 1 000 000 / (targetThicknessMm × widthMm × density[kg/m³])` |
| CR-005 reduction % | `(GirişKalınlık − HedefKalınlık) / GirişKalınlık × 100` | `(input − target) × 100 / input` |

- **Unit mapping:** density is ONE registry parameter in kg/m³, shared with WND-001 (OPEN-007, DEC-SET-001). §28.8 writes it in kg/dm³ with ×1000. With ρ[kg/dm³] = ρ[kg/m³] / 1000, the ×1000 becomes ×1 000 000; the two forms are algebraically identical. Golden check: 28 750 × 10⁶ / (2 × 1500 × 7850) = 1220.807 m.
- **Invariants (§28.8), refused with codes, never clamped:**
  - the target thickness must be below the input thickness (`engine_target_thickness_not_below_input`);
  - the scrap must be below the input mass; equal or more is `engine_scrap_not_below_input_mass`.
  - The scrap is supplied: a stated 0 is a value, a missing one is unavailable.
- **Never a guess:** a missing input is `Unavailable` and is named; Ö2 kilogram and millimetre inputs take at most three decimals and are refused beyond. Nothing rounds.
- **One width:** the coil's width is one input for both sides, as §28.8 uses one `En`.
- **Not in version 1:** multi-pass rolling, the pass plan, tolerance, work hardening and quality results. The master keeps these in the process model.
- **Golden tests** (`ColdRollingEngineTests`): the §28.8 example (28.750 kg, 1500 mm, 2,00 → 1,50 mm → 1220,807 m, 1627,742 m, 1,333, %25) at the master's printed precision (±0.0005), plus an independent 40-digit reference (±1e-18). Its density 7850 kg/m³ and scrap 0 kg are test inputs, not production parameters.

## Behaviour kept on purpose — Business decision pending

- **One approved plan can open more than one order** (`FromApprovedPlan` does not mark the plan as converted). §7.3 does not say whether a plan becomes one order or several partial ones; the question is open with Business (#66 5728934213, the same principle as Purchasing's request → several orders). Pinned by a test; changes only by that decision. The physical guard is Inventory's.

## What this module is not

- No stock movement, lot or label: Inventory owns those.
- No trim/kerf value, tolerance number, template, rounding stage or roll-substitution policy. The only formulas are the MFG-6 engines and the MFG-7 cold-rolling engine above, and every parameter is supplied to them.
- No UI yet (the normal screen is the next card); HTTP since MFG-API, persistence since MFG-5 (above).
- No other engine yet: tube/profile, painting and cut-to-length follow in the DEC-PRDCT-001 order.

## dependency_wait

- **Real stock movement** for issue/return/output and label identity: Inventory on main.
- **T15 integration with Inventory** (ÜRÜN #280 Not-1, as the Inventory owner):
  - an `InputIssue` is met by an Inventory `Issue` movement and an `OutputDeclaration` by a `Receipt`; the MFG fact and the Inventory movement are written in **one transaction**, so "Manufacturing recorded the issue, Inventory refused it" cannot happen (the same principle as the T09 acceptance share);
  - a **blocked** parent roll, or one whose document is still pending (Ö3 flag), is checked **before** the MFG fact is recorded; Manufacturing does not know about blocks and must not, the integration asks Inventory first;
  - **subcontract / customer-owned goods:** the output's owner and custodian are inherited from the parent roll's Inventory position; Manufacturing does not choose them (which is why it carries no owner).
- **Command surface:** the verifier's refusal is mapped and its snapshot retained by the command handler that records a fact; nothing binds it to the domain methods yet.
- **Ö7 profile**: approved tolerance/formula numbers and the "reason + single approval" exception path with a named approver. Until then every non-zero difference keeps the order open.
- **Known cost**: its money shape (a currency identity, not a code string) comes with T25/Finance.
- **Estimated facts**: every fact here is an actual measurement; R6's actual/estimated distinction arrives with the first estimated source.
