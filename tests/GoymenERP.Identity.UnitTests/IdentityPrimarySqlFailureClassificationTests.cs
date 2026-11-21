using System.Reflection;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Identity.UnitTests;

public sealed class IdentityPrimarySqlFailureClassificationTests
{
    [Fact]
    public void Only_known_SQL_deadlock_and_lock_timeout_shapes_are_retryable_primary_failures()
    {
        foreach (var number in new[] { 1205, 1222, 2601, 2627, 547, -2 })
        {
            var sql = SqlFailure(number);
            Exception[] shapes = [sql, new DbUpdateException("SENTETIK save", sql),
                new InvalidOperationException("SENTETIK query", sql),
                new InvalidOperationException("SENTETIK save wrapper", new DbUpdateException("SENTETIK save", sql))];
            foreach (var shape in shapes)
                Assert.Equal(number is 1205 or 1222, IdentityAccessAssignmentService.IsRetryablePrimarySqlFailure(shape));
            Assert.False(IdentityAccessAssignmentService.IsRetryablePrimarySqlFailure(new OperationCanceledException("SENTETIK cancellation", sql)));
            Assert.False(IdentityAccessAssignmentService.IsRetryablePrimarySqlFailure(new InvalidOperationException(
                "SENTETIK wrapped cancellation", new OperationCanceledException("SENTETIK cancellation", sql))));
        }
        Assert.False(IdentityAccessAssignmentService.IsRetryablePrimarySqlFailure(new InvalidOperationException("SENTETIK programming fault")));
        Assert.False(IdentityAccessAssignmentService.IsRetryablePrimarySqlFailure(new DbUpdateConcurrencyException("SENTETIK version conflict")));
    }

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
