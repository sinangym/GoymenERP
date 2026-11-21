using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.DocumentsRuntime;

/// <summary>
/// #66 6860586175: adapts Finance's authorized owner read of an official sales invoice (a reserved id or its record)
/// to the Document Vault owner-link contract, so the PDF is uploaded to the record before it is recorded. The vault
/// never reads Finance persistence. Unknown and other-company ids answer NotFound, a missing Record/Approve authority
/// Denied (both non-disclosing to the client); provider failures remain unavailable.
/// </summary>
public sealed class FinanceDocumentOwnerLinkPort(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    IFinanceOfficialSalesInvoiceOwnerReadV1 invoices) : IDocumentOwnerLinkPortV1
{
    public string Kind => DocumentOwnerLinkKindsV1.FinanceOfficialSalesInvoice;

    public async Task<DocumentOwnerAccessV1> CheckReadableAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ownerId == Guid.Empty || !IsCurrent())
        {
            return DocumentOwnerAccessV1.Denied;
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId!;
        try
        {
            var outcome = await invoices.CheckReadableAsync(
                new FinanceContext(companyId, actorId, correlation.CorrelationId, correlation.CausationId), ownerId,
                cancellationToken);
            if (!IsCurrent() || companyContext.CompanyId != companyId
                || !string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal))
            {
                return DocumentOwnerAccessV1.Denied;
            }

            return outcome switch
            {
                ReadOutcome.Found => DocumentOwnerAccessV1.Readable,
                ReadOutcome.NotFound => DocumentOwnerAccessV1.NotFound,
                ReadOutcome.Denied => DocumentOwnerAccessV1.Denied,
                _ => DocumentOwnerAccessV1.Unavailable,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return DocumentOwnerAccessV1.Unavailable;
        }
    }

    private bool IsCurrent() =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId) && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty && currentUser.CanAccessCompany(companyContext.CompanyId);
}
