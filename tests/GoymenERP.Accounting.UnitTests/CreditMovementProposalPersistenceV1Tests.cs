using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Accounting.UnitTests;

public sealed class CreditMovementProposalPersistenceV1Tests
{
    [Fact]
    public void Credit_proposal_has_company_source_dedup_and_only_forward_fix_can_remove_received_facts()
    {
        using var db = Context();
        var model = db.GetService<IDesignTimeModel>().Model;
        var row = model.FindEntityType(typeof(CreditMovementProposalRow))!;
        Assert.Equal("accounting", row.GetSchema());
        Assert.Single(row.GetDeclaredQueryFilters());
        Assert.Contains(row.GetIndexes(), index => index.IsUnique && index.Properties.Select(x => x.Name).SequenceEqual(["CompanyId", "MovementId", "IsReversal"]));
        var migration = new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.AddCreditMovementProposals();
        Assert.Contains("THROW 51971", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql, StringComparison.Ordinal);
    }
    [Fact]
    public async Task Accepted_credit_source_cannot_be_edited_or_deleted_before_opening_a_database_connection()
    {
        using var db = Context();
        var row = new CreditMovementProposalRow { CompanyId = Company, Id = Guid.NewGuid(), MovementId = Guid.NewGuid(), SourceHash = new string('A', 64),
            SourceJson = "{}", MappingStatus = "MappingMissing", WaitingReasonsJson = "[]", ReceivedAtUtc = DateTimeOffset.UtcNow };
        db.Attach(row); row.SourceJson = "{\"changed\":true}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear(); db.Attach(row); db.Remove(row);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    private static readonly Guid Company = Guid.Parse("c0712000-0000-4000-8000-000000000001");
    private static AccountingDbContext Context()
    {
        var company = new CompanyContext(); company.Set(Company);
        return new(new DbContextOptionsBuilder<AccountingDbContext>().UseSqlServer("Server=unused;Database=unused;Trusted_Connection=True;TrustServerCertificate=True").Options, company);
    }
}
