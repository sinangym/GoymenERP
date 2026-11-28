using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.CustomerReturns;

namespace GoymenERP.Modules.Sales.Application;

public sealed partial class SalesCustomerReturnService : ISalesCustomerReturnDecisionReadV1
{
    // Pure delegation to the unchanged native formatter; no read authority or calculation is introduced.
    public static SalesCustomerReturnView ProjectDecisionReturn(SalesCustomerReturn item) => View(item);
    public async Task<SalesCustomerReturnDecisionReadResultV1> ReadCustomerReturnDecisionAsync(Guid returnId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence); cancellationToken.ThrowIfCancellationRequested(); Scope? scope = null;
        try
        {
            scope = await RequireAsync(ApprovePermission, cancellationToken); await Fence(cancellationToken);
            if (!customerReturnDecisionSafety || returns is not ISalesCustomerReturnDecisionReadStoreV1 reader) return Empty(SalesCustomerReturnDecisionReadStateV1.Unavailable);
            if (returnId == Guid.Empty) return Empty(SalesCustomerReturnDecisionReadStateV1.NotFound);
            var source = await reader.ReadCustomerReturnDecisionSourceAsync(returnId, Fence, cancellationToken);
            var captured = source is null ? null : Copy(source);
            await Fence(cancellationToken);
            if (captured is null) return Empty(SalesCustomerReturnDecisionReadStateV1.NotFound);
            // Detach before any consumer await: validation, digest and projection all use one immutable captured snapshot.
            if (captured.Id != returnId || captured.CompanyId != scope.CompanyId || captured.Version < 1
                || captured.Facts.SubmittedBy is null || captured.Facts.SubmittedAtUtc is null) return Empty(SalesCustomerReturnDecisionReadStateV1.Unavailable);
            var view = FreezeCustomerReturnView(View(captured));
            var pending = captured.State == SalesCustomerReturnState.PendingApproval;
            var detail = new SalesCustomerReturnDecisionDetailV1(scope.CompanyId, view, pending, pending,
                SalesCustomerReturnDecisionDigestsV1.SourceFactsDigest(scope.CompanyId, view), "");
            await Fence(cancellationToken);
            if (captured.CreatedBy == scope.ActorId || captured.Facts.SubmittedBy == scope.ActorId) return Empty(SalesCustomerReturnDecisionReadStateV1.Denied);
            return new(SalesCustomerReturnDecisionReadStateV1.Found, detail with { PreviewDigest = SalesCustomerReturnDecisionDigestsV1.PreviewDigest(detail) });
        }
        catch (OperationCanceledException) { throw; }
        catch (SalesDraftServiceException e) when (e.Status is 401 or 403) { return Empty(SalesCustomerReturnDecisionReadStateV1.Denied); }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.Data.Common.DbException or System.Text.Json.JsonException or SalesDraftServiceException)
        { return Empty(SalesCustomerReturnDecisionReadStateV1.Unavailable); }
        SalesCustomerReturnDecisionReadResultV1 Empty(SalesCustomerReturnDecisionReadStateV1 state) => new(state, null);
        async Task Fence(CancellationToken token)
        { token.ThrowIfCancellationRequested(); await RequireStillAsync(scope!, ApprovePermission, token); await freshConsumerFence(token); token.ThrowIfCancellationRequested(); await RequireStillAsync(scope!, ApprovePermission, token); token.ThrowIfCancellationRequested(); }
    }
    public async Task<SalesCustomerReturnOriginalReadResultV1> ReadCustomerReturnOriginalVersionAsync(Guid returnId, string expectedVersion,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence); cancellationToken.ThrowIfCancellationRequested(); Scope? scope = null;
        try
        {
            scope = await RequireAsync(ApprovePermission, cancellationToken); await Fence(cancellationToken);
            if (!customerReturnDecisionSafety || returns is not ISalesCustomerReturnDecisionReadStoreV1 reader
                || !long.TryParse(expectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 1 || n == long.MaxValue
                || n.ToString(CultureInfo.InvariantCulture) != expectedVersion) return Empty(SalesCustomerReturnDecisionReadStateV1.Unavailable);
            if (returnId == Guid.Empty) return Empty(SalesCustomerReturnDecisionReadStateV1.NotFound);
            var answer = await reader.ReadCustomerReturnOriginalVersionAsync(returnId, n, scope.ActorId, Fence, cancellationToken);
            if (answer?.Receipt is { OriginalDecision: { Lines: not null } } candidate)
                answer = answer with { Receipt = candidate with { OriginalDecision = FreezeCustomerReturnView(candidate.OriginalDecision) } };
            await Fence(cancellationToken);
            if (answer is null || !Enum.IsDefined(answer.State) || answer.State == SalesCustomerReturnDecisionReadStateV1.Found && (answer.Receipt is null || answer.CurrentVersion is null)
                || answer.State != SalesCustomerReturnDecisionReadStateV1.Found && answer.Receipt is not null || answer.CurrentVersion is <= 0) return Empty(SalesCustomerReturnDecisionReadStateV1.Unavailable);
            if (answer.Receipt is { } r && (r.CompanyId != scope.CompanyId || r.ReturnId != returnId || r.ActorId != scope.ActorId || r.ExpectedVersion != n
                || r.ResultVersion != n + 1 || answer.CurrentVersion < r.ResultVersion || !SalesCustomerReturnDecisionDigestsV1.IsOriginalReceipt(r)))
                return Empty(SalesCustomerReturnDecisionReadStateV1.Unavailable);
            return answer;
        }
        catch (OperationCanceledException) { throw; }
        catch (SalesDraftServiceException e) when (e.Status is 401 or 403) { return Empty(SalesCustomerReturnDecisionReadStateV1.Denied); }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.Data.Common.DbException or System.Text.Json.JsonException or SalesDraftServiceException)
        { return Empty(SalesCustomerReturnDecisionReadStateV1.Unavailable); }
        SalesCustomerReturnOriginalReadResultV1 Empty(SalesCustomerReturnDecisionReadStateV1 state) => new(state, null);
        async Task Fence(CancellationToken token)
        { token.ThrowIfCancellationRequested(); await RequireStillAsync(scope!, ApprovePermission, token); await freshConsumerFence(token); token.ThrowIfCancellationRequested(); await RequireStillAsync(scope!, ApprovePermission, token); token.ThrowIfCancellationRequested(); }
    }
    private static SalesCustomerReturnView FreezeCustomerReturnView(SalesCustomerReturnView view) => view with { Lines = Array.AsReadOnly(view.Lines.ToArray()) };
}
