using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Requests;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// API-20 / API-21 (plan §3.2, S-01; delegation No 160, 161): the sales request use cases, on the draft service's fence and
/// owners. The order of every command is the draft's: authority first (nothing, not even the replay, is read before it),
/// then the replay (ADR-011 §4/§7), then the owners (Cari account → per line Product eligibility → Settings order-unit
/// profile, each answered for this company on the business date and measured again after every awaited read), then the
/// domain, then one save with the attempt and the audit. No price, money profile or policy value is read or chosen.
/// <list type="bullet">
/// <item><b>Names:</b> <c>sales.requests.write</c> records, <c>sales.requests.read</c> lists and reads,
/// <c>sales.requests.approve</c> decides; none confers another, and no draft name confers them.</item>
/// <item><b>Decisions:</b> one approver; a rejection needs its reason and is allowed from Submitted or Approved. A stale
/// version or a wrong state is the key's terminal 409, recorded and replayed (ADR-011 §7).</item>
/// <item><b>Missing store:</b> a host that composes no request store answers 503 (setup), never a silent empty list.</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService
{
    public async Task<SalesRequestResponse> CreateRequestAsync(CreateSalesRequestRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();

        // 1. Authority first (ADR-011 §5).
        var permission = bindings.RequestWritePermission;
        var scope = await RequireAsync(permission, cancellationToken);
        var store = RequestStore();
        var requestHash = SalesRequestHash.Compute(request);
        var envelope = Envelope(scope, metadata, permission!);

        // 2. Replay: the same key, content and actor answers what it answered before.
        var replay = await FindRequestReplayAsync(scope, permission, envelope, requestHash, SalesRequestCommands.CreateV1, cancellationToken);
        if (replay is not null)
        {
            return RequestReplayed(replay);
        }

        // 3. The owners, each answered for this company and business date.
        var lineInputs = new List<SalesRequestLineInput>(request.Lines.Count);
        try
        {
            await RequireAccountAsync(scope, permission, request.PartyId, request.CompanyAccountId, cancellationToken);
            for (var index = 0; index < request.Lines.Count; index++)
            {
                var line = request.Lines[index];
                var (product, orderUnit) = await ReadProductUnitAsync(scope, permission, index, line.ProductId, line.ExpectedProductVersion,
                    line.OrderUnitId, request.BusinessDate,
                    refusal => RequestTerminalAsync(scope, permission, envelope, requestHash, SalesRequestCommands.CreateV1, null, refusal,
                        cancellationToken),
                    cancellationToken);
                lineInputs.Add(new SalesRequestLineInput(line.LineId, product, orderUnit, line.Quantity, Decimal(line.Quantity),
                    line.RequestedDeliveryDate, line.Note));
            }
        }
        catch (RequestRaceWon won)
        {
            return won.Response;
        }

        // 4. The last fence that can still prevent the write; the domain builds the request on the store's number.
        scope = await RequireStillAsync(scope, permission, cancellationToken);
        var header = new SalesRequestHeader(request.PartyId, request.CompanyAccountId, request.BusinessDate,
            request.RequestedDeliveryDate, request.Note);
        var now = timeProvider.GetUtcNow();
        SalesRequestStoreResult stored;
        try
        {
            stored = await store.StoreRequestAsync(envelope, requestHash,
                documentNo => SalesRequest.Create(Guid.NewGuid(), scope.CompanyId, documentNo, header, lineInputs, scope.ActorId, now),
                cancellationToken);
        }
        catch (SalesRuleException exception)
        {
            throw new SalesDraftServiceException(422, "validation_failed", [new("request", exception.Code)]);
        }
        catch (ArgumentException)
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            // ADR-011 §8: the loser of a same-key race answers the winner's result, read afresh.
            var won = await FindRequestReplayAsync(scope, permission, envelope, requestHash, SalesRequestCommands.CreateV1, cancellationToken);
            return won is null ? throw Store(exception) : RequestReplayed(won);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        // No fence after the commit (ORG #321 P3-1): the committed result is answered; the next request of the key meets the fence.
        return new SalesRequestResponse(RequestDetail(stored.Request), stored.Outcome == SalesDraftWriteOutcome.Replayed,
            stored.OriginalCorrelationId);
    }

    public async Task<SalesRequestListResponse> GetRequestListAsync(SalesRequestListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.RequestReadPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        var store = RequestStore();
        var pageSize = query.PageSize == 0 ? DefaultPageSize : query.PageSize;
        if (pageSize is < 1 or > MaximumPageSize || query.Cursor?.Length > MaximumCursorLength
            || (query.Cursor is not null && string.IsNullOrWhiteSpace(query.Cursor)))
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }

        // An exact state name narrows the list; anything else is a shape error, never "all".
        SalesRequestState? state = null;
        if (query.State is not null)
        {
            if (!Enum.TryParse<SalesRequestState>(query.State, ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed)
                || !string.Equals(parsed.ToString(), query.State, StringComparison.Ordinal))
            {
                throw new SalesDraftServiceException(400, "validation_failed", [new("state", "validation_failed")]);
            }

            state = parsed;
        }

        SalesRequestPage page;
        try
        {
            page = await GuardedAsync(scope, permission, () => store.ListRequestsAsync(pageSize, query.Cursor, state, cancellationToken),
                cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        return new SalesRequestListResponse([.. page.Items.Select(RequestSummary)], page.NextCursor);
    }

    public async Task<SalesRequestDetailView> GetRequestDetailAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.RequestReadPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        var store = RequestStore();
        if (requestId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        SalesRequest? request;
        try
        {
            request = await GuardedAsync(scope, permission, () => store.FindRequestAsync(requestId, cancellationToken), cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        // Another company's request is null under the tenant filter: 404, never 403.
        return request is null ? throw new SalesDraftServiceException(404, "not_found") : RequestDetail(request);
    }

    public Task<SalesRequestResponse> ApproveRequestAsync(Guid requestId, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default) =>
        DecideRequestAsync(requestId, request, metadata, SalesRequestCommands.ApproveV1,
            (target, actor, version, now) => target.Approve(version, actor, now),
            new SalesDraftStateAudit("sales.request.approved.v1", "Approve", "Satış talebi onaylandı"), cancellationToken);

    public Task<SalesRequestResponse> RejectRequestAsync(Guid requestId, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default) =>
        DecideRequestAsync(requestId, request, metadata, SalesRequestCommands.RejectV1,
            (target, actor, version, now) => target.Reject(version, actor, request.Reason, now),
            new SalesDraftStateAudit("sales.request.rejected.v1", "Reject", "Satış talebi reddedildi"), cancellationToken);

    private async Task<SalesRequestResponse> DecideRequestAsync(Guid requestId, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, string commandName, Action<SalesRequest, string, long, DateTimeOffset> decide,
        SalesDraftStateAudit audit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();

        var permission = bindings.RequestApprovePermission;
        var scope = await RequireAsync(permission, cancellationToken);
        var store = RequestStore();
        if (!long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion) || expectedVersion < 1)
        {
            throw new SalesDraftServiceException(400, "validation_failed", [new("expectedVersion", "validation_failed")]);
        }

        var requestHash = SalesRequestHash.Compute(commandName, requestId, request);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindRequestReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            return RequestReplayed(replay);
        }

        if (requestId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        // The last fence that can still prevent the write; none after the commit (ORG #321 P3-1).
        scope = await RequireStillAsync(scope, permission, cancellationToken);
        SalesRequestStoreResult? result;
        try
        {
            result = await store.DecideRequestAsync(envelope, requestHash, commandName, requestId,
                (target, now) => decide(target, scope.ActorId, expectedVersion, now), audit, cancellationToken);
        }
        catch (SalesVersionConflictException exception)
        {
            // §7: the caller's expected version is stale — recorded under the key, replayed on retry.
            throw await RequestTerminalAsync(scope, permission, envelope, requestHash, commandName, requestId,
                new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.VersionConflict, [new("expectedVersion", exception.Code)]),
                cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.StateConflict)
        {
            // §7: the decision is not allowed from the request's state — terminal for this key too.
            throw await RequestTerminalAsync(scope, permission, envelope, requestHash, commandName, requestId,
                new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.StateConflict, [new("state", exception.Code)]),
                cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code is SalesRuleCodes.RequestRejectionReasonRequired
                                                        or SalesRuleCodes.RequestRejectionReasonTooLong)
        {
            throw new SalesDraftServiceException(422, exception.Code, [new("reason", exception.Code)]);
        }
        catch (SalesRuleException exception)
        {
            throw new SalesDraftServiceException(422, "validation_failed", [new("request", exception.Code)]);
        }
        catch (ArgumentException)
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            // §8: a lost same-key race answers the winner; a lost version race is the caller's concurrent_change.
            var won = await FindRequestReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
            return won is null ? throw Store(exception) : RequestReplayed(won);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        return result is null
            ? throw new SalesDraftServiceException(404, "not_found")
            : new SalesRequestResponse(RequestDetail(result.Request), result.Outcome == SalesDraftWriteOutcome.Replayed,
                result.OriginalCorrelationId);
    }

    /// <summary>A host without the request store is a setup gap (503), never an empty answer.</summary>
    private ISalesRequestStore RequestStore() =>
        requests ?? throw new SalesDraftServiceException(503, "service_unavailable");

    private async Task<SalesRequestReplay?> FindRequestReplayAsync(Scope scope, string? permission, SalesDraftWriteEnvelope envelope,
        string requestHash, string commandName, CancellationToken cancellationToken)
    {
        var store = RequestStore();
        try
        {
            return await GuardedAsync(scope, permission,
                () => store.FindRequestReplayAsync(envelope, requestHash, commandName, cancellationToken), cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }
    }

    /// <summary>ADR-011 §4/§7: the stored request as the replay, or the recorded refusal as the same refusal.</summary>
    private SalesRequestResponse RequestReplayed(SalesRequestReplay replay)
    {
        if (replay.Refusal is { } refusal)
        {
            throw new SalesDraftServiceException(refusal.Status, refusal.Code, refusal.Errors, isReplay: true,
                originalCorrelationId: replay.OriginalCorrelationId);
        }

        return new SalesRequestResponse(RequestDetail(replay.Request!), true, replay.OriginalCorrelationId);
    }

    /// <summary>
    /// ADR-011 §7 for a request command: the refusal is recorded under the key before it is answered; a lost record race
    /// answers the winner (its refusal as a replay, its request through <see cref="RequestRaceWon"/>).
    /// </summary>
    private async Task<SalesDraftServiceException> RequestTerminalAsync(Scope scope, string? permission, SalesDraftWriteEnvelope envelope,
        string requestHash, string commandName, Guid? requestId, SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken)
    {
        await RequireStillAsync(scope, permission, cancellationToken);
        try
        {
            await RequestStore().RecordRequestRefusalAsync(envelope, requestHash, commandName, requestId, refusal, cancellationToken);
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            var won = await FindRequestReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
            if (won?.Refusal is { } recorded)
            {
                return new SalesDraftServiceException(recorded.Status, recorded.Code, recorded.Errors, isReplay: true,
                    originalCorrelationId: won.OriginalCorrelationId);
            }

            if (won?.Request is not null)
            {
                throw new RequestRaceWon(RequestReplayed(won));
            }
        }

        return new SalesDraftServiceException(refusal.Status, refusal.Code, refusal.Errors);
    }

    /// <summary>Raised inside the owner reads when another caller of the same key stored a request first; carries its replay.</summary>
    private sealed class RequestRaceWon(SalesRequestResponse response) : Exception
    {
        public SalesRequestResponse Response { get; } = response;
    }

    private static SalesRequestSummaryView RequestSummary(SalesRequest request) => new(
        request.Id, request.CompanyId, request.DocumentNo, request.State.ToString(),
        request.Version.ToString(CultureInfo.InvariantCulture), request.Header.PartyId, request.Header.CompanyAccountId,
        request.Header.BusinessDate, request.CreatedAtUtc, request.Lines.Count);

    private static SalesRequestDetailView RequestDetail(SalesRequest request) => new(
        RequestSummary(request), request.Header.RequestedDeliveryDate, request.Header.Note,
        [.. request.Lines.Select(line => new SalesRequestLineView(line.Id, line.LineNo, line.Product.ProductId, line.Product.ProductVersion,
            line.Product.Code, line.Product.Name, line.OrderUnit.UnitId, line.OrderUnit.UnitCode, line.QuantityText,
            line.RequestedDeliveryDate, line.Note))],
        request.Decision is { } decision
            ? new SalesRequestDecisionV1(decision.DecidedBy, decision.DecidedAtUtc, decision.RejectionReason)
            : null);
}
