using System.Buffers.Binary;
using System.Data;
using System.Globalization;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Logistics.Infrastructure.Outbox;

/// <summary>
/// STK-S27 (LOJ-D19, registration contract v1): the read-only Work outbox source over <c>logistics.OutboxMessages</c>.
/// It reads only the ambient company (the dispatcher sets it per scope; an unresolved context reads nothing), never writes,
/// and orders by the row's SQL Server rowversion. Only rows below <c>MIN_ACTIVE_ROWVERSION()</c> are returned: a row whose
/// transaction is still open holds a lower rowversion than rows committed after it, so the cursor never passes it.
/// The position is the rowversion as a fixed-width decimal text (ordinal order equals numeric order).
/// </summary>
public sealed class LogisticsDefiniteShipmentOutboxEventSource(LogisticsDbContext dbContext, ICompanyContext companyContext)
    : IWorkOutboxEventSourceV1
{
    // ulong.MaxValue has 20 decimal digits, so every position is exactly this wide.
    private const int PositionLength = 20;
    private const string PositionFormat = "D20";
    private const string HorizonQuery = "SELECT MIN_ACTIVE_ROWVERSION() AS [Value]";

    public async Task<IReadOnlyList<WorkOutboxEventV1>> ReadAfterAsync(string? afterPosition, int maxCount,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || maxCount < 1)
        {
            return [];
        }

        var companyId = companyContext.CompanyId;
        var after = ParsePosition(afterPosition);
        if (System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Logistics outbox reads cannot borrow an ambient transaction.");
        var borrowed = dbContext.Database.CurrentTransaction;
        if (borrowed is not null && borrowed.GetDbTransaction().IsolationLevel != IsolationLevel.ReadCommitted)
            throw new InvalidOperationException("Logistics outbox reads require a READ COMMITTED transaction.");
        await using var owned = borrowed is null
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken) : null;
        var horizons = await dbContext.Database.SqlQueryRaw<byte[]>(HorizonQuery).ToListAsync(cancellationToken);
        var horizon = horizons.Count == 1 && horizons[0] is { Length: sizeof(ulong) } bytes
            ? BinaryPrimitives.ReadUInt64BigEndian(bytes)
            : throw new InvalidOperationException("Logistics outbox horizon could not be read.");
        Fence();
        var rows = await dbContext.OutboxMessages.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Position > after && row.Position < horizon)
            .OrderBy(row => row.Position)
            .Take(maxCount)
            .ToListAsync(cancellationToken);
        Fence();
        var result = rows.Select(row => new WorkOutboxEventV1(row.EventId, row.CompanyId, row.EventType, row.SchemaVersion, row.PayloadJson,
                row.OccurredAtUtc, row.CorrelationId, row.CausationId, FormatPosition(row.Position)))
            .ToArray();
        if (owned is not null) await owned.CommitAsync(cancellationToken);
        return result;

        void Fence()
        {
            if (!companyContext.IsResolved || companyContext.CompanyId != companyId)
                throw new InvalidOperationException("Logistics outbox company changed during read.");
        }
    }

    /// <summary>The cursor text this source writes; exposed for tests.</summary>
    internal static string FormatPosition(ulong position) => position.ToString(PositionFormat, CultureInfo.InvariantCulture);

    private static ulong ParsePosition(string? position)
    {
        if (position is null)
        {
            return 0UL;
        }

        return position.Length == PositionLength
            && ulong.TryParse(position, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidOperationException("Logistics outbox cursor is not a position this source wrote.");
    }
}
