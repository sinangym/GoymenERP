using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.DocumentsRuntime;

/// <summary>
/// WP13 (YOL-D11) adapter over the Document Vault, answered with the actor's own Documents read authority (Sales never reads
/// document tables). The signed approval must be the CURRENT version of a clean, downloadable PDF, PNG or JPEG uploaded to this sales
/// order (<see cref="DocumentOwnerLinkKindsV1.SalesOrder"/>, owner id = the order) in the context company. The vault already enforced
/// the size bound, the type list and the content signature at upload; anything else, or any failure, answers a non-Accepted outcome
/// (fail closed).
/// </summary>
public sealed class SalesOrderSignedApprovalDocumentPort(IDocumentService documents) : ISalesOrderSignedApprovalDocumentPortV1
{
    /// <summary>The scanned-image and PDF kinds a signed paper may arrive as (the vault's standard kinds).</summary>
    internal static readonly IReadOnlySet<string> AcceptedContentTypes =
        new HashSet<string>(["application/pdf", "image/png", "image/jpeg"], StringComparer.Ordinal);

    public async Task<SalesSignedApprovalDocumentCheckV1> CheckAsync(Guid companyId, Guid orderId, Guid documentId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (companyId == Guid.Empty || orderId == Guid.Empty || documentId == Guid.Empty)
        {
            return Refused(SalesSignedApprovalDocumentOutcomeV1.NotFound);
        }

        DocumentVersionHistoryResponse history;
        try
        {
            // The newest version only: a signed approval pins the version the vault holds now.
            history = await documents.ListVersionsAsync(documentId, 1, null, cancellationToken);
        }
        catch (DocumentServiceException exception)
        {
            return Refused(exception.Status switch
            {
                401 or 403 => SalesSignedApprovalDocumentOutcomeV1.Denied,
                404 => SalesSignedApprovalDocumentOutcomeV1.NotFound,
                _ => SalesSignedApprovalDocumentOutcomeV1.Unavailable,
            });
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Refused(SalesSignedApprovalDocumentOutcomeV1.Unavailable);
        }

        if (history is null || history.DocumentId != documentId || history.Versions is not { Count: 1 } versions)
        {
            return Refused(SalesSignedApprovalDocumentOutcomeV1.Unavailable);
        }

        var view = versions[0];
        if (view.Id != documentId || view.CompanyId != companyId || view.VersionNo < 1)
        {
            return Refused(SalesSignedApprovalDocumentOutcomeV1.Unavailable);
        }

        if (!string.Equals(view.OwnerKind, DocumentOwnerLinkKindsV1.SalesOrder, StringComparison.Ordinal) || view.OwnerId != orderId)
        {
            return Refused(SalesSignedApprovalDocumentOutcomeV1.OwnerMismatch);
        }

        if (!AcceptedContentTypes.Contains(view.ContentType))
        {
            return Refused(SalesSignedApprovalDocumentOutcomeV1.KindNotAllowed);
        }

        if (!view.Downloadable)
        {
            return Refused(SalesSignedApprovalDocumentOutcomeV1.NotDownloadable);
        }

        return new SalesSignedApprovalDocumentCheckV1(SalesSignedApprovalDocumentOutcomeV1.Accepted, view.VersionNo, view.Sha256);
    }

    private static SalesSignedApprovalDocumentCheckV1 Refused(SalesSignedApprovalDocumentOutcomeV1 outcome) => new(outcome, null, null);
}
