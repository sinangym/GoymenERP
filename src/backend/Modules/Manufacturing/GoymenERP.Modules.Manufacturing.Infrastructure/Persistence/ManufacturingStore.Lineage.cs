using System.Text.Json;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Lineage;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

/// <summary>
/// URT-S20 (URT-D62, URT-D63): the lineage edges of a declared output — every input label the order consumed, with its
/// consumed kg, as Inventory staged them for the output's genealogy (Inventory's port answer; Manufacturing never writes
/// Inventory's label tables). The edges are recorded in the output declaration's own transaction, after the output fact,
/// and are a snapshot of that moment: append-only, never changed or deleted here. The share each input carries is not
/// stored; it is <see cref="ProductionLineageShareCalculatorV1"/>'s answer over the edges, here at write (the edges must
/// be readable by it) and at every read.
/// </summary>
public sealed partial class ManufacturingStore
{
    private const string OutputLineageEventType = "manufacturing.output.lineage.recorded.v1";

    /// <summary>
    /// Records the lineage edges of the output <paramref name="outputId"/> of <paramref name="orderId"/>. It runs inside
    /// the caller's output transaction and writes no command record (the output declaration's own record covers the
    /// command) and no order version. With edges already stored for the output, the same edges are a replay and other
    /// edges are refused (<see cref="ManufacturingStoreCodes.FactIdConflict"/>). A replayed output with no edges stored
    /// (an output declared before the edges were recorded) records none: the edges are a snapshot of the declaration,
    /// never taken afterwards.
    /// </summary>
    public async Task<ManufacturingWriteOutcome> RecordOutputLineageAsync(Guid orderId, Guid outputId, Guid outputLabelId,
        Guid genealogyTransformationId, IReadOnlyList<ProductionLineageShareInput> inputs, bool outputReplayed,
        ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        RequireMetadata(metadata);
        var companyId = RequireResolvedCompany();
        if (orderId == Guid.Empty || outputId == Guid.Empty || outputLabelId == Guid.Empty
            || genealogyTransformationId == Guid.Empty
            || inputs.Any(input => input is not null && input.InputLabelId == outputLabelId))
        {
            throw Refused(ManufacturingStoreCodes.LineageInvalid, "Çıktının soy kenarı geçersiz; hiçbir şey kaydedilmedi.");
        }

        var shares = ProductionLineageShareCalculatorV1.Calculate(inputs);
        if (shares.Status != ProductionLineageShareStatus.Calculated)
        {
            throw Refused(ManufacturingStoreCodes.LineageInvalid,
                "Çıktının girdi etiketleri okunamadı; soy kenarı kaydedilmedi.");
        }

        RequireFits([.. inputs.Select(input => input.ConsumedKg)]);

        var outputKind = (int)FactKind.OutputDeclaration;
        var declared = await dbContext.Facts.AsNoTracking()
            .AnyAsync(fact => fact.Kind == outputKind && fact.Id == outputId && fact.OrderId == orderId, cancellationToken);
        if (!declared)
        {
            throw Refused(ManufacturingStoreCodes.LineageInvalid,
                "Soy kenarı bu emrin beyan edilmiş bir çıktısına ait değil; hiçbir şey kaydedilmedi.");
        }

        var stored = await dbContext.OutputLineageEdges.AsNoTracking()
            .Where(edge => edge.OutputId == outputId)
            .ToListAsync(cancellationToken);
        if (stored.Count > 0)
        {
            return SameEdges(stored, orderId, outputLabelId, genealogyTransformationId, shares)
                ? ManufacturingWriteOutcome.Replayed
                : throw Refused(ManufacturingStoreCodes.FactIdConflict,
                    "Aynı çıktının soy kenarları farklı içerikle verildi.");
        }

        if (outputReplayed)
        {
            return ManufacturingWriteOutcome.Replayed;
        }

        var now = timeProvider.GetUtcNow();
        var recordedBy = metadata.ActorId.Trim();
        var correlationId = metadata.CorrelationId.Trim();
        foreach (var line in shares.Lines)
        {
            dbContext.OutputLineageEdges.Add(new OutputLineageEdgeRow
            {
                CompanyId = companyId,
                OrderId = orderId,
                OutputKind = outputKind,
                OutputId = outputId,
                InputLabelId = line.InputLabelId,
                OutputLabelId = outputLabelId,
                GenealogyTransformationId = genealogyTransformationId,
                ConsumedKg = line.ConsumedKg,
                CalculatorVersion = ProductionLineageShareCalculatorV1.ContractVersion,
                RecordedAtUtc = now,
                RecordedBy = recordedBy,
                CorrelationId = correlationId,
            });
        }

        Audit(metadata, companyId, OrderEntityType, orderId, OutputLineageEventType, "RecordLineage",
            versionBefore: null, versionAfter: null, before: "{}",
            after: JsonSerializer.Serialize(new
            {
                outputId,
                outputLabelId,
                genealogyTransformationId,
                calculatorVersion = ProductionLineageShareCalculatorV1.ContractVersion,
                totalConsumedKg = shares.TotalConsumedKg,
                inputs = shares.Lines.Select(line => new
                {
                    inputLabelId = line.InputLabelId,
                    consumedKg = line.ConsumedKg,
                    share = line.Share,
                }),
            }, Json),
            "Üretim çıktısının girdi etiketi soy kenarları");
        await SaveAsync(cancellationToken);
        return ManufacturingWriteOutcome.Recorded;
    }

    private static bool SameEdges(IReadOnlyList<OutputLineageEdgeRow> stored, Guid orderId, Guid outputLabelId,
        Guid genealogyTransformationId, ProductionLineageShareResult shares)
    {
        if (stored.Count != shares.Lines.Count
            || stored.Any(edge => edge.OrderId != orderId || edge.OutputLabelId != outputLabelId
                                  || edge.GenealogyTransformationId != genealogyTransformationId))
        {
            return false;
        }

        var consumed = stored.ToDictionary(edge => edge.InputLabelId, edge => edge.ConsumedKg);
        return shares.Lines.All(line =>
            consumed.TryGetValue(line.InputLabelId, out var kg) && kg == line.ConsumedKg);
    }
}
