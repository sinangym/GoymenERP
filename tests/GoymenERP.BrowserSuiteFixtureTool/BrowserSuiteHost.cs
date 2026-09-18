using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GoymenERP.BrowserSuiteFixtureTool;

/// <summary>
/// The real API host (Program.cs, unchanged) in process, configured with exactly the API container's
/// validated environment for this suite (base compose file plus the suite override), and pointed at
/// the run's disposable database over loopback instead of the compose network name.
/// </summary>
internal sealed class BrowserSuiteHost(
    string repositoryRoot,
    IReadOnlyDictionary<string, string> apiEnvironment,
    string loopbackConnectionString,
    BrowserSuiteRunBinding runBinding) : WebApplicationFactory<global::Program>
{
    private const string ConnectionKey = "DB_CONNECTION_STRING";
    private const string EnvironmentKey = "ASPNETCORE_ENVIRONMENT";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (!apiEnvironment.TryGetValue(EnvironmentKey, out var environment)
            || !string.Equals(environment, "Testing", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The browser suite host runs only with the container's Testing environment.");
        }

        builder.UseEnvironment(environment);
        builder.UseContentRoot(Path.Combine(repositoryRoot, "src", "backend", "GoymenERP.Api"));
        builder.UseSetting(ConnectionKey, loopbackConnectionString);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            apiEnvironment
                .Where(pair => pair.Key is not (ConnectionKey or EnvironmentKey))
                .Select(pair => new KeyValuePair<string, string?>(
                    pair.Key.Replace("__", ":", StringComparison.Ordinal),
                    pair.Value))
                .Concat(MaskedProcessEnvelope())
                .Concat(FixtureWriterBinding())));
        builder.ConfigureLogging(logging => logging.ClearProviders());
    }

    /// <summary>
    /// Opens the gated fixture writers (Api/Shell/C01E2eFixtureWriterActivation) in this host only: the
    /// runner is the one place these keys are set, from its already validated disposable run.
    /// </summary>
    private IEnumerable<KeyValuePair<string, string?>> FixtureWriterBinding() =>
    [
        new("BrowserSuiteFixture:Host", "CONFIRMED"),
        new("BrowserSuiteFixture:TestedCommit", runBinding.TestedCommit),
        new("BrowserSuiteFixture:RunId", runBinding.RunId),
        new("BrowserSuiteFixture:ComposeProject", runBinding.ComposeProject),
    ];

    /// <summary>
    /// The runner's own process carries the run's C-01 envelope variables (the workflow exports them for
    /// every step), but the API container of a normal-profile suite does not. Any envelope key the
    /// container's environment lacks is masked here (a later null value wins), so the in-process host
    /// never activates a capability the container did not.
    /// </summary>
    private IEnumerable<KeyValuePair<string, string?>> MaskedProcessEnvelope() =>
        Environment.GetEnvironmentVariables().Keys
            .OfType<string>()
            .Where(name => (name.StartsWith("GOYMEN_ERP_C01_E2E_", StringComparison.Ordinal)
                            || name.StartsWith("C01E2e__", StringComparison.Ordinal))
                           && !apiEnvironment.ContainsKey(name))
            .Select(name => new KeyValuePair<string, string?>(
                name.Replace("__", ":", StringComparison.Ordinal),
                null));
}

/// <summary>The validated disposable run the in-process host is bound to.</summary>
internal sealed record BrowserSuiteRunBinding(string TestedCommit, string RunId, string ComposeProject);
