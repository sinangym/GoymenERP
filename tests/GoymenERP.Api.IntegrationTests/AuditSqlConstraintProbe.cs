using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Tests insert-time CHECK constraints separately from the owner's append-only UPDATE trigger.</summary>
internal static class AuditSqlConstraintProbe
{
    internal static async Task InsertInvalidCloneAsync(string connectionString, string sourcePredicate, string change)
    {
        var assignment = change.Split(" = ", 2, StringSplitOptions.None);
        Assert.Equal(2, assignment.Length);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var columns = new List<string>();
        await using (var metadata = new SqlCommand("""
            SELECT QUOTENAME([name]) FROM sys.columns
            WHERE [object_id] = OBJECT_ID(N'[audit].[AuditEvents]')
              AND [is_identity] = 0 AND [is_computed] = 0
            ORDER BY [column_id];
            """, connection))
        await using (var reader = await metadata.ExecuteReaderAsync())
            while (await reader.ReadAsync()) columns.Add(reader.GetString(0));
        Assert.NotEmpty(columns);
        Assert.Contains(assignment[0], columns);
        Assert.Contains("[Id]", columns);
        var projection = columns.Select(column => column == "[Id]" ? "NEWID()"
            : column == assignment[0] ? assignment[1] : column);
        // Predicate and mutation are fixed literals from these two typed-audit fixtures, never request input.
        await using var command = new SqlCommand(
            "INSERT INTO [audit].[AuditEvents] (" + string.Join(",", columns) + ") SELECT TOP (1) "
            + string.Join(",", projection) + " FROM [audit].[AuditEvents] WHERE " + sourcePredicate, connection);
        await command.ExecuteNonQueryAsync();
    }
}
