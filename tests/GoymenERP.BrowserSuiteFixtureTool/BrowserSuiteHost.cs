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
    string loopbackConnectionString) : WebApplicationFactory<global::Program>
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
                    pair.Value))));
        builder.ConfigureLogging(logging => logging.ClearProviders());
    }
}
