using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure.WorkInbox;

/// <summary>
/// WP6 (AI-D31, AI-D32, DALGA_PLANI_v2 1.4; KLN-D24, KLN-D107, KLN-D119): the transfer receipt excesses waiting for a decision,
/// as Inventory lists them for the Work inbox. Two requests wait, both decided through the native owner command from the paired Work screen by a
/// company-wide holder of <c>inventory.stock-transfer.overage-approve</c> (<c>DecideInventoryTransferOverageV1</c>; the inbox
/// decides nothing):
/// <list type="bullet">
/// <item>request 1 — a receipt line that declared an excess (received kg above dispatched kg) with no decision yet; it waits
/// since the receipt was recorded;</item>
/// <item>a corrected re-request (number 2, 3, …) after a rejection, with no decision yet; it waits since it was recorded.</item>
/// </list>
/// This is exactly the set the owner's own candidate search lists for the <c>overage-decision</c> purpose. Every pending line of
/// the company is returned, the caller's own included: Work hides the owner's "may not decide" set. That set is the owner's
/// rule word for word (<c>DecideOverageCoreAsync</c>): the receiver who recorded the receipt never decides its excess and, for a
/// re-request, neither does the re-requester — so <c>PreparedBy</c> is the requester of the waiting request and
/// <c>ExcludedDeciders</c> carries the receiver when they differ. No one else is excluded (YOL-D23).
/// <para>
/// The rule numbers are the stored receipt and re-request kilograms (and pieces of a piece-tracked label), never recomputed:
/// dispatched / received / excess for request 1, the first declared excess as the cap and the re-requested excess for a
/// re-request. The item carries the destination location as its scope (D11); the owner's decision accepts only a company-wide
/// grant, so <see cref="IWorkInboxSourceV1.AcceptsScopedApprovers"/> stays false and a scoped holder is not asked.
/// </para>
/// </summary>
internal sealed partial class InventoryTransferOverageWorkInboxSourceV1 : IWorkApprovalDecisionSourceV1
{
    public const string ModuleName = "inventory";
    public const string KindName = "inventory.transfer-overage.v1";

    /// <summary>The receipt declared an excess: threshold = dispatched kg, actual = received kg, deviation = excess kg.</summary>
    public const string ReceivedExcessRule = "inventory.transfer-overage-received";

    /// <summary>A corrected re-request after a rejection: threshold = the first declared excess (its cap), actual = the re-requested kg.</summary>
    public const string RenewalRule = "inventory.transfer-overage-renewal";

    /// <summary>The pieces of a piece-tracked label: threshold = dispatched, actual = received, deviation = excess pieces.</summary>
    public const string ExcessPiecesRule = "inventory.transfer-overage-pieces";

    internal const string DecisionScreenPath = "/onaylarim/inventory.transfer-overage.v1/:approvalId";
    private static string DecisionLink(Guid itemId) => "/onaylarim/inventory.transfer-overage.v1/" + itemId.ToString("D");

    private const int FirstRequestNumber = 1;
    private const string KilogramUnit = "kg";
    private const string PieceUnit = "adet";

    private readonly InventoryWorkInboxRows rows;
    private readonly InventoryWorkInboxFence fence;

    public InventoryTransferOverageWorkInboxSourceV1(Persistence.InventoryDbContext dbContext, InventoryWorkInboxFence fence)
        : this(new InventoryWorkInboxDbRows(dbContext), fence)
    {
    }

    internal InventoryTransferOverageWorkInboxSourceV1(InventoryWorkInboxRows rows, InventoryWorkInboxFence fence)
    {
        this.rows = rows;
        this.fence = fence;
    }

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => InventoryPermissionManifestV1.StockTransferOverageApprove;

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

