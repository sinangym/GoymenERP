using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Locations;
using GoymenERP.Modules.Organization.Domain.Sites;
using GoymenERP.Modules.Organization.Domain.WorkCenters;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Organization.UnitTests;

public sealed class OrganizationWorkCenterPlanningReferenceReaderTests
{
    private static readonly Guid CompanyA = Guid.Parse("0f000000-0000-4000-8000-00000000000a");
    private static readonly Guid CompanyB = Guid.Parse("0f000000-0000-4000-8000-00000000000b");
    private static readonly Guid WorkCenterA = Guid.Parse("0f300000-0000-4000-8000-000000000001");
    private static readonly Guid WorkCenterB = Guid.Parse("0f300000-0000-4000-8000-000000000002");

    [Theory]
    [InlineData(WorkCenterKind.Paint, "Paint")]
    [InlineData(WorkCenterKind.TubeProfile, "TubeProfile")]
    [InlineData(WorkCenterKind.Slitting, "Slitting")]
    [InlineData(WorkCenterKind.ColdRolling, "ColdRolling")]
    [InlineData(WorkCenterKind.CutToLength, "CutToLength")]
    public async Task Reads_the_work_centers_canonical_kind(WorkCenterKind kind, string expected)
    {
        var database = $"organization-work-center-reference-{Guid.NewGuid():N}";
        await SeedAsync(database, CompanyA, WorkCenterA, kind);

        var reference = await Reader(database, Resolved(CompanyA)).ReadAsync(CompanyA, WorkCenterA);

        Assert.Equal(new OrganizationWorkCenterPlanningReferenceV1(WorkCenterA, expected), reference);
    }

    [Fact]
    public async Task Foreign_unknown_and_empty_work_centers_are_not_found()
    {
        var database = $"organization-work-center-scope-{Guid.NewGuid():N}";
        await SeedAsync(database, CompanyA, WorkCenterA, WorkCenterKind.Paint);
        await SeedAsync(database, CompanyB, WorkCenterB, WorkCenterKind.Slitting);

        var reader = Reader(database, Resolved(CompanyA));
        Assert.Null(await reader.ReadAsync(CompanyA, WorkCenterB));
        Assert.Null(await reader.ReadAsync(CompanyA, Guid.NewGuid()));
        Assert.Null(await reader.ReadAsync(CompanyA, Guid.Empty));
    }

    [Fact]
    public async Task A_company_outside_the_resolved_context_is_refused()
    {
        var database = $"organization-work-center-context-{Guid.NewGuid():N}";
        await SeedAsync(database, CompanyA, WorkCenterA, WorkCenterKind.ColdRolling);

        await Assert.ThrowsAsync<OrganizationCompanyContextMismatchException>(
            () => Reader(database, Resolved(CompanyB)).ReadAsync(CompanyA, WorkCenterA));
        await Assert.ThrowsAsync<OrganizationCompanyContextMismatchException>(
            () => Reader(database, new CompanyContext()).ReadAsync(CompanyA, WorkCenterA));
    }

    [Fact]
    public void The_module_read_depends_on_no_user_or_permission_service()
    {
        var parameters = typeof(OrganizationWorkCenterPlanningReferenceReader).GetConstructors().Single()
            .GetParameters().Select(parameter => parameter.ParameterType);

        Assert.Equal([typeof(OrganizationDbContext), typeof(ICompanyContext)], parameters);
    }

    private static OrganizationWorkCenterPlanningReferenceReader Reader(string database, ICompanyContext context) =>
        new(Context(database, context), context);

    private static async Task SeedAsync(string database, Guid companyId, Guid workCenterId, WorkCenterKind kind)
    {
        await using var context = Context(database, Resolved(companyId));
        var siteId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        context.Sites.Add(new Site(siteId, companyId, $"SITE-{workCenterId:N}", "Sentetik saha", SiteType.Factory));
        context.Locations.Add(new Location(locationId, companyId, siteId, $"LOC-{workCenterId:N}", "Sentetik hol", false));
        context.WorkCenters.Add(new WorkCenter(
            workCenterId, companyId, locationId, $"WC-{workCenterId:N}", "Sentetik iş merkezi", kind));
        await context.SaveChangesAsync();
    }

    private static CompanyContext Resolved(Guid companyId)
    {
        var context = new CompanyContext();
        context.Set(companyId);
        return context;
    }

    private static OrganizationDbContext Context(string database, ICompanyContext companyContext) => new(
        new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase(database).Options,
        companyContext,
        new OrganizationBootstrapWriteScope());
}
