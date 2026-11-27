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

public sealed class ProductPackageStandardApprovalSafetyTests
{
    [Theory]
    [InlineData("missing-writer")] [InlineData("dirty")] [InlineData("audit")] [InlineData("ambient")]
    [InlineData("nonrelational-clean")] [InlineData("nonrelational-audit")]
    public async Task Both_decisions_refuse_caller_state_without_owner_reads_or_audit_cleanup(string boundary)
    {
        foreach (var approve in new[] { true, false })
        {
            var company = new CompanyContext(); company.Set(Guid.NewGuid()); var user = new User(); var authority = new Authority();
            var options = new DbContextOptionsBuilder<ProductPriceListDbContext>();
            if (boundary.StartsWith("nonrelational", StringComparison.Ordinal)) options.UseInMemoryDatabase("SENTETIK-package-entry-" + Guid.NewGuid());
            else options.UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;");
            await using var database = new ProductPriceListDbContext(options.Options, company);
            var caller = new ProductPackageStandardDecisionRow { CompanyId = company.CompanyId, Version = 1, DecisionId = Guid.NewGuid() };
            if (boundary == "dirty") database.PackageStandardDecisions.Add(caller);
            var writer = new Writer { HasPendingDrafts = boundary is "audit" or "nonrelational-audit" };
            var core = new ProductPackageStandardService(database, company, user, authority, new AuditEventCollector(), TimeProvider.System);
            var runtime = new GuardedProductPackageStandardService(core, boundary == "missing-writer" ? null : writer);
            var metadata = new ProductPackageStandardCommandMetadataV2(company.CompanyId, user.UserId, "SENTETIK", null, "Web", null, null, "entry");
            using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
            var ambientBefore = Transaction.Current;
            var result = approve ? await runtime.ApproveAsync(1, new("2026-11-27", "SENTETIK"), metadata)
                : await runtime.RejectAsync(1, new("SENTETIK"), metadata);
            Assert.Equal(503, result.Answer.StatusCode); Assert.False(result.IsReplay);
            Assert.Equal(0, authority.Calls); Assert.Equal((0, 0), (writer.Writes, writer.Discards));
            Assert.Equal(boundary is "audit" or "nonrelational-audit", writer.HasPendingDrafts);
            Assert.Equal(boundary == "dirty", database.ChangeTracker.HasChanges());
            if (boundary == "dirty") Assert.Same(caller, Assert.Single(database.ChangeTracker.Entries()).Entity);
            Assert.Same(ambientBefore, Transaction.Current);
        }
    }

    [Fact]
    public void All_runtime_aliases_resolve_the_same_guarded_facade_and_never_the_raw_synthetic_core()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        using var database = new ProductPriceListDbContext(new DbContextOptionsBuilder<ProductPriceListDbContext>()
            .UseInMemoryDatabase("SENTETIK-package-di-" + Guid.NewGuid()).Options, company);
        var services = new ServiceCollection();
        services.AddProductPriceLists(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["DB_CONNECTION_STRING"] = "Server=unused.invalid;Database=unused;Connect Timeout=1;" }).Build());
        services.AddSingleton(database); services.AddSingleton<ICompanyContext>(company);
        services.AddSingleton<ICurrentUser>(new User()); services.AddSingleton<IErpAuthorityRevalidationService>(new Authority());
        services.AddSingleton<IAuditEventCollector>(new AuditEventCollector());
        services.AddSingleton<IAuditTransactionWriterV1>(new Writer());
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        var guarded = Assert.IsType<GuardedProductPackageStandardService>(scope.ServiceProvider.GetRequiredService<IProductPackageStandardServiceV3>());
        Assert.Same(guarded, scope.ServiceProvider.GetRequiredService<IProductPackageStandardServiceV2>());
        Assert.Same(guarded, scope.ServiceProvider.GetRequiredService<IProductPackageStandardReaderV2>());
        Assert.Same(guarded, scope.ServiceProvider.GetRequiredService<IProductPackageStandardReaderV3>());
        Assert.Null(scope.ServiceProvider.GetService<ProductPackageStandardService>());
        Assert.DoesNotContain(services, registration => registration.ServiceType == typeof(ProductPackageStandardService));
    }

    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true; public string UserId => "SENTETIK package checker";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>();
        public bool CanAccessCompany(Guid id) => true;
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public int Calls;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default)
        { Calls++; return Task.FromResult(PermissionScope.Denied); }
    }
    private sealed class Writer : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts { get; set; } public int Writes, Discards;
        public Task WritePendingAsync(IDbTransaction transaction, CancellationToken token = default)
        { Writes++; throw new InvalidOperationException("No actual Audit write at an entry refusal."); }
        public void DiscardPending() { Discards++; HasPendingDrafts = false; }
    }
}
