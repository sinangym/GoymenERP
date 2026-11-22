using GoymenERP.Api.DocumentsRuntime;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WP13 (YOL-D11): the host adapter over the Document Vault answers Accepted (with the current version and hash) only for the current
/// version of a clean, downloadable PDF/PNG/JPEG uploaded to THIS order in the context company; another record's document, another
/// company's answer, another kind, an unscanned file and any vault refusal or failure are never accepted (fail closed). It asks the
/// vault through the actor's own document read (<see cref="IDocumentService.ListVersionsAsync"/>), newest version only.
/// </summary>
public sealed class SalesOrderSignedApprovalDocumentPortTests
{
    private static readonly Guid Company = TestCompanyIds.CompanyA;
    private static readonly Guid Order = Guid.Parse("0d0d0d0d-0d0d-4d0d-8d0d-0d0d0d0d0d0d");
    private static readonly Guid Document = Guid.Parse("d0c0d0c0-d0c0-4d0c-8d0c-d0c0d0c0d0c0");
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static DocumentView View(string ownerKind = DocumentOwnerLinkKindsV1.SalesOrder, Guid? ownerId = null, string contentType = "application/pdf",
        bool downloadable = true, Guid? companyId = null) =>
        new(Document, companyId ?? Company, ownerKind, ownerId ?? Order, 3, "imzali-onay.pdf", contentType, 1024, Sha, "Clean", downloadable,
            "SENTETIK-uploader", DateTimeOffset.UnixEpoch);

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    public async Task The_current_clean_version_uploaded_to_the_order_is_accepted_with_its_version_and_hash(string contentType)
    {
        var vault = new Vault(_ => new DocumentVersionHistoryResponse(Document, [View(contentType: contentType)], null));

        var check = await new SalesOrderSignedApprovalDocumentPort(vault).CheckAsync(Company, Order, Document);

        Assert.Equal((SalesSignedApprovalDocumentOutcomeV1.Accepted, (int?)3, Sha), (check.Outcome, check.VersionNo, check.Sha256));
        Assert.Equal((Document, 1, (string?)null), vault.Asked);
    }

    [Fact]
    public async Task Another_records_document_another_kind_an_unscanned_file_or_another_companys_answer_is_never_accepted()
    {
        async Task<SalesSignedApprovalDocumentOutcomeV1> Outcome(DocumentView view) =>
            (await new SalesOrderSignedApprovalDocumentPort(new Vault(_ => new DocumentVersionHistoryResponse(Document, [view], null)))
                .CheckAsync(Company, Order, Document)).Outcome;

        Assert.Equal(SalesSignedApprovalDocumentOutcomeV1.OwnerMismatch, await Outcome(View(ownerId: Guid.NewGuid())));
        Assert.Equal(SalesSignedApprovalDocumentOutcomeV1.OwnerMismatch, await Outcome(View(ownerKind: DocumentOwnerLinkKindsV1.CounterpartyParty)));
        Assert.Equal(SalesSignedApprovalDocumentOutcomeV1.KindNotAllowed, await Outcome(View(contentType: "text/csv")));
        Assert.Equal(SalesSignedApprovalDocumentOutcomeV1.NotDownloadable, await Outcome(View(downloadable: false)));
        Assert.Equal(SalesSignedApprovalDocumentOutcomeV1.Unavailable, await Outcome(View(companyId: TestCompanyIds.CompanyB)));
    }

    [Theory]
    [InlineData(401, SalesSignedApprovalDocumentOutcomeV1.Denied)]
    [InlineData(403, SalesSignedApprovalDocumentOutcomeV1.Denied)]
    [InlineData(404, SalesSignedApprovalDocumentOutcomeV1.NotFound)]
    [InlineData(503, SalesSignedApprovalDocumentOutcomeV1.Unavailable)]
    public async Task A_vault_refusal_is_mapped_and_never_accepted(int status, SalesSignedApprovalDocumentOutcomeV1 expected)
    {
        var vault = new Vault(_ => throw new DocumentServiceException(status, "SENTETIK"));

        var check = await new SalesOrderSignedApprovalDocumentPort(vault).CheckAsync(Company, Order, Document);

        Assert.Equal((expected, (int?)null, (string?)null), (check.Outcome, check.VersionNo, check.Sha256));
    }

    [Fact]
    public async Task A_failure_an_empty_history_or_an_empty_identity_is_never_accepted_and_empty_ids_do_not_ask_the_vault()
    {
        var failing = new SalesOrderSignedApprovalDocumentPort(new Vault(_ => throw new TimeoutException()));
        var empty = new SalesOrderSignedApprovalDocumentPort(new Vault(_ => new DocumentVersionHistoryResponse(Document, [], null)));
        var untouched = new Vault(_ => throw new InvalidOperationException("The vault must not be asked."));

        Assert.Equal(SalesSignedApprovalDocumentOutcomeV1.Unavailable, (await failing.CheckAsync(Company, Order, Document)).Outcome);
        Assert.Equal(SalesSignedApprovalDocumentOutcomeV1.Unavailable, (await empty.CheckAsync(Company, Order, Document)).Outcome);
        Assert.Equal(SalesSignedApprovalDocumentOutcomeV1.NotFound,
            (await new SalesOrderSignedApprovalDocumentPort(untouched).CheckAsync(Company, Order, Guid.Empty)).Outcome);
        Assert.Null(untouched.Asked);
    }

    private sealed class Vault(Func<Guid, DocumentVersionHistoryResponse> history) : IDocumentService
    {
        public (Guid, int?, string?)? Asked { get; private set; }

        public Task<DocumentVersionHistoryResponse> ListVersionsAsync(Guid documentId, int? pageSize, string? cursor,
            CancellationToken cancellationToken = default)
        {
            Asked = (documentId, pageSize, cursor);
            return Task.FromResult(history(documentId));
        }

        public Task<DocumentUploadResponse> UploadAsync(UploadDocumentRequest request, DocumentCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentListResponse> ListAsync(string? ownerKind, Guid ownerId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentContentV1> GetContentAsync(Guid documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentVersionWriteResponse> AppendVersionAsync(Guid documentId, AppendDocumentVersionRequest request,
            DocumentCommandMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentContentV1> GetVersionContentAsync(Guid documentId, int versionNo, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentLinkWriteResponse> LinkAsync(Guid documentId, LinkDocumentRequest request, DocumentCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentLinkCandidatesResponse> ListLinkCandidatesAsync(string? ownerKind, Guid ownerId, string? sha256,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
