using System.Net;
using Microsoft.AspNetCore.Cors.Infrastructure;
using GoymenERP.Api.Shell;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GoymenERP.CariAcceptance006;

public sealed class AcceptanceFactory(RunSettings identity) : WebApplicationFactory<global::Program>
{
    public void StartLoopback()
    {
        identity.Validate("Testing");
        UseKestrel(options => options.Listen(IPAddress.Loopback, identity.ApiPort));
        StartServer();
        if (Services.GetRequiredService<ShellCapabilityRegistry>().Revision != AcceptanceProfile.Revision)
            throw new InvalidOperationException("C006 capability revision did not match.");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        identity.Validate("Testing");
        builder.UseEnvironment(identity.EnvironmentName);
        builder.UseContentRoot(Path.Combine(RunSettings.ApprovedRoot, "src/backend/GoymenERP.Api"));
        builder.UseSetting("DB_CONNECTION_STRING", identity.NewOwnedDatabaseConnection());
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["DB_PROVIDER"] = "SqlServer",
                ["Authentication:Mode"] = "DevelopmentBootstrap",
                ["BootstrapIdentity:HeaderName"] = "X-Development-User",
                ["BootstrapIdentity:Users:0:UserId"] = "company-a-user",
                ["BootstrapIdentity:Users:0:DisplayName"] = "C006 Sentetik A Kullanıcısı",
                ["BootstrapIdentity:Users:1:UserId"] = "company-b-user",
                ["BootstrapIdentity:Users:1:DisplayName"] = "C006 Sentetik B Kullanıcısı",
                ["BootstrapIdentity:Users:2:UserId"] = "group-bootstrap-admin",
                ["BootstrapIdentity:Users:2:DisplayName"] = "C006 Sentetik A/B Testçisi",
                ["BootstrapIdentity:Users:3:UserId"] = "company-a-viewer",
                ["BootstrapIdentity:Users:3:DisplayName"] = "C006 Sentetik Salt Okuma",
                ["OrganizationSeed:Companies:0:LegalName"] = "C006 Sentetik Şirket A",
                ["OrganizationSeed:Companies:0:Vkn"] = "1111111111",
                ["OrganizationSeed:Companies:0:IsPlaceholder"] = "true",
                ["OrganizationSeed:Companies:0:InvoiceCapable"] = "false",
                ["OrganizationSeed:Companies:1:LegalName"] = "C006 Sentetik Şirket B",
                ["OrganizationSeed:Companies:1:Vkn"] = "1111111111",
                ["OrganizationSeed:Companies:1:IsPlaceholder"] = "true",
                ["OrganizationSeed:Companies:1:InvoiceCapable"] = "false",
                ["OrganizationSeed:Companies:2:LegalName"] = "C006 Sentetik Kullanılmayan Şirket C",
                ["Cors:AllowedOrigins:0"] = "http://127.0.0.1:25173",
                ["Cors:AllowedOrigins:1"] = "http://localhost:25173",
            }));
        builder.ConfigureServices((context, services) =>
        {
            AcceptanceProfile.Configure(services, context.HostingEnvironment.EnvironmentName, identity);
            // Test-host-only policy: Program's eager origin capture is not broadened or edited.
            services.PostConfigure<CorsOptions>(options => options.AddDefaultPolicy(policy =>
                policy.WithOrigins("http://127.0.0.1:25173", "http://localhost:25173")
                    .AllowAnyHeader().WithExposedHeaders(SourceBinding.ClientContractHeader).AllowAnyMethod()));
        });
        builder.ConfigureLogging(logging => logging.ClearProviders());
    }
}
