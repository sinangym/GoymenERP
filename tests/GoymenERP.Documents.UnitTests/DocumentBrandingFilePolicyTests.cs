using GoymenERP.Modules.Documents.Application;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;
using GoymenERP.Modules.Documents.Infrastructure;
using Microsoft.Extensions.Configuration;
using static GoymenERP.Documents.UnitTests.Harness;

namespace GoymenERP.Documents.UnitTests;

/// <summary>Actual configuration reader, synthetic vault store/scanner; no environment or SQL acceptance claim.</summary>
public sealed class DocumentBrandingFilePolicyTests
{
    private const string Key = DocumentBrandingFilePolicy.AllowedContentTypesKey;

    [Fact]
    public void Missing_policy_preserves_the_closed_standard_set_and_a_configured_subset_uses_canonical_kind_order()
    {
        var configuration = Configuration();
        var reader = new DocumentBrandingFilePolicy(configuration);
        Assert.Equal(DocumentContentPolicy.StandardKinds, reader.Read().Kinds);
        configuration[Key + ":0"] = "image/jpeg";
        configuration[Key + ":1"] = "image/png";
        var narrowed = reader.Read();
        Assert.True(narrowed.Available);
        Assert.Equal([DocumentFileKind.Png, DocumentFileKind.Jpeg], narrowed.Kinds);
        Assert.DoesNotContain(DocumentFileKind.Pdf, narrowed.Kinds);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("application/pdf", null)]
    [InlineData(null, "application/xml")]
    [InlineData(null, "image/pjpeg")]
    [InlineData(null, "image/png ")]
    [InlineData(null, "IMAGE/PNG")]
    [InlineData(null, "")]
    public void Explicit_empty_scalar_or_noncanonical_or_nonstandard_type_is_unavailable(string? scalar, string? first)
    {
        var values = new Dictionary<string, string?> { [scalar is not null ? Key : Key + ":0"] = scalar ?? first };
        AssertClosed(new DocumentBrandingFilePolicy(new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
    }

    [Fact]
    public void Null_empty_list_duplicate_sparse_nonindex_and_nested_configuration_do_not_fall_back_to_defaults()
    {
        foreach (var values in new Dictionary<string, string?>[]
        {
            new() { [Key] = null },
            new() { [Key + ":0"] = "image/png", [Key + ":1"] = "image/png" },
            new() { [Key + ":1"] = "image/png" },
            new() { [Key + ":00"] = "image/png" },
            new() { [Key + ":mime"] = "image/png" },
            new() { [Key + ":0:type"] = "image/png" },
            new() { [Key] = "image/png", [Key + ":0"] = "application/pdf" },
        }) AssertClosed(new(new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
    }

    [Fact]
    public async Task Narrowed_admission_refuses_new_upload_but_keeps_original_payload_actor_key_and_response_on_replay()
    {
        var configuration = Configuration(); var policy = new DocumentBrandingFilePolicy(configuration);
        var harness = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean)).Grant(DocumentsPermissionManifestV1.FilesUpload);
        var service = Service(harness, policy); var request = PdfUpload();
        var original = await service.UploadAsync(request, harness.Metadata("branding-fixed-upload"));
        configuration[Key + ":0"] = "image/png";
        var replay = await service.UploadAsync(request, harness.Metadata("branding-fixed-upload"));
        Assert.True(replay.IsReplay); Assert.Equal(original.Document, replay.Document);
        var mismatch = await Assert.ThrowsAsync<DocumentServiceException>(() => service.UploadAsync(request with { FileName = "changed.pdf" }, harness.Metadata("branding-fixed-upload")));
        Assert.Equal(409, mismatch.Status);
        var refused = await Assert.ThrowsAsync<DocumentServiceException>(() => service.UploadAsync(request, harness.Metadata("branding-new-upload")));
        Assert.Equal((422, DocumentRuleCodes.TypeNotAllowed), (refused.Status, refused.Code));
        Assert.Single(harness.World.Documents); Assert.Single(harness.World.Attempts);
        var png = await service.UploadAsync(request with { FileName = "logo.png", DeclaredContentType = "image/png", Content = DocumentContentRulesTests.Png },
            harness.Metadata("branding-png"));
        Assert.Equal("image/png", png.Document.ContentType);
    }

    [Fact]
    public async Task A_reload_at_the_store_fence_refuses_the_new_upload_without_metadata_or_attempt_and_other_owners_stay_unchanged()
    {
        var configuration = Configuration(); var policy = new DocumentBrandingFilePolicy(configuration);
        var harness = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean)).Grant(DocumentsPermissionManifestV1.FilesUpload);
        harness.Store.BeforeWrite = () => configuration[Key + ":0"] = "image/png";
        var refused = await Assert.ThrowsAsync<DocumentServiceException>(() => Service(harness, policy).UploadAsync(PdfUpload(), harness.Metadata("branding-reload")));
        Assert.Equal((422, DocumentRuleCodes.TypeNotAllowed), (refused.Status, refused.Code));
        Assert.Empty(harness.World.Documents); Assert.Empty(harness.World.Attempts);
        // A content-addressed orphan may remain; the failed command never claims a committed document.
        Assert.Single(harness.World.Objects.Objects);
        harness.Store.BeforeWrite = null;
        harness.World.Readable.Add((CompanyA, CompanyA));
        var ordinary = await harness.Service.UploadAsync(Harness.Upload(CompanyA, DocumentContentRulesTests.Pdf), harness.Metadata("ordinary-pdf"));
        Assert.Equal("application/pdf", ordinary.Document.ContentType);
    }

