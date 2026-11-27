namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class InvoiceLineCogsCorrectionStoreV1
{
    // Only the scoped internal cancellation reader sets this callback; it never owns a mutation boundary.
    private Func<CancellationToken, Task>? cancellationReadFence;
}
