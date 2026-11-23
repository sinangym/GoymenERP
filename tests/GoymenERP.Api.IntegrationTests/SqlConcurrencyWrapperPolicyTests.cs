using System.Reflection;
using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Pure owner-local exception policies, not SQL rollback or endpoint acceptance evidence.</summary>
public sealed class SqlConcurrencyWrapperPolicyTests
{
    [Theory]
    [InlineData(1205, 0)]
    [InlineData(1205, 1)]
    [InlineData(1205, 2)]
    [InlineData(1205, 3)]
    [InlineData(1222, 0)]
    [InlineData(1222, 1)]
    [InlineData(1222, 2)]
    [InlineData(1222, 3)]
    [InlineData(2601, 0)]
    [InlineData(2601, 1)]
    [InlineData(2601, 2)]
    [InlineData(2601, 3)]
    [InlineData(547, 0)]
    [InlineData(547, 1)]
    [InlineData(547, 2)]
    [InlineData(547, 3)]
    [InlineData(-2, 0)]
    [InlineData(-2, 1)]
    [InlineData(-2, 2)]
    [InlineData(-2, 3)]
    public void Reviewed_query_and_save_wrappers_keep_each_owners_existing_sql_number_policy(int number, int shape)
    {
        var sql = SqlFailure(number);
        Exception error = shape switch
        {
            0 => sql,
            1 => new DbUpdateException("SENTETIK save", sql),
            2 => new InvalidOperationException("SENTETIK query wrapper", sql),
            3 => new InvalidOperationException("SENTETIK save wrapper", new DbUpdateException("SENTETIK save", sql)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        // Finance has always treated direct DbException/DbUpdateException as nonterminal unavailable.
        Assert.Equal(shape is 0 or 1 || number is 1205 or 1222,
            FinanceTreasuryTransaction.IsTreasuryDatabaseFailure(error));
        Assert.Equal(number == 1205, AccountingStore.IsPeriodTransactionDeadlock(error));
        Assert.Equal(number is 1205 or 1222, ManufacturingCostGenerationHost.IsCostGenerationSqlConcurrencyFailure(error));
        Assert.Equal(number == 1205, InventorySubcontractAllocationReleaseStoreV1.IsSubcontractReleaseDeadlock(error));
    }

    [Fact]
    public void Unrelated_exceptions_cancellation_and_unreviewed_wrapper_depth_are_not_sql_concurrency()
    {
        var sql = SqlFailure(1205);
        Exception[] errors =
        [
            new InvalidOperationException("SENTETIK domain invariant"),
            new OperationCanceledException("SENTETIK cancellation", sql),
            new UnauthorizedAccessException("SENTETIK authority", sql),
            new AggregateException(sql),
            new InvalidOperationException("SENTETIK wrong wrapper", new UnauthorizedAccessException("SENTETIK authority", sql)),
            new InvalidOperationException("SENTETIK extra depth", new InvalidOperationException("SENTETIK nested", sql)),
        ];
        Assert.All(errors, error =>
        {
            Assert.False(FinanceTreasuryTransaction.IsTreasuryDatabaseFailure(error));
            Assert.False(AccountingStore.IsPeriodTransactionDeadlock(error));
            Assert.False(ManufacturingCostGenerationHost.IsCostGenerationSqlConcurrencyFailure(error));
            Assert.False(InventorySubcontractAllocationReleaseStoreV1.IsSubcontractReleaseDeadlock(error));
        });
        var ordinarySave = new DbUpdateException("SENTETIK non-SQL save");
        Assert.True(FinanceTreasuryTransaction.IsTreasuryDatabaseFailure(ordinarySave));
        Assert.False(AccountingStore.IsPeriodTransactionDeadlock(ordinarySave));
        Assert.False(ManufacturingCostGenerationHost.IsCostGenerationSqlConcurrencyFailure(ordinarySave));
        Assert.False(InventorySubcontractAllocationReleaseStoreV1.IsSubcontractReleaseDeadlock(ordinarySave));
    }

    // Same installed-SqlClient construction pattern as the existing deterministic Logistics deadlock fixture.
    private static SqlException SqlFailure(int number)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var error = typeof(SqlError).GetConstructor(hidden,
                [typeof(int), typeof(byte), typeof(byte), typeof(string), typeof(string), typeof(string), typeof(int), typeof(Exception)])
            ?.Invoke([number, (byte)51, (byte)13, "SENTETIK", "SENTETIK SQL failure", string.Empty, 0, null])
            ?? throw new InvalidOperationException("SqlError constructor is not available.");
        var errors = typeof(SqlErrorCollection).GetConstructor(hidden, Type.EmptyTypes)?.Invoke(null)
            ?? throw new InvalidOperationException("SqlErrorCollection constructor is not available.");
        (typeof(SqlErrorCollection).GetMethod("Add", hidden, [typeof(SqlError)])
            ?? throw new InvalidOperationException("SqlErrorCollection.Add is not available.")).Invoke(errors, [error]);
        return typeof(SqlException).GetMethod("CreateException", BindingFlags.Static | BindingFlags.NonPublic,
                [typeof(SqlErrorCollection), typeof(string)])?.Invoke(null, [errors, "16.00.0000"]) as SqlException
            ?? throw new InvalidOperationException("SqlException.CreateException is not available.");
    }
}
