using System.Text.Json;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

public sealed partial class SalesDraftService
{
    // SAT-D265(5): calculator test vectors are source evidence, not the independent golden acceptance.
    // Kept closed until that exact decision artifact is in main; this is not an ERP activation setting.
    private static bool MarginGoldenAccepted => false;

    private static SalesMarginCostSnapshot? LastMarginCost(SalesOrderDraft draft, Guid lineId) =>
        draft.MarginCosts.Where(cost => cost.LineId == lineId && cost.Revision <= draft.Revision)
            .OrderByDescending(cost => cost.Revision).FirstOrDefault();

    private static bool SameCostBasis(SalesOrderDraft draft, SalesOrderDraftLine line, SalesMarginCostSnapshot cost) =>
        cost.ProductId == line.Product.ProductId && cost.UnitId == line.OrderUnit.UnitId
        // SAT-D269 follow-up 6951607190: a quantity edit inside the same revision preserves the pinned unit cost.
        // A genuine quantity revision still observes its own approval cost; no stock/accounting amount is rewritten.
        && (cost.Quantity == line.Quantity.Value || cost.Revision == draft.Revision);

    private static bool LegacyApprovedCost(SalesOrderDraft draft, SalesOrderDraftLine line)
    {
        var prior = draft.ArchivedRevisions.OrderByDescending(item => item.Revision).FirstOrDefault()
            ?.Lines.SingleOrDefault(item => item.LineId == line.Id);
        return prior is not null && prior.ProductId == line.Product.ProductId && prior.OrderUnitId == line.OrderUnit.UnitId
            && decimal.TryParse(prior.QuantityText, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var quantity) && quantity == line.Quantity.Value;
    }

    private async Task<IReadOnlyList<SalesMarginCostSnapshot>> PrepareMarginCostsAsync(Scope scope, string? permission,
        SalesOrderDraft draft, CancellationToken cancellationToken)
    {
        var costs = new List<SalesMarginCostSnapshot>();
        foreach (var line in draft.Lines.OrderBy(item => item.LineNo))
        {
            var previous = LastMarginCost(draft, line.Id);
            // The same product/quantity reinstated after customer rejection keeps its original immutable cost.
            if (previous is not null && SameCostBasis(draft, line, previous)) continue;
            if (previous is null && LegacyApprovedCost(draft, line)) continue; // SAT-D273: no backfill.
            costs.Add(await ReadMarginCostAsync(scope, permission, draft, line, cancellationToken));
        }
        return costs;
    }

    private async Task<SalesMarginCostSnapshot> ReadMarginCostAsync(Scope scope, string? permission,
        SalesOrderDraft draft, SalesOrderDraftLine line, CancellationToken cancellationToken)
    {
        if (marginCosts is null) return UnavailableCost(draft, line, SalesLineMarginUnavailableReason.CostSourceMissing);
        scope = await RequireStillAsync(scope, permission, cancellationToken);
        SalesMarginCostReadAnswerV1? answer;
        try
        {
            answer = await marginCosts.ReadAsync(scope.CompanyId, line.Product.ProductId, line.OrderUnit.UnitId,
                draft.Header.BusinessDate, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RequireStillAsync(scope, permission, cancellationToken);
            return UnavailableCost(draft, line, SalesLineMarginUnavailableReason.CostUnreadable);
        }
        await RequireStillAsync(scope, permission, cancellationToken);
        if (answer is null || answer.ContractVersion != 1 || answer.CompanyId != scope.CompanyId
            || answer.ProductId != line.Product.ProductId || answer.UnitId != line.OrderUnit.UnitId)
            return UnavailableCost(draft, line, SalesLineMarginUnavailableReason.CostUnreadable);
        var result = new SalesMarginCostSnapshot(line.Id, draft.Revision, line.Product.ProductId, line.OrderUnit.UnitId,
            line.Quantity.Value, answer.CurrencyId, answer.CostPerUnit, answer.UnavailableReason,
            answer.SourceProof, timeProvider.GetUtcNow());
        try
        {
            result.Validate();
            using var proof = JsonDocument.Parse(result.SourceProof);
            if (proof.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            return result;
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException or ArgumentException)
        {
            return UnavailableCost(draft, line, SalesLineMarginUnavailableReason.CostUnreadable);
        }
    }

    private SalesMarginCostSnapshot UnavailableCost(SalesOrderDraft draft, SalesOrderDraftLine line,
        SalesLineMarginUnavailableReason reason) => new(line.Id, draft.Revision, line.Product.ProductId, line.OrderUnit.UnitId,
            line.Quantity.Value, null, null, reason, JsonSerializer.Serialize(new { unavailable = reason.ToString() }), timeProvider.GetUtcNow());

    private async Task<SalesDraftMarginV1> ReadMarginsAsync(Scope scope, SalesOrderDraft draft, CancellationToken cancellationToken)
    {
        var totals = SalesOrderTotalsCalculator.CalculateWithLineTax(draft);
        var lines = new List<SalesDraftLineMarginV1>();
        foreach (var line in draft.Lines.OrderBy(item => item.LineNo))
        {
            var cost = LastMarginCost(draft, line.Id);
            if (cost is not null && !SameCostBasis(draft, line, cost)
                && draft.ApprovalState == InternalApprovalState.Approved)
                cost = UnavailableCost(draft, line, SalesLineMarginUnavailableReason.CostRepinPolicyPending);
            else if (cost is null || !SameCostBasis(draft, line, cost))
                cost = draft.ApprovalState == InternalApprovalState.Approved || LegacyApprovedCost(draft, line)
                    ? UnavailableCost(draft, line, SalesLineMarginUnavailableReason.CostSourceMissing)
                    : await ReadMarginCostAsync(scope, bindings.ReadPermission, draft, line, cancellationToken);
            var margin = cost.UnavailableReason is { } reason ? SalesLineMargin.Unavailable(reason)
                : !MarginGoldenAccepted ? SalesLineMargin.Unavailable(SalesLineMarginUnavailableReason.FormulaGoldenPending)
                : SalesLineMarginCalculator.Calculate(new SalesLineMarginInput(
                    totals.Lines.Single(item => item.LineId == line.Id).NetAmountBeforeSurcharge,
                    line.OrderUnit.UnitId, line.Quantity.Value, cost.CurrencyId!.Value,
                    SalesLineMarginCost.Of(cost.CostPerUnit!.Value, cost.CurrencyId.Value, cost.UnitId)));
            lines.Add(LineMargin(line.Id, line.LineNo, margin));
        }
        // Every slow owner read can revoke both disclosure names. Nothing is returned on a stale disclosure.
        await RequireStillAsync(scope, bindings.ReadPermission, cancellationToken);
        if (await DiscloseAsync(scope, cancellationToken) != SalesPriceDisclosure.Visible)
            throw new SalesDraftServiceException(403, "forbidden");
        if (await DiscloseMarginAsync(scope, cancellationToken) != SalesMarginDisclosure.Visible)
            return new(SalesMarginDisclosure.Withheld, null, null, null);
        return new(SalesMarginDisclosure.Visible, SalesDraftMarginTextsV1.PurchaseCostOnly,
            draft.ApprovalState == InternalApprovalState.Approved ? SalesDraftMarginTextsV1.Snapshot : SalesDraftMarginTextsV1.Preview, lines);
    }
}
