using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;
using static GoymenERP.Documents.UnitTests.Harness;

namespace GoymenERP.Documents.UnitTests;

/// <summary>
/// F-026 (PLT-S9): the generated-document port over test doubles. A producing module keeps the PDF it rendered as a new
/// logical document of its own owner record, with the upload's fences (fresh authority, the owner's readable answer, the
/// PDF allowlist and signature, ADR-011 replay) plus the DEC-DOC-010 template snapshot; a refusal is an outcome, never an
/// exception. KLN-D59: the authority is the producing command's own permission that the owner kind declared, never
/// <c>documents.files.upload</c>. KLN-D60: the PDF is not scanned and is kept as a system-generated version. SENTETIK / NOT_ENV.
/// </summary>
public sealed class GeneratedDocumentStoreTests
{
    private const string Upload = DocumentsPermissionManifestV1.FilesUpload;
    private const string Producer = Harness.ProducerPermission;
    private const string Read = DocumentsPermissionManifestV1.FilesRead;
    private const string TemplateCode = "inventory.material-label-pdf";
    private static readonly Guid Label = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly byte[] Pdf = DocumentContentRulesTests.Pdf;

    [Fact]
    public async Task A_generated_pdf_is_stored_once_as_a_new_document_primarily_linked_to_its_owner_with_the_template_snapshot()
    {
        var world = World();
        var scanner = new FixedScanner(DocumentScanVerdictV1.Clean);
        var a = new Harness(CompanyA, scanner, world: world).Grant(Producer);

        var result = await a.Generated.StoreAsync(Request("g-1"));

        Assert.Equal(GeneratedDocumentStoreOutcomeV1.Stored, result.Outcome);
        Assert.True(result.IsStored);
        Assert.Null(result.Code);
        var document = Assert.Single(world.Documents);
        Assert.Equal(document.Id, result.DocumentId);
        Assert.Equal((1, DocumentContentHash.Compute(Pdf), "SystemGenerated", true), (result.VersionNo, result.Sha256, result.ScanState, result.Downloadable));
        Assert.Equal(DocumentScanState.SystemGenerated, document.ScanState);
        Assert.Null(document.ScannerId);
        Assert.Equal(0, scanner.Scans);
        Assert.Equal((CompanyA, DocumentOwnerLinkKindsV1.InventoryMaterialLabel, Label, "Malzeme Etiketi.pdf", DocumentFileKind.Pdf),
            (document.CompanyId, document.OwnerKind, document.OwnerId, document.FileName, document.Kind));
        Assert.Equal([new StoredDocumentLink(DocumentOwnerLinkKindsV1.InventoryMaterialLabel, Label)], a.Store.LinksOf(CompanyA, document.Id));
        Assert.Equal([DocumentObjectKeys.For(CompanyA, document.Sha256)], world.Objects.Objects.Keys);

        var attempt = Assert.Single(world.Attempts);
        Assert.Equal((CompanyA, DocumentCommands.StoreGeneratedV1, "g-1", Actor, document.Id),
            (attempt.Company, attempt.Command, attempt.Key, attempt.Actor, attempt.DocumentId));
        var template = world.Templates[(CompanyA, document.Id)];
        Assert.Equal((TemplateCode, "v1"), (template.Code, template.Version));
        Assert.Equal(ProducerCommand, world.ProducerCommands[(CompanyA, document.Id)]);
        var envelope = Assert.Single(world.Envelopes);
        Assert.Equal((Producer, Actor, "g-1"), (envelope.PermissionCode, envelope.ActorId, envelope.IdempotencyKey));
    }

    [Fact]
    public async Task The_same_key_and_request_replays_the_first_document_and_another_template_under_the_key_is_a_conflict()
    {
        var world = World();
        var a = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Producer);
        var first = await a.Generated.StoreAsync(Request("g-replay", correlation: "SENTETIK-first"));

