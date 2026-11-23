using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinancePeriodCarryPersistenceTests
{
    private static readonly Guid Company = Guid.Parse("ae210000-0000-4000-8000-000000000001");
    [Fact]
    public void Carry_and_counter_record_have_tenant_keys_filters_and_single_owner_uniqueness()
    {
        using var db = Context(); var model = db.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(FinancePeriodCarryVoucherRow), typeof(FinancePeriodCarryReversalRow) })
        {
            var entity = model.FindEntityType(type)!;
            Assert.Equal("finance", entity.GetSchema());
            Assert.Equal(["CompanyId", "Id"], entity.FindPrimaryKey()!.Properties.Select(x => x.Name));
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.Contains(entity.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(["CompanyId", "ClosureId"]));
            Assert.All(entity.GetForeignKeys(), x => Assert.Equal(DeleteBehavior.Restrict, x.DeleteBehavior));
        }
        var reversal = model.FindEntityType(typeof(FinancePeriodCarryReversalRow))!;
        Assert.Contains(reversal.GetForeignKeys(), x => x.Properties.Select(p => p.Name).SequenceEqual(["CompanyId", "VoucherId"]));
        Assert.Contains(db.Database.GetMigrations(), x => x == "20261117154319_AddPeriodCarryVouchers");
        Assert.False(db.Database.HasPendingModelChanges());
        var migration = new GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations.AddPeriodCarryVouchers();
        Assert.DoesNotContain(migration.UpOperations, x => x is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51921", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[finance].[PeriodCarryVouchers]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[finance].[PeriodCarryReversals]", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, x => x is SqlOperation);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Carry_proof_and_counter_record_are_immutable_before_opening_any_connection(bool reversal)
    {
        using var db = Context();
        if (reversal)
        {
            var row = new FinancePeriodCarryReversalRow { Id = Guid.NewGuid(), CompanyId = Company, SourceHash = new string('a', 64), Reason = "SENTETIK" };
            db.Attach(row); row.Reason = "changed";
        }
        else
        {
            var row = new FinancePeriodCarryVoucherRow { Id = Guid.NewGuid(), CompanyId = Company, SourceHash = new string('a', 64), SourceJson = "{}", LinesJson = "[]" };
            db.Attach(row); row.LinesJson = "[{}]";
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    private static FinanceDbContext Context()
    {
        var company = new CompanyContext(); company.Set(Company);
        return new(new DbContextOptionsBuilder<FinanceDbContext>().UseSqlServer("Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true").Options, company);
    }
}
