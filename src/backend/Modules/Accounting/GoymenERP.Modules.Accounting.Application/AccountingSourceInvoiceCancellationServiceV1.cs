using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Accounting.Application.Contracts;

namespace GoymenERP.Modules.Accounting.Application;

public sealed class AccountingSourceInvoiceCancellationServiceV1(IAccountingSourceInvoiceCancellationStoreV1 store,
    IAccountingSourceInvoiceCancellationAuthorityV1 authority, ICompanyContext company)
    : IAccountingSourceInvoiceCancellationPortV1
{
    public async Task<AccountingSourceInvoiceCancellationAnswerV1> CancelAsync(AccountingSourceInvoiceCancellationV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Valid(request)) return new("Invalid");
        if (!Current(request.CompanyId)) return new("Denied");
        var answer = await authority.CheckAsync(request.CompanyId, request.ActorId, request.DocumentKind,
            request.SourceDocumentId, cancellationToken);
        if (!Current(request.CompanyId)) return new("Denied");
        if (answer.Decision != AccountingSourceDocumentAuthorityDecisionV1.Allowed || string.IsNullOrWhiteSpace(answer.PermissionCode))
            return new(answer.Decision == AccountingSourceDocumentAuthorityDecisionV1.Denied ? "Denied" : "Unavailable");
        var metadata = new AccountingCommandMetadata(request.ActorId, answer.PermissionCode,
            [.. answer.RoleCodes.Order(StringComparer.Ordinal)], request.CorrelationId, request.CausationId,
            request.CancellationId.ToString("D"), SourceChannels.System);
        async Task Fence(CancellationToken token)
        {
            if (!Current(request.CompanyId)) throw new AccountingStoreException(AccountingStoreCodes.CompanyContextMismatch, "Şirket değişti.");
            var fresh = await authority.CheckAsync(request.CompanyId, request.ActorId, request.DocumentKind, request.SourceDocumentId, token);
            if (!Current(request.CompanyId) || fresh.Decision != AccountingSourceDocumentAuthorityDecisionV1.Allowed
                || fresh.PermissionCode != answer.PermissionCode)
                throw new AccountingStoreException("source_invoice_cancellation_authority_lost", "Kaynak iptal yetkisi değişti.");
        }
        try { return await store.CancelAsync(request, metadata, Fence, cancellationToken); }
        catch (AccountingStoreException exception) when (exception.Code is "source_invoice_cancellation_conflict") { return new("Conflict"); }
        catch (AccountingStoreException exception) when (exception.Code == AccountingStoreCodes.CompanyContextMismatch) { return new("Denied"); }
        catch (AccountingStoreException) { return new("Unavailable"); }
    }

    private bool Current(Guid id) => company.IsResolved && id != Guid.Empty && company.CompanyId == id;
    public static bool Valid(AccountingSourceInvoiceCancellationV1 r) => r.CompanyId != Guid.Empty && r.CancellationId != Guid.Empty
        && r.SourceDocumentId != Guid.Empty && r.InvoiceId != Guid.Empty && r.FinalizedVersion > 0 && r.CancelledBy != Guid.Empty
        && r.CancelledAtUtc != default && r.CancelledAtUtc.Offset == TimeSpan.Zero
        && r.DocumentKind is AccountingSourceDocumentKindsV1.SalesInvoice or AccountingSourceDocumentKindsV1.PurchaseInvoice
        && !string.IsNullOrWhiteSpace(r.ActorId) && r.ActorId.Length <= 128
        && !string.IsNullOrWhiteSpace(r.CorrelationId) && r.CorrelationId.Length <= 128 && r.CausationId is not { Length: > 128 }
        && !string.IsNullOrWhiteSpace(r.Reason) && r.Reason == r.Reason.Trim() && r.Reason.Length <= 1000
        && !r.Reason.Any(char.IsControl);
}
