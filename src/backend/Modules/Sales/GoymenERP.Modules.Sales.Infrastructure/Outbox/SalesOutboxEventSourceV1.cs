using System.Buffers.Binary;
using System.Globalization;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Outbox;

/// <summary>Existing append-only Sales producers, declared exactly for the Work registration contract.</summary>
public static class SalesOutboxContractV1
{
    // Existing Accounting inbox dedup/source identity; never rename after delivery.
    public const string SourceName = "sales";
    public static IReadOnlyList<WorkOutboxEventTypeV1> Publishes { get; } = Array.AsReadOnly<WorkOutboxEventTypeV1>(
    [
        new WorkOutboxEventTypeV1(SalesStore.InvoiceFinalizedEventType, 1),
        new WorkOutboxEventTypeV1(SalesStore.CustomerReturnFinalizedEventType, 1),
        new WorkOutboxEventTypeV1(GoymenERP.Modules.Sales.Application.Contracts.SalesOfficialInvoiceCancellationCommandsV1.EventType, 1),
    ]);
}

/// <summary>
/// Reads Sales-owned events, bounded by the committed SQL Server rowversion horizon. K3 review pending:
/// the fixed MIN_ACTIVE_ROWVERSION metadata query contains no input or tenant bypass. Excluding the
/// first active version prevents a later transaction from moving the cursor past an uncommitted event.
/// Registration does not enable Work dispatch or Accounting admission.
/// </summary>
public sealed class SalesOutboxEventSourceV1(SalesDbContext database, ICompanyContext company) : IWorkOutboxEventSourceV1
{
    public async Task<IReadOnlyList<WorkOutboxEventV1>> ReadAfterAsync(
        string? afterPosition, int maxCount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!company.IsResolved || company.CompanyId == Guid.Empty || maxCount < 1) return [];
        var companyId = company.CompanyId;
        var after = afterPosition is null ? 0UL : afterPosition.Length == 20
            && ulong.TryParse(afterPosition, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : throw new InvalidOperationException("Satış outbox imleci geçersiz.");
        var values = await database.Database.SqlQueryRaw<byte[]>("SELECT MIN_ACTIVE_ROWVERSION() AS [Value]")
            .ToListAsync(cancellationToken);
        var horizon = values.Count == 1 && values[0] is { Length: sizeof(ulong) } bytes
            ? BinaryPrimitives.ReadUInt64BigEndian(bytes)
            : throw new InvalidOperationException("Satış outbox commit ufku okunamadı.");
        Fence();
        var rows = await database.OutboxMessages.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Position > after && row.Position < horizon)
            .OrderBy(row => row.Position).Take(maxCount).ToListAsync(cancellationToken);
        Fence();
        return rows.Select(row => new WorkOutboxEventV1(row.Id, row.CompanyId, row.EventType, row.SchemaVersion,
            row.PayloadJson, row.OccurredAtUtc, row.CorrelationId, row.CausationId,
            row.Position.ToString("D20", CultureInfo.InvariantCulture))).ToArray();

        void Fence()
        {
            if (!company.IsResolved || company.CompanyId != companyId)
                throw new InvalidOperationException("Satış outbox şirket bağlamı değişti.");
        }
    }
}
