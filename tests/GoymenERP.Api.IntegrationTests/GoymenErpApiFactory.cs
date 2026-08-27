using GoymenERP.Modules.Organization.Infrastructure.Persistence;
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
                ["BootstrapIdentity:HeaderName"] = "X-Development-User",
                ["BootstrapIdentity:Users:0:UserId"] = TestUsers.CompanyA,
                ["BootstrapIdentity:Users:0:DisplayName"] = "Şirket A Test Kullanıcısı",
                ["BootstrapIdentity:Users:0:AllowedCompanyIds:0"] = TestCompanyIds.CompanyA.ToString(),
                ["BootstrapIdentity:Users:1:UserId"] = TestUsers.CompanyB,
                ["BootstrapIdentity:Users:1:DisplayName"] = "Şirket B Test Kullanıcısı",
                ["BootstrapIdentity:Users:1:AllowedCompanyIds:0"] = TestCompanyIds.CompanyB.ToString(),
                ["BootstrapIdentity:Users:2:UserId"] = TestUsers.GroupAdmin,
                ["BootstrapIdentity:Users:2:DisplayName"] = "Grup Test Yöneticisi",
                ["BootstrapIdentity:Users:2:AllowedCompanyIds:0"] = TestCompanyIds.CompanyA.ToString(),
                ["BootstrapIdentity:Users:2:AllowedCompanyIds:1"] = TestCompanyIds.CompanyB.ToString(),
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<OrganizationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<OrganizationDbContext>>();
            services.AddDbContext<OrganizationDbContext>(options =>
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
}

public sealed record LocationSnapshot(Guid Id, bool IsActive, long Version);
