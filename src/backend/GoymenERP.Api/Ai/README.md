# AI runtime and gateway (`ai-gateway.v1`)

Sources: AI-D3/AI-D4 (data never leaves above its class), AI-D22 (lean core), AI-D24 (task × data class model table,
document/e-mail text only on a company-internal model), AI-D30 (budget ends → work stops), AI-D34 (a/b/c/d), DALGA_PLANI_v2
A.1, WORK_AI_TAMAMLAMA_PLANI_v1 WP-A1.

**Off by default.** `Ai:Enabled=false`; the shipped provider kind is `Fake`, which the runtime never admits in Production.
No external or company-internal model is wired: provider and contract, key, KVKK confirmation, monthly budget, hardware
(OPEN-014) and production activation (AI-D6) are human gates. Every feature switch is off until a Settings version turns it
on.

## Layers

| Layer | Type | Owner | Does |
|---|---|---|---|
| Ask endpoint | `AiAskService` (`POST /api/v1/ai/ask`) | Api | ERP-000J §8 order: actor, server company, `ai.panel.use`, tool allowlist, tool permissions, argument canonicalization, data class, owner READ, fence, Group E security events. Then calls the gateway. |
| Planning | `IAiModelProviderV1.PlanAsync` | Api | Untrusted model output: a list of READ tool calls, re-checked before any tool runs. |
| **Gateway** | `IAiGatewayV1.ComposeAsync` (`Gateway/AiGateway.cs`) | Api | Feature switch → source check → task × data class route → budget reservation → compose → settle → grounding check. |
| Composition | `IAiAnswerComposerV1` | Api | One composer per (provider kind, placement). Only `FakeAiAnswerComposer` (Cloud and OnPremises) exists. |
| Policy + ledger | `ISettingsAiGatewayPolicyProviderV1`, `ISettingsAiBudgetLedgerV1` | Settings | Versioned policy and append-only usage ledger (`src/backend/Modules/Settings/README.md`). |

## Contract for other AI packages (B1–B5, C*)

Call `IAiGatewayV1.ComposeAsync(AiGatewayRequestV1)` (scoped service). Never call a composer directly.

```csharp
var result = await gateway.ComposeAsync(new AiGatewayRequestV1(
    Feature: AiFeatureV1.Summary,              // your package's switch
    TaskKind: AiTaskKindV1.Summarize,          // your column of the model table
    Instruction: instructionText,              // bounded by Ai:MaxQuestionLength
    ContentDataClass: AiDataClassV1.Internal,  // class of the instruction text itself, by content (AI-D34 d)
    Grounding: releasedToolResults,            // AiGroundingItemV1 per READ result you already released
    Caller: new AiGatewayCallerV1(companyId, actorId, correlationId, causationId)));  // server-resolved only
```

Your package owns, **before** the call: authority (permission measured fresh), the owner READ tools, the data class of each
source and the Group E security events. The gateway owns: the switch, the route, the budget, the model call and the check
of the text against the sources. It never runs a tool, never writes a business record, never executes a command (WP-C1
does that under its own approval path) and never receives an authority it could widen.

| Status | Meaning | Caller does |
|---|---|---|
| `Answered` | `Answer.Text` is supported by `Answer.Citations` (`Grounded=true`); for `Chat` an ungrounded reply (`Grounded=false`, no citations). | Show text + citations. |
| `CannotAnswer` | `no_source` (nothing to rest on; no model call, no budget) or `unsupported_by_sources` (the text held a number or citation the sources do not; units still recorded). | Show "cannot answer"; never show model text. |
| `FeatureDisabled` | The switch is off in the policy in force, or no policy is recorded. | Hide/disable the feature. |
| `BudgetExhausted` | The month's ceiling is reached, or no ceiling/reservation is recorded. | Stop; notify (AI-D30). |
| `DataClassRefused` | Effective class above `Ai:MaxDataClass`, no exact task × class route, or a route the fixed rules forbid. | Refuse; record your refusal event. |
| `RuntimeUnavailable` | Runtime off or not ready. | Treat as disabled. |
| `Invalid` | Malformed request (no caller, empty instruction, undefined enum, a source of another tool, `Chat` with grounding, `Converse` outside `Chat`). | Programming error. |
| `Unavailable` | Policy store, ledger or model failed, or the policy version changed between read and reservation. | Retry later. |

### Fixed rules (no Settings version loosens them)

- **Numbers only from tools.** Every number in the text (after `[n]` citation markers are removed) must appear verbatim in a
  cited source — its content, title, version or as-of date. Totals, differences, rounding and reformatting are refused;
  calculators stay in the backend.
- **No source, no answer.** Every feature except `Chat` needs at least one grounding item.
- **Effective data class** = highest of `ContentDataClass` and every source. The route is the exact (task, class) cell.
- **Cloud ≤ Internal.** Confidential and strictly confidential content never goes to a cloud model (no pseudonymization yet).
- **Document and e-mail free text** (`Document`, `Email` features) only on an `OnPremises` route.
- **General chat** (`Chat`): only `TaskKind.Converse`, no grounding (ERP tools closed, AI-D28 b).
- The model sees the deployment name, task, instruction and numbered sources only — no company, actor, session or policy.

### Budget

Before the model call the gateway reserves the version's per-request units in the Settings ledger (refused when the ceiling
would be passed, when the ceiling/reservation is empty, or when the policy version changed); after the call it settles the
provider-reported units (zero on failure). The ledger is append-only; the month is the `Europe/Istanbul` calendar month.

### Versioning

`AiGatewayContractV1.Version = "ai-gateway.v1"`. Enum values (`AiFeatureV1`, `AiTaskKindV1`, `AiModelPlacementV1`) are
stored by Settings and never renumbered; a new member takes the next number and needs a Settings check-constraint widening.
A breaking change is a new `…V2` contract beside this one.

## Open points (NEEDS-SINAN, fail-closed until decided)

- Monthly budget value and its unit (provider billing unit/tokens vs. currency) — S5.
- Pseudonymization for confidential data on cloud models (until then: refused).
- Classification of a free-typed panel question (today: `Internal`).
- Which features and routes to switch on per company (a Settings version, human decision).
