using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;
using GoymenERP.Modules.Documents.Infrastructure;
using GoymenERP.Modules.Documents.Infrastructure.Persistence;
using GoymenERP.Modules.Documents.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Documents.Infrastructure.Scanning;
using GoymenERP.Modules.Documents.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Documents.UnitTests;

/// <summary>
/// The vault's persistent model without a database (schema, tenant filters, keys, checks, the append-only write guard
/// that refuses before any connection, the migration's refusal), the development object store and scanner, and the
/// module's fail-closed registration. What SQL Server enforces is proven by the relational acceptance. SENTETIK / NOT_ENV.
/// </summary>
public sealed class DocumentsInfrastructureTests : IDisposable
{
    private static readonly Guid CompanyA = Harness.CompanyA;
    private readonly string root = Path.Combine(Path.GetTempPath(), $"goymen-docvault-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Every_vault_table_is_in_its_schema_filtered_by_the_active_company_and_keyed_company_first()
    {
        using var context = Context();
        var design = context.GetService<IDesignTimeModel>().Model;
        foreach (var (type, table) in new[] { (typeof(ContentBlobRow), "ContentBlobs"), (typeof(DocumentRow), "Documents"),
                     (typeof(DocumentVersionRow), "DocumentVersions"), (typeof(DocumentLinkRow), "DocumentLinks"), (typeof(DocumentAttemptRow), "DocumentAttempts") })
        {
            var entity = design.FindEntityType(type)!;
            Assert.Equal((DocumentsDbContext.Schema, table), (entity.GetSchema(), entity.GetTableName()));
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.Contains(entity.GetForeignKeys(), key => key.Properties.Any(property => property.Name == "CompanyId"));
        }

        Assert.Equal(["CompanyId", "Sha256"], Key(design, typeof(ContentBlobRow)));
        Assert.Equal(["CompanyId", "DocumentId", "VersionNo"], Key(design, typeof(DocumentVersionRow)));
        Assert.Equal(["CompanyId", "DocumentId", "OwnerKind", "OwnerId"], Key(design, typeof(DocumentLinkRow)));
        Assert.Equal(["CompanyId", "CommandName", "IdempotencyKeyHash"], Key(design, typeof(DocumentAttemptRow)));
        var audit = context.Model.FindEntityType(typeof(AuditEvent))!;
        Assert.Equal(("audit", "AuditEvents"), (audit.GetSchema(), audit.GetTableName()));
    }

    [Fact]
    public void The_checks_hold_the_scan_rule_and_the_indexes_the_dedup_and_owner_reads_and_the_snapshot_matches_the_model()
    {
        using var context = Context();
        var design = context.GetService<IDesignTimeModel>().Model;
        var versions = design.FindEntityType(typeof(DocumentVersionRow))!;
        Assert.Contains(versions.GetCheckConstraints(), check => check.Name == "CK_DocumentVersions_Scan"
            && check.Sql == "([ScanState] = 2 AND [ScannerId] IS NOT NULL AND LEN([ScannerId]) > 0) OR ([ScanState] = 1 AND [ScannerId] IS NULL)");
        Assert.Equal(["CompanyId", "Sha256", "UploadedAtUtc"],
            versions.GetIndexes().Single(index => index.GetDatabaseName() == "IX_DocumentVersions_CompanyId_Sha256_UploadedAtUtc").Properties
                .Select(property => property.Name));
        Assert.Equal(DocumentsDbContext.IdentityCollation, versions.FindProperty(nameof(DocumentVersionRow.Sha256))!.GetCollation());
        var links = design.FindEntityType(typeof(DocumentLinkRow))!;
        Assert.Equal(["CompanyId", "OwnerKind", "OwnerId", "LinkedAtUtc"],
            links.GetIndexes().Single().Properties.Select(property => property.Name));
        var attempts = design.FindEntityType(typeof(DocumentAttemptRow))!;
        var perDocument = attempts.GetIndexes().Single(index => index.GetDatabaseName() == "UX_DocumentAttempts_DocumentId");
        Assert.True(perDocument.IsUnique);
        Assert.Equal("[CommandName] = N'documents.upload.v1'", perDocument.GetFilter());
        Assert.False(attempts.FindProperty(nameof(DocumentAttemptRow.DocumentId))!.IsNullable);
        Assert.Contains(design.FindEntityType(typeof(ContentBlobRow))!.GetCheckConstraints(), check => check.Name == "CK_ContentBlobs_Kind"
            && check.Sql == "[ContentKind] IN (1, 2, 3)");
        Assert.Equal(["20260930120000_InitialDocuments"], context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void The_migration_down_refuses_with_51058_while_a_document_exists_before_it_drops_anything()
    {
        var down = new InitialDocuments().DownOperations;
        var guard = Assert.IsType<SqlOperation>(down[0]);
        Assert.Contains("IF EXISTS (SELECT 1 FROM [documents].[Documents])", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51058,", guard.Sql, StringComparison.Ordinal);
        Assert.Equal(["DocumentAttempts", "DocumentLinks", "DocumentVersions", "ContentBlobs", "Documents"],
            down.Skip(1).Select(operation => Assert.IsType<DropTableOperation>(operation).Name));
    }

    [Fact]
    public async Task The_write_guard_refuses_any_update_any_delete_and_another_company_before_any_connection()
    {
        await using (var context = Context())
        {
            var row = Version(CompanyA);
            context.Versions.Attach(row);
            row.FileName = "Başka.pdf";
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("değiştirilemez veya silinemez", refusal.Message, StringComparison.Ordinal);
        }

        await using (var context = Context())
        {
            var row = new DocumentRow { Id = Guid.NewGuid(), CompanyId = CompanyA, CreatedBy = "SENTETIK", CorrelationId = "SENTETIK" };
            context.Documents.Attach(row);
            context.Entry(row).State = EntityState.Deleted;
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("silinemez", refusal.Message, StringComparison.Ordinal);
        }

        await using (var context = Context())
        {
            context.Versions.Add(Version(Harness.CompanyB));
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("aktif şirket", refusal.Message, StringComparison.Ordinal);
        }

        await using (var context = Context(resolved: false))
        {
            context.Versions.Add(Version(CompanyA));
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task The_file_system_store_keeps_an_object_once_refuses_other_bytes_under_its_key_and_any_key_outside_its_shape()
    {
        var store = new FileSystemDocumentObjectStore(root);
        var pdf = DocumentContentRulesTests.Pdf;
        var sha = DocumentContentHash.Compute(pdf);
        var key = DocumentObjectKeys.For(CompanyA, sha);

        Assert.Null(await store.ReadAsync(key));
        await store.PutAsync(key, pdf, sha);
        await store.PutAsync(key, pdf, sha);
        Assert.Equal(pdf, await store.ReadAsync(key));
        Assert.Single(Directory.GetFiles(root, "*", SearchOption.AllDirectories));

        // Bytes that are not the key's hash are refused before anything is written.
        await Assert.ThrowsAsync<ArgumentException>(() => store.PutAsync(key, DocumentContentRulesTests.Png, sha));
        // An object altered on disk is detected on the next put of its key; nothing is overwritten.
        await File.WriteAllBytesAsync(Directory.GetFiles(root, "*", SearchOption.AllDirectories).Single(), DocumentContentRulesTests.Png);
        await Assert.ThrowsAsync<DocumentObjectStoreUnavailableException>(() => store.PutAsync(key, pdf, sha));
        Assert.Equal(DocumentContentRulesTests.Png, await store.ReadAsync(key));

        foreach (var bad in new[] { "../x", $"companies/{CompanyA:N}/sha256/../../x", $"companies/{CompanyA:D}/sha256/{sha}", $"companies/{CompanyA:N}/sha256/{sha.ToUpperInvariant()}" })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => store.ReadAsync(bad));
        }

        Assert.Throws<ArgumentException>(() => DocumentObjectKeys.For(Guid.Empty, sha));
        Assert.StartsWith("companies/11111111111141118111111111111111/sha256/", key, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_development_scanner_knows_only_the_eicar_test_signature_and_says_so_in_its_identity()
    {
        var scanner = new DevelopmentSignatureScanner();
        byte[] eicar = [.. "%PDF-1.4\n"u8.ToArray(), .. "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"u8.ToArray()];

        Assert.Equal(DocumentScanVerdictV1.Infected, await scanner.ScanAsync(eicar, DocumentFileKind.Pdf));
        Assert.Equal(DocumentScanVerdictV1.Clean, await scanner.ScanAsync(DocumentContentRulesTests.Pdf, DocumentFileKind.Pdf));
        Assert.Contains("development", scanner.ScannerId, StringComparison.Ordinal);
    }

    [Fact]
    public void Outside_development_no_object_store_and_no_scanner_are_registered_and_development_settings_are_refused()
    {
        var production = Services(allowDevelopment: false);
        Assert.Null(production.GetService<IDocumentObjectStoreV1>());
        Assert.Null(production.GetService<IDocumentContentScannerV1>());
        Assert.Equal(DocumentsServiceBindings.DefaultMaximumUploadBytes, production.GetRequiredService<DocumentsServiceBindings>().MaximumUploadBytes);

        Assert.Throws<InvalidOperationException>(() => Services(allowDevelopment: false, (DocumentsModuleExtensions.ObjectStoreRootPathKey, root)));
        Assert.Throws<InvalidOperationException>(() =>
            Services(allowDevelopment: false, (DocumentsModuleExtensions.ScannerModeKey, DocumentsModuleExtensions.DevelopmentScannerMode)));
        Assert.Throws<InvalidOperationException>(() => Services(allowDevelopment: true, (DocumentsModuleExtensions.MaximumUploadBytesKey, "0")));
        Assert.Throws<InvalidOperationException>(() => Services(allowDevelopment: true, (DocumentsModuleExtensions.ScannerModeKey, "ClamAV")));
    }

    [Fact]
    public void In_development_the_file_system_store_and_the_eicar_scanner_are_registered_unless_the_scanner_is_turned_off()
    {
        var development = Services(allowDevelopment: true, (DocumentsModuleExtensions.ObjectStoreRootPathKey, root),
            (DocumentsModuleExtensions.MaximumUploadBytesKey, "1048576"));
        Assert.Equal(Path.GetFullPath(root), Assert.IsType<FileSystemDocumentObjectStore>(development.GetRequiredService<IDocumentObjectStoreV1>()).RootPath);
        Assert.IsType<DevelopmentSignatureScanner>(development.GetRequiredService<IDocumentContentScannerV1>());
        Assert.Equal(1048576, development.GetRequiredService<DocumentsServiceBindings>().MaximumUploadBytes);

        var noScanner = Services(allowDevelopment: true, (DocumentsModuleExtensions.ScannerModeKey, DocumentsModuleExtensions.NoScannerMode));
        Assert.Null(noScanner.GetService<IDocumentContentScannerV1>());
        Assert.NotNull(noScanner.GetService<IDocumentObjectStoreV1>());
    }

    private static ServiceProvider Services(bool allowDevelopment, params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(
                [new("DB_CONNECTION_STRING", "Server=SENTETIK.invalid;Database=SENTETIK;"), .. settings.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))]))
            .Build();
        return new ServiceCollection().AddDocumentsModule(configuration, allowDevelopmentStorageAndScanner: allowDevelopment).BuildServiceProvider();
    }

    private static string[] Key(IModel model, Type type) =>
        [.. model.FindEntityType(type)!.FindPrimaryKey()!.Properties.Select(property => property.Name)];

    private static DocumentVersionRow Version(Guid company) => new DocumentVersionRow
    {
        CompanyId = company, DocumentId = Guid.NewGuid(), VersionNo = 1, FileName = "SENTETIK.pdf", Sha256 = new string('a', 64), ScanState = 1,
        UploadedBy = "SENTETIK", UploadedAtUtc = DateTimeOffset.UtcNow, CorrelationId = "SENTETIK",
    };

    private static DocumentsDbContext Context(bool resolved = true)
    {
        var company = new CompanyContext();
        if (resolved)
        {
            company.Set(CompanyA);
        }

        var options = new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseSqlServer("Server=SENTETIK.invalid;Database=SENTETIK;Connect Timeout=1;",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", DocumentsDbContext.Schema))
            .Options;
        return new DocumentsDbContext(options, company);
    }
}
