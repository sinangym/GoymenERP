using System.Buffers.Binary;
using System.Data;
using System.Globalization;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// Finance-owned committed outbox cursor. K3 review pending: fixed MIN_ACTIVE_ROWVERSION metadata query,
/// no tenant bypass, no dynamic input. A later commit cannot advance past an earlier uncommitted row.
/// </summary>
public sealed class FinanceCreditOutboxEventSourceV1(FinanceDbContext database, ICompanyContext company) : IWorkOutboxEventSourceV1
{
    public async Task<IReadOnlyList<WorkOutboxEventV1>> ReadAfterAsync(string? afterPosition, int maxCount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!company.IsResolved || company.CompanyId == Guid.Empty || maxCount < 1) return [];
        var companyId = company.CompanyId;
        var after = afterPosition is null ? 0UL : afterPosition.Length == 20
            && ulong.TryParse(afterPosition, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : throw new InvalidOperationException("Finans outbox imleci geçersiz.");
        if (System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Finance outbox reads cannot borrow an ambient transaction.");
        var borrowed = database.Database.CurrentTransaction;
        if (borrowed is not null && borrowed.GetDbTransaction().IsolationLevel != IsolationLevel.ReadCommitted)
            throw new InvalidOperationException("Finance outbox reads require a READ COMMITTED transaction.");
        await using var owned = borrowed is null
            ? await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken) : null;
        var values = await database.Database.SqlQueryRaw<byte[]>("SELECT MIN_ACTIVE_ROWVERSION() AS [Value]").ToListAsync(cancellationToken);
        var horizon = values.Count == 1 && values[0] is { Length: sizeof(ulong) } bytes
            ? BinaryPrimitives.ReadUInt64BigEndian(bytes) : throw new InvalidOperationException("Finans outbox commit ufku okunamadı.");
        Fence();
        var rows = await database.OutboxMessages.AsNoTracking().Where(row => row.CompanyId == companyId && row.Position > after && row.Position < horizon)
            .OrderBy(row => row.Position).Take(maxCount).ToListAsync(cancellationToken);
        Fence();
        var result = rows.Select(row => new WorkOutboxEventV1(row.Id, row.CompanyId, row.EventType, row.SchemaVersion, row.PayloadJson,
            row.OccurredAtUtc, row.CorrelationId, row.CausationId, row.Position.ToString("D20", CultureInfo.InvariantCulture))).ToArray();
        if (owned is not null) await owned.CommitAsync(cancellationToken);
        return result;
        void Fence() { if (!company.IsResolved || company.CompanyId != companyId) throw new InvalidOperationException("Finans outbox şirket bağlamı değişti."); }
    }
}
