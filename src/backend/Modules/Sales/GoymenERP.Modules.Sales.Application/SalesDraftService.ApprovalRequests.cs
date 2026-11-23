using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.ApprovalRequests;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Quotes;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// WP4 — the Sales approval requests (SINAN_KARARLARI_2026-11-22_EK_SORULAR SAT-D475 / SAT-D476). The K3 skeleton of the risk
/// exception: the operation's own name is measured before anything is read, after every awaited read and right before the single
/// save; the replay comes before any read; one save writes the request (or its decision), its attempt and its audit event; a
/// terminal refusal (stale version, state conflict, self-decision, a quote that needs no request) is recorded under the key and
/// answered again on retry. No new permission is registered and none is granted.
/// <list type="bullet">
/// <item><b>SAT-D476 — quote conversion request</b> (<c>sales.quotes.write</c>): an OPEN quote, EXPIRED on the server's
/// business day, whose current revision carries no expired-conversion approval and no pending request. The quote's own approval
/// (<c>ApproveExpiredQuoteConversionAsync</c>, unchanged) answers it; a rejection (<c>sales.quotes.expired-conversion.approve</c>)
/// carries a reason. No maker ≠ checker is decided for it (SAT-D44 (2)).</item>
/// <item><b>SAT-D475 — over-delivery request:</b> opened by the Logistics shipment gate through
/// <see cref="ISalesOverDeliveryGatePortV1"/> (<see cref="EvaluateOverDeliveryGateAsync"/>) in the shipper's name; approved or
/// rejected under <c>sales.orders.over-delivery.approve</c>, never by the shipper (SAT-D216). The approval judges the requested
/// shipped total afresh with the order's measured quantity, the Settings tolerance and the one realization calculator: only an
/// approval-band total is approved, and the approval appends the order's own over-delivery approval fact (SAT-D30 / SAT-D213)
/// covering that total, so the shipment then passes the gate and the realization axis shows it approved.</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService : ISalesApprovalRequestService
{
    private static readonly SalesDraftStateAudit QuoteConversionRequestedAudit =
        new("sales.quote.expired-conversion-requested.v1", "RequestQuoteConversion", "Süresi dolmuş teklif için dönüşüm onayı talep edildi");

    private static readonly SalesDraftStateAudit QuoteConversionRejectedAudit =
        new("sales.quote.expired-conversion-request-rejected.v1", "RejectQuoteConversionRequest", "Teklif dönüşüm onayı talebi reddedildi");

    private static readonly SalesDraftStateAudit OverDeliveryRequestedAudit =
        new("sales.order.over-delivery-requested.v1", "RequestOverDelivery", "Onay bandında fazla sevk denendi; onay talebi açıldı");

    private static readonly SalesDraftStateAudit OverDeliveryRequestApprovedAudit =
        new("sales.order.over-delivery-request-approved.v1", "ApproveOverDeliveryRequest", "Fazla teslim talebi onaylandı");

    private static readonly SalesDraftStateAudit OverDeliveryRequestRejectedAudit =
        new("sales.order.over-delivery-request-rejected.v1", "RejectOverDeliveryRequest", "Fazla teslim talebi reddedildi");

    public async Task<SalesApprovalRequestCommandResult> RequestQuoteConversionAsync(Guid quoteId,
        SalesQuoteConversionRequestRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.QuoteWritePermission;
        var scope = await RequireAsync(permission, cancellationToken);
        var requests = ApprovalRequests();
        var quoteStore = QuoteStore();
        var expectedVersion = ExpectedVersion(request.ExpectedVersion);
        const string commandName = SalesApprovalRequestCommands.RequestQuoteConversionV1;
        var requestHash = SalesApprovalRequestHash.Compute(quoteId, request);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindApprovalReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            return await ApprovalReplayedAsync(scope, permission, replay, cancellationToken);
        }

        if (quoteId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var today = ServerBusinessDay();
        var quote = await ApprovalStoreAsync(scope, permission, () => quoteStore.FindQuoteAsync(quoteId, cancellationToken), cancellationToken)
            ?? throw new SalesDraftServiceException(404, "not_found");
        var refusal = QuoteRequestRefusal(quote, expectedVersion, today);
        if (refusal is not null)
        {
            return await ApprovalTerminalAsync(scope, envelope, requestHash, commandName, quote.Id, refusal, cancellationToken);
        }

        var existing = await ApprovalStoreAsync(scope, permission, () => requests.ListApprovalRequestsAsync(
            SalesApprovalRequestKind.QuoteExpiredConversion, quote.Id, SalesApprovalRequestApiV1.MaximumListed, cancellationToken),
            cancellationToken);
        if (existing.Any(item => item.State == SalesApprovalRequestState.Requested && item.SubjectRevision == quote.Revision))
        {
            // Not recorded: the pending request may be rejected and a new one asked under the same key.
            throw new SalesDraftServiceException(409, SalesApprovalRequestProblemCodesV1.Duplicate,
                [new("quote", SalesApprovalRequestProblemCodesV1.Duplicate)]);
        }

        var requestId = Guid.NewGuid();
        await RequireStillAsync(scope, permission, cancellationToken);
        SalesApprovalRequestStoreResult stored;
        try
        {
            stored = await requests.StoreQuoteConversionRequestAsync(envelope, requestHash, quote.Id,
                (requestNo, now) => SalesApprovalRequest.RequestQuoteConversion(requestId, scope.CompanyId, quote.Id, requestNo,
                    quote.Revision, request.Note, scope.ActorId, now),
                QuoteConversionRequestedAudit, cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.ApprovalRequestTextTooLong)
        {
            throw new SalesDraftServiceException(422, SalesApprovalRequestProblemCodesV1.TextTooLong,
                [new("note", SalesApprovalRequestProblemCodesV1.TextTooLong)]);
        }
        catch (ArgumentException)
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            var won = await FindApprovalReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
            return won is null ? throw Store(exception) : await ApprovalReplayedAsync(scope, permission, won, cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        return await ApprovalResultAsync(scope, permission, stored, cancellationToken);
    }

    public Task<SalesApprovalRequestCommandResult> RejectQuoteConversionRequestAsync(Guid quoteId, int requestNo,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        RejectApprovalRequestAsync(SalesApprovalRequestKind.QuoteExpiredConversion, quoteId, requestNo, request,
            SalesApprovalRequestCommands.RejectQuoteConversionV1, bindings.QuoteExpiredConversionApprovePermission, metadata,
            QuoteConversionRejectedAudit, cancellationToken);

    public Task<SalesApprovalRequestCommandResult> RejectOverDeliveryRequestAsync(Guid draftId, int requestNo,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        RejectApprovalRequestAsync(SalesApprovalRequestKind.OverDelivery, draftId, requestNo, request,
            SalesApprovalRequestCommands.RejectOverDeliveryV1, bindings.OverDeliveryApprovePermission, metadata,
            OverDeliveryRequestRejectedAudit, cancellationToken);

    public async Task<SalesApprovalRequestCommandResult> ApproveOverDeliveryRequestAsync(Guid draftId, int requestNo,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.OverDeliveryApprovePermission;
        var scope = await RequireAsync(permission, cancellationToken);
        var requests = ApprovalRequests();
        var expectedVersion = ExpectedVersion(request.ExpectedVersion);
        const string commandName = SalesApprovalRequestCommands.ApproveOverDeliveryV1;
        var requestHash = SalesApprovalRequestHash.Compute(commandName, draftId, requestNo, request);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindApprovalReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            return await ApprovalReplayedAsync(scope, permission, replay, cancellationToken);
        }

        if (draftId == Guid.Empty || requestNo < 1)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        SalesApprovalRequestStoreResult? result;
        try
        {
            result = await requests.ApproveOverDeliveryRequestAsync(envelope, requestHash, draftId, requestNo,
                async (draft, item, writeCancellationToken) =>
                {
                    scope = await RequireStillAsync(scope, permission, writeCancellationToken);
                    var input = await OverDeliveryRequestInputAsync(scope, permission, draft, item, expectedVersion, writeCancellationToken);
                    // The last fence that can still prevent the write; none after the commit.
                    scope = await RequireStillAsync(scope, permission, writeCancellationToken);
                    return input;
                },
                (draft, item, input, now) =>
                {
                    item.Approve(expectedVersion, scope.ActorId, now);
                    return draft.ApproveOverDelivery(draft.Version, scope.ActorId, input, now);
                },
                OverDeliveryRequestApprovedAudit, cancellationToken);
        }
        catch (SalesVersionConflictException exception)
        {
            return await ApprovalTerminalAsync(scope, envelope, requestHash, commandName, draftId,
                new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.VersionConflict, [new("expectedVersion", exception.Code)]),
                cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.StateConflict)
        {
            return await ApprovalTerminalAsync(scope, envelope, requestHash, commandName, draftId,
                new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.StateConflict, [new("state", exception.Code)]),
                cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code is SalesRuleCodes.ApprovalRequestSelfDecision
            or SalesRuleCodes.OverDeliverySelfApproval)
        {
            return await ApprovalTerminalAsync(scope, envelope, requestHash, commandName, draftId,
                new SalesDraftTerminalRefusal(409, SalesApprovalRequestProblemCodesV1.SelfDecision, [new("actor", exception.Code)]),
                cancellationToken);
        }
        catch (ApprovalRequestStale stale)
        {
            return await ApprovalTerminalAsync(scope, envelope, requestHash, commandName, draftId,
                new SalesDraftTerminalRefusal(409, SalesApprovalRequestProblemCodesV1.Stale, [new(stale.Field, SalesApprovalRequestProblemCodesV1.Stale)]),
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
            var won = await FindApprovalReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
            return won is null ? throw Store(exception) : await ApprovalReplayedAsync(scope, permission, won, cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        return result is null
            ? throw new SalesDraftServiceException(404, "not_found")
            : await ApprovalResultAsync(scope, permission, result, cancellationToken);
    }

    public async Task<SalesApprovalRequestListResponse> GetQuoteConversionRequestsAsync(Guid quoteId, CancellationToken cancellationToken = default)
    {
        var permission = bindings.QuoteReadPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        var requests = ApprovalRequests();
        var quoteStore = QuoteStore();
        if (quoteId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var quote = await ApprovalStoreAsync(scope, permission, () => quoteStore.FindQuoteAsync(quoteId, cancellationToken), cancellationToken)
            ?? throw new SalesDraftServiceException(404, "not_found");
        var items = await ApprovalStoreAsync(scope, permission, () => requests.ListApprovalRequestsAsync(
            SalesApprovalRequestKind.QuoteExpiredConversion, quote.Id, SalesApprovalRequestApiV1.MaximumListed, cancellationToken),
            cancellationToken);
        return new SalesApprovalRequestListResponse([.. items.Select(item => ApprovalRequestView(item, quote))]);
    }

    public async Task<SalesApprovalRequestListResponse> GetOverDeliveryRequestsAsync(Guid draftId, CancellationToken cancellationToken = default)
    {
        var permission = bindings.ReadPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        var requests = ApprovalRequests();
        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var order = await ApprovalStoreAsync(scope, permission, () => store.FindDraftAsync(draftId, cancellationToken), cancellationToken)
            ?? throw new SalesDraftServiceException(404, "not_found");
        var items = await ApprovalStoreAsync(scope, permission, () => requests.ListApprovalRequestsAsync(
            SalesApprovalRequestKind.OverDelivery, order.Id, SalesApprovalRequestApiV1.MaximumListed, cancellationToken),
            cancellationToken);
        return new SalesApprovalRequestListResponse([.. items.Select(item => ApprovalRequestView(item, null))]);
    }

    /// <summary>
    /// SAT-D475 — the Logistics shipment gate (a company-scoped system read; the consumer measured its own authority). The shipped
    /// total the shipment would make is judged against the line's measured quantity with the order business date's Settings
    /// tolerance and the one realization calculator: within or warn → Pass; approval band covered by a recorded over-delivery
    /// approval → Pass; approval band otherwise → the line's pending request is answered or a new one is opened in the shipper's
    /// name → Held; block band → Blocked. Anything that cannot be decided is Unavailable (the shipment stops).
    /// </summary>
    public async Task<SalesOverDeliveryGateReadV1> EvaluateOverDeliveryGateAsync(SalesOverDeliveryGateRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw new InvalidOperationException("Fazla teslim kapısı şirket bağlamı çözülmeden okunmaz.");
        }

        SalesOverDeliveryGateReadV1 Answer(SalesOverDeliveryGateResultV1 result, int? requestNo = null) =>
            new(request.OrderId, request.LineId, result, requestNo, SalesOverDeliveryGateContractV1.Version);

        if (request.OrderId == Guid.Empty || request.LineId == Guid.Empty || request.ShippedBefore < 0m || request.Quantity <= 0m
            || request.Envelope is null || store is not ISalesApprovalRequestStore requests)
        {
            return Answer(SalesOverDeliveryGateResultV1.Unavailable);
        }

        var scope = new Scope(companyContext.CompanyId, string.Empty, [], IsSalesSupport: false, IsSystemRead: true);
        try
        {
            var order = await GuardedAsync(scope, null, () => store.FindDraftAsync(request.OrderId, cancellationToken), cancellationToken);
            var line = order?.Lines.SingleOrDefault(candidate => candidate.Id == request.LineId);
            if (order is null || order.CompanyId != scope.CompanyId || line is null || Basis(order) is not { } basis
                || !basis.Lines.TryGetValue(line.Id, out var measured))
            {
                return Answer(SalesOverDeliveryGateResultV1.Unavailable);
            }

            var total = checked(request.ShippedBefore + request.Quantity);
            var ordered = measured.Quantity;
            var proposed = Quantity.Of(total, ordered.Unit);
            if (!proposed.IsGreaterThan(ordered))
            {
                return Answer(SalesOverDeliveryGateResultV1.Pass);
            }

            var policy = await ToleranceAsync(scope, null, order, cancellationToken);
            if (policy is null)
            {
                return Answer(SalesOverDeliveryGateResultV1.Unavailable);
            }

            var band = SalesRealizationCalculator.Band(ordered, proposed.Subtract(ordered), policy.Tolerance);
            if (band is DeliveryToleranceBand.Within or DeliveryToleranceBand.Warn)
            {
                return Answer(SalesOverDeliveryGateResultV1.Pass);
            }

            if (band == DeliveryToleranceBand.Block)
            {
                return Answer(SalesOverDeliveryGateResultV1.Blocked);
            }

            if (order.IsOverDeliveryApproved(line.Id, ordered, proposed))
            {
                return Answer(SalesOverDeliveryGateResultV1.Pass);
            }

            var (pending, _) = await requests.OpenOverDeliveryRequestAsync(new SalesOverDeliveryRequestOpenV1(order.Id, line.Id,
                basis.RevisionNo, ordered.Value, request.Quantity, total, request.LoadingInstructionId, request.ShipmentId,
                request.Envelope), OverDeliveryRequestedAudit, cancellationToken);
            return Answer(SalesOverDeliveryGateResultV1.Held, pending.RequestNo);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException { Status: 401 or 403 }))
        {
            return Answer(SalesOverDeliveryGateResultV1.Unavailable);
        }
    }

    // ---- the over-delivery approval's fresh judgement --------------------------------------------------------------------------

    /// <summary>
    /// The approval input read afresh on the order the store holds: the request must still name an open, approved order's line
    /// measured against the same quantity, and its requested total must be in the approval band (the block band is never approved;
    /// a total that no longer needs approval is stale). The shipper the input names is the request's own requester (SAT-D216).
    /// </summary>
    private async Task<OverDeliveryApprovalInput> OverDeliveryRequestInputAsync(Scope scope, string? permission, SalesOrderDraft draft,
        SalesApprovalRequest item, long expectedVersion, CancellationToken cancellationToken)
    {
        if (item.Version != expectedVersion)
        {
            throw new SalesVersionConflictException(expectedVersion, item.Version);
        }

        if (item.State != SalesApprovalRequestState.Requested)
        {
            throw new SalesRuleException(SalesRuleCodes.StateConflict, "Yalnız bekleyen talep karara bağlanır.");
        }

        if (string.Equals(item.RequestedBy, scope.ActorId, StringComparison.Ordinal))
        {
            throw new SalesRuleException(SalesRuleCodes.ApprovalRequestSelfDecision, "Sevki deneyen kişi kendi talebini onaylayamaz.");
        }

        if (item.LineId is not { } lineId || item.OrderedValue is not { } requestedOrdered || item.RequestedTotalValue is not { } requestedTotal)
        {
            throw new InvalidOperationException("Fazla teslim talebinin olguları eksik.");
        }

        SalesOrderDraftLine line;
        try
        {
            line = draft.RequireOverDeliveryApprovable(draft.Version, lineId);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.OverDeliveryLineUnknown)
        {
            throw new ApprovalRequestStale("lineId");
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.StateConflict)
        {
            throw new ApprovalRequestStale("state");
        }

        if (draft.State != SalesDraftState.Draft || Basis(draft) is not { } basis || !basis.Lines.TryGetValue(line.Id, out var measured)
            || measured.Quantity.Value != requestedOrdered)
        {
            // A cancelled or closed order ships nothing more, and a revision that changed the measured quantity leaves the request
            // answering another question.
            throw new ApprovalRequestStale("ordered");
        }

        var policy = await ToleranceAsync(scope, permission, draft, cancellationToken)
            ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.OverDeliveryPolicyUnavailable);
        Quantity total;
        DeliveryToleranceBand band;
        try
        {
            total = Quantity.Of(requestedTotal, measured.Quantity.Unit);
            band = SalesRealizationCalculator.Band(measured.Quantity, total.Subtract(measured.Quantity), policy.Tolerance);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException or SalesRuleException)
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.OverDeliveryPolicyUnavailable);
        }

        if (band == DeliveryToleranceBand.Block)
        {
            throw new SalesDraftServiceException(422, SalesApprovalRequestProblemCodesV1.Blocked,
                [new("requestedTotal", SalesApprovalRequestProblemCodesV1.Blocked)]);
        }

        if (band != DeliveryToleranceBand.Approval || draft.IsOverDeliveryApproved(line.Id, measured.Quantity, total))
        {
            throw new ApprovalRequestStale("requestedTotal");
        }

        return new OverDeliveryApprovalInput(line.Id, basis.RevisionNo, measured.Text, requestedTotal, [item.RequestedBy]);
    }

    // ---- shared helpers ---------------------------------------------------------------------------------------------------------

    private async Task<SalesApprovalRequestCommandResult> RejectApprovalRequestAsync(SalesApprovalRequestKind kind, Guid subjectId,
        int requestNo, SalesDraftStateCommandRequest? request, string commandName, string? permission, SalesCommandMetadata metadata,
        SalesDraftStateAudit audit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(permission, cancellationToken);
        var requests = ApprovalRequests();
        var expectedVersion = ExpectedVersion(request.ExpectedVersion);
        var requestHash = SalesApprovalRequestHash.Compute(commandName, subjectId, requestNo, request);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindApprovalReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            return await ApprovalReplayedAsync(scope, permission, replay, cancellationToken);
        }

        if (subjectId == Guid.Empty || requestNo < 1)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        await RequireStillAsync(scope, permission, cancellationToken);
        SalesApprovalRequestStoreResult? result;
        try
        {
            result = await requests.RejectApprovalRequestAsync(envelope, requestHash, commandName, kind, subjectId, requestNo,
                (target, now) => target.Reject(expectedVersion, scope.ActorId, request.Reason, now), audit, cancellationToken);
        }
        catch (SalesVersionConflictException exception)
        {
            return await ApprovalTerminalAsync(scope, envelope, requestHash, commandName, subjectId,
                new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.VersionConflict, [new("expectedVersion", exception.Code)]),
                cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.StateConflict)
        {
            return await ApprovalTerminalAsync(scope, envelope, requestHash, commandName, subjectId,
                new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.StateConflict, [new("state", exception.Code)]),
                cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.ApprovalRequestSelfDecision)
        {
            return await ApprovalTerminalAsync(scope, envelope, requestHash, commandName, subjectId,
                new SalesDraftTerminalRefusal(409, SalesApprovalRequestProblemCodesV1.SelfDecision, [new("actor", exception.Code)]),
                cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.ApprovalRequestReasonRequired)
        {
            throw new SalesDraftServiceException(422, SalesApprovalRequestProblemCodesV1.ReasonRequired,
                [new("reason", SalesApprovalRequestProblemCodesV1.ReasonRequired)]);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.ApprovalRequestTextTooLong)
        {
            throw new SalesDraftServiceException(422, SalesApprovalRequestProblemCodesV1.TextTooLong,
                [new("reason", SalesApprovalRequestProblemCodesV1.TextTooLong)]);
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
            var won = await FindApprovalReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
            return won is null ? throw Store(exception) : await ApprovalReplayedAsync(scope, permission, won, cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        return result is null
            ? throw new SalesDraftServiceException(404, "not_found")
            : await ApprovalResultAsync(scope, permission, result, cancellationToken);
    }

    /// <summary>The quote request's recorded refusals: a stale version, a closed quote, a quote not expired or already approved.</summary>
    private static SalesDraftTerminalRefusal? QuoteRequestRefusal(SalesQuote quote, long expectedVersion, DateOnly today)
    {
        if (quote.Version != expectedVersion)
        {
            return new(409, SalesDraftProblemCodesV1.VersionConflict, [new("expectedVersion", SalesDraftProblemCodesV1.VersionConflict)]);
        }

        if (quote.State != SalesQuoteState.Open)
        {
            return new(409, SalesDraftProblemCodesV1.StateConflict, [new("state", SalesDraftProblemCodesV1.StateConflict)]);
        }

        if (!quote.IsExpiredOn(today))
        {
            return new(409, SalesApprovalRequestProblemCodesV1.QuoteNotExpired, [new("validUntil", SalesApprovalRequestProblemCodesV1.QuoteNotExpired)]);
        }

        return quote.CurrentExpiryApproval is not null
            ? new(409, SalesApprovalRequestProblemCodesV1.QuoteAlreadyApproved, [new("quote", SalesApprovalRequestProblemCodesV1.QuoteAlreadyApproved)])
            : null;
    }

    /// <summary>
    /// The derived status: a rejected or approved request as recorded; a quote request is Approved when its quote carries the
    /// expired-conversion approval of the asked revision, Stale when the quote moved on without one, Pending otherwise. Without the
    /// quote (an over-delivery request), Requested is Pending.
    /// </summary>
    internal static string ApprovalRequestStatus(SalesApprovalRequest item, SalesQuote? quote)
    {
        if (item.State == SalesApprovalRequestState.Rejected)
        {
            return SalesApprovalRequestApiV1.StatusRejected;
        }

        if (item.State == SalesApprovalRequestState.Approved)
        {
            return SalesApprovalRequestApiV1.StatusApproved;
        }

        if (item.Kind != SalesApprovalRequestKind.QuoteExpiredConversion || quote is null)
        {
            return SalesApprovalRequestApiV1.StatusPending;
        }

        if (quote.ExpiryApprovals.Any(approval => approval.Revision == item.SubjectRevision))
        {
            return SalesApprovalRequestApiV1.StatusApproved;
        }

        return quote.State == SalesQuoteState.Open && quote.Revision == item.SubjectRevision
            ? SalesApprovalRequestApiV1.StatusPending
            : SalesApprovalRequestApiV1.StatusStale;
    }

    internal static SalesApprovalRequestView ApprovalRequestView(SalesApprovalRequest item, SalesQuote? quote) => new(
        SalesApprovalRequestApiV1.KindText(item.Kind),
        item.SubjectId,
        item.RequestNo,
        SalesDraftApiV1.RevisionText(item.SubjectRevision),
        ApprovalRequestStatus(item, quote),
        item.Version.ToString(CultureInfo.InvariantCulture),
        item.LineId,
        item.Ordered,
        item.Quantity,
        item.RequestedTotal,
        item.SourceInstructionId,
        item.SourceShipmentId,
        item.Note,
        item.RequestedBy,
        item.RequestedAtUtc,
        item.Decision?.DecidedBy,
        item.Decision?.DecidedAtUtc,
        item.Decision?.Reason);

    private ISalesApprovalRequestStore ApprovalRequests() =>
        store as ISalesApprovalRequestStore ?? throw new SalesDraftServiceException(503, "service_unavailable");

    private async Task<T> ApprovalStoreAsync<T>(Scope scope, string? permission, Func<Task<T>> read, CancellationToken cancellationToken)
    {
        try
        {
            return await GuardedAsync(scope, permission, read, cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }
    }

    private Task<SalesApprovalRequestReplay?> FindApprovalReplayAsync(Scope scope, string? permission, SalesDraftWriteEnvelope envelope,
        string requestHash, string commandName, CancellationToken cancellationToken) =>
        ApprovalStoreAsync(scope, permission,
            () => ApprovalRequests().FindApprovalRequestReplayAsync(envelope, requestHash, commandName, cancellationToken), cancellationToken);

    private async Task<SalesApprovalRequestCommandResult> ApprovalReplayedAsync(Scope scope, string? permission,
        SalesApprovalRequestReplay replay, CancellationToken cancellationToken)
    {
        if (replay.Refusal is { } refusal)
        {
            throw new SalesDraftServiceException(refusal.Status, refusal.Code, refusal.Errors, isReplay: true,
                originalCorrelationId: replay.OriginalCorrelationId);
        }

        return new SalesApprovalRequestCommandResult(await ViewOfAsync(scope, permission, replay.Request!, cancellationToken), true,
            replay.OriginalCorrelationId);
    }

    private async Task<SalesApprovalRequestCommandResult> ApprovalResultAsync(Scope scope, string? permission,
        SalesApprovalRequestStoreResult stored, CancellationToken cancellationToken) =>
        new(await ViewOfAsync(scope, permission, stored.Request, cancellationToken), stored.Outcome == SalesDraftWriteOutcome.Replayed,
            stored.OriginalCorrelationId);

    /// <summary>A quote request's view reads its quote for the derived status; a quote that cannot be read leaves it Pending.</summary>
    private async Task<SalesApprovalRequestView> ViewOfAsync(Scope scope, string? permission, SalesApprovalRequest item,
        CancellationToken cancellationToken)
    {
        SalesQuote? quote = null;
        if (item.Kind == SalesApprovalRequestKind.QuoteExpiredConversion && quotes is { } quoteStore)
        {
            quote = await ApprovalStoreAsync(scope, permission, () => quoteStore.FindQuoteAsync(item.SubjectId, cancellationToken),
                cancellationToken);
        }

        return ApprovalRequestView(item, quote);
    }

    /// <summary>ADR-011 §7: the refusal is recorded under the key and then answered; a lost record race answers the winner.</summary>
    private async Task<SalesApprovalRequestCommandResult> ApprovalTerminalAsync(Scope scope, SalesDraftWriteEnvelope envelope,
        string requestHash, string commandName, Guid subjectId, SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken)
    {
        var permission = envelope.PermissionCode;
        await RequireStillAsync(scope, permission, cancellationToken);
        try
        {
            await ApprovalRequests().RecordApprovalRequestRefusalAsync(envelope, requestHash, commandName, subjectId, refusal, cancellationToken);
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            var won = await FindApprovalReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
            if (won is not null)
            {
                return await ApprovalReplayedAsync(scope, permission, won, cancellationToken);
            }
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        throw new SalesDraftServiceException(refusal.Status, refusal.Code, refusal.Errors);
    }

    /// <summary>A recorded (terminal) 409: the over-delivery request no longer stands as asked; names the field that moved.</summary>
    private sealed class ApprovalRequestStale(string field) : Exception
    {
        public string Field { get; } = field;
    }
}
