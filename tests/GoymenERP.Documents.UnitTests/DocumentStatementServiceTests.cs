using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;
using static GoymenERP.Documents.UnitTests.Harness;

namespace GoymenERP.Documents.UnitTests;

/// <summary>
/// B-26 / FNS-D52 through the service: the bank-statement kinds are accepted only on a record whose owner port opted in,
/// a statement file never reaches a record that did not (not by upload, not by linking, not as a new version), and the
/// upload limits a record lists are its own. SENTETIK / NOT_ENV.
/// </summary>
public sealed class DocumentStatementServiceTests
{
    private const string Read = DocumentsPermissionManifestV1.FilesRead;
    private const string Upload = DocumentsPermissionManifestV1.FilesUpload;
    private const string Link = DocumentsPermissionManifestV1.FilesLink;
    private const string OrderKind = DocumentOwnerLinkKindsV1.ManufacturingOrder;
    private static readonly Guid Party = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
    private static readonly Guid Statement = Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee");
    private static readonly Guid Order = Guid.Parse("ffffffff-ffff-4fff-8fff-ffffffffffff");

    [Theory]
    [InlineData("ekstre.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", DocumentFileKind.Xlsx)]
    [InlineData("ekstre.sta", "text/plain", DocumentFileKind.Mt940)]
    [InlineData("ekstre.csv", "text/csv", DocumentFileKind.Csv)]
    public async Task A_statement_file_is_stored_on_an_opted_in_record_and_refused_on_the_cari_record_without_a_trace(string name,
        string declared, DocumentFileKind kind)
    {
        var world = World();
        var service = Clean(world).Grant(Upload, Read);
        var content = DocumentStatementContentRulesTests.Content(kind);

        var stored = await service.Service.UploadAsync(new UploadDocumentRequest(StatementOwnerKind, Statement, name, declared, content),
            service.Metadata($"statement-{kind}"));
        var refused = await Refused(service.Service.UploadAsync(Harness.Upload(Party, content, name, declared), service.Metadata($"cari-{kind}")));

        Assert.Equal((StatementOwnerKind, Statement, 1), (stored.Document.OwnerKind, stored.Document.OwnerId, stored.Document.VersionNo));
        Assert.Equal(DocumentContentPolicy.Info(kind).ContentType, stored.Document.ContentType);
        Assert.Equal(content, (await service.Service.GetContentAsync(stored.Document.Id)).Content);
        Assert.Equal((422, DocumentRuleCodes.TypeNotAllowed), (refused.Status, refused.Code));
        Assert.Equal([("file", DocumentRuleCodes.TypeNotAllowed)], refused.Errors.Select(error => (error.Field, error.Code)));
        Assert.Single(world.Documents);
        Assert.Single(world.Attempts);
    }

    [Fact]
    public async Task A_workbook_with_a_macro_part_is_refused_even_on_an_opted_in_record_before_anything_is_scanned_or_stored()
    {
        var world = World();
        var scanner = new FixedScanner(DocumentScanVerdictV1.Clean);
        var service = new Harness(CompanyA, scanner, world: world).Grant(Upload);

        var refused = await Refused(service.Service.UploadAsync(new UploadDocumentRequest(StatementOwnerKind, Statement, "ekstre.xlsx", null,
            DocumentStatementContentRulesTests.Workbook("xl/vbaProject.bin")), service.Metadata("macro")));

        Assert.Equal((422, DocumentRuleCodes.ContentMismatch), (refused.Status, refused.Code));
        Assert.Equal(0, scanner.Scans);
        Assert.Empty(world.Documents);
        Assert.Empty(world.Attempts);
        Assert.Equal(0, world.Objects.Puts);
    }

    [Fact]
    public async Task A_statement_file_is_not_linked_to_a_record_that_keeps_the_standard_list_but_a_pdf_on_the_statement_record_is()
    {
        var world = World();
        var service = Clean(world).Grant(Upload, Read, Link);
        var csv = (await service.Service.UploadAsync(new UploadDocumentRequest(StatementOwnerKind, Statement, "ekstre.csv", "text/csv",
            DocumentStatementContentRulesTests.Csv), service.Metadata("csv"))).Document;
        var pdf = (await service.Service.UploadAsync(new UploadDocumentRequest(StatementOwnerKind, Statement, "dekont.pdf", "application/pdf",
            DocumentContentRulesTests.Pdf), service.Metadata("pdf"))).Document;
        var attempts = world.Attempts.Count;

        var refused = await Refused(service.Service.LinkAsync(csv.Id, new LinkDocumentRequest(OrderKind, Order, 1), service.Metadata("link-csv")));
        var linked = await service.Service.LinkAsync(pdf.Id, new LinkDocumentRequest(OrderKind, Order, 1), service.Metadata("link-pdf"));

        Assert.Equal((422, DocumentProblemCodesV1.LinkNotAccepted), (refused.Status, refused.Code));
        Assert.Equal([("ownerKind", DocumentProblemCodesV1.LinkNotAccepted)], refused.Errors.Select(error => (error.Field, error.Code)));
        Assert.Single(service.Store.LinksOf(CompanyA, csv.Id));
        Assert.Equal((OrderKind, Order), (linked.Document.OwnerKind, linked.Document.OwnerId));
        Assert.Equal(2, service.Store.LinksOf(CompanyA, pdf.Id).Count);
        Assert.Equal(attempts + 1, world.Attempts.Count);
        Assert.Equal([pdf.Id], (await service.Service.ListAsync(OrderKind, Order)).Items.Select(item => item.Id));
    }

