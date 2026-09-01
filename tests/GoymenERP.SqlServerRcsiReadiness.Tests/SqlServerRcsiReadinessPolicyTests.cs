using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using GoymenERP.BuildingBlocks.SqlServer;

namespace GoymenERP.SqlServerRcsiReadiness.Tests;

public sealed class SqlServerRcsiReadinessPolicyTests
{
    private const string LogicalEndpointA = "tcp:sql-a.example,1433";
    private const string LogicalEndpointASha256 =
        "75a99409b81404222ad7347d4dad424dfab7b355844a736531e2105eb621c05f";
    private const string LogicalEndpointBSha256 =
        "f67561664930dd93b930601f71be9bcd6ce230064681b7b7f03695df0c745fe1";
    private const string ToolAssemblySha256 =
        "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
    private static readonly SqlServerRcsiReadinessBinding Binding =
        SqlServerRcsiReadinessBinding.Create(
            "staging/blue",
            "GoymenErp",
            [LogicalEndpointBSha256, LogicalEndpointASha256, LogicalEndpointBSha256]);
    private static readonly SqlServerRcsiReadinessRunContext RunContext =
        CreateRunContext();

    [Fact]
    public void ApprovedLogicalEndpointSet_IsNormalizedSortedAndUnique()
    {
        Assert.Equal(
            [LogicalEndpointASha256, LogicalEndpointBSha256],
            Binding.ApprovedLogicalEndpointSha256);
        Assert.Equal(
            LogicalEndpointASha256,
            SqlServerLogicalEndpointFingerprint.ComputeSha256(
                " TCP:SQL-A.EXAMPLE,1433 "));
    }

    [Fact]
    public void ReadyEvidence_UsesApprovedLogicalEndpointAndExactDatabase()
    {
        var evidence = SqlServerRcsiReadinessPolicy.Evaluate(
            Binding,
            configuredDatabase: "GoymenErp",
            observedDatabase: "GoymenErp",
            observedLogicalEndpointSha256: LogicalEndpointASha256,
            readCommittedSnapshotSetting: 1);

        Assert.True(evidence.IsReady);
        Assert.Equal(SqlServerRcsiReadinessExitCode.Ready, evidence.ExitCode);
        Assert.Equal("RCSI_READY", evidence.ReasonCode);
        Assert.True(evidence.ReadCommittedSnapshotEnabled);
        Assert.True(evidence.LogicalEndpointMatches);
    }

    [Fact]
    public void RcsiDisabledEvidence_IsNotReadyAndPreservesStartupMessage()
    {
        var evidence = SqlServerRcsiReadinessPolicy.Evaluate(
            Binding,
            configuredDatabase: "GoymenErp",
            observedDatabase: "GoymenErp",
            observedLogicalEndpointSha256: LogicalEndpointASha256,
            readCommittedSnapshotSetting: 0);

        Assert.False(evidence.IsReady);
        Assert.Equal(SqlServerRcsiReadinessExitCode.RcsiDisabled, evidence.ExitCode);
        Assert.Equal("RCSI_DISABLED", evidence.ReasonCode);
        Assert.False(evidence.ReadCommittedSnapshotEnabled);
        Assert.Equal(
            "Identity authorization snapshot requires SQL Server "
            + "READ_COMMITTED_SNAPSHOT ON.",
            SqlServerRcsiReadinessPolicy.GetStartupFailureMessage(evidence));
    }

    [Theory]
    [InlineData("goymenerp", "GoymenErp")]
    [InlineData("GoymenErp", "OtherDatabase")]
    public void DatabaseBinding_IsExactAndFailsClosed(
        string configuredDatabase,
        string observedDatabase)
    {
        var evidence = SqlServerRcsiReadinessPolicy.Evaluate(
            Binding,
            configuredDatabase,
            observedDatabase,
            LogicalEndpointASha256,
            readCommittedSnapshotSetting: 1);

        Assert.False(evidence.IsReady);
        Assert.Equal(
            SqlServerRcsiReadinessExitCode.ExpectedDatabaseMismatch,
            evidence.ExitCode);
        Assert.Equal("EXPECTED_DATABASE_MISMATCH", evidence.ReasonCode);
        Assert.Null(evidence.ReadCommittedSnapshotEnabled);
    }

