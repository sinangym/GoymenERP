using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace GoymenERP.BrowserSuiteFixtureTool;

/// <summary>
/// The real API host (Program.cs, unchanged) in process, configured with exactly the API container's
/// validated environment for this suite (base compose file plus the suite override), and pointed at
/// the run's disposable database over loopback instead of the compose network name.
/// </summary>
/// <remarks>
/// Program reads its shell and fixture-writer gates BEFORE it builds the app, from the configuration the
/// builder was created with: the process environment and the host settings (<c>UseSetting</c>). App
/// configuration sources added by the factory reach the app only after that point, so none of the values
/// below may travel that way (u02 run 35411370499 failed on exactly this: the process's C-01
/// envelope reached the shell gate, and an in-memory null mask did not).
/// </remarks>
internal sealed class BrowserSuiteHost : WebApplicationFactory<global::Program>
{
    private const string ConnectionKey = "DB_CONNECTION_STRING";
    private const string EnvironmentKey = "ASPNETCORE_ENVIRONMENT";

    private readonly string _repositoryRoot;
    private readonly IReadOnlyDictionary<string, string> _apiEnvironment;
    private readonly string _loopbackConnectionString;
    private readonly BrowserSuiteRunBinding _runBinding;

    private BrowserSuiteHost(
        string repositoryRoot,
        IReadOnlyDictionary<string, string> apiEnvironment,
        string loopbackConnectionString,
        BrowserSuiteRunBinding runBinding)
    {
        _repositoryRoot = repositoryRoot;
        _apiEnvironment = apiEnvironment;
        _loopbackConnectionString = loopbackConnectionString;
        _runBinding = runBinding;
    }

    /// <summary>
    /// The only way to get a host. The runner's own process carries the run's C-01 envelope variables
    /// (the workflow exports them to every step), but the API container of a normal-profile suite does
    /// not: every envelope variable the container's environment lacks is removed from this process first
    /// (by prefix, so a new one is removed too). The runner has already read its own settings, including
    /// the disposable SQL connection, before this call.
    /// </summary>
    internal static BrowserSuiteHost Create(
        string repositoryRoot,
        IReadOnlyDictionary<string, string> apiEnvironment,
        string loopbackConnectionString,
        BrowserSuiteRunBinding runBinding)
    {
        RemoveProcessEnvelope(apiEnvironment);
        return new BrowserSuiteHost(repositoryRoot, apiEnvironment, loopbackConnectionString, runBinding);
    }

    internal static IReadOnlyList<string> RemoveProcessEnvelope(IReadOnlyDictionary<string, string> apiEnvironment)
    {
        var removed = Environment.GetEnvironmentVariables().Keys
            .OfType<string>()
            .Where(name => (name.StartsWith("GOYMEN_ERP_C01_E2E_", StringComparison.Ordinal)
                            || name.StartsWith("C01E2e__", StringComparison.Ordinal))
                           && !apiEnvironment.ContainsKey(name))
            .Order(StringComparer.Ordinal)
            .ToArray();
        foreach (var name in removed)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        return removed;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (!_apiEnvironment.TryGetValue(EnvironmentKey, out var environment)
            || !string.Equals(environment, "Testing", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The browser suite host runs only with the container's Testing environment.");
        }

        builder.UseEnvironment(environment);
        builder.UseContentRoot(Path.Combine(_repositoryRoot, "src", "backend", "GoymenERP.Api"));
        builder.UseSetting(ConnectionKey, _loopbackConnectionString);
        foreach (var (key, value) in _apiEnvironment)
        {
            if (key is not (ConnectionKey or EnvironmentKey))
            {
                builder.UseSetting(key.Replace("__", ":", StringComparison.Ordinal), value);
            }
        }

        foreach (var (key, value) in FixtureWriterBinding())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureLogging(logging => logging.ClearProviders());
    }

    /// <summary>
    /// Opens the gated fixture writers (Api/Shell/C01E2eFixtureWriterActivation) in this host only: the
    /// runner is the one place these keys are set, from its already validated disposable run.
    /// </summary>
    private IEnumerable<KeyValuePair<string, string>> FixtureWriterBinding() =>
    [
        new("BrowserSuiteFixture:Host", "CONFIRMED"),
        new("BrowserSuiteFixture:TestedCommit", _runBinding.TestedCommit),
        new("BrowserSuiteFixture:RunId", _runBinding.RunId),
        new("BrowserSuiteFixture:ComposeProject", _runBinding.ComposeProject),
    ];
}

/// <summary>The validated disposable run the in-process host is bound to.</summary>
internal sealed record BrowserSuiteRunBinding(string TestedCommit, string RunId, string ComposeProject);
