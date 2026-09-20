# FIN-T05 — the host lines CARİ owns (ready delta)

**From:** FİNANS (Finance and Accounting owner). **To:** CARİ (central writer: `Program.cs`).
**Source:** Sinan's K1, #66 `5748258299` (20 September 2026), which closes `5745134466`.

The decision is implemented on this branch; what is missing is the composition, and `Program.cs` is
yours. Two registrations, both **scoped**, both **before** `AddFinanceServices(...)` — the module's own
registration is `TryAdd` (it tests the SERVICE TYPE only), so an earlier one wins and the undecided fallback then
does nothing. **DONE** in `7a9fe94c`, with the ordering written down in `Program.cs` where it could be undone.

```csharp
// FIN-T05 (Sinan's K1, #66 5748258299): a definitive manual bank receipt's period is decided by its
// occurredOn. The rule needs Accounting's period port, which the Finance module cannot see, so the
// host composes them; Finance's own TryAdd below then leaves this registration in place.
builder.Services.AddScoped<IAccountingPeriodStatusPortV1, AccountingPeriodStatusPort>();
builder.Services.AddScoped<IFinanceRecordPeriodRule, OccurredOnFinanceRecordPeriodRule>();
```

`AccountingPeriodStatusPort` and `OccurredOnFinanceRecordPeriodRule` live in
`src/backend/GoymenERP.Api/Shell/`; `IAccountingPeriodStatusPortV1` is
`GoymenERP.Modules.Accounting.Application.Contracts`.

## Why scoped, not singleton

Both depend on `ICompanyContext`, `ICurrentUser` and `IAccountingPeriodStore`, which are per request.
A singleton here would capture one request's context — the exact shape of defect #366 closed in two
other adapters.

## What must NOT be done

- **Do not grant anything.** The port reads under the CALLER's own
  `accounting.periods.read`. A Finance persona that records definitive entries therefore needs that
  name, or Record is refused with `finance_period_rule_unavailable`. That is a human grant decision
  (surfaced on #66 as a consequence of K1, not a new question), not a line in this file.
- **Do not register the rule as the fallback's replacement inside the Finance module.** The module
  must stay unable to see Accounting; the mapping lives in one host class on purpose, so the decision
  does not become a general rule for every accounting source.
