using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;
using GoymenERP.Modules.Documents.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// KLN-D59 / KLN-D60 on real SQL Server: a PDF the server generated is stored as a <c>SystemGenerated</c> (3) version
/// without a scanner, with its SHA-256 and an audit naming the producing command and its permission; the scan check
/// still refuses a system-generated version that names a scanner; the scan-state migration steps back while no such
/// version exists and refuses its Down with 51592 once one does, keeping the widened check. SENTETIK.
/// </summary>
public sealed partial class DocumentVaultSqlServerRelationalAcceptanceTests
{
    private const string SystemGeneratedScanStateMigration = "20261115165534_AddSystemGeneratedDocumentScanState";
    private const string SentetikProducerPermission = "sentetik.labels.print";
    private const string SentetikProducerCommand = "sentetik.material-label.print";

    [SqlServerFact]
    public async Task A_system_generated_pdf_is_kept_without_a_scanner_and_the_scan_state_migrations_Down_refuses_with_51592()
    {
        await using var run = await Run.StartAsync();
        var companyContext = new CompanyContext();
        companyContext.Set(CompanyA);
        var options = new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", DocumentsDbContext.Schema))
            .Options;
        const string TakesSystemGenerated =
            "SELECT COUNT(*) FROM sys.check_constraints WHERE [name] = N'CK_DocumentVersions_Scan' AND CHARINDEX(N'[ScanState]=(3)', [definition]) > 0";
        string[] all = [InitialDocumentsMigration, PrimaryLinkMarkerMigration, WidenContentKindsMigration, SystemGeneratedScanStateMigration];

        await using (var a = run.Store(CompanyA))
        {
            await a.Store.StoreUploadAsync(run.Envelope("k-scanned"), Hash("scanned"), Upload(CompanyA, PartyA, Pdf, "A.pdf"), PermitWriteAsync);
        }

        // Only scanned versions exist: the rollback loses nothing and passes; the widened check is applied again.
        await using (var context = new DocumentsDbContext(options, companyContext))
        {
            Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync());
            Assert.Equal(1, await run.ScalarAsync<int>(TakesSystemGenerated));
            await context.GetService<IMigrator>().MigrateAsync(WidenContentKindsMigration);
            Assert.Equal(0, await run.ScalarAsync<int>(TakesSystemGenerated));
            await context.Database.MigrateAsync();
            Assert.Equal(1, await run.ScalarAsync<int>(TakesSystemGenerated));
        }

        var generated = OtherPdf;
        var sha = DocumentContentHash.Compute(generated);
        Guid documentId;
        await using (var a = run.Store(CompanyA))
        {
            var envelope = run.Envelope("k-generated") with { PermissionCode = SentetikProducerPermission };
            var upload = new NewDocumentUpload(Guid.NewGuid(), DocumentOwnerLink.TryCreate(DocumentOwnerLinkKindsV1.CounterpartyParty, PartyA)!,
                DocumentFileName.TryCreate("Etiket.pdf")!, DocumentContentPolicy.Info(DocumentFileKind.Pdf), generated.LongLength, sha,
                DocumentObjectKeys.For(CompanyA, sha), DocumentScanState.SystemGenerated, null, T0,
                GeneratedDocumentTemplate.TryCreate("sentetik.material-label-pdf", "v1"), SentetikProducerCommand);
            documentId = (await a.Store.StoreUploadAsync(envelope, Hash("generated"), upload, PermitWriteAsync)).Document.Id;
        }

        Assert.Equal($"3||{sha}", await run.ScalarAsync<string>(
            "SELECT CONCAT(ScanState, N'|', ScannerId, N'|', Sha256) FROM documents.DocumentVersions WHERE CompanyId = @c AND DocumentId = @d",
            ("@c", CompanyA), ("@d", documentId)));
        Assert.Equal($"{SentetikProducerCommand}|{SentetikProducerPermission}|true|{run.Actor}", await run.ScalarAsync<string>(
            "SELECT CONCAT(JSON_VALUE(AfterJson, '$.producerCommand'), N'|', JSON_VALUE(AfterJson, '$.producerPermission'), N'|', "
            + "JSON_VALUE(AfterJson, '$.systemGenerated'), N'|', ActorId) FROM audit.AuditEvents "
            + "WHERE CompanyId = @c AND EntityId = @id AND EventType = N'documents.document.generated.v1'",
            ("@c", CompanyA), ("@id", documentId.ToString("D"))));

        // A system-generated version names no scanner (547: CK_DocumentVersions_Scan).
        var named = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO documents.DocumentVersions (CompanyId, DocumentId, VersionNo, FileName, Sha256, ScanState, ScannerId, UploadedBy, "
            + "UploadedAtUtc, CorrelationId) VALUES (@c, @d, 2, N'SENTETIK.pdf', @sha, 3, N'SENTETIK-scanner-v1', N'SENTETIK', SYSDATETIMEOFFSET(), "
            + "N'SENTETIK')", ("@c", CompanyA), ("@d", documentId), ("@sha", sha)));
        Assert.Equal(547, named.Number);

        await using (var context = new DocumentsDbContext(options, companyContext))
        {
            // No XML source exists: explicitly step down the later content-kind change before testing this guard.
            await context.GetService<IMigrator>().MigrateAsync(SystemGeneratedScanStateMigration);
            var refusal = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(WidenContentKindsMigration));
            Assert.Equal(51592, refusal.Number);
            Assert.Equal(all, await context.Database.GetAppliedMigrationsAsync());
        }

        Assert.Equal(1, await run.ScalarAsync<int>(TakesSystemGenerated));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM documents.DocumentVersions WHERE ScanState = 3"));
    }
}
