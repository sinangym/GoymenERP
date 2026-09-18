using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.BrowserSuiteFixtureTool;

/// <summary>
/// `materialize --suite &lt;suite&gt;`: runs one suite owner's seed against the run's disposable database
/// through the real API host, then writes the seed outputs, the evidence file and one SUITE_FIXTURE line.
/// The disposable-run rules are the C-01 fixture tool's, unchanged (the workflow contract test pins
/// both tools to the same literals). SQL is used here only to count audit and command rows.
/// </summary>
internal static partial class BrowserSuiteFixtureTool
{
    private const string ConnectionVariable = "GOYMEN_ERP_C01_E2E_SQLSERVER_CONNECTION";
    private const string DisposableConfirmationVariable = "GOYMEN_ERP_C01_E2E_DISPOSABLE";
    private const string TestedCommitVariable = "GOYMEN_ERP_C01_E2E_TESTED_COMMIT";
    private const string RunIdVariable = "GOYMEN_ERP_C01_E2E_RUN_ID";
    private const string ComposeProjectVariable = "GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT";
    private const string SuiteVariable = "BROWSER_SUITE";
    private const string ApiEnvironmentVariable = "BROWSER_E2E_API_ENVIRONMENT";
    private const string EvidencePathVariable = "BROWSER_E2E_SUITE_FIXTURE_EVIDENCE";
    private const string SeedPathVariable = "BROWSER_E2E_SUITE_SEED_FILE";

