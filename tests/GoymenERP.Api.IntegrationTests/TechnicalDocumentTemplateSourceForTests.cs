using GoymenERP.Modules.Documents.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Explicit synthetic no-publication fixture for legacy technical PDF HTTP facts. Not a production fallback.</summary>
internal sealed class TechnicalDocumentTemplateSourceForTests : IDocumentPublishedTemplateReadPortV1
{
    public Task<DocumentPublishedTemplateReadV1> ReadAsync(Guid companyId, DocumentTemplateModuleV1 module,
        string code, CancellationToken cancellationToken = default) => Task.FromResult(new DocumentPublishedTemplateReadV1(
            DocumentPublishedTemplateOutcomeV1.NoSource, companyId, module, code, null, null, null));
}
