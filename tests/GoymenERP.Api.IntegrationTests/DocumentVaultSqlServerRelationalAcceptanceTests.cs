using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;
using GoymenERP.Modules.Documents.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// ERP-000G / AT-065 / AT-057 on real SQL Server. Store level: an upload is ONE save (blob when new, document, version 1,
/// owner link, attempt, Web audit event); the same key answers the stored document without a second row; the same
/// content in the same company reuses its blob and the audit records how many versions already held it, while another
/// company holds its own blob; company B finds nothing of A by id, by owner or by content; the schema refuses a clean
/// version without its scanner, a version without its blob and a second blob row for the same content; the write guard
/// refuses any update or delete; the migration's rollback is refused with 51058 while a document exists. Route level (the
/// real host, the central authority, the Cari owner's read): upload, list, download and the duplicate warning for a
/// permitted actor; 403 without the vault's names; 404 without the Cari read and for another company; the bytes served
/// are the bytes stored. SENTETIK.
/// </summary>
public sealed class DocumentVaultSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid PartyA = Guid.Parse("d0c00000-0000-4000-8000-00000000000a");
    private static readonly Guid PartyB = Guid.Parse("d0c00000-0000-4000-8000-00000000000b");
    private static readonly Guid PartyShared = Guid.Parse("d0c00000-0000-4000-8000-0000000000ab");
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Pdf = "%PDF-1.7\n% SENTETIK sözleşme\n"u8.ToArray();
    private static readonly byte[] OtherPdf = "%PDF-1.7\n% SENTETIK başka belge\n"u8.ToArray();
    private static Task PermitWriteAsync(CancellationToken _) => Task.CompletedTask;

    [SqlServerFact]
    public async Task A_refused_before_write_callback_leaves_no_document_metadata_audit_or_outbox()
    {
        await using var run = await Run.StartAsync();
        var before = await run.CountsAsync();
        var auditBefore = await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'documents.document.uploaded.v1'");
        var outboxBefore = await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages");
        var invoked = false;

        await using (var a = run.Store(CompanyA))
        {
            var refusal = await Assert.ThrowsAsync<DocumentServiceException>(() => a.Store.StoreUploadAsync(
                run.Envelope("k-refused"), Hash("refused"), Upload(CompanyA, PartyA, Pdf, "Reddedilen.pdf"), _ =>
                {
                    invoked = true;
                    throw new DocumentServiceException(403, "forbidden");
                }));
            Assert.Equal((403, "forbidden"), (refusal.Status, refusal.Code));
        }

        Assert.True(invoked);
        Assert.Equal(before, await run.CountsAsync());
        Assert.Equal(auditBefore, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'documents.document.uploaded.v1'"));
        Assert.Equal(outboxBefore, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages"));
    }

    [SqlServerFact]
    public async Task An_inventory_material_label_link_is_stored_once_replayed_and_remains_company_scoped()
    {
        await using var run = await Run.StartAsync();
        var labelId = Guid.Parse("d0c00000-0000-4000-8000-00000000a065");
        DocumentStoreResult first;
        await using (var a = run.Store(CompanyA))
        {
            first = await a.Store.StoreUploadAsync(run.Envelope("k-label"), Hash("label-upload"),
                Upload(CompanyA, labelId, Pdf, "Etiket.pdf", DocumentOwnerLinkKindsV1.InventoryMaterialLabel), PermitWriteAsync);
            var replay = await a.Store.StoreUploadAsync(run.Envelope("k-label", correlation: "SENTETIK-label-retry"), Hash("label-upload"),
                Upload(CompanyA, labelId, Pdf, "Etiket.pdf", DocumentOwnerLinkKindsV1.InventoryMaterialLabel), PermitWriteAsync);

            Assert.False(first.IsReplay);
            Assert.True(replay.IsReplay);
            Assert.Equal(first.Document.Id, replay.Document.Id);
            Assert.Single(await a.Store.ListByOwnerAsync(DocumentOwnerLinkKindsV1.InventoryMaterialLabel, labelId, 10));
        }

        await using (var b = run.Store(CompanyB))
        {
            Assert.Empty(await b.Store.ListByOwnerAsync(DocumentOwnerLinkKindsV1.InventoryMaterialLabel, labelId, 10));
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM documents.DocumentLinks WHERE CompanyId = @company AND OwnerKind = @kind AND OwnerId = @owner",
            ("@company", CompanyA), ("@kind", DocumentOwnerLinkKindsV1.InventoryMaterialLabel), ("@owner", labelId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM documents.DocumentAttempts WHERE CompanyId = @company AND DocumentId = @document AND CommandName = @command",
            ("@company", CompanyA), ("@document", first.Document.Id), ("@command", DocumentCommands.UploadV1)));
    }

    [SqlServerFact]
    public async Task An_upload_is_one_save_with_its_blob_attempt_and_web_audit_and_the_same_key_replays_without_a_second_row()
    {
        await using var run = await Run.StartAsync();
        DocumentStoreResult first;
        await using (var a = run.Store(CompanyA))
        {
            first = await a.Store.StoreUploadAsync(run.Envelope("k-1"), Hash("upload-1"), Upload(CompanyA, PartyA, Pdf, "Sözleşme.pdf"), PermitWriteAsync);
            Assert.False(first.IsReplay);
            Assert.Equal((CompanyA, DocumentOwnerLinkKindsV1.CounterpartyParty, PartyA, 1, "Sözleşme.pdf", DocumentFileKind.Pdf),
                (first.Document.CompanyId, first.Document.OwnerKind, first.Document.OwnerId, first.Document.VersionNo, first.Document.FileName,
                    first.Document.Kind));
            Assert.Equal((DocumentScanState.Clean, "SENTETIK-scanner-v1"), (first.Document.ScanState, first.Document.ScannerId));
        }

        Assert.Equal((1, 1, 1, 1, 1), await run.CountsAsync());
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id AND EventType = N'documents.document.uploaded.v1' AND SourceChannel = N'Web' "
            + "AND CompanyId = @company AND AfterJson LIKE N'%\"sameContentEarlierVersions\":0%'",
            ("@id", first.Document.Id.ToString("D")), ("@company", CompanyA)));

        await using (var a = run.Store(CompanyA))
        {
            // The same key and request: the stored document with the first correlation; never a second row.
            var replay = await a.Store.StoreUploadAsync(run.Envelope("k-1", correlation: "SENTETIK-retry"), Hash("upload-1"),
                Upload(CompanyA, PartyA, Pdf, "Sözleşme.pdf"), PermitWriteAsync);
            Assert.Equal((true, first.Document.Id, run.Correlation), (replay.IsReplay, replay.Document.Id, replay.OriginalCorrelationId));
            Assert.Equal(first.Document.Id, (await a.Store.FindReplayAsync(run.Envelope("k-1"), Hash("upload-1"), DocumentCommands.UploadV1))!.Document.Id);

            // Another request or another actor under the key: one refusal, nothing written.
            var other = await Assert.ThrowsAsync<DocumentStoreException>(() => a.Store.StoreUploadAsync(run.Envelope("k-1"), Hash("upload-2"),
                Upload(CompanyA, PartyA, OtherPdf, "Başka.pdf"), PermitWriteAsync));
            Assert.Equal(DocumentStoreCodes.SameKeyPayloadMismatch, other.Code);
            await Assert.ThrowsAsync<DocumentStoreException>(() => a.Store.FindReplayAsync(run.Envelope("k-1", actor: "SENTETIK-other"), Hash("upload-1"),
                DocumentCommands.UploadV1));
        }

        Assert.Equal((1, 1, 1, 1, 1), await run.CountsAsync());
    }

    [SqlServerFact]
    public async Task The_same_content_reuses_the_companys_blob_records_the_earlier_versions_and_another_company_holds_its_own()
    {
        await using var run = await Run.StartAsync();
        Guid first;
        Guid second;
        await using (var a = run.Store(CompanyA))
        {
            first = (await a.Store.StoreUploadAsync(run.Envelope("k-a1"), Hash("a1"), Upload(CompanyA, PartyA, Pdf, "İlk.pdf"), PermitWriteAsync)).Document.Id;
            second = (await a.Store.StoreUploadAsync(run.Envelope("k-a2"), Hash("a2"), Upload(CompanyA, PartyShared, Pdf, "İkinci.pdf"), PermitWriteAsync)).Document.Id;

            // AT-057: the second document of the same content is found by content, newest first, and the first is its duplicate.
            var sameContent = await a.Store.FindByContentAsync(DocumentContentHash.Compute(Pdf), second, 10);
            Assert.Equal([first], sameContent.Select(document => document.Id));
        }

        await using (var b = run.Store(CompanyB))
        {
            await b.Store.StoreUploadAsync(run.Envelope("k-b1"), Hash("b1"), Upload(CompanyB, PartyShared, Pdf, "B.pdf"), PermitWriteAsync);
            // B's same-content lookup never sees A's documents.
            Assert.Single(await b.Store.FindByContentAsync(DocumentContentHash.Compute(Pdf), Guid.NewGuid(), 10));
        }

        // One blob per company however many documents share it; A's second upload recorded one earlier version.
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM documents.ContentBlobs WHERE CompanyId = @c", ("@c", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM documents.ContentBlobs WHERE CompanyId = @c", ("@c", CompanyB)));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM documents.Documents"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id AND AfterJson LIKE N'%\"sameContentEarlierVersions\":1%'",
            ("@id", second.ToString("D"))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId = @c AND EventType = N'documents.document.uploaded.v1' "
            + "AND AfterJson LIKE N'%\"sameContentEarlierVersions\":0%'", ("@c", CompanyB)));
    }

    [SqlServerFact]
    public async Task Company_b_finds_no_document_of_company_a_by_id_by_owner_or_by_content_and_cannot_write_into_a()
    {
        await using var run = await Run.StartAsync();
        Guid id;
        await using (var a = run.Store(CompanyA))
        {
            id = (await a.Store.StoreUploadAsync(run.Envelope("k-a"), Hash("a"), Upload(CompanyA, PartyShared, Pdf, "A.pdf"), PermitWriteAsync)).Document.Id;
        }

        await using (var b = run.Store(CompanyB))
        {
            Assert.Null(await b.Store.FindDocumentAsync(id));
            Assert.Empty(await b.Store.ListByOwnerAsync(DocumentOwnerLinkKindsV1.CounterpartyParty, PartyShared, 10));
            Assert.Empty(await b.Store.FindByContentAsync(DocumentContentHash.Compute(Pdf), Guid.NewGuid(), 10));
            // A's object key under B's context is refused before anything is written.
            var refusal = await Assert.ThrowsAsync<DocumentStoreException>(() => b.Store.StoreUploadAsync(run.Envelope("k-b"), Hash("b"),
                Upload(CompanyA, PartyShared, OtherPdf, "B.pdf"), PermitWriteAsync));
            Assert.Equal(DocumentStoreCodes.CompanyContextMismatch, refusal.Code);
            // The same key hash in B is B's own attempt space: no replay of A's document.
            Assert.Null(await b.Store.FindReplayAsync(run.Envelope("k-a"), Hash("a"), DocumentCommands.UploadV1));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM documents.Documents"));
    }

    [SqlServerFact]
    public async Task The_schema_refuses_a_clean_version_without_its_scanner_an_orphan_version_and_a_second_blob_and_nothing_is_updated_or_deleted()
    {
        await using var run = await Run.StartAsync();
        StoredDocument stored;
        await using (var a = run.Store(CompanyA))
        {
            stored = (await a.Store.StoreUploadAsync(run.Envelope("k-a"), Hash("a"), Upload(CompanyA, PartyA, Pdf, "A.pdf"), PermitWriteAsync)).Document;
        }

        async Task<int> ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            try
            {
                await run.ExecuteAsync(sql, parameters);
                return 0;
            }
            catch (SqlException exception)
            {
                return exception.Number;
            }
        }

        const string Version = "INSERT INTO documents.DocumentVersions (CompanyId, DocumentId, VersionNo, FileName, Sha256, ScanState, ScannerId, UploadedBy, "
            + "UploadedAtUtc, CorrelationId) VALUES (@c, @d, 2, N'SENTETIK.pdf', @sha, @state, @scanner, N'SENTETIK', SYSDATETIMEOFFSET(), N'SENTETIK')";
        // DEC-DOC-009: Clean names its scanner; NotScanned names none.
        Assert.Equal(547, await ExecuteAsync(Version, ("@c", CompanyA), ("@d", stored.Id), ("@sha", stored.Sha256), ("@state", 2), ("@scanner", DBNull.Value)));
        Assert.Equal(547, await ExecuteAsync(Version, ("@c", CompanyA), ("@d", stored.Id), ("@sha", stored.Sha256), ("@state", 1), ("@scanner", "x")));
        // A version of content the company does not hold, and one of another company's document.
        Assert.Equal(547, await ExecuteAsync(Version, ("@c", CompanyA), ("@d", stored.Id), ("@sha", new string('0', 64)), ("@state", 1),
            ("@scanner", DBNull.Value)));
        Assert.Equal(547, await ExecuteAsync(Version, ("@c", CompanyB), ("@d", stored.Id), ("@sha", stored.Sha256), ("@state", 1), ("@scanner", DBNull.Value)));
        // The company's content is one blob row.
        Assert.Equal(2627, await ExecuteAsync("INSERT INTO documents.ContentBlobs (CompanyId, Sha256, SizeBytes, ContentKind, ObjectKey, FirstStoredBy, "
            + "FirstStoredAtUtc) VALUES (@c, @sha, 1, 1, N'x', N'SENTETIK', SYSDATETIMEOFFSET())", ("@c", CompanyA), ("@sha", stored.Sha256)));

        // The application never updates or deletes a vault row: the write guard refuses before SQL.
        await using (var a = run.Store(CompanyA))
        {
            var version = await a.Context.Versions.SingleAsync();
            version.FileName = "Değiştirildi.pdf";
            await Assert.ThrowsAsync<InvalidOperationException>(() => a.Context.SaveChangesAsync());
        }

        await using (var a = run.Store(CompanyA))
        {
            a.Context.Links.Remove(await a.Context.Links.SingleAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => a.Context.SaveChangesAsync());
        }

        Assert.Equal((1, 1, 1, 1, 1), await run.CountsAsync());
        Assert.Equal("A.pdf", await run.ScalarAsync<string>("SELECT FileName FROM documents.DocumentVersions"));
    }

    [SqlServerFact]
    public async Task The_migration_rolls_back_an_empty_schema_and_refuses_with_51058_while_a_document_exists()
    {
        await using var run = await Run.StartAsync();
        var companyContext = new CompanyContext();
        companyContext.Set(CompanyA);
        var options = new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", DocumentsDbContext.Schema))
            .Options;
        const string TableExists = "SELECT COUNT(*) FROM sys.tables WHERE [name] = N'Documents' AND SCHEMA_NAME([schema_id]) = N'documents'";

        await using (var context = new DocumentsDbContext(options, companyContext))
        {
            Assert.Equal(["20260930120000_InitialDocuments"], await context.Database.GetAppliedMigrationsAsync());
            await context.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase);
            Assert.Equal(0, await run.ScalarAsync<int>(TableExists));
            await context.Database.MigrateAsync();
            Assert.Equal(1, await run.ScalarAsync<int>(TableExists));
        }

        await using (var a = run.Store(CompanyA))
        {
            await a.Store.StoreUploadAsync(run.Envelope("k-a"), Hash("a"), Upload(CompanyA, PartyA, Pdf, "A.pdf"), PermitWriteAsync);
        }

        await using (var context = new DocumentsDbContext(options, companyContext))
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase));
            Assert.Equal(51058, error.Number);
            Assert.Equal(["20260930120000_InitialDocuments"], await context.Database.GetAppliedMigrationsAsync());
        }

        Assert.Equal((1, 1, 1, 1, 1), await run.CountsAsync());
    }

    /// <summary>
    /// The real route through the host (central registration: the vault names in the catalogue, AddDocumentsRuntime,
    /// MapDocumentEndpoints and the initializer): upload → list → download for a permitted actor, the AT-057 warning on the
    /// same file again, the owner's read on top of the vault's names, and the company fence.
    /// </summary>
    [SqlServerFact]
    public async Task The_routes_upload_list_and_download_for_a_permitted_actor_and_answer_403_or_404_to_everyone_else()
    {
        var inventoryOwner = new InventoryOwnerFixture(Guid.Parse("d0c00000-0000-4000-8000-00000000a065"));
        await using var run = await Run.StartAsync(inventoryOwner);
        await run.SeedPartiesAsync();

        // The vault's names without the Cari read: the party is not found (never a hint that it exists), nothing is stored.
        await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, DocumentsPermissionManifestV1.FilesRead, DocumentsPermissionManifestV1.FilesUpload);
        using (var vaultOnly = run.Client(TestUsers.CompanyAViewer, CompanyA))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await vaultOnly.GetAsync(ListPath(PartyA))).StatusCode);
            using var refused = await run.UploadAsync(vaultOnly, PartyA, Pdf, "Sözleşme.pdf", "k-no-cari");
            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        }

        // The Cari read without the vault's names: 403 before anything is read.
        await run.GrantAsync(TestUsers.CompanyB, CompanyB, "counterparty.parties.read");
        using (var cariOnly = run.Client(TestUsers.CompanyB, CompanyB))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await cariOnly.GetAsync(ListPath(PartyB))).StatusCode);
            using var refused = await run.UploadAsync(cariOnly, PartyB, Pdf, "Sözleşme.pdf", "k-no-vault");
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM documents.Documents"));

        await run.GrantAsync(TestUsers.CompanyA, CompanyA, "counterparty.parties.read", DocumentsPermissionManifestV1.FilesRead,
            DocumentsPermissionManifestV1.FilesUpload);
        using var writer = run.Client(TestUsers.CompanyA, CompanyA);
        Guid documentId;
        using (var uploaded = await run.UploadAsync(writer, PartyA, Pdf, "Sözleşme.pdf", "k-route-1"))
        {
            Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
            var body = await uploaded.Content.ReadFromJsonAsync<JsonElement>();
            var document = body.GetProperty("document");
            documentId = document.GetProperty("id").GetGuid();
            Assert.Equal(("Sözleşme.pdf", "application/pdf", "Clean", true), (document.GetProperty("fileName").GetString(),
                document.GetProperty("contentType").GetString(), document.GetProperty("scanState").GetString(), document.GetProperty("downloadable").GetBoolean()));
            Assert.Equal(0, body.GetProperty("duplicates").GetArrayLength());
            Assert.False(body.GetProperty("isReplay").GetBoolean());
        }

        // The same key again: the same document, the replay headers, no second row.
        using (var replay = await run.UploadAsync(writer, PartyA, Pdf, "Sözleşme.pdf", "k-route-1"))
        {
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal("true", replay.Headers.GetValues("Idempotency-Replayed").Single());
            Assert.Equal(documentId, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("document").GetProperty("id").GetGuid());
        }

        // AT-057: the same file again under a new key is stored and warns with the earlier document.
        using (var again = await run.UploadAsync(writer, PartyA, Pdf, "Sözleşme (2).pdf", "k-route-2"))
        {
            Assert.Equal(HttpStatusCode.Created, again.StatusCode);
            var duplicate = Assert.Single((await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("duplicates").EnumerateArray());
            Assert.Equal((documentId, "Sözleşme.pdf"), (duplicate.GetProperty("documentId").GetGuid(), duplicate.GetProperty("fileName").GetString()));
        }

        // A disguised file is refused and stores nothing.
        using (var disguised = await run.UploadAsync(writer, PartyA, "<html><script>alert(1)</script></html>"u8.ToArray(), "fatura.pdf", "k-route-3"))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, disguised.StatusCode);
            Assert.Equal(DocumentRuleCodes.ContentMismatch, (await disguised.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        // "Close and reopen": the list shows both, newest first; the download is the stored bytes as an attachment.
        using (var list = await writer.GetAsync(ListPath(PartyA)))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var body = await list.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(body.GetProperty("canUpload").GetBoolean());
            Assert.Equal(["Sözleşme (2).pdf", "Sözleşme.pdf"], body.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("fileName").GetString()));
        }

        using (var download = await writer.GetAsync(ContentPath(documentId)))
        {
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal(Pdf, await download.Content.ReadAsByteArrayAsync());
            Assert.Equal("application/pdf", download.Content.Headers.ContentType!.MediaType);
            Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
            Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        }

        // Company B with both names: A's document id is not found, B's list of the shared party is empty, and a party
        // without an account in B is not B's record.
        await run.GrantAsync(TestUsers.CompanyB, CompanyB, DocumentsPermissionManifestV1.FilesRead);
        using (var readerB = run.Client(TestUsers.CompanyB, CompanyB))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await readerB.GetAsync(ContentPath(documentId))).StatusCode);
            using var listB = await readerB.GetAsync(ListPath(PartyShared));
            Assert.Equal(HttpStatusCode.OK, listB.StatusCode);
            Assert.Equal(0, (await listB.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").GetArrayLength());
            Assert.Equal(HttpStatusCode.NotFound, (await readerB.GetAsync(ListPath(PartyA))).StatusCode);
        }

        // Company A's actor holding the vault names but not the Cari read cannot download A's document either.
        using (var vaultOnly = run.Client(TestUsers.CompanyAViewer, CompanyA))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await vaultOnly.GetAsync(ContentPath(documentId))).StatusCode);
        }

        // AT-065 Inventory owner negatives use the real Documents routes and SQL store with the actual central adapter.
        // The owner producer is controlled here; R14 separately proves the real browser-created Inventory label.
        var allowedActor = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead);
        inventoryOwner.ReadableActors.Add(allowedActor);
        Guid labelDocumentId;
        using (var uploaded = await run.UploadAsync(writer, inventoryOwner.LabelId, Pdf, "Etiket.pdf", "k-label-route",
                   DocumentOwnerLinkKindsV1.InventoryMaterialLabel))
        {
            Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
            labelDocumentId = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("document").GetProperty("id").GetGuid();
        }

        var effectsBeforeNegatives = await run.CountsAsync();

        const string DocumentsDenied = TestUsers.NoAssignment;
        var documentsDeniedActor = await run.GrantAsync(DocumentsDenied, CompanyA,
            ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead);
        inventoryOwner.ReadableActors.Add(documentsDeniedActor);
        using (var documentsDenied = run.Client(DocumentsDenied, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden,
                (await documentsDenied.GetAsync(ListPath(inventoryOwner.LabelId, DocumentOwnerLinkKindsV1.InventoryMaterialLabel))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await documentsDenied.GetAsync(ContentPath(labelDocumentId))).StatusCode);
            using var refused = await run.UploadAsync(documentsDenied, inventoryOwner.LabelId, Pdf, "Reddedildi.pdf", "k-label-no-documents",
                DocumentOwnerLinkKindsV1.InventoryMaterialLabel);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        // CompanyAViewer holds both Documents names from the earlier case but is not readable by the Inventory owner.
        using (var ownerDenied = run.Client(TestUsers.CompanyAViewer, CompanyA))
        {
            Assert.Equal(HttpStatusCode.NotFound,
                (await ownerDenied.GetAsync(ListPath(inventoryOwner.LabelId, DocumentOwnerLinkKindsV1.InventoryMaterialLabel))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await ownerDenied.GetAsync(ContentPath(labelDocumentId))).StatusCode);
            using var refused = await run.UploadAsync(ownerDenied, inventoryOwner.LabelId, Pdf, "Reddedildi.pdf", "k-label-no-owner",
                DocumentOwnerLinkKindsV1.InventoryMaterialLabel);
            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        }

        await run.GrantAsync(TestUsers.CompanyB, CompanyB, DocumentsPermissionManifestV1.FilesUpload,
            ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead);
        using (var foreign = run.Client(TestUsers.CompanyB, CompanyB))
        {
            Assert.Equal(HttpStatusCode.NotFound,
                (await foreign.GetAsync(ListPath(inventoryOwner.LabelId, DocumentOwnerLinkKindsV1.InventoryMaterialLabel))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync(ContentPath(labelDocumentId))).StatusCode);
            using var refused = await run.UploadAsync(foreign, inventoryOwner.LabelId, Pdf, "Reddedildi.pdf", "k-label-foreign",
                DocumentOwnerLinkKindsV1.InventoryMaterialLabel);
            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        }

        Assert.Equal(effectsBeforeNegatives, await run.CountsAsync());

        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM documents.Documents"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM documents.ContentBlobs"));
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'documents.document.uploaded.v1' AND SourceChannel = N'Web' AND CompanyId = @c",
            ("@c", CompanyA)));
    }

    private static string ListPath(Guid party, string ownerKind = DocumentOwnerLinkKindsV1.CounterpartyParty) =>
        $"{DocumentsApiV1.DocumentsPath}?ownerKind={ownerKind}&ownerId={party:D}";

    private static string ContentPath(Guid id) => $"{DocumentsApiV1.DocumentsPath}/{id:D}/content";

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static NewDocumentUpload Upload(Guid company, Guid party, byte[] content, string fileName,
        string ownerKind = DocumentOwnerLinkKindsV1.CounterpartyParty)
    {
        var sha = DocumentContentHash.Compute(content);
        return new NewDocumentUpload(Guid.NewGuid(), DocumentOwnerLink.TryCreate(ownerKind, party)!,
            DocumentFileName.TryCreate(fileName)!, DocumentContentPolicy.Info(DocumentFileKind.Pdf), content.LongLength, sha,
            DocumentObjectKeys.For(company, sha), DocumentScanState.Clean, "SENTETIK-scanner-v1", T0);
    }

    private sealed class InventoryOwnerFixture(Guid labelId)
    {
        public Guid LabelId { get; } = labelId;
        public HashSet<Guid> ReadableActors { get; } = [];
    }

    private sealed class FixedInventoryLabelDetails(
        ICompanyContext company,
        ICurrentUser user,
        InventoryOwnerFixture fixture) : IInventoryLabelDetailsV1
    {
        public Task<InventoryLabelDetailV1> GetAsync(Guid labelId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!company.IsResolved || company.CompanyId != CompanyA || labelId != fixture.LabelId)
            {
                throw new InventoryLabelDetailException(404, InventoryLabelDetailCodesV1.NotFound);
            }

            if (!user.IsAuthenticated || !Guid.TryParse(user.UserId, out var actorId) || !fixture.ReadableActors.Contains(actorId))
            {
                throw new InventoryLabelDetailException(403, InventoryLabelDetailCodesV1.Forbidden);
            }

            return Task.FromResult(new InventoryLabelDetailV1(
                "1",
                new InventoryLabelViewV1(labelId, CompanyA, "LBL-AT065", Guid.NewGuid(), Guid.NewGuid(), null, null,
                    "Complete", [], [], [], null!),
                null!,
                null!,
                null!));
        }
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string objectRoot;

        private Run(SqlServerRelationalApiFactory factory, string objectRoot)
        {
            Factory = factory;
            this.objectRoot = objectRoot;
        }

        public SqlServerRelationalApiFactory Factory { get; }

        public string Actor { get; } = "SENTETIK-docvault-" + Guid.NewGuid().ToString("N");

        public string Correlation { get; } = $"at-065-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync(InventoryOwnerFixture? inventoryOwner = null)
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var objectRoot = Path.Combine(Path.GetTempPath(), $"goymen-docvault-sql-{Guid.NewGuid():N}");
            var factory = new SqlServerRelationalApiFactory(baseConnectionString,
                configureServices: inventoryOwner is null ? null : services =>
                {
                    services.RemoveAll<IInventoryLabelDetailsV1>();
                    services.AddScoped<IInventoryLabelDetailsV1>(provider => new FixedInventoryLabelDetails(
                        provider.GetRequiredService<ICompanyContext>(), provider.GetRequiredService<ICurrentUser>(), inventoryOwner));
                },
                configurationOverrides: new Dictionary<string, string?> { ["Documents:ObjectStore:RootPath"] = objectRoot });
            var run = new Run(factory, objectRoot);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            // The host applies the vault's migration after Organization's; applied again here idempotently.
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            await using var context = new DocumentsDbContext(Options(factory).Options, companyContext);
            await context.Database.MigrateAsync();
            return run;
        }

        public DocumentWriteEnvelope Envelope(string key, string? correlation = null, string? actor = null) =>
            new(actor ?? Actor, DocumentsPermissionManifestV1.FilesUpload, ["SENTETIK-DOCUMENTS"], correlation ?? Correlation, null, key, "Web",
                "127.0.0.1", "SENTETIK-agent");

        public StoreScope Store(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var dbContext = new DocumentsDbContext(Options(Factory).AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext)).Options,
                companyContext);
            return new StoreScope(dbContext, new DocumentsStore(dbContext, companyContext, collector));
        }

        /// <summary>SYNTHETIC Cari parties: one with an account in A, one in B, one shared by A and B.</summary>
        public Task SeedPartiesAsync() => ExecuteAsync(
            """
            INSERT INTO [counterparty].[Parties] ([Id], [Code], [Kind], [LegalName], [TradeName], [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES (@PartyA, N'CAR-965001', N'LegalEntity', N'SENTETIK Belge A', NULL, 1, 1, @Now, @Now),
                   (@PartyB, N'CAR-965002', N'LegalEntity', N'SENTETIK Belge B', NULL, 1, 1, @Now, @Now),
                   (@Shared, N'CAR-965003', N'LegalEntity', N'SENTETIK Belge Ortak', NULL, 1, 1, @Now, @Now);
            INSERT INTO [counterparty].[CompanyPartyAccounts] ([Id], [CompanyId], [PartyId], [Roles], [ExternalIntegrationCode], [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES (NEWID(), @A, @PartyA, @Customer, N'SENTETIK-D1', 1, 1, @Now, @Now),
                   (NEWID(), @B, @PartyB, @Customer, N'SENTETIK-D2', 1, 1, @Now, @Now),
                   (NEWID(), @A, @Shared, @Customer, N'SENTETIK-D3', 1, 1, @Now, @Now),
                   (NEWID(), @B, @Shared, @Customer, N'SENTETIK-D4', 1, 1, @Now, @Now);
            """,
            ("@PartyA", PartyA), ("@PartyB", PartyB), ("@Shared", PartyShared), ("@A", CompanyA), ("@B", CompanyB),
            ("@Customer", (int)PartyRole.Customer), ("@Now", T0));

        public HttpClient Client(string subject, Guid company)
        {
            var client = Factory.CreateClientFor(subject, company);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            return client;
        }

        public async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid party, byte[] content, string fileName, string key,
            string ownerKind = DocumentOwnerLinkKindsV1.CounterpartyParty)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(ownerKind), DocumentsApiV1.OwnerKindField);
            form.Add(new StringContent(party.ToString("D")), DocumentsApiV1.OwnerIdField);
            var file = new ByteArrayContent(content);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(file, DocumentsApiV1.FileField, fileName);
            using var request = new HttpRequestMessage(HttpMethod.Post, DocumentsApiV1.DocumentsPath) { Content = form };
            request.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(request);
        }

        /// <summary>A fresh synthetic role of the given names for the bootstrap subject in one company (company-wide scope).</summary>
        public async Task<Guid> GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "Synthetic document vault user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"AT-065-{roleId:N}", "Synthetic document vault role", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
            return actor;
        }

        /// <summary>(blobs, documents, versions, links, attempts) as the real SQL holds them.</summary>
        public async Task<(int Blobs, int Documents, int Versions, int Links, int Attempts)> CountsAsync() =>
            (await ScalarAsync<int>("SELECT COUNT(*) FROM documents.ContentBlobs"),
                await ScalarAsync<int>("SELECT COUNT(*) FROM documents.Documents"),
                await ScalarAsync<int>("SELECT COUNT(*) FROM documents.DocumentVersions"),
                await ScalarAsync<int>("SELECT COUNT(*) FROM documents.DocumentLinks"),
                await ScalarAsync<int>("SELECT COUNT(*) FROM documents.DocumentAttempts"));

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            if (Directory.Exists(objectRoot))
            {
                Directory.Delete(objectRoot, recursive: true);
            }
        }

        private static DbContextOptionsBuilder<DocumentsDbContext> Options(SqlServerRelationalApiFactory factory) =>
            new DbContextOptionsBuilder<DocumentsDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", DocumentsDbContext.Schema));
    }

    private sealed class StoreScope(DocumentsDbContext dbContext, DocumentsStore store) : IAsyncDisposable
    {
        public DocumentsDbContext Context => dbContext;

        public DocumentsStore Store => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }
}
