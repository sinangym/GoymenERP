using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingInventoryCostStoreV1
{
    // Called only inside Atomic, at the existing positive stale comparison. Unavailable/missing owner data is not stale.
    private AccountingInventoryCostProposalV1 CloseStale(AccountingInventoryCostProposalV1 prepared, AccountingCommandMetadata metadata)
    {
        if (prepared.Status != "Prepared" || prepared.Preparation is null || prepared.StaleClosure is not null)
            throw Refuse("inventory_cost_state_invalid");
        var closed = prepared with
        {
            Version = checked(prepared.Version + 1), Status = "Stale",
            StaleClosure = new(Guid.NewGuid(), prepared.Version, prepared.Preparation.PreparedBy, clock.GetUtcNow(),
                AccountingCostProposalStaleEventV1.ReasonCode, AccountingCostProposalStaleEventV1.ReasonText,
                metadata.CorrelationId, metadata.CausationId),
        };
        Add(closed); // The immutable version is also the outbox record; no second event or money record is created.
        Audit(closed, metadata, "stale-closed", prepared);
        return closed;
    }
    private async Task RequireUnrenewedStale(AccountingInventoryCostProposalV1 closed, CancellationToken token)
    {
        // Same serializable owner transaction and existing event/component/version unique key arbitrate competing renewals.
        // A fresh proposal has a new identity, while the economic source and exact component are never recreated.
        var latest = await db.InventoryCostProposals.AsNoTracking()
            .Where(x => x.EventId == closed.EventId && x.ComponentKey == closed.Component.ComponentKey)
            .OrderByDescending(x => x.Version).FirstAsync(token);
        RequireCompany(closed.CompanyId);
        if (latest.Id != closed.ProposalId || latest.Version != closed.Version)
            throw new AccountingVersionConflictException(closed.Version, latest.Version);
    }

}
