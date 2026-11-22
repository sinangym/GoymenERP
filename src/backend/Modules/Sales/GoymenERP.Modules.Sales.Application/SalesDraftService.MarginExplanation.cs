using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// WP11 (DALGA_PLANI_v2 1.12, "bu sayıyı açıkla"): one line's margin explained. The margin is resolved exactly as the
/// line-pricing read resolves it (<see cref="ResolveLineMarginAsync"/>) and computed by the same calculator run that returns the
/// steps (<see cref="SalesLineMarginCalculator.Explain"/>), so the explained number IS the shown number; nothing is recomputed
/// here or on the screen. The existing names decide: the draft read, the price read (else 403) and the separate margin read
/// <c>sales.orders.margin.read</c> (SAT-D250/SAT-D266/SAT-D267, else 403 — a withheld margin has no trace). No new permission.
/// Nothing is written.
/// </summary>
public sealed partial class SalesDraftService
{
    public async Task<SalesDraftLineMarginExplanationV1> ExplainDraftLineMarginAsync(Guid draftId, Guid lineId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        if (draftId == Guid.Empty || lineId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var draft = await LoadDraftAsync(scope, bindings.ReadPermission, draftId, cancellationToken);
        await RequireMarginDisclosureAsync(scope, cancellationToken);
        var line = draft.Lines.SingleOrDefault(item => item.Id == lineId)
            ?? throw new SalesDraftServiceException(404, "not_found");
        var totals = SalesOrderTotalsCalculator.CalculateWithLineTax(draft);
        var resolved = await ResolveLineMarginAsync(scope, draft, totals, line, cancellationToken);

        // Every slow owner read can revoke the names: nothing of the trace is returned on a stale disclosure.
        await RequireStillAsync(scope, bindings.ReadPermission, cancellationToken);
        await RequireMarginDisclosureAsync(scope, cancellationToken);
        return Explanation(draft, line, resolved);
    }

    private async Task RequireMarginDisclosureAsync(Scope scope, CancellationToken cancellationToken)
    {
        if (await DiscloseAsync(scope, cancellationToken) != SalesPriceDisclosure.Visible
            || await DiscloseMarginAsync(scope, cancellationToken) != SalesMarginDisclosure.Visible)
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }
    }

    private static SalesDraftLineMarginExplanationV1 Explanation(SalesOrderDraft draft, SalesOrderDraftLine line,
        ResolvedLineMargin resolved)
    {
        var steps = resolved.Explained.Steps;
        var sources = new List<SalesMarginTraceSourceV1>
        {
            new(SalesMarginTraceSourceKindsV1.OrderLine, line.Id, draft.Revision,
                line.LineNo.ToString(CultureInfo.InvariantCulture), null, null),
        };
        var cost = resolved.Cost;
        if (cost.UnavailableReason is null)
        {
            sources.Add(new(draft.ApprovalState == InternalApprovalState.Approved
                    ? SalesMarginTraceSourceKindsV1.CostSnapshot
                    : SalesMarginTraceSourceKindsV1.CostObservation,
                cost.LineId, cost.Revision, cost.UnitId.ToString("D", CultureInfo.InvariantCulture), cost.RecordedAtUtc,
                cost.SourceProof));
        }

        var rules = new List<string> { SalesMarginTraceRulesV1.BaseGolden };
        if (steps.Any(step => step.Code == SalesLineMarginTraceCodes.SaleRate) && cost.SaleRate is { } rate)
        {
            sources.Add(new(SalesMarginTraceSourceKindsV1.SaleRate, rate.CurrencyId, null,
                rate.RateDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), null,
                rate.Rate.ToString(CultureInfo.InvariantCulture)));
            rules.Add(SalesMarginTraceRulesV1.ForeignSaleGolden);
        }

        if (steps.Any(step => step.Code == SalesLineMarginTraceCodes.KilogramsPerUnit)
            && draft.KilogramsPerUnitOf(line.Id) is { } perUnit)
        {
            sources.Add(new(SalesMarginTraceSourceKindsV1.KilogramsPerUnit, perUnit.LineId, perUnit.Revision,
                perUnit.UnitId.ToString("D", CultureInfo.InvariantCulture), perUnit.RecordedAtUtc, perUnit.SourceProof));
            rules.Add(SalesMarginTraceRulesV1.PieceGolden);
        }

        return new SalesDraftLineMarginExplanationV1(SalesDraftLineMarginExplanationV1.CurrentContractVersion, draft.Id,
            draft.Version.ToString(CultureInfo.InvariantCulture), MarginKind(draft), SalesMarginTraceRulesV1.CalculatorVersion,
            LineMargin(line.Id, line.LineNo, resolved.Explained.Margin),
            [.. steps.Select(static step => new SalesMarginTraceStepV1(step.Code, step.Operation, step.Operands, step.Value,
                step.CurrencyId, step.UnitId, step.Scale))],
            sources, rules);
    }
}
