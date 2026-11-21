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

    /// <summary>F-026 (PLT-S9): the label/barcode PDF Inventory renders (F-026a) may be kept in the vault on this record.</summary>
    public bool AcceptsGeneratedDocuments => true;

    /// <summary>STK-S26 / KLN-D59: the label producer reuses the existing read permission; no role or grant is added.</summary>
    public IReadOnlyCollection<string> GeneratedDocumentProducerPermissions => [ErpPermissions.InventoryLabelsRead];

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

    /// <summary>
    /// KLN-D50: a stock (material) label takes an earlier document of the same company by a link (KLN-D49: one readable
    /// source record and this record readable), so the same file is never uploaded twice.
    /// </summary>
    public bool AcceptsExistingDocumentLinks => true;
}
