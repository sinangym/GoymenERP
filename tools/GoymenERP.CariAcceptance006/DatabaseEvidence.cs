using Microsoft.Data.SqlClient;

namespace GoymenERP.CariAcceptance006;

public sealed record Effects(int Parties, int Accounts, int CreateAudits, int LinkAudits, int CreateExecutions, int LinkExecutions)
{
    public Effects AddJourney(int count) => new(Parties + count, Accounts + count * 2,
        CreateAudits + count, LinkAudits + count, CreateExecutions + count, LinkExecutions + count);
}
public sealed record AccountRow(Guid PartyId, string Code, string LegalName, Guid CompanyId, int Roles, string Version);
public sealed record DatabaseSnapshot(string DatabaseName, bool Rcsi, Effects Effects, IReadOnlyList<AccountRow> Rows);

// Fixed read-only queries from the pinned real-SQL test contract, not a SQL console.
// The connection can only be constructed for this new, exact C006 database.
public static class DatabaseEvidence
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
              (SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=N'counterparty.company-account.create.v1');
            """;
        await using var reader = await counts.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("C006 count snapshot missing.");
        var effects = new Effects(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
            reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5));
        await reader.DisposeAsync();
        await using var rows = connection.CreateCommand();
        rows.CommandText = """
            SELECT p.Id, p.Code, p.LegalName, a.CompanyId, a.Roles, a.Version
            FROM counterparty.Parties AS p
            JOIN counterparty.CompanyPartyAccounts AS a ON a.PartyId=p.Id
            ORDER BY p.Id,a.CompanyId;
            """;
        await using var rowReader = await rows.ExecuteReaderAsync();
        var values = new List<AccountRow>();
        while (await rowReader.ReadAsync())
            values.Add(new(rowReader.GetGuid(0), rowReader.GetString(1), rowReader.GetString(2),
                rowReader.GetGuid(3), rowReader.GetInt32(4), rowReader.GetInt64(5).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return new(identity.DatabaseName, rcsi, effects, values);
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