    [Fact]
    public async Task A_statement_version_is_appended_only_while_every_linked_record_accepts_its_kind()
    {
        var world = World();
        var service = Clean(world).Grant(Upload, Read, Link);
        var alone = (await service.Service.UploadAsync(new UploadDocumentRequest(StatementOwnerKind, Statement, "dekont.pdf", "application/pdf",
            DocumentContentRulesTests.Pdf), service.Metadata("alone"))).Document;
        var shared = (await service.Service.UploadAsync(new UploadDocumentRequest(StatementOwnerKind, Statement, "ortak.pdf", "application/pdf",
            [.. DocumentContentRulesTests.Pdf, 0x0A]), service.Metadata("shared"))).Document;
        await service.Service.LinkAsync(shared.Id, new LinkDocumentRequest(OrderKind, Order, 1), service.Metadata("link-shared"));
        var versions = world.Versions.Count;

        var refused = await Refused(service.Service.AppendVersionAsync(shared.Id,
            new AppendDocumentVersionRequest(1, "ekstre.csv", "text/csv", DocumentStatementContentRulesTests.Csv), service.Metadata("csv-on-shared")));
        // The kind refusal is answered before the version fence, so a stale expected version does not hide it.
        var stale = await Refused(service.Service.AppendVersionAsync(shared.Id,
            new AppendDocumentVersionRequest(7, "ekstre.csv", "text/csv", DocumentStatementContentRulesTests.Csv), service.Metadata("csv-stale")));
        var appended = await service.Service.AppendVersionAsync(alone.Id,
            new AppendDocumentVersionRequest(1, "ekstre.csv", "text/csv", DocumentStatementContentRulesTests.Csv), service.Metadata("csv-on-alone"));

        Assert.Equal((422, DocumentRuleCodes.TypeNotAllowed), (refused.Status, refused.Code));
        Assert.Equal((422, DocumentRuleCodes.TypeNotAllowed), (stale.Status, stale.Code));
        Assert.Equal((2, "text/csv"), (appended.Document.VersionNo, appended.Document.ContentType));
        Assert.Equal(versions + 1, world.Versions.Count);
        Assert.Single((await service.Service.ListVersionsAsync(shared.Id, null, null)).Versions);
    }

    [Fact]
    public async Task The_upload_limits_a_record_lists_are_its_own_accepted_kinds_under_the_same_size_bound()
    {
        var world = World();
        var service = Clean(world).Grant(Read, Upload);

        var cari = (await service.Service.ListAsync(DocumentOwnerLinkKindsV1.CounterpartyParty, Party)).Limits;
        var statement = (await service.Service.ListAsync(StatementOwnerKind, Statement)).Limits;

        Assert.Equal([".pdf", ".png", ".jpg", ".jpeg"], cari.Extensions);
        Assert.Equal(["application/pdf", "image/png", "image/jpeg"], cari.ContentTypes);
        Assert.Equal([".pdf", ".png", ".jpg", ".jpeg", ".xlsx", ".sta", ".mt940", ".940", ".txt", ".csv"], statement.Extensions);
        Assert.Equal(["application/pdf", "image/png", "image/jpeg", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "text/plain", "text/csv"], statement.ContentTypes);
        Assert.Equal(cari.MaximumBytes, statement.MaximumBytes);
    }

    private static Harness Clean(Harness.SharedWorld world) =>
        new(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world);

    private static Harness.SharedWorld World()
    {
        var world = new Harness.SharedWorld();
        world.Readable.Add((CompanyA, Party));
        world.TargetReadable.Add((CompanyA, Statement));
        world.TargetReadable.Add((CompanyA, Order));
        return world;
    }

    private static async Task<DocumentServiceException> Refused<T>(Task<T> call) =>
        await Assert.ThrowsAsync<DocumentServiceException>(() => call);
}
