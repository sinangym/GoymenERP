using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// F-017 F17-1 (ALIS_F017 v1 §3.4), without a database: the purchase line lock contract (resource name, canonical acquisition order,
/// busy code, short wait), the binding refusals before any statement, and the answer reading of the one fixed statement over a fake
/// connection — a negative application lock answer is busy, a non-integer answer is a technical failure (never busy, never granted),
/// and the statement carries only the resource and the wait as parameters. That the lock really serializes two transactions is
/// proven on SQL Server (<c>PurchasingSqlServerRelationalAcceptanceTests.FutureSupply</c>). SENTETIK / NOT_ENV.
/// </summary>
public sealed class PurchaseLineSerializationLockTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid LineA = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid LineB = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002");

    [Fact]
    public void The_resource_name_is_the_pinned_prefix_with_both_identities_in_n_format()
    {
        var name = PurchaseLineSerializationLockContractV1.ResourceName(CompanyA, LineA);

        Assert.Equal("goymen:purchase-line:11111111111141118111111111111111:aaaaaaaa000040008000000000000001", name);
        Assert.StartsWith(PurchaseLineSerializationLockContractV1.ResourcePrefix, name, StringComparison.Ordinal);
        Assert.True(name.Length <= 255);
    }

    [Fact]
    public void The_resource_name_refuses_an_empty_company_or_line()
    {
        Assert.Throws<ArgumentException>(() => PurchaseLineSerializationLockContractV1.ResourceName(Guid.Empty, LineA));
        Assert.Throws<ArgumentException>(() => PurchaseLineSerializationLockContractV1.ResourceName(CompanyA, Guid.Empty));
    }

    [Fact]
    public void The_acquisition_order_is_distinct_and_ascending_and_refuses_an_empty_line()
    {
        Assert.Equal([LineA, LineB], PurchaseLineSerializationLockContractV1.AcquisitionOrder([LineB, LineA, LineB]));
        Assert.Throws<ArgumentException>(() => PurchaseLineSerializationLockContractV1.AcquisitionOrder([LineA, Guid.Empty]));
    }

    [Fact]
    public void The_busy_code_and_the_short_wait_are_the_contract_values_and_every_side_names_the_same_code()
    {
        Assert.Equal("purchasing_line_busy", PurchaseLineSerializationLockContractV1.BusyCode);
        Assert.Equal(TimeSpan.FromSeconds(5), PurchaseLineSerializationLockContractV1.LockTimeout);
        Assert.Equal(PurchaseLineSerializationLockContractV1.BusyCode, PurchaseAcceptanceParticipantCodes.LineBusy);
        Assert.Equal(PurchaseLineSerializationLockContractV1.BusyCode, PurchasingStoreCodes.LineBusy);
        Assert.Equal(PurchaseLineSerializationLockContractV1.BusyCode, PurchasingProblemCodesV1.LineBusy);
        Assert.Equal(PurchaseLineSerializationLockContractV1.BusyCode, new PurchaseLineBusyException(LineA, -1).Code);
    }

    [Fact]
    public void Binding_refuses_a_closed_connection_a_foreign_transaction_and_an_empty_company_before_any_statement()
    {
        var closed = new FakeConnection(ConnectionState.Closed, 0);
        Assert.Throws<InvalidOperationException>(() => PurchaseLineSerializationLock.Bind(closed, new FakeTransaction(closed), CompanyA));

        var open = new FakeConnection(ConnectionState.Open, 0);
        var other = new FakeConnection(ConnectionState.Open, 0);
        Assert.Throws<InvalidOperationException>(() => PurchaseLineSerializationLock.Bind(open, new FakeTransaction(other), CompanyA));
        Assert.Throws<InvalidOperationException>(() => PurchaseLineSerializationLock.Bind(open, new FakeTransaction(open), Guid.Empty));
        Assert.Empty(open.Commands);
        Assert.Empty(closed.Commands);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task A_granted_answer_continues_and_the_statement_carries_only_the_resource_and_the_wait(int answer)
    {
        var connection = new FakeConnection(ConnectionState.Open, answer);
        var transaction = new FakeTransaction(connection);

        await PurchaseLineSerializationLock.Bind(connection, transaction, CompanyA).AcquireAsync(LineA);

        var command = Assert.Single(connection.Commands);
        Assert.Same(transaction, command.Transaction);
        Assert.Contains("sp_getapplock", command.CommandText, StringComparison.Ordinal);
        Assert.Contains("@LockOwner = N'Transaction'", command.CommandText, StringComparison.Ordinal);
        Assert.Contains("@LockMode = N'Exclusive'", command.CommandText, StringComparison.Ordinal);
        Assert.DoesNotContain(CompanyA.ToString("N"), command.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["@resource", "@timeout"], command.Parameters.Cast<DbParameter>().Select(parameter => parameter.ParameterName).ToArray());
        Assert.Equal(PurchaseLineSerializationLockContractV1.ResourceName(CompanyA, LineA), command.Parameters["@resource"].Value);
        Assert.Equal(5000, command.Parameters["@timeout"].Value);
        Assert.True(command.CommandTimeout > 5);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-2)]
    [InlineData(-3)]
    [InlineData(-999)]
    public async Task A_negative_answer_is_busy(int answer)
    {
        var connection = new FakeConnection(ConnectionState.Open, answer);

        var busy = await Assert.ThrowsAsync<PurchaseLineBusyException>(() =>
            PurchaseLineSerializationLock.Bind(connection, new FakeTransaction(connection), CompanyA).AcquireAsync(LineA));

        Assert.Equal(LineA, busy.LineId);
        Assert.Equal(answer, busy.LockResult);
    }

    [Fact]
    public async Task A_missing_or_non_integer_answer_is_a_technical_failure_never_busy()
    {
        foreach (object? answer in new object?[] { null, DBNull.Value, "0", 0L })
        {
            var connection = new FakeConnection(ConnectionState.Open, answer);
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PurchaseLineSerializationLock.Bind(connection, new FakeTransaction(connection), CompanyA).AcquireAsync(LineA));
            Assert.IsNotType<PurchaseLineBusyException>(failure);
        }
    }

    [Fact]
    public async Task Several_lines_are_locked_once_each_in_ascending_order_and_the_first_refusal_stops_the_walk()
    {
        var connection = new FakeConnection(ConnectionState.Open, 0);
        await PurchaseLineSerializationLock.Bind(connection, new FakeTransaction(connection), CompanyA).AcquireAllAsync([LineB, LineA, LineB]);
        Assert.Equal(
            [PurchaseLineSerializationLockContractV1.ResourceName(CompanyA, LineA), PurchaseLineSerializationLockContractV1.ResourceName(CompanyA, LineB)],
            connection.Commands.Select(command => command.Parameters["@resource"].Value).ToArray());

        var refusing = new FakeConnection(ConnectionState.Open, -1);
        await Assert.ThrowsAsync<PurchaseLineBusyException>(() =>
            PurchaseLineSerializationLock.Bind(refusing, new FakeTransaction(refusing), CompanyA).AcquireAllAsync([LineB, LineA]));
        Assert.Single(refusing.Commands);
    }

    private sealed class FakeTransaction(DbConnection connection) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        protected override DbConnection DbConnection => connection;

        public override void Commit()
        {
        }

        public override void Rollback()
        {
        }
    }

    private sealed class FakeConnection(ConnectionState state, object? answer) : DbConnection
    {
        public List<FakeCommand> Commands { get; } = [];

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => "fake";

        public override string DataSource => "fake";

        public override string ServerVersion => "fake";

        public override ConnectionState State => state;

        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

        public override void Close()
        {
        }

        public override void Open() => throw new NotSupportedException();

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => new FakeTransaction(this);

        protected override DbCommand CreateDbCommand()
        {
            var command = new FakeCommand(answer);
            Commands.Add(command);
            return command;
        }
    }

    private sealed class FakeCommand(object? answer) : DbCommand
    {
        private readonly SqlParameterCollection _parameters = new SqlCommand().Parameters;

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; }

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        public new DbTransaction? Transaction => DbTransaction;

        protected override DbConnection? DbConnection { get; set; }

        protected override DbParameterCollection DbParameterCollection => _parameters;

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery() => throw new NotSupportedException();

        public override object? ExecuteScalar() => answer;

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter() => new SqlParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
    }
}
