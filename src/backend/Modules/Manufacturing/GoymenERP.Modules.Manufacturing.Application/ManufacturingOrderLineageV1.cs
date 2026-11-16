using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Domain.Lineage;

namespace GoymenERP.Modules.Manufacturing.Application;

// URT-S22 (API-28, UI-18, AT-050, URT-D63, KLN-D44): the order's lineage, read only. The edges and their shares come
// only from Manufacturing's own lineage read port (IManufacturingLabelLineageReadPortV1 over
// ProductionLineageShareCalculatorV1, the one share calculator); this read reshapes them into the backward trace (each
// output label with all its input labels) and the forward trace (each input label with every output it went into),
// and passes the kg and the shares as lossless invariant text, unrounded: no display rounding policy is chosen here.
// KLN-D44: shown under the order read permission the order screen already needs; no new permission, nothing stored.

/// <summary>One input label of an output's lineage: its consumed kg and its share of the output (null unless calculated).</summary>
public sealed record OrderLineageInputShareViewV1(Guid InputLabelId, string ConsumedKg, string? Share);

/// <summary>
/// One output label's lineage — the backward trace: the order and output declaration it was produced by, Inventory's
/// genealogy transformation, the share status (<c>calculated</c>, <c>pending</c>, <c>unreadable</c>) with the
/// calculator's code when not calculated, the total consumed kg the shares are taken of (null unless calculated) and
/// EVERY input label, ordered by input label id.
/// </summary>
public sealed record OrderLineageOutputViewV1(
    Guid OrderId,
    Guid OutputId,
    Guid OutputLabelId,
    Guid GenealogyTransformationId,
    string ShareStatus,
    string? ShareCode,
    string? TotalConsumedKg,
    IReadOnlyList<OrderLineageInputShareViewV1> Inputs);

/// <summary>One output an input label went into, with the input's consumed kg in it and its share of it.</summary>
public sealed record OrderLineageFedOutputViewV1(
    Guid OutputId,
    Guid OutputLabelId,
    string ShareStatus,
    string ConsumedKg,
    string? Share);

/// <summary>One input label of the order — the forward trace: every output of the order it went into.</summary>
public sealed record OrderLineageInputViewV1(Guid InputLabelId, IReadOnlyList<OrderLineageFedOutputViewV1> Outputs);

/// <summary>
/// The order's lineage, version 1. <see cref="Outputs"/> is the backward trace of the order's own output labels and
/// <see cref="Inputs"/> the forward trace of its input labels (the same edges read the other way round). One step
/// beyond the order: <see cref="Upstream"/> are other orders' outputs whose labels this order consumed and
/// <see cref="Downstream"/> other orders' outputs this order's output labels went into. <see cref="Truncated"/> says a
/// list met the read's bound and the answer is not the whole lineage.
/// </summary>
public sealed record OrderLineageViewV1(
    int ContractVersion,
    Guid OrderId,
    int CalculatorVersion,
    bool Truncated,
    IReadOnlyList<OrderLineageOutputViewV1> Outputs,
    IReadOnlyList<OrderLineageInputViewV1> Inputs,
    IReadOnlyList<OrderLineageOutputViewV1> Upstream,
    IReadOnlyList<OrderLineageOutputViewV1> Downstream);

/// <summary>The share statuses of the order lineage contract.</summary>
public static class ManufacturingOrderLineageCodesV1
{
    public const int ContractVersion = 1;

    public const string Calculated = "calculated";
    public const string Pending = "pending";
    public const string Unreadable = "unreadable";
}

public sealed partial class ManufacturingCommandService
{
    /// <summary>
    /// URT-S22 (API-28): one order's lineage under the order read permission
    /// (<see cref="ErpPermissions.ManufacturingOrdersRead"/>, KLN-D44), measured again after the reads. The
    /// server-resolved company only: another company's order is not found, never forbidden. A read meeting concurrent
    /// commits, no lineage port, a failing port or an answer the read cannot trust (another company or order, an unknown
    /// status, a calculated line without a share) is a retryable 503 — never a partial or empty lineage in its place.
    /// Cancellation is not swallowed.
    /// </summary>
    public async Task<ManufacturingApiResult<OrderLineageViewV1>> GetOrderLineageAsync(Guid orderId,
        CancellationToken cancellationToken)
    {
        if (!HasContext() || ActorGuid() is not { } actorId)
        {
            return Denied<OrderLineageViewV1>();
        }

        if (orderId == Guid.Empty)
        {
            return NotFoundResult<OrderLineageViewV1>();
        }

        var companyId = companyContext.CompanyId;
        ManufacturingApiResult<OrderLineageViewV1> answer;
        try
        {
            var order = await orders.FindOrderAsync(orderId, cancellationToken);
            if (order is null || order.CompanyId != companyId)
            {
                answer = NotFoundResult<OrderLineageViewV1>();
            }
            else
            {
                answer = await ReadOrderLineageAsync(companyId, order.Id, cancellationToken) is { } view
                    ? new ManufacturingApiResult<OrderLineageViewV1>(ManufacturingApiStatus.Ok, view, false, null)
                    : new ManufacturingApiResult<OrderLineageViewV1>(ManufacturingApiStatus.Unavailable, null, false,
                        null);
            }
        }
        catch (ManufacturingStoreException exception) when (exception.Code == ManufacturingStoreCodes.ConcurrentChange)
        {
            answer = new ManufacturingApiResult<OrderLineageViewV1>(ManufacturingApiStatus.Unavailable, null, false,
                null);
        }

        // A grant withdrawn while the lineage was read answers nothing of it.
        var scope = await authority.RevalidatePermissionScopeAsync(
            ErpPermissions.ManufacturingOrdersRead, companyId, cancellationToken);
        return scope.IsGranted && HasCapturedContext(companyId, actorId)
            ? answer
            : Denied<OrderLineageViewV1>();
    }

