using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.SalesContracts;

namespace GoymenERP.Modules.Sales.Application;

public sealed partial class SalesContractService : ISalesContractDiscountDecisionReadV1
{
    public async Task<SalesContractDiscountDecisionReadResultV1> ReadContractDiscountDecisionAsync(Guid contractId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence); cancellationToken.ThrowIfCancellationRequested(); Scope? scope = null;
        try
        {
            scope = await RequireAsync(ApprovePermission, cancellationToken); await Fence(cancellationToken);
            if (contractId == Guid.Empty) return new(SalesContractDiscountDecisionReadStateV1.NotFound, null);
            if (contracts is not ISalesContractDiscountDecisionReadStoreV1 reader) return new(SalesContractDiscountDecisionReadStateV1.Unavailable, null);
            var supplied = await reader.ReadContractDiscountDecisionSourceAsync(contractId, Fence, cancellationToken);
            var source = supplied is null ? null : SalesContractDiscountDecisionDigestsV1.Freeze(supplied);
            await Fence(cancellationToken);
            if (source is null) return new(SalesContractDiscountDecisionReadStateV1.NotFound, null);
            if (!ValidContractDecisionSource(source, scope.CompanyId, contractId)) return new(SalesContractDiscountDecisionReadStateV1.Unavailable, null);
            if (source.Contract.CreatedBy == scope.ActorId) return new(SalesContractDiscountDecisionReadStateV1.Denied, null);
            var first = await DiscloseAmountsAsync(scope, cancellationToken); await Fence(cancellationToken);
            // Price disclosure is the LAST asynchronous release measurement. A later approve/consumer await could revoke PriceRead.
            var final = await DiscloseAmountsAsync(scope, cancellationToken); cancellationToken.ThrowIfCancellationRequested();
            if (!Matches(scope.CompanyId, scope.ActorId)) return new(SalesContractDiscountDecisionReadStateV1.Denied, null);
            return new(SalesContractDiscountDecisionReadStateV1.Found, ProjectContractDiscountDecision(source, Day(timeProvider.GetUtcNow()),
                first == SalesPriceDisclosure.Visible && final == SalesPriceDisclosure.Visible ? SalesPriceDisclosure.Visible : SalesPriceDisclosure.Withheld));
        }
        catch (OperationCanceledException) { throw; }
        catch (SalesDraftServiceException failure) when (failure.Status is 401 or 403) { return new(SalesContractDiscountDecisionReadStateV1.Denied, null); }
        catch (Exception failure) when (ContractDecisionReadFailure(failure))
        {
            if (scope is not null) try { await Fence(cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch (SalesDraftServiceException denial) when (denial.Status is 401 or 403) { return new(SalesContractDiscountDecisionReadStateV1.Denied, null); }
            catch (Exception recheck) when (ContractDecisionReadFailure(recheck)) { return new(SalesContractDiscountDecisionReadStateV1.Unavailable, null); }
            return new(SalesContractDiscountDecisionReadStateV1.Unavailable, null);
        }
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); await RequireStillAsync(scope!, ApprovePermission, ct); await freshConsumerFence(ct);
            ct.ThrowIfCancellationRequested(); await RequireStillAsync(scope!, ApprovePermission, ct); ct.ThrowIfCancellationRequested();
        }
    }

    public async Task<SalesContractDiscountOriginalVersionReadResultV1> ReadContractDiscountOriginalVersionAsync(Guid contractId, string expectedVersion,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence); cancellationToken.ThrowIfCancellationRequested(); Scope? scope = null;
        try
        {
            scope = await RequireAsync(ApprovePermission, cancellationToken); await Fence(cancellationToken);
            if (contractId == Guid.Empty || !SalesContractDiscountDecisionDigestsV1.TryVersion(expectedVersion, out var version) || version == long.MaxValue)
                return new(SalesContractDiscountDecisionReadStateV1.Unavailable, null);
            if (contracts is not ISalesContractDiscountDecisionReadStoreV1 reader) return new(SalesContractDiscountDecisionReadStateV1.Unavailable, null);
            var result = await reader.ReadContractDiscountOriginalVersionAsync(contractId, version, scope.ActorId, Fence, cancellationToken); await Fence(cancellationToken);
            if (result is { State: SalesContractDiscountDecisionReadStateV1.Denied, Receipt: null }) return result;
            if (result is { State: SalesContractDiscountDecisionReadStateV1.NotFound, Receipt: null }) return result;
            if (result is not { State: SalesContractDiscountDecisionReadStateV1.Found, Receipt: { CurrentSource: { } current } receipt }
                || !ValidContractDecisionSource(current, scope.CompanyId, contractId)
                || receipt.CompanyId != scope.CompanyId || receipt.ContractId != contractId || receipt.ActorId != scope.ActorId
                || receipt.ExpectedVersion != expectedVersion || receipt.ResultVersion != (version + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                || receipt.OriginalDecisionDigest != SalesContractDiscountDecisionDigestsV1.OriginalDecisionDigest(receipt))
                return new(SalesContractDiscountDecisionReadStateV1.Unavailable, null);
            if (current.Contract.CreatedBy == scope.ActorId) return new(SalesContractDiscountDecisionReadStateV1.Denied, null);
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (SalesDraftServiceException failure) when (failure.Status is 401 or 403) { return new(SalesContractDiscountDecisionReadStateV1.Denied, null); }
        catch (Exception failure) when (ContractDecisionReadFailure(failure))
        {
            if (scope is not null) try { await Fence(cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch (SalesDraftServiceException denial) when (denial.Status is 401 or 403) { return new(SalesContractDiscountDecisionReadStateV1.Denied, null); }
            catch (Exception recheck) when (ContractDecisionReadFailure(recheck)) { return new(SalesContractDiscountDecisionReadStateV1.Unavailable, null); }
            return new(SalesContractDiscountDecisionReadStateV1.Unavailable, null);
        }
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); await RequireStillAsync(scope!, ApprovePermission, ct); await freshConsumerFence(ct);
            ct.ThrowIfCancellationRequested(); await RequireStillAsync(scope!, ApprovePermission, ct); ct.ThrowIfCancellationRequested();
        }
    }
    public static SalesContractDiscountDecisionDetailV1 ProjectContractDiscountDecision(SalesContractDiscountDecisionSourceV1 source, DateOnly today, SalesPriceDisclosure disclosure)
    {
        source = SalesContractDiscountDecisionDigestsV1.Freeze(source);
        var pending = source.Contract.State == SalesContractState.PendingDiscountApproval && source.Contract.Version < long.MaxValue;
        var detail = new SalesContractDiscountDecisionDetailV1(source.CompanyId, View(source.Contract, today, disclosure), pending, pending,
            SalesContractDiscountDecisionDigestsV1.SourceFactsDigest(source), "") { Source = source, ObservedBusinessDate = today };
        return detail with { PreviewDigest = SalesContractDiscountDecisionDigestsV1.PreviewDigest(detail) };
    }
    public static bool MatchesContractDiscountDecisionDetail(SalesContractDiscountDecisionDetailV1? detail)
    {
        if (detail?.Source is not { Contract: { } item } source || detail.Contract is null || detail.ObservedBusinessDate is not { } today
            || !ValidContractDecisionSource(source, detail.CompanyId, item.Id)) return false;
        var expected = ProjectContractDiscountDecision(source, today, detail.Contract.AmountDisclosure);
        return SalesContractDiscountDecisionDigestsV1.Same(detail, expected);
    }
    public static bool MatchesContractDecisionResponse(SalesContractDiscountDecisionSourceV1 source, SalesContractView? view) => view is not null
        && SalesContractDiscountDecisionDigestsV1.Same(view with { IsInEffectToday = false }, View(source.Contract, default, view.AmountDisclosure) with { IsInEffectToday = false });
    private static bool ValidContractDecisionSource(SalesContractDiscountDecisionSourceV1? source, Guid company, Guid id) => source?.Contract is { } item
        && company != Guid.Empty && id != Guid.Empty && source.CompanyId == company && item.CompanyId == company && item.Id == id && item.DiscountApprovalRequired;
    private static bool ContractDecisionReadFailure(Exception e) => e is InvalidOperationException or ArgumentException or System.Text.Json.JsonException
        or FormatException or OverflowException or SalesDraftServiceException or SalesDraftStoreException or System.Data.Common.DbException or TimeoutException or NotSupportedException;
}
