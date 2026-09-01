using System.Text;
using GoymenERP.BuildingBlocks.SqlServer;

namespace GoymenERP.Identity.UnitTests;

public sealed class SqlServerRcsiReadinessPolicyTests
{
    private const string DataSource = "tcp:sql-a.example,1433";
    private const string DataSourceSha256 =
        "75a99409b81404222ad7347d4dad424dfab7b355844a736531e2105eb621c05f";
    private static readonly SqlServerRcsiReadinessBinding Binding =
        SqlServerRcsiReadinessBinding.CreateFromDataSource(
            "staging/blue",
            "GoymenErp",
            DataSource);

    [Fact]
    public void ReadyEvidence_UsesExactExpectedAndObservedDatabase()
    {
        var evidence = SqlServerRcsiReadinessPolicy.Evaluate(
            Binding,
            configuredDatabase: "GoymenErp",
            observedDatabase: "GoymenErp",
            observedDataSourceSha256: DataSourceSha256,
            readCommittedSnapshotSetting: 1);

        Assert.True(evidence.IsReady);
        Assert.Equal(SqlServerRcsiReadinessExitCode.Ready, evidence.ExitCode);
        Assert.Equal("RCSI_READY", evidence.ReasonCode);
        Assert.True(evidence.ReadCommittedSnapshotEnabled);
        Assert.True(evidence.DataSourceMatches);
    }

    [Fact]
    public void RcsiDisabledEvidence_IsNotReadyAndPreservesStartupMessage()
    {
        var evidence = SqlServerRcsiReadinessPolicy.Evaluate(
            Binding,
            configuredDatabase: "GoymenErp",
            observedDatabase: "GoymenErp",
            observedDataSourceSha256: DataSourceSha256,
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
            observedDataSourceSha256: DataSourceSha256,
            readCommittedSnapshotSetting: 1);

        Assert.False(evidence.IsReady);
        Assert.Equal(
            SqlServerRcsiReadinessExitCode.ExpectedDatabaseMismatch,
            evidence.ExitCode);
        Assert.Equal("EXPECTED_DATABASE_MISMATCH", evidence.ReasonCode);
        Assert.Null(evidence.ReadCommittedSnapshotEnabled);
    }

    [Theory]
    [InlineData("", "GoymenErp")]
    [InlineData("staging blue", "GoymenErp")]
    [InlineData("staging", "")]
    [InlineData("staging", "bad\ndatabase")]
    public void InvalidBinding_IsRejected(string targetLabel, string expectedDatabase)
    {
        Assert.False(SqlServerRcsiReadinessBinding.TryCreate(
            targetLabel,
            expectedDatabase,
            DataSourceSha256,
            out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void InvalidExpectedDataSourceFingerprint_IsRejected(string fingerprint)
    {
        Assert.False(SqlServerRcsiReadinessBinding.TryCreate(
            "staging/blue",
            "GoymenErp",
            fingerprint,
            out _));
    }

    [Fact]
    public void DataSourceFingerprint_IsNormalizedWithoutExposingTheSource()
    {
        var lower = SqlServerDataSourceFingerprint.ComputeSha256(
            " tcp:sql-a.example,1433 ");
        var upper = SqlServerDataSourceFingerprint.ComputeSha256(
            "TCP:SQL-A.EXAMPLE,1433");

        Assert.Equal(DataSourceSha256, lower);
        Assert.Equal(lower, upper);
        Assert.DoesNotContain("sql-a", lower, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpectedTargetMismatch_FailsClosedBeforeRcsiCanBeReady()
    {
        var otherTarget = SqlServerDataSourceFingerprint.ComputeSha256(
            "tcp:sql-b.example,1433");

        var evidence = SqlServerRcsiReadinessPolicy.Evaluate(
            Binding,
            configuredDatabase: "GoymenErp",
            observedDatabase: "GoymenErp",
            observedDataSourceSha256: otherTarget,
            readCommittedSnapshotSetting: 1);

        Assert.False(evidence.IsReady);
        Assert.Equal(
            SqlServerRcsiReadinessExitCode.ExpectedTargetMismatch,
            evidence.ExitCode);
        Assert.Equal("EXPECTED_TARGET_MISMATCH", evidence.ReasonCode);
        Assert.False(evidence.DataSourceMatches);
        Assert.Equal(otherTarget, evidence.ObservedDataSourceSha256);
        Assert.Null(evidence.ReadCommittedSnapshotEnabled);
    }

    [Fact]
    public void CanonicalEvidence_IsDeterministicAndSecretFree()
    {
        var evidence = SqlServerRcsiReadinessPolicy.Evaluate(
            Binding,
            configuredDatabase: "GoymenErp",
            observedDatabase: "GoymenErp",
            observedDataSourceSha256: DataSourceSha256,
            readCommittedSnapshotSetting: 1);

        var first = SqlServerRcsiReadinessEvidenceJson.SerializeUtf8(evidence);
        var second = SqlServerRcsiReadinessEvidenceJson.SerializeUtf8(evidence);
        var json = Encoding.UTF8.GetString(first);

        Assert.Equal(first, second);
        Assert.Equal(
            "{\"contractVersion\":\"GOYMEN-ERP-SQL-RCSI-READINESS-V1\","
            + "\"targetLabel\":\"staging/blue\","
            + "\"expectedDataSourceSha256\":\"75a99409b81404222ad7347d4dad424d"
            + "fab7b355844a736531e2105eb621c05f\","
            + "\"observedDataSourceSha256\":\"75a99409b81404222ad7347d4dad424d"
            + "fab7b355844a736531e2105eb621c05f\","
            + "\"dataSourceMatches\":true,"
            + "\"expectedDatabase\":\"GoymenErp\","
            + "\"configuredDatabase\":\"GoymenErp\","
            + "\"observedDatabase\":\"GoymenErp\","
            + "\"readCommittedSnapshotEnabled\":true,"
            + "\"status\":\"READY\",\"reasonCode\":\"RCSI_READY\","
            + "\"exitCode\":0,"
            + "\"queryMode\":\"READ_ONLY_SYS_DATABASES_CURRENT_V1\"}",
            json);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("server", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user id", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidBindingEvidence_IsCanonicalAndDoesNotEchoInput()
    {
        var json = SqlServerRcsiReadinessEvidenceJson.Serialize(
            SqlServerRcsiReadinessPolicy.InvalidBinding());

        Assert.Contains("\"targetLabel\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"expectedDataSourceSha256\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"observedDataSourceSha256\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"dataSourceMatches\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"expectedDatabase\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"reasonCode\":\"BINDING_INVALID\"", json, StringComparison.Ordinal);
        Assert.Contains("\"exitCode\":2", json, StringComparison.Ordinal);
    }
}
