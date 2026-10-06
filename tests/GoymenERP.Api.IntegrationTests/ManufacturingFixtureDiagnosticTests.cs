using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ManufacturingFixtureDiagnosticTests
{
    [Theory]
    [InlineData("counterparty_provider_unavailable", "mfg_plan_party_account")]
    [InlineData("inventory_parent_roll_unavailable", "mfg_plan_parent_roll")]
    [InlineData("product_manufacturing_technical_snapshot_unavailable", "mfg_plan_product_snapshot")]
    [InlineData("product_runtime_unconfigured", "mfg_plan_product_snapshot")]
    [InlineData("settings_provider_unavailable", "mfg_plan_settings_profile")]
    [InlineData("settings_profile_invalid", "mfg_plan_settings_profile")]
    public void Maps_only_closed_owner_failure_codes(string code, string expected)
    {
        Assert.Equal(expected, ManufacturingEndpoints.FixtureDiagnosticPhase(code));
        Assert.Null(ManufacturingEndpoints.FixtureDiagnosticPhase("unexpected_owner_detail"));
        Assert.Null(ManufacturingEndpoints.FixtureDiagnosticPhase(null));
    }

    [Fact]
    public void Emits_phase_only_inside_exact_testing_mfg_fixture_gate()
    {
        var gated = Context("Testing", "mfg", fixtureGate: true);
        ManufacturingEndpoints.SetFixtureDiagnostic(
            gated, ManufacturingApiStatus.Unavailable, "counterparty_provider_unavailable");
        Assert.Equal("mfg_plan_party_account",
            gated.Response.Headers[ManufacturingEndpoints.FixtureDiagnosticHeaderName].ToString());

        foreach (var context in new[]
                 {
                     Context("Development", "mfg", fixtureGate: true),
                     Context("Testing", "mfg", fixtureGate: false),
                     Context("Testing", "c01", fixtureGate: true),
                 })
        {
            ManufacturingEndpoints.SetFixtureDiagnostic(
                context, ManufacturingApiStatus.Unavailable, "counterparty_provider_unavailable");
            Assert.False(context.Response.Headers.ContainsKey(
                ManufacturingEndpoints.FixtureDiagnosticHeaderName));
        }

        var success = Context("Testing", "mfg", fixtureGate: true);
        ManufacturingEndpoints.SetFixtureDiagnostic(
            success, ManufacturingApiStatus.Ok, "counterparty_provider_unavailable");
        Assert.False(success.Response.Headers.ContainsKey(
            ManufacturingEndpoints.FixtureDiagnosticHeaderName));
    }

    private static DefaultHttpContext Context(string environment, string profile, bool fixtureGate)
    {
        var registrations = new ServiceCollection()
            .AddSingleton<IHostEnvironment>(new TestEnvironment(environment))
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["BrowserSuiteApi:Profile"] = profile,
                }).Build());
        if (fixtureGate)
        {
            var identity = new TestIdentity();
            registrations.AddSingleton<ISettingsManufacturingToleranceProviderV1>(
                new C01E2eManufacturingToleranceFixtureProvider(identity, identity));
        }

        return new DefaultHttpContext { RequestServices = registrations.BuildServiceProvider() };
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "fixture-diagnostic-tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestIdentity : ICompanyContext, ICurrentUser
    {
        public bool IsResolved => true;
        public Guid CompanyId => Guid.Parse("10000000-0000-0000-0000-000000000001");
        public bool IsAuthenticated => true;
        public string UserId => "fixture-diagnostic-user";
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { CompanyId };
        public bool CanAccessCompany(Guid companyId) => companyId == CompanyId;
        public void Set(Guid companyId) => throw new NotSupportedException();
    }
}
