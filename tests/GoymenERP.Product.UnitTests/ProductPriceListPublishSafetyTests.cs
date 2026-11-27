using System.Data;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductPriceListPublishSafetyTests
{
    [Theory]
    [InlineData("missing-writer")] [InlineData("dirty")] [InlineData("audit")] [InlineData("ambient")]
    [InlineData("nonrelational-clean")] [InlineData("nonrelational-audit")]
    public async Task Publish_entry_refuses_without_reading_or_clearing_caller_work(string boundary)
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid()); var user = new User(); var authority = new Authority();
        var options = new DbContextOptionsBuilder<ProductPriceListDbContext>();
        if (boundary.StartsWith("nonrelational", StringComparison.Ordinal)) options.UseInMemoryDatabase("SENTETIK-publish-" + Guid.NewGuid());
        else options.UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;");
        await using var database = new ProductPriceListDbContext(options.Options, company);
        var caller = new ProductPackageStandardDecisionRow { CompanyId = company.CompanyId, Version = 1, DecisionId = Guid.NewGuid() };
        if (boundary == "dirty") database.PackageStandardDecisions.Add(caller);
        var collector = new AuditEventCollector(); var writer = new Writer { HasPendingDrafts = boundary is "audit" or "nonrelational-audit" };
        var core = new ProductPriceListService(company, user, authority, new(database, company, collector, TimeProvider.System), TimeProvider.System, null, _ => true);
        var facade = new GuardedProductPriceListService(core, boundary == "missing-writer" ? null : writer);
        using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
        var before = Transaction.Current;
        var exception = await Assert.ThrowsAsync<ProductPriceListException>(() => facade.PublishAsync(Guid.NewGuid(), new("1", 1),
            new(user.UserId, ProductPriceListPermissionsV1.Publish, [], "SENTETIK", null, "key", "Web")));
        Assert.Equal((503, ProductPriceListCodesV1.Unavailable), (exception.Status, exception.Code));
        Assert.Equal(0, authority.Calls); Assert.Equal((0, 0), (writer.Writes, writer.Discards));
        Assert.Equal(boundary is "audit" or "nonrelational-audit", writer.HasPendingDrafts);
        Assert.Equal(boundary == "dirty", database.ChangeTracker.HasChanges());
        if (boundary == "dirty") Assert.Same(caller, Assert.Single(database.ChangeTracker.Entries()).Entity);
        Assert.Same(before, Transaction.Current);
    }

    [Fact]
    public async Task Unresolved_company_keeps_native_authentication_then_membership_refusal_without_database_access()
    {
        foreach (var authenticated in new[] { false, true })
        {
            var company = new CompanyContext(); var user = new User { Authenticated = authenticated };
            var authority = new Authority(); var writer = new Writer();
            await using var database = new ProductPriceListDbContext(new DbContextOptionsBuilder<ProductPriceListDbContext>()
                .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
            var core = new ProductPriceListService(company, user, authority,
                new(database, company, new AuditEventCollector(), TimeProvider.System), TimeProvider.System, null, _ => true);
            var facade = new GuardedProductPriceListService(core, writer);
            var error = await Assert.ThrowsAsync<ProductPriceListException>(() => facade.PublishAsync(Guid.NewGuid(), new("1", 1),
                new(user.UserId, ProductPriceListPermissionsV1.Publish, [], "SENTETIK", null, "key", "Web")));
            Assert.Equal(authenticated ? 403 : 401, error.Status);
            Assert.Equal(0, authority.Calls); Assert.Equal((0, 0), (writer.Writes, writer.Discards)); Assert.False(database.ChangeTracker.HasChanges());
            // This initial refusal never enters the owned scope or opens a database transaction.
            Assert.Null(database.Database.CurrentTransaction);
        }
    }

    [Theory]
    [InlineData(false, "dirty")] [InlineData(true, "dirty")] [InlineData(false, "audit")] [InlineData(true, "audit")]
    public async Task Unresolved_company_entry_guard_still_preserves_caller_work_before_any_owned_cleanup(bool authenticated, string boundary)
    {
        var company = new CompanyContext(); var user = new User { Authenticated = authenticated }; var authority = new Authority();
        var writer = new Writer { HasPendingDrafts = boundary == "audit" };
        await using var database = new ProductPriceListDbContext(new DbContextOptionsBuilder<ProductPriceListDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        var caller = new ProductPackageStandardDecisionRow { CompanyId = Guid.NewGuid(), Version = 1, DecisionId = Guid.NewGuid() };
        if (boundary == "dirty") database.PackageStandardDecisions.Add(caller);
        var core = new ProductPriceListService(company, user, authority,
            new(database, company, new AuditEventCollector(), TimeProvider.System), TimeProvider.System, null, _ => true);
        var facade = new GuardedProductPriceListService(core, writer);
        var error = await Assert.ThrowsAsync<ProductPriceListException>(() => facade.PublishAsync(Guid.NewGuid(), new("1", 1),
            new(user.UserId, ProductPriceListPermissionsV1.Publish, [], "SENTETIK", null, "key", "Web")));
        Assert.Equal((503, ProductPriceListCodesV1.Unavailable), (error.Status, error.Code));
        Assert.Equal(0, authority.Calls); Assert.Equal((0, 0), (writer.Writes, writer.Discards));
        Assert.Equal(boundary == "audit", writer.HasPendingDrafts);
        Assert.Equal(boundary == "dirty", database.ChangeTracker.HasChanges());
        if (boundary == "dirty") Assert.Same(caller, Assert.Single(database.ChangeTracker.Entries()).Entity);
    }

    [Fact]
    public async Task First_fresh_authority_await_cannot_admit_new_caller_work_into_the_owned_publish_cleanup()
    {
        foreach (var pendingAudit in new[] { false, true })
        {
            var company = new CompanyContext(); company.Set(Guid.NewGuid()); var user = new User();
            var writer = new Writer(); var authority = new Authority { Granted = true };
            await using var database = new ProductPriceListDbContext(new DbContextOptionsBuilder<ProductPriceListDbContext>()
                .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
            var caller = new ProductPackageStandardDecisionRow { CompanyId = company.CompanyId, Version = 1, DecisionId = Guid.NewGuid() };
            authority.AfterRead = () =>
            {
                if (pendingAudit) writer.HasPendingDrafts = true;
                else database.PackageStandardDecisions.Add(caller);
            };
            var core = new ProductPriceListService(company, user, authority,
                new(database, company, new AuditEventCollector(), TimeProvider.System), TimeProvider.System, null, _ => true);
            var facade = new GuardedProductPriceListService(core, writer);
            var error = await Assert.ThrowsAsync<ProductPriceListException>(() => facade.PublishAsync(Guid.NewGuid(), new("1", 1),
                new(user.UserId, ProductPriceListPermissionsV1.Publish, [], "SENTETIK", null, "key", "Web")));
            Assert.Equal((503, ProductPriceListCodesV1.Unavailable), (error.Status, error.Code));
            Assert.Equal(1, authority.Calls); Assert.Equal((0, 0), (writer.Writes, writer.Discards));
            Assert.Equal(pendingAudit, writer.HasPendingDrafts); Assert.Equal(!pendingAudit, database.ChangeTracker.HasChanges());
            if (!pendingAudit) Assert.Same(caller, Assert.Single(database.ChangeTracker.Entries()).Entity);
            Assert.Null(database.Database.CurrentTransaction);
        }
    }

    [Fact]
    public void Runtime_price_list_interface_resolves_only_the_native_guarded_facade()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        using var database = new ProductPriceListDbContext(new DbContextOptionsBuilder<ProductPriceListDbContext>()
            .UseInMemoryDatabase("SENTETIK-publish-di-" + Guid.NewGuid()).Options, company);
        var services = new ServiceCollection();
        services.AddProductPriceLists(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["DB_CONNECTION_STRING"] = "Server=unused.invalid;Database=unused;Connect Timeout=1;" }).Build());
        services.AddSingleton(database); services.AddSingleton<ICompanyContext>(company); services.AddSingleton<ICurrentUser>(new User());
        services.AddSingleton<IErpAuthorityRevalidationService>(new Authority()); services.AddSingleton<IAuditEventCollector>(new AuditEventCollector());
        services.AddSingleton<IAuditTransactionWriterV1>(new Writer());
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        Assert.IsType<GuardedProductPriceListService>(scope.ServiceProvider.GetRequiredService<IProductPriceListServiceV1>());
        Assert.Null(scope.ServiceProvider.GetService<ProductPriceListService>());
        Assert.DoesNotContain(services, registration => registration.ServiceType == typeof(ProductPriceListService));
    }
    private sealed class User : ICurrentUser
    { public bool Authenticated = true; public bool IsAuthenticated => Authenticated; public string UserId => "SENTETIK-publisher"; public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>(); public bool CanAccessCompany(Guid id) => true; }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public int Calls; public bool Granted; public Action? AfterRead;
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string p, Guid c, CancellationToken t = default)
        {
            Calls++; await Task.Yield(); AfterRead?.Invoke();
            return Granted ? new(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) : PermissionScope.Denied;
        }
    }
    private sealed class Writer : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts { get; set; } public int Writes, Discards;
        public Task WritePendingAsync(IDbTransaction tx, CancellationToken token = default) { Writes++; throw new InvalidOperationException("Entry refusal must not flush."); }
        public void DiscardPending() { Discards++; HasPendingDrafts = false; }
    }
}
