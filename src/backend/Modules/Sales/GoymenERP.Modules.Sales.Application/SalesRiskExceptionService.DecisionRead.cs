using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Application;

public sealed partial class SalesRiskExceptionService : ISalesRiskExceptionDecisionReadV1
{
    public async Task<SalesRiskExceptionDecisionReadResultV1> ReadRiskExceptionDecisionAsync(Guid exceptionId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence); cancellationToken.ThrowIfCancellationRequested(); Scope? scope = null;
        try
        {
            scope = await RequireAsync(ApprovePermission, cancellationToken); await Fence(cancellationToken);
            if (exceptionId == Guid.Empty) return new(SalesRiskExceptionDecisionReadStateV1.NotFound, null);
            if (exceptions is not ISalesRiskExceptionDecisionReadStoreV1 reader) return new(SalesRiskExceptionDecisionReadStateV1.Unavailable, null);
            var source = await reader.ReadRiskExceptionDecisionSourceAsync(exceptionId, Fence, cancellationToken); await Fence(cancellationToken);
            if (source is null) return new(SalesRiskExceptionDecisionReadStateV1.NotFound, null);
            if (!ValidRiskSource(source, scope.CompanyId, exceptionId)) return new(SalesRiskExceptionDecisionReadStateV1.Unavailable, null);
            if (source.Recorded.RequestedBy == scope.ActorId) return new(SalesRiskExceptionDecisionReadStateV1.Denied, null);
            await Fence(cancellationToken); // final completed consumer must not release facts after permission loss/cancel
            return new(SalesRiskExceptionDecisionReadStateV1.Found, ProjectRiskDecision(source, Day(timeProvider.GetUtcNow())));
        }
        catch (OperationCanceledException) { throw; }
        catch (SalesDraftServiceException failure) when (failure.Status is 401 or 403) { return new(SalesRiskExceptionDecisionReadStateV1.Denied, null); }
        catch (Exception failure) when (RiskReadFailure(failure))
        {
            if (scope is not null) try { await Fence(cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch (SalesDraftServiceException denial) when (denial.Status is 401 or 403) { return new(SalesRiskExceptionDecisionReadStateV1.Denied, null); }
            catch (Exception recheck) when (RiskReadFailure(recheck)) { return new(SalesRiskExceptionDecisionReadStateV1.Unavailable, null); }
            return new(SalesRiskExceptionDecisionReadStateV1.Unavailable, null);
        }
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); await RequireStillAsync(scope!, ApprovePermission, ct);
            await freshConsumerFence(ct); ct.ThrowIfCancellationRequested(); await RequireStillAsync(scope!, ApprovePermission, ct); ct.ThrowIfCancellationRequested();
        }
    }
    public async Task<SalesRiskExceptionOriginalVersionReadResultV1> ReadRiskExceptionOriginalVersionAsync(Guid exceptionId, string expectedVersion,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence); cancellationToken.ThrowIfCancellationRequested(); Scope? scope = null;
        try
        {
            scope = await RequireAsync(ApprovePermission, cancellationToken); await Fence(cancellationToken);
            if (exceptionId == Guid.Empty || !long.TryParse(expectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
                || version < 1 || version == long.MaxValue || version.ToString(CultureInfo.InvariantCulture) != expectedVersion)
                return new(SalesRiskExceptionDecisionReadStateV1.Unavailable, null);
            if (exceptions is not ISalesRiskExceptionDecisionReadStoreV1 reader) return new(SalesRiskExceptionDecisionReadStateV1.Unavailable, null);
            var result = await reader.ReadRiskExceptionOriginalVersionAsync(exceptionId, version, scope.ActorId, Fence, cancellationToken); await Fence(cancellationToken);
            if (result is { State: SalesRiskExceptionDecisionReadStateV1.NotFound, Receipt: null }) return result;
            if (result is not { State: SalesRiskExceptionDecisionReadStateV1.Found, Receipt: { CurrentSource: { } current } receipt }
                || receipt.CompanyId != scope.CompanyId || receipt.ExceptionId != exceptionId || receipt.ActorId != scope.ActorId
                || receipt.ExpectedVersion != expectedVersion || receipt.ResultVersion != (version + 1).ToString(CultureInfo.InvariantCulture)
                || !ValidRiskSource(current, scope.CompanyId, exceptionId)
                || receipt.OriginalDecisionDigest != SalesRiskExceptionDecisionDigestsV1.OriginalDecisionDigest(receipt))
                return new(SalesRiskExceptionDecisionReadStateV1.Unavailable, null);
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (SalesDraftServiceException failure) when (failure.Status is 401 or 403) { return new(SalesRiskExceptionDecisionReadStateV1.Denied, null); }
        catch (Exception failure) when (RiskReadFailure(failure))
        {
            if (scope is not null) try { await Fence(cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch (SalesDraftServiceException denial) when (denial.Status is 401 or 403) { return new(SalesRiskExceptionDecisionReadStateV1.Denied, null); }
            catch (Exception recheck) when (RiskReadFailure(recheck)) { return new(SalesRiskExceptionDecisionReadStateV1.Unavailable, null); }
            return new(SalesRiskExceptionDecisionReadStateV1.Unavailable, null);
        }
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); await RequireStillAsync(scope!, ApprovePermission, ct); await freshConsumerFence(ct);
            ct.ThrowIfCancellationRequested(); await RequireStillAsync(scope!, ApprovePermission, ct); ct.ThrowIfCancellationRequested();
        }
    }
    public static SalesRiskExceptionDecisionDetailV1 ProjectRiskDecision(SalesRiskExceptionDecisionSourceV1 source, DateOnly today)
    {
        var item = SalesRiskExceptionDecisionDigestsV1.Restore(source.CompanyId, source.Recorded);
        if (source.DroppedByShipmentId is { } drop) item.ApplyShipmentDropped(drop);
        var pending = source.Recorded.State == "Requested" && source.Recorded.Version < long.MaxValue;
        var detail = new SalesRiskExceptionDecisionDetailV1(source.CompanyId, source.Recorded.Id, source.OrderDocumentNo,
            source.OrderVersion.ToString(CultureInfo.InvariantCulture), source.OrderState, source.OrderApprovalState, source.HasApprovedRevision,
            View(item, today), pending && source.OrderState == "Draft" && (source.OrderApprovalState == "Approved" || source.HasApprovedRevision), pending,
            SalesRiskExceptionDecisionDigestsV1.SourceFactsDigest(source), "") { Source = source, ObservedBusinessDate = today };
        return detail with { PreviewDigest = SalesRiskExceptionDecisionDigestsV1.PreviewDigest(detail) };
    }
    public static bool MatchesRiskDecisionDetail(SalesRiskExceptionDecisionDetailV1? detail) => detail?.Source is { } source
        && detail.Exception is not null && ValidRiskSource(source, detail.CompanyId, detail.ExceptionId)
        && detail.ObservedBusinessDate is { } today
        && detail.Exception == ProjectRiskDecision(source, today).Exception
        && detail.OrderDocumentNo == source.OrderDocumentNo && detail.OrderVersion == source.OrderVersion.ToString(CultureInfo.InvariantCulture)
        && detail.OrderState == source.OrderState && detail.OrderApprovalState == source.OrderApprovalState && detail.HasApprovedRevision == source.HasApprovedRevision
        && detail.SourceFactsDigest == SalesRiskExceptionDecisionDigestsV1.SourceFactsDigest(source)
        && detail.PreviewDigest == SalesRiskExceptionDecisionDigestsV1.PreviewDigest(detail)
        && detail.RejectionSupported == (source.Recorded.State == "Requested" && source.Recorded.Version < long.MaxValue)
        && detail.ApprovalSupported == (detail.RejectionSupported && source.OrderState == "Draft" && (source.OrderApprovalState == "Approved" || source.HasApprovedRevision));
    private static bool ValidRiskSource(SalesRiskExceptionDecisionSourceV1? s, Guid company, Guid id)
    {
        if (s is null || s.Recorded is null || s.CompanyId != company || company == Guid.Empty || s.Recorded.Id != id || id == Guid.Empty
            || s.OrderVersion < 1 || string.IsNullOrWhiteSpace(s.OrderDocumentNo) || s.OrderDocumentNo.Length > 200
            || s.OrderState is not ("Draft" or "Cancelled" or "Closed") || s.OrderApprovalState is not ("Preparing" or "PendingApproval" or "Approved" or "Rejected" or "Withdrawn")) return false;
        try { var item = SalesRiskExceptionDecisionDigestsV1.Restore(company, s.Recorded);
            if (s.DroppedByShipmentId is { } drop) item.ApplyShipmentDropped(drop); return true; }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or OverflowException) { return false; }
    }
    private static bool RiskReadFailure(Exception e) => e is InvalidOperationException or ArgumentException or System.Text.Json.JsonException or FormatException or OverflowException
        or SalesDraftServiceException or SalesDraftStoreException or System.Data.Common.DbException or TimeoutException or NotSupportedException;
}
