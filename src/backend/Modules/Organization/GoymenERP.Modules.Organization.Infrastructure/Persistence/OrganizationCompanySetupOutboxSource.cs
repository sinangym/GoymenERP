using System.Buffers.Binary;
using System.Data;
using System.Globalization;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

/// <summary>Owner-only ambient-company read, with a safe committed rowversion horizon; no source row is mutated.</summary>
public sealed class OrganizationCompanySetupOutboxSource(OrganizationDbContext database, ICompanyContext companyContext) : IWorkOutboxEventSourceV1
{
    public async Task<IReadOnlyList<WorkOutboxEventV1>> ReadAfterAsync(string? afterPosition, int maxCount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || maxCount < 1) return [];
        var companyId = companyContext.CompanyId;
        var after = 0UL;
        if (afterPosition is not null && (afterPosition.Length != 20
            || !ulong.TryParse(afterPosition, NumberStyles.None, CultureInfo.InvariantCulture, out after)))
            throw new InvalidOperationException("Company setup outbox cursor is invalid.");
        if (System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Organization outbox reads cannot borrow an ambient transaction.");
        var borrowed = database.Database.CurrentTransaction;
        if (borrowed is not null && borrowed.GetDbTransaction().IsolationLevel != IsolationLevel.ReadCommitted)
            throw new InvalidOperationException("Organization outbox reads require a READ COMMITTED transaction.");
        await using var owned = borrowed is null
            ? await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken) : null;
        // Fixed SQL, no user text or tenant bypass. A transaction's lower, still-active rowversion holds the cursor.
        var horizons = await database.Database.SqlQueryRaw<byte[]>("SELECT MIN_ACTIVE_ROWVERSION() AS [Value]").ToListAsync(cancellationToken);
        var horizon = horizons.Count == 1 && horizons[0] is { Length: sizeof(ulong) } bytes
            ? BinaryPrimitives.ReadUInt64BigEndian(bytes) : throw new InvalidOperationException("Outbox horizon unavailable.");
        Fence();
        var rows = await database.CompanySetupOutboxMessages.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Position > after && x.Position < horizon)
            .OrderBy(x => x.Position).Take(maxCount).ToListAsync(cancellationToken);
        Fence();
        var result = rows.Select(x => new WorkOutboxEventV1(x.Id, companyId, OrganizationCompanyOpenedV1.EventType, 1, x.PayloadJson,
            x.OccurredAtUtc, x.CorrelationId, x.CausationId, x.Position.ToString("D20", CultureInfo.InvariantCulture))).ToArray();
        if (owned is not null) await owned.CommitAsync(cancellationToken);
        return result;

        void Fence()
        {
            if (!companyContext.IsResolved || companyContext.CompanyId != companyId)
                throw new InvalidOperationException("Organization outbox company changed during read.");
        }
    }
}
