using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Manufacturing.Application;
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
        var gated = Context("Testing", "CONFIRMED", "mfg");
        ManufacturingEndpoints.SetFixtureDiagnostic(
            gated, ManufacturingApiStatus.Unavailable, "counterparty_provider_unavailable");
        Assert.Equal("mfg_plan_party_account",
            gated.Response.Headers[ManufacturingEndpoints.FixtureDiagnosticHeaderName].ToString());

        foreach (var context in new[]
                 {
                     Context("Development", "CONFIRMED", "mfg"),
                     Context("Testing", "UNCONFIRMED", "mfg"),
                     Context("Testing", "CONFIRMED", "c01"),
                 })
        {
            ManufacturingEndpoints.SetFixtureDiagnostic(
                context, ManufacturingApiStatus.Unavailable, "counterparty_provider_unavailable");
            Assert.False(context.Response.Headers.ContainsKey(
                ManufacturingEndpoints.FixtureDiagnosticHeaderName));
        }

        var success = Context("Testing", "CONFIRMED", "mfg");
        ManufacturingEndpoints.SetFixtureDiagnostic(
            success, ManufacturingApiStatus.Ok, "counterparty_provider_unavailable");
        Assert.False(success.Response.Headers.ContainsKey(
            ManufacturingEndpoints.FixtureDiagnosticHeaderName));
    }

    private static DefaultHttpContext Context(string environment, string host, string profile)
    {
        var services = new ServiceCollection()
            .AddSingleton<IHostEnvironment>(new TestEnvironment(environment))
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["BrowserSuiteFixture:Host"] = host,
                    ["BrowserSuiteApi:Profile"] = profile,
                }).Build())
            .BuildServiceProvider();
        return new DefaultHttpContext { RequestServices = services };
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "fixture-diagnostic-tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
