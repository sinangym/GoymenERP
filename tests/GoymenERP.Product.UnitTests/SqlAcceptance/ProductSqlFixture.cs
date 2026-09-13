using System.Data;
using System.Text.RegularExpressions;
using GoymenERP.Modules.Product.Domain.Products;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Product.UnitTests.SqlAcceptance;

// A narrow projection supplied by the admitted owner's verified contract. This
// does not parse a second manifest, grant authority, start a host, open a connection,
// create/migrate a database, commit, clean up or publish ENV_READY.
internal sealed class ProductSqlFixture
{
    public ProductSqlFixture(string runId, Guid instanceId, string databaseName, Guid codeGroupId)
    {
        if (!Regex.IsMatch(runId, "\\Aproduct-u0102-[0-9a-f]{12}\\z", RegexOptions.CultureInvariant)
            || instanceId == Guid.Empty || codeGroupId == Guid.Empty
            || databaseName != "GoymenERP_Product_U0102_" + runId[^12..])
            throw new ArgumentException("Product SQL fixture identity differs from the accepted run contract.");
        RunId = runId;
        InstanceId = instanceId;
        DatabaseName = databaseName;
        CodeGroupId = codeGroupId;
    }

    public string RunId { get; }
    public Guid InstanceId { get; }
    public string DatabaseName { get; }
    public Guid CodeGroupId { get; }

    public async Task VerifyDatabaseAsync(SqlConnection connection, SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();
        if (connection.State != ConnectionState.Open || connection.DataSource != "127.0.0.1,26433"
            || connection.Database != DatabaseName
            || (transaction is not null && !ReferenceEquals(transaction.Connection, connection)))
            throw new InvalidOperationException("Product fixture requires its owner's open isolated SQL connection/transaction.");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT DB_NAME();";
        if (await command.ExecuteScalarAsync(cancellationToken) is not string actual || actual != DatabaseName)
            throw new InvalidOperationException("Observed Product database differs from the accepted fixture.");
    }

    public async Task SeedCodeGroupsAsync(SqlConnection connection, SqlTransaction transaction,
        IReadOnlyList<ProductCodeGroupRow> groups, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var boundGroups = BindCodeGroups(groups);
        await VerifyDatabaseAsync(connection, transaction, cancellationToken);
        foreach (var group in boundGroups)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO [product].[CodeGroups] ([Id], [Name], [Prefix]) VALUES (@id, @name, @prefix);";
            command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = group.Id;
            command.Parameters.Add("@name", SqlDbType.NVarChar, group.Name.Length).Value = group.Name;
            command.Parameters.Add("@prefix", SqlDbType.VarChar, ProductCode.MaximumPrefixLength).Value = group.Prefix;
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("The Product code-group fixture was not inserted exactly once.");
        }
    }

    internal IReadOnlyList<ProductCodeGroupRow> BindCodeGroups(IReadOnlyList<ProductCodeGroupRow> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var frozen = groups.ToArray();
        // Bind the browser's group to an explicit fixture row, never the first
        // server option. Additional reviewed groups may share its prefix for races.
        if (frozen.Length == 0 || frozen.Any(group => group is null)
            || frozen.Count(group => group.Id == CodeGroupId) != 1
            || frozen.Select(group => group.Id).Distinct().Count() != frozen.Length)
            throw new ArgumentException("Fixture groups must include exactly the contract codeGroupId without duplicate identities.");
        return frozen;
    }

    public async Task ReserveRetiredCodeAsync(SqlConnection connection, SqlTransaction transaction,
        Guid existingProductId, ProductCode retiredCode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (existingProductId == Guid.Empty) throw new ArgumentException("An existing fixture Product is required.");
        var code = ProductCode.Parse(retiredCode.Value).Value;
        await VerifyDatabaseAsync(connection, transaction, cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO [product].[CodeRegistrations] ([Code], [ProductId], [IsCurrent]) VALUES (@code, @productId, 0);";
        command.Parameters.Add("@code", SqlDbType.VarChar, code.Length).Value = code;
        command.Parameters.Add("@productId", SqlDbType.UniqueIdentifier).Value = existingProductId;
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("The retired fixture code was not reserved exactly once.");
    }
}
