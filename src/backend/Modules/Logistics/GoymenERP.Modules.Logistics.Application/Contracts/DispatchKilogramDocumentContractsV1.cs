using GoymenERP.Modules.Logistics.Domain.WeighingDifferences;

namespace GoymenERP.Modules.Logistics.Application.Contracts;

public sealed record DispatchKilogramFinalizedDocumentV1(Guid Ettn, string Kind, Guid DocumentId,
    IReadOnlyList<DispatchKilogramDeclaredLine> Lines, int ExpectedDocumentVersion, string ExpectedDocumentSha256);

public sealed record DispatchKilogramDocumentReadV1(Guid CompanyId, Guid DispatchNoteId, Guid DocumentId,
    int Version, string Sha256);

/// <summary>Actual Documents owner evidence under the actor's own read authority, clean/downloadable PDF,
/// linked to this company and dispatch note. Metadata and user-asserted quantities do not prove legal validity.</summary>
public interface IDispatchKilogramDocumentReadPortV1
{
    Task<DispatchKilogramDocumentReadV1?> ReadAsync(Guid companyId, Guid dispatchNoteId, Guid documentId,
        int? version, CancellationToken cancellationToken = default);
}
