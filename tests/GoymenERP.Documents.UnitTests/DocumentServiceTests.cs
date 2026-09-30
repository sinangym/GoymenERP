using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;
using static GoymenERP.Documents.UnitTests.Harness;

namespace GoymenERP.Documents.UnitTests;

/// <summary>
/// The vault's use cases over test doubles (the real SQL behaviour is the relational acceptance's): the fences, the
/// owner's read on top of the vault's own names, the content rules, the scan's fail-closed state, the replay, the
/// AT-057 warning and the company fence on every read. SENTETIK / NOT_ENV.
/// </summary>
public sealed class DocumentServiceTests
{
    private const string Read = DocumentsPermissionManifestV1.FilesRead;
    private const string Upload = DocumentsPermissionManifestV1.FilesUpload;
    private static readonly Guid Party = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly byte[] Pdf = DocumentContentRulesTests.Pdf;

    [Fact]
    public async Task An_upload_stores_the_object_once_and_one_document_linked_to_the_party_and_it_downloads_byte_for_byte()
    {
        var world = World();
        var a = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Read, Upload);

        var uploaded = await a.Service.UploadAsync(Harness.Upload(Party, Pdf), a.Metadata("k-1"));

        var document = uploaded.Document;
        Assert.False(uploaded.IsReplay);
        Assert.Empty(uploaded.Duplicates);
        Assert.Equal((CompanyA, DocumentOwnerLinkKindsV1.CounterpartyParty, Party, 1, "Sözleşme.pdf", "application/pdf", (long)Pdf.Length),
            (document.CompanyId, document.OwnerKind, document.OwnerId, document.VersionNo, document.FileName, document.ContentType, document.SizeBytes));
        Assert.Equal((DocumentContentHash.Compute(Pdf), "Clean", true, Actor), (document.Sha256, document.ScanState, document.Downloadable, document.UploadedBy));
        Assert.Equal([DocumentObjectKeys.For(CompanyA, document.Sha256)], world.Objects.Objects.Keys);
        var envelope = Assert.Single(world.Envelopes);
        Assert.Equal((Upload, "Web", "k-1"), (envelope.PermissionCode, envelope.SourceChannel, envelope.IdempotencyKey));