    private async Task<OrderLineageViewV1?> ReadOrderLineageAsync(Guid companyId, Guid orderId,
        CancellationToken cancellationToken)
    {
        if (labelLineage is null)
        {
            return null;
        }

        ManufacturingOrderLineageV1 lineage;
        try
        {
            lineage = await labelLineage.ReadOrderAsync(companyId, orderId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }

        return OrderLineageView(companyId, orderId, lineage);
    }

    /// <summary>
    /// The port's answer as the order lineage shows it, or null when the answer cannot be trusted: another company or
    /// order, an own output of another order, an upstream or downstream output of this order, another calculator
    /// version, an unknown status, a calculated lineage without its total or a line without its share, or a share on a
    /// lineage that is not calculated.
    /// </summary>
    internal static OrderLineageViewV1? OrderLineageView(Guid companyId, Guid orderId,
        ManufacturingOrderLineageV1? lineage)
    {
        if (lineage is null || lineage.CompanyId != companyId || lineage.OrderId != orderId
            || lineage.Outputs is null || lineage.Upstream is null || lineage.Downstream is null)
        {
            return null;
        }

        var outputs = Lineages(lineage.Outputs, companyId, output => output.OrderId == orderId);
        var upstream = Lineages(lineage.Upstream, companyId, output => output.OrderId != orderId);
        var downstream = Lineages(lineage.Downstream, companyId, output => output.OrderId != orderId);
        if (outputs is null || upstream is null || downstream is null)
        {
            return null;
        }

        var inputs = outputs
            .SelectMany(output => output.Inputs.Select(input => (Input: input, Output: output)))
            .GroupBy(pair => pair.Input.InputLabelId)
            .OrderBy(group => group.Key)
            .Select(group => new OrderLineageInputViewV1(group.Key,
                group.Select(pair => new OrderLineageFedOutputViewV1(pair.Output.OutputId, pair.Output.OutputLabelId,
                    pair.Output.ShareStatus, pair.Input.ConsumedKg, pair.Input.Share)).ToArray()))
            .ToArray();
        return new OrderLineageViewV1(ManufacturingOrderLineageCodesV1.ContractVersion, orderId,
            ProductionLineageShareCalculatorV1.ContractVersion, lineage.Truncated, outputs, inputs, upstream,
            downstream);
    }

    private static OrderLineageOutputViewV1[]? Lineages(IReadOnlyList<ManufacturingLabelLineageV1> lineages,
        Guid companyId, Func<ManufacturingLabelLineageV1, bool> belongs)
    {
        var views = new OrderLineageOutputViewV1[lineages.Count];
        for (var index = 0; index < lineages.Count; index++)
        {
            var lineage = lineages[index];
            if (lineage is null || lineage.CompanyId != companyId || !belongs(lineage)
                || LineageOutputView(lineage) is not { } view)
            {
                return null;
            }

            views[index] = view;
        }

        return views;
    }

    private static OrderLineageOutputViewV1? LineageOutputView(ManufacturingLabelLineageV1 lineage)
    {
        if (lineage.CalculatorVersion != ProductionLineageShareCalculatorV1.ContractVersion
            || lineage.Inputs is null || lineage.Inputs.Any(input => input is null))
        {
            return null;
        }

        var status = lineage.ShareStatus switch
        {
            nameof(ProductionLineageShareStatus.Calculated) => ManufacturingOrderLineageCodesV1.Calculated,
            nameof(ProductionLineageShareStatus.Pending) => ManufacturingOrderLineageCodesV1.Pending,
            nameof(ProductionLineageShareStatus.Unreadable) => ManufacturingOrderLineageCodesV1.Unreadable,
            _ => null,
        };
        var calculated = status == ManufacturingOrderLineageCodesV1.Calculated;
        if (status is null
            || (calculated && (lineage.TotalConsumedKg is null || lineage.ShareCode is not null
                || lineage.Inputs.Any(input => input.Share is null)))
            || (!calculated && (lineage.TotalConsumedKg is not null || string.IsNullOrEmpty(lineage.ShareCode)
                || lineage.Inputs.Any(input => input.Share is not null))))
        {
            return null;
        }

        return new OrderLineageOutputViewV1(lineage.OrderId, lineage.OutputId, lineage.OutputLabelId,
            lineage.GenealogyTransformationId, status, lineage.ShareCode,
            lineage.TotalConsumedKg is { } total ? Text(total) : null,
            lineage.Inputs.Select(input => new OrderLineageInputShareViewV1(input.InputLabelId, Text(input.ConsumedKg),
                input.Share is { } share ? Text(share) : null)).ToArray());
    }
}
