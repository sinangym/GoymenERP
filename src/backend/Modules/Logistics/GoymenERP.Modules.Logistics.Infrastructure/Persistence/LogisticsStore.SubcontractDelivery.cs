using System.Data;
using System.Text.Json;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.Quantities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

public sealed partial class LogisticsStore : ILogisticsSubcontractDeliverySourceStoreV1
{
    public Task<LogisticsStoreResult> StoreSubcontractInstructionAsync(LogisticsWriteEnvelope envelope, string requestHash, DateOnly effectiveOn,
        Func<LoadingInstruction> build, IReadOnlyDictionary<Guid, LogisticsSubcontractSourceV1> sources, CancellationToken token = default)
    {
        RequireSubcontractTransaction();
        return StoreInstructionCoreAsync(envelope, requestHash, effectiveOn, build, token, null, sources);
    }

    public async Task<IReadOnlyDictionary<Guid, LogisticsSubcontractSourceV1>> ReadSubcontractSourcesAsync(Guid instructionId, CancellationToken token = default)
    {
        var companyId = RequireResolvedCompany();
        var kind = await dbContext.Instructions.AsNoTracking().Where(row => row.Id == instructionId).Select(row => (int?)row.ShipmentKind).SingleOrDefaultAsync(token);
        if (kind != (int)LoadingInstructionShipmentKind.ContractProcessingDelivery) return new Dictionary<Guid, LogisticsSubcontractSourceV1>();
        var rows = await dbContext.Lines.AsNoTracking().Where(row => row.InstructionId == instructionId).ToArrayAsync(token);
        var sources = new Dictionary<Guid, LogisticsSubcontractSourceV1>();
        foreach (var row in rows)
        {
            var source = row.SubcontractSourceJson is not null ? JsonSerializer.Deserialize<LogisticsSubcontractSourceV1>(row.SubcontractSourceJson, Json) : null;
            if (source is null || source.CompanyId != companyId || source.SalesOrderId != row.SourceOrderId
                || source.SalesOrderLineId != row.SourceOrderLineId || source.SalesLineRevision != row.SourceOrderRevision
                || source.ProductId != row.ProductId || source.LabelId == Guid.Empty || source.OutputFactId == Guid.Empty
                || source.ManufacturingOrderId == Guid.Empty || source.ManufacturingSalesLinkRevision < 1 || row.AllocationId is null)
                throw new InvalidOperationException("Fason talimatın gerçek owner kaynağı okunamıyor.");
            sources.Add(row.LineId, source);
        }
        return sources;
    }

    public async Task<LogisticsSubcontractInstructionV1?> FindSubcontractResultAsync(LogisticsWriteEnvelope envelope, string requestHash,
        string command, CancellationToken token = default)
    {
        RequireSubcontractTransaction(); RequireEnvelope(envelope); RequireHash(requestHash); RequireCommand(command);
        var attempt = await FindAttemptAsync(command, IdempotencyKeyHash(envelope.IdempotencyKey), token);
        if (attempt is null) return null;
        RequireSameContent(attempt, envelope.ActorId.Trim(), requestHash);
        if (attempt.Outcome == AttemptOutcomeRefused)
        {
            var refusal = JsonSerializer.Deserialize<LogisticsTerminalRefusal>(attempt.RefusalJson!, Json)
                ?? throw new InvalidOperationException("Kayıtlı ret okunamıyor.");
            if (refusal.Status is < 400 or > 599 || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null
                || !LogisticsPhysicalStockRefusalDetailV1.IsValid(refusal.Status, refusal.Code, refusal.Detail))
                throw new InvalidOperationException("Kayıtlı terminal ret okunamıyor.");
            throw new LogisticsInstructionServiceException(refusal.Status, refusal.Code, refusal.Errors, isReplay: true,
                originalCorrelationId: attempt.CorrelationId, detail: refusal.Detail);
        }
        var result = attempt.ResultJson is not null ? JsonSerializer.Deserialize<LogisticsSubcontractInstructionV1>(attempt.ResultJson, Json) : null;
        if (result is null || result.CompanyId != RequireResolvedCompany() || result.InstructionId != attempt.InstructionId)
            throw new InvalidOperationException("Fason komutun özgün cevabı okunamıyor.");
        return result with { IsReplay = true, OriginalCorrelationId = attempt.CorrelationId };
    }

    public async Task CompleteSubcontractResultAsync(LogisticsWriteEnvelope envelope, string requestHash, string command,
        LogisticsSubcontractInstructionV1 result, CancellationToken token = default)
    {
        RequireSubcontractTransaction(); RequireEnvelope(envelope); RequireHash(requestHash); RequireCommand(command);
        if (result.CompanyId != RequireResolvedCompany() || result.IsReplay || result.OriginalCorrelationId != envelope.CorrelationId.Trim()
            || !await dbContext.Instructions.AnyAsync(row => row.Id == result.InstructionId && row.ShipmentKind == (int)LoadingInstructionShipmentKind.ContractProcessingDelivery, token))
            throw new InvalidOperationException("Fason komut cevabı aynı gerçek talimat ve aktörün işlemine aittir.");
        var attempt = await dbContext.Attempts.SingleAsync(row => row.CommandName == command && row.IdempotencyKeyHash == IdempotencyKeyHash(envelope.IdempotencyKey), token);
        RequireSameContent(attempt, envelope.ActorId.Trim(), requestHash);
        if (attempt.Outcome != AttemptOutcomeRecorded || attempt.ResultJson is not null || attempt.InstructionId != result.InstructionId)
            throw new InvalidOperationException("Özgün cevap bir kez kaydedilir.");
        attempt.ResultJson = JsonSerializer.Serialize(result, Json);
        await SaveAsync(token);
    }

