using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Engines;
using GoymenERP.Modules.Manufacturing.Domain.Engines.Balance;

namespace GoymenERP.Modules.Manufacturing.Domain.Orders;

/// <summary>
/// URT-D49 (1): the mass account of a whole order — every planned input's <see cref="MassBalance"/> added category by
/// category and put through the same BAL-003 calculator (<see cref="MassBalanceEngineV1"/>, <c>GDC-BAL/1</c>). It is the
/// scope of the "output above input" comparison for a process without a main roll input
/// (<see cref="Plans.ManufacturingProcessMainInputV1"/>); a roll-based process keeps the per-roll comparison. The output
/// side is the same as a roll's: every output class, usable remnant included, the latest WIP reading of each input and
/// the recorded corrections (URT-D48 (1)).
/// </summary>
public sealed record OrderMassTotalV1
{
    private OrderMassTotalV1(Kilograms netIssued, MassDifferenceV1 reconciliation)
    {
        NetIssued = netIssued;
        Reconciliation = reconciliation;
    }

    /// <summary>Σ of the inputs' issued minus returned kg.</summary>
    public Kilograms NetIssued { get; }

    /// <summary>BAL-003 for the order total.</summary>
    public MassDifferenceV1 Reconciliation { get; }

    public decimal Unexplained => Reconciliation.DifferenceKg;

    /// <summary>More mass is accounted on the order as a whole than was issued to it (BAL-003 below zero).</summary>
    public bool OutputExceedsInput => Unexplained < 0m;

    public static OrderMassTotalV1 Of(IReadOnlyCollection<MassBalance> balances)
    {
        ArgumentNullException.ThrowIfNull(balances);
        if (balances.Any(balance => balance is null))
        {
            throw new ArgumentException("Emir toplamı yalnız rulo/girdi dengelerinden oluşur.", nameof(balances));
        }

        Kilograms Sum(Func<MassBalance, Kilograms> mass) =>
            balances.Aggregate(Kilograms.Zero, (sum, balance) => sum + mass(balance));

        var netIssued = Sum(balance => balance.NetIssued);
        var corrections = Sum(_ => MassBalance.RecordedMeasurementCorrections);
        var result = MassBalanceEngineV1.Difference(new MassDifferenceInputV1(
            netIssued.Value,
            Sum(balance => balance.OutputOf(OutputClass.GoodProduct)).Value,
            Sum(balance => balance.OutputOf(OutputClass.SecondQuality)).Value,
            Sum(balance => balance.OutputOf(OutputClass.UsableRemnant)).Value,
            Sum(balance => balance.OutputOf(OutputClass.Scrap)).Value,
            Sum(balance => balance.OutputOf(OutputClass.ProcessLoss)).Value,
            Sum(balance => balance.WorkInProgress).Value,
            corrections.Value));

        // Every operand is a sum of Kilograms (never negative, at most three decimals): the engine cannot refuse them.
        return result.Outcome == EngineOutcome.Computed
            ? new OrderMassTotalV1(netIssued, result.Value!)
            : throw new InvalidOperationException(
                $"GDC-BAL/1 refused an order's own total masses ({result.RefusalCode} on {result.RefusedParameter}).");
    }
}
