using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.DocumentsRuntime;

/// <summary>
/// Adapts Sales's authorized order-detail read to the Document Vault owner-link contract. The vault never reads Sales
/// persistence. Missing, foreign and unreadable orders stay non-disclosing; provider failures remain unavailable.
/// </summary>
public sealed class SalesDocumentOwnerLinkPort(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ISalesDraftService sales) : IDocumentOwnerLinkPortV1
{
    public string Kind => DocumentOwnerLinkKindsV1.SalesOrder;

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
            var detail = await sales.GetDraftDetailAsync(ownerId, cancellationToken);
            if (!IsCurrent() || companyContext.CompanyId != companyId
                || !string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal))
            {
                return DocumentOwnerAccessV1.Denied;
            }

            return detail.Draft.Id == ownerId && detail.Draft.CompanyId == companyId
                ? DocumentOwnerAccessV1.Readable
                : DocumentOwnerAccessV1.Unavailable;
        }
        catch (SalesDraftServiceException exception) when (exception.Status == 404)
        {
            return DocumentOwnerAccessV1.NotFound;
        }
        catch (SalesDraftServiceException exception) when (exception.Status is 401 or 403)
        {
            return DocumentOwnerAccessV1.Denied;
        }
        catch (SalesDraftServiceException exception) when (exception.Status == 409)
        {
            return DocumentOwnerAccessV1.Conflict;
        }
        catch (SalesDraftServiceException exception) when (exception.Status == 503)
        {
            return DocumentOwnerAccessV1.Unavailable;
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
