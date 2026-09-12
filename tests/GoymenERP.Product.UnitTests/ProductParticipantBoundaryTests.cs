using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Product.UnitTests;

// Negative enrollment boundaries only. These connection doubles never execute
// SQL; ownership/disposal/rollback acceptance requires the owner real SQL runner.
public sealed class ProductParticipantBoundaryTests
{
    [Fact]
    public async Task Every_public_save_overload_remains_closed()
    {
        await using var context = new ProductDbContext(new DbContextOptionsBuilder<ProductDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=ProductModelOnly;Integrated Security=true;Encrypt=false").Options);
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges(false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(false));
    }

    [Fact]
    public async Task Closed_owner_connection_is_rejected_without_opening_it()
    {
        await using var connection = new Connection(ConnectionState.Closed);
        await using var transaction = new Transaction(connection);
        var observed = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ProductDraftParticipant.EnlistAsync(connection, transaction, Authority(), default,
                onStaged: _ => { observed++; return Task.CompletedTask; }));
        Assert.Equal(0, connection.OpenCalls);
        Assert.Equal(0, observed);
    }

    [Fact]
    public async Task Transaction_from_another_connection_cannot_enroll_product()
    {
        await using var first = new Connection(ConnectionState.Open);
        await using var other = new Connection(ConnectionState.Open);
        await using var transaction = new Transaction(other);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ProductDraftParticipant.EnlistAsync(first, transaction, Authority(), default));
        Assert.Equal(0, first.OpenCalls);
        Assert.Equal(0, other.OpenCalls);
    }

    [Fact]
    public async Task Cancellation_prevents_enrollment_before_any_database_access()
    {
        await using var connection = new Connection(ConnectionState.Open);
        await using var transaction = new Transaction(connection);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ProductDraftParticipant.EnlistAsync(connection, transaction, Authority(), new CancellationToken(true)));
    }

    private static GroupSharedMasterCommandContext Authority() => GroupSharedMasterCommandContext.FromServerDecision(
        new(true, GroupSharedMasterKind.Product, GroupSharedMasterOperation.DraftCreate, Guid.NewGuid(), Guid.NewGuid(), "test-revision"),
        GroupSharedMasterOperation.DraftCreate);

    private sealed class Connection(ConnectionState state) : DbConnection
    {
        public int OpenCalls { get; private set; }
        [AllowNull] public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "boundary-double";
        public override string DataSource => "boundary-double";
        public override string ServerVersion => "0";
        public override ConnectionState State => state;
        public override void Open() { OpenCalls++; throw new InvalidOperationException("No test connection may be opened."); }
        public override void Close() { }
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }

    private sealed class Transaction(DbConnection connection) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        protected override DbConnection DbConnection => connection;
        public override void Commit() => throw new InvalidOperationException("Product does not own commit.");
        public override void Rollback() => throw new InvalidOperationException("Product does not own rollback.");
    }
}
