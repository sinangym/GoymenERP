using Microsoft.Data.SqlClient;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using System.Globalization;
using System.Text;

namespace GoymenERP.CariAcceptance006;

public sealed record Effects(int Parties, int Accounts, int CreateAudits, int LinkAudits, int CreateExecutions, int LinkExecutions,
    int ContactAudits = 0, int ContactExecutions = 0)
{
    public Effects AddJourney(int count) => new(Parties + count, Accounts + count * 2,
        CreateAudits + count, LinkAudits + count, CreateExecutions + count, LinkExecutions + count,
        ContactAudits, ContactExecutions);
}
public sealed record AccountRow(Guid PartyId, string Code, string LegalName, Guid CompanyId, int Roles, string Version,
    string? ContactInformationJson = null, bool IsActive = true);
public sealed record DatabaseSnapshot(string DatabaseName, bool Rcsi, Effects Effects, IReadOnlyList<AccountRow> Rows,
    string PartyStateSha256 = "", IReadOnlyList<ContactExecutionRow>? ContactExecutionRows = null);
public sealed record ContactExecutionRow(Guid Id, Guid CompanyId, string IdempotencyKeyHash,
    string RequestHash, string ActorId, string Outcome, int HttpStatusCode, string ResponseJson, int SchemaVersion);

