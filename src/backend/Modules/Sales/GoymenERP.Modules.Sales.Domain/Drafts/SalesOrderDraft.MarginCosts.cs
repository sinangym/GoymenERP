namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// SAT-D426: the rate the order froze at its internal approval from the sale currency into the functional currency — the Settings
/// rate of the approval day (<paramref name="RateDay"/>) — kept with the line's approval snapshot and never re-read for display.
/// </summary>
public sealed record SalesMarginSaleRate(Guid CurrencyId, decimal Rate, DateOnly RateDay)
{
    /// <summary>The rate's stored precision (the column's scale).</summary>
    public const int RateScale = 8;

    public bool IsValid => CurrencyId != Guid.Empty && Rate > 0m && Rate <= 99999999.99999999m && RateDay != default
        && decimal.Round(Rate, RateScale, MidpointRounding.AwayFromZero) == Rate;
}

/// <summary>
/// SAT-D268–274: an append-only cost observed at internal approval, never a stock movement. SAT-D426: a foreign-currency order
/// also freezes its sale rate here (<see cref="SaleRate"/>); a TL order, or an order whose rate could not be read, keeps none.
/// </summary>
public sealed record SalesMarginCostSnapshot(
    Guid LineId, int Revision, Guid ProductId, Guid UnitId, decimal Quantity,
    Guid? CurrencyId, decimal? CostPerUnit, SalesLineMarginUnavailableReason? UnavailableReason,
    string SourceProof, DateTimeOffset RecordedAtUtc, SalesMarginSaleRate? SaleRate = null)
{
    public void Validate()
    {
        if (SaleRate is { IsValid: false })
            throw new InvalidOperationException("Marj satış kuru anlık görüntüsü geçersiz.");
        if (LineId == Guid.Empty || ProductId == Guid.Empty || UnitId == Guid.Empty || Revision < 0 || Quantity <= 0
            || string.IsNullOrWhiteSpace(SourceProof) || SourceProof.Length > 65536
            || RecordedAtUtc == default
            || (UnavailableReason is null
                ? CurrencyId is null || CurrencyId == Guid.Empty || CostPerUnit is null || CostPerUnit < 0 || CostPerUnit > 999999999999999.9999m
                    || decimal.Round(CostPerUnit.Value, 4, MidpointRounding.AwayFromZero) != CostPerUnit
                : !Enum.IsDefined(UnavailableReason.Value) || CurrencyId is not null || CostPerUnit is not null))
            throw new InvalidOperationException("Marj maliyeti kaynak anlık görüntüsü geçersiz.");
    }
}

public sealed partial class SalesOrderDraft
{
    private readonly List<SalesMarginCostSnapshot> marginCosts = [];
    public IReadOnlyList<SalesMarginCostSnapshot> MarginCosts => marginCosts.AsReadOnly();

    public void PinMarginCosts(IReadOnlyList<SalesMarginCostSnapshot> snapshots)
    {
        if (ApprovalState != InternalApprovalState.Approved)
            throw new InvalidOperationException("Marj maliyeti yalnız iç onayla sabitlenir.");
        if (snapshots.Select(item => item.LineId).Distinct().Count() != snapshots.Count)
            throw new InvalidOperationException("Aynı satırın maliyeti bir onayda iki kez sabitlenemez.");
        foreach (var snapshot in snapshots)
        {
            snapshot.Validate();
            var line = Lines.SingleOrDefault(candidate => candidate.Id == snapshot.LineId);
            if (line is null || snapshot.Revision != Revision || snapshot.ProductId != line.Product.ProductId
                // SAT-D438: a non-kg line may pin its cost in the cost unit (kg); a kg line pins in its own unit only.
                || (snapshot.UnitId != line.OrderUnit.UnitId && line.OrderUnit.IsKilogram) || snapshot.Quantity != line.Quantity.Value
                || snapshot.RecordedAtUtc != Approval.DecidedAtUtc
                || marginCosts.Any(item => item.LineId == snapshot.LineId && item.Revision == snapshot.Revision))
                throw new InvalidOperationException("Marj maliyeti onaylanan satırla uyuşmuyor.");
        }
        marginCosts.AddRange(snapshots);
    }

    public void RestoreMarginCost(SalesMarginCostSnapshot snapshot)
    {
        snapshot.Validate();
        if (snapshot.Revision > Revision || marginCosts.Any(item => item.LineId == snapshot.LineId && item.Revision == snapshot.Revision))
            throw new InvalidOperationException("Marj maliyeti geçmişi değiştirilemez.");
        marginCosts.Add(snapshot);
    }
}
