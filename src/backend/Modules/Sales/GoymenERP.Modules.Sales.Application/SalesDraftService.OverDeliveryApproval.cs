using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// SAT-D30 (EK3) / SAT-D168 (EK24) / SAT-D213 + SAT-D216 (EK37): the approval of one order line's over-delivery in the tolerance
/// APPROVAL band, under <c>sales.orders.over-delivery.approve</c> (Satış Uzmanı, Satış Müdürü, Genel Müdür, Admin — a grant
/// decision and a human gate; no role name here).
/// <list type="bullet">
/// <item><b>The band is the realization axis' own answer:</b> the line's shipped total from the AT-024 delivery owner (Logistics,
/// through <see cref="ISalesOrderDeliveryReadPortV1"/>), the measured quantity of the status read (<see cref="Basis"/>), the Settings
/// tolerance of the order's business date (<see cref="ToleranceAsync"/>) and <see cref="SalesRealizationCalculator"/> — nothing is
/// re-implemented here. Only <see cref="LineRealizationState.AwaitingApproval"/> is approvable; the block band is 422
/// (<see cref="SalesDraftProblemCodesV1.OverDeliveryBlocked"/>), anything else the key's recorded 409.</item>
/// <item><b>SAT-D213:</b> the approval records the shipped total it covers; the command names the total the user saw and refuses
/// when the owner's total differs (recorded 409). A later, larger total awaits a new approval.</item>
/// <item><b>SAT-D216 (fail-closed):</b> the approver must be none of the actors the delivery owner names for the line's shipments.
/// An owner that does not name them (the Logistics owner on main does not — SAT-D209) is the recorded 409
/// <see cref="SalesDraftProblemCodesV1.OverDeliveryShipperUnverified"/>: nothing is approved by someone who may be the shipper.</item>
/// <item><b>Ordering</b> (the force-close's): fence → version shape → the key's replay → the store reads the draft → fence → the
/// domain's own preconditions → the delivery owner (fence) → the Settings tolerance (fence) → the judgement → last fence → ONE
/// save (approval row + header version + attempt + audit). An unreadable owner or policy is 503 (not recorded).</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService
{
    public async Task<SalesOrderDraftResponse> ApproveOverDeliveryAsync(Guid draftId, SalesDraftOverDeliveryApprovalRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();

        var permission = bindings.OverDeliveryApprovePermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (!long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion) || expectedVersion < 1)
        {
            throw new SalesDraftServiceException(400, "validation_failed", [new("expectedVersion", "validation_failed")]);
        }

        if (!decimal.TryParse(request.Shipped, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seenShipped))
        {
            throw new SalesDraftServiceException(400, "validation_failed", [new("shipped", "validation_failed")]);
        }

        const string commandName = SalesDraftCommands.ApproveOverDeliveryV1;
        var requestHash = SalesDraftRequestHash.Compute(commandName, draftId, request);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            // The same key, content and actor: the stored result again — no owner is asked, nothing is approved twice.
            return await ReplayedAsync(scope, replay, cancellationToken);
        }

        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var audit = new SalesDraftStateAudit("sales.order-draft.over-delivery-approved.v1", "ApproveOverDelivery",
            "Onay bandındaki fazla teslim, onay anındaki sevk toplamıyla onaylandı (SAT-D30, SAT-D213; geçmiş silinmez)");

        SalesDraftStateResult? result;
        try
        {
            result = await store.ApproveOverDeliveryAsync(envelope, requestHash, commandName, draftId,
                (draft, input, now) => draft.ApproveOverDelivery(expectedVersion, scope.ActorId, input, now), audit,
                async (draft, writeCancellationToken) =>
                {
                    scope = await RequireStillAsync(scope, permission, writeCancellationToken);
                    // The domain's own preconditions first: a stale or terminal order, or an unknown line, asks no owner.
                    var line = draft.RequireOverDeliveryApprovable(expectedVersion, request.LineId);
                    var input = await OverDeliveryInputAsync(scope, permission, draft, line, seenShipped, writeCancellationToken);
                    // The last fence that can still prevent the write; none after the commit (ORG #321 P3-1).
                    scope = await RequireStillAsync(scope, permission, writeCancellationToken);
                    return input;
                }, cancellationToken);
        }
        catch (SalesVersionConflictException exception)
        {
            throw await TerminalAsync(scope, permission, envelope, requestHash, new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.VersionConflict,
                [new("expectedVersion", exception.Code)]), cancellationToken, commandName, draftId);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.StateConflict)
        {
            throw await TerminalAsync(scope, permission, envelope, requestHash, new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.StateConflict,
                [new("state", exception.Code)]), cancellationToken, commandName, draftId);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.OverDeliverySelfApproval)
        {
            throw await TerminalAsync(scope, permission, envelope, requestHash, new SalesDraftTerminalRefusal(409,
                SalesDraftProblemCodesV1.OverDeliverySelfApproval, [new("actor", exception.Code)]), cancellationToken, commandName, draftId);
        }
        catch (OverDeliveryRefused refused)
        {
            throw await TerminalAsync(scope, permission, envelope, requestHash, new SalesDraftTerminalRefusal(409, refused.Code,
                [new(refused.Field, refused.Code)]), cancellationToken, commandName, draftId);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.OverDeliveryLineUnknown)
        {
            // Not recorded: the same key may name the order's line next time.
            throw new SalesDraftServiceException(422, "validation_failed", [new("lineId", exception.Code)]);
        }
        catch (SalesRuleException exception)
        {
            throw new SalesDraftServiceException(422, "validation_failed", [new("draft", exception.Code)]);
        }
        catch (ArgumentException)
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            var won = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
            if (won is null)
            {
                throw Store(exception);
            }

            return await ReplayedAsync(scope, won, cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        if (result is null)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        return Response(scope, result.Draft, result.Outcome == SalesDraftWriteOutcome.Replayed, result.OriginalCorrelationId,
            await DiscloseAsync(scope, cancellationToken));
    }

    /// <summary>
    /// The approval's input read afresh at the command: the line's realization exactly as the status read computes it (basis,
    /// delivery owner, Settings tolerance, calculator, recorded approvals), refused unless it is AwaitingApproval, plus the shippers
    /// the owner names for the line (SAT-D216). Unreadable owners are 503, never a guessed answer.
    /// </summary>
    private async Task<OverDeliveryApprovalInput> OverDeliveryInputAsync(Scope scope, string? permission, SalesOrderDraft draft,
        SalesOrderDraftLine line, decimal seenShipped, CancellationToken cancellationToken)
    {
        if (Basis(draft) is not { } basis)
        {
            // A revision awaits re-approval and the last approved quantity cannot be taken exactly: nothing is judged (the axis
            // shows PendingReapproval).
            throw new OverDeliveryRefused(SalesDraftProblemCodesV1.OverDeliveryNotAwaitingApproval, "lineId");
        }

        if (deliveries is not { } owner)
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.DeliveryProviderUnavailable);
        }

        SalesOrderDeliveryReadAnswerV1? answer;
        try
        {
            answer = await GuardedAsync(scope, permission, () => owner.ReadOrderDeliveriesAsync(draft.Id, cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.DeliveryProviderUnavailable);
        }

        var recorded = RecordedQuantities(scope, draft, answer)
            ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.DeliveryProviderUnavailable);
        var shipped = recorded[line.Id].Shipped;
        if (shipped.Value != seenShipped)
        {
            // SAT-D213: the approval covers the total the user saw; another total is never approved in its name.
            throw new OverDeliveryRefused(SalesDraftProblemCodesV1.OverDeliveryShippedChanged, "shipped");
        }

        // RequireOverDeliveryApprovable already refused a cancelled or force-closed order (409 state conflict), so the line is
        // open here: the calculator judges it as an open line.
        if (!SalesRealizationCalculator.NeedsTolerance(shipped, lineClosed: false))
        {
            throw new OverDeliveryRefused(SalesDraftProblemCodesV1.OverDeliveryNotAwaitingApproval, "lineId");
        }

        var policy = await ToleranceAsync(scope, permission, draft, cancellationToken)
            ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.OverDeliveryPolicyUnavailable);
        var (basisQuantity, basisText) = basis.Lines[line.Id];
        LineRealization realization;
        try
        {
            realization = SalesRealizationCalculator.Line(basisQuantity, shipped, lineClosed: false, policy.Tolerance,
                draft.IsOverDeliveryApproved(line.Id, basisQuantity, shipped));
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException or SalesRuleException)
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.DeliveryProviderUnavailable);
        }

        if (realization.State == LineRealizationState.OutsideTolerance)
        {
            // SAT-D15: the block band is never approvable. Not recorded (the axis shows it as outside the tolerance).
            throw new SalesDraftServiceException(422, SalesDraftProblemCodesV1.OverDeliveryBlocked,
                [new("lineId", SalesDraftProblemCodesV1.OverDeliveryBlocked)]);
        }

        if (realization.State != LineRealizationState.AwaitingApproval)
        {
            throw new OverDeliveryRefused(SalesDraftProblemCodesV1.OverDeliveryNotAwaitingApproval, "lineId");
        }

        var shippers = Shippers(answer!, line.Id)
            ?? throw new OverDeliveryRefused(SalesDraftProblemCodesV1.OverDeliveryShipperUnverified, "shippedBy");
        return new OverDeliveryApprovalInput(line.Id, basis.RevisionNo, basisText, shipped.Value, shippers);
    }

    /// <summary>
    /// SAT-D216: the actors the delivery owner names for the line's definite shipments (every entry with a shipped total), or null
    /// when any such entry names none (the owner does not tell), names a blank actor or one longer than an actor id can be
    /// (<see cref="SalesOrderDraft.MaximumActorLength"/>) — fail-closed (the recorded 409), never "nobody shipped" and never a 400.
    /// The comparison with the approver is ordinal on the trimmed id: the owner must name shippers in the Sales actor id format.
    /// </summary>
    private static IReadOnlyCollection<string>? Shippers(SalesOrderDeliveryReadAnswerV1 answer, Guid lineId)
    {
        var shippers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in answer.Lines.Where(entry => entry.OrderLineId == lineId && entry.ShippedQuantity > 0m))
        {
            if (entry.ShippedBy is not { Count: > 0 } named
                || named.Any(shipper => string.IsNullOrWhiteSpace(shipper) || shipper.Trim().Length > SalesOrderDraft.MaximumActorLength))
            {
                return null;
            }

            shippers.UnionWith(named.Select(shipper => shipper.Trim()));
        }

        return shippers.Count == 0 ? null : shippers;
    }

    /// <summary>A recorded (terminal) refusal of the over-delivery approval: its stable code and the field it names.</summary>
    private sealed class OverDeliveryRefused(string code, string field) : Exception
    {
        public string Code { get; } = code;
        public string Field { get; } = field;
    }
}
