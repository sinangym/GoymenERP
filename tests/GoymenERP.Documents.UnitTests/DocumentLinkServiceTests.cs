using GoymenERP.Modules.Documents.Application.Contracts;
using static GoymenERP.Documents.UnitTests.Harness;

namespace GoymenERP.Documents.UnitTests;

/// <summary>
/// B-26 / DEC-DOC-002: an EXISTING document linked to one more owner record. The same physical file is shown on the
/// second record without a second blob, document or version; the link is idempotent, version-fenced, audited by the
/// store and refused for an owner kind that did not opt in.
/// </summary>
public sealed class DocumentLinkServiceTests
{
    private const string Read = DocumentsPermissionManifestV1.FilesRead;
    private const string Upload = DocumentsPermissionManifestV1.FilesUpload;
    private const string Link = DocumentsPermissionManifestV1.FilesLink;
    private const string OrderKind = DocumentOwnerLinkKindsV1.ManufacturingOrder;
    private static readonly byte[] V1 = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31];
    private static readonly byte[] V2 = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x32];
    private static readonly Guid Party = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Order = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid OtherOrder = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");

    [Fact]
    public async Task Link_shows_the_same_file_on_the_second_record_without_a_new_blob_or_version()
    {
        var world = World();
        var service = Clean(world).Grant(Upload, Read, Link);
        var document = await UploadAsync(service);
        var puts = world.Objects.Puts;

        var linked = await service.Service.LinkAsync(document.Id, new LinkDocumentRequest(OrderKind, Order, 1), service.Metadata("link-1"));

        Assert.False(linked.IsReplay);
        Assert.Equal(service.Correlation.CorrelationId, linked.OriginalCorrelationId);
        Assert.Equal((document.Id, OrderKind, Order, 1), (linked.Document.Id, linked.Document.OwnerKind, linked.Document.OwnerId,
            linked.Document.VersionNo));
        Assert.Equal(document.Sha256, linked.Document.Sha256);
        Assert.Single(world.Documents);
        Assert.Single(world.Versions);
        Assert.Equal(puts, world.Objects.Puts);
        Assert.Equal([(DocumentOwnerLinkKindsV1.CounterpartyParty, Party), (OrderKind, Order)],
            service.Store.LinksOf(CompanyA, document.Id).Select(link => (link.OwnerKind, link.OwnerId)));

        var underOrder = await service.Service.ListAsync(OrderKind, Order);
        var underParty = await service.Service.ListAsync(DocumentOwnerLinkKindsV1.CounterpartyParty, Party);
        Assert.Equal((document.Id, OrderKind, Order), (underOrder.Items.Single().Id, underOrder.Items.Single().OwnerKind,
            underOrder.Items.Single().OwnerId));
        Assert.Equal((document.Id, DocumentOwnerLinkKindsV1.CounterpartyParty, Party), (underParty.Items.Single().Id,
            underParty.Items.Single().OwnerKind, underParty.Items.Single().OwnerId));
    }

    [Fact]
    public async Task Link_requires_an_authenticated_actor_with_the_link_permission()
    {
        var world = World();
        var owner = Clean(world).Grant(Upload, Read);
        var document = await UploadAsync(owner);

        var anonymous = await Refused(new Harness(CompanyA, actor: null, world: world).Grant(Link).Service
            .LinkAsync(document.Id, new LinkDocumentRequest(OrderKind, Order, 1), owner.Metadata("link-anon")));
        var withoutLink = await Refused(owner.Service.LinkAsync(document.Id, new LinkDocumentRequest(OrderKind, Order, 1),
            owner.Metadata("link-no-grant")));

        Assert.Equal(401, anonymous.Status);
        Assert.Equal(403, withoutLink.Status);
        Assert.Single(owner.Store.LinksOf(CompanyA, document.Id));
        Assert.DoesNotContain(world.Attempts, attempt => attempt.Command == DocumentCommands.LinkV1);
    }

    [Fact]
    public async Task Owner_kind_that_did_not_opt_in_is_refused_and_unknown_kind_is_invalid()
    {
        var world = World();
        world.TargetReadable.Add((CompanyA, OtherOrder));
        var service = Clean(world).Grant(Upload, Link);
        var document = await UploadAsync(service);

        var closed = await Refused(service.Service.LinkAsync(document.Id,
            new LinkDocumentRequest(DocumentOwnerLinkKindsV1.FinanceOfficialSalesInvoice, OtherOrder, 1), service.Metadata("link-invoice")));
        var cari = await Refused(service.Service.LinkAsync(document.Id,
            new LinkDocumentRequest(DocumentOwnerLinkKindsV1.CounterpartyParty, Party, 1), service.Metadata("link-cari")));
        var unknown = await Refused(service.Service.LinkAsync(document.Id,
            new LinkDocumentRequest("sentetik.unknown", Order, 1), service.Metadata("link-unknown")));

        Assert.Equal((422, DocumentProblemCodesV1.LinkNotAccepted), (closed.Status, closed.Code));
        Assert.Equal((422, DocumentProblemCodesV1.LinkNotAccepted), (cari.Status, cari.Code));
        Assert.Equal((400, DocumentProblemCodesV1.ValidationFailed), (unknown.Status, unknown.Code));
        Assert.Equal(0, service.ClosedTarget.Calls);
        Assert.Single(service.Store.LinksOf(CompanyA, document.Id));
        Assert.DoesNotContain(world.Attempts, attempt => attempt.Command == DocumentCommands.LinkV1);
    }

    [Fact]
    public async Task Second_link_to_the_same_record_is_already_linked_without_a_second_effect()
    {
        var world = World();
        var service = Clean(world).Grant(Upload, Link);
        var document = await UploadAsync(service);
        await service.Service.LinkAsync(document.Id, new LinkDocumentRequest(OrderKind, Order, 1), service.Metadata("link-1"));
        var attempts = world.Attempts.Count;

        var refusal = await Refused(service.Service.LinkAsync(document.Id, new LinkDocumentRequest(OrderKind, Order, 1),
            service.Metadata("link-2")));

        Assert.Equal((409, DocumentProblemCodesV1.AlreadyLinked), (refusal.Status, refusal.Code));
        Assert.Equal(2, service.Store.LinksOf(CompanyA, document.Id).Count);
        Assert.Equal(attempts, world.Attempts.Count);
    }

    [Fact]
    public async Task Stale_expected_version_is_conflict_without_a_link()
    {
        var world = World();
        var service = Clean(world).Grant(Upload, Link);
        var document = await UploadAsync(service);
        await service.Service.AppendVersionAsync(document.Id, new AppendDocumentVersionRequest(1, "v2.pdf", "application/pdf", V2),
            service.Metadata("append-v2"));

        var refusal = await Refused(service.Service.LinkAsync(document.Id, new LinkDocumentRequest(OrderKind, Order, 1),
            service.Metadata("link-stale")));

        Assert.Equal((409, DocumentProblemCodesV1.ConcurrentChange), (refusal.Status, refusal.Code));
        Assert.Single(service.Store.LinksOf(CompanyA, document.Id));
    }

    [Fact]
    public async Task Same_key_replays_with_the_original_correlation_and_a_different_payload_is_refused()
    {
        var world = World();
        world.TargetReadable.Add((CompanyA, OtherOrder));
        var first = Clean(world).Grant(Upload, Link);
        var document = await UploadAsync(first);
        var original = await first.Service.LinkAsync(document.Id, new LinkDocumentRequest(OrderKind, Order, 1), first.Metadata("link-1"));

        var again = Clean(world).Grant(Link);
        var replay = await again.Service.LinkAsync(document.Id, new LinkDocumentRequest(OrderKind, Order, 1), again.Metadata("link-1"));
        var mismatch = await Refused(again.Service.LinkAsync(document.Id, new LinkDocumentRequest(OrderKind, OtherOrder, 1),
            again.Metadata("link-1")));

        Assert.True(replay.IsReplay);
        Assert.Equal(original.OriginalCorrelationId, replay.OriginalCorrelationId);
        Assert.NotEqual(again.Correlation.CorrelationId, replay.OriginalCorrelationId);
        Assert.Equal((OrderKind, Order), (replay.Document.OwnerKind, replay.Document.OwnerId));
        Assert.Equal((409, DocumentProblemCodesV1.SameKeyPayloadMismatch), (mismatch.Status, mismatch.Code));
        Assert.Equal(2, again.Store.LinksOf(CompanyA, document.Id).Count);
        Assert.Single(world.Attempts, attempt => attempt.Command == DocumentCommands.LinkV1);
    }

    [Fact]
    public async Task Unreadable_source_or_target_record_is_not_found_and_nothing_is_linked()
    {
        var world = World();
        var service = Clean(world).Grant(Upload, Link);
        var document = await UploadAsync(service);

        world.TargetReadable.Remove((CompanyA, Order));
        var target = await Refused(service.Service.LinkAsync(document.Id, new LinkDocumentRequest(OrderKind, Order, 1),
            service.Metadata("link-target")));
        world.TargetReadable.Add((CompanyA, Order));
        world.Readable.Remove((CompanyA, Party));
        var source = await Refused(service.Service.LinkAsync(document.Id, new LinkDocumentRequest(OrderKind, Order, 1),
            service.Metadata("link-source")));

        Assert.Equal(404, target.Status);
        Assert.Equal(404, source.Status);
        Assert.Single(service.Store.LinksOf(CompanyA, document.Id));
        Assert.DoesNotContain(world.Attempts, attempt => attempt.Command == DocumentCommands.LinkV1);
    }

    [Fact]
    public async Task Owner_access_is_measured_again_immediately_before_the_write()
    {
        var world = World();
        var service = Clean(world).Grant(Upload, Link);
        var document = await UploadAsync(service);
        service.Store.BeforeWrite = () => world.TargetOutcomes[(CompanyA, Order)] = DocumentOwnerAccessV1.Denied;

        var refusal = await Refused(service.Service.LinkAsync(document.Id, new LinkDocumentRequest(OrderKind, Order, 1),
            service.Metadata("link-race")));

        Assert.Equal(404, refusal.Status);
        Assert.Single(service.Store.LinksOf(CompanyA, document.Id));
        Assert.DoesNotContain(world.Attempts, attempt => attempt.Command == DocumentCommands.LinkV1);
    }

    [Fact]
    public async Task Another_company_cannot_link_or_see_the_document()
    {
        var world = World();
        world.TargetReadable.Add((CompanyB, Order));
        var owner = Clean(world).Grant(Upload, Link);
        var document = await UploadAsync(owner);

        var other = new Harness(CompanyB, world: world).Grant(Link, Read);
        var refusal = await Refused(other.Service.LinkAsync(document.Id, new LinkDocumentRequest(OrderKind, Order, 1),
            other.Metadata("link-other-company")));

        Assert.Equal(404, refusal.Status);
        Assert.Single(owner.Store.LinksOf(CompanyA, document.Id));
        Assert.Empty((await other.Service.ListAsync(OrderKind, Order)).Items);
    }

    [Fact]
    public async Task Read_is_the_union_of_links_and_a_new_version_needs_every_linked_record()
    {
        var world = World();
        var service = Clean(world).Grant(Upload, Read, Link);
        var document = await UploadAsync(service);
        await service.Service.LinkAsync(document.Id, new LinkDocumentRequest(OrderKind, Order, 1), service.Metadata("link-1"));

        // Only the second record is readable now: the file is still readable through it.
        world.Readable.Remove((CompanyA, Party));
        Assert.Equal(V1, (await service.Service.GetContentAsync(document.Id)).Content);
        var history = await service.Service.ListVersionsAsync(document.Id, null, null);
        Assert.Equal((OrderKind, Order), (history.Versions.Single().OwnerKind, history.Versions.Single().OwnerId));

        // A new version changes the file on every linked record, so it needs every linked record.
        var append = await Refused(service.Service.AppendVersionAsync(document.Id,
            new AppendDocumentVersionRequest(1, "v2.pdf", "application/pdf", V2), service.Metadata("append-v2")));
        Assert.Equal(404, append.Status);
        Assert.Single(world.Versions);

        world.Readable.Add((CompanyA, Party));
        var appended = await service.Service.AppendVersionAsync(document.Id,
            new AppendDocumentVersionRequest(1, "v2.pdf", "application/pdf", V2), service.Metadata("append-v2-ok"));
        Assert.Equal(2, appended.Document.VersionNo);
        Assert.Equal(V2, (await service.Service.GetContentAsync(document.Id)).Content);
        Assert.Equal(2, service.Store.LinksOf(CompanyA, document.Id).Count);
    }

    [Fact]
    public async Task No_readable_link_is_not_found_for_every_read()
    {
        var world = World();
        var service = Clean(world).Grant(Upload, Read, Link);
        var document = await UploadAsync(service);
        await service.Service.LinkAsync(document.Id, new LinkDocumentRequest(OrderKind, Order, 1), service.Metadata("link-1"));

        world.Readable.Remove((CompanyA, Party));
        world.TargetReadable.Remove((CompanyA, Order));

        Assert.Equal(404, (await Refused(service.Service.GetContentAsync(document.Id))).Status);
        Assert.Equal(404, (await Refused(service.Service.GetVersionContentAsync(document.Id, 1))).Status);
        Assert.Equal(404, (await Refused(service.Service.ListVersionsAsync(document.Id, null, null))).Status);
    }

    private static Harness Clean(Harness.SharedWorld world) =>
        new(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world);

    private static async Task<DocumentView> UploadAsync(Harness service) =>
        (await service.Service.UploadAsync(Harness.Upload(Party, V1), service.Metadata($"create-{Guid.NewGuid():N}"))).Document;

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
