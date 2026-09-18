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

Not here: closing an order, a list or search, cost, Inventory movements (T15), or any role assignment.

## Behaviour kept on purpose — Business decision pending

- **One approved plan can open more than one order** (`FromApprovedPlan` does not mark the plan as converted). §7.3 does not say whether a plan becomes one order or several partial ones; the question is open with Business (#66 5728934213, the same principle as Purchasing's request → several orders). Pinned by a test; changes only by that decision. The physical guard is Inventory's.

## What this module is not

- No stock movement, lot or label: Inventory owns those.
- No formula, trim/kerf, tolerance, template, rounding stage or roll-substitution policy.
- No UI yet (the normal screen is the next card); HTTP since MFG-API, persistence since MFG-5 (above).
- No other engine (cold rolling, tube/profile, painting, cut-to-length).

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
