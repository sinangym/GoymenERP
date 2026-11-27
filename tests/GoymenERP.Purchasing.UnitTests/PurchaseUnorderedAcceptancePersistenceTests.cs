using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseUnorderedAcceptancePersistenceTests
{
    [Fact]
    public void Unordered_facts_and_original_receipts_have_company_keys_filters_and_a_real_same_company_foreign_key()
    {
        using var db = Database(); var model = db.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(PurchaseUnorderedAcceptanceRow), typeof(PurchaseUnorderedAcceptanceAttemptRow) })
        {
            var entity = model.FindEntityType(type)!;
            Assert.Equal("purchasing", entity.GetSchema()); Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.Contains("CompanyId", entity.FindPrimaryKey()!.Properties.Select(x => x.Name));
            Assert.DoesNotContain(entity.GetProperties(), x => x.Name.Contains("OrderId", StringComparison.Ordinal));
        }
        var fact = model.FindEntityType(typeof(PurchaseUnorderedAcceptanceRow))!;
        Assert.True(fact.FindProperty("Version")!.IsConcurrencyToken);
        var attempt = model.FindEntityType(typeof(PurchaseUnorderedAcceptanceAttemptRow))!;
        Assert.Contains(attempt.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(PurchaseUnorderedAcceptanceRow)
            && x.Properties.Select(p => p.Name).SequenceEqual(["CompanyId", "AcceptanceId", "AcceptanceLineId"])
            && x.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.Contains(attempt.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name)
            .SequenceEqual(["CompanyId", "AcceptanceId", "AcceptanceLineId"]));
        Assert.Contains(model.FindEntityType(typeof(PurchaseInvoiceReceiptBindingRow))!.GetForeignKeys(),
            x => x.PrincipalEntityType.ClrType == typeof(PurchaseAcceptanceRow));
    }

    [Fact]
    public void Stored_occurrence_and_receipt_are_append_only_before_any_SQL_write()
    {
        using var db = Database();
        var fact = new PurchaseUnorderedAcceptanceRow { CompanyId = dbCompany, AcceptanceId = Guid.NewGuid(), AcceptanceLineId = Guid.NewGuid(), Version = 1 };
        db.Attach(fact); fact.RecordHash = new string('a', 64);
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges()); db.ChangeTracker.Clear();
        var attempt = new PurchaseUnorderedAcceptanceAttemptRow { CompanyId = dbCompany, KeyHash = new string('b', 64) };
        db.Attach(attempt); db.Remove(attempt);
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Theory]
    [InlineData("unknown-kind")]
    [InlineData("unknown-field")]
    public void Retained_source_JSON_is_strict_and_cannot_silently_change_the_subject_variant(string defect)
    {
        var subject = new PurchaseUnorderedGoodsSubjectV1(Guid.NewGuid(), "1");
        var source = new PurchaseUnorderedAcceptanceSourceV1(dbCompany, Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
            subject, Guid.NewGuid(), "1", "1", DateTimeOffset.UtcNow, new(Guid.NewGuid(), 1, new string('a', 64), "SENTETIK-line"), []);
        var record = new PurchaseUnorderedAcceptanceRecordV1(source, "1", "1", new(Guid.NewGuid(), 1), "SENTETIK-actor", DateTimeOffset.UtcNow);
        var json = JsonSerializer.Serialize(record, PurchaseUnorderedAcceptanceStoreV1.Json);
        json = defect == "unknown-kind" ? json.Replace("\"Goods\"", "\"Unknown\"", StringComparison.Ordinal)
            : json.Insert(1, "\"unexpected\":true,");
        Assert.Throws<JsonException>(() => PurchaseUnorderedAcceptanceStoreV1.Restore(json));
    }

    private static readonly Guid dbCompany = Guid.Parse("ad50b301-0000-4000-8000-000000000001");
    private static PurchasingDbContext Database()
    {
        var company = new CompanyContext(); company.Set(dbCompany);
        return new(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
    }
}