// Fixed read-only queries from the pinned real-SQL test contract, not a SQL console.
// The connection can only be constructed for this new, exact C006 database.
public static partial class DatabaseEvidence
{
    public static async Task WaitForServerAsync(RunSettings identity)
    {
        identity.Validate("Testing");
        var settings = new SqlConnectionStringBuilder(identity.NewOwnedDatabaseConnection())
        {
            InitialCatalog = "master", ConnectTimeout = 5,
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            await using var connection = new SqlConnection(settings.ConnectionString);
            try { await connection.OpenAsync(timeout.Token); return; }
            catch (SqlException) when (!timeout.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), timeout.Token);
            }
        }
    }

    public static async Task<DatabaseSnapshot> ReadAsync(RunSettings identity)
    {
        identity.Validate("Testing");
        await using var connection = new SqlConnection(identity.NewOwnedDatabaseConnection());
        await connection.OpenAsync();
        await using var metadata = connection.CreateCommand();
        metadata.CommandText = "SELECT DB_NAME(), is_read_committed_snapshot_on FROM sys.databases WHERE database_id=DB_ID();";
        await using var metaReader = await metadata.ExecuteReaderAsync();
        if (!await metaReader.ReadAsync() || metaReader.GetString(0) != identity.DatabaseName)
            throw new InvalidOperationException("C006 database identity mismatch.");
        var rcsi = metaReader.GetBoolean(1);
        await metaReader.DisposeAsync();

        await using var counts = connection.CreateCommand();
        counts.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM [counterparty].[Parties]),
              (SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts]),
              (SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=N'counterparty.party.created.v1'),
              (SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=N'counterparty.company-account.created.v1'),
              (SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=N'counterparty.party.create.v1'),
              (SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=N'counterparty.company-account.create.v1'),
              (SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=N'counterparty.company-account.contact-information.changed.v1'),
              (SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=N'counterparty.company-account.contact-information.update.v1');
            """;
        await using var reader = await counts.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("C006 count snapshot missing.");
        var effects = new Effects(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
            reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7));
        await reader.DisposeAsync();
        await using var rows = connection.CreateCommand();
        rows.CommandText = """
            SELECT p.Id, p.Code, p.LegalName, a.CompanyId, a.Roles, a.Version, a.ContactInformationJson, a.IsActive
            FROM counterparty.Parties AS p
            JOIN counterparty.CompanyPartyAccounts AS a ON a.PartyId=p.Id
            ORDER BY p.Id,a.CompanyId;
            """;
        await using var rowReader = await rows.ExecuteReaderAsync();
        var values = new List<AccountRow>();
        while (await rowReader.ReadAsync())
            values.Add(new(rowReader.GetGuid(0), rowReader.GetString(1), rowReader.GetString(2),
                rowReader.GetGuid(3), rowReader.GetInt32(4), rowReader.GetInt64(5).ToString(CultureInfo.InvariantCulture),
                rowReader.IsDBNull(6) ? null : rowReader.GetString(6), rowReader.GetBoolean(7)));
        await rowReader.DisposeAsync();
        await using var partyState = connection.CreateCommand();
        partyState.CommandText = "SELECT (SELECT * FROM [counterparty].[Parties] ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES);";
        var partyHash = HashPartyState(await partyState.ExecuteScalarAsync(), effects.Parties);
        await using var executions = connection.CreateCommand();
        executions.CommandText = """
            SELECT Id, CompanyId, IdempotencyKeyHash, RequestHash, ActorId, Outcome,
                   HttpStatusCode, ResponseJson, SchemaVersion
            FROM idempotency.CommandExecutions
            WHERE CommandName=N'counterparty.company-account.contact-information.update.v1'
            ORDER BY Id;
            """;
        await using var executionReader = await executions.ExecuteReaderAsync();
        var contactExecutions = new List<ContactExecutionRow>();
        while (await executionReader.ReadAsync())
            contactExecutions.Add(new(executionReader.GetGuid(0), executionReader.GetGuid(1),
                executionReader.GetString(2), executionReader.GetString(3), executionReader.GetString(4),
                executionReader.GetString(5), executionReader.GetInt32(6), executionReader.GetString(7),
                executionReader.GetInt32(8)));
        return new(identity.DatabaseName, rcsi, effects, values, partyHash, contactExecutions);
    }

    public static string HashPartyState(object? scalar, int partyCount)
    {
        // SQL Server's scalar FOR JSON subquery returns DBNull for an empty set.
        // Only the independently counted empty Party set permits that representation.
        var json = scalar switch
        {
            string value => value,
            DBNull when partyCount == 0 => "[]",
            _ => throw new InvalidOperationException("C006 Party snapshot scalar type/count mismatch."),
        };
        using var document = System.Text.Json.JsonDocument.Parse(json);
        if (partyCount < 0 || document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array
            || document.RootElement.GetArrayLength() != partyCount)
            throw new InvalidOperationException("C006 Party snapshot array/count mismatch.");
        // Preserve the exact nonempty JSON bytes; do not reserialize or normalize them.
        return SourceBinding.Hash(Encoding.UTF8.GetBytes(json));
    }

    public static void VerifyContactEdit(
        RunSettings identity, DatabaseSnapshot baseline, DatabaseSnapshot current,
        Guid partyId, Guid companyId, CounterpartyContactInformation expectedInformation)
    {
        identity.Validate("Testing");
        var expectedJson = CounterpartyContactInformationEditor.Serialize(expectedInformation);
        var before = baseline.Rows.SingleOrDefault(row => row.PartyId == partyId && row.CompanyId == companyId);
        var after = current.Rows.SingleOrDefault(row => row.PartyId == partyId && row.CompanyId == companyId);
        if (before is null || after is null || baseline.DatabaseName != identity.DatabaseName
            || current.DatabaseName != identity.DatabaseName || !baseline.Rcsi || !current.Rcsi
            || baseline.PartyStateSha256.Length != 64 || baseline.PartyStateSha256 != current.PartyStateSha256
            || baseline.Rows.Count != current.Rows.Count || before.ContactInformationJson == expectedJson
            || current.Effects != baseline.Effects with
            {
                ContactAudits = baseline.Effects.ContactAudits + 1,
                ContactExecutions = baseline.Effects.ContactExecutions + 1,
            })
            throw new InvalidOperationException("C006 contact edit must change one existing account, with no new Party/account or unrelated effect.");
        var nextVersion = checked(CounterpartyContactInformationEditor.ParseVersion(before.Version) + 1)
            .ToString(CultureInfo.InvariantCulture);
        if (after != before with { Version = nextVersion, ContactInformationJson = expectedJson }
            || baseline.Rows.Where(row => row != before).Any(row => !current.Rows.Contains(row)))
            throw new InvalidOperationException("C006 contact edit canonical value, version or company isolation mismatch.");
    }

    public static void VerifyBrowserJourney(RunSettings identity, DatabaseSnapshot baseline, DatabaseSnapshot current, int count)
    {
        if (count < 1 || count > 100 || baseline.DatabaseName != identity.DatabaseName
            || current.DatabaseName != identity.DatabaseName || !current.Rcsi
            || current.Effects != baseline.Effects.AddJourney(count))
            throw new InvalidOperationException("C006 browser journey SQL delta mismatch.");
        foreach (var row in baseline.Rows)
            if (!current.Rows.Contains(row)) throw new InvalidOperationException("C006 baseline account changed.");
        var prior = baseline.Rows.Select(x => x.PartyId).ToHashSet();
        var added = current.Rows.Where(x => !prior.Contains(x.PartyId)).GroupBy(x => x.PartyId).ToArray();
        if (added.Length != count) throw new InvalidOperationException("C006 unexpected Party count.");
        foreach (var party in added)
            if (party.Count() != 2 || party.Any(x => x.Roles != 0 || !x.LegalName.StartsWith(identity.BrowserPrefix, StringComparison.Ordinal))
                || !party.Select(x => x.CompanyId).ToHashSet().SetEquals([Guid.Parse(identity.CompanyAId), Guid.Parse(identity.CompanyBId)]))
                throw new InvalidOperationException("C006 unexpected journey prefix or company accounts.");
    }
}
