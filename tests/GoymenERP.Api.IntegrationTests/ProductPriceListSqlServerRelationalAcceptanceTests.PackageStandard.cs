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
/// B-08 / BS-10 (STK-S12) on a REAL SQL Server. Until the company imports a package standard table, the read port answers NoSource
/// and nothing is seeded. The Excel import resolves the product codes and writes an insert-only version that holds the whole
/// table, with one attempt and one audit event. A replay changes nothing, a stale version is refused, and an unknown code refuses
/// the whole file without storing anything. An ERP edit sets or removes one product's entry as a new version, carrying the other
/// entries over. The read port names the version that a consumer snapshots (AT-017), and answers NoSource again once the entry is
/// removed. The database holds the entry shape. The forward-fix rollback of <see cref="AddProductPackageStandards"/> passes on an
/// empty store and refuses (51492) while a version exists. SENTETIK / NOT_ENV.
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
    public async Task The_package_standard_table_is_imported_edited_replayed_and_read_as_NoSource_until_an_entry_exists()
    {
        await using var run = await Run.StartAsync();
        var code = await run.ScalarAsync<string>("SELECT Code FROM product.Products WHERE Id = @id", ProductId);

        // BS-10: no table yet, so the consumers read NoSource and nothing is guessed; the screen sees version "0".
        await using (var scope = run.Request())
        {
            var reader = (IProductPackageStandardReaderV2)PackageService(scope, run);
            Assert.Equal(ProductPackageStandardReadV2.Without(ProductPackageStandardReadOutcomeV2.NoSource),
                await reader.GetAsync(ProductId));
            Assert.Equal(ProductPackageStandardReadOutcomeV2.NotFound, (await reader.GetAsync(Guid.NewGuid())).Outcome);
            var empty = await PackageService(scope, run).GetCurrentAsync();
            Assert.Equal(("0", (string?)null, 0), (empty.Version, empty.SourceKind, empty.Entries.Count));
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardVersions"));

        // An unknown code refuses the whole file: no version, no stored answer, the same key stays usable.
        await using (var scope = run.Request())
        {
            var unknown = await PackageService(scope, run).ImportAsync(PackageImportRequest("0", "SENTETIK-YOK", "6"),
                PackageMetadata(run, "package-import-1"));
            Assert.Equal((400, ProductPackageStandardProblemCodesV2.FileInvalid), (unknown.Answer.StatusCode, unknown.Answer.Code));
            Assert.Contains("Satır 2", unknown.Answer.Detail, StringComparison.Ordinal);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardVersions"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardAttempts"));

        Guid importedVersionId;
        await using (var scope = run.Request())
        {
            var service = PackageService(scope, run);
            var imported = await service.ImportAsync(PackageImportRequest("0", code, "12"), PackageMetadata(run, "package-import-1"));
            Assert.Equal((204, false), (imported.Answer.StatusCode, imported.IsReplay));
            var replay = await service.ImportAsync(PackageImportRequest("0", code, "12"),
                PackageMetadata(run, "package-import-1") with { CorrelationId = "retry" });
            Assert.True(replay.IsReplay);
            Assert.Equal("corr-package-import-1", replay.OriginalCorrelationId);
            var stale = await service.ImportAsync(PackageImportRequest("0", code, "24"), PackageMetadata(run, "package-import-2"));
            Assert.Equal((409, ProductPackageStandardProblemCodesV2.VersionConflict), (stale.Answer.StatusCode, stale.Answer.Code));

            var table = await service.GetCurrentAsync();
            Assert.Equal(("1", ProductPackageStandardSourceKindsV2.Import, "SENTETIK KAYNAK", "paket.xlsx"),
                (table.Version, table.SourceKind, table.SourceLabel, table.FileName));
            var entry = Assert.Single(table.Entries);
            Assert.Equal((ProductId, code, 12), (entry.ProductId, entry.ProductCode, entry.PiecesPerPackage));

            var read = await ((IProductPackageStandardReaderV2)service).GetAsync(ProductId);
            Assert.Equal((ProductPackageStandardReadOutcomeV2.Found, 12, 1L, ProductPackageStandardSourceKindsV2.Import,
                "SENTETIK KAYNAK"), (read.Outcome, read.PiecesPerPackage, read.StandardVersion, read.SourceKind, read.SourceLabel));
            importedVersionId = Assert.IsType<Guid>(read.StandardVersionId);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardVersions"));
        // The replay stores nothing new: the import and the stale refusal are the two answers.
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardAttempts"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Product.PackageStandardTable' AND EntityId = @id",
            importedVersionId.ToString("D")));

        // The ERP edit: a new version per change, the same value is no change, and removing the entry reads NoSource again.
        Guid editedVersionId;
        await using (var scope = run.Request())
        {
            var service = PackageService(scope, run);
            var set = await service.SetAsync(ProductId, new SetProductPackageStandardRequestV2("1", 10, "SENTETIK gerekçe"),
                PackageMetadata(run, "package-set-1"));
            Assert.Equal(204, set.Answer.StatusCode);
            var same = await service.SetAsync(ProductId, new SetProductPackageStandardRequestV2("2", 10, "SENTETIK gerekçe"),
                PackageMetadata(run, "package-set-2"));
            Assert.Equal(204, same.Answer.StatusCode);
            var missing = await service.SetAsync(Guid.NewGuid(), new SetProductPackageStandardRequestV2("2", 4, "SENTETIK gerekçe"),
                PackageMetadata(run, "package-set-3"));
            Assert.Equal((404, (string?)null), (missing.Answer.StatusCode, missing.Answer.Code));

            var read = await ((IProductPackageStandardReaderV2)service).GetAsync(ProductId);
            Assert.Equal((ProductPackageStandardReadOutcomeV2.Found, 10, 2L, ProductPackageStandardSourceKindsV2.Edit, (string?)null),
                (read.Outcome, read.PiecesPerPackage, read.StandardVersion, read.SourceKind, read.SourceLabel));
            editedVersionId = Assert.IsType<Guid>(read.StandardVersionId);
            Assert.NotEqual(importedVersionId, editedVersionId);

            var removed = await service.SetAsync(ProductId, new SetProductPackageStandardRequestV2("2", null, "SENTETIK gerekçe"),
                PackageMetadata(run, "package-set-4"));
            Assert.Equal(204, removed.Answer.StatusCode);
            Assert.Equal(ProductPackageStandardReadV2.Without(ProductPackageStandardReadOutcomeV2.NoSource),
                await ((IProductPackageStandardReaderV2)service).GetAsync(ProductId));
            var table = await service.GetCurrentAsync();
            Assert.Equal(("3", ProductPackageStandardSourceKindsV2.Edit, 0), (table.Version, table.SourceKind, table.Entries.Count));
        }

        // AT-017: earlier versions stay as they were; three versions, one audit event each.
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardVersions"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardLines"));
        Assert.Equal(12, await run.ScalarAsync<int>(
            "SELECT PiecesPerPackage FROM product.ProductPackageStandardLines WHERE Version = 1 AND ProductId = @id", ProductId));
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Product.PackageStandardTable'"));
        Assert.Equal(1, await run.ScalarAsync<int>(
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

        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardVersions"));
    }

    private static ProductPackageStandardService PackageService(RequestScope scope, Run run) =>
        new(scope.Context, scope.CompanyContext, new User(run.CreatorActorId), new Granted(), ScopeAudit(scope), TimeProvider.System);

    private static ImportProductPackageStandardsRequestV2 PackageImportRequest(string expectedVersion, string code, string pieces) =>
        new(expectedVersion, " SENTETIK KAYNAK ", "SENTETIK gerekçe", "paket.xlsx",
            Convert.ToBase64String(PackageWorkbook(code, pieces)));

    private static ProductPackageStandardCommandMetadataV2 PackageMetadata(Run run, string key) =>
        new(TestCompanyIds.CompanyA, run.CreatorActorId.ToString("D"), "corr-" + key, null, "Web", null, null, key);

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
