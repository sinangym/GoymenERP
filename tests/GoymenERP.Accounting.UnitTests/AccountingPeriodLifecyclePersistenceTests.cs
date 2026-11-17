using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Accounting.UnitTests;

public sealed class AccountingPeriodLifecyclePersistenceTests
{
    [Fact]
    public void Closure_and_reopening_keep_company_references_versions_and_a_forward_fix_down()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        using var db = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true").Options, company);
        var model = db.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(AccountingPeriodClosureRow), typeof(AccountingPeriodReopeningRow) })
        {
            var entity = model.FindEntityType(type)!;
            Assert.Equal("accounting", entity.GetSchema());
            Assert.Equal(["CompanyId", "Id"], entity.FindPrimaryKey()!.Properties.Select(x => x.Name));
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.True(entity.FindProperty("Version")!.IsConcurrencyToken);
            Assert.All(entity.GetForeignKeys(), x =>
            {
                Assert.Equal(DeleteBehavior.Restrict, x.DeleteBehavior);
                Assert.Equal("CompanyId", x.Properties[0].Name);
            });
        }
        var reopening = model.FindEntityType(typeof(AccountingPeriodReopeningRow))!;
        Assert.Contains(reopening.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(["CompanyId", "ClosureId"]));
        Assert.Contains(reopening.GetCheckConstraints(), x => x.Name == "CK_PeriodReopenings_Approval"
            && x.Sql.Contains("[ApprovedBy] <> [ProposedBy]", StringComparison.Ordinal));
        var migration = new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.AddPeriodClosureCarryAndReopening();
        Assert.DoesNotContain(migration.UpOperations, x => x is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51920", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[accounting].[PeriodClosures]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[accounting].[PeriodReopenings]", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, x => x is SqlOperation);
    }

    [Theory]
    [InlineData("self-approval")]
    [InlineData("source-changed")]
    [InlineData("version-skipped")]
    public async Task Reopening_history_refuses_changed_source_self_approval_and_skipped_version(string fault)
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        using var db = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true").Options, company);
        var row = new AccountingPeriodReopeningRow { Id = Guid.NewGuid(), CompanyId = company.CompanyId,
            PeriodId = Guid.NewGuid(), ClosureId = Guid.NewGuid(), ClosedPeriodVersion = 3, ProposedBy = Guid.NewGuid(),
            ProposedAtUtc = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero), Reason = "SENTETIK", Version = 1 };
        db.Attach(row); row.ApprovedBy = fault == "self-approval" ? row.ProposedBy : Guid.NewGuid();
        row.ApprovedAtUtc = row.ProposedAtUtc.AddMinutes(1); row.Version = fault == "version-skipped" ? 3 : 2;
        if (fault == "source-changed") row.ClosureId = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
}
