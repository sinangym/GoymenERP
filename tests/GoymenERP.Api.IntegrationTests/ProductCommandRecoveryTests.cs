using GoymenERP.Api.ProductRuntime;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ProductCommandRecoveryTests
{
    // Controlled recovery-decision regression, not a SQL disconnect/commit proof.
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, false)]
    public void OnlyConfirmedRollbackBeforeCommitAndWithoutCancellation_AllowsRetry(
        bool rollbackConfirmed, bool commitStarted, bool cancellationRequested, bool expected) =>
        Assert.Equal(expected, ProductSqlCommandRunner.CanRetryAfterRollback(rollbackConfirmed, commitStarted, cancellationRequested));
}