    public async Task<LogisticsStoreResult?> ShipSubcontractInstructionAsync(LogisticsWriteEnvelope envelope, string requestHash,
        Guid instructionId, long expectedVersion, IReadOnlyDictionary<Guid, Guid> shipmentIds,
        IReadOnlyList<LogisticsShipmentRiskGateFactsV1> gates, DateTimeOffset occurredAt, CancellationToken token = default)
    {
        RequireSubcontractTransaction();
        return await MutateInstructionCoreAsync(envelope, requestHash, LogisticsSubcontractDeliveryV1.ShipCommand, instructionId, (instruction, _) =>
        {
            if (!instruction.IsContractProcessingDelivery || instruction.Version != expectedVersion || instruction.Shares.Any(share => share.Kind == LoadingShareKind.Shipped))
                throw new LogisticsInstructionServiceException(409, LogisticsSubcontractDeliveryV1.SourceConflict);
            var loaded = instruction.Lines.Where(line => !line.LoadedNotShipped.IsZero).ToArray();
            if (loaded.Length == 0 || loaded.Length != instruction.Lines.Count || loaded.Length != shipmentIds.Count || loaded.Any(line => !shipmentIds.ContainsKey(line.Id)))
                throw new LogisticsInstructionServiceException(422, LogisticsSubcontractDeliveryV1.Invalid);
            foreach (var line in loaded)
                instruction.MarkShipped(instruction.Version, shipmentIds[line.Id], line.Id, line.LoadedNotShipped, occurredAt);
        }, new("logistics.subcontract-delivery.shipped.v1", "ShipSubcontract", "Müşterinin fason malı tüm talimatla sevk edildi"),
            null, null, token, gates, shipmentIds.Count);
    }

    private void RequireSubcontractTransaction()
    {
        if (dbContext.Database.CurrentTransaction is not { } transaction
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Fason yazma ve özgün cevap gerçek ortak Serializable işlem gerektirir.");
    }

    private void AppendWeighingCorrection(Guid companyId, LoadingInstruction instruction,
        VehicleWeighingCorrection correction, string actorId)
    {
        if (instruction.Weighing is not { } original || !string.Equals(correction.CorrectedBy, actorId, StringComparison.Ordinal))
            throw new InvalidOperationException("Tartı düzeltmesini kaydeden bu komutun gerçek aktörüdür.");
        dbContext.WeighingCorrections.Add(new VehicleWeighingCorrectionRow
        {
            CompanyId = companyId, InstructionId = instruction.Id, CorrectionId = correction.CorrectionId,
            Revision = correction.Revision, Direction = (int)correction.Direction,
            FirstWeighingKg = RequireFits(correction.FirstWeighing.Value), SecondWeighingKg = RequireFits(correction.SecondWeighing.Value),
            NetKg = RequireFits(correction.Net.Value), PreviousNetKg = RequireFits(correction.PreviousNet.Value),
            LoadedKgAtWeighing = RequireFits(original.LoadedAtWeighing.Value),
            DifferenceKg = correction.DifferenceKg < 0m ? -RequireFits(-correction.DifferenceKg) : RequireFits(correction.DifferenceKg),
            Reason = correction.Reason, SharesBefore = correction.SharesBefore, CorrectedBy = actorId,
            CorrectedAtUtc = correction.CorrectedAtUtc,
        });
    }

    private static void RestoreWeighingCorrection(LoadingInstruction instruction, VehicleWeighingCorrectionRow row)
    {
        if (row.CompanyId != instruction.CompanyId || row.InstructionId != instruction.Id
            || instruction.Weighing?.LoadedAtWeighing.Value != row.LoadedKgAtWeighing)
            throw new InvalidOperationException("Kayıtlı tartı düzeltmesi aynı şirket/talimat ve özgün yükleme kanıtına aittir.");
        var correction = instruction.CorrectWeighing(instruction.Version, row.CorrectionId, (WeighingDirection)row.Direction,
            Quantity.Of(row.FirstWeighingKg, QuantityUnit.Kilogram), Quantity.Of(row.SecondWeighingKg, QuantityUnit.Kilogram),
            row.Reason, row.CorrectedBy, row.CorrectedAtUtc);
        if (correction.Revision != row.Revision || correction.Net.Value != row.NetKg || correction.PreviousNet.Value != row.PreviousNetKg
            || correction.DifferenceKg != row.DifferenceKg || correction.SharesBefore != row.SharesBefore
            || correction.Reason != row.Reason || correction.CorrectedBy != row.CorrectedBy || correction.CorrectedAtUtc != row.CorrectedAtUtc)
            throw new InvalidOperationException("Kayıtlı tartı düzeltmesi gerçek önceki tartı ve pay sırasından yeniden üretilemiyor.");
    }

    private static object[] CorrectionSnapshot(LoadingInstruction instruction) =>
        [.. instruction.WeighingCorrections.Select(correction => (object)new
        {
            correction.CorrectionId, correction.Revision, direction = correction.Direction.ToString(),
            first = correction.FirstWeighing.Value, second = correction.SecondWeighing.Value,
            net = correction.Net.Value, previousNet = correction.PreviousNet.Value, difference = correction.DifferenceKg,
            correction.Reason, correction.SharesBefore, correction.CorrectedBy, correction.CorrectedAtUtc,
        })];
}
