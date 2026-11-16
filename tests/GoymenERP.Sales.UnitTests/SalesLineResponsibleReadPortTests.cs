using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Sales.UnitTests;

public sealed class SalesLineResponsibleReadPortTests
{
    [Fact]
    public async Task Unresolved_company_fails_closed_without_attempting_a_database_read()
    {
        var company = new CompanyContext();
        await using var database = Database(company);
        var result = await new SalesLineResponsibleReadPort(database, company).ReadAsync(Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(SalesLineResponsibleOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.ResponsibleUserId);
        Assert.Null(result.ObservedOrderVersion);
    }

    [Fact]
    public async Task Missing_line_identity_never_substitutes_an_actor_or_role()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        await using var database = Database(company);
        var result = await new SalesLineResponsibleReadPort(database, company).ReadAsync(Guid.NewGuid(), Guid.Empty);
        Assert.Equal(SalesLineResponsibleOutcomeV1.NotFound, result.Outcome);
        Assert.Equal(company.CompanyId, result.CompanyId);
        Assert.Null(result.ResponsibleUserId);
    }

    [Fact]
    public async Task Cancelled_lookup_is_not_reported_as_missing_or_successful()
    {
        var company = new CompanyContext();
        await using var database = Database(company);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SalesLineResponsibleReadPort(database, company)
            .ReadAsync(Guid.NewGuid(), Guid.NewGuid(), cancellation.Token));
    }

    private static SalesDbContext Database(CompanyContext company) => new(new DbContextOptionsBuilder<SalesDbContext>()
        .UseSqlServer("Server=(local);Database=ModelOnly;Integrated Security=true;TrustServerCertificate=true").Options, company);
}
