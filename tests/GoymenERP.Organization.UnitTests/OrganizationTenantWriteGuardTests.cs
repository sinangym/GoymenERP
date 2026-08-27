using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Organization.Domain.Sites;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Organization.UnitTests;

public sealed class OrganizationTenantWriteGuardTests
{
    [Fact]
    public async Task AddedTenantRecord_WithoutCompanyContext_FailsClosed()
    {
        var companyId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(new CompanyContext());
        dbContext.Sites.Add(CreateSite(companyId));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dbContext.SaveChangesAsync());

        Assert.Equal(
            "Organizasyon yazma işlemi için aktif şirket bağlamı zorunludur.",
            exception.Message);
    }

    [Fact]
    public async Task AddedTenantRecord_ForDifferentCompany_IsRejected()
    {
        var companyContext = new CompanyContext();
        companyContext.Set(Guid.NewGuid());
        await using var dbContext = CreateDbContext(companyContext);
        dbContext.Sites.Add(CreateSite(Guid.NewGuid()));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dbContext.SaveChangesAsync());

        Assert.Equal(
            "Organizasyon kaydının CompanyId değeri aktif şirket bağlamıyla eşleşmelidir.",
            exception.Message);
    }

    [Fact]
    public void ModifiedTenantRecord_ForDifferentCompany_IsRejectedOnSynchronousPath()
    {
        var companyContext = new CompanyContext();
        companyContext.Set(Guid.NewGuid());
        using var dbContext = CreateDbContext(companyContext);
        dbContext.Attach(CreateSite(Guid.NewGuid())).State = EntityState.Modified;

        var exception = Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());

        Assert.Equal(
            "Organizasyon kaydının CompanyId değeri aktif şirket bağlamıyla eşleşmelidir.",
            exception.Message);
    }

    [Fact]
    public async Task MatchingCompanyContext_AllowsNormalWrite()
    {
        var companyId = Guid.NewGuid();
        var companyContext = new CompanyContext();
        companyContext.Set(companyId);
        await using var dbContext = CreateDbContext(companyContext);
        dbContext.Sites.Add(CreateSite(companyId));

        var affectedRows = await dbContext.SaveChangesAsync();

        Assert.Equal(1, affectedRows);
    }

    [Fact]
    public async Task ExplicitBootstrapScope_AllowsMultiCompanySeedWriteWithoutRequestContext()
    {
        var companyContext = new CompanyContext();
        var bootstrapScope = new OrganizationBootstrapWriteScope();
        await using var dbContext = CreateDbContext(companyContext, bootstrapScope);
        dbContext.Sites.Add(CreateSite(Guid.NewGuid()));
        dbContext.Sites.Add(CreateSite(Guid.NewGuid()));

        int affectedRows;
        using (bootstrapScope.BeginBootstrapWrite())
        {
            affectedRows = await dbContext.SaveChangesAsync();
        }

        Assert.Equal(2, affectedRows);
        Assert.False(bootstrapScope.IsActive);
    }

    [Fact]
    public async Task IdempotencyRecord_WithoutMatchingCompanyContext_IsRejected()
    {
        var companyContext = new CompanyContext();
        companyContext.Set(Guid.NewGuid());
        await using var dbContext = CreateDbContext(companyContext);
        dbContext.CommandExecutions.Add(CreateCommandExecution(Guid.NewGuid()));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dbContext.SaveChangesAsync());

        Assert.Equal(
            "Idempotency kaydı aktif şirket bağlamıyla eşleşmelidir.",
            exception.Message);
    }

    [Fact]
    public void ExistingIdempotencyRecord_CannotBeModified()
    {
        var companyId = Guid.NewGuid();
        var companyContext = new CompanyContext();
        companyContext.Set(companyId);
        using var dbContext = CreateDbContext(companyContext);
        dbContext.Attach(CreateCommandExecution(companyId)).State = EntityState.Modified;

        var exception = Assert.Throws<InvalidOperationException>(() => dbContext.SaveChanges());

        Assert.Equal("Idempotency kayıtları değiştirilemez veya silinemez.", exception.Message);
    }

    private static OrganizationDbContext CreateDbContext(
        ICompanyContext companyContext,
        OrganizationBootstrapWriteScope? bootstrapScope = null)
    {
        var options = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseInMemoryDatabase($"organization-write-guard-{Guid.NewGuid():N}")
            .Options;
        return new OrganizationDbContext(
            options,
            companyContext,
            bootstrapScope ?? new OrganizationBootstrapWriteScope());
    }

    private static Site CreateSite(Guid companyId)
    {
        return new Site(
            Guid.NewGuid(),
            companyId,
            $"SITE-{Guid.NewGuid():N}",
            "Write Guard Test Site",
            SiteType.Factory);
    }

    private static CommandExecution CreateCommandExecution(Guid companyId)
    {
        return CommandExecution.Complete(new CommandExecutionData(
            Guid.NewGuid(),
            companyId,
            "test.command.v1",
            new string('A', 64),
            new string('B', 64),
            Guid.NewGuid().ToString("D"),
            "Succeeded",
            204,
            "{\"result\":\"Succeeded\"}",
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            1));
    }
}
