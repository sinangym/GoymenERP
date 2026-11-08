using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// SAT-D16 / SAT-D17 (EK3) / SAT-D168 (EK24) / SAT-D214–D216 (EK37): closing one order line with a shortfall, under
/// <c>sales.orders.lines.close</c> (Satış Uzmanı, Satış Müdürü, Genel Müdür, Admin — a grant decision and a human gate; no role
/// name here).
/// <list type="bullet">
/// <item><b>The band is the realization axis' own answer:</b> the line's shipped total from the AT-024 delivery owner (Logistics,
/// through <see cref="ISalesOrderDeliveryReadPortV1"/>), the measured quantity of the status read (<see cref="Basis"/>), the Settings
/// tolerance of the order's business date (<see cref="ToleranceAsync"/>) and <see cref="SalesRealizationCalculator"/> judging the
/// line as a CLOSED line (SAT-D16: the shortfall is measured only at the closure) — nothing is re-implemented here. Within and
/// warn bands close directly (SAT-D17, SAT-D214: no extra approval); the approval band closes on this user action (SAT-D17); the
/// block band is 422 (<see cref="SalesDraftProblemCodesV1.LineClosureBlocked"/>), never closed this way.</item>
/// <item><b>Nothing shipped or no shortfall</b> is not a closure (the recorded 409
/// <see cref="SalesDraftProblemCodesV1.LineClosureNotApplicable"/>): the remainder is counted as cancelled, never moved.</item>
/// <item><b>SAT-D213-like identity:</b> the command names the shipped total the user saw; when the owner's total differs, nothing
/// is closed (recorded 409).</item>
/// <item><b>SAT-D216 (fail-closed):</b> the closer must be none of the actors the delivery owner names for the line's shipments.
/// An owner that does not name them (the Logistics owner on main does not — SAT-D209) is the recorded 409
/// <see cref="SalesDraftProblemCodesV1.LineClosureShipperUnverified"/>.</item>
/// <item><b>Ordering</b> (the over-delivery approval's): fence → version shape → the key's replay → the store reads the draft →
/// fence → the domain's own preconditions → the delivery owner (fence) → the Settings tolerance (fence) → the judgement → last
/// fence → ONE save (closure row + header version + attempt + audit). An unreadable owner or policy is 503 (not recorded).</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService
{
    public async Task<SalesOrderDraftResponse> CloseLineAsync(Guid draftId, SalesDraftLineClosureRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();

        var permission = bindings.LineClosePermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (!long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion) || expectedVersion < 1)
        {
            throw new SalesDraftServiceException(400, "validation_failed", [new("expectedVersion", "validation_failed")]);
        }

        if (!decimal.TryParse(request.Shipped, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seenShipped))
        {
            throw new SalesDraftServiceException(400, "validation_failed", [new("shipped", "validation_failed")]);
        }

        const string commandName = SalesDraftCommands.CloseLineV1;
        var requestHash = SalesDraftRequestHash.Compute(commandName, draftId, request);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            // The same key, content and actor: the stored result again — no owner is asked, nothing is closed twice.
            return await ReplayedAsync(scope, replay, cancellationToken);
        }

        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var audit = new SalesDraftStateAudit("sales.order-draft.line-closed.v1", "CloseLine",
            "Satır eksik teslimle kapatıldı; kalan miktar iptal edilen kalan sayıldı (SAT-D17; geçmiş silinmez)");

        SalesDraftStateResult? result;
        try
        {
            result = await store.CloseLineAsync(envelope, requestHash, commandName, draftId,
                (draft, input, now) => draft.CloseLine(expectedVersion, scope.ActorId, input, now), audit,
                async (draft, writeCancellationToken) =>
                {
                    scope = await RequireStillAsync(scope, permission, writeCancellationToken);
                    // The domain's own preconditions first: a stale, terminal or never-approved order, an unknown or already closed
                    // line asks no owner.
                    var line = draft.RequireLineClosable(expectedVersion, request.LineId);
                    var input = await LineClosureInputAsync(scope, permission, draft, line, seenShipped, writeCancellationToken);
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
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.LineClosureSelfClosure)
        {
            throw await TerminalAsync(scope, permission, envelope, requestHash, new SalesDraftTerminalRefusal(409,
                SalesDraftProblemCodesV1.LineClosureSelfClosure, [new("actor", exception.Code)]), cancellationToken, commandName, draftId);
        }
        catch (LineClosureRefused refused)
        {
            throw await TerminalAsync(scope, permission, envelope, requestHash, new SalesDraftTerminalRefusal(409, refused.Code,
                [new(refused.Field, refused.Code)]), cancellationToken, commandName, draftId);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.LineClosureLineUnknown)
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
    /// The closure's input read afresh at the command: the line's shipped total from the delivery owner, the measured quantity of
    /// the status read, the Settings tolerance and the calculator judging the line as closed, plus the shippers the owner names
    /// for the line (SAT-D216). Unreadable owners are 503, never a guessed answer.
    /// </summary>
    private async Task<LineClosureInput> LineClosureInputAsync(Scope scope, string? permission, SalesOrderDraft draft,
        SalesOrderDraftLine line, decimal seenShipped, CancellationToken cancellationToken)
    {
        if (Basis(draft) is not { } basis)
        {
            // A revision awaits re-approval and the last approved quantity cannot be taken exactly: nothing is judged.
            throw new LineClosureRefused(SalesDraftProblemCodesV1.LineClosureNotApplicable, "lineId");
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
            // The closure covers the total the user saw; another total is never closed in its name.
            throw new LineClosureRefused(SalesDraftProblemCodesV1.LineClosureShippedChanged, "shipped");
        }

        var (basisQuantity, basisText) = basis.Lines[line.Id];
        if (shipped.IsZero || !basisQuantity.IsGreaterThan(shipped))
        {
            // Nothing shipped (the line is passivated or cancelled, not closed) or no shortfall: nothing to close.
            throw new LineClosureRefused(SalesDraftProblemCodesV1.LineClosureNotApplicable, "lineId");
        }

        var policy = await ToleranceAsync(scope, permission, draft, cancellationToken)
            ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.LineClosurePolicyUnavailable);
        LineRealization realization;
        try
        {
            realization = SalesRealizationCalculator.Line(basisQuantity, shipped, lineClosed: true, policy.Tolerance);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException or SalesRuleException)
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.DeliveryProviderUnavailable);
        }

        if (realization.Band is not { } band || realization.Direction != DeliveryDeviationDirection.Under)
        {
            throw new LineClosureRefused(SalesDraftProblemCodesV1.LineClosureNotApplicable, "lineId");
        }

        if (band == DeliveryToleranceBand.Block)
        {
            // SAT-D17: the block band never closes this way (the remainder ships, or the force-close applies). Not recorded.
            throw new SalesDraftServiceException(422, SalesDraftProblemCodesV1.LineClosureBlocked,
                [new("lineId", SalesDraftProblemCodesV1.LineClosureBlocked)]);
        }

        var shippers = Shippers(answer!, line.Id)
            ?? throw new LineClosureRefused(SalesDraftProblemCodesV1.LineClosureShipperUnverified, "shippedBy");
        return new LineClosureInput(line.Id, basis.RevisionNo, basisText, shipped.Value, band, shippers);
    }

    /// <summary>A recorded (terminal) refusal of the line closure: its stable code and the field it names.</summary>
    private sealed class LineClosureRefused(string code, string field) : Exception
    {
        public string Code { get; } = code;
        public string Field { get; } = field;
    }
}
