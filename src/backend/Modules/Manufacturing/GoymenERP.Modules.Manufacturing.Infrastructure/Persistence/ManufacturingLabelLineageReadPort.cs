using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Lineage;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

/// <summary>
/// URT-S20 (URT-D62, URT-D63): Manufacturing's read of its own output lineage edges, backward by output label and
/// forward by input label; URT-S22 (API-28) adds the order's lineage with one step upstream and downstream. Every
/// lineage carries all the output's input labels and their shares from
/// <see cref="ProductionLineageShareCalculatorV1"/> — the one calculator; nothing here computes a share of its own. Reads
/// under the caller's resolved company only (the tenant filter), without tracking, and creates nothing.
/// </summary>
internal sealed class ManufacturingLabelLineageReadPort(ManufacturingDbContext dbContext, ICompanyContext companyContext)
    : IManufacturingLabelLineageReadPortV1
{
    public async Task<ManufacturingLabelLineageV1?> ReadBackwardAsync(Guid companyId, Guid outputLabelId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireCompany(companyId);
        if (outputLabelId == Guid.Empty)
        {
            throw new ArgumentException("Soy izi sorgusu geçersiz.", nameof(outputLabelId));
        }

        var edges = await dbContext.OutputLineageEdges.AsNoTracking()
            .Where(edge => edge.OutputLabelId == outputLabelId)
            .ToListAsync(cancellationToken);
        if (edges.Count == 0)
        {
            return null;
        }

        var outputs = edges.Select(edge => edge.OutputId).Distinct().ToArray();
        if (outputs.Length != 1)
        {
            // An output label is produced by exactly one output declaration; edges of two outputs under one label are
            // a broken record, never merged into one lineage.
            throw new InvalidOperationException("Çıktı etiketinin soy kenarları birden çok çıktıya bağlı; soy izi okunmaz.");
        }

        return Lineage(edges);
    }

    public async Task<IReadOnlyList<ManufacturingLabelLineageV1>> ReadForwardAsync(Guid companyId, Guid inputLabelId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireCompany(companyId);
        if (inputLabelId == Guid.Empty)
        {
            throw new ArgumentException("Soy izi sorgusu geçersiz.", nameof(inputLabelId));
        }

        var outputIds = await dbContext.OutputLineageEdges.AsNoTracking()
            .Where(edge => edge.InputLabelId == inputLabelId)
            .OrderBy(edge => edge.OrderId)
            .ThenBy(edge => edge.OutputId)
            .Select(edge => edge.OutputId)
            .Take(IManufacturingLabelLineageReadPortV1.MaximumForwardOutputs)
            .ToListAsync(cancellationToken);
        if (outputIds.Count == 0)
        {
            return [];
        }

        var edges = await dbContext.OutputLineageEdges.AsNoTracking()
            .Where(edge => outputIds.Contains(edge.OutputId))
            .ToListAsync(cancellationToken);
        var byOutput = edges.ToLookup(edge => edge.OutputId);
        return [.. outputIds.Select(outputId => Lineage([.. byOutput[outputId]]))];
    }

    public async Task<ManufacturingOrderLineageV1> ReadOrderAsync(Guid companyId, Guid orderId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireCompany(companyId);
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("Soy izi sorgusu geçersiz.", nameof(orderId));
        }

        var edges = dbContext.OutputLineageEdges.AsNoTracking();
        var (ownIds, ownTruncated) = await OutputIdsAsync(edges.Where(edge => edge.OrderId == orderId), false,
            cancellationToken);
        var outputs = await LineagesAsync(ownIds, cancellationToken);

        // One step beyond the order, on both sides; the order's own outputs are already in Outputs.
        var inputLabels = outputs.SelectMany(lineage => lineage.Inputs).Select(input => input.InputLabelId)
            .Distinct().ToArray();
        var outputLabels = outputs.Select(lineage => lineage.OutputLabelId).Distinct().ToArray();
        var (upstreamIds, upstreamTruncated) = await OutputIdsAsync(
            edges.Where(edge => edge.OrderId != orderId && inputLabels.Contains(edge.OutputLabelId)),
            inputLabels.Length == 0, cancellationToken);
        var (downstreamIds, downstreamTruncated) = await OutputIdsAsync(
            edges.Where(edge => edge.OrderId != orderId && outputLabels.Contains(edge.InputLabelId)),
            outputLabels.Length == 0, cancellationToken);

        return new ManufacturingOrderLineageV1(
            companyId,
            orderId,
            ownTruncated || upstreamTruncated || downstreamTruncated,
            outputs,
            await LineagesAsync(upstreamIds, cancellationToken),
            await LineagesAsync(downstreamIds, cancellationToken));
    }

    /// <summary>
    /// The distinct outputs of the edges, ordered by order then output id, at most
    /// <see cref="IManufacturingLabelLineageReadPortV1.MaximumForwardOutputs"/>; one more is read only to say whether the
    /// bound cut the list. With <paramref name="nothingToMatch"/> (no label to look for) nothing is read.
    /// </summary>
    private static async Task<(List<Guid> OutputIds, bool Truncated)> OutputIdsAsync(
        IQueryable<OutputLineageEdgeRow> edges, bool nothingToMatch, CancellationToken cancellationToken)
    {
        if (nothingToMatch)
        {
            return ([], false);
        }

        const int bound = IManufacturingLabelLineageReadPortV1.MaximumForwardOutputs;
        var outputIds = await edges
            .Select(edge => new { edge.OrderId, edge.OutputId })
            .Distinct()
            .OrderBy(key => key.OrderId)
            .ThenBy(key => key.OutputId)
            .Take(bound + 1)
            .Select(key => key.OutputId)
            .ToListAsync(cancellationToken);
        return outputIds.Count > bound ? (outputIds.Take(bound).ToList(), true) : (outputIds, false);
    }

    /// <summary>Every edge of the given outputs, one lineage per output in the given order.</summary>
    private async Task<IReadOnlyList<ManufacturingLabelLineageV1>> LineagesAsync(List<Guid> outputIds,
        CancellationToken cancellationToken)
    {
        if (outputIds.Count == 0)
        {
            return [];
        }

        var edges = await dbContext.OutputLineageEdges.AsNoTracking()
            .Where(edge => outputIds.Contains(edge.OutputId))
            .ToListAsync(cancellationToken);
        var byOutput = edges.ToLookup(edge => edge.OutputId);
        return [.. outputIds.Select(outputId => Lineage([.. byOutput[outputId]]))];
    }

    private void RequireCompany(Guid companyId)
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || companyContext.CompanyId != companyId)
        {
            // Deny-first: without the caller's resolved company, or for another company, there is no tenant filter to read under.
            throw new InvalidOperationException("Üretim soy izi yalnız çözümlenmiş şirket bağlamında okunur.");
        }
    }

    private static ManufacturingLabelLineageV1 Lineage(IReadOnlyList<OutputLineageEdgeRow> edges)
    {
        var first = edges[0];
        var shares = ProductionLineageShareCalculatorV1.Calculate(
            [.. edges.Select(edge => new ProductionLineageShareInput(edge.InputLabelId, new Kilograms(edge.ConsumedKg)))]);
        IReadOnlyList<ManufacturingLabelLineageInputV1> inputs = shares.Status == ProductionLineageShareStatus.Calculated
            ? [.. shares.Lines.Select(line => new ManufacturingLabelLineageInputV1(line.InputLabelId, line.ConsumedKg, line.Share))]
            : [.. edges.OrderBy(edge => edge.InputLabelId)
                .Select(edge => new ManufacturingLabelLineageInputV1(edge.InputLabelId, edge.ConsumedKg, null))];
        return new ManufacturingLabelLineageV1(
            first.CompanyId,
            first.OrderId,
            first.OutputId,
            first.OutputLabelId,
            first.GenealogyTransformationId,
            ProductionLineageShareCalculatorV1.ContractVersion,
            shares.Status.ToString(),
            shares.PendingCode ?? shares.UnreadableCode,
            shares.TotalConsumedKg,
            inputs);
    }
}
