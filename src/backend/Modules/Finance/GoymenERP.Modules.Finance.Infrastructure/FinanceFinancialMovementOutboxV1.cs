using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// Stages the source event in the producer's existing save/treasury transaction. A failed command,
/// audit or save rolls this row back as well. Replayed commands return before reaching this writer.
/// </summary>
internal static class FinanceFinancialMovementOutboxV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static void Expense(FinanceDbContext database, FinanceContext context, ExpensePaymentView source)
    {
        if (source.Status != ExpensePaymentStatus.Recorded || !source.PaymentConfirmed
            || string.IsNullOrWhiteSpace(source.PreparedBy) || string.IsNullOrWhiteSpace(source.ApprovedBy)
            || source.PreparedBy == source.ApprovedBy || source.ApprovedAtUtc is null
            || source.RecordedAtUtc is null || source.RecordedBy != context.ActorId)
            throw new InvalidOperationException("Only a confirmed, separately approved expense is published.");
        var id = Guid.NewGuid();
        Add(database, context, source.Id, source.CompanyId, source.Version, source.RecordedAtUtc.Value,
            FinanceFinancialMovementEventsV1.ExpensePaymentRecorded, id,
            JsonSerializer.Serialize(new FinanceExpensePaymentRecordedV1(id, source), Json));
    }

    internal static void Transfer(FinanceDbContext database, FinanceContext context, CashTransferView source)
    {
        if (source.Preparation.Kind != CashTransferKind.SameCurrency || !source.TransferConfirmed
            || source.RecordedBy != context.ActorId || source.Preparation.Rate is not null
            || source.Preparation.SourceMoney != source.Preparation.TargetMoney)
            throw new InvalidOperationException("Only a confirmed same-currency transfer is published by this contract.");
        var id = Guid.NewGuid();
        Add(database, context, source.Id, source.CompanyId, source.Version, source.RecordedAtUtc,
            FinanceFinancialMovementEventsV1.CashTransferRecorded, id,
            JsonSerializer.Serialize(new FinanceCashTransferRecordedV1(id, source), Json));
    }

    private static void Add(FinanceDbContext database, FinanceContext context, Guid sourceId, Guid companyId,
        string sourceVersion, DateTimeOffset recordedAt, string eventType, Guid eventId, string payload)
    {
        if (sourceId == Guid.Empty || companyId == Guid.Empty || companyId != context.CompanyId
            || recordedAt == default || !long.TryParse(sourceVersion, NumberStyles.None, CultureInfo.InvariantCulture,
                out var version) || version < 1)
            throw new InvalidOperationException("A Finance event must name its exact recorded company and source version.");
        database.OutboxMessages.Add(new FinanceOutboxRow
        {
            Id = eventId, CompanyId = companyId, AggregateId = sourceId, AggregateVersion = version,
            EventType = eventType, SchemaVersion = FinanceFinancialMovementEventsV1.SchemaVersion,
            PayloadJson = payload, OccurredAtUtc = recordedAt,
            CorrelationId = context.CorrelationId, CausationId = context.CausationId
        });
    }
}
