using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Application;

public sealed partial class SalesDraftService
{
    private bool quoteApprovalSafety;
    // Same scoped core across native aliases. Synthetic legacy domain fixtures do not claim SQL ownership.
    public SalesDraftService WithQuoteApprovalSafety() { quoteApprovalSafety = true; return this; }

    private Task<SalesQuoteResponse> RunExpiredQuoteApprovalAsync(Guid id, SalesQuoteExpiryApprovalRequest request,
        SalesCommandMetadata metadata, CancellationToken token, SalesQuoteDecisionCommandV1? decision = null)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(metadata);
        return RunQuoteDecisionAsync<SalesQuoteResponse>(request.ExpectedVersion, metadata, SalesQuoteCommands.ApproveExpiredConversionV1,
            () => SalesQuoteHash.Compute(id, request), ct => ApproveExpiredQuoteConversionCoreAsync(id, request, metadata, ct),
            async (scope, envelope, hash, ct) =>
            {
                var replay = await FindQuoteReplayAsync(scope, bindings.QuoteExpiredConversionApprovePermission, envelope, hash,
                    SalesQuoteCommands.ApproveExpiredConversionV1, ct);
                return replay is null ? null : await QuoteReplayedAsync(scope, replay, ct);
            }, result => result.IsReplay, token, decision);
    }

    private Task<SalesApprovalRequestCommandResult> RunQuoteConversionRejectionAsync(Guid id, int no, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, CancellationToken token, SalesQuoteDecisionCommandV1? decision = null)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(metadata);
        return RunQuoteDecisionAsync<SalesApprovalRequestCommandResult>(request.ExpectedVersion, metadata, SalesApprovalRequestCommands.RejectQuoteConversionV1,
            () => SalesApprovalRequestHash.Compute(SalesApprovalRequestCommands.RejectQuoteConversionV1, id, no, request),
            ct => RejectQuoteConversionRequestCoreAsync(id, no, request, metadata, ct), async (scope, envelope, hash, ct) =>
            {
                var replay = await FindApprovalReplayAsync(scope, bindings.QuoteExpiredConversionApprovePermission, envelope, hash,
                    SalesApprovalRequestCommands.RejectQuoteConversionV1, ct);
                return replay is null ? null : await ApprovalReplayedAsync(scope, bindings.QuoteExpiredConversionApprovePermission, replay, ct);
            }, result => result.IsReplay, token, decision);
    }

    private async Task<T> RunQuoteDecisionAsync<T>(string expectedVersion, SalesCommandMetadata metadata, string command,
        Func<string> payloadHash, Func<CancellationToken, Task<T>> execute,
        Func<Scope, SalesDraftWriteEnvelope, string, CancellationToken, Task<T?>> originalKey,
        Func<T, bool> isReplay, CancellationToken token, SalesQuoteDecisionCommandV1? decision = null) where T : class
    {
        token.ThrowIfCancellationRequested();
        var boundary = quotes as ISalesQuoteApprovalStoreV1;
        // Retrying in a different store/context would not own the rejection and quote reads together.
        var sameStore = command != SalesApprovalRequestCommands.RejectQuoteConversionV1 || ReferenceEquals(quotes, store);
        var cleanBefore = boundary?.QuoteApprovalEntryIsClean == true;
        var permission = bindings.QuoteExpiredConversionApprovePermission;
        var scope = await RequireAsync(permission, token); token.ThrowIfCancellationRequested();
        _ = ExpectedVersion(expectedVersion); // Preserve native shape and 401/403 ordering.
        if (boundary is null || !sameStore || !cleanBefore || !boundary.QuoteApprovalEntryIsClean)
            throw new SalesDraftServiceException(503, "service_unavailable");
        var hash = payloadHash(); var envelope = Envelope(scope, metadata, permission!);
        try
        {
            return await boundary.RunQuoteApprovalAsync(envelope, hash, command, cleanBefore, execute,
                ct => originalKey(scope, envelope, hash, ct), isReplay,
                async ct => { scope = await RequireStillAsync(scope, permission, ct); ct.ThrowIfCancellationRequested(); }, token, decision);
        }
        catch (SalesDraftStoreException refusal) { throw Store(refusal); }
    }
}
