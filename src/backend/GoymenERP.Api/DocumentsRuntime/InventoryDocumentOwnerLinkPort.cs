using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Api.DocumentsRuntime;

public sealed class InventoryDocumentOwnerLinkPort(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IInventoryLabelDetailsV1 labels) : IDocumentOwnerLinkPortV1
{
    public string Kind => DocumentOwnerLinkKindsV1.InventoryMaterialLabel;

    public async Task<DocumentOwnerAccessV1> CheckReadableAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId;
        if (!companyContext.IsResolved || companyId == Guid.Empty || !currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(actorId)
            || !currentUser.CanAccessCompany(companyId))
        {
            return DocumentOwnerAccessV1.Denied;
        }

        try
        {
            var detail = await labels.GetAsync(ownerId, cancellationToken);
            if (!companyContext.IsResolved || companyContext.CompanyId != companyId || !currentUser.IsAuthenticated
                || !string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal) || !currentUser.CanAccessCompany(companyId))
            {
                return DocumentOwnerAccessV1.Denied;
            }

            return detail.Identity.LabelId == ownerId && detail.Identity.CompanyId == companyId
                ? DocumentOwnerAccessV1.Readable
                : DocumentOwnerAccessV1.Unavailable;
        }
        catch (InventoryLabelDetailException exception) when (
            exception.StatusCode == 404 && exception.Code == InventoryLabelDetailCodesV1.NotFound)
        {
            return DocumentOwnerAccessV1.NotFound;
        }
        catch (InventoryLabelDetailException exception) when (exception.StatusCode is 401 or 403)
        {
            return DocumentOwnerAccessV1.Denied;
        }
        catch (InventoryLabelDetailException exception) when (
            exception.StatusCode == 409 && exception.Code == InventoryLabelDetailCodesV1.ReadConflict)
        {
            return DocumentOwnerAccessV1.Conflict;
        }
        catch (InventoryLabelDetailException exception) when (
            exception.StatusCode == 503 && exception.Code == InventoryLabelDetailCodesV1.Unavailable)
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
}
