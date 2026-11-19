using System.Buffers.Binary;
using System.Data;
using System.Globalization;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Outbox;

/// <summary>
/// Read-only company-scoped source. A committed row above an earlier uncommitted row cannot move the cursor past it:
/// only positions strictly below MIN_ACTIVE_ROWVERSION are publishable. Registering this source does not enable Work.
/// K3 review pending: the fixed horizon query contains no business data or dynamic SQL and bypasses no tenant filter.
/// </summary>
public sealed class PurchasingOutboxEventSource(PurchasingDbContext database, ICompanyContext company)
    : IWorkOutboxEventSourceV1
{
    private const int PositionLength = 20;
    private const string PositionFormat = "D20";
    private const string HorizonQuery = "SELECT MIN_ACTIVE_ROWVERSION() AS [Value]";

    public async Task<IReadOnlyList<WorkOutboxEventV1>> ReadAfterAsync(string? afterPosition, int maxCount,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!company.IsResolved || company.CompanyId == Guid.Empty || maxCount < 1) return [];
        var companyId = company.CompanyId;
        var after = ParsePosition(afterPosition);
        if (System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Purchasing outbox reads cannot borrow an ambient transaction.");
        var borrowed = database.Database.CurrentTransaction;
        if (borrowed is not null && borrowed.GetDbTransaction().IsolationLevel != IsolationLevel.ReadCommitted)
            throw new InvalidOperationException("Purchasing outbox reads require a READ COMMITTED transaction.");
        // Pooled sessions may retain a producer's isolation. Own the read boundary without changing a caller's transaction.
        await using var owned = borrowed is null
            ? await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        var horizons = await database.Database.SqlQueryRaw<byte[]>(HorizonQuery).ToListAsync(cancellationToken);
        var horizon = horizons.Count == 1 && horizons[0] is { Length: sizeof(ulong) } bytes
            ? BinaryPrimitives.ReadUInt64BigEndian(bytes)
            : throw new InvalidOperationException("Alış outbox commit ufku okunamadı.");
        RequireSameCompany(companyId);
        var rows = await database.OutboxMessages.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Position > after && row.Position < horizon)
            .OrderBy(row => row.Position).Take(maxCount).ToListAsync(cancellationToken);
        RequireSameCompany(companyId);
        var result = rows.Select(row => new WorkOutboxEventV1(row.Id, row.CompanyId, row.EventType, row.SchemaVersion,
            row.PayloadJson, row.OccurredAtUtc, row.CorrelationId, row.CausationId, FormatPosition(row.Position))).ToArray();
        if (owned is not null) await owned.CommitAsync(cancellationToken);
        return result;
    }

    internal static string FormatPosition(ulong value) => value.ToString(PositionFormat, CultureInfo.InvariantCulture);

    internal static ulong ParsePosition(string? text) => text is null ? 0UL
        : text.Length == PositionLength && ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value : throw new InvalidOperationException("Alış outbox imleci geçersiz.");

    private void RequireSameCompany(Guid companyId)
    {
        if (!company.IsResolved || company.CompanyId != companyId)
            throw new InvalidOperationException("Alış outbox şirket bağlamı okuma sırasında değişti.");
    }
}
