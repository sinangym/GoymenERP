using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.PurchasingRuntime;

public static class PurchaseInvoiceImportHost
{
    public static IServiceCollection AddPurchaseInvoiceImportHost(this IServiceCollection services)
    {
        services.AddScoped<IDocumentOwnerLinkPortV1, PurchaseInvoiceImportOwnerLink>();
        services.AddScoped<IPurchaseInvoiceImportDocumentReadV1, PurchaseInvoiceImportDocumentRead>();
        return services;
    }
}

/// <summary>The owner proof reads Purchasing's reservation contract, avoiding any vault/service dependency cycle.</summary>
public sealed class PurchaseInvoiceImportOwnerLink(ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority, IPurchaseInvoiceImportReservationStoreV1 reservations) : IDocumentOwnerLinkPortV1
{
    public string Kind => DocumentOwnerLinkKindsV1.PurchasingInvoiceImport;
    public IReadOnlyCollection<DocumentFileKind> AcceptedFileKinds => DocumentContentPolicy.PurchaseInvoiceImportKinds;

    public async Task<DocumentOwnerAccessV1> CheckReadableAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        var companyId = company.CompanyId; var actor = user.UserId;
        bool Same() => company.IsResolved && companyId != Guid.Empty && company.CompanyId == companyId
            && user.IsAuthenticated && !string.IsNullOrWhiteSpace(actor) && user.UserId == actor && user.CanAccessCompany(companyId);
        async Task<bool> Allowed()
        {
            if (!Same()) return false;
            foreach (var permission in new[] { PurchaseIncomingInvoicePermissionNamesV1.Import, PurchasingPermissionManifestV1.OrdersPriceRead })
            {
                var scope = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
                if (!scope.IsGranted || !scope.IsCompanyWide || !Same()) return false;
            }
            return true;
        }
        try
        {
            if (!await Allowed()) return DocumentOwnerAccessV1.Denied;
            if (ownerId == Guid.Empty) return DocumentOwnerAccessV1.NotFound;
            var reservation = await reservations.FindAsync(ownerId, cancellationToken);
            if (!await Allowed()) return DocumentOwnerAccessV1.Denied;
            if (reservation is null) return DocumentOwnerAccessV1.NotFound;
            return reservation.CompanyId == companyId && reservation.Id == ownerId && reservation.Version == "1"
                ? DocumentOwnerAccessV1.Readable : DocumentOwnerAccessV1.Unavailable;
        }
        catch (OperationCanceledException) { throw; }
        catch { return DocumentOwnerAccessV1.Unavailable; }
    }
}

public sealed class PurchaseInvoiceImportDocumentRead(IDocumentService documents, ICompanyContext company, ICurrentUser user)
    : IPurchaseInvoiceImportDocumentReadV1
{
    public async Task<PurchaseInvoiceImportDocumentV1> ReadAsync(Guid importId, Guid documentId, int version, CancellationToken token = default)
    {
        var companyId = company.CompanyId; var actor = user.UserId;
        void Same()
        {
            if (!company.IsResolved || companyId == Guid.Empty || company.CompanyId != companyId || !user.IsAuthenticated
                || string.IsNullOrWhiteSpace(actor) || user.UserId != actor || !user.CanAccessCompany(companyId))
                throw new PurchasingServiceException(403, "forbidden");
        }
        Same();
        try
        {
            var source = await documents.ListAsync(DocumentOwnerLinkKindsV1.PurchasingInvoiceImport, importId, token); Same();
            var linked = source.Items.SingleOrDefault(item => item.Id == documentId);
            if (linked is null || linked.CompanyId != companyId || linked.OwnerKind != DocumentOwnerLinkKindsV1.PurchasingInvoiceImport
                || linked.OwnerId != importId || version < 1 || version > linked.VersionNo) throw new PurchasingServiceException(404, "not_found");
            var content = await documents.GetVersionContentAsync(documentId, version, token); Same();
            if (content.DocumentId != documentId) throw new PurchasingServiceException(503, "service_unavailable");
            var format = content.ContentType switch
            {
                "application/xml" => PurchaseInvoiceFileFormatV1.UblXml,
                "text/csv" => PurchaseInvoiceFileFormatV1.Csv,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => PurchaseInvoiceFileFormatV1.Xlsx,
                _ => throw new PurchasingServiceException(400, "validation_failed"),
            };
            return new(companyId, content.DocumentId, version, content.Sha256, format, content.Content);
        }
        catch (DocumentServiceException exception)
        {
            Same();
            throw new PurchasingServiceException(exception.Status is 401 or 403 or 404 ? exception.Status : 503,
                exception.Status is 401 or 403 or 404 ? exception.Code : "service_unavailable");
        }
    }
}
