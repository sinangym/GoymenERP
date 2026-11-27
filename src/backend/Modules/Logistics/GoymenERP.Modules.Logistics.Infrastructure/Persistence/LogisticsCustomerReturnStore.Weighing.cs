using System.Globalization;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.Quantities;
using GoymenERP.Modules.Logistics.Domain.Returns;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

public sealed partial class LogisticsCustomerReturnStore
{
    public async Task<CustomerReturnStoreResultV1> StoreWeighingAsync(LogisticsWriteEnvelope envelope, string requestHash,
        CustomerReturn customerReturn, CancellationToken cancellationToken = default)
    {
        RequireEnvelope(envelope); RequireHash(requestHash); var company = RequireCompany(); RequireSerializable();
        const string command = LogisticsCustomerReturnCommandsV1.RecordWeighing;
        if (customerReturn.CompanyId != company || customerReturn.Status != CustomerReturnStatus.Pending || customerReturn.Version != 2
            || customerReturn.Weighing is not { } weighing || weighing.RecordedBy != envelope.ActorId.Trim()
            || envelope.PermissionCode != LogisticsPermissionManifestV1.WeighingsRecord)
            throw Refused(LogisticsStoreCodes.ConcurrentChange, "İade tartımı tamamlanmış tek ileri geçiş değil.");
        var key = LogisticsStore.IdempotencyKeyHash(envelope.IdempotencyKey);
        var replay = await FindAttemptAsync(command, key, cancellationToken);
        if (replay is not null)
        {
            RequireSameAttempt(replay, envelope.ActorId.Trim(), requestHash);
            var original = Replay(replay); return new(RequireSuccessful(original), LogisticsWriteOutcome.Replayed, original.OriginalCorrelationId);
        }
        var row = await dbContext.CustomerReturns.SingleOrDefaultAsync(item => item.CustomerReturnId == customerReturn.Id, cancellationToken)
            ?? throw Refused(LogisticsStoreCodes.ConcurrentChange, "Bekleyen iade bulunamadı.");
        var before = Restore(row);
        if (before.Status != CustomerReturnStatus.Pending || before.Version != 1 || before.Weighing is not null || !SameImmutable(before, customerReturn))
            throw Refused(LogisticsStoreCodes.ConcurrentChange, "İadenin tartım sürümü değişti.");
        await ValidateSourceAsync(customerReturn, cancellationToken);
        row.Version = customerReturn.Version; row.WeighingId = weighing.WeighingId; row.WeighingDirection = (int)weighing.Direction;
        row.FirstWeighingKg = RequireFits(weighing.FirstWeighing.Value); row.SecondWeighingKg = RequireFits(weighing.SecondWeighing.Value);
        row.NetWeighingKg = RequireFits(weighing.Net.Value); row.WeighingRecordedBy = weighing.RecordedBy; row.WeighingRecordedAtUtc = weighing.RecordedAtUtc;
        dbContext.CustomerReturnAttempts.Add(Attempt(company, command, key, requestHash, envelope, customerReturn));
        Audit(envelope, customerReturn, "logistics.customer-return.weighed.v1", "RecordWeighing", before.Version, customerReturn.Version,
            "Adetli müşteri iadesinin fiilî net tartımı kaydedildi");
        await SaveAsync(cancellationToken);
        return new(customerReturn, LogisticsWriteOutcome.Recorded, envelope.CorrelationId);
    }

    internal static CustomerReturnWeighingViewV1? ReadWeighing(CustomerReturnRow row)
    {
        if (row.WeighingId is null)
        {
            if (row.WeighingDirection is not null || row.FirstWeighingKg is not null || row.SecondWeighingKg is not null
                || row.NetWeighingKg is not null || row.WeighingRecordedBy is not null || row.WeighingRecordedAtUtc is not null)
                throw new InvalidOperationException("Eksik iade tartım kanıtı.");
            return null;
        }
        if (row.WeighingId == Guid.Empty || row.QuantityUnit != (int)QuantityUnit.Piece || row.WeighingDirection is not (1 or 2) || row.FirstWeighingKg is not > 0
            || row.SecondWeighingKg is not > 0 || row.NetWeighingKg is not > 0 || string.IsNullOrWhiteSpace(row.WeighingRecordedBy)
            || row.WeighingRecordedAtUtc is not { Offset: var offset } || offset != TimeSpan.Zero)
            throw new InvalidOperationException("Eksik iade tartım kanıtı.");
        if (WeighbridgeCalculator.Net((WeighingDirection)row.WeighingDirection.Value,
            Quantity.Positive(row.FirstWeighingKg.Value, QuantityUnit.Kilogram), Quantity.Positive(row.SecondWeighingKg.Value, QuantityUnit.Kilogram)).Value != row.NetWeighingKg.Value)
            throw new InvalidOperationException("İade tartım neti özgün hesapla uyuşmuyor.");
        return new(row.WeighingId.Value, ((WeighingDirection)row.WeighingDirection.Value).ToString(),
            Weight(row.FirstWeighingKg.Value), Weight(row.SecondWeighingKg.Value),
            Weight(row.NetWeighingKg.Value), row.WeighingRecordedBy, row.WeighingRecordedAtUtc.Value);
    }

    private static CustomerReturnWeighingViewV1? WeighingView(CustomerReturnWeighing? value) => value is null ? null : new(value.WeighingId,
        value.Direction.ToString(), Weight(value.FirstWeighing.Value), Weight(value.SecondWeighing.Value),
        Weight(value.Net.Value), value.RecordedBy, value.RecordedAtUtc);

    private static void RestoreWeighing(CustomerReturn item, CustomerReturnWeighingViewV1? value)
    {
        if (value is null) return;
        if (!Enum.TryParse<WeighingDirection>(value.Direction, out var direction) || direction.ToString() != value.Direction)
            throw new InvalidOperationException("İade tartım yönü okunamadı.");
        item.RecordWeighing(1, value.WeighingId, direction,
            Quantity.Positive(decimal.Parse(value.FirstWeighingKg, CultureInfo.InvariantCulture), QuantityUnit.Kilogram),
            Quantity.Positive(decimal.Parse(value.SecondWeighingKg, CultureInfo.InvariantCulture), QuantityUnit.Kilogram), value.RecordedBy, value.RecordedAtUtc);
        if (Weight(item.Weighing!.Net.Value) != value.NetKg)
            throw new InvalidOperationException("İade tartım neti özgün hesapla uyuşmuyor.");
    }
    private static string Weight(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
