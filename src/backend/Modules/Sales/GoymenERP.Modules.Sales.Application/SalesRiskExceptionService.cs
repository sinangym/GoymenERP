using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.RiskExceptions;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// API-07 — the Sales side of the single-shipment risk exception (EK21 SAT-D153…D162, DEC-FIN-009; Sinan 2026-11-10). The K3
/// skeleton: the operation's own name is measured before anything is read, after every awaited read and right before the single
/// save; the replay comes before any read; one save writes the exception, its attempt and the audit event; a terminal refusal
/// (stale version, state conflict, self-decision) is recorded under the key and answered again on retry.
/// <list type="bullet">
/// <item><b>Request</b> (<c>sales.orders.risk-exception.request</c>, SAT-D159/D162): an approved, open order (not cancelled or
/// closed) and one loading instruction; a pending or a today-valid exception of the same shipment is a 409 (not recorded).
/// An order whose risk the deterministic calculator answers <c>Within</c> its limit is a 409
/// <c>sales_risk_exception_within_limit</c> (EK56 SAT-D284; not recorded, retryable); <c>Over</c> and <c>Unavailable</c> are
/// accepted (an unknown risk is never "within"). <c>sales.drafts.read</c> is not asked; the answer carries no amount.</item>
/// <item><b>Approve / reject</b> (<c>sales.orders.risk-exception.approve</c>, SAT-D153): the expected version, never the
/// requester; the approval is valid until the end of today's Europe/Istanbul business date (SAT-D160).</item>
/// <item><b>Withdraw</b> (<c>sales.orders.risk-exception.request</c>, no new permission; EK56 SAT-D286): the expected version,
/// only the requester and only a pending request; another actor is a recorded 409 <c>sales_risk_exception_not_requester</c>.
/// The order's eligibility is not asked (a cancelled order's pending request can still be withdrawn).</item>
/// <item><b>Separate record</b> (SAT-D161): the order is read, never changed; no revision or re-approval opens.</item>
/// </list>
/// Today the risk read cannot answer <c>Within</c> (shipped-not-invoiced has no source yet), so the SAT-D284 refusal is
/// reachable once the invoicing owner answers; until then every request is <c>Over</c> or <c>Unavailable</c> and accepted.
/// </summary>
public sealed class SalesRiskExceptionService(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    ICorrelationContext correlation,
    ISalesRiskExceptionStore exceptions,
    ISalesOrderDraftStore orders,
    TimeProvider timeProvider,
    ISalesOrderRiskOutcomeSource? riskOutcomes = null) : ISalesRiskExceptionService
{
    private const string RequestPermission = SalesPermissionManifestV1.OrdersRiskExceptionRequest;
    private const string ApprovePermission = SalesPermissionManifestV1.OrdersRiskExceptionApprove;
    private const string ReadPermission = SalesPermissionManifestV1.DraftsRead;

    private static readonly SalesDraftStateAudit RequestedAudit =
        new("sales.order.risk-exception.requested.v1", "RequestRiskException", "Tek sevk risk istisnası istendi");

    private static readonly SalesDraftStateAudit ApprovedAudit =
        new("sales.order.risk-exception.approved.v1", "ApproveRiskException", "Tek sevk risk istisnası onaylandı");

    private static readonly SalesDraftStateAudit RejectedAudit =
        new("sales.order.risk-exception.rejected.v1", "RejectRiskException", "Tek sevk risk istisnası reddedildi");

    private static readonly SalesDraftStateAudit WithdrawnAudit =
        new("sales.order.risk-exception.withdrawn.v1", "WithdrawRiskException", "Tek sevk risk istisnası geri çekildi");

    public async Task<SalesRiskExceptionCommandResult> RequestRiskExceptionAsync(Guid draftId, RequestSalesRiskExceptionRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        var scope = await RequireAsync(RequestPermission, cancellationToken);
        if (request.LoadingInstructionId == Guid.Empty)
        {
            throw new SalesDraftServiceException(400, "validation_failed", [new("loadingInstructionId", "validation_failed")]);
        }

        var requestHash = SalesRiskExceptionHash.Compute(draftId, request);
        var envelope = Envelope(scope, RequestPermission, metadata);
        var replay = await FindReplayAsync(scope, RequestPermission, envelope, requestHash, SalesRiskExceptionCommands.RequestV1,
            cancellationToken);
        if (replay is not null)
        {
            return Replayed(replay);
        }

        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        // Another company's order is null under the tenant filter: 404, and nothing is read for it.
        var order = await StoreAsync(scope, RequestPermission, () => orders.FindDraftAsync(draftId, cancellationToken), cancellationToken)
            ?? throw new SalesDraftServiceException(404, "not_found");
        RequireEligible(order);
        var existing = await StoreAsync(scope, RequestPermission,
            () => exceptions.ListShipmentRiskExceptionsAsync(order.Id, request.LoadingInstructionId, cancellationToken), cancellationToken);
        var today = Day(timeProvider.GetUtcNow());
        if (existing.Any(item => item.State == SalesShipmentRiskExceptionState.Requested || item.IsValidOn(today)))
        {
            throw new SalesDraftServiceException(409, SalesRiskExceptionProblemCodesV1.Duplicate,
                [new("loadingInstructionId", SalesRiskExceptionProblemCodesV1.Duplicate)]);
        }

        // EK56 SAT-D284: an order within its limit needs no exception. Only Within refuses (not recorded: the risk can change,
        // the retry is evaluated afresh); Unavailable is accepted. No amount, limit or balance is named (SAT-D162).
        if (await RiskOutcomeAsync(scope, order, cancellationToken) == SalesRiskOutcome.Within)
        {
            throw new SalesDraftServiceException(409, SalesRiskExceptionProblemCodesV1.WithinLimit,
                [new("risk", SalesRiskExceptionProblemCodesV1.WithinLimit)]);
        }

        var exceptionId = Guid.NewGuid();
        await RequireStillAsync(scope, RequestPermission, cancellationToken);
        SalesRiskExceptionStoreResult stored;
        try
        {
            stored = await exceptions.StoreRiskExceptionAsync(envelope, requestHash, order.Id,
                (exceptionNo, now) => SalesShipmentRiskException.Request(exceptionId, scope.CompanyId, order.Id, exceptionNo,
                    request.LoadingInstructionId, request.Reason, scope.ActorId, now),
                RequestedAudit, cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.RiskExceptionReasonTooLong)
        {
            throw new SalesDraftServiceException(422, exception.Code, [new("reason", exception.Code)]);
        }
        catch (SalesRuleException exception)
        {
            throw new SalesDraftServiceException(422, "validation_failed", [new("reason", exception.Code)]);
        }
        catch (ArgumentException)
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            // ADR-011 §8: a racing caller of the same key answers the winner; another pending request of the same shipment that
            // won the unique index leaves nothing under this key — 409 concurrent_change, the retry names the duplicate.
            var won = await FindReplayAsync(scope, RequestPermission, envelope, requestHash, SalesRiskExceptionCommands.RequestV1,
                cancellationToken);
            return won is null ? throw Store(exception) : Replayed(won);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        // No fence after the commit (S03): the stored exception is answered; the next request of the key meets the fence first.
        return Result(stored, stored.Outcome == SalesDraftWriteOutcome.Replayed);
    }

    public Task<SalesRiskExceptionCommandResult> ApproveRiskExceptionAsync(Guid draftId, int exceptionNo,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        DecideAsync(draftId, exceptionNo, request, SalesRiskExceptionCommands.ApproveV1, ApprovePermission, metadata,
            (target, actor, version, now) => target.Approve(version, actor, now, Day(now)), ApprovedAudit, cancellationToken);

    public Task<SalesRiskExceptionCommandResult> RejectRiskExceptionAsync(Guid draftId, int exceptionNo,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        DecideAsync(draftId, exceptionNo, request, SalesRiskExceptionCommands.RejectV1, ApprovePermission, metadata,
            (target, actor, version, now) => target.Reject(version, actor, request!.Reason, now), RejectedAudit, cancellationToken);

    /// <summary>EK56 SAT-D286: the requester withdraws their own pending request under the request permission (no new name).</summary>
    public Task<SalesRiskExceptionCommandResult> WithdrawRiskExceptionAsync(Guid draftId, int exceptionNo,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        DecideAsync(draftId, exceptionNo, request, SalesRiskExceptionCommands.WithdrawV1, RequestPermission, metadata,
            (target, actor, version, now) => target.Withdraw(version, actor, now), WithdrawnAudit, cancellationToken);

    public async Task<SalesRiskExceptionListResponse> GetRiskExceptionsAsync(Guid draftId, CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(ReadPermission, cancellationToken);
        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var order = await StoreAsync(scope, ReadPermission, () => orders.FindDraftAsync(draftId, cancellationToken), cancellationToken)
            ?? throw new SalesDraftServiceException(404, "not_found");
        var items = await StoreAsync(scope, ReadPermission,
            () => exceptions.ListRiskExceptionsAsync(order.Id, SalesRiskExceptionApiV1.MaximumListed, cancellationToken), cancellationToken);
        var today = Day(timeProvider.GetUtcNow());
        return new SalesRiskExceptionListResponse([.. items.Select(item => View(item, today))]);
    }

    private async Task<SalesRiskExceptionCommandResult> DecideAsync(Guid draftId, int exceptionNo, SalesDraftStateCommandRequest? request,
        string commandName, string permission, SalesCommandMetadata metadata,
        Action<SalesShipmentRiskException, string, long, DateTimeOffset> decide, SalesDraftStateAudit audit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        var scope = await RequireAsync(permission, cancellationToken);
        if (!long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion)
            || expectedVersion < 1)
        {
            throw new SalesDraftServiceException(400, "validation_failed", [new("expectedVersion", "validation_failed")]);
        }

        var requestHash = SalesRiskExceptionHash.Compute(commandName, draftId, exceptionNo, request);
        var envelope = Envelope(scope, permission, metadata);
        var replay = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            return Replayed(replay);
        }

        if (draftId == Guid.Empty || exceptionNo < 1)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var order = await StoreAsync(scope, permission, () => orders.FindDraftAsync(draftId, cancellationToken), cancellationToken)
            ?? throw new SalesDraftServiceException(404, "not_found");
        if (commandName == SalesRiskExceptionCommands.ApproveV1)
        {
            // A cancelled or closed order ships nothing: its exception is not approved (a rejection stays possible).
            RequireEligible(order);
        }

        // The last fence that can still prevent the write; none after the commit.
        await RequireStillAsync(scope, permission, cancellationToken);
        SalesRiskExceptionStoreResult? result;
        try
        {
            result = await exceptions.DecideRiskExceptionAsync(envelope, requestHash, commandName, order.Id, exceptionNo,
                (target, now) => decide(target, scope.ActorId, expectedVersion, now), audit, cancellationToken);
        }
        catch (SalesVersionConflictException exception)
        {
            return await TerminalAsync(scope, envelope, requestHash, commandName, order.Id,
                new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.VersionConflict, [new("expectedVersion", exception.Code)]),
                cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.StateConflict)
        {
            return await TerminalAsync(scope, envelope, requestHash, commandName, order.Id,
                new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.StateConflict, [new("state", exception.Code)]),
                cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.RiskExceptionSelfDecision)
        {
            // SAT-D153/D159: the requester decides their own request; the same actor under the same key stays refused.
            return await TerminalAsync(scope, envelope, requestHash, commandName, order.Id,
                new SalesDraftTerminalRefusal(409, SalesRiskExceptionProblemCodesV1.SelfDecision, [new("actor", exception.Code)]),
                cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.RiskExceptionNotRequester)
        {
            // EK56 SAT-D286: someone else withdraws the request; the same actor under the same key stays refused.
            return await TerminalAsync(scope, envelope, requestHash, commandName, order.Id,
                new SalesDraftTerminalRefusal(409, SalesRiskExceptionProblemCodesV1.NotRequester, [new("actor", exception.Code)]),
                cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.RiskExceptionReasonTooLong)
        {
            throw new SalesDraftServiceException(422, exception.Code, [new("reason", exception.Code)]);
        }
        catch (SalesRuleException exception)
        {
            throw new SalesDraftServiceException(422, "validation_failed", [new("reason", exception.Code)]);
        }
        catch (ArgumentException)
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            // ADR-011 §8: a lost same-key race answers the winner; a lost version race is the caller's concurrent_change.
            var won = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
            return won is null ? throw Store(exception) : Replayed(won);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        return result is null
            ? throw new SalesDraftServiceException(404, "not_found")
            : Result(result, result.Outcome == SalesDraftWriteOutcome.Replayed);
    }

    /// <summary>
    /// EK56 SAT-D284: the order's risk result under the request permission. Without a source, or when it cannot answer, the result
    /// is Unavailable (accepted, never "within"); a lost authority stays the source's 401/403. The fence is measured again after.
    /// </summary>
    private async Task<SalesRiskOutcome> RiskOutcomeAsync(Scope scope, SalesOrderDraft order, CancellationToken cancellationToken)
    {
        if (riskOutcomes is null)
        {
            return SalesRiskOutcome.Unavailable;
        }

        SalesRiskOutcome outcome;
        try
        {
            outcome = await riskOutcomes.EvaluateOrderRiskOutcomeAsync(order, RequestPermission, cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
        {
            outcome = SalesRiskOutcome.Unavailable;
        }

        await RequireStillAsync(scope, RequestPermission, cancellationToken);
        return outcome;
    }

    /// <summary>SAT-D154/D161: only an approved (now or in an earlier revision), open order ships, so only it takes an exception.</summary>
    private static void RequireEligible(SalesOrderDraft order)
    {
        var approved = order.ApprovalState == InternalApprovalState.Approved || order.ArchivedRevisions.Count > 0;
        if (!approved || order.State != SalesDraftState.Draft)
        {
            throw new SalesDraftServiceException(409, SalesRiskExceptionProblemCodesV1.OrderNotEligible,
                [new("state", SalesRiskExceptionProblemCodesV1.OrderNotEligible)]);
        }
    }

    private SalesDraftWriteEnvelope Envelope(Scope scope, string permission, SalesCommandMetadata metadata) =>
        new(scope.ActorId, permission, scope.RoleCodes, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey,
            correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent);

    private async Task<SalesRiskExceptionReplay?> FindReplayAsync(Scope scope, string permission, SalesDraftWriteEnvelope envelope,
        string requestHash, string commandName, CancellationToken cancellationToken) =>
        await StoreAsync(scope, permission,
            () => exceptions.FindRiskExceptionReplayAsync(envelope, requestHash, commandName, cancellationToken), cancellationToken);

    /// <summary>ADR-011 §4/§7: the stored exception as the replay, or the recorded refusal as the same refusal.</summary>
    private SalesRiskExceptionCommandResult Replayed(SalesRiskExceptionReplay replay)
    {
        if (replay.Refusal is { } refusal)
        {
            throw new SalesDraftServiceException(refusal.Status, refusal.Code, refusal.Errors, isReplay: true,
                originalCorrelationId: replay.OriginalCorrelationId);
        }

        return new SalesRiskExceptionCommandResult(View(replay.Exception!, Day(timeProvider.GetUtcNow())), true,
            replay.OriginalCorrelationId);
    }

    /// <summary>ADR-011 §7: the refusal is recorded under the key and then answered; a lost record race answers the winner.</summary>
    private async Task<SalesRiskExceptionCommandResult> TerminalAsync(Scope scope, SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, Guid orderId, SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken)
    {
        var permission = envelope.PermissionCode;
        await RequireStillAsync(scope, permission, cancellationToken);
        try
        {
            await exceptions.RecordRiskExceptionRefusalAsync(envelope, requestHash, commandName, orderId, refusal, cancellationToken);
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            var won = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
            if (won is not null)
            {
                return Replayed(won);
            }
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        throw new SalesDraftServiceException(refusal.Status, refusal.Code, refusal.Errors);
    }

    private SalesRiskExceptionCommandResult Result(SalesRiskExceptionStoreResult stored, bool isReplay) =>
        new(View(stored.Exception, Day(timeProvider.GetUtcNow())), isReplay, stored.OriginalCorrelationId);

    internal static SalesRiskExceptionView View(SalesShipmentRiskException item, DateOnly today) => new(
        item.OrderId,
        item.ExceptionNo,
        item.LoadingInstructionId,
        item.State.ToString(),
        item.Version.ToString(CultureInfo.InvariantCulture),
        item.Reason,
        item.RequestedBy,
        item.RequestedAtUtc,
        item.Decision?.DecidedBy,
        item.Decision?.DecidedAtUtc,
        item.Decision?.RejectionReason,
        item.ValidOn,
        item.IsValidOn(today));

    /// <summary>SAT-D10/D160: the Europe/Istanbul day; without the zone nothing is guessed (503, never UTC).</summary>
    private static DateOnly Day(DateTimeOffset instant)
    {
        try
        {
            return SalesBusinessDay.DateOf(instant);
        }
        catch (SalesBusinessDayUnavailableException)
        {
            throw new SalesDraftServiceException(503, "service_unavailable");
        }
    }

    // ---- fences -------------------------------------------------------------------------------------------------------------

    private async Task<Scope> RequireAsync(string permission, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new SalesDraftServiceException(401, "unauthenticated");
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }

        if (!ErpPermissions.IsKnown(permission))
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.PermissionUnregistered);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId;
        var measured = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        if (!measured.IsGranted || !measured.IsCompanyWide || !Matches(companyId, actorId))
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }

        return new Scope(companyId, actorId, [.. measured.RoleCodes.Order(StringComparer.Ordinal)]);
    }

    private async Task RequireStillAsync(Scope scope, string permission, CancellationToken cancellationToken)
    {
        var current = await RequireAsync(permission, cancellationToken);
        if (current.CompanyId != scope.CompanyId || !string.Equals(current.ActorId, scope.ActorId, StringComparison.Ordinal))
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }
    }

    private bool Matches(Guid companyId, string actorId) =>
        currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal)
        && companyContext.IsResolved
        && companyContext.CompanyId == companyId
        && currentUser.CanAccessCompany(companyId);

    /// <summary>A Sales store read under the fence: a store refusal is mapped; the fence is measured again after the await.</summary>
    private async Task<T> StoreAsync<T>(Scope scope, string permission, Func<Task<T>> read, CancellationToken cancellationToken)
    {
        T result;
        try
        {
            result = await read();
        }
        catch (SalesDraftStoreException exception)
        {
            await RequireStillAsync(scope, permission, cancellationToken);
            throw Store(exception);
        }

        await RequireStillAsync(scope, permission, cancellationToken);
        return result;
    }

    private static SalesDraftServiceException Store(SalesDraftStoreException exception) => exception.Code switch
    {
        SalesDraftStoreCodes.SameKeyPayloadMismatch => new SalesDraftServiceException(409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch),
        SalesDraftStoreCodes.ConcurrentChange => new SalesDraftServiceException(409, SalesDraftProblemCodesV1.ConcurrentChange),
        SalesDraftStoreCodes.CompanyContextMismatch => new SalesDraftServiceException(403, "forbidden"),
        _ => new SalesDraftServiceException(503, "service_unavailable"),
    };

    private sealed record Scope(Guid CompanyId, string ActorId, IReadOnlyCollection<string> RoleCodes);
}
