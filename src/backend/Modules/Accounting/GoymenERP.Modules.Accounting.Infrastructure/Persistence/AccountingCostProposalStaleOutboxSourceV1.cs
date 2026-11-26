using System.Buffers.Binary;
using System.Data;
using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

/// <summary>The immutable stale proposal version is the transactional outbox record. Same committed rowversion horizon
/// as Sales: a later committed closure cannot advance the dispatcher cursor past an earlier uncommitted closure.</summary>
public sealed class AccountingCostProposalStaleOutboxSourceV1(AccountingDbContext db, ICompanyContext company) : IWorkOutboxEventSourceV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<IReadOnlyList<WorkOutboxEventV1>> ReadAfterAsync(string? afterPosition, int maxCount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!company.IsResolved || company.CompanyId == Guid.Empty || maxCount < 1) return [];
        var companyId = company.CompanyId;
        var after = afterPosition is null ? 0UL : afterPosition.Length == 20
            && ulong.TryParse(afterPosition, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                ? parsed : throw new InvalidOperationException("Accounting stale proposal cursor is invalid.");
        if (System.Transactions.Transaction.Current is not null) throw new InvalidOperationException("Accounting outbox cannot borrow an ambient transaction.");
        var borrowed = db.Database.CurrentTransaction;
        if (borrowed is not null && borrowed.GetDbTransaction().IsolationLevel != IsolationLevel.ReadCommitted)
            throw new InvalidOperationException("Accounting outbox reads require READ COMMITTED.");
        await using var owned = borrowed is null ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken) : null;
        var values = await db.Database.SqlQueryRaw<byte[]>("SELECT MIN_ACTIVE_ROWVERSION() AS [Value]").ToListAsync(cancellationToken);
        var horizon = values.Count == 1 && values[0] is { Length: sizeof(ulong) } bytes
            ? BinaryPrimitives.ReadUInt64BigEndian(bytes) : throw new InvalidOperationException("Accounting commit horizon is unavailable.");
        Fence();
        var rows = await db.InventoryCostProposals.AsNoTracking().Where(x => x.CompanyId == companyId && x.Status == 7
            && x.OutboxPosition > after && x.OutboxPosition < horizon).OrderBy(x => x.OutboxPosition).Take(maxCount).ToArrayAsync(cancellationToken);
        Fence();
        var result = rows.Select(ToEvent).ToArray();
        if (owned is not null) await owned.CommitAsync(cancellationToken);
        Fence();
        return result;
        void Fence() { if (!company.IsResolved || company.CompanyId != companyId) throw new InvalidOperationException("Accounting outbox company changed."); }
    }
    internal static WorkOutboxEventV1 ToEvent(AccountingInventoryCostProposalRow row)
    {
        var view = JsonSerializer.Deserialize<AccountingInventoryCostProposalV1>(row.ViewJson, Json);
        var closure = view?.StaleClosure;
        if (view is null || view.Status != "Stale" || row.Status != 7 || closure is null
            || !Enum.IsDefined(view.Kind) || view.CompanyId != row.CompanyId || view.ProposalId != row.Id || view.Version != row.Version || row.OutboxPosition == 0
            || closure.EventId == Guid.Empty || closure.PreparedBy != row.PreparedBy || closure.PreparedBy == Guid.Empty
            || closure.PreparedVersion != row.Version - 1 || closure.ReasonCode != AccountingCostProposalStaleEventV1.ReasonCode
            || closure.ReasonText != AccountingCostProposalStaleEventV1.ReasonText || closure.ClosedAtUtc == default
            || closure.ClosedAtUtc.Offset != TimeSpan.Zero || string.IsNullOrWhiteSpace(closure.CorrelationId))
            throw new InvalidOperationException("Accounting stale closure evidence is invalid.");
        var payload = new AccountingCostProposalStalePayloadV1(row.CompanyId, row.Id, row.Version, closure.PreparedBy,
            closure.ReasonCode, closure.ReasonText, closure.ClosedAtUtc, view.Kind);
        return new(closure.EventId, row.CompanyId, AccountingCostProposalStaleEventV1.EventType, 1, JsonSerializer.Serialize(payload, Json),
            closure.ClosedAtUtc, closure.CorrelationId, closure.CausationId, row.OutboxPosition.ToString("D20", CultureInfo.InvariantCulture));
    }
}
