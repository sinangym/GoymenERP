using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed class ManufacturingPlanPriorityPersistenceTests
{
    [Fact]
    public void Priority_history_is_company_filtered_append_only_and_references_its_own_company_plan()
    {
        var company = new CompanyContext(); company.Set(Guid.Parse("10000000-0000-0000-0000-000000000001"));
        using var db = new ManufacturingDbContext(new DbContextOptionsBuilder<ManufacturingDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PlanPriorityVersionRow))!;
        Assert.Equal(("manufacturing", "PlanPriorityVersions"), (entity.GetSchema(), entity.GetTableName()));
        Assert.Equal(["CompanyId", "PlanId", "Revision"], entity.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Single(entity.GetDeclaredQueryFilters());
        Assert.Contains(entity.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(PlanRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "PlanId"])
            && key.DeleteBehavior == DeleteBehavior.Restrict);
        var row = new PlanPriorityVersionRow { CompanyId = company.CompanyId, PlanId = Guid.NewGuid(), Revision = 1,
            Score = 7, RecordedBy = "synthetic", RecordedAtUtc = DateTimeOffset.UtcNow, Reason = "synthetic", CorrelationId = "synthetic" };
        db.Attach(row); row.Score = 8;
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }
}
