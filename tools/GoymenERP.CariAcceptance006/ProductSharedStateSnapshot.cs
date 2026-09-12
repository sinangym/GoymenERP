using System.Data;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace GoymenERP.CariAcceptance006;

// The existing publisher's shared-owner projection, also compile-linked by the
// real SQL regression. No host, manifest, connection creation or state mutation.
internal sealed record ProductSharedStateSnapshot(JsonElement Audit, JsonElement Terminals)
{
    internal static async Task<ProductSharedStateSnapshot> ReadAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Product shared snapshot requires the owner's open connection.");
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT JSON_QUERY(COALESCE((SELECT * FROM [audit].[AuditEvents] WHERE [SubjectKind]=N'GroupSharedProduct' ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES), N'[]')) AS audit,
                JSON_QUERY(COALESCE((SELECT * FROM [idempotency].[GroupSharedCommandTerminals] ORDER BY [Subject], [CommandName], [IdempotencyKeyHash] FOR JSON PATH, INCLUDE_NULL_VALUES), N'[]')) AS terminals
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
            """;
        var json = new StringBuilder();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) json.Append(reader.GetString(0));
        using var document = JsonDocument.Parse(json.ToString());
        var audit = document.RootElement.GetProperty("audit");
        var terminals = document.RootElement.GetProperty("terminals");
        // Fix the SQL representation, not the evidence gate. Missing, null or
        // malformed arrays still fail; every populated SQL JSON fragment is kept.
        if (audit.ValueKind != JsonValueKind.Array || terminals.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Product shared snapshot must expose both actual JSON arrays.");
        return new(audit.Clone(), terminals.Clone());
    }
}