        var again = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Producer);
        var replay = await again.Generated.StoreAsync(Request("g-replay", correlation: "SENTETIK-second"));

        Assert.Equal(GeneratedDocumentStoreOutcomeV1.Replayed, replay.Outcome);
        Assert.True(replay.IsStored);
        Assert.Equal(first.DocumentId, replay.DocumentId);
        Assert.Equal("SENTETIK-first", replay.OriginalCorrelationId);
        Assert.Single(world.Documents);
        Assert.Single(world.Attempts);

        var otherTemplate = await again.Generated.StoreAsync(Request("g-replay", templateVersion: "v2"));
        var otherPdf = await again.Generated.StoreAsync(Request("g-replay", pdf: [.. Pdf, .. "%SENTETIK-2\n"u8.ToArray()]));
        var otherCommand = await again.Generated.StoreAsync(Request("g-replay", producerCommand: "sentetik.material-label.reprint"));

        Assert.Equal((GeneratedDocumentStoreOutcomeV1.IdempotencyConflict, DocumentProblemCodesV1.SameKeyPayloadMismatch),
            (otherTemplate.Outcome, otherTemplate.Code));
        Assert.Equal(GeneratedDocumentStoreOutcomeV1.IdempotencyConflict, otherPdf.Outcome);
        Assert.Equal(GeneratedDocumentStoreOutcomeV1.IdempotencyConflict, otherCommand.Outcome);
        Assert.Null(otherTemplate.DocumentId);
        Assert.Single(world.Documents);
        Assert.Single(world.Attempts);
    }

    [Fact]
    public async Task A_generated_key_and_an_upload_key_with_the_same_text_are_separate_attempts()
    {
        var world = World();
        world.Readable.Add((CompanyA, Label));
        var a = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Upload, Producer);

        await a.Service.UploadAsync(Harness.Upload(Label, Pdf), a.Metadata("shared-key"));
        var generated = await a.Generated.StoreAsync(Request("shared-key"));

        Assert.Equal(GeneratedDocumentStoreOutcomeV1.Stored, generated.Outcome);
        Assert.Equal(2, world.Documents.Count);
        Assert.Equal([DocumentCommands.UploadV1, DocumentCommands.StoreGeneratedV1], world.Attempts.Select(attempt => attempt.Command));
    }

    [Fact]
    public async Task The_producer_permission_is_measured_fresh_and_the_callers_company_must_be_the_context_company()
    {
        var world = World();

        var anonymous = await new Harness(CompanyA, actor: null, world: world).Grant(Producer).Generated.StoreAsync(Request("g-anon"));
        var ungranted = await new Harness(CompanyA, world: world).Generated.StoreAsync(Request("g-none"));
        // KLN-D59: the vault's own permissions do not stand in for the producing command's permission.
        var readOnly = await new Harness(CompanyA, world: world).Grant(Read, Upload).Generated.StoreAsync(Request("g-read"));
        var otherCompany = await new Harness(CompanyA, world: world).Grant(Producer).Generated.StoreAsync(Request("g-b") with { CompanyId = CompanyB });
        var emptyCompany = await new Harness(CompanyA, world: world).Grant(Producer).Generated.StoreAsync(Request("g-empty") with { CompanyId = Guid.Empty });

        Assert.Equal(GeneratedDocumentStoreOutcomeV1.Unauthenticated, anonymous.Outcome);
        Assert.Equal(
            [GeneratedDocumentStoreOutcomeV1.Forbidden, GeneratedDocumentStoreOutcomeV1.Forbidden, GeneratedDocumentStoreOutcomeV1.Forbidden,
                GeneratedDocumentStoreOutcomeV1.Forbidden],
            new[] { ungranted, readOnly, otherCompany, emptyCompany }.Select(result => result.Outcome));
        Assert.All(new[] { anonymous, ungranted, readOnly, otherCompany, emptyCompany }, result => Assert.False(result.IsStored));
        Assert.Empty(world.Documents);
        Assert.Empty(world.Attempts);
        Assert.Empty(world.Objects.Objects);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" sentetik.labels.print")]
    [InlineData("SENTETIK.LABELS.PRINT")]
    [InlineData("sentetik.labels.reprint")]
    [InlineData(DocumentsPermissionManifestV1.FilesUpload)]
    public async Task A_producer_permission_the_owner_kind_did_not_declare_is_forbidden_even_when_granted(string? permission)
    {
        var world = World();
        var a = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Producer, Upload);
        if (!string.IsNullOrWhiteSpace(permission))
        {
            a.Grant(permission);
        }

        var result = await a.Generated.StoreAsync(Request("g-undeclared", producerPermission: permission));

        Assert.Equal((GeneratedDocumentStoreOutcomeV1.Forbidden, GeneratedDocumentStoreCodesV1.ProducerNotDeclared), (result.Outcome, result.Code));
        Assert.Equal(0, a.GeneratedTarget.Calls);
        Assert.Empty(world.Documents);
        Assert.Empty(world.Attempts);
        Assert.Empty(world.Objects.Objects);
    }

    [Fact]
    public void An_owner_port_declares_no_producer_permission_unless_it_says_so()
    {
        IDocumentOwnerLinkPortV1 silent = new SilentOwnerPort();
        var harness = new Harness(CompanyA);

        Assert.Empty(silent.GeneratedDocumentProducerPermissions);
        Assert.False(silent.AcceptsGeneratedDocuments);
        Assert.Equal([Producer], harness.GeneratedTarget.GeneratedDocumentProducerPermissions);
        Assert.Empty(harness.Owner.GeneratedDocumentProducerPermissions);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("print")]
    [InlineData("Sentetik.Label.Print")]
    [InlineData("sentetik.material-label.print ")]
    [InlineData("sentetik..print")]
    [InlineData("sentetik.material_label.print")]
    public async Task A_missing_or_malformed_producer_command_is_an_invalid_request(string? command)
    {
        var world = World();
        var a = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Producer);

        var result = await a.Generated.StoreAsync(Request("g-command", producerCommand: command));
        var tooLong = await a.Generated.StoreAsync(Request("g-command-long", producerCommand: "sentetik." + new string('a', 120)));

        Assert.Equal((GeneratedDocumentStoreOutcomeV1.InvalidRequest, DocumentProblemCodesV1.ValidationFailed), (result.Outcome, result.Code));
        Assert.Equal(GeneratedDocumentStoreOutcomeV1.InvalidRequest, tooLong.Outcome);
        Assert.Empty(world.Documents);
        Assert.Empty(world.Objects.Objects);
    }

    [Theory]
    [InlineData("inventory.material-label.print", true)]
    [InlineData("sales.order-pdf.render", true)]
    [InlineData("sales", false)]
    [InlineData("sales.", false)]
    [InlineData(".sales.print", false)]
    [InlineData("1sales.print", false)]
    public void The_producer_command_is_a_lowercase_dotted_technical_name(string value, bool valid)
    {
        var command = GeneratedDocumentProducerCommand.TryCreate(value);

        Assert.Equal(valid, command is not null);
        if (command is not null)
        {
            Assert.Equal(value, command.Value);
        }
    }

    [Fact]
    public async Task Only_an_owner_kind_that_opted_in_takes_generated_documents()
    {
        var world = World();
        world.TargetReadable.Add((CompanyA, Label));
        var a = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Producer);

        // A registered kind that did not opt in (the Manufacturing order takes existing links only; the official sales
        // invoice takes neither, FNS-D36/D38) and a kind no owner declared.
        var notOptedIn = await a.Generated.StoreAsync(Request("g-mo", ownerKind: DocumentOwnerLinkKindsV1.ManufacturingOrder));
        var invoice = await a.Generated.StoreAsync(Request("g-inv", ownerKind: DocumentOwnerLinkKindsV1.FinanceOfficialSalesInvoice));
        var undeclared = await a.Generated.StoreAsync(Request("g-so", ownerKind: DocumentOwnerLinkKindsV1.SalesOrder));

        Assert.Equal((GeneratedDocumentStoreOutcomeV1.OwnerNotAccepted, GeneratedDocumentStoreCodesV1.OwnerNotAccepted),
            (notOptedIn.Outcome, notOptedIn.Code));
        Assert.Equal(GeneratedDocumentStoreOutcomeV1.OwnerNotAccepted, invoice.Outcome);
        Assert.Equal(GeneratedDocumentStoreOutcomeV1.InvalidRequest, undeclared.Outcome);
        Assert.Equal(0, a.LinkTarget.Calls);
        Assert.Equal(0, a.ClosedTarget.Calls);
        Assert.Empty(world.Documents);
        Assert.Empty(world.Objects.Objects);
    }

    [Fact]
    public async Task An_owner_record_the_actor_cannot_read_is_not_found_and_nothing_is_kept()
    {
        var world = new Harness.SharedWorld();
        world.GeneratedReadable.Add((CompanyB, Label));
        var a = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Producer);

        var hidden = await a.Generated.StoreAsync(Request("g-hidden"));

        Assert.Equal((GeneratedDocumentStoreOutcomeV1.OwnerNotFound, "not_found"), (hidden.Outcome, hidden.Code));
        Assert.Empty(world.Documents);
        Assert.Empty(world.Attempts);
        Assert.Empty(world.Objects.Objects);
    }

    [Theory]
    [InlineData("Etiket.png")]
    [InlineData("Etiket")]
    [InlineData("")]
    public async Task A_file_name_that_is_not_a_pdf_name_is_an_invalid_request(string fileName)
    {
        var world = World();
        var a = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Producer);

        var result = await a.Generated.StoreAsync(Request("g-name", fileName: fileName));

        Assert.Equal(GeneratedDocumentStoreOutcomeV1.InvalidRequest, result.Outcome);
        Assert.Empty(world.Documents);
        Assert.Empty(world.Objects.Objects);
    }

    [Fact]
    public async Task Bytes_that_are_not_a_pdf_or_are_empty_are_an_invalid_request()
    {
        var world = World();
        var a = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Producer);

        var png = await a.Generated.StoreAsync(Request("g-png", pdf: DocumentContentRulesTests.Png));
        var empty = await a.Generated.StoreAsync(Request("g-empty", pdf: []));
        var tooLarge = await a.Generated.StoreAsync(Request("g-large", pdf: [.. Pdf, .. new byte[4096]]));

        Assert.Equal((GeneratedDocumentStoreOutcomeV1.InvalidRequest, DocumentRuleCodes.ContentMismatch), (png.Outcome, png.Code));
        Assert.Equal(GeneratedDocumentStoreOutcomeV1.InvalidRequest, empty.Outcome);
        Assert.Equal(GeneratedDocumentStoreOutcomeV1.InvalidRequest, tooLarge.Outcome);
        Assert.Empty(world.Documents);
        Assert.Empty(world.Objects.Objects);
    }

    [Theory]
    [InlineData(null, "v1")]
    [InlineData("", "v1")]
    [InlineData("Inventory.Label", "v1")]
    [InlineData("label", "v1")]
    [InlineData(TemplateCode, null)]
    [InlineData(TemplateCode, "")]
    [InlineData(TemplateCode, ".v1")]
    [InlineData(TemplateCode, "v 1")]
    public async Task A_missing_or_malformed_template_snapshot_is_an_invalid_request(string? code, string? version)
    {
        var world = World();
        var a = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Producer);

        var result = await a.Generated.StoreAsync(Request("g-template") with { TemplateCode = code, TemplateVersion = version });

        Assert.Equal((GeneratedDocumentStoreOutcomeV1.InvalidRequest, DocumentProblemCodesV1.ValidationFailed), (result.Outcome, result.Code));
        Assert.Empty(world.Documents);
    }

    [Theory]
    [InlineData("", "SENTETIK-correlation", null)]
    [InlineData(" g-1", "SENTETIK-correlation", null)]
    [InlineData("g-1,g-2", "SENTETIK-correlation", null)]
    [InlineData("g-1", "", null)]
    [InlineData("g-1", "SENTETIK-correlation", " ")]
    public async Task Command_metadata_follows_the_route_rules_without_a_route_in_front(string key, string correlation, string? causation)
    {
        var world = World();
        var a = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Producer);

        var result = await a.Generated.StoreAsync(Request(key, correlation: correlation) with { CausationId = causation });
        var tooLong = await a.Generated.StoreAsync(Request(new string('k', 129)));

        Assert.Equal((GeneratedDocumentStoreOutcomeV1.InvalidRequest, DocumentProblemCodesV1.CommandMetadataInvalid), (result.Outcome, result.Code));
        Assert.Equal(GeneratedDocumentStoreOutcomeV1.InvalidRequest, tooLong.Outcome);
        Assert.Empty(world.Documents);
    }

    [Fact]
    public async Task A_system_generated_pdf_is_never_handed_to_the_scanner_and_is_kept_with_its_sha256()
    {
        var world = World();
        var scanner = new FixedScanner(DocumentScanVerdictV1.Infected);
        var a = new Harness(CompanyA, scanner, world: world).Grant(Producer);

        var result = await a.Generated.StoreAsync(Request("g-unscanned-by-design"));

        Assert.Equal((GeneratedDocumentStoreOutcomeV1.Stored, "SystemGenerated"), (result.Outcome, result.ScanState));
        Assert.Equal(0, scanner.Scans);
        var document = Assert.Single(world.Documents);
        Assert.Equal((DocumentContentHash.Compute(Pdf), DocumentScanState.SystemGenerated), (document.Sha256, document.ScanState));
        Assert.Null(document.ScannerId);
        Assert.Single(world.Objects.Objects);
    }

    [Fact]
    public async Task Without_a_scanner_the_generated_pdf_is_kept_as_system_generated_and_is_downloadable()
    {
        var world = World();
        var a = new Harness(CompanyA, scanner: null, world: world).Grant(Producer, Read);

        var result = await a.Generated.StoreAsync(Request("g-no-scanner"));

        Assert.Equal(GeneratedDocumentStoreOutcomeV1.Stored, result.Outcome);
        Assert.Equal(("SystemGenerated", true), (result.ScanState, result.Downloadable));
        var document = Assert.Single(world.Documents);
        Assert.Null(document.ScannerId);
        var content = await a.Service.GetContentAsync(document.Id);
        Assert.Equal((Pdf, document.Sha256), (content.Content, content.Sha256));
    }

    [Fact]
    public async Task A_user_upload_without_a_scanner_still_fails_closed_as_not_scanned()
    {
        var world = World();
        world.Readable.Add((CompanyA, Label));
        var a = new Harness(CompanyA, scanner: null, world: world).Grant(Upload, Read);

        var uploaded = await a.Service.UploadAsync(Harness.Upload(Label, Pdf), a.Metadata("u-no-scanner"));

        var document = Assert.Single(world.Documents);
        Assert.Equal(DocumentScanState.NotScanned, document.ScanState);
        Assert.Equal((uploaded.Document.Id, "NotScanned", false), (document.Id, uploaded.Document.ScanState, uploaded.Document.Downloadable));
        var refusal = await Assert.ThrowsAsync<DocumentServiceException>(() => a.Service.GetContentAsync(document.Id));
        Assert.Equal((409, DocumentProblemCodesV1.NotScanned), (refusal.Status, refusal.Code));
    }

    [Fact]
    public async Task A_grant_revoked_at_the_store_fence_writes_no_metadata()
    {
        var world = World();
        var a = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Producer);
        a.Store.BeforeWrite = a.Authority.Grants.Clear;

        var result = await a.Generated.StoreAsync(Request("g-revoked"));

        Assert.Equal(GeneratedDocumentStoreOutcomeV1.Forbidden, result.Outcome);
        Assert.Empty(world.Documents);
        Assert.Empty(world.Attempts);
        Assert.Empty(world.Envelopes);
    }

    [Fact]
    public async Task Owner_visibility_lost_after_the_object_store_or_at_the_store_fence_writes_no_metadata()
    {
        var afterPutWorld = World();
        var afterPut = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: afterPutWorld).Grant(Producer);
        afterPutWorld.Objects.AfterPut = afterPutWorld.GeneratedReadable.Clear;
        var lostAfterPut = await afterPut.Generated.StoreAsync(Request("g-lost-put"));

        var fenceWorld = World();
        var fence = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: fenceWorld).Grant(Producer);
        fence.Store.BeforeWrite = fenceWorld.GeneratedReadable.Clear;
        var lostAtFence = await fence.Generated.StoreAsync(Request("g-lost-fence"));

        Assert.Equal(GeneratedDocumentStoreOutcomeV1.OwnerNotFound, lostAfterPut.Outcome);
        Assert.Equal(GeneratedDocumentStoreOutcomeV1.OwnerNotFound, lostAtFence.Outcome);
        Assert.Empty(afterPutWorld.Documents);
        Assert.Empty(afterPutWorld.Attempts);
        Assert.Empty(fenceWorld.Documents);
        Assert.Empty(fenceWorld.Attempts);
    }

    [Fact]
    public async Task A_missing_or_failing_object_store_is_unavailable_and_claims_nothing_stored()
    {
        var noObjects = World();
        var withoutStore = await new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), withObjects: false, world: noObjects)
            .Grant(Producer).Generated.StoreAsync(Request("g-no-objects"));

        var failingWorld = World();
        failingWorld.Objects.Fail = true;
        var failing = await new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: failingWorld)
            .Grant(Producer).Generated.StoreAsync(Request("g-failing"));

        Assert.Equal((GeneratedDocumentStoreOutcomeV1.Unavailable, "service_unavailable"), (withoutStore.Outcome, withoutStore.Code));
        Assert.Equal(GeneratedDocumentStoreOutcomeV1.Unavailable, failing.Outcome);
        Assert.False(withoutStore.IsStored);
        Assert.Empty(noObjects.Documents);
        Assert.Empty(failingWorld.Documents);
    }

    [Fact]
    public async Task An_owner_port_fault_is_unavailable_but_cancellation_propagates()
    {
        var faulted = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: World()).Grant(Producer);
        faulted.GeneratedTarget.Error = new InvalidOperationException("SENTETIK-owner-fault");
        Assert.Equal(GeneratedDocumentStoreOutcomeV1.Unavailable, (await faulted.Generated.StoreAsync(Request("g-fault"))).Outcome);

        var cancelled = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: World()).Grant(Producer);
        cancelled.GeneratedTarget.Error = new OperationCanceledException();
        await Assert.ThrowsAsync<OperationCanceledException>(() => cancelled.Generated.StoreAsync(Request("g-cancel")));
    }

    [Theory]
    [InlineData("inventory.material-label-pdf", "v1", true)]
    [InlineData("sales.order-pdf", "2026.11.1", true)]
    [InlineData("sales.order_pdf", "v1", false)]
    [InlineData("sales", "v1", false)]
    [InlineData("sales.order-pdf", "v1+build", false)]
    public void The_template_snapshot_is_a_recorded_code_and_version_never_interpreted(string code, string version, bool valid)
    {
        var template = GeneratedDocumentTemplate.TryCreate(code, version);

        Assert.Equal(valid, template is not null);
        if (template is not null)
        {
            Assert.Equal((code, version), (template.Code, template.Version));
        }
    }

    private static Harness.SharedWorld World()
    {
        var world = new Harness.SharedWorld();
        world.GeneratedReadable.Add((CompanyA, Label));
        return world;
    }

    private static GeneratedDocumentStoreRequestV1 Request(string key, byte[]? pdf = null, string? templateVersion = "v1",
        string ownerKind = DocumentOwnerLinkKindsV1.InventoryMaterialLabel, string? fileName = "Malzeme Etiketi.pdf",
        string correlation = "SENTETIK-correlation", string? producerPermission = Producer, string? producerCommand = ProducerCommand) =>
        new(CompanyA, ownerKind, Label, fileName, pdf ?? Pdf, TemplateCode, templateVersion, key, correlation, null, producerPermission,
            producerCommand);

    /// <summary>An owner port that leaves every optional member at its interface default (KLN-D59: no producer declared).</summary>
    private sealed class SilentOwnerPort : IDocumentOwnerLinkPortV1
    {
        public string Kind => "sentetik.silent";

        public Task<DocumentOwnerAccessV1> CheckReadableAsync(Guid ownerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(DocumentOwnerAccessV1.NotFound);
    }
}
