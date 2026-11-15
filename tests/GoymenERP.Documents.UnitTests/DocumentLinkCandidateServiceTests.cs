using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;
using static GoymenERP.Documents.UnitTests.Harness;

namespace GoymenERP.Documents.UnitTests;

/// <summary>
/// KLN-D50 / DEC-DOC-002: before uploading a file again, the record asks which earlier documents of this company hold
/// the same content and may be linked instead. The list offers the link only for an owner kind that takes existing
/// documents and only while the link name is granted; the probe names a candidate under a record the actor may read
/// (KLN-D49 (3)), never under another company and never when it is already on the target. SENTETIK / NOT_ENV.
/// </summary>
public sealed class DocumentLinkCandidateServiceTests
{
    private const string Read = DocumentsPermissionManifestV1.FilesRead;
    private const string Upload = DocumentsPermissionManifestV1.FilesUpload;
    private const string Link = DocumentsPermissionManifestV1.FilesLink;
    private const string OrderKind = DocumentOwnerLinkKindsV1.ManufacturingOrder;
    private const string PartyKind = DocumentOwnerLinkKindsV1.CounterpartyParty;
    private static readonly byte[] V1 = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31];
    private static readonly byte[] Other = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x39];
    private static readonly Guid Party = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid HiddenParty = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
    private static readonly Guid Order = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    [Fact]
    public async Task The_list_offers_the_link_only_for_a_kind_that_takes_existing_documents_and_with_the_link_permission()
    {
        var world = World();
        var reader = Clean(world).Grant(Read);
        var linker = Clean(world).Grant(Read, Link);

        Assert.False((await reader.Service.ListAsync(OrderKind, Order)).CanLink);
        Assert.True((await linker.Service.ListAsync(OrderKind, Order)).CanLink);
        // The harness Cari owner did not opt in: the link name alone does not offer it.
        Assert.False((await linker.Service.ListAsync(PartyKind, Party)).CanLink);
    }

    [Fact]
    public async Task Candidates_name_an_earlier_document_under_a_readable_record_and_drop_it_once_it_is_on_the_target()
    {
        var world = World();
        var service = Clean(world).Grant(Upload, Read, Link);
        var document = await UploadAsync(service, Party, V1);
        await UploadAsync(service, Party, Other);
        var sha256 = DocumentContentHash.Compute(V1);

        var before = await service.Service.ListLinkCandidatesAsync(OrderKind, Order, sha256);

        Assert.Equal((OrderKind, Order, sha256), (before.OwnerKind, before.OwnerId, before.Sha256));
        var candidate = Assert.Single(before.Items);
        Assert.Equal((document.Id, 1, document.FileName, document.SizeBytes, PartyKind, Party),
            (candidate.DocumentId, candidate.VersionNo, candidate.FileName, candidate.SizeBytes, candidate.SourceOwnerKind,
                candidate.SourceOwnerId));
        Assert.Equal("application/pdf", candidate.ContentType);

        await service.Service.LinkAsync(document.Id, new LinkDocumentRequest(OrderKind, Order, 1), service.Metadata("link-1"));
        var after = await service.Service.ListLinkCandidatesAsync(OrderKind, Order, sha256);

        Assert.Empty(after.Items);
        // The probe stores nothing: no attempt is recorded by it.
        Assert.Single(world.Attempts, attempt => attempt.Command == DocumentCommands.LinkV1);
    }

    [Fact]
    public async Task A_document_none_of_whose_records_is_readable_is_withheld_from_the_candidates()
    {
        var world = World();
        world.Readable.Add((CompanyA, HiddenParty));
        var service = Clean(world).Grant(Upload, Link);
        var hidden = await UploadAsync(service, HiddenParty, V1);
        var visible = await UploadAsync(service, Party, V1);
        world.Readable.Remove((CompanyA, HiddenParty));

        var candidates = await service.Service.ListLinkCandidatesAsync(OrderKind, Order, DocumentContentHash.Compute(V1));

        Assert.Equal([visible.Id], candidates.Items.Select(item => item.DocumentId));
        Assert.DoesNotContain(candidates.Items, item => item.DocumentId == hidden.Id);
    }

    [Fact]
    public async Task The_probe_needs_the_link_permission_an_opted_in_kind_a_well_formed_hash_and_a_readable_target()
    {
        var world = World();
        var owner = Clean(world).Grant(Upload);
        await UploadAsync(owner, Party, V1);
        var sha256 = DocumentContentHash.Compute(V1);
        var linker = Clean(world).Grant(Link);

        var anonymous = await Refused(new Harness(CompanyA, actor: null, world: world).Grant(Link).Service
            .ListLinkCandidatesAsync(OrderKind, Order, sha256));
        var withoutLink = await Refused(owner.Service.ListLinkCandidatesAsync(OrderKind, Order, sha256));
        var closed = await Refused(linker.Service.ListLinkCandidatesAsync(DocumentOwnerLinkKindsV1.FinanceOfficialSalesInvoice, Order, sha256));
        var malformed = await Refused(linker.Service.ListLinkCandidatesAsync(OrderKind, Order, sha256.ToUpperInvariant()));
        var shortHash = await Refused(linker.Service.ListLinkCandidatesAsync(OrderKind, Order, "abc"));
        world.TargetReadable.Remove((CompanyA, Order));
        var unreadable = await Refused(linker.Service.ListLinkCandidatesAsync(OrderKind, Order, sha256));

        Assert.Equal(401, anonymous.Status);
        Assert.Equal(403, withoutLink.Status);
        Assert.Equal((422, DocumentProblemCodesV1.LinkNotAccepted), (closed.Status, closed.Code));
        Assert.Equal((400, DocumentProblemCodesV1.ValidationFailed), (malformed.Status, malformed.Code));
        Assert.Equal((400, DocumentProblemCodesV1.ValidationFailed), (shortHash.Status, shortHash.Code));
        Assert.Equal(404, unreadable.Status);
        Assert.Equal(0, linker.ClosedTarget.Calls);
    }

    [Fact]
    public async Task Another_companys_document_with_the_same_content_is_never_a_candidate()
    {
        var world = World();
        world.TargetReadable.Add((CompanyB, Order));
        var owner = Clean(world).Grant(Upload);
        await UploadAsync(owner, Party, V1);

        var other = new Harness(CompanyB, world: world).Grant(Link);
        var candidates = await other.Service.ListLinkCandidatesAsync(OrderKind, Order, DocumentContentHash.Compute(V1));

        Assert.Empty(candidates.Items);
    }

    private static Harness Clean(Harness.SharedWorld world) =>
        new(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world);

    private static async Task<DocumentView> UploadAsync(Harness service, Guid party, byte[] content) =>
        (await service.Service.UploadAsync(Harness.Upload(party, content), service.Metadata($"create-{Guid.NewGuid():N}"))).Document;

    private static Harness.SharedWorld World()
    {
        var world = new Harness.SharedWorld();
        world.Readable.Add((CompanyA, Party));
        world.TargetReadable.Add((CompanyA, Order));
        return world;
    }

    private static async Task<DocumentServiceException> Refused<T>(Task<T> call) =>
        await Assert.ThrowsAsync<DocumentServiceException>(() => call);
}
