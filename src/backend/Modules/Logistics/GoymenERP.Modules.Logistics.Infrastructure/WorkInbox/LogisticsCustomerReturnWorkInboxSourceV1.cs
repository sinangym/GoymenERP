using GoymenERP.BuildingBlocks.Quantities;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Returns;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;

namespace GoymenERP.Modules.Logistics.Infrastructure.WorkInbox;

/// <summary>
/// WP6 (AI-D31, AI-D32, DALGA_PLANI_v2 1.4): the physical customer returns waiting for the acceptance decision, as Logistics
/// lists them for the Work inbox. A return waits while its status is Pending — the only state
/// <c>ApproveLogisticsCustomerReturnV1</c> / <c>RejectLogisticsCustomerReturnV1</c> accept — and is decided on the instruction
/// detail screen (the shell's <c>logistics.customer-returns.approve</c> / <c>.reject</c> controls) by a company-wide holder of
/// <c>logistics.customer-returns.decision</c>; approving also needs Inventory's physical allocation write, which the owner
/// measures on the decision. The inbox decides nothing.
/// <para>
/// Every pending return of the company is returned, the requester's own included: Work hides the owner's "may not decide" set,
/// which is the owner's rule word for word (<c>CustomerReturn.RequireDecision</c>): whoever prepared the physical return never
/// approves or rejects it. No one else is excluded (YOL-D23). The rule's numbers are the stored returned quantity and the
/// delivered quantity of the source delivery it comes from, in the line's own unit; nothing is recomputed. The free-text reason
/// is not copied. Returns carry no document number, so the label is the return's identity.
/// </para>
/// </summary>
internal sealed class LogisticsCustomerReturnWorkInboxSourceV1 : IWorkInboxSourceV1
{
    public const string ModuleName = "logistics";
    public const string KindName = "logistics.customer-return.v1";

    /// <summary>A physical return was requested: threshold = the source delivery's quantity, actual = the returned quantity.</summary>
    public const string ReturnRequestRule = "logistics.customer-return-request";

    private const string KilogramUnit = "kg";
    private const string PieceUnit = "adet";

    private readonly LogisticsWorkInboxRows rows;
    private readonly LogisticsWorkInboxFence fence;

    public LogisticsCustomerReturnWorkInboxSourceV1(LogisticsDbContext dbContext, LogisticsWorkInboxFence fence)
        : this(new LogisticsWorkInboxDbRows(dbContext), fence)
    {
    }

    internal LogisticsCustomerReturnWorkInboxSourceV1(LogisticsWorkInboxRows rows, LogisticsWorkInboxFence fence)
    {
        this.rows = rows;
        this.fence = fence;
    }

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => LogisticsPermissionManifestV1.CustomerReturnsDecision;

    public async Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        cancellationToken.ThrowIfCancellationRequested();
        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is { } refused)
        {
            return refused;
        }

        var companyId = context.CompanyId;
        const int pendingStatus = (int)CustomerReturnStatus.Pending;
        List<Pending> pending;
        try
        {
            var read = await rows.ListAsync(
                (from customerReturn in rows.CustomerReturns
                 where customerReturn.CompanyId == companyId && customerReturn.Status == pendingStatus
                 orderby customerReturn.RequestedAtUtc, customerReturn.CustomerReturnId
                 select new
                 {
                     customerReturn.CustomerReturnId,
                     customerReturn.LoadingInstructionId,
                     customerReturn.QuantityUnit,
                     customerReturn.Quantity,
                     customerReturn.SourceDeliveredQuantity,
                     customerReturn.RequestedBy,
                     customerReturn.RequestedAtUtc,
                 })
                .Take(maxCount + 1),
                cancellationToken);
            pending = [.. read.Select(row => new Pending(row.CustomerReturnId, row.LoadingInstructionId, row.QuantityUnit,
                row.Quantity, row.SourceDeliveredQuantity, row.RequestedBy, row.RequestedAtUtc))];
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await fence.UnavailableAsync(context, ApproverPermission, cancellationToken);
        }

        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }

        var items = new List<WorkInboxItemV1>(Math.Min(pending.Count, maxCount));
        foreach (var row in pending.Take(maxCount))
        {
            if (!TryItem(row, out var item))
            {
                // A record Logistics cannot read as its own invariants say: unavailable, never a partial list.
                return WorkInboxSourceResultV1.Unavailable();
            }

            items.Add(item);
        }

        return WorkInboxSourceResultV1.Found(companyId, items, pending.Count > maxCount);
    }

    private static bool TryItem(Pending row, out WorkInboxItemV1 item)
    {
        item = null!;
        var unit = row.QuantityUnit switch
        {
            (int)QuantityUnit.Kilogram => KilogramUnit,
            (int)QuantityUnit.Piece => PieceUnit,
            _ => null,
        };
        if (row.CustomerReturnId == Guid.Empty || row.InstructionId == Guid.Empty || unit is null
            || string.IsNullOrWhiteSpace(row.RequestedBy) || row.Quantity <= 0m || row.SourceDeliveredQuantity <= 0m)
        {
            return false;
        }

        var label = LogisticsWorkInboxFence.Id(row.CustomerReturnId);
        item = new WorkInboxItemV1(ModuleName, KindName, row.CustomerReturnId, label, row.RequestedAtUtc.ToUniversalTime(),
            LogisticsWorkInboxFence.InstructionLink(row.InstructionId), row.RequestedBy.Trim(),
            [
                new WorkApprovalRuleResultV1(ReturnRequestRule, WorkInboxRulesV1.Decimal(row.SourceDeliveredQuantity),
                    WorkInboxRulesV1.Decimal(row.Quantity), null, unit),
            ],
            [LogisticsWorkInboxFence.InstructionReference(row.InstructionId)]);
        return true;
    }

    private sealed record Pending(Guid CustomerReturnId, Guid InstructionId, int QuantityUnit, decimal Quantity,
        decimal SourceDeliveredQuantity, string RequestedBy, DateTimeOffset RequestedAtUtc);
}
