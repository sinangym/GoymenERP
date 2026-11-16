using System.IO.Compression;
using System.Security;
using System.Text;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using GoymenERP.Modules.Product.Infrastructure.PriceLists.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// B-08 / BS-10 (STK-S12) and SAT-D382 / SAT-D383 (STK-SC36-c) on a REAL SQL Server. Until the company imports a package standard
/// table, the read port answers NoSource and nothing is seeded. The Excel import resolves the product codes and proposes an
/// insert-only version that holds the whole table; it takes effect only once another person approves it from today's business day.
/// A replay changes nothing; a stale version, an unknown code, a waiting version and the preparer's own decision are refused, and
/// every refused file is kept with its report. An ERP edit sets or removes one product's entry as a new version, carrying the other
/// entries over; a rejected version never takes effect. The read port names the version that a consumer snapshots (AT-017), and
/// answers NoSource again once an approved removal is in effect. The database holds the entry shape. The forward-fix rollback of
/// <see cref="AddProductPackageStandards"/> passes on an empty store and refuses (51492) while a version exists. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class ProductPriceListSqlServerRelationalAcceptanceTests
{
    private const int PackageStandardRollbackRefusal = 51492;

    [SqlServerFact]
    public async Task The_package_standard_migrations_Down_passes_on_an_empty_store_and_refuses_while_a_version_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddProductPackageStandards().DownOperations[0]).Sql;
        var code = await run.ScalarAsync<string>("SELECT Code FROM product.Products WHERE Id = @id", ProductId);

        await using (var scope = run.Request())
        {
            await scope.Context.Database.ExecuteSqlRawAsync(guard);

            var imported = await PackageService(scope, run).ImportAsync(PackageImportRequest("0", code, "12"),
                PackageMetadata(run, "package-import-1"));
            Assert.Equal(204, imported.Answer.StatusCode);

            var refusal = await Assert.ThrowsAsync<SqlException>(() => scope.Context.Database.ExecuteSqlRawAsync(guard));
            Assert.Equal(PackageStandardRollbackRefusal, refusal.Number);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardVersions"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardLines"));
    }

    [SqlServerFact]
    public async Task The_package_standard_table_is_imported_approved_edited_replayed_and_read_as_NoSource_until_an_entry_is_effective()
    {
        await using var run = await Run.StartAsync();
        var code = await run.ScalarAsync<string>("SELECT Code FROM product.Products WHERE Id = @id", ProductId);
        var clock = new FixedClock(PackageClockNow);
        var today = ProductPriceBusinessDayV2.DateOf(clock.GetUtcNow());
        var todayText = ProductPriceListWire.FormatDate(today);
        var approver = Guid.NewGuid();

        // BS-10: no table yet, so the consumers read NoSource and nothing is guessed; the screen sees version "0".
        await using (var scope = run.Request())
        {
            var reader = (IProductPackageStandardReaderV2)PackageService(scope, run.CreatorActorId, clock);
            Assert.Equal(ProductPackageStandardReadV2.Without(ProductPackageStandardReadOutcomeV2.NoSource),
                await reader.GetAsync(ProductId));
            Assert.Equal(ProductPackageStandardReadOutcomeV2.NotFound, (await reader.GetAsync(Guid.NewGuid())).Outcome);
            var empty = await PackageService(scope, run.CreatorActorId, clock).GetCurrentAsync();
            Assert.Equal(("0", (string?)null, 0), (empty.Version, empty.SourceKind, empty.Entries.Count));
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardVersions"));

        // SAT-D383: an unknown code refuses the whole file without a version, and the file is kept with its report as the key's
        // terminal answer.
        await using (var scope = run.Request())
        {
            var unknown = await PackageService(scope, run.CreatorActorId, clock).ImportAsync(
                PackageImportRequest("0", "SENTETIK-YOK", "6"), PackageMetadata(run.CreatorActorId, "package-import-0"));
            Assert.Equal((400, ProductPackageStandardProblemCodesV2.FileInvalid), (unknown.Answer.StatusCode, unknown.Answer.Code));
            Assert.Contains("Satır 2", unknown.Answer.Detail, StringComparison.Ordinal);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardVersions"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardAttempts"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM product.ProductPackageStandardImportFiles WHERE Outcome = N'Refused' AND Version IS NULL"));

        // SAT-D382: the accepted import is proposed as Pending, so the consumers still read NoSource until it is approved.
        Guid importedVersionId;
        await using (var scope = run.Request())
        {
            var service = PackageService(scope, run.CreatorActorId, clock);
            var imported = await service.ImportAsync(PackageImportRequest("0", code, "12"),
                PackageMetadata(run.CreatorActorId, "package-import-1"));
            Assert.Equal((204, false), (imported.Answer.StatusCode, imported.IsReplay));
            var replay = await service.ImportAsync(PackageImportRequest("0", code, "12"),
                PackageMetadata(run.CreatorActorId, "package-import-1") with { CorrelationId = "retry" });
            Assert.True(replay.IsReplay);
            Assert.Equal("corr-package-import-1", replay.OriginalCorrelationId);
            var stale = await service.ImportAsync(PackageImportRequest("0", code, "24"),
                PackageMetadata(run.CreatorActorId, "package-import-2"));
            Assert.Equal((409, ProductPackageStandardProblemCodesV2.VersionConflict), (stale.Answer.StatusCode, stale.Answer.Code));

            var pendingTable = await service.GetCurrentAsync();
            Assert.Equal(("1", (string?)null, 0), (pendingTable.Version, pendingTable.SourceKind, pendingTable.Entries.Count));
            Assert.Equal(ProductPackageStandardReadV2.Without(ProductPackageStandardReadOutcomeV2.NoSource),
                await ((IProductPackageStandardReaderV2)service).GetAsync(ProductId));

            var blocked = await service.SetAsync(ProductId, new SetProductPackageStandardRequestV2("1", 10, "SENTETIK gerekçe"),
                PackageMetadata(run.CreatorActorId, "package-set-0"));
            Assert.Equal((409, ProductPackageStandardProblemCodesV3.ApprovalPending), (blocked.Answer.StatusCode, blocked.Answer.Code));

            var byPreparer = await service.ApproveAsync(1, new ApproveProductPackageStandardVersionRequestV3(todayText,
                "SENTETIK onay"), PackageMetadata(run.CreatorActorId, "package-approve-0"));
            Assert.Equal((409, ProductPackageStandardProblemCodesV3.PreparerCannotDecide),
                (byPreparer.Answer.StatusCode, byPreparer.Answer.Code));
        }

        await using (var scope = run.Request())
        {
            var service = PackageService(scope, approver, clock);
            var approved = await service.ApproveAsync(1, new ApproveProductPackageStandardVersionRequestV3(todayText,
                "SENTETIK onay"), PackageMetadata(approver, "package-approve-1"));
            Assert.Equal(204, approved.Answer.StatusCode);

            var table = await service.GetCurrentAsync();
            Assert.Equal(("1", ProductPackageStandardSourceKindsV2.Import, "SENTETIK KAYNAK", "paket.xlsx"),
                (table.Version, table.SourceKind, table.SourceLabel, table.FileName));
            var entry = Assert.Single(table.Entries);
            Assert.Equal((ProductId, code, 12), (entry.ProductId, entry.ProductCode, entry.PiecesPerPackage));

            var read = await ((IProductPackageStandardReaderV3)service).GetAsync(ProductId, null, today);
            Assert.Equal((ProductPackageStandardReadOutcomeV2.Found, 12, 1L, ProductPackageStandardSourceKindsV2.Import,
                "SENTETIK KAYNAK", ProductPackageStandardEntryScopesV3.Product, today),
                (read.Outcome, read.PiecesPerPackage, read.StandardVersion, read.SourceKind, read.SourceLabel, read.EntryScope,
                    read.EffectiveFrom));
            importedVersionId = Assert.IsType<Guid>(read.StandardVersionId);
            Assert.Equal(ProductPackageStandardReadV3.Without(ProductPackageStandardReadOutcomeV2.NoSource),
                await ((IProductPackageStandardReaderV3)service).GetAsync(ProductId, null, today.AddDays(-1)));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardVersions"));
        // The replay stores nothing new: the refused file, the import, the stale refusal, the blocked edit, the preparer's refusal
        // and the approval are the six answers.
        Assert.Equal(6, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardAttempts"));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardImportFiles"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM product.ProductPackageStandardImportFiles WHERE Outcome = N'Accepted' AND Version = 1"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Product.PackageStandardImportFile'"));
        // The proposal and its approval: two events for the version.
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Product.PackageStandardTable' AND EntityId = @id",
            importedVersionId.ToString("D")));

        // The ERP edit: a new version per change takes effect once approved, the same value is no change, a rejected version
        // never takes effect, and an approved removal reads NoSource again.
        Guid editedVersionId;
        await using (var scope = run.Request())
        {
            var preparer = PackageService(scope, run.CreatorActorId, clock);
            var decider = PackageService(scope, approver, clock);
            var set = await preparer.SetAsync(ProductId, new SetProductPackageStandardRequestV2("1", 10, "SENTETIK gerekçe"),
                PackageMetadata(run.CreatorActorId, "package-set-1"));
            Assert.Equal(204, set.Answer.StatusCode);
            Assert.Equal(12, (await ((IProductPackageStandardReaderV2)preparer).GetAsync(ProductId)).PiecesPerPackage);
            Assert.Equal(204, (await decider.ApproveAsync(2, new ApproveProductPackageStandardVersionRequestV3(todayText,
                "SENTETIK onay"), PackageMetadata(approver, "package-approve-2"))).Answer.StatusCode);

            var same = await preparer.SetAsync(ProductId, new SetProductPackageStandardRequestV2("2", 10, "SENTETIK gerekçe"),
                PackageMetadata(run.CreatorActorId, "package-set-2"));
            Assert.Equal(204, same.Answer.StatusCode);
            var missing = await preparer.SetAsync(Guid.NewGuid(), new SetProductPackageStandardRequestV2("2", 4, "SENTETIK gerekçe"),
                PackageMetadata(run.CreatorActorId, "package-set-3"));
            Assert.Equal((404, (string?)null), (missing.Answer.StatusCode, missing.Answer.Code));

            var read = await ((IProductPackageStandardReaderV2)preparer).GetAsync(ProductId);
            Assert.Equal((ProductPackageStandardReadOutcomeV2.Found, 10, 2L, ProductPackageStandardSourceKindsV2.Edit, (string?)null),
                (read.Outcome, read.PiecesPerPackage, read.StandardVersion, read.SourceKind, read.SourceLabel));
            editedVersionId = Assert.IsType<Guid>(read.StandardVersionId);
            Assert.NotEqual(importedVersionId, editedVersionId);

            var removal = await preparer.SetAsync(ProductId, new SetProductPackageStandardRequestV2("2", null, "SENTETIK gerekçe"),
                PackageMetadata(run.CreatorActorId, "package-set-4"));
            Assert.Equal(204, removal.Answer.StatusCode);
            Assert.Equal(204, (await decider.RejectAsync(3, new RejectProductPackageStandardVersionRequestV3("SENTETIK ret"),
                PackageMetadata(approver, "package-reject-3"))).Answer.StatusCode);
            Assert.Equal(10, (await ((IProductPackageStandardReaderV2)preparer).GetAsync(ProductId)).PiecesPerPackage);
            var decidedAgain = await decider.ApproveAsync(3, new ApproveProductPackageStandardVersionRequestV3(todayText,
                "SENTETIK onay"), PackageMetadata(approver, "package-approve-3"));
            Assert.Equal((409, ProductPackageStandardProblemCodesV3.VersionNotPending),
                (decidedAgain.Answer.StatusCode, decidedAgain.Answer.Code));

            var removed = await preparer.SetAsync(ProductId, new SetProductPackageStandardRequestV2("3", null, "SENTETIK gerekçe"),
                PackageMetadata(run.CreatorActorId, "package-set-5"));
            Assert.Equal(204, removed.Answer.StatusCode);
            Assert.Equal(204, (await decider.ApproveAsync(4, new ApproveProductPackageStandardVersionRequestV3(todayText,
                "SENTETIK onay"), PackageMetadata(approver, "package-approve-4"))).Answer.StatusCode);
            Assert.Equal(ProductPackageStandardReadV2.Without(ProductPackageStandardReadOutcomeV2.NoSource),
                await ((IProductPackageStandardReaderV2)preparer).GetAsync(ProductId));
            var table = await preparer.GetCurrentAsync();
            Assert.Equal(("4", ProductPackageStandardSourceKindsV2.Edit, 0), (table.Version, table.SourceKind, table.Entries.Count));
        }

        // AT-017: earlier versions stay as they were; four versions, the proposal and the decision of each in the audit.
        Assert.Equal(4, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardVersions"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardLines"));
        Assert.Equal(12, await run.ScalarAsync<int>(
            "SELECT PiecesPerPackage FROM product.ProductPackageStandardLines WHERE Version = 1 AND ProductId = @id", ProductId));
        Assert.Equal(4, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardDecisions"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM product.ProductPackageStandardDecisions WHERE Version = 3 AND Decision = N'Rejected'"));
        Assert.Equal(8, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Product.PackageStandardTable'"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Product.PackageStandardTable' AND EntityId = @id",
            editedVersionId.ToString("D")));

        // The database holds the entry shape; the rollback refuses while a version exists.
        await using (var scope = run.Request())
        {
            var zero = await Assert.ThrowsAsync<SqlException>(() => scope.Context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO product.ProductPackageStandardLines (CompanyId, Version, ProductId, PiecesPerPackage)
                VALUES ({TestCompanyIds.CompanyA}, 3, {ProductId}, 0)
                """));
            Assert.Equal(CheckConstraintViolation, zero.Number);

            var guard = Assert.IsType<SqlOperation>(new AddProductPackageStandards().DownOperations[0]).Sql;
            var refusal = await Assert.ThrowsAsync<SqlException>(() => scope.Context.Database.ExecuteSqlRawAsync(guard));
            Assert.Equal(PackageStandardRollbackRefusal, refusal.Number);
        }

        Assert.Equal(4, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardVersions"));
    }

    /// <summary>A fixed instant in the middle of an Istanbul business day, so "today" cannot turn over during a run.</summary>
    private static readonly DateTimeOffset PackageClockNow = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    private static ProductPackageStandardService PackageService(RequestScope scope, Run run) =>
        PackageService(scope, run.CreatorActorId, TimeProvider.System);

    private static ProductPackageStandardService PackageService(RequestScope scope, Guid actor, TimeProvider clock,
        IProductPackageStandardCustomerPortV3? customers = null) =>
        new(scope.Context, scope.CompanyContext, new User(actor), new Granted(), ScopeAudit(scope), clock, customers);

    private static ImportProductPackageStandardsRequestV2 PackageImportRequest(string expectedVersion, string code, string pieces) =>
        new(expectedVersion, " SENTETIK KAYNAK ", "SENTETIK gerekçe", "paket.xlsx",
            Convert.ToBase64String(PackageWorkbook(code, pieces)));

    private static ProductPackageStandardCommandMetadataV2 PackageMetadata(Run run, string key) =>
        PackageMetadata(run.CreatorActorId, key);

    private static ProductPackageStandardCommandMetadataV2 PackageMetadata(Guid actor, string key) =>
        new(TestCompanyIds.CompanyA, actor.ToString("D"), "corr-" + key, null, "Web", null, null, key);

    /// <summary>A one-sheet <c>.xlsx</c> with the header row and one product row (inline strings, a number cell).</summary>
    private static byte[] PackageWorkbook(string code, string pieces)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            PackageEntry(archive, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + "<Default Extension=\"xml\" ContentType=\"application/xml\"/></Types>");
            PackageEntry(archive, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" "
                + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>"
                + "<sheet name=\"Paket\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            PackageEntry(archive, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>"
                + "</Relationships>");
            PackageEntry(archive, "xl/worksheets/sheet1.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>"
                + "<row r=\"1\">" + PackageText("A1", ProductPackageStandardFileFormatV2.ProductCodeHeader)
                + PackageText("B1", ProductPackageStandardFileFormatV2.PiecesPerPackageHeader) + "</row>"
                + "<row r=\"2\">" + PackageText("A2", code) + $"<c r=\"B2\"><v>{SecurityElement.Escape(pieces)}</v></c></row>"
                + "</sheetData></worksheet>");
        }

        return stream.ToArray();
    }

    private static void PackageEntry(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string PackageText(string cell, string value) =>
        $"<c r=\"{cell}\" t=\"inlineStr\"><is><t>{SecurityElement.Escape(value)}</t></is></c>";
}