    [Fact]
    public void UnapprovedLogicalEndpoint_FailsClosedBeforeRcsiCanBeReady()
    {
        var otherEndpoint = SqlServerLogicalEndpointFingerprint.ComputeSha256(
            "tcp:sql-c.example,1433");

        var evidence = SqlServerRcsiReadinessPolicy.Evaluate(
            Binding,
            configuredDatabase: "GoymenErp",
            observedDatabase: "GoymenErp",
            observedLogicalEndpointSha256: otherEndpoint,
            readCommittedSnapshotSetting: 1);

        Assert.False(evidence.IsReady);
        Assert.Equal(
            SqlServerRcsiReadinessExitCode.ExpectedLogicalEndpointMismatch,
            evidence.ExitCode);
        Assert.Equal("EXPECTED_LOGICAL_ENDPOINT_MISMATCH", evidence.ReasonCode);
        Assert.False(evidence.LogicalEndpointMatches);
        Assert.Equal(otherEndpoint, evidence.ObservedLogicalEndpointSha256);
        Assert.Null(evidence.ReadCommittedSnapshotEnabled);
    }

    [Fact]
    public async Task UnapprovedConfiguredLogicalEndpoint_IsRejectedBeforeConnectionOpen()
    {
        var connection = new NeverOpenDbConnection(
            database: "GoymenErp",
            dataSource: "tcp:unapproved-logical-endpoint.invalid,1433");

        var evidence = await new SqlServerRcsiReadinessProbe().ProbeAsync(
            connection,
            Binding);

        Assert.Equal(
            SqlServerRcsiReadinessExitCode.ExpectedLogicalEndpointMismatch,
            evidence.ExitCode);
        Assert.Equal(0, connection.OpenCount);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task PostOpenLogicalEndpointDrift_IsRejectedBeforeCommandAndConnectionCloses()
    {
        var connection = new SwitchingEndpointDbConnection(
            database: "GoymenErp",
            configuredDataSource: LogicalEndpointA,
            connectedDataSource: "tcp:unapproved-after-open.invalid,1433");

        var evidence = await new SqlServerRcsiReadinessProbe().ProbeAsync(
            connection,
            Binding);

        Assert.Equal(
            SqlServerRcsiReadinessExitCode.ExpectedLogicalEndpointMismatch,
            evidence.ExitCode);
        Assert.Equal(1, connection.OpenCount);
        Assert.Equal(0, connection.CreateCommandCount);
        Assert.Equal(1, connection.CloseCount);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void EmptyOrInvalidLogicalEndpointSet_IsRejected()
    {
        Assert.False(SqlServerRcsiReadinessBinding.TryCreate(
            "staging/blue",
            "GoymenErp",
            [],
            out _));
        Assert.False(SqlServerRcsiReadinessBinding.TryCreate(
            "staging/blue",
            "GoymenErp",
            ["not-a-sha256"],
            out _));
    }

    [Fact]
    public void RunContext_RequiresUniqueUuidExactCommitAndUtcObservation()
    {
        Assert.False(SqlServerRcsiReadinessRunContext.TryCreate(
            Guid.Empty.ToString("D"),
            new string('a', 40),
            ToolAssemblySha256,
            ToolAssemblySha256,
            ToolAssemblySha256,
            DateTimeOffset.UtcNow,
            out _));
        Assert.False(SqlServerRcsiReadinessRunContext.TryCreate(
            Guid.NewGuid().ToString("D"),
            "short",
            ToolAssemblySha256,
            ToolAssemblySha256,
            ToolAssemblySha256,
            DateTimeOffset.UtcNow,
            out _));
        Assert.False(SqlServerRcsiReadinessRunContext.TryCreate(
            Guid.NewGuid().ToString("D"),
            new string('a', 40),
            ToolAssemblySha256,
            ToolAssemblySha256,
            ToolAssemblySha256,
            new DateTimeOffset(2026, 9, 1, 13, 0, 0, TimeSpan.FromHours(3)),
            out _));
    }

    [Fact]
    public void CanonicalEvidence_IsDeterministicFreshBoundAndSecretFree()
    {
        var evidence = SqlServerRcsiReadinessPolicy.Evaluate(
            Binding,
            configuredDatabase: "GoymenErp",
            observedDatabase: "GoymenErp",
            observedLogicalEndpointSha256: LogicalEndpointASha256,
            readCommittedSnapshotSetting: 1);

        var first = SqlServerRcsiReadinessEvidenceJson.SerializeUtf8(
            evidence,
            RunContext);
        var second = SqlServerRcsiReadinessEvidenceJson.SerializeUtf8(
            evidence,
            RunContext);
        var json = Encoding.UTF8.GetString(first);

        Assert.Equal(first, second);
        Assert.Equal(
            "{\"contractVersion\":\"GOYMEN-ERP-SQL-RCSI-READINESS-V1\","
            + "\"runId\":\"11111111-1111-1111-1111-111111111111\","
            + "\"testedCommit\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\","
            + "\"expectedToolAssemblySha256\":\"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\","
            + "\"observedToolAssemblySha256\":\"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\","
            + "\"runtimePayloadManifestSha256\":\"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\","
            + "\"observedAtUtc\":\"2026-09-01T10:20:30.1234567Z\","
            + "\"targetLabel\":\"staging/blue\","
            + "\"approvedLogicalEndpointSha256\":["
            + "\"75a99409b81404222ad7347d4dad424dfab7b355844a736531e2105eb621c05f\","
            + "\"f67561664930dd93b930601f71be9bcd6ce230064681b7b7f03695df0c745fe1\"],"
            + "\"observedLogicalEndpointSha256\":"
            + "\"75a99409b81404222ad7347d4dad424dfab7b355844a736531e2105eb621c05f\","
            + "\"logicalEndpointMatches\":true,"
            + "\"expectedDatabase\":\"GoymenErp\","
            + "\"configuredDatabase\":\"GoymenErp\","
            + "\"observedDatabase\":\"GoymenErp\","
            + "\"readCommittedSnapshotEnabled\":true,"
            + "\"status\":\"READY\",\"reasonCode\":\"RCSI_READY\","
            + "\"exitCode\":0,"
            + "\"queryMode\":\"READ_ONLY_SYS_DATABASES_CURRENT_V1\"}",
            json);
        Assert.DoesNotContain(LogicalEndpointA, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user id", json, StringComparison.OrdinalIgnoreCase);
    }

    private static SqlServerRcsiReadinessRunContext CreateRunContext()
    {
        Assert.True(SqlServerRcsiReadinessRunContext.TryCreate(
            "11111111-1111-1111-1111-111111111111",
            new string('a', 40),
            ToolAssemblySha256,
            ToolAssemblySha256,
            ToolAssemblySha256,
            new DateTimeOffset(2026, 9, 1, 10, 20, 30, TimeSpan.Zero)
                .AddTicks(1_234_567),
            out var context));
        return context;
    }

    private sealed class NeverOpenDbConnection(
        string database,
        string dataSource) : DbConnection
    {
        private string _connectionString = string.Empty;

        [AllowNull]
        public override string ConnectionString
        {
            get => _connectionString;
            set => _connectionString = value ?? string.Empty;
        }

        public override string Database => database;

        public override string DataSource => dataSource;

        public override string ServerVersion => "test";

        public override ConnectionState State => ConnectionState.Closed;

        public int OpenCount { get; private set; }

        public override void ChangeDatabase(string databaseName) =>
            throw new NotSupportedException();

        public override void Close()
        {
        }

        public override void Open()
        {
            OpenCount++;
            throw new InvalidOperationException("Connection must not be opened.");
        }

        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            OpenCount++;
            throw new InvalidOperationException("Connection must not be opened.");
        }

        protected override DbTransaction BeginDbTransaction(
            IsolationLevel isolationLevel) =>
            throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() =>
            throw new NotSupportedException();
    }

    private sealed class SwitchingEndpointDbConnection(
        string database,
        string configuredDataSource,
        string connectedDataSource) : DbConnection
    {
        private ConnectionState _state = ConnectionState.Closed;
        private string _connectionString = string.Empty;

        [AllowNull]
        public override string ConnectionString
        {
            get => _connectionString;
            set => _connectionString = value ?? string.Empty;
        }

        public override string Database => database;
        public override string DataSource =>
            _state == ConnectionState.Open ? connectedDataSource : configuredDataSource;
        public override string ServerVersion => "test";
        public override ConnectionState State => _state;
        public int OpenCount { get; private set; }
        public int CloseCount { get; private set; }
        public int CreateCommandCount { get; private set; }

        public override void ChangeDatabase(string databaseName) =>
            throw new NotSupportedException();

        public override void Close()
        {
            CloseCount++;
            _state = ConnectionState.Closed;
        }

        public override void Open()
        {
            OpenCount++;
            _state = ConnectionState.Open;
        }

        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            Open();
            return Task.CompletedTask;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();

        protected override DbCommand CreateDbCommand()
        {
            CreateCommandCount++;
            throw new NotSupportedException();
        }
    }
}
