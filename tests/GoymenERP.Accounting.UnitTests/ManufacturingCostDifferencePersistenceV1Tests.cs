using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Accounting.UnitTests;

public sealed class ManufacturingCostDifferencePersistenceV1Tests
{
    private static readonly Guid Company = Guid.Parse("c0622000-0000-4000-8000-000000000001");

    [Fact]
    public void Cost_difference_migration_requires_a_forward_fix_before_dropping_any_stored_source()
    {
        var migration = new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.AddManufacturingCostDifferenceProposals();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51962", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
    }

    [Fact]
    public void Proposal_source_is_company_scoped_and_preparation_is_concurrent_with_explicit_period_and_journal_links()
    {
        using var db = Context();
        var model = db.GetService<IDesignTimeModel>().Model;
        var row = model.FindEntityType(typeof(ManufacturingCostDifferenceRow))!;
        Assert.Equal("accounting", row.GetSchema());
        Assert.Single(row.GetDeclaredQueryFilters());
        Assert.Equal(new[] { "CompanyId", "Id" }, row.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.True(row.FindProperty("Version")!.IsConcurrencyToken);
        Assert.Contains(row.GetIndexes(), index => index.IsUnique && index.Properties.Select(x => x.Name)
            .SequenceEqual(new[] { "CompanyId", "GenerationId", "GenerationVersion" }));
        Assert.Contains(row.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(PeriodRow)
            && key.Properties.Select(x => x.Name).SequenceEqual(new[] { "CompanyId", "PeriodId" }));
        Assert.Contains(row.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(JournalRow)
            && key.Properties.Select(x => x.Name).SequenceEqual(new[] { "CompanyId", "JournalId" }));
        Assert.Contains(row.GetCheckConstraints(), check => check.Name == "CK_ManufacturingCostDifferences_State"
            && check.Sql.Contains("[ApprovedBy] <> [PreparedBy]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Source_edits_and_posted_decision_edits_are_refused_before_a_connection_is_opened()
    {
        using var db = Context();
        var row = Row();
        db.Attach(row);
        row.SignedTlDifference = "601";
        row.Version = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        row = Row(); row.Status = 3; row.Version = 3;
        db.Attach(row);
        row.Reason = "SENTETIK overwrite"; row.Version = 4;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    private static AccountingDbContext Context()
    {
        var company = new CompanyContext(); company.Set(Company);
        return new(new DbContextOptionsBuilder<AccountingDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Trusted_Connection=True;TrustServerCertificate=True").Options, company);
    }
    private static ManufacturingCostDifferenceRow Row() => new()
    {
        Id = Guid.NewGuid(), CompanyId = Company, ManufacturingOrderId = Guid.NewGuid(), GenerationId = Guid.NewGuid(),
        GenerationVersion = "2", SourceHash = new string('A', 64), SourceJson = "{}", SourceOccurredAtUtc = DateTimeOffset.UtcNow,
        SourceCorrelationId = "SENTETIK", SignedTlDifference = "600", Status = 1, Version = 1,
    };
}
