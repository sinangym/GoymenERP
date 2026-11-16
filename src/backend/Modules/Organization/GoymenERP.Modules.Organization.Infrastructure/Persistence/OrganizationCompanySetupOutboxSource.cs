using System.Buffers.Binary;
using System.Globalization;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

/// <summary>Owner-only ambient-company read, with a safe committed rowversion horizon; no source row is mutated.</summary>
public sealed class OrganizationCompanySetupOutboxSource(OrganizationDbContext database, ICompanyContext companyContext) : IWorkOutboxEventSourceV1
{
    public async Task<IReadOnlyList<WorkOutboxEventV1>> ReadAfterAsync(string? afterPosition, int maxCount, CancellationToken cancellationToken)
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || maxCount < 1) return [];
        var after = 0UL;
        if (afterPosition is not null && (afterPosition.Length != 20
            || !ulong.TryParse(afterPosition, NumberStyles.None, CultureInfo.InvariantCulture, out after)))
            throw new InvalidOperationException("Company setup outbox cursor is invalid.");
        // Fixed SQL, no user text or tenant bypass. A transaction's lower, still-active rowversion holds the cursor.
        var horizons = await database.Database.SqlQueryRaw<byte[]>("SELECT MIN_ACTIVE_ROWVERSION() AS [Value]").ToListAsync(cancellationToken);
        var horizon = horizons.Count == 1 && horizons[0] is { Length: sizeof(ulong) } bytes
            ? BinaryPrimitives.ReadUInt64BigEndian(bytes) : throw new InvalidOperationException("Outbox horizon unavailable.");
        var companyId = companyContext.CompanyId;
        var rows = await database.CompanySetupOutboxMessages.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Position > after && x.Position < horizon)
            .OrderBy(x => x.Position).Take(maxCount).ToListAsync(cancellationToken);
        return rows.Select(x => new WorkOutboxEventV1(x.Id, companyId, OrganizationCompanyOpenedV1.EventType, 1, x.PayloadJson,
            x.OccurredAtUtc, x.CorrelationId, x.CausationId, x.Position.ToString("D20", CultureInfo.InvariantCulture))).ToArray();
    }
}
