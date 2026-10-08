using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.DocumentsRuntime;

/// <summary>
/// Adapts Purchasing's authorized purchase-order detail read to the Document Vault owner-link contract. The vault
/// never reads Purchasing persistence. A missing, foreign or unreadable order is non-disclosing; provider failures
/// remain unavailable so the caller can distinguish a temporary owner outage from a record that is not visible.
/// </summary>
public sealed class PurchasingDocumentOwnerLinkPort(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IPurchasingService purchasing) : IDocumentOwnerLinkPortV1
{
    public string Kind => DocumentOwnerLinkKindsV1.PurchasingPurchaseOrder;

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
            var detail = await purchasing.GetOrderDetailAsync(ownerId, cancellationToken);
            if (!IsCurrent() || companyContext.CompanyId != companyId
                || !string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal))
            {
                return DocumentOwnerAccessV1.Denied;
            }

            return detail.Order.Id == ownerId && detail.Order.CompanyId == companyId
                ? DocumentOwnerAccessV1.Readable
                : DocumentOwnerAccessV1.Unavailable;
        }
        catch (PurchasingServiceException exception) when (exception.Status == 404)
        {
            return DocumentOwnerAccessV1.NotFound;
        }
        catch (PurchasingServiceException exception) when (exception.Status is 401 or 403)
        {
            return DocumentOwnerAccessV1.Denied;
        }
        catch (PurchasingServiceException exception) when (exception.Status == 409)
        {
            return DocumentOwnerAccessV1.Conflict;
        }
        catch (PurchasingServiceException exception) when (exception.Status == 503)
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
