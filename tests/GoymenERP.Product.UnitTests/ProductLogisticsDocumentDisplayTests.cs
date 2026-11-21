using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductLogisticsDocumentDisplayTests
{
    private static readonly Guid Company = Guid.Parse("58000000-0000-4000-8000-000000000050");
    [Fact]
    public async Task Denied_authority_and_changed_actor_refuse_before_opening_the_Product_connection()
    {
        using var db = new ProductDbContext(new DbContextOptionsBuilder<ProductDbContext>().UseSqlServer("Server=SENTETIK.invalid;Database=SENTETIK;Connect Timeout=1;").Options);
        var company = new CompanyContext(); company.Set(Company); var user = new User(); var authority = new Authority();
        var owner = new ProductLogisticsDocumentDisplay(db, company, user, authority);
        Assert.Equal(ProductLogisticsDocumentDisplayOutcomeV1.Forbidden, (await owner.ReadAsync(Company, Guid.NewGuid())).Outcome);
        Assert.Equal(ErpPermissions.LogisticsLoadingInstructionsRead, Assert.Single(authority.Permissions));
        authority.Granted = true; authority.DuringRead = () => user.UserId = "SENTETIK-other";
        Assert.Equal(ProductLogisticsDocumentDisplayOutcomeV1.Forbidden, (await owner.ReadAsync(Company, Guid.NewGuid())).Outcome);
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }
    [Fact]
    public async Task Foreign_company_empty_identity_and_cancellation_do_not_query_global_products()
    {
        using var db = new ProductDbContext(new DbContextOptionsBuilder<ProductDbContext>().UseSqlServer("Server=SENTETIK.invalid;Database=SENTETIK;Connect Timeout=1;").Options);
        var company = new CompanyContext(); company.Set(Company); var authority = new Authority { Granted = true };
        var owner = new ProductLogisticsDocumentDisplay(db, company, new User(), authority);
        Assert.Equal(ProductLogisticsDocumentDisplayOutcomeV1.Forbidden, (await owner.ReadAsync(Guid.NewGuid(), Guid.NewGuid())).Outcome);
        Assert.Empty(authority.Permissions);
        Assert.Equal(ProductLogisticsDocumentDisplayOutcomeV1.NotFound, (await owner.ReadAsync(Company, Guid.Empty)).Outcome);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner.ReadAsync(Company, Guid.NewGuid(), cancelled.Token));
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }
    private sealed class User : ICurrentUser
    { public bool IsAuthenticated => true; public string UserId { get; set; } = "SENTETIK-actor"; public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company }; public bool CanAccessCompany(Guid id) => AllowedCompanyIds.Contains(id); }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public bool Granted; public Action? DuringRead; public List<string> Permissions { get; } = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        { Permissions.Add(permission); DuringRead?.Invoke(); return Task.FromResult(Granted ? new(true, false, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) : PermissionScope.Denied); }
    }
}