    public static async Task<int> Main(string[] arguments)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        try
        {
            if (arguments is not ["materialize", "--suite", var suite])
            {
                throw new InvalidOperationException("Kullanım: materialize --suite <suite>");
            }

            var settings = ReadSettings(suite);
            var connection = ValidateDisposableConnection(
                settings.ConnectionString,
                settings.RunId,
                settings.ComposeProject);
            var fixtures = BrowserSuiteFixtures.Discover(typeof(BrowserSuiteFixtureTool).Assembly);
            if (!fixtures.TryGetValue(suite, out var fixture))
            {
                throw new InvalidOperationException($"Browser suite '{suite}' has no fixture.");
            }

            var before = await CountAsync(connection.ConnectionString);
            IReadOnlyDictionary<string, string> outputs;
            int requests;
            await using (var host = new BrowserSuiteHost(
                             settings.RepositoryRoot,
                             settings.ApiEnvironment,
                             connection.ConnectionString,
                             new BrowserSuiteRunBinding(
                                 settings.TestedCommit,
                                 settings.RunId,
                                 settings.ComposeProject)))
            {
                using (host.CreateClient())
                {
                    // Creating the first client builds and starts the host (Program.cs as-is).
                }

                var actors = host.Services.GetRequiredService<IConfiguration>()
                    .GetSection("BootstrapIdentity:Users")
                    .GetChildren()
                    .Select(user => user["UserId"])
                    .OfType<string>()
                    .ToHashSet(StringComparer.Ordinal);
                var context = new BrowserSuiteFixtureContext(
                    suite,
                    host.Services,
                    actors,
                    handlers => host.CreateDefaultClient(handlers));
                outputs = await fixture.SeedAsync(context, CancellationToken.None);
                ValidateOutputs(outputs, actors);
                requests = context.Requests;
            }

            var after = await CountAsync(connection.ConnectionString);
            await WriteJsonAsync(settings.SeedPath, new SortedDictionary<string, string>(
                outputs.ToDictionary(pair => pair.Key, pair => pair.Value),
                StringComparer.Ordinal));
            await WriteJsonAsync(settings.EvidencePath, new BrowserSuiteFixtureEvidence(
                SchemaVersion: 1,
                SuiteId: "BROWSER-SUITE-FIXTURE-V1",
                Suite: suite,
                TestedCommit: settings.TestedCommit,
                RunId: settings.RunId,
                ComposeProject: settings.ComposeProject,
                Requests: requests,
                AuditEventsBefore: before.AuditEvents,
                AuditEventsAfter: after.AuditEvents,
                CommandExecutionsBefore: before.CommandExecutions,
                CommandExecutionsAfter: after.CommandExecutions,
                OutputKeys: outputs.Keys.Order(StringComparer.Ordinal).ToArray(),
                StartedAtUtc: startedAtUtc,
                CompletedAtUtc: DateTimeOffset.UtcNow));
            Console.WriteLine(
                $"SUITE_FIXTURE suite={suite} head={settings.TestedCommit} requests={requests} "
                + $"audit_delta={after.AuditEvents - before.AuditEvents} "
                + $"command_delta={after.CommandExecutions - before.CommandExecutions} "
                + $"outputs={outputs.Count}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Browser suite fixture failed: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    internal static void ValidateOutputs(
        IReadOnlyDictionary<string, string> outputs,
        IReadOnlySet<string> actors)
    {
        foreach (var (key, value) in outputs)
        {
            if (!OutputKeyPattern().IsMatch(key))
            {
                throw new InvalidOperationException($"Seed output key '{key}' is not a safe key.");
            }

            var isGuid = Guid.TryParseExact(value, "D", out var guid)
                         && guid != Guid.Empty
                         && string.Equals(value, guid.ToString("D"), StringComparison.Ordinal);
            if (!isGuid && !actors.Contains(value))
            {
                throw new InvalidOperationException(
                    $"Seed output '{key}' must be a lowercase GUID or an actor id of this run.");
            }
        }
    }

    private static BrowserSuiteFixtureSettings ReadSettings(string suite)
    {
        if (!SuitePattern().IsMatch(suite)
            || !string.Equals(Environment.GetEnvironmentVariable(SuiteVariable), suite, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"--suite must be a safe suite id equal to {SuiteVariable}.");
        }

        if (!string.Equals(
                Environment.GetEnvironmentVariable(DisposableConfirmationVariable),
                "CONFIRMED",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{DisposableConfirmationVariable}=CONFIRMED olmadan fixture yazılamaz.");
        }

        var testedCommit = RequiredVariable(TestedCommitVariable);
        if (!CommitPattern().IsMatch(testedCommit))
        {
            throw new InvalidOperationException(
                $"{TestedCommitVariable} exact 40 karakter Git SHA olmalıdır.");
        }

        var composeProject = RequiredVariable(ComposeProjectVariable);
        if (!ComposeProjectPattern().IsMatch(composeProject))
        {
            throw new InvalidOperationException(
                $"{ComposeProjectVariable} güvenli compose proje adı değildir.");
        }

        var repositoryRoot = Directory.GetCurrentDirectory();
        if (!File.Exists(Path.Combine(repositoryRoot, "GoymenERP.slnx")))
        {
            throw new InvalidOperationException("The tool must run from the repository root.");
        }

        var apiEnvironment = JsonSerializer.Deserialize<Dictionary<string, string>>(
                                 File.ReadAllText(RequiredVariable(ApiEnvironmentVariable)))
                             ?? throw new InvalidOperationException("The API environment file is empty.");

        return new BrowserSuiteFixtureSettings(
            repositoryRoot,
            RequiredVariable(ConnectionVariable),
            JsonPath(EvidencePathVariable),
            JsonPath(SeedPathVariable),
            testedCommit.ToLowerInvariant(),
            RequiredVariable(RunIdVariable),
            composeProject,
            apiEnvironment);
    }

    private static SqlConnectionStringBuilder ValidateDisposableConnection(
        string connectionString,
        string runId,
        string composeProject)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var server = builder.DataSource.Split(',', 2, StringSplitOptions.TrimEntries)[0];
        if (!string.Equals(server, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(server, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Fixture aracı yalnız loopback SQL Server hedefinde çalışabilir.");
        }

        var projectPrefix = $"goymen-c01-{runId}-";
        var attempt = composeProject.StartsWith(projectPrefix, StringComparison.Ordinal)
            ? composeProject[projectPrefix.Length..]
            : string.Empty;
        if (runId.Any(character => character is < '0' or > '9')
            || attempt.Length == 0
            || attempt.Any(character => character is < '0' or > '9'))
        {
            throw new InvalidOperationException(
                "Fixture runner ve compose proje kimliği birbiriyle eşleşmelidir.");
        }

        var expectedDatabase = $"GoymenErpC01_{runId}_{attempt}";
        if (!string.Equals(builder.InitialCatalog, expectedDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Fixture aracı yalnız exact run'a ait disposable veritabanını hedefleyebilir.");
        }

        if (builder.IntegratedSecurity || string.IsNullOrWhiteSpace(builder.UserID))
        {
            throw new InvalidOperationException(
                "Disposable E2E SQL kimliği açık ve izole olmalıdır.");
        }

        return builder;
    }

    private static async Task<(long AuditEvents, long CommandExecutions)> CountAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        return (
            await CountAsync(connection, "SELECT COUNT_BIG(*) FROM [audit].[AuditEvents];"),
            await CountAsync(connection, "SELECT COUNT_BIG(*) FROM [idempotency].[CommandExecutions];"));
    }

    private static async Task<long> CountAsync(SqlConnection connection, string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return await command.ExecuteScalarAsync() is long count
            ? count
            : throw new InvalidOperationException("COUNT_BIG sonucu geçersiz.");
    }

    private static async Task WriteJsonAsync<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        await File.WriteAllTextAsync(
            temporaryPath,
            JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true,
            }) + Environment.NewLine);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static string JsonPath(string variable)
    {
        var path = Path.GetFullPath(RequiredVariable(variable));
        return string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase)
            ? path
            : throw new InvalidOperationException($"{variable} must name a .json file.");
    }

    private static string RequiredVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{name} zorunludur.")
            : value.Trim();
    }

    [GeneratedRegex("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex ComposeProjectPattern();

    [GeneratedRegex("^[a-z][a-z0-9]{1,15}$", RegexOptions.CultureInvariant)]
    private static partial Regex SuitePattern();

    [GeneratedRegex("^[a-z][A-Za-z0-9]{0,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex OutputKeyPattern();
}

internal sealed record BrowserSuiteFixtureSettings(
    string RepositoryRoot,
    string ConnectionString,
    string EvidencePath,
    string SeedPath,
    string TestedCommit,
    string RunId,
    string ComposeProject,
    IReadOnlyDictionary<string, string> ApiEnvironment);

internal sealed record BrowserSuiteFixtureEvidence(
    int SchemaVersion,
    string SuiteId,
    string Suite,
    string TestedCommit,
    string RunId,
    string ComposeProject,
    int Requests,
    long AuditEventsBefore,
    long AuditEventsAfter,
    long CommandExecutionsBefore,
    long CommandExecutionsAfter,
    string[] OutputKeys,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);
