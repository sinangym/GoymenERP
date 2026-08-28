using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Organization.Domain.Companies;
using GoymenERP.Modules.Organization.Domain.Locations;
using GoymenERP.Modules.Organization.Domain.Sites;
using GoymenERP.Modules.Organization.Domain.WorkCenters;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Organization.UnitTests;

public sealed class OrganizationTenantQueryFilterTests
{
    [Fact]
    public void Model_HasExpectedCompanyQueryFilterBoundaries()
    {
        using var dbContext = CreateDbContext(
            $"organization-filter-metadata-{Guid.NewGuid():N}",
            ResolvedCompanyContext(Guid.NewGuid()));

        Assert.Empty(dbContext.Model.FindEntityType(typeof(Company))!.GetDeclaredQueryFilters());
        Assert.NotEmpty(dbContext.Model.FindEntityType(typeof(Site))!.GetDeclaredQueryFilters());
        Assert.NotEmpty(dbContext.Model.FindEntityType(typeof(Location))!.GetDeclaredQueryFilters());
        Assert.NotEmpty(dbContext.Model.FindEntityType(typeof(WorkCenter))!.GetDeclaredQueryFilters());
        Assert.NotEmpty(
            dbContext.Model.FindEntityType(typeof(CommandExecution))!.GetDeclaredQueryFilters());
        Assert.NotEmpty(
            dbContext.Model.FindEntityType(typeof(AuditEvent))!.GetDeclaredQueryFilters());
    }

    [Fact]
    public async Task AuditEventQuery_IsCompanyScoped_AndUnresolvedContextFailsClosed()
    {
        var databaseName = $"organization-audit-filter-{Guid.NewGuid():N}";
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var companyAEvent = CreateAuditEvent(companyA, "company-a");
        var companyBEvent = CreateAuditEvent(companyB, "company-b");

        await using (var seedContext = CreateDbContext(
                         databaseName,
                         ResolvedCompanyContext(companyA)))
        {
            seedContext.Set<AuditEvent>().AddRange(companyAEvent, companyBEvent);
            await seedContext.SaveChangesAsync();
        }

        await using (var companyAContext = CreateDbContext(
                         databaseName,
                         ResolvedCompanyContext(companyA)))
        {
            var visibleEvents = await companyAContext.Set<AuditEvent>()
                .AsNoTracking()
                .ToListAsync();

            Assert.Equal(companyA, Assert.Single(visibleEvents).CompanyId);
        }

        await using (var companyBContext = CreateDbContext(
                         databaseName,
                         ResolvedCompanyContext(companyB)))
        {
            var visibleEvents = await companyBContext.Set<AuditEvent>()
                .AsNoTracking()
                .ToListAsync();

            Assert.Equal(companyB, Assert.Single(visibleEvents).CompanyId);
        }

        await using var unresolvedContext = CreateDbContext(databaseName, new CompanyContext());
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => unresolvedContext.Set<AuditEvent>().AsNoTracking().ToListAsync());
        Assert.Equal("Şirket bağlamı henüz belirlenmedi.", exception.Message);
    }

    private static OrganizationDbContext CreateDbContext(
        string databaseName,
        ICompanyContext companyContext)
    {
        var options = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new OrganizationDbContext(
            options,
            companyContext,
            new OrganizationBootstrapWriteScope());
    }

    private static CompanyContext ResolvedCompanyContext(Guid companyId)
    {
        var companyContext = new CompanyContext();
        companyContext.Set(companyId);
        return companyContext;
    }

    private static AuditEvent CreateAuditEvent(Guid companyId, string discriminator)
    {
        var envelope = new AuditEventEnvelope(
            Guid.NewGuid(),
            companyId,
            DateTimeOffset.UtcNow,
            "organization.query-filter.test.v1",
            "OrganizationQueryFilterProbe",
            discriminator,
            "Read",
            null,
            null,
            AuditActorTypes.System,
            "organization-query-filter-tests",
            "test.organization.audit.query-filter",
            "[]",
            "{}",
            "{}",
            "OrganizationDbContext audit görünürlük testi",
            null,
            AuditAiInvolvement.None,
            null,
            SourceChannels.System,
            null,
            null,
            $"organization-filter-{discriminator}-{Guid.NewGuid():N}",
            null,
            new string('A', 64),
            AuditOutcomes.Succeeded,
            1);
        return AuditEvent.Create(
            new AuditEventData(envelope, AuditEventIntegrity.Compute(envelope)));
    }
}