    [Fact]
    public async Task Invalid_policy_is_unavailable_for_new_upload_and_replay_without_new_scanning_or_metadata()
    {
        var configuration = Configuration(); var policy = new DocumentBrandingFilePolicy(configuration);
        var harness = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean)).Grant(DocumentsPermissionManifestV1.FilesUpload);
        var service = Service(harness, policy); var request = PdfUpload();
        await service.UploadAsync(request, harness.Metadata("branding-policy-invalid"));
        configuration[Key + ":0"] = "application/xml";
        foreach (var key in new[] { "branding-policy-invalid", "branding-policy-new" })
            Assert.Equal(503, (await Assert.ThrowsAsync<DocumentServiceException>(() => service.UploadAsync(request, harness.Metadata(key)))).Status);
        Assert.Single(harness.World.Documents); Assert.Single(harness.World.Attempts); Assert.Single(harness.World.Objects.Objects);
    }

    [Fact]
    public async Task New_version_uses_current_admission_but_a_recorded_version_replay_keeps_its_original_kind()
    {
        var configuration = Configuration(); var policy = new DocumentBrandingFilePolicy(configuration);
        var harness = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean)).Grant(DocumentsPermissionManifestV1.FilesUpload);
        var service = Service(harness, policy);
        var original = await service.UploadAsync(PdfUpload(), harness.Metadata("branding-version-upload"));
        var append = new AppendDocumentVersionRequest(1, "version.pdf", "application/pdf", DocumentContentRulesTests.Pdf);
        var version = await service.AppendVersionAsync(original.Document.Id, append, harness.Metadata("branding-version"));
        configuration[Key + ":0"] = "image/png";
        var replay = await service.AppendVersionAsync(original.Document.Id, append, harness.Metadata("branding-version"));
        Assert.True(replay.IsReplay); Assert.Equal(version.Document, replay.Document);
        var refused = await Assert.ThrowsAsync<DocumentServiceException>(() => service.AppendVersionAsync(original.Document.Id,
            append with { ExpectedCurrentVersion = 2 }, harness.Metadata("branding-next-version")));
        Assert.Equal((422, DocumentRuleCodes.TypeNotAllowed), (refused.Status, refused.Code));
        Assert.Equal(2, harness.World.Attempts.Count);
    }

    [Fact]
    public async Task A_reload_at_the_version_store_fence_keeps_the_original_document_version_and_attempt_count()
    {
        var configuration = Configuration(); var policy = new DocumentBrandingFilePolicy(configuration);
        var harness = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean)).Grant(DocumentsPermissionManifestV1.FilesUpload);
        var service = Service(harness, policy);
        var original = await service.UploadAsync(PdfUpload(), harness.Metadata("branding-before-reload"));
        harness.Store.BeforeWrite = () => configuration[Key + ":0"] = "image/png";
        var refused = await Assert.ThrowsAsync<DocumentServiceException>(() => service.AppendVersionAsync(original.Document.Id,
            new(1, "next.pdf", "application/pdf", DocumentContentRulesTests.Pdf), harness.Metadata("branding-version-reload")));
        Assert.Equal((422, DocumentRuleCodes.TypeNotAllowed), (refused.Status, refused.Code));
        Assert.Equal(1, Assert.Single(harness.World.Documents).VersionNo); Assert.Single(harness.World.Attempts);
    }

    private static IConfigurationRoot Configuration() => new ConfigurationBuilder().AddInMemoryCollection().Build();
    private static void AssertClosed(DocumentBrandingFilePolicy policy)
    { var actual = policy.Read(); Assert.False(actual.Available); Assert.Empty(actual.Kinds); }
    private static UploadDocumentRequest PdfUpload() => new(DocumentBrandingContractV1.CompanyOwnerKind, CompanyA,
        "branding.pdf", "application/pdf", DocumentContentRulesTests.Pdf);
    private static DocumentService Service(Harness harness, DocumentBrandingFilePolicy policy) => new(harness.Company, harness.User,
        harness.Authority, harness.Correlation, [new PolicyOwner(policy)], harness.Store, harness.Objects, harness.Scanner,
        new DocumentsServiceBindings(4096), TimeProvider.System);
    private sealed class PolicyOwner(DocumentBrandingFilePolicy policy) : IDocumentOwnerLinkPortV1
    {
        public string Kind => DocumentBrandingContractV1.CompanyOwnerKind;
        public IReadOnlyCollection<DocumentFileKind> AcceptedFileKinds => policy.Read() is { Available: true } value ? value.Kinds
            : throw new DocumentServiceException(503, "service_unavailable");
        public Task<DocumentOwnerAccessV1> CheckReadableAsync(Guid ownerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(policy.Read().Available ? DocumentOwnerAccessV1.Readable : DocumentOwnerAccessV1.Unavailable);
    }
}
