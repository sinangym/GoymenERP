using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>Reads Purchasing's actual invoice/receipt owners only. Candidate quantities are advisory, never a reservation.</summary>
public sealed class PurchaseIncomingInvoiceWorkspace(PurchasingDbContext context, ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority, IPurchaseIncomingInvoiceWorkflowV1 workflow) : IPurchaseIncomingInvoiceWorkspaceV1
{
    private const int PageSize = 25;
    public async Task<PurchaseIncomingInvoicePageV1> ListAsync(string? officialNumber, int pageIndex, CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(cancellationToken);
        ValidatePage(pageIndex);
        var number = officialNumber?.Trim();
        if (number is { Length: > 128 } || number?.Any(char.IsControl) == true) throw Invalid();
        var query = context.IncomingInvoices.AsNoTracking();
        if (!string.IsNullOrEmpty(number)) query = query.Where(row => row.OfficialNumber.Contains(number));
        var ids = await GuardReadAsync(scope, () => query.OrderBy(row => row.OfficialNumber).ThenBy(row => row.InvoiceId)
            .Skip(pageIndex * PageSize).Take(PageSize + 1).Select(row => row.InvoiceId).ToArrayAsync(cancellationToken), cancellationToken);
        await FenceAsync(scope, cancellationToken);
        var items = new List<PurchaseIncomingInvoiceViewV1>();
        foreach (var id in ids.Take(PageSize))
        {
            var snapshot = await workflow.ReadAsync(id, cancellationToken);
            await FenceAsync(scope, cancellationToken);
            if (snapshot.CompanyId != scope.CompanyId || snapshot.Id != id) throw Unavailable();
            items.Add(PurchaseIncomingInvoiceWireV1.View(snapshot));
        }
        return new(pageIndex, ids.Length > PageSize, items);
    }

    public async Task<PurchaseInvoiceReceiptCandidatePageV1> CandidatesAsync(Guid invoiceId, Guid invoiceLineId, int pageIndex,
        CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(cancellationToken);
        ValidatePage(pageIndex);
        var snapshot = await workflow.ReadAsync(invoiceId, cancellationToken);
        await FenceAsync(scope, cancellationToken);
        if (snapshot.CompanyId != scope.CompanyId || snapshot.Id != invoiceId) throw Unavailable();
        var line = snapshot.Lines.SingleOrDefault(item => item.Id == invoiceLineId)
            ?? throw new PurchasingServiceException(404, "not_found");
        if (snapshot.Status != PurchaseIncomingInvoiceStatus.Recorded)
            return new(invoiceId, invoiceLineId, snapshot.Version.ToString(CultureInfo.InvariantCulture), pageIndex, false, []);
        var query = from accepted in context.Acceptances.AsNoTracking()
                    join order in context.Orders.AsNoTracking() on accepted.OrderId equals order.Id
                    join sourceLine in context.OrderLines.AsNoTracking()
                        on new { accepted.OrderId, accepted.LineId } equals new { sourceLine.OrderId, sourceLine.LineId }
                    where order.SupplierPartyId == snapshot.SupplierPartyId && order.CompanyAccountId == snapshot.CompanyAccountId
                        && order.CurrencyId == snapshot.CurrencyId && sourceLine.ProductId == line.ProductId && sourceLine.UnitId == line.UnitId
                    let undone = context.ReceiptAdjustments.Where(row => row.AcceptanceId == accepted.AcceptanceId)
                        .Sum(row => (decimal?)row.UndoQuantity) ?? 0m
                    let billed = context.InvoiceReceiptBindings.Where(row => row.AcceptanceId == accepted.AcceptanceId)
                        .Sum(row => (decimal?)row.Quantity) ?? 0m
                    let available = accepted.Quantity - undone - billed
                    where available > 0m
                    orderby accepted.RecordedAtUtc, accepted.AcceptanceId
                    select new { accepted.CompanyId, accepted.OrderId, order.DocumentNumber, sourceLine.LineId, sourceLine.LineNo,
                        accepted.AcceptanceId, accepted.RecordedAtUtc, sourceLine.ProductId, sourceLine.UnitId, Available = available };
        var rows = await GuardReadAsync(scope, () => query.Skip(pageIndex * PageSize).Take(PageSize + 1).ToArrayAsync(cancellationToken), cancellationToken);
        await FenceAsync(scope, cancellationToken);
        if (rows.Any(row => row.CompanyId != scope.CompanyId || row.Available > PurchaseIncomingInvoice.MaximumStoredQuantity
            || row.OrderId == Guid.Empty || row.LineId == Guid.Empty || row.AcceptanceId == Guid.Empty || row.LineNo < 1)) throw Unavailable();
        return new(invoiceId, invoiceLineId, snapshot.Version.ToString(CultureInfo.InvariantCulture), pageIndex, rows.Length > PageSize,
            rows.Take(PageSize).Select(row => new PurchaseInvoiceReceiptCandidateV1(row.OrderId, row.DocumentNumber,
                row.LineId, row.LineNo, row.AcceptanceId, row.RecordedAtUtc, row.ProductId, row.UnitId,
                PurchaseAcceptanceParticipantContractV1.FormatQuantity(row.Available))).ToArray());
    }

    private async Task<(Guid CompanyId, string Actor)> RequireAsync(CancellationToken token)
    {
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId)) throw new PurchasingServiceException(401, "unauthenticated");
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.CanAccessCompany(company.CompanyId)) throw Forbidden();
        var companyId = company.CompanyId; var actor = user.UserId;
        foreach (var permission in new[] { PurchaseIncomingInvoicePermissionNamesV1.Read, PurchasingPermissionManifestV1.OrdersPriceRead })
        {
            var measured = await authority.RevalidatePermissionScopeAsync(permission, companyId, token);
            if (!measured.IsGranted || !measured.IsCompanyWide || !company.IsResolved || company.CompanyId != companyId
                || !user.IsAuthenticated || user.UserId != actor || !user.CanAccessCompany(companyId)) throw Forbidden();
        }
        return (companyId, actor);
    }
    private async Task FenceAsync((Guid CompanyId, string Actor) scope, CancellationToken token)
    { if (await RequireAsync(token) != scope) throw Forbidden(); }
    private async Task<T> GuardReadAsync<T>((Guid CompanyId, string Actor) scope, Func<Task<T>> read, CancellationToken token)
    {
        try { return await read(); }
        catch (Exception exception) when (exception is not OperationCanceledException and not PurchasingServiceException)
        { await FenceAsync(scope, token); throw Unavailable(); }
    }
    private static void ValidatePage(int pageIndex) { if (pageIndex < 0 || pageIndex > int.MaxValue / PageSize) throw Invalid(); }
    private static PurchasingServiceException Invalid() => new(400, "validation_failed");
    private static PurchasingServiceException Forbidden() => new(403, "forbidden");
    private static PurchasingServiceException Unavailable() => new(503, "service_unavailable");
}
