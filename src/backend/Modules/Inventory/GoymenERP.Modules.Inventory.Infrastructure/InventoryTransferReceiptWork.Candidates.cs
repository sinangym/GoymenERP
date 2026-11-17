using System.Globalization;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Transfers;
using GoymenERP.BuildingBlocks.Security;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure;

public sealed partial class InventoryTransferReceiptWork
{
    public async Task<InventoryTransferReceiptWorkCandidatesV1> SearchAsync(string purpose, string? query = null,
        string? cursor = null, int limit = 25, CancellationToken cancellationToken = default)
    {
        var id = company.IsResolved ? company.CompanyId : Guid.Empty; var actor = user.UserId ?? string.Empty;
        var permission = InventoryTransferReceiptWorkPurposesV1.Permission(purpose);
        if (permission is null || limit is < 1 or > 100 || query is not null
            && (query.Length > 64 || query != query.Trim() || query.Length == 0 || query.Any(char.IsControl)))
            throw new InventoryTransferException(400, InventoryTransferCodesV1.ValidationFailed);
        try
        {
            await Fence();
            Guid? after = null;
            if (cursor is not null)
            {
                if (cursors is not { IsAvailable: true }) throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable);
                after = cursors.Unprotect(id, actor, purpose, query, limit, cursor);
            }
            var selected = database.Transfers.AsNoTracking().Where(x => x.CompanyId == id);
            if (purpose == InventoryTransferReceiptWorkPurposesV1.Dispatch)
                selected = selected.Where(x => x.Status == (int)InventoryTransferStatus.Planned || x.Status == (int)InventoryTransferStatus.InTransit
                    || x.Status == (int)InventoryTransferStatus.PartiallyAccepted);
            else if (purpose is InventoryTransferReceiptWorkPurposesV1.Receive or InventoryTransferReceiptWorkPurposesV1.LossRequest)
            {
                selected = selected.Where(x =>
                    ((x.Status == (int)InventoryTransferStatus.InTransit || x.Status == (int)InventoryTransferStatus.PartiallyAccepted)
                    && database.TransferLines.Any(line => line.TransferId == x.Id
                        && !database.TransferAcceptances.Any(receipt => receipt.TransferId == x.Id && receipt.LineId == line.LineId)))
                    // A rejected excess may be corrected after the physical transfer is wholly accepted.
                    || purpose == InventoryTransferReceiptWorkPurposesV1.Receive
                    && database.TransferOverageDecisions.Any(decision => decision.TransferId == x.Id && decision.Decision == 2
                        && !database.TransferOverageDecisions.Any(approved => approved.TransferId == x.Id && approved.LineId == decision.LineId && approved.Decision == 1)));
            }
            else if (purpose == InventoryTransferReceiptWorkPurposesV1.OverageDecision)
            {
                selected = selected.Where(x => database.TransferReceipts.Any(receipt => receipt.TransferId == x.Id && receipt.OverageKg > 0m
                        && !database.TransferOverageDecisions.Any(decision => decision.TransferId == x.Id && decision.LineId == receipt.LineId && decision.RequestNumber == 1))
                    || database.TransferOverageRenewals.Any(renewal => renewal.TransferId == x.Id
                        && !database.TransferOverageDecisions.Any(decision => decision.TransferId == x.Id && decision.LineId == renewal.LineId
                            && decision.RequestNumber == renewal.RequestNumber)));
            }
            else
            {
                selected = selected.Where(x => database.TransferLossRequests.Any(request => request.TransferId == x.Id
                    && !database.TransferLossDecisions.Any(decision => decision.TransferId == x.Id && decision.LineId == request.LineId
                        && decision.RequestNumber == request.RequestNumber)
                    && !database.TransferLossWithdrawals.Any(withdrawal => withdrawal.TransferId == x.Id && withdrawal.LineId == request.LineId
                        && withdrawal.RequestNumber == request.RequestNumber)));
            }
            if (query is not null)
                selected = selected.Where(x => database.TransferLines.Any(line => line.TransferId == x.Id
                    && database.Labels.Any(label => label.Id == line.LabelId && label.Code.Contains(query))));
            if (after.HasValue) selected = selected.Where(x => x.Id.CompareTo(after.Value) > 0);
            var rows = await selected.OrderBy(x => x.Id).Take(limit + 1).ToListAsync(cancellationToken);
            var page = rows.Take(limit).ToArray(); var ids = page.Select(x => x.Id).ToArray();
            var labels = await (from line in database.TransferLines.AsNoTracking()
                join label in database.Labels.AsNoTracking() on new { line.CompanyId, Id = line.LabelId } equals new { label.CompanyId, label.Id }
                where ids.Contains(line.TransferId)
                orderby line.TransferId, line.Sequence
                select new { line.TransferId, label.Code }).ToListAsync(cancellationToken);
            var dispatches = await database.TransferDispatches.AsNoTracking().Where(x => ids.Contains(x.TransferId))
                .Select(x => new { x.TransferId, x.DispatchedAtUtc }).ToListAsync(cancellationToken);
            await Fence();
            if (page.Any(x => x.CompanyId != id || x.Id == Guid.Empty || x.Version <= 0 || !Enum.IsDefined((InventoryTransferStatus)x.Status)
                    || x.SourceLocationId == Guid.Empty || x.DestinationLocationId == Guid.Empty
                    || !labels.Any(label => label.TransferId == x.Id))
                || labels.Any(x => string.IsNullOrWhiteSpace(x.Code) || x.Code.Length > 64 || x.Code.Any(char.IsControl)))
                throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable);
            var more = rows.Count > limit; string? next = null;
            if (more)
            {
                if (cursors is not { IsAvailable: true }) throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable);
                next = cursors.Protect(id, actor, purpose, query, limit, page[^1].Id);
            }
            return new(id, purpose, query, limit, page.Select(x => new InventoryTransferReceiptWorkCandidateV1(
                new(x.Id, x.CompanyId, x.Version.ToString(CultureInfo.InvariantCulture), ((InventoryTransferStatus)x.Status).ToString(),
                    x.SourceLocationId, x.DestinationLocationId, x.PlannedAtUtc, dispatches.SingleOrDefault(d => d.TransferId == x.Id)?.DispatchedAtUtc),
                labels.Where(label => label.TransferId == x.Id).Select(label => label.Code).ToArray())).ToArray(), more, next);
        }
        catch (OperationCanceledException) { throw; }
        catch (InventoryTransferException) { await Fence(); throw; }
        catch { await Fence(); throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable); }

        async Task Fence()
        {
            Same(); PermissionScope grant;
            try { grant = await authority.RevalidatePermissionScopeAsync(permission, id, cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch { Same(); throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable); }
            Same();
            if (!grant.IsGranted || !grant.IsCompanyWide) throw new InventoryTransferException(403, InventoryTransferCodesV1.Forbidden);
        }
        void Same()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (id == Guid.Empty || !company.IsResolved || company.CompanyId != id || !user.IsAuthenticated
                || string.IsNullOrWhiteSpace(actor) || user.UserId != actor || !user.CanAccessCompany(id))
                throw new InventoryTransferException(403, InventoryTransferCodesV1.Forbidden);
        }
    }
}
