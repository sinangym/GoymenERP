using System.Text.Json;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

public sealed partial class ManufacturingStore : IManufacturingCostGenerationStoreV1
{
    public async Task<IReadOnlyList<ManufacturingCostGenerationV1>> ReadCostGenerationsAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var companyId = RequireResolvedCompany();
        var rows = await dbContext.CostGenerations.AsNoTracking().Where(row => row.CompanyId == companyId && row.OrderId == orderId)
            .OrderBy(row => row.ClosedOrderVersion).ThenBy(row => row.Revision).ToArrayAsync(cancellationToken);
        return rows.Select(CostGeneration).ToArray();
    }

    public async Task<ManufacturingCostGenerationV1> AppendCostGenerationAsync(ManufacturingCostGenerationV1 generation,
        long expectedRevision, ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        RequireMetadata(metadata); RequireCompany(generation.CompanyId);
        if (dbContext.Database.CurrentTransaction is null) throw new InvalidOperationException("Maliyet nesli sahip modüllerin ortak işlemini gerektirir.");
        if (generation.GenerationId == Guid.Empty || generation.OrderId == Guid.Empty || generation.ClosedOrderVersion < 2
            || expectedRevision < 0 || generation.Revision != checked(expectedRevision + 1) || !Enum.IsDefined(generation.State)
            || string.IsNullOrWhiteSpace(generation.InventoryVersion) || generation.InventoryVersion.Length > 128
            || generation.InputFingerprint.Length != 64 || !generation.InputFingerprint.All(Uri.IsHexDigit)
            || string.IsNullOrWhiteSpace(generation.CalculatorVersion) || generation.CalculatorVersion.Length > 128
            || string.IsNullOrWhiteSpace(generation.Reason) || generation.Reason.Length > 1000
            || generation.CorrelationId != metadata.CorrelationId.Trim()) throw new ArgumentException("Maliyet nesli kanıtı geçersiz.");
        using var evidence = JsonDocument.Parse(generation.EvidenceJson); using var result = JsonDocument.Parse(generation.ResultJson);
        var order = await FindOrderAsync(generation.OrderId, cancellationToken)
            ?? throw Refused(ManufacturingStoreCodes.OrderNotFound, "Bu şirketin böyle bir emri yok.");
        if (order.Status != OrderStatus.Closed || order.Version != generation.ClosedOrderVersion)
            throw Refused(ManufacturingStoreCodes.ConcurrentChange, "Emrin kapanış sürümü değişti.");
        var rows = await ReadCostGenerationsAsync(order.Id, cancellationToken);
        var sameClose = rows.Where(row => row.ClosedOrderVersion == generation.ClosedOrderVersion).ToArray();
        var previous = sameClose.LastOrDefault();
        if ((previous?.Revision ?? 0) != expectedRevision || sameClose.Any(row => row.GenerationId != generation.GenerationId)
            || rows.Any(row => row.GenerationId == generation.GenerationId && row.ClosedOrderVersion != generation.ClosedOrderVersion)
            || previous is { State: ManufacturingCostGenerationStateV1.Reversed }
            || previous is { State: ManufacturingCostGenerationStateV1.Recognized } && generation.State != ManufacturingCostGenerationStateV1.Reversed
            || previous is null && generation.State == ManufacturingCostGenerationStateV1.Reversed
            || previous is not null && previous.InventoryVersion == generation.InventoryVersion)
            throw Refused(ManufacturingStoreCodes.ConcurrentChange, "Maliyet nesli değişti; geçmiş yeniden okunmalıdır.");
        dbContext.CostGenerations.Add(new CostGenerationRow
        {
            CompanyId = generation.CompanyId, OrderId = generation.OrderId, GenerationId = generation.GenerationId,
            ClosedOrderVersion = generation.ClosedOrderVersion, Revision = generation.Revision,
            InventoryVersion = generation.InventoryVersion, State = (int)generation.State,
            InputFingerprint = generation.InputFingerprint, CalculatorVersion = generation.CalculatorVersion,
            EvidenceJson = generation.EvidenceJson, ResultJson = generation.ResultJson, RecordedAtUtc = generation.RecordedAtUtc.ToUniversalTime(),
            Reason = generation.Reason, CorrelationId = generation.CorrelationId, RecordedBy = metadata.ActorId.Trim(),
        });
        Audit(metadata, generation.CompanyId, "Manufacturing.CostGeneration", generation.GenerationId,
            "manufacturing.order.cost-generation-recorded.v1", generation.State.ToString(), previous?.Revision,
            generation.Revision, previous is null ? "{}" : JsonSerializer.Serialize(previous, Json), JsonSerializer.Serialize(generation, Json), generation.Reason);
        Complete(metadata, generation.CompanyId, generation);
        await SaveAsync(cancellationToken);
        return generation;
    }

    private static ManufacturingCostGenerationV1 CostGeneration(CostGenerationRow row) => new(row.CompanyId, row.OrderId,
        row.GenerationId, row.ClosedOrderVersion, row.Revision, row.InventoryVersion, (ManufacturingCostGenerationStateV1)row.State,
        row.InputFingerprint, row.CalculatorVersion, row.EvidenceJson, row.ResultJson, row.RecordedAtUtc, row.Reason, row.CorrelationId);
}
