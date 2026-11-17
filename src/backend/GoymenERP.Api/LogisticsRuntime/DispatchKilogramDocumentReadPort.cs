using System.Globalization;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Api.LogisticsRuntime;

public sealed class DispatchKilogramDocumentReadPort(IDocumentService documents) : IDispatchKilogramDocumentReadPortV1
{
    public async Task<DispatchKilogramDocumentReadV1?> ReadAsync(Guid companyId, Guid dispatchNoteId, Guid documentId,
        int? version, CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty || dispatchNoteId == Guid.Empty || documentId == Guid.Empty || version is < 1 or int.MaxValue) return null;
        var history = await documents.ListVersionsAsync(documentId, 1,
            version is { } pinned ? (pinned + 1).ToString(CultureInfo.InvariantCulture) : null, cancellationToken);
        if (history?.DocumentId != documentId || history.Versions is not { Count: 1 }) return null;
        var view = history.Versions[0];
        return view.Id == documentId && view.CompanyId == companyId && view.OwnerKind == DocumentOwnerLinkKindsV1.LogisticsDispatchNote
            && view.OwnerId == dispatchNoteId && view.ContentType == "application/pdf" && view.Downloadable
            && (version is null || view.VersionNo == version) && view.VersionNo > 0
            && view.Sha256 is { Length: 64 } && view.Sha256.All(Uri.IsHexDigit)
            ? new(companyId, dispatchNoteId, documentId, view.VersionNo, view.Sha256) : null;
    }
}
