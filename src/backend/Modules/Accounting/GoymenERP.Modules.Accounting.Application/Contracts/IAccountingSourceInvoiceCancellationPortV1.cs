namespace GoymenERP.Modules.Accounting.Application.Contracts;

/// <summary>The committed producer cancellation. SourceDocumentId is the original admission identity.</summary>
public sealed record AccountingSourceInvoiceCancellationV1(Guid CompanyId, string ActorId,
    string CorrelationId, string? CausationId, Guid CancellationId, string DocumentKind,
    Guid SourceDocumentId, Guid InvoiceId, long FinalizedVersion, Guid CancelledBy,
    DateTimeOffset CancelledAtUtc, string Reason);

public sealed record AccountingSourceInvoiceCancellationAnswerV1(string Status, bool IsReplay = false);

public interface IAccountingSourceInvoiceCancellationPortV1
{
    Task<AccountingSourceInvoiceCancellationAnswerV1> CancelAsync(AccountingSourceInvoiceCancellationV1 request,
        CancellationToken cancellationToken = default);
}

/// <summary>Host-bound source cancellation authority; not a grant and not interchangeable with finalization.</summary>
public interface IAccountingSourceInvoiceCancellationAuthorityV1
{
    Task<AccountingSourceDocumentAuthorityAnswerV1> CheckAsync(Guid companyId, string actorId,
        string documentKind, Guid sourceDocumentId, CancellationToken cancellationToken);
}

public sealed class UnavailableAccountingSourceInvoiceCancellationAuthorityV1 : IAccountingSourceInvoiceCancellationAuthorityV1
{
    public Task<AccountingSourceDocumentAuthorityAnswerV1> CheckAsync(Guid companyId, string actorId,
        string documentKind, Guid sourceDocumentId, CancellationToken cancellationToken) =>
        Task.FromResult(AccountingSourceDocumentAuthorityAnswerV1.Unavailable);
}

public interface IAccountingSourceInvoiceCancellationStoreV1
{
    Task<AccountingSourceInvoiceCancellationAnswerV1> CancelAsync(AccountingSourceInvoiceCancellationV1 request,
        AccountingCommandMetadata metadata, Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);
}
