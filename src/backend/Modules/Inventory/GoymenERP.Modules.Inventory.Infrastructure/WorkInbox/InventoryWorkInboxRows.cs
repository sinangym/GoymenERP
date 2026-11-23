using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.WorkInbox;

/// <summary>
/// WP6: the Inventory tables the Work inbox sources read, untracked. The sources write their queries once over these sets;
/// <see cref="ListAsync{T}"/> runs them. The module's own DbContext answers in production (its company query filter applies on
/// top of every source's explicit company filter); a unit test answers with in-memory rows.
/// </summary>
internal abstract class InventoryWorkInboxRows
{
    public abstract IQueryable<InventoryTransferRow> Transfers { get; }

    public abstract IQueryable<InventoryTransferLineRow> TransferLines { get; }

    public abstract IQueryable<InventoryTransferDispatchLineRow> TransferDispatchLines { get; }

    public abstract IQueryable<InventoryTransferReceiptRow> TransferReceipts { get; }

    public abstract IQueryable<InventoryTransferOverageDecisionRow> TransferOverageDecisions { get; }

    public abstract IQueryable<InventoryTransferOverageRenewalRow> TransferOverageRenewals { get; }

    public abstract IQueryable<InventoryTransferLossRequestRow> TransferLossRequests { get; }

    public abstract IQueryable<InventoryTransferLossDecisionRow> TransferLossDecisions { get; }

    public abstract IQueryable<InventoryTransferLossWithdrawalRow> TransferLossWithdrawals { get; }

    public abstract IQueryable<LabelRow> Labels { get; }

    /// <summary>WP5 (YOL-D41): the opening-cost submissions and their one decision each.</summary>
    public abstract IQueryable<OpeningCostRow> OpeningCosts { get; }

    public abstract IQueryable<OpeningCostDecisionRow> OpeningCostDecisions { get; }

    public abstract Task<List<T>> ListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken);
}

/// <summary>The production rows: Inventory's own DbContext, no tracking.</summary>
internal sealed class InventoryWorkInboxDbRows(InventoryDbContext dbContext) : InventoryWorkInboxRows
{
    public override IQueryable<InventoryTransferRow> Transfers => dbContext.Transfers.AsNoTracking();

    public override IQueryable<InventoryTransferLineRow> TransferLines => dbContext.TransferLines.AsNoTracking();

    public override IQueryable<InventoryTransferDispatchLineRow> TransferDispatchLines =>
        dbContext.TransferDispatchLines.AsNoTracking();

    public override IQueryable<InventoryTransferReceiptRow> TransferReceipts => dbContext.TransferReceipts.AsNoTracking();

    public override IQueryable<InventoryTransferOverageDecisionRow> TransferOverageDecisions =>
        dbContext.TransferOverageDecisions.AsNoTracking();

    public override IQueryable<InventoryTransferOverageRenewalRow> TransferOverageRenewals =>
        dbContext.TransferOverageRenewals.AsNoTracking();

    public override IQueryable<InventoryTransferLossRequestRow> TransferLossRequests =>
        dbContext.TransferLossRequests.AsNoTracking();

    public override IQueryable<InventoryTransferLossDecisionRow> TransferLossDecisions =>
        dbContext.TransferLossDecisions.AsNoTracking();

    public override IQueryable<InventoryTransferLossWithdrawalRow> TransferLossWithdrawals =>
        dbContext.TransferLossWithdrawals.AsNoTracking();

    public override IQueryable<LabelRow> Labels => dbContext.Labels.AsNoTracking();

    public override IQueryable<OpeningCostRow> OpeningCosts => dbContext.OpeningCosts.AsNoTracking();

    public override IQueryable<OpeningCostDecisionRow> OpeningCostDecisions => dbContext.OpeningCostDecisions.AsNoTracking();

    public override Task<List<T>> ListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken) =>
        query.ToListAsync(cancellationToken);
}