        List<Pending> candidates;
        try
        {
            candidates = [.. await ReadFirstRequestsAsync(context.CompanyId, maxCount + 1, cancellationToken),
                .. await ReadRenewalsAsync(context.CompanyId, maxCount + 1, cancellationToken)];
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await fence.UnavailableAsync(context, ApproverPermission, cancellationToken);
        }

        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }

        // Each read is the oldest maxCount + 1 of its request kind, so the oldest maxCount of both are among them.
        var ordered = candidates
            .OrderBy(candidate => candidate.WaitingSinceUtc)
            .ThenBy(candidate => candidate.TransferId)
            .ThenBy(candidate => candidate.LineId)
            .ToList();
        if (ordered.Select(candidate => (candidate.TransferId, candidate.LineId)).Distinct().Count() != ordered.Count)
        {
            // One line waits for one request at a time (a re-request follows a rejection): two answers break the owner's rule.
            return WorkInboxSourceResultV1.Unavailable();
        }

        var items = new List<WorkInboxItemV1>(Math.Min(ordered.Count, maxCount));
        foreach (var candidate in ordered.Take(maxCount))
        {
            if (!TryItem(candidate, out var item))
            {
                // A record Inventory cannot read as its own invariants say: unavailable, never a partial list.
                return WorkInboxSourceResultV1.Unavailable();
            }

            items.Add(item);
        }

        return WorkInboxSourceResultV1.Found(context.CompanyId, items, ordered.Count > maxCount);
    }

    private async Task<IReadOnlyList<Pending>> ReadFirstRequestsAsync(Guid companyId, int take, CancellationToken cancellationToken)
    {
        var read = await rows.ListAsync(
            (from receipt in rows.TransferReceipts
             join transfer in rows.Transfers
                 on new { receipt.CompanyId, Id = receipt.TransferId } equals new { transfer.CompanyId, transfer.Id }
             join line in rows.TransferLines
                 on new { receipt.CompanyId, receipt.TransferId, receipt.LineId }
                 equals new { line.CompanyId, line.TransferId, line.LineId }
             join label in rows.Labels on new { line.CompanyId, Id = line.LabelId } equals new { label.CompanyId, label.Id }
             where receipt.CompanyId == companyId && transfer.CompanyId == companyId && receipt.OverageKg > 0m
                 && !rows.TransferOverageDecisions.Any(decision => decision.CompanyId == companyId
                     && decision.TransferId == receipt.TransferId && decision.LineId == receipt.LineId
                     && decision.RequestNumber == FirstRequestNumber)
             orderby receipt.RecordedAtUtc, receipt.TransferId, receipt.LineId
             select new
             {
                 receipt.TransferId,
                 receipt.LineId,
                 transfer.DestinationLocationId,
                 line.LabelId,
                 LabelCode = label.Code,
                 receipt.DispatchedKg,
                 receipt.ReceivedKg,
                 receipt.OverageKg,
                 receipt.DispatchedPieceCount,
                 receipt.ReceivedPieceCount,
                 receipt.OveragePieceCount,
                 receipt.RecordedBy,
                 receipt.RecordedAtUtc,
             })
            .Take(take),
            cancellationToken);
        return [.. read.Select(row => new Pending(row.TransferId, row.LineId, row.DestinationLocationId, row.LabelId,
            row.LabelCode, FirstRequestNumber, row.RecordedAtUtc, row.RecordedBy, row.RecordedBy, row.DispatchedKg,
            row.ReceivedKg, row.OverageKg, null, row.DispatchedPieceCount, row.ReceivedPieceCount, row.OveragePieceCount))];
    }

    private async Task<IReadOnlyList<Pending>> ReadRenewalsAsync(Guid companyId, int take, CancellationToken cancellationToken)
    {
        var read = await rows.ListAsync(
            (from renewal in rows.TransferOverageRenewals
             join receipt in rows.TransferReceipts
                 on new { renewal.CompanyId, renewal.TransferId, renewal.LineId }
                 equals new { receipt.CompanyId, receipt.TransferId, receipt.LineId }
             join transfer in rows.Transfers
                 on new { renewal.CompanyId, Id = renewal.TransferId } equals new { transfer.CompanyId, transfer.Id }
             join line in rows.TransferLines
                 on new { renewal.CompanyId, renewal.TransferId, renewal.LineId }
                 equals new { line.CompanyId, line.TransferId, line.LineId }
             join label in rows.Labels on new { line.CompanyId, Id = line.LabelId } equals new { label.CompanyId, label.Id }
             where renewal.CompanyId == companyId && transfer.CompanyId == companyId
                 && !rows.TransferOverageDecisions.Any(decision => decision.CompanyId == companyId
                     && decision.TransferId == renewal.TransferId && decision.LineId == renewal.LineId
                     && decision.RequestNumber == renewal.RequestNumber)
             orderby renewal.RecordedAtUtc, renewal.TransferId, renewal.LineId
             select new
             {
                 renewal.TransferId,
                 renewal.LineId,
                 transfer.DestinationLocationId,
                 line.LabelId,
                 LabelCode = label.Code,
                 renewal.RequestNumber,
                 FirstOverageKg = receipt.OverageKg,
                 RenewalOverageKg = renewal.OverageKg,
                 renewal.OveragePieceCount,
                 ReceivedBy = receipt.RecordedBy,
                 renewal.RecordedBy,
                 renewal.RecordedAtUtc,
             })
            .Take(take),
            cancellationToken);
        return [.. read.Select(row => new Pending(row.TransferId, row.LineId, row.DestinationLocationId, row.LabelId,
            row.LabelCode, row.RequestNumber, row.RecordedAtUtc, row.RecordedBy, row.ReceivedBy, null, null,
            row.FirstOverageKg, row.RenewalOverageKg, null, null, row.OveragePieceCount))];
    }

    private static bool TryItem(Pending row, out WorkInboxItemV1 item)
    {
        item = null!;
        if (row.TransferId == Guid.Empty || row.LineId == Guid.Empty || row.LabelId == Guid.Empty
            || row.DestinationLocationId == Guid.Empty || row.RequestNumber < FirstRequestNumber
            || string.IsNullOrWhiteSpace(row.RequestedBy) || string.IsNullOrWhiteSpace(row.ReceivedBy)
            || string.IsNullOrWhiteSpace(row.LabelCode) || row.LabelCode.Length > WorkInboxRulesV1.MaxDocumentLabelLength
            || row.LabelCode.Any(char.IsControl) || row.FirstOverageKg <= 0m
            || row.RequestNumber == FirstRequestNumber && (row.DispatchedKg is not { } dispatched || dispatched < 0m
                || row.ReceivedKg is not { } received || received < 0m)
            || row.RequestNumber > FirstRequestNumber && (row.RenewalOverageKg is not { } renewed || renewed <= 0m
                || renewed > row.FirstOverageKg))
        {
            return false;
        }

        var rules = new List<WorkApprovalRuleResultV1>(2);
        if (row.RequestNumber == FirstRequestNumber)
        {
            rules.Add(new(ReceivedExcessRule, WorkInboxRulesV1.Decimal(row.DispatchedKg!.Value),
                WorkInboxRulesV1.Decimal(row.ReceivedKg!.Value), WorkInboxRulesV1.Decimal(row.FirstOverageKg), KilogramUnit));
            if (row.OveragePieceCount is { } excessPieces)
            {
                if (excessPieces <= 0 || row.DispatchedPieceCount is not { } dispatchedPieces
                    || row.ReceivedPieceCount is not { } receivedPieces)
                {
                    return false;
                }

                rules.Add(new(ExcessPiecesRule, WorkInboxRulesV1.Decimal(dispatchedPieces),
                    WorkInboxRulesV1.Decimal(receivedPieces), WorkInboxRulesV1.Decimal(excessPieces), PieceUnit));
            }
        }
        else
        {
            rules.Add(new(RenewalRule, WorkInboxRulesV1.Decimal(row.FirstOverageKg),
                WorkInboxRulesV1.Decimal(row.RenewalOverageKg!.Value), null, KilogramUnit));
            if (row.OveragePieceCount is { } renewedPieces)
            {
                if (renewedPieces <= 0)
                {
                    return false;
                }

                rules.Add(new(ExcessPiecesRule, null, null, WorkInboxRulesV1.Decimal(renewedPieces), PieceUnit));
            }
        }

        var receiver = row.ReceivedBy.Trim();
        var requester = row.RequestedBy.Trim();
        IReadOnlyList<string>? excluded = WorkInboxRulesV1.SameUser(receiver, requester) ? null : [receiver];
        item = new WorkInboxItemV1(ModuleName, KindName,
            InventoryWorkInboxFence.TransferLineItemId(KindName, row.TransferId, row.LineId), row.LabelCode,
            row.WaitingSinceUtc.ToUniversalTime(), DecisionLink(InventoryWorkInboxFence.TransferLineItemId(KindName, row.TransferId, row.LineId)), requester, rules,
            [
                new WorkApprovalReferenceV1("inventory-label", row.LabelCode, row.LabelId,
                    InventoryWorkInboxFence.LabelLink(row.LabelId)),
                new WorkApprovalReferenceV1("inventory-transfer", InventoryWorkInboxFence.Id(row.TransferId), row.TransferId,
                    InventoryWorkInboxFence.TransferOverageApprovalPath),
            ],
            excluded,
            new WorkInboxItemScopeV1(null, row.DestinationLocationId, null));
        return true;
    }

    private sealed record Pending(
        Guid TransferId,
        Guid LineId,
        Guid DestinationLocationId,
        Guid LabelId,
        string LabelCode,
        int RequestNumber,
        DateTimeOffset WaitingSinceUtc,
        string RequestedBy,
        string ReceivedBy,
        decimal? DispatchedKg,
        decimal? ReceivedKg,
        decimal FirstOverageKg,
        decimal? RenewalOverageKg,
        int? DispatchedPieceCount,
        int? ReceivedPieceCount,
        int? OveragePieceCount);
}
