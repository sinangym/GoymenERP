# Manufacturing (MFG-1 + MFG-2 — first independent slices of T15, Dilme/Sarım)

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

## Behaviour kept on purpose — Business decision pending

- **One approved plan can open more than one order** (`FromApprovedPlan` does not mark the plan as converted). §7.3 does not say whether a plan becomes one order or several partial ones; the question is open with Business (#66 5728934213, the same principle as Purchasing's request → several orders). Pinned by a test; changes only by that decision. The physical guard is Inventory's.

## What this module is not

- No stock movement, lot or label: Inventory owns those.
- No formula, trim/kerf, tolerance, template, rounding stage or roll-substitution policy.
- No persistence, HTTP, DI, permission, host or pin.
- No other engine (cold rolling, tube/profile, painting, cut-to-length).

## dependency_wait

- **Real stock movement** for issue/return/output and label identity: Inventory on main.
- **T15 integration with Inventory** (ÜRÜN #280 Not-1, as the Inventory owner):
  - an `InputIssue` is met by an Inventory `Issue` movement and an `OutputDeclaration` by a `Receipt`; the MFG fact and the Inventory movement are written in **one transaction**, so "Manufacturing recorded the issue, Inventory refused it" cannot happen (the same principle as the T09 acceptance share);
  - a **blocked** parent roll, or one whose document is still pending (Ö3 flag), is checked **before** the MFG fact is recorded; Manufacturing does not know about blocks and must not, the integration asks Inventory first;
  - **subcontract / customer-owned goods:** the output's owner and custodian are inherited from the parent roll's Inventory position; Manufacturing does not choose them (which is why it carries no owner).
- **Command surface:** the verifier's refusal is mapped and its snapshot retained by the command handler that records a fact; nothing binds it to the domain methods yet.
- **Ö7 profile**: approved tolerance/formula numbers and the "reason + single approval" exception path with a named approver. Until then every non-zero difference keeps the order open.
- **Persistence**: DbContext/migration via the central writer, after Inventory's persistence (#293) and the allocation rule (T11). The domain's `Version` becomes the stored concurrency token there.
- **Known cost**: its money shape (a currency identity, not a code string) comes with T25/Finance.
- **Estimated facts**: every fact here is an actual measurement; R6's actual/estimated distinction arrives with the first estimated source.
