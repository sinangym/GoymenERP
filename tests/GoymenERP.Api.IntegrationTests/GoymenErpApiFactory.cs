using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Idempotency.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class GoymenErpApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"goymen-erp-tests-{Guid.NewGuid():N}";

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        // Existing application tests are compatible-client tests. Legacy-client
        // acceptance explicitly removes this declaration and verifies fail-closed behavior.
        client.DefaultRequestHeaders.Add(
            GoymenERP.Api.Contracts.CounterpartyClientContract.HeaderName,
            GoymenERP.Api.Contracts.CounterpartyClientContract.Version);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting(
            "DB_CONNECTION_STRING",
            "Server=testing;Database=testing;User Id=testing;Password=testing;");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Mode"] = "DevelopmentBootstrap",
                ["BootstrapIdentity:HeaderName"] = "X-Development-User",
                ["BootstrapIdentity:Users:0:UserId"] = TestUsers.CompanyA,
                ["BootstrapIdentity:Users:0:DisplayName"] = "Şirket A Test Kullanıcısı",
                ["BootstrapIdentity:Users:1:UserId"] = TestUsers.CompanyB,
                ["BootstrapIdentity:Users:1:DisplayName"] = "Şirket B Test Kullanıcısı",
                ["BootstrapIdentity:Users:2:UserId"] = TestUsers.GroupAdmin,
                ["BootstrapIdentity:Users:2:DisplayName"] = "Grup Test Yöneticisi",
                ["BootstrapIdentity:Users:3:UserId"] = TestUsers.CompanyAViewer,
                ["BootstrapIdentity:Users:3:DisplayName"] = "Şirket A Salt Okuma Kullanıcısı",
                ["BootstrapIdentity:Users:4:UserId"] = TestUsers.Unprovisioned,
                ["BootstrapIdentity:Users:4:DisplayName"] = "ERP Kaydı Olmayan Kullanıcı",
                ["BootstrapIdentity:Users:5:UserId"] = TestUsers.SiteAuditReader,
                ["BootstrapIdentity:Users:5:DisplayName"] = "Dar Kapsam Audit Test Kullanıcısı",
                ["BootstrapIdentity:Users:6:UserId"] = TestUsers.NoAssignment,
                ["BootstrapIdentity:Users:6:DisplayName"] = "Atamasız ERP Test Kullanıcısı",
                // Runtime seed assigns physical topology only to GDC. Company-B
                // isolation tests still need a foreign tenant fixture, so the test
                // host explicitly provisions the same deterministic topology for GSP.
                ["OrganizationSeed:Companies:1:ApplyTopologyTemplates"] = "true",
            });
        });

        builder.ConfigureServices(services => UseInMemoryDatabases(services, _databaseName));
    }

    /// <summary>
    /// Replaces every host-wired module database with an in-memory one, so the in-process host starts
    /// without SQL Server. Shared with tests that start another factory over Program (e.g. the browser
    /// suite runner's host).
    /// </summary>
    internal static void UseInMemoryDatabases(IServiceCollection services, string databaseName)
    {
        services.RemoveAll<DbContextOptions<OrganizationDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<OrganizationDbContext>>();
        services.AddDbContext<OrganizationDbContext>((serviceProvider, options) =>
            options.UseInMemoryDatabase(databaseName)
                .AddInterceptors(serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>()));

        services.RemoveAll<DbContextOptions<CounterpartyDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<CounterpartyDbContext>>();
        services.AddDbContext<CounterpartyDbContext>(options =>
            options.UseInMemoryDatabase($"{databaseName}-counterparty"));

        services.RemoveAll<DbContextOptions<IdentityDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<IdentityDbContext>>();
        services.AddDbContext<IdentityDbContext>(options =>
            options.UseInMemoryDatabase($"{databaseName}-identity"));
        services.RemoveAll<DbContextOptions<AuditDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<AuditDbContext>>();
        services.AddDbContext<AuditDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        services.RemoveAll<DbContextOptions<CommandExecutionDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<CommandExecutionDbContext>>();
        services.AddDbContext<CommandExecutionDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));

        // Finance is host-wired since FIN-T04b (#320); without this the in-memory host would try
        // to migrate it against the placeholder SQL connection string at startup.
        services.RemoveAll<DbContextOptions<FinanceDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<FinanceDbContext>>();
        services.AddDbContext<FinanceDbContext>((serviceProvider, options) =>
            options.UseInMemoryDatabase($"{databaseName}-finance")
                .AddInterceptors(serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>()));

        // Sales is host-wired since SALES-API (#321); without this the in-memory host would try to
        // migrate it against the placeholder SQL connection string at startup.
        services.RemoveAll<DbContextOptions<SalesDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<SalesDbContext>>();
        services.AddDbContext<SalesDbContext>((serviceProvider, options) =>
            options.UseInMemoryDatabase($"{databaseName}-sales")
                .AddInterceptors(serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>()));

        // Logistics is host-wired since LOG-4a; without this the in-memory host would try to migrate
        // it against the placeholder SQL connection string at startup.
        services.RemoveAll<DbContextOptions<LogisticsDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<LogisticsDbContext>>();
        services.AddDbContext<LogisticsDbContext>((serviceProvider, options) =>
            options.UseInMemoryDatabase($"{databaseName}-logistics")
                .AddInterceptors(serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>()));

        // Purchasing is host-wired since PUR-4a; without this the in-memory host would try to migrate
        // it against the placeholder SQL connection string at startup.
        services.RemoveAll<DbContextOptions<PurchasingDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<PurchasingDbContext>>();
        services.AddDbContext<PurchasingDbContext>((serviceProvider, options) =>
            options.UseInMemoryDatabase($"{databaseName}-purchasing")
                .AddInterceptors(serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>()));

        // Manufacturing is host-wired since MFG-API (#313); without this the in-memory host
        // would try to migrate it against the placeholder SQL connection string at startup.
        services.RemoveAll<DbContextOptions<ManufacturingDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<ManufacturingDbContext>>();
        services.AddDbContext<ManufacturingDbContext>((serviceProvider, options) =>
            options.UseInMemoryDatabase($"{databaseName}-manufacturing")
                .AddInterceptors(serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>()));

        // Inventory is host-wired since T07(a) (#310); without this the in-memory host would
        // try to migrate it against the placeholder SQL connection string at startup.
        services.RemoveAll<DbContextOptions<InventoryDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<InventoryDbContext>>();
        services.AddDbContext<InventoryDbContext>((serviceProvider, options) =>
            options.UseInMemoryDatabase($"{databaseName}-inventory")
                .AddInterceptors(serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>()));
    }

    public HttpClient CreateClientFor(string userId, Guid? selectedCompanyId = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", userId);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        if (selectedCompanyId.HasValue)
        {
            client.DefaultRequestHeaders.Add(
                "X-Company-Id",
                selectedCompanyId.Value.ToString());
        }

        return client;
    }

    public async Task<LocationSnapshot> GetLocationIgnoringFiltersAsync(
        Guid companyId,
        string code)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        return await dbContext.Locations
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.Code == code)
            .Select(x => new LocationSnapshot(x.Id, x.IsActive, x.Version))
            .SingleAsync();
    }

    public async Task<IReadOnlyList<AuditEvent>> GetAuditEventsIgnoringFiltersAsync(
        string correlationId)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        return await dbContext.AuditEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CorrelationId == correlationId)
            .OrderBy(x => x.OccurredAtUtc)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<CommandExecution>> GetCommandExecutionsAsync(
        string idempotencyKey)
    {
        var keyHash = IdempotencyHash.ComputeKey(idempotencyKey);
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CommandExecutionDbContext>();
        return await dbContext.CommandExecutions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.IdempotencyKeyHash == keyHash)
            .ToListAsync();
    }

    public async Task ReplaceUserAssignmentScopeAsync(
        string externalSubject,
        Guid companyId,
        AccessScopeType scopeType,
        Guid scopeId)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var userId = await dbContext.ExternalIdentities
            .Where(x => x.Subject == externalSubject)
            .Select(x => x.UserId)
            .SingleAsync();
        var operatorRoleIds = dbContext.RolePermissions
            .Where(x => x.PermissionCode == "organization.locations.status.update")
            .Select(x => x.RoleId);
        var assignment = await dbContext.RoleAssignments
            .Include(x => x.Scopes)
            .SingleAsync(x =>
                x.UserId == userId
                && x.CompanyId == companyId
                && operatorRoleIds.Contains(x.RoleId));

        dbContext.RoleAssignmentScopes.RemoveRange(assignment.Scopes);
        assignment.AddScope(scopeType, scopeId);
        await dbContext.SaveChangesAsync();
    }
}

public static class TestCompanyIds
{
    public static readonly Guid CompanyA = Guid.Parse(
        "10000000-0000-0000-0000-000000000001");

    public static readonly Guid CompanyB = Guid.Parse(
        "10000000-0000-0000-0000-000000000002");

    public static readonly Guid CompanyC = Guid.Parse(
        "10000000-0000-0000-0000-000000000003");
}

public static class TestUsers
{
    public const string CompanyA = "company-a-user";

    public const string CompanyB = "company-b-user";

    public const string GroupAdmin = "group-bootstrap-admin";

    public const string CompanyAViewer = "company-a-viewer";

    public const string Unprovisioned = "unprovisioned-user";

    public const string SiteAuditReader = "site-audit-reader";

    public const string NoAssignment = "no-assignment-user";
}

public sealed record LocationSnapshot(Guid Id, bool IsActive, long Version);
