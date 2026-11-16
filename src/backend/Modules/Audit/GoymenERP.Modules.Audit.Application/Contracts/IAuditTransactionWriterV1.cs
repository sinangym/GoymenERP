using System.Data;

namespace GoymenERP.Modules.Audit.Application.Contracts;

/// <summary>
/// Appends the request collector's company audit batch through the Audit owner in the caller's
/// open relational transaction. It neither commits that transaction nor grants authority.
/// A failure must abort the command; pending drafts are discarded after every attempt.
/// </summary>
public interface IAuditTransactionWriterV1
{
    Task WritePendingAsync(IDbTransaction transaction, CancellationToken cancellationToken = default);

    /// <summary>Discards drafts of an aborted attempt; it never changes persisted audit rows.</summary>
    void DiscardPending();
}
