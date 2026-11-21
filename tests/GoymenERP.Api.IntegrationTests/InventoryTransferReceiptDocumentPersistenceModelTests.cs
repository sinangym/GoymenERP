using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed class InventoryTransferReceiptDocumentPersistenceModelTests
{
    [Fact]
    public void Receipt_document_history_has_a_company_key_and_previous_version_foreign_key_without_defaults()
    {
        using var database = Database();
        var entity = database.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(InventoryTransferReceiptDocumentVersionRow))!;
        Assert.Equal(new[] { "CompanyId", "DocumentId", "Version" }, entity.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Single(entity.GetDeclaredQueryFilters());
        Assert.Contains(entity.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(InventoryTransferReceiptDocumentVersionRow)
            && x.Properties.Select(p => p.Name).SequenceEqual(new[] { "CompanyId", "DocumentId", "PreviousVersion" }));
        Assert.Contains(entity.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(InventoryTransferRow));
        Assert.Equal(InventoryDbContext.ReceiptDocumentStateCheck, Assert.Single(entity.GetCheckConstraints()).Sql);
        Assert.All(entity.GetProperties(), x => Assert.Null(x.FindAnnotation(RelationalAnnotationNames.DefaultValueSql)));
    }
    [Fact]
    public void Receipt_document_forward_migration_matches_the_model_and_refuses_Down_before_any_history_drop()
    {
        using var database = Database();
        Assert.False(database.Database.HasPendingModelChanges());
        Assert.Single(database.Database.GetMigrations(), name =>
            name.EndsWith("_AddInventoryTransferReceiptDocuments", StringComparison.Ordinal));
        var migration = new AddInventoryTransferReceiptDocuments();
        Assert.DoesNotContain(migration.UpOperations, x => x is SqlOperation or InsertDataOperation or UpdateDataOperation or DeleteDataOperation);
        Assert.Equal("TransferReceiptDocumentVersions", Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()).Name);
        Assert.Contains("THROW 51999", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Receipt_document_recorded_history_is_never_updated_or_deleted(bool delete)
    {
        using var database = Database(); var row = Draft(); database.Attach(row);
        if (delete) database.Remove(row); else row.LinesJson = "[]";
        Assert.Throws<InvalidOperationException>(() => database.SaveChanges());
    }
    [Fact]
    public void Receipt_document_projection_requires_the_same_immutable_content_and_the_actual_predecessor()
    {
        var draft = Draft();
        var line = Assert.Single(JsonSerializer.Deserialize<InventoryTransferReceiptDocumentLineV1[]>(draft.LinesJson,
            InventoryTransferReceiptDocuments.Json)!);
        var effective = Effective(draft);
        var result = InventoryTransferReceiptDocuments.Project([draft, effective]);
        Assert.Equal(("2", "Effective", line.ReceivedKg), (result.Version, result.Status, Assert.Single(result.Lines).ReceivedKg));
        effective.LinesJson = JsonSerializer.Serialize(new[] { line with { ReceivedKg = "4" } }, InventoryTransferReceiptDocuments.Json);
        Assert.Throws<InventoryTransferException>(() => InventoryTransferReceiptDocuments.Project([draft, effective]));
        effective = Effective(draft); effective.PreviousVersion = null;
        Assert.Throws<InventoryTransferException>(() => InventoryTransferReceiptDocuments.Project([draft, effective]));
        Assert.Throws<InventoryTransferException>(() => InventoryTransferReceiptDocuments.Project([Effective(draft)]));
    }
    [Theory]
    [InlineData("5", "5", null, true)]
    [InlineData("5", "6", "SENTETIK excess", true)]
    [InlineData("5", "6", null, false)]
    [InlineData("5", "4", "SENTETIK excess", false)]
    [InlineData("5", "0", null, false)]
    [InlineData("5", "4.1234", null, false)]
    public void Receipt_document_line_quantity_and_excess_reason_proof_is_not_a_default(string dispatched, string received, string? reason, bool valid)
    {
        var row = Draft();
        row.LinesJson = JsonSerializer.Serialize(new[] { new InventoryTransferReceiptDocumentLineV1(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            dispatched, received, reason) }, InventoryTransferReceiptDocuments.Json);
        if (valid) Assert.True(InventoryTransferReceiptDocuments.Valid(InventoryTransferReceiptDocuments.Project([row])));
        else Assert.Throws<InventoryTransferException>(() => InventoryTransferReceiptDocuments.Project([row]));
    }
    [Fact]
    public void Optional_receipt_document_version_does_not_change_a_legacy_request_JSON_shape()
    {
        var request = new ReceiveInventoryTransferLineV1("2", DateTimeOffset.Parse("2026-11-17T00:00:00Z"),
            new("inventory.transfer.incoming", Guid.NewGuid(), null), "5", null);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.DoesNotContain("expectedIncomingDocumentVersion", JsonSerializer.Serialize(request, options), StringComparison.Ordinal);
        Assert.Contains("expectedIncomingDocumentVersion", JsonSerializer.Serialize(request with { ExpectedIncomingDocumentVersion = "2" }, options), StringComparison.Ordinal);
    }
    [Fact]
    public void Receipt_document_cursor_is_bound_to_its_purpose_company_actor_transfer_and_signature()
    {
        // Explicit synthetic key, unrelated to any runtime signing configuration.
        using var codec = new InventoryStockPositionCursorCodec(new(Convert.ToBase64String(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray())), TimeProvider.System);
        var source = (IInventoryTransferReceiptDocumentCursorV1)codec;
        var transfer = Guid.NewGuid(); var last = Guid.NewGuid();
        var cursor = source.Protect(TestCompanyIds.CompanyA, "SENTETIK", transfer, last);
        Assert.Equal(last, source.Unprotect(TestCompanyIds.CompanyA, "SENTETIK", transfer, cursor));
        foreach (var invalid in new Func<Guid>[] {
            () => source.Unprotect(TestCompanyIds.CompanyB, "SENTETIK", transfer, cursor),
            () => source.Unprotect(TestCompanyIds.CompanyA, "SENTETIK-other", transfer, cursor),
            () => source.Unprotect(TestCompanyIds.CompanyA, "SENTETIK", Guid.NewGuid(), cursor),
            () => source.Unprotect(TestCompanyIds.CompanyA, "SENTETIK", transfer, cursor + "x") })
            Assert.Equal(400, Assert.Throws<InventoryTransferException>(() => { _ = invalid(); }).Status);
    }
    private static InventoryDbContext Database()
    {
        var company = new CompanyContext(); company.Set(TestCompanyIds.CompanyA);
        return new(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer("Server=unused;Database=SyntheticModelOnly;Integrated Security=True;TrustServerCertificate=True").Options, company);
    }
    private static InventoryTransferReceiptDocumentVersionRow Draft() => new()
    {
        CompanyId = TestCompanyIds.CompanyA, DocumentId = Guid.NewGuid(), TransferId = Guid.NewGuid(), SourceTransferVersion = 2,
        Version = 1, Status = 1, ReceivedAtUtc = DateTimeOffset.Parse("2026-11-17T00:00:00Z"), RecordedAtUtc = DateTimeOffset.Parse("2026-11-17T00:01:00Z"),
        RecordedBy = "SENTETIK", LinesJson = JsonSerializer.Serialize(new[] { new InventoryTransferReceiptDocumentLineV1(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "5", "5", null) },
            InventoryTransferReceiptDocuments.Json),
    };
    private static InventoryTransferReceiptDocumentVersionRow Effective(InventoryTransferReceiptDocumentVersionRow draft) => new()
    {
        CompanyId = draft.CompanyId, DocumentId = draft.DocumentId, TransferId = draft.TransferId, SourceTransferVersion = draft.SourceTransferVersion,
        Version = 2, PreviousVersion = 1, Status = 2, ReceivedAtUtc = draft.ReceivedAtUtc, LinesJson = draft.LinesJson,
        RecordedAtUtc = draft.RecordedAtUtc.AddMinutes(1), RecordedBy = "SENTETIK", FinalizationReason = "SENTETIK kontrol",
    };
}
