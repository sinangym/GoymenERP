using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
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
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<OrganizationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<OrganizationDbContext>>();
            services.AddDbContext<OrganizationDbContext>((serviceProvider, options) =>
                options.UseInMemoryDatabase(_databaseName)
                    .AddInterceptors(serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>()));
            services.RemoveAll<DbContextOptions<IdentityDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<IdentityDbContext>>();
            services.AddDbContext<IdentityDbContext>(options =>
                options.UseInMemoryDatabase($"{_databaseName}-identity"));
            services.RemoveAll<DbContextOptions<AuditDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AuditDbContext>>();
            services.AddDbContext<AuditDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
        });
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
}

public static class TestCompanyIds
{
    public static readonly Guid CompanyA = Guid.Parse(
        "10000000-0000-0000-0000-000000000001");

    public static readonly Guid CompanyB = Guid.Parse(
        "10000000-0000-0000-0000-000000000002");
}

public static class TestUsers
{
    public const string CompanyA = "company-a-user";

    public const string CompanyB = "company-b-user";

    public const string GroupAdmin = "group-bootstrap-admin";

    public const string CompanyAViewer = "company-a-viewer";

    public const string Unprovisioned = "unprovisioned-user";

    public const string SiteAuditReader = "site-audit-reader";
}

public sealed record LocationSnapshot(Guid Id, bool IsActive, long Version);
