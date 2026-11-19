using System.Buffers.Binary;
using System.Data;
using System.Globalization;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>Owner-only company read of explicitly declared Inventory events. The committed rowversion horizon prevents
/// a later committed row from advancing past an earlier still-open producer transaction. Registration never starts Work.</summary>
public sealed class InventoryOutboxEventSource(InventoryDbContext db, ICompanyContext company) : IWorkOutboxEventSourceV1
{
    internal const string HorizonQuery = "SELECT MIN_ACTIVE_ROWVERSION() AS [Value]";

    public async Task<IReadOnlyList<WorkOutboxEventV1>> ReadAfterAsync(string? afterPosition, int maxCount,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!company.IsResolved || company.CompanyId == Guid.Empty || maxCount < 1) return [];
        var companyId = company.CompanyId;
        var after = ParsePosition(afterPosition);
        if (System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Inventory outbox reads cannot borrow an ambient transaction.");
        var borrowed = db.Database.CurrentTransaction;
        if (borrowed is not null && borrowed.GetDbTransaction().IsolationLevel != IsolationLevel.ReadCommitted)
            throw new InvalidOperationException("Inventory outbox reads require a READ COMMITTED transaction.");
        // The horizon and rows must share current committed visibility, never a caller's older snapshot.
        await using var owned = borrowed is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken) : null;
        // Database visibility watermark only: fixed SQL, no caller text, no module tables and no tenant bypass.
        // This new SQL surface requires explicit K3 review; no guard allowlist is changed by this implementation.
        var horizons = await db.Database.SqlQueryRaw<byte[]>(HorizonQuery).ToListAsync(cancellationToken);
        if (!SameCompany(companyId)) throw new InvalidOperationException("Inventory outbox company changed during read.");
        var horizon = horizons.Count == 1 && horizons[0] is { Length: sizeof(ulong) } bytes
            ? BinaryPrimitives.ReadUInt64BigEndian(bytes) : throw new InvalidOperationException("Inventory outbox horizon unavailable.");
        var rows = await db.OutboxMessages.AsNoTracking().Where(row => row.CompanyId == companyId && row.Position > after
            && row.Position < horizon && row.SchemaVersion == 1
            && (row.EventType == InventoryFutureAllocationReleasedOutboxV1.EventType
                || row.EventType == InventoryOutboxContractV1.ManufacturingCostDifferenceProposed
                || row.EventType == InventoryPurchaseInvoiceCostDifferenceV1.EventType))
            .OrderBy(row => row.Position).Take(maxCount).ToListAsync(cancellationToken);
        if (!SameCompany(companyId)) throw new InvalidOperationException("Inventory outbox company changed during read.");
        var result = rows.Select(row => new WorkOutboxEventV1(row.Id, row.CompanyId, row.EventType, row.SchemaVersion, row.PayloadJson,
            row.OccurredAtUtc, row.CorrelationId, row.CausationId, FormatPosition(row.Position))).ToArray();
        if (owned is not null) await owned.CommitAsync(cancellationToken);
        return result;
    }

    private bool SameCompany(Guid companyId) => company.IsResolved && company.CompanyId == companyId;
    internal static string FormatPosition(ulong position) => position.ToString("D20", CultureInfo.InvariantCulture);
    internal static ulong ParsePosition(string? position)
    {
        if (position is null) return 0UL;
        if (position.Length != 20 || position.Any(value => value is < '0' or > '9')
            || !ulong.TryParse(position, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            throw new InvalidOperationException("Inventory outbox cursor is invalid.");
        return parsed;
    }
}
