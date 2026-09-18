# Manufacturing (MFG-1 — first independent slice of T15, Dilme/Sarım)

Source of truth: #275 plan v2 §7.3, §13, R3, R6 and `SINAN_KARARLARI_2026-09-18_O1_O8_VE_SON_ASAMA.md` (Ö2, Ö7), read at exact head `a5b8143b`; coordinator decisions of 2026-09-18.

## What this slice is

Domain records and their invariants only:

- `ProductionPlan` — parent rolls and planned mass, an optional **declared** target (`InputMass` or `GoodOutputMass`); approval records a decision and consumes nothing.
- `ProductionOrder` — created only from an approved plan, with its own snapshot of the plan's inputs and target.
- **Realization facts**, each with its own id and UTC time (R6), with the totals derived from them:
  - `InputIssue`, `InputReturn`: the same id twice is refused (`input_already_issued`, `return_already_recorded`); a return never exceeds what was issued.
  - `OutputDeclaration`: bound to an issued parent roll of this order; a class out of good product, second quality, usable remnant, scrap or process loss; the same id twice is refused.
  - `WipMeasurement`: the latest reading is the roll's WIP; a reading that is not strictly later than the latest is refused (`wip_measurement_not_latest`); the same id twice is refused.
- `MassBalance` per parent roll: `issued − returned = Σ outputs + WIP + unexplained`. The unexplained difference keeps its sign and is never folded into any class ("açıklanamayan fark otomatik fire olmaz").
- `Close`:
  - refused while any roll has WIP (`wip_open`, **TEMPORARY — awaiting Business: "Üretim emri, işlemde kütle (WIP) varken kapanabilir mi?"**);
  - refused while any roll has an unexplained difference (`unexplained_difference`; no Ö7 tolerance is approved).
- `RemainingTarget`: only in the basis the plan declared; `Unavailable` otherwise.
- `Kilograms`: non-negative, at most three decimals (Ö2); a fourth decimal is refused, never rounded or cut.
- `CostResult`: R6 status only; every output is `NotCalculated`, an unknown cost has no amount (never 0), and no known cost can be built in this slice.
- Other modules are `Guid` identities only (company, work center, parent roll/label, output label, user).

## What this slice is not

- No stock movement, lot or label: Inventory owns those.
- No formula, trim/kerf, tolerance, template, rounding stage or roll-substitution policy.
- No persistence, HTTP, DI, permission or host.
- No other engine (cold rolling, tube/profile, painting, cut-to-length).

## dependency_wait

- **Real stock movement** for issue/return/output and label identity: Inventory on main.
- **Ö7 profile**: approved tolerance/formula numbers and the "reason + single approval" exception path with a named approver. Until then every non-zero difference keeps the order open.
- **Persistence and concurrency**: DbContext/migration via the central writer. Optimistic concurrency (`expectedVersion`) is added with the first command surface, not in this domain-only slice (#280 review SİPARİŞ P3-2).
- **Known cost**: its money shape (a currency identity, not a code string) comes with T25/Finance.
- **Estimated facts**: every fact here is an actual measurement; R6's actual/estimated distinction arrives with the first estimated source.
