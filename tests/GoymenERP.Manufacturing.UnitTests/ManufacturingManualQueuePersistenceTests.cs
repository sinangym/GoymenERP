using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed class ManufacturingManualQueuePersistenceTests
{
    [Fact]
    public async Task Queue_snapshots_are_company_bound_and_append_only_and_have_unique_positions_and_members()
    {
        var company = new CompanyContext(); company.Set(Guid.Parse("10000000-0000-0000-0000-000000000001"));
        await using var context = new ManufacturingDbContext(new DbContextOptionsBuilder<ManufacturingDbContext>()
            .UseSqlServer("Server=unopened.invalid;Database=synthetic;Integrated Security=true;Encrypt=false").Options, company);
        var model = context.GetService<IDesignTimeModel>().Model;
        var version = model.FindEntityType(typeof(WorkCenterQueueVersionRow))!;
        var entries = model.FindEntityType(typeof(WorkCenterQueueEntryRow))!;
        Assert.Single(version.GetDeclaredQueryFilters()); Assert.Single(entries.GetDeclaredQueryFilters());
        Assert.Contains(version.GetCheckConstraints(), check => check.Sql == ManufacturingDbContext.WorkCenterQueueIdentityCheck);
        Assert.Contains(version.GetCheckConstraints(), check => check.Sql == ManufacturingDbContext.WorkCenterQueueTextCheck);
        Assert.Equal(ManufacturingDbContext.WorkCenterQueueSequenceCheck, Assert.Single(entries.GetCheckConstraints()).Sql);
        Assert.Equal(["CompanyId", "WorkCenterId", "Revision"], version.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(["CompanyId", "WorkCenterId", "Revision", "Sequence"], entries.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(entries.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name)
            .SequenceEqual(["CompanyId", "WorkCenterId", "Revision", "PlanId"]));
        Assert.Contains(entries.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(PlanRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "PlanId"]));
        var stored = new WorkCenterQueueVersionRow { CompanyId = company.CompanyId, WorkCenterId = Guid.NewGuid(), Revision = 1,
            IsManual = true, RecordedBy = "synthetic", RecordedAtUtc = DateTimeOffset.UnixEpoch, Reason = "synthetic", CorrelationId = "synthetic" };
        context.WorkCenterQueueVersions.Attach(stored); stored.Reason = "changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        context.WorkCenterQueueEntries.Add(new WorkCenterQueueEntryRow { CompanyId = Guid.NewGuid(), WorkCenterId = Guid.NewGuid(), Revision = 1, Sequence = 1, PlanId = Guid.NewGuid() });
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }
}
