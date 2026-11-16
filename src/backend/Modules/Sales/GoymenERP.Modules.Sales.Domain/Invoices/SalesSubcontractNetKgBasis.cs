namespace GoymenERP.Modules.Sales.Domain.Invoices;

/// <summary>
/// One delivery weighing as the Sales subcontract invoice sees it (KLN-D89; Logistics' net-weight read port, adapted by the
/// host): the weighing identity, whether the owner answered a consistent record, its net kilograms and the Sales order lines
/// the weighed loading instruction serves. Facts only: no share, no money.
/// </summary>
/// <param name="WeighingId">The Logistics weighing identity (one weighing per loading instruction).</param>
/// <param name="IsAvailable">True only when the Logistics owner answered a consistent record; NoSource and Unavailable are false.</param>
/// <param name="NetKg">The weighbridge net kilograms (WGT-001); null when the owner did not answer one.</param>
/// <param name="ServedOrderLineIds">The Sales order lines the weighed instruction's lines serve.</param>
public sealed record SalesSubcontractWeighingFacts(
    Guid WeighingId,
    bool IsAvailable,
    decimal? NetKg,
    IReadOnlyCollection<Guid> ServedOrderLineIds);

/// <summary>One subcontract invoice line's kg ratio share: its own weighing's net kilograms over the invoice's net kilograms.</summary>
public sealed record SalesSubcontractNetKgShare(Guid OrderLineId, Guid WeighingId, Quantity NetKg);

/// <summary>
/// KLN-D89 (SINAN-CEVAP 34.1): the kilograms the subcontract delivery invoice's kg ratio split is measured in. Each line's share
/// is its <b>weighbridge net kg</b> over the sum of those net kilograms (<see cref="Denominator"/>). The declared (waybill)
/// kilograms and the LOJ-D12 shipment-share kilograms are never used as the ratio basis, so this type takes neither.
/// <para>
/// Fail closed: KLN-D89 chose no rule for a line without its own weighing, so the calculation stops with an explicit code
/// instead of borrowing another kilogram. A line has its own weighing only when the weighing is available, its net kg is
/// positive and the weighed instruction serves that order line alone. A weighing shared by several order lines is split by
/// <see cref="SalesSubcontractWeighingSplit"/> (SAT-D376/D377/D402, theoretical kg, whole kg, residue on the last line); this
/// multi-weighing basis does not split it, so each of its lines is "not weighed" here.
/// </para>
/// <para>
/// No money is split here: the residue rule of a money split is not chosen. The basis is exact (kilograms at
/// three (the kilogram scale) decimals, no rounding), and the one Sales invoice calculator prices it.
/// </para>
/// </summary>
public sealed class SalesSubcontractNetKgBasis
{
    private const string NetKgUnavailableMessage = "Teslim tartımının net kg'ı okunamadı; fason fatura kg oranı hesaplanmadı.";
    private const string LineNotWeighedMessage =
        "Satırın kendi teslim tartımı yok; KLN-D89 tartımsız satır için kural seçmedi, fason fatura kg oranı hesaplanmadı.";

    private SalesSubcontractNetKgBasis(IReadOnlyList<SalesSubcontractNetKgShare> shares, Quantity denominator)
    {
        Shares = shares;
        Denominator = denominator;
    }

    /// <summary>Every invoice line's net kg, in the order the lines were asked.</summary>
    public IReadOnlyList<SalesSubcontractNetKgShare> Shares { get; }

    /// <summary>The ratio denominator: the sum of the lines' weighbridge net kilograms (KLN-D89).</summary>
    public Quantity Denominator { get; }

    /// <summary>
    /// Builds the basis for the invoice lines (<paramref name="lines"/>: order line → the weighing it was delivered with, null
    /// when it has none) from the weighings the Logistics owner answered.
    /// </summary>
    /// <exception cref="SalesRuleException">
    /// <see cref="SalesRuleCodes.SubcontractNetKgUnavailable"/> when a line's weighing was not answered consistently;
    /// <see cref="SalesRuleCodes.SubcontractLineNotWeighed"/> when a line has no weighing of its own. Both name the line.
    /// </exception>
    public static SalesSubcontractNetKgBasis Of(
        IReadOnlyList<(Guid OrderLineId, Guid? WeighingId)> lines,
        IReadOnlyCollection<SalesSubcontractWeighingFacts> weighings)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(weighings);
        if (lines.Count == 0)
        {
            throw new ArgumentException("Fason fatura kg oranı en az bir satır ister.", nameof(lines));
        }

        if (lines.Select(line => line.OrderLineId).Distinct().Count() != lines.Count)
        {
            throw new ArgumentException("Aynı sipariş satırı fason fatura kg oranında iki kez yer alamaz.", nameof(lines));
        }

        if (weighings.Select(weighing => weighing.WeighingId).Distinct().Count() != weighings.Count)
        {
            throw new ArgumentException("Aynı teslim tartımı iki kez verilemez.", nameof(weighings));
        }

        var byId = weighings.ToDictionary(weighing => weighing.WeighingId);
        var shares = new List<SalesSubcontractNetKgShare>(lines.Count);
        var denominator = Quantity.Zero(QuantityUnit.Kilogram);
        foreach (var (orderLineId, weighingId) in lines)
        {
            if (weighingId is not { } id || !byId.TryGetValue(id, out var weighing))
            {
                throw new SalesRuleException(SalesRuleCodes.SubcontractLineNotWeighed, LineNotWeighedMessage, orderLineId);
            }

            if (!weighing.IsAvailable || weighing.NetKg is not { } netKg || netKg <= 0m
                || decimal.Round(netKg, Quantity.KilogramScale) != netKg)
            {
                throw new SalesRuleException(SalesRuleCodes.SubcontractNetKgUnavailable, NetKgUnavailableMessage, orderLineId);
            }

            var served = weighing.ServedOrderLineIds;
            if (served.Distinct().Count() != 1 || !served.Contains(orderLineId))
            {
                throw new SalesRuleException(SalesRuleCodes.SubcontractLineNotWeighed, LineNotWeighedMessage, orderLineId);
            }

            var lineNetKg = Quantity.Positive(netKg, QuantityUnit.Kilogram);
            shares.Add(new SalesSubcontractNetKgShare(orderLineId, id, lineNetKg));
            denominator = denominator.Add(lineNetKg);
        }

        // One weighing serves one order line here, so two invoice lines never name the same weighing.
        if (shares.Select(share => share.WeighingId).Distinct().Count() != shares.Count)
        {
            throw new SalesRuleException(SalesRuleCodes.InternalInvariant, LineNotWeighedMessage);
        }

        return new SalesSubcontractNetKgBasis(shares, denominator);
    }

    /// <summary>
    /// The exact ratio of one line (its net kg over <see cref="Denominator"/>), unrounded; the money it weighs is rounded by the
    /// invoice calculator, never here.
    /// </summary>
    public decimal RatioOf(Guid orderLineId)
    {
        var share = Shares.SingleOrDefault(candidate => candidate.OrderLineId == orderLineId)
            ?? throw new ArgumentException("Sipariş satırı bu fason fatura kg oranında yok.", nameof(orderLineId));
        return share.NetKg.Value / Denominator.Value;
    }
}