        // "Close and reopen": a fresh request lists it and downloads the verified bytes.
        var again = new Harness(CompanyA, world: world).Grant(Read);
        var list = await again.Service.ListAsync(DocumentOwnerLinkKindsV1.CounterpartyParty, Party);
        Assert.Equal(document.Id, Assert.Single(list.Items).Id);
        Assert.False(list.CanUpload);
        Assert.Equal(4096, list.Limits.MaximumBytes);
        Assert.Equal([".pdf", ".png", ".jpg", ".jpeg"], list.Limits.Extensions);
        var content = await again.Service.GetContentAsync(document.Id);
        Assert.Equal(Pdf, content.Content);
        Assert.Equal(("application/pdf", "Sözleşme.pdf", document.Sha256), (content.ContentType, content.FileName, content.Sha256));
    }

    [Fact]
    public async Task The_vault_names_are_measured_before_anything_and_the_owner_read_is_required_on_top()
    {
        var world = World();
        // No authentication, no company access, no grant: nothing is read, nothing is stored.
        await Refused(401, new Harness(CompanyA, actor: null, world: world).Grant(Upload).Service.UploadAsync(Harness.Upload(Party, Pdf), Meta()));
        await Refused(403, new Harness(CompanyA, world: world).Service.UploadAsync(Harness.Upload(Party, Pdf), Meta()));
        await Refused(403, new Harness(CompanyA, world: world).Grant(Read).Service.UploadAsync(Harness.Upload(Party, Pdf), Meta()));
        await Refused(403, new Harness(CompanyA, world: world).Grant(Upload).Service.ListAsync(DocumentOwnerLinkKindsV1.CounterpartyParty, Party));
        await Refused(403, new Harness(CompanyA, world: world).Grant(Upload).Service.GetContentAsync(Guid.NewGuid()));

        // The vault's name without the owner's read of THIS party: not found, never a hint that the party exists.
        world.Readable.Clear();
        var owner = new Harness(CompanyA, world: world).Grant(Read, Upload);
        await Refused(404, owner.Service.UploadAsync(Harness.Upload(Party, Pdf), Meta()));
        await Refused(404, owner.Service.ListAsync(DocumentOwnerLinkKindsV1.CounterpartyParty, Party));
        Assert.Empty(world.Documents);
        Assert.Empty(world.Objects.Objects);
    }

    [Fact]
    public async Task A_grant_revoked_while_the_owner_answers_stops_the_upload_before_the_save()
    {
        var world = World();
        var a = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Upload);
        a.Authority.OnMeasure = () =>
        {
            if (a.Owner.Calls > 0)
            {
                a.Authority.Grants.Clear();
            }
        };

        await Refused(403, a.Service.UploadAsync(Harness.Upload(Party, Pdf), Meta()));
        Assert.Empty(world.Documents);
    }

    [Fact]
    public async Task A_grant_revoked_at_the_store_fence_writes_no_metadata()
    {
        var world = World();
        var a = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Upload);
        a.Store.BeforeWrite = a.Authority.Grants.Clear;

        await Refused(403, a.Service.UploadAsync(Harness.Upload(Party, Pdf), Meta()));

        Assert.Empty(world.Documents);
        Assert.Empty(world.Attempts);
        Assert.Empty(world.Envelopes);
        Assert.Single(world.Objects.Objects);
    }

    [Fact]
    public async Task Owner_visibility_lost_at_the_store_fence_writes_no_metadata()
    {
        var world = World();
        var a = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Upload);
        a.Store.BeforeWrite = world.Readable.Clear;

        await Refused(404, a.Service.UploadAsync(Harness.Upload(Party, Pdf), Meta()));

        Assert.Empty(world.Documents);
        Assert.Empty(world.Attempts);
        Assert.Empty(world.Envelopes);
        Assert.Single(world.Objects.Objects);
    }

    [Fact]
    public async Task Another_companys_document_is_not_found_by_id_and_absent_from_its_list()
    {
        var world = World();
        world.Readable.Add((CompanyB, Party));
        var a = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Read, Upload);
        var stored = (await a.Service.UploadAsync(Harness.Upload(Party, Pdf), Meta())).Document;

        var b = new Harness(CompanyB, world: world).Grant(Read, Upload);
        await Refused(404, b.Service.GetContentAsync(stored.Id));
        Assert.Empty((await b.Service.ListAsync(DocumentOwnerLinkKindsV1.CounterpartyParty, Party)).Items);
    }

    [Fact]
    public async Task A_document_whose_owner_record_the_actor_cannot_read_is_not_found_even_with_the_vault_name()
    {
        var world = World();
        var a = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Read, Upload);
        var stored = (await a.Service.UploadAsync(Harness.Upload(Party, Pdf), Meta())).Document;

        world.Readable.Clear();
        await Refused(404, new Harness(CompanyA, world: world).Grant(Read).Service.GetContentAsync(stored.Id));
    }

    [Theory]
    [InlineData("setup.exe", "application/octet-stream", DocumentRuleCodes.TypeNotAllowed)]
    [InlineData("sayfa.html", "text/html", DocumentRuleCodes.TypeNotAllowed)]
    [InlineData("resim.png", "image/png", DocumentRuleCodes.ContentMismatch)]
    [InlineData("belge.pdf", "text/html", DocumentRuleCodes.ContentMismatch)]
    public async Task A_dangerous_or_disguised_file_is_refused_before_it_is_scanned_or_stored(string name, string declared, string code)
    {
        var world = World();
        var scanner = new FixedScanner(DocumentScanVerdictV1.Clean);
        var a = new Harness(CompanyA, scanner, world: world).Grant(Upload);

        var refusal = await Refused(422, a.Service.UploadAsync(Harness.Upload(Party, Pdf, name, declared), Meta()));

        Assert.Equal(code, refusal.Code);
        Assert.Equal(0, scanner.Scans);
        Assert.Empty(world.Objects.Objects);
        Assert.Empty(world.Documents);
    }

    [Fact]
    public async Task Empty_oversized_unnamed_and_unknown_kind_uploads_are_refused_with_their_codes()
    {
        var a = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), maximumBytes: 16, world: World()).Grant(Upload);

        Assert.Equal(DocumentRuleCodes.Empty, (await Refused(422, a.Service.UploadAsync(Harness.Upload(Party, []), Meta()))).Code);
        Assert.Equal(DocumentRuleCodes.TooLarge, (await Refused(422, a.Service.UploadAsync(Harness.Upload(Party, [.. Pdf, .. new byte[16]]), Meta()))).Code);
        var unnamed = await Refused(400, a.Service.UploadAsync(Harness.Upload(Party, Pdf, "   "), Meta()));
        Assert.Equal(DocumentRuleCodes.FileNameInvalid, Assert.Single(unnamed.Errors).Code);
        var unknownKind = await Refused(400, a.Service.UploadAsync(Harness.Upload(Party, Pdf) with { OwnerKind = "sales.order" }, Meta()));
        Assert.Equal("ownerKind", Assert.Single(unknownKind.Errors).Field);
        var emptyOwner = await Refused(400, a.Service.UploadAsync(Harness.Upload(Guid.Empty, Pdf), Meta()));
        Assert.Equal("ownerId", Assert.Single(emptyOwner.Errors).Field);
    }

    [Fact]
    public async Task Malware_is_refused_and_nothing_is_stored()
    {
        var world = World();
        var a = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Infected), world: world).Grant(Upload);

        var refusal = await Refused(422, a.Service.UploadAsync(Harness.Upload(Party, Pdf), Meta()));

        Assert.Equal(DocumentProblemCodesV1.ContentRejected, refusal.Code);
        Assert.Empty(world.Objects.Objects);
        Assert.Empty(world.Documents);
        Assert.Empty(world.Attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Without_a_scanner_answer_the_version_is_stored_not_scanned_and_is_never_served(bool scannerUnavailable)
    {
        var world = World();
        var a = new Harness(CompanyA, scannerUnavailable ? new FixedScanner(DocumentScanVerdictV1.Unavailable) : null, world: world).Grant(Read, Upload);

        var document = (await a.Service.UploadAsync(Harness.Upload(Party, Pdf), Meta())).Document;

        Assert.Equal(("NotScanned", false), (document.ScanState, document.Downloadable));
        Assert.Null(Assert.Single(world.Documents).ScannerId);
        var refusal = await Refused(409, new Harness(CompanyA, world: world).Grant(Read).Service.GetContentAsync(document.Id));
        Assert.Equal(DocumentProblemCodesV1.NotScanned, refusal.Code);
    }

    [Fact]
    public async Task Without_an_object_store_the_upload_and_the_download_are_unavailable_and_nothing_is_recorded()
    {
        var world = World();
        var a = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), withObjects: false, world: world).Grant(Read, Upload);
        await Refused(503, a.Service.UploadAsync(Harness.Upload(Party, Pdf), Meta()));
        Assert.Empty(world.Documents);

        world.Objects.Fail = true;
        await Refused(503, new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Upload)
            .Service.UploadAsync(Harness.Upload(Party, Pdf), Meta()));
        Assert.Empty(world.Documents);
    }

    [Fact]
    public async Task Bytes_that_no_longer_match_the_recorded_hash_are_never_served()
    {
        var world = World();
        var a = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Read, Upload);
        var document = (await a.Service.UploadAsync(Harness.Upload(Party, Pdf), Meta())).Document;

        var key = Assert.Single(world.Objects.Objects.Keys);
        world.Objects.Objects[key] = [.. Pdf.Select(value => (byte)(value ^ 1))];
        await Refused(503, new Harness(CompanyA, world: world).Grant(Read).Service.GetContentAsync(document.Id));

        world.Objects.Objects.Remove(key);
        await Refused(503, new Harness(CompanyA, world: world).Grant(Read).Service.GetContentAsync(document.Id));
    }

    [Fact]
    public async Task The_same_key_answers_the_stored_document_again_and_another_body_or_actor_under_it_is_refused()
    {
        var world = World();
        var a = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Upload);
        var first = await a.Service.UploadAsync(Harness.Upload(Party, Pdf), a.Metadata("k-same"));

        var retry = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Upload);
        var replay = await retry.Service.UploadAsync(Harness.Upload(Party, Pdf), retry.Metadata("k-same"));
        Assert.True(replay.IsReplay);
        Assert.Equal((first.Document.Id, a.Correlation.CorrelationId), (replay.Document.Id, replay.OriginalCorrelationId));
        Assert.Single(world.Documents);

        var otherBody = await Refused(409, retry.Service.UploadAsync(Harness.Upload(Party, Pdf, "Başka.pdf"), retry.Metadata("k-same")));
        Assert.Equal(DocumentProblemCodesV1.SameKeyPayloadMismatch, otherBody.Code);
        var otherActor = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), actor: "SENTETIK-other", world: world).Grant(Upload);
        await Refused(409, otherActor.Service.UploadAsync(Harness.Upload(Party, Pdf), otherActor.Metadata("k-same")));
        Assert.Single(world.Documents);
    }

    [Fact]
    public async Task A_stored_replay_rechecks_upload_authority_and_owner_before_disclosure()
    {
        var world = World();
        var writer = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Upload);
        await writer.Service.UploadAsync(Harness.Upload(Party, Pdf), writer.Metadata("k-replay-fence"));

        var revoked = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Upload);
        revoked.Authority.OnMeasure = () =>
        {
            if (revoked.Authority.Measurements == 2)
            {
                revoked.Authority.Grants.Clear();
            }
        };
        await Refused(403, revoked.Service.UploadAsync(Harness.Upload(Party, Pdf), revoked.Metadata("k-replay-fence")));

        world.Readable.Clear();
        var hidden = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Upload);
        await Refused(404, hidden.Service.UploadAsync(Harness.Upload(Party, Pdf), hidden.Metadata("k-replay-fence")));

        Assert.Single(world.Documents);
        Assert.Single(world.Attempts);
        Assert.Single(world.Objects.Objects);
    }

    [Fact]
    public async Task The_same_content_again_is_stored_as_a_new_document_with_a_warning_naming_only_readable_duplicates()
    {
        var world = World();
        var otherParty = Guid.NewGuid();
        var hiddenParty = Guid.NewGuid();
        world.Readable.Add((CompanyA, otherParty));
        world.Readable.Add((CompanyA, hiddenParty));
        var scanner = new FixedScanner(DocumentScanVerdictV1.Clean);
        var first = (await new Harness(CompanyA, scanner, world: world).Grant(Upload).Service.UploadAsync(Harness.Upload(Party, Pdf, "İlk.pdf"), Meta()))
            .Document;
        var onOther = (await new Harness(CompanyA, scanner, world: world).Grant(Upload)
            .Service.UploadAsync(Harness.Upload(otherParty, Pdf, "Diğer.pdf"), Meta())).Document;
        var onHidden = (await new Harness(CompanyA, scanner, world: world).Grant(Upload)
            .Service.UploadAsync(Harness.Upload(hiddenParty, Pdf, "Gizli.pdf"), Meta())).Document;
        // The same bytes in company B are B's own; A never hears of them.
        world.Readable.Add((CompanyB, Party));
        await new Harness(CompanyB, scanner, world: world).Grant(Upload).Service.UploadAsync(Harness.Upload(Party, Pdf), Meta());

        world.Readable.Remove((CompanyA, hiddenParty));
        var again = await new Harness(CompanyA, scanner, world: world).Grant(Upload).Service.UploadAsync(Harness.Upload(Party, Pdf, "Tekrar.pdf"), Meta());

        Assert.NotEqual(first.Id, again.Document.Id);
        Assert.Equal([onOther.Id, first.Id], again.Duplicates.Select(duplicate => duplicate.DocumentId));
        Assert.DoesNotContain(again.Duplicates, duplicate => duplicate.DocumentId == onHidden.Id);
        Assert.Equal(("Diğer.pdf", otherParty), (again.Duplicates[0].FileName, again.Duplicates[0].OwnerId));
        // One object per company, however many documents share it.
        Assert.Equal(2, world.Objects.Objects.Count);
        Assert.Equal(5, world.Documents.Count);
    }

    [Fact]
    public async Task The_list_offers_the_upload_only_with_its_own_name_and_answers_newest_first()
    {
        var world = World();
        var scanner = new FixedScanner(DocumentScanVerdictV1.Clean);
        var writer = new Harness(CompanyA, scanner, world: world).Grant(Read, Upload);
        await writer.Service.UploadAsync(Harness.Upload(Party, Pdf, "Bir.pdf"), Meta());
        await Task.Delay(5);
        await writer.Service.UploadAsync(Harness.Upload(Party, DocumentContentRulesTests.Png, "İki.png", "image/png"), Meta());

        var both = await new Harness(CompanyA, world: world).Grant(Read, Upload).Service.ListAsync(DocumentOwnerLinkKindsV1.CounterpartyParty, Party);
        Assert.True(both.CanUpload);
        Assert.Equal(["İki.png", "Bir.pdf"], both.Items.Select(item => item.FileName));
        Assert.Equal(["image/png", "application/pdf"], both.Items.Select(item => item.ContentType));

        var readOnly = await new Harness(CompanyA, world: world).Grant(Read).Service.ListAsync(DocumentOwnerLinkKindsV1.CounterpartyParty, Party);
        Assert.False(readOnly.CanUpload);
        await Refused(400, new Harness(CompanyA, world: world).Grant(Read).Service.ListAsync("sales.order", Party));
    }

    private static Harness.SharedWorld World()
    {
        var world = new Harness.SharedWorld();
        world.Readable.Add((CompanyA, Party));
        return world;
    }

    private static DocumentCommandMetadata Meta() => new($"k-{Guid.NewGuid():N}", $"SENTETIK-{Guid.NewGuid():N}", null);

    private static async Task<DocumentServiceException> Refused<T>(int status, Task<T> call)
    {
        var refusal = await Assert.ThrowsAsync<DocumentServiceException>(() => call);
        Assert.Equal(status, refusal.Status);
        return refusal;
    }
}
