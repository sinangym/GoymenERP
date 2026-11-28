using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.ApprovalRequests;

namespace GoymenERP.Modules.Sales.Application;

public sealed partial class SalesDraftService : ISalesQuoteConversionDecisionReadV1
{
    public async Task<SalesQuoteDecisionReadResultV1> ReadQuoteConversionDecisionAsync(Guid requestId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence);
        cancellationToken.ThrowIfCancellationRequested();
        Scope? scope = null;
        var permission = bindings.QuoteExpiredConversionApprovePermission;
        try
        {
            scope = await RequireAsync(permission, cancellationToken);
            await Fence(cancellationToken);
            if (requestId == Guid.Empty) return new(SalesQuoteDecisionReadStateV1.NotFound, null);
            if (quotes is not ISalesQuoteDecisionReadStoreV1 reader) return new(SalesQuoteDecisionReadStateV1.Unavailable, null);
            var found = await reader.ReadQuoteConversionDecisionAsync(requestId, Fence, cancellationToken);
            await Fence(cancellationToken);
            if (found is null) return new(SalesQuoteDecisionReadStateV1.NotFound, null);
            if (found.Request is not { } selected || found.Quote is not { } quote || selected.Id != requestId
                || selected.CompanyId != scope!.CompanyId || quote.CompanyId != scope.CompanyId || selected.SubjectId != quote.Id
                || selected.Kind != SalesApprovalRequestKind.QuoteExpiredConversion)
                return new(SalesQuoteDecisionReadStateV1.Unavailable, null);
            // Same native disclosure rule: the approval permission does not grant price visibility or QuotesRead.
            var disclosure = await DiscloseAsync(scope!, cancellationToken);
            await Fence(cancellationToken);
            var human = QuoteDetail(quote, disclosure, ServerBusinessDay());
            var request = ApprovalRequestView(selected, quote);
            var pending = request.Status == SalesApprovalRequestApiV1.StatusPending;
            var result = new SalesQuoteConversionDecisionDetailV1(requestId, request, human, pending,
                selected.State == SalesApprovalRequestState.Requested, SalesQuoteDecisionDigestsV1.SourceFactsDigest(request, human), "");
            await Fence(cancellationToken);
            // A normally completing consumer may revoke ONLY price visibility. It is not an approval co-permission.
            var finalDisclosure = await DiscloseAsync(scope!, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            scope = await RequireStillAsync(scope!, permission, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (finalDisclosure != disclosure)
            {
                human = QuoteDetail(quote, finalDisclosure, ServerBusinessDay());
                result = result with { Quote = human, SourceFactsDigest = SalesQuoteDecisionDigestsV1.SourceFactsDigest(request, human) };
            }
            return new(SalesQuoteDecisionReadStateV1.Found, result with { PreviewDigest = SalesQuoteDecisionDigestsV1.PreviewDigest(result) });
        }
        catch (OperationCanceledException) { throw; }
        catch (SalesDraftServiceException failure) when (failure.Status is 401 or 403)
        { return new(SalesQuoteDecisionReadStateV1.Denied, null); }
        catch
        {
            try { if (scope is not null) await Fence(cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch (SalesDraftServiceException failure) when (failure.Status is 401 or 403)
            { return new(SalesQuoteDecisionReadStateV1.Denied, null); }
            catch { return new(SalesQuoteDecisionReadStateV1.Unavailable, null); }
            return new(SalesQuoteDecisionReadStateV1.Unavailable, null);
        }

        async Task Fence(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            scope = await RequireStillAsync(scope!, permission, token);
            await freshConsumerFence(token); token.ThrowIfCancellationRequested();
            scope = await RequireStillAsync(scope, permission, token); token.ThrowIfCancellationRequested();
        }
    }

    public Task<SalesQuoteResponse> ApproveSelectedQuoteConversionAsync(SalesQuoteConversionTargetV1 target,
        SalesQuoteExpiryApprovalRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(request);
        return RunExpiredQuoteApprovalAsync(target.QuoteId, request, metadata, cancellationToken,
            new(target, request.ExpectedVersion, request.Note));
    }

    public Task<SalesApprovalRequestCommandResult> RejectSelectedQuoteConversionAsync(SalesQuoteConversionTargetV1 target,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(request);
        return RunQuoteConversionRejectionAsync(target.QuoteId, target.RequestNo, request, metadata, cancellationToken,
            new(target, request.ExpectedVersion, request.Reason));
    }
}
