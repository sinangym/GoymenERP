using System.Text.Json;
using System.Text.RegularExpressions;
using GoymenERP.Api.IntegrationTests.Fixtures;
using Microsoft.Data.SqlClient;

return await C01E2eFixtureTool.RunAsync(args);

internal static partial class C01E2eFixtureTool
{
    private const string ConnectionVariable =
        "GOYMEN_ERP_C01_E2E_SQLSERVER_CONNECTION";
    private const string DisposableConfirmationVariable =
        "GOYMEN_ERP_C01_E2E_DISPOSABLE";
    private const string EvidencePathVariable =
        "GOYMEN_ERP_C01_E2E_FIXTURE_EVIDENCE";
    private const string TestedCommitVariable = "GOYMEN_ERP_C01_E2E_TESTED_COMMIT";
    private const string RunIdVariable = "GOYMEN_ERP_C01_E2E_RUN_ID";
    private const string ComposeProjectVariable = "GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT";

    private const string BaselineEvidencePathVariable =
        "GOYMEN_ERP_C01_E2E_BASELINE_EVIDENCE";

    internal static async Task<int> RunAsync(string[] arguments)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        try
        {
            if (arguments.Length != 1
                || arguments[0] is not ("materialize" or "verify-readonly"))
            {
                throw new InvalidOperationException(
                    "Kullanım: materialize | verify-readonly");
            }

            var settings = ReadSettings();
            var builder = ValidateDisposableConnection(settings.ConnectionString);
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();

            return arguments[0] == "materialize"
                ? await MaterializeAsync(connection, settings, startedAtUtc)
                : await VerifyReadOnlyAsync(connection, settings, startedAtUtc);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"C-01 fixture operation failed: {exception.GetType().Name}: "
                + exception.Message);
            return 1;
        }
    }

    private static async Task<int> MaterializeAsync(
        SqlConnection connection,
        C01E2eFixtureToolSettings settings,
        DateTimeOffset startedAtUtc)
    {
        var auditBefore = await CountAsync(
            connection,
            "SELECT COUNT_BIG(*) FROM [audit].[AuditEvents];");
        var executionsBefore = await CountAsync(
            connection,
            "SELECT COUNT_BIG(*) FROM [idempotency].[CommandExecutions];");

        using var artifact = C01E2eGoldenFixture.Load();
        await C01E2eGoldenFixture.ApplyAsync(connection, artifact);

        try
        {
            await C01E2eGoldenFixture.ApplyAsync(connection, artifact);
            throw new InvalidOperationException(
                "Fixture ikinci uygulamada tanımlı fail-fast üretmedi.");
        }
        catch (SqlException exception) when (exception.Number == 51021)
        {
            // Expected: exact fixture is already present and no duplicate was written.
        }

        var database = await C01E2eGoldenFixtureDatabaseVerifier.VerifyAsync(connection);
        if (database.AuditEventCount != auditBefore
            || database.CommandExecutionCount != executionsBefore)
        {
            throw new InvalidOperationException(
                "Fixture setup AuditEvent veya CommandExecution üretmemelidir.");
        }

        var evidence = new C01E2eFixtureEvidence(
            SchemaVersion: 1,
            SuiteId: "C01-E2E-INFRASTRUCTURE-V1",
            Repository: "sinangym/GoymenERP",
            TestedCommit: settings.TestedCommit,
            RunId: settings.RunId,
            ComposeProject: settings.ComposeProject,
            Fixture: new C01E2eFixtureArtifactEvidence(
                C01E2eGoldenFixture.FixtureId,
                SchemaVersion: 1,
                artifact.Bytes.Length,
                artifact.Sha256),
            Database: CreateJsonEvidence(
                database,
                auditDelta: database.AuditEventCount - auditBefore,
                commandExecutionDelta: database.CommandExecutionCount - executionsBefore),
            DuplicateApplyOutcome: "fixture_already_applied:51021",
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: DateTimeOffset.UtcNow);

        await WriteEvidenceAsync(settings.EvidencePath, evidence);
        Console.WriteLine(
            $"{C01E2eGoldenFixture.FixtureId} materialized and verified: "
            + $"{database.PartyCount} parties, {database.AccountCount} accounts, "
            + $"SHA-256 {artifact.Sha256}.");
        return 0;
    }

    private static async Task<int> VerifyReadOnlyAsync(
        SqlConnection connection,
        C01E2eFixtureToolSettings settings,
        DateTimeOffset startedAtUtc)
    {
        var baselinePath = Path.GetFullPath(RequiredVariable(BaselineEvidencePathVariable));
        var baseline = JsonSerializer.Deserialize<C01E2eFixtureEvidence>(
                await File.ReadAllTextAsync(baselinePath),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Fixture baseline kanıtı okunamadı.");
        ValidateBaselineIdentity(baseline, settings);

        using var artifact = C01E2eGoldenFixture.Load();
        if (baseline.Fixture.ByteLength != artifact.Bytes.Length
            || !string.Equals(
                baseline.Fixture.Sha256,
                artifact.Sha256,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Fixture baseline artifact kimliği değişti.");
        }

        var database = await C01E2eGoldenFixtureDatabaseVerifier.VerifyAsync(connection);
        var auditDelta = database.AuditEventCount - baseline.Database.AuditEventCount;
        var executionDelta =
            database.CommandExecutionCount - baseline.Database.CommandExecutionCount;
        if (auditDelta != 0 || executionDelta != 0)
        {
            throw new InvalidOperationException(
                "C-01 salt-okuma browser koşusu AuditEvent veya CommandExecution üretmemelidir.");
        }

        var current = CreateJsonEvidence(database, auditDelta, executionDelta);
        if (!SameFixtureDatabase(baseline.Database, current))
        {
            throw new InvalidOperationException(
                "Browser koşusu golden fixture cardinality veya sequence durumunu değiştirdi.");
        }

        var evidence = new C01E2eReadOnlyVerificationEvidence(
            SchemaVersion: 1,
            SuiteId: "C01-E2E-POST-BROWSER-SQL-V1",
            Repository: "sinangym/GoymenERP",
            TestedCommit: settings.TestedCommit,
            RunId: settings.RunId,
            ComposeProject: settings.ComposeProject,
            Fixture: baseline.Fixture,
            Database: current,
            BaselineEvidenceFile: Path.GetFileName(baselinePath),
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: DateTimeOffset.UtcNow);
        await WriteEvidenceAsync(settings.EvidencePath, evidence);
        Console.WriteLine("C-01 post-browser SQL read-only verification passed.");
        return 0;
    }

    private static C01E2eFixtureDatabaseJsonEvidence CreateJsonEvidence(
        C01E2eGoldenFixtureDatabaseEvidence database,
        long auditDelta,
        long commandExecutionDelta)
    {
        return new C01E2eFixtureDatabaseJsonEvidence(
            database.PartyCount,
            database.AccountCount,
            database.GdcAccountCount,
            database.GdcGlobalActiveVisibleCount,
            database.GdcGlobalInactiveCount,
            database.GspGlobalActiveVisibleCount,
            SequenceNextValue: database.Sequence.CurrentValue,
            AuditEventCount: database.AuditEventCount,
            CommandExecutionCount: database.CommandExecutionCount,
            FixtureAuditDelta: auditDelta,
            FixtureCommandExecutionDelta: commandExecutionDelta);
    }

    private static void ValidateBaselineIdentity(
        C01E2eFixtureEvidence baseline,
        C01E2eFixtureToolSettings settings)
    {
        if (baseline.SchemaVersion != 1
            || baseline.SuiteId != "C01-E2E-INFRASTRUCTURE-V1"
            || baseline.Repository != "sinangym/GoymenERP"
            || baseline.TestedCommit != settings.TestedCommit
            || baseline.RunId != settings.RunId
            || baseline.ComposeProject != settings.ComposeProject
            || baseline.Fixture.Id != C01E2eGoldenFixture.FixtureId
            || baseline.Fixture.SchemaVersion != 1)
        {
            throw new InvalidOperationException(
                "Fixture baseline kanıtı bu exact run/tree ile eşleşmiyor.");
        }
    }

    private static bool SameFixtureDatabase(
        C01E2eFixtureDatabaseJsonEvidence baseline,
        C01E2eFixtureDatabaseJsonEvidence current)
    {
        return baseline.PartyCount == current.PartyCount
            && baseline.AccountCount == current.AccountCount
            && baseline.GdcAccountCount == current.GdcAccountCount
            && baseline.GdcGlobalActiveVisibleCount == current.GdcGlobalActiveVisibleCount
            && baseline.GdcGlobalInactiveCount == current.GdcGlobalInactiveCount
            && baseline.GspGlobalActiveVisibleCount == current.GspGlobalActiveVisibleCount
            && baseline.SequenceNextValue == current.SequenceNextValue;
    }

    private static C01E2eFixtureToolSettings ReadSettings()
    {
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

        var evidencePath = Path.GetFullPath(RequiredVariable(EvidencePathVariable));
        if (!string.Equals(Path.GetExtension(evidencePath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Fixture kanıtı .json dosyası olmalıdır.");
        }

        return new C01E2eFixtureToolSettings(
            RequiredVariable(ConnectionVariable),
            evidencePath,
            testedCommit.ToLowerInvariant(),
            RequiredVariable(RunIdVariable),
            composeProject);
    }

    private static SqlConnectionStringBuilder ValidateDisposableConnection(
        string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var server = builder.DataSource.Split(',', 2, StringSplitOptions.TrimEntries)[0];
        if (!string.Equals(server, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(server, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Fixture aracı yalnız loopback SQL Server hedefinde çalışabilir.");
        }

        if (!string.Equals(builder.InitialCatalog, "GoymenErp", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Fixture aracı yalnız disposable GoymenErp veritabanını hedefleyebilir.");
        }

        if (builder.IntegratedSecurity || string.IsNullOrWhiteSpace(builder.UserID))
        {
            throw new InvalidOperationException(
                "Disposable E2E SQL kimliği açık ve izole olmalıdır.");
        }

        return builder;
    }

    private static async Task<long> CountAsync(SqlConnection connection, string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var value = await command.ExecuteScalarAsync();
        return value is long count
            ? count
            : throw new InvalidOperationException("E2E baseline COUNT_BIG sonucu geçersiz.");
    }

    private static async Task WriteEvidenceAsync<T>(
        string path,
        T evidence)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        var json = JsonSerializer.Serialize(
            evidence,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true,
            });
        await File.WriteAllTextAsync(temporaryPath, json + Environment.NewLine);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static string RequiredVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} zorunludur.");
        }

        return value.Trim();
    }

    [GeneratedRegex("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex ComposeProjectPattern();
}

internal sealed record C01E2eFixtureToolSettings(
    string ConnectionString,
    string EvidencePath,
    string TestedCommit,
    string RunId,
    string ComposeProject);

internal sealed record C01E2eFixtureEvidence(
    int SchemaVersion,
    string SuiteId,
    string Repository,
    string TestedCommit,
    string RunId,
    string ComposeProject,
    C01E2eFixtureArtifactEvidence Fixture,
    C01E2eFixtureDatabaseJsonEvidence Database,
    string DuplicateApplyOutcome,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);

internal sealed record C01E2eFixtureArtifactEvidence(
    string Id,
    int SchemaVersion,
    int ByteLength,
    string Sha256);

internal sealed record C01E2eFixtureDatabaseJsonEvidence(
    long PartyCount,
    long AccountCount,
    long GdcAccountCount,
    long GdcGlobalActiveVisibleCount,
    long GdcGlobalInactiveCount,
    long GspGlobalActiveVisibleCount,
    long SequenceNextValue,
    long AuditEventCount,
    long CommandExecutionCount,
    long FixtureAuditDelta,
    long FixtureCommandExecutionDelta);

internal sealed record C01E2eReadOnlyVerificationEvidence(
    int SchemaVersion,
    string SuiteId,
    string Repository,
    string TestedCommit,
    string RunId,
    string ComposeProject,
    C01E2eFixtureArtifactEvidence Fixture,
    C01E2eFixtureDatabaseJsonEvidence Database,
    string BaselineEvidenceFile,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);
