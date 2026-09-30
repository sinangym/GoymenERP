using GoymenERP.Api.ProductRuntime;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ProductImportCommandRecoveryTests
{
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, false)]
    public void OnlyConfirmedRollbackBeforeCommitAndWithoutCancellation_AllowsImportRetry(
        bool rollbackConfirmed, bool commitStarted, bool cancellationRequested, bool expected) =>
        Assert.Equal(expected, ProductImportSqlCommandRunner.CanRetryAfterRollback(
            rollbackConfirmed, commitStarted, cancellationRequested));
}
