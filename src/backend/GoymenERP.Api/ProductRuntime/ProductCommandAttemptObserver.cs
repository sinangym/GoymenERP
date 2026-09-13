using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Api.ProductRuntime;

public enum ProductCommandAttemptStage { ProductStaged, ProductFlushed, AuditFlushed, TerminalFlushed, BeforeCommit }

// A no-op in the application. Tests may replace it to interleave real SQL revocation,
// cancellation or owner failures. It grants no authority and is not a second writer.
public interface IProductCommandAttemptObserver
{
    Task ObserveAsync(ProductCommandAttemptStage stage, GroupSharedMasterCommandContext authority, CancellationToken cancellationToken);
}

public sealed class NullProductCommandAttemptObserver : IProductCommandAttemptObserver
{
    public Task ObserveAsync(ProductCommandAttemptStage stage, GroupSharedMasterCommandContext authority,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
