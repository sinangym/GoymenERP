namespace GoymenERP.Modules.Manufacturing.Application;

/// <summary>
/// One input label of an output's lineage (URT-D63): its consumed kg in the output's order and its share of the output
/// as <c>ProductionLineageShareCalculatorV1</c> computes it — an unrounded ratio in [0, 1], null when the calculator
/// cannot give one (then <see cref="ManufacturingLabelLineageV1.ShareCode"/> says why).
/// </summary>
public sealed record ManufacturingLabelLineageInputV1(Guid InputLabelId, decimal ConsumedKg, decimal? Share);

/// <summary>
/// One output label's lineage (URT-D62, URT-D63): the output declaration of the order it was produced by, Inventory's
/// genealogy transformation the edges were read from, and EVERY input label with its share, ordered by input label id —
/// in the forward and the backward trace alike, all inputs are shown. <see cref="ShareStatus"/> is the calculator's
/// status (<c>Calculated</c>, <c>Pending</c>, <c>Unreadable</c>) and <see cref="TotalConsumedKg"/> the kg the shares are
/// taken of (null unless calculated). The output label carries its own number; the input numbers are not printed on it.
/// </summary>
public sealed record ManufacturingLabelLineageV1(
    Guid CompanyId,
    Guid OrderId,
    Guid OutputId,
    Guid OutputLabelId,
    Guid GenealogyTransformationId,
    int CalculatorVersion,
    string ShareStatus,
    string? ShareCode,
    decimal? TotalConsumedKg,
    IReadOnlyList<ManufacturingLabelLineageInputV1> Inputs);

/// <summary>
/// URT-S22 (API-28, URT-D63, KLN-D44): one order's lineage. <see cref="Outputs"/> are the lineages of the order's own
/// outputs, each with all its input labels and shares (backward inside the order; read the other way round it is the
/// forward trace of each input label). One step beyond the order: <see cref="Upstream"/> are the lineages of the other
/// orders' outputs whose labels this order consumed, and <see cref="Downstream"/> the lineages of the other orders'
/// outputs this order's output labels went into. Every list is ordered by order then output id and bounded by
/// <see cref="IManufacturingLabelLineageReadPortV1.MaximumForwardOutputs"/>; <see cref="Truncated"/> says a bound was met
/// and the answer is not the whole lineage.
/// </summary>
public sealed record ManufacturingOrderLineageV1(
    Guid CompanyId,
    Guid OrderId,
    bool Truncated,
    IReadOnlyList<ManufacturingLabelLineageV1> Outputs,
    IReadOnlyList<ManufacturingLabelLineageV1> Upstream,
    IReadOnlyList<ManufacturingLabelLineageV1> Downstream);

/// <summary>
/// URT-S20 (URT-D62, URT-D63): Manufacturing's own read of its output lineage edges. Backward: the lineage of an output
/// label (which input labels it came from). Forward: the lineage of every output an input label went into, each with all
/// of that output's inputs. Reads only under the caller's resolved company (another company throws), creates nothing and
/// publishes no route. KLN-D44: the caller shows it under the existing label-detail read permission; this port grants
/// nothing and adds no permission.
/// </summary>
public interface IManufacturingLabelLineageReadPortV1
{
    /// <summary>The most outputs one forward read returns (a bound, never an unbounded list).</summary>
    const int MaximumForwardOutputs = 500;

    /// <summary>The output label's lineage, or null when Manufacturing recorded no edge for it.</summary>
    Task<ManufacturingLabelLineageV1?> ReadBackwardAsync(Guid companyId, Guid outputLabelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The lineage of each output the input label went into, ordered by order then output id; at most
    /// <see cref="MaximumForwardOutputs"/>.
    /// </summary>
    Task<IReadOnlyList<ManufacturingLabelLineageV1>> ReadForwardAsync(Guid companyId, Guid inputLabelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// URT-S22 (API-28): the order's lineage with one step upstream and downstream; empty lists when Manufacturing
    /// recorded no edge for the order. It does not say whether the order exists: the caller reads the order first.
    /// </summary>
    Task<ManufacturingOrderLineageV1> ReadOrderAsync(Guid companyId, Guid orderId,
        CancellationToken cancellationToken = default);
}
