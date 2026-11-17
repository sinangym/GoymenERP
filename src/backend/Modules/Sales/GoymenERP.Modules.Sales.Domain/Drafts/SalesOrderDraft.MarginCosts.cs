namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>SAT-D268–274: an append-only cost observed at internal approval, never a stock movement.</summary>
public sealed record SalesMarginCostSnapshot(
    Guid LineId, int Revision, Guid ProductId, Guid UnitId, decimal Quantity,
    Guid? CurrencyId, decimal? CostPerUnit, SalesLineMarginUnavailableReason? UnavailableReason,
    string SourceProof, DateTimeOffset RecordedAtUtc)
{
    public void Validate()
    {
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
                || snapshot.UnitId != line.OrderUnit.UnitId || snapshot.Quantity != line.Quantity.Value
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
