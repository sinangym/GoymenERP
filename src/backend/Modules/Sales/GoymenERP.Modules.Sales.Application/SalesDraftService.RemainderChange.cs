using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Quantities;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// Partial delivery (user decision 2026-09-30, binding): "on a partially delivered order line the delivered part is kept; the
/// remaining (undelivered) quantity can be cancelled or its quantity revised, and this requires approval." AT-024 ("Kısmi
/// teslimat sipariş kalanını doğru tutar"), DEC-SAL-002 (a quantity change needs approval), DEC-SAL-005 (the effect is read
/// from the owners, never assumed).
/// <list type="bullet">
/// <item><b>Two people, two names:</b> the request under the quantity-write name (<c>sales.orders.quantity.write</c>), the
/// approval / rejection under the approve name (<c>sales.orders.approve</c>) with D2's persona fence (a Satış Destek actor
/// does not decide a change on their own draft or their own request). No new permission name is introduced.</item>
/// <item><b>The floor</b> is the delivery owner's RECORDED totals of the line — Logistics' loading read
/// (<see cref="ISalesOrderLoadingReadPortV1"/>: loaded ≥ shipped ≥ delivered in the order unit) — read afresh at the request
/// AND at the approval: a line is never reduced below what is delivered or already loaded. An owner that is not composed or
/// cannot answer is 503 (nothing changes on an unread floor).</item>
/// <item><b>Allocations (fail-closed):</b> a reduction that would leave an ACTIVE Inventory allocation reserving more than the
/// line still needs (kg: active kg &gt; new quantity − shipped; a count line: any active allocation) is refused, recorded
/// (409 <c>sales_remainder_blocked</c>): Sales releases nothing — the user resizes or releases it in Inventory.</item>
/// <item><b>Ordering</b> (the quantity revision's): fence → version shape → the key's replay (§4/§7; asks no owner) → the
/// store reads the draft → fence → the domain's own preconditions (a stale, wrong-state or duplicate command asks no owner) →
/// each owner (fence after each) → blockers → last fence → ONE save. Nothing is measured after the commit (S03). A replay
/// (same key, content, actor) answers the stored result again: no second request, decision, quantity or audit.</item>
/// <item><b>No stock or money effect</b>: only the line's ordered quantity (and remainder state) moves; the Sales line port then
/// answers the capped quantity and Logistics refuses to load beyond it. Residual race (declared): no transaction spans Sales and
/// Logistics — a load committed between the floor read and the save is not seen by this save; Logistics' own cap then refuses
/// any further load above the approved quantity.</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService
{
    public async Task<SalesOrderDraftResponse> RequestRemainderChangeAsync(Guid draftId, SalesDraftRemainderChangeRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();

        var permission = bindings.QuantityPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        var expectedVersion = ExpectedVersion(request.ExpectedVersion);
        var kind = request.Kind switch
        {
            SalesRemainderChangeNamesV1.Cancel when request.Quantity is null => RemainderChangeKind.Cancel,
            SalesRemainderChangeNamesV1.Revise when request.Quantity is not null => RemainderChangeKind.Revise,
            _ => throw new SalesDraftServiceException(400, "validation_failed", [new("kind", "validation_failed")]),
        };

        const string commandName = SalesDraftCommands.RequestRemainderChangeV1;
        var requestHash = SalesDraftRequestHash.Compute(commandName, draftId, request);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            // The same key, content and actor: the stored request again — no owner is asked, nothing is requested twice.
            return await ReplayedAsync(scope, replay, cancellationToken);
        }

        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        decimal? quantity = request.Quantity is null ? null : RemainderQuantity(request.Quantity);
        var audit = new SalesDraftStateAudit("sales.order-draft.remainder-change-requested.v1", "RequestRemainderChange",
            "Kısmi teslim edilen satırın kalanı için iptal/revizyon onaya sunuldu (teslim edilen kısım korunur)");
        RemainderFloor? floor = null;

        SalesDraftStateResult? result;
        try
        {
            result = await store.ChangeRemainderAsync(envelope, requestHash, commandName, draftId,
                (draft, now) => draft.RequestRemainderChange(expectedVersion, request.LineId, kind, request.Quantity, quantity, request.Reason,
                    floor!, scope.ActorId, now), audit,
                async (draft, writeCancellationToken) =>
                {
                    scope = await RequireStillAsync(scope, permission, writeCancellationToken);
                    // The domain's own preconditions first: a stale, unapproved, cancelled or duplicate request asks no owner.
                    draft.RequireRemainderChangeable(expectedVersion, request.LineId, request.Reason);
                    var line = draft.Lines.Single(candidate => candidate.Id == request.LineId);
                    floor = await ReadRemainderFloorAsync(scope, permission, draft, line, writeCancellationToken);
                    await RequireAllocationsFitAsync(scope, permission, draft, line, kind == RemainderChangeKind.Cancel ? floor.Loaded : quantity!.Value,
                        floor, writeCancellationToken);
                    // The last fence that can still prevent the write; none after the commit (ORG #321 P3-1).
                    scope = await RequireStillAsync(scope, permission, writeCancellationToken);
                }, cancellationToken);
        }
        catch (Exception exception) when (exception is not SalesDraftServiceException && IsRemainderRefusal(exception))
        {
            throw await RemainderRefusalAsync(scope, envelope, requestHash, commandName, draftId, exception, cancellationToken);
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            var won = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
            return won is null ? throw Store(exception) : await ReplayedAsync(scope, won, cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        return result is null
            ? throw new SalesDraftServiceException(404, "not_found")
            : Response(scope, result.Draft, result.Outcome == SalesDraftWriteOutcome.Replayed, result.OriginalCorrelationId,
                await DiscloseAsync(scope, cancellationToken));
    }

    public Task<SalesOrderDraftResponse> ApproveRemainderChangeAsync(Guid draftId, int changeNo, SalesDraftRemainderDecisionRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        DecideRemainderChangeAsync(draftId, changeNo, request, metadata, approving: true, cancellationToken);

    public Task<SalesOrderDraftResponse> RejectRemainderChangeAsync(Guid draftId, int changeNo, SalesDraftRemainderDecisionRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        DecideRemainderChangeAsync(draftId, changeNo, request, metadata, approving: false, cancellationToken);

    /// <summary>
    /// The decision on THE pending change under the approve name. An approval re-reads the floor (and, for a reduction, the
    /// allocations) and applies the change on it; a rejection asks no owner. D2's persona fence: a Satış Destek actor does not
    /// decide on their own draft or their own request (403, never recorded).
    /// </summary>
    private async Task<SalesOrderDraftResponse> DecideRemainderChangeAsync(Guid draftId, int changeNo, SalesDraftRemainderDecisionRequest request,
        SalesCommandMetadata metadata, bool approving, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();

        var permission = bindings.ApprovePermission;
        var scope = await RequireAsync(permission, cancellationToken);
        var expectedVersion = ExpectedVersion(request.ExpectedVersion);
        if (approving && request.Reason is not null)
        {
            throw new SalesDraftServiceException(400, "validation_failed", [new("reason", "validation_failed")]);
        }

        var commandName = approving ? SalesDraftCommands.ApproveRemainderChangeV1 : SalesDraftCommands.RejectRemainderChangeV1;
        var requestHash = SalesDraftRequestHash.Compute(commandName, draftId, changeNo, request);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            // The same key, content and actor: the stored decision again — nothing is applied twice.
            return await ReplayedAsync(scope, replay, cancellationToken);
        }

        if (draftId == Guid.Empty || changeNo < 1)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var audit = approving
            ? new SalesDraftStateAudit("sales.order-draft.remainder-change-approved.v1", "ApproveRemainderChange",
                "Satır kalanı değişikliği onaylandı; teslim edilen/yüklenen kısım korundu, satır miktarı güncellendi")
            : new SalesDraftStateAudit("sales.order-draft.remainder-change-rejected.v1", "RejectRemainderChange",
                "Satır kalanı değişikliği reddedildi; satır değişmedi");
        RemainderFloor? floor = null;

        SalesDraftStateResult? result;
        try
        {
            result = await store.ChangeRemainderAsync(envelope, requestHash, commandName, draftId,
                (draft, now) => approving
                    ? draft.ApproveRemainderChange(expectedVersion, changeNo, floor!, scope.ActorId, now)
                    : draft.RejectRemainderChange(expectedVersion, changeNo, scope.ActorId, request.Reason, now),
                audit,
                async (draft, writeCancellationToken) =>
                {
                    scope = await RequireStillAsync(scope, permission, writeCancellationToken);
                    var pending = draft.RequireRemainderDecidable(expectedVersion, changeNo, approving);
                    if (scope.IsSalesSupport && (IsOwn(scope, draft)
                        || string.Equals(pending.Request.RequestedBy, scope.ActorId, StringComparison.Ordinal)))
                    {
                        throw new SalesDraftServiceException(403, "forbidden");
                    }

                    if (approving)
                    {
                        var line = draft.Lines.Single(candidate => candidate.Id == pending.Request.LineId);
                        floor = await ReadRemainderFloorAsync(scope, permission, draft, line, writeCancellationToken);
                        var target = pending.Request.Kind == RemainderChangeKind.Cancel ? floor.Loaded : pending.Request.RequestedQuantity!.Value;
                        await RequireAllocationsFitAsync(scope, permission, draft, line, target, floor, writeCancellationToken);
                    }

                    scope = await RequireStillAsync(scope, permission, writeCancellationToken);
                }, cancellationToken);
        }
        catch (Exception exception) when (exception is not SalesDraftServiceException && IsRemainderRefusal(exception))
        {
            throw await RemainderRefusalAsync(scope, envelope, requestHash, commandName, draftId, exception, cancellationToken);
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            var won = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
            return won is null ? throw Store(exception) : await ReplayedAsync(scope, won, cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        return result is null
            ? throw new SalesDraftServiceException(404, "not_found")
            : Response(scope, result.Draft, result.Outcome == SalesDraftWriteOutcome.Replayed, result.OriginalCorrelationId,
                await DiscloseAsync(scope, cancellationToken));
    }

    /// <summary>
    /// The lines' remainder facts and every recorded change under the draft read. The floor is read afresh from the delivery
    /// owner for the screen; an owner that cannot answer leaves the numbers null ("Unreadable"), never 0 — the commands read it
    /// again and refuse on an unread floor.
    /// </summary>
    public async Task<SalesOrderDraftRemainderChangesResponse> GetRemainderChangesAsync(Guid draftId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        SalesOrderDraft? draft;
        try
        {
            draft = await GuardedAsync(scope, bindings.ReadPermission, () => store.FindDraftAsync(draftId, cancellationToken), cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        if (draft is null)
        {
            // Another company's draft is null under the tenant filter: 404, and its owner is never asked.
            throw new SalesDraftServiceException(404, "not_found");
        }

        var loading = await ReadLoadingAsync(scope, bindings.ReadPermission, draft, cancellationToken);
        var lines = draft.Lines.OrderBy(line => line.LineNo).Select(line =>
        {
            var recorded = loading is null ? null : loading.GetValueOrDefault(line.Id) ?? new LineLoading(0, 0m, 0m, 0m, 0m);
            return new SalesDraftRemainderLineV1(line.Id, line.LineNo, line.OrderUnit.UnitId, line.QuantityText, RemainderName(line.Remainder),
                recorded is null ? null : Text(recorded.Loaded, line.Quantity.Unit),
                recorded is null ? null : Text(recorded.Shipped, line.Quantity.Unit),
                recorded is null ? null : Text(recorded.Delivered, line.Quantity.Unit),
                recorded is null ? null : Text(recorded.Loaded, line.Quantity.Unit));
        }).ToArray();
        var items = draft.RemainderChanges.Select(change => RemainderChangeView(draft, change)).ToArray();
        return new SalesOrderDraftRemainderChangesResponse(draft.Version.ToString(CultureInfo.InvariantCulture),
            loading is null ? SalesRemainderChangeNamesV1.Unreadable : SalesRemainderChangeNamesV1.Readable, lines, items);
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------

    private sealed class RemainderBlocked(string source) : Exception
    {
        public string BlockingSource { get; } = source;
    }

    private static long ExpectedVersion(string text) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var version) && version >= 1
            ? version
            : throw new SalesDraftServiceException(400, "validation_failed", [new("expectedVersion", "validation_failed")]);

    /// <summary>The revised quantity text as the domain's exact value (the create's Ö2 lexical rule), refused on <c>quantity</c>.</summary>
    private static decimal RemainderQuantity(string text) => Decimal(text);

    /// <summary>
    /// The delivery owner's recorded totals of <paramref name="line"/> in its order unit (Logistics' loading read; absent =
    /// nothing recorded = 0), released only under the command's name. Not composed, failing or malformed: 503, never an
    /// assumed zero.
    /// </summary>
    private async Task<RemainderFloor> ReadRemainderFloorAsync(Scope scope, string? permission, SalesOrderDraft draft, SalesOrderDraftLine line,
        CancellationToken cancellationToken)
    {
        var loading = await ReadLoadingAsync(scope, permission, draft, cancellationToken)
            ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.RemainderFloorUnavailable);
        var recorded = loading.GetValueOrDefault(line.Id) ?? new LineLoading(0, 0m, 0m, 0m, 0m);
        return new RemainderFloor(recorded.Loaded, recorded.Shipped, recorded.Delivered);
    }

    /// <summary>
    /// A REDUCTION must not leave an active allocation reserving more than the line still needs: for a kilogram line the
    /// active kilograms may not exceed (new quantity − shipped); for a count line any active allocation blocks (kilograms and
    /// pieces are never compared). An increase asks no allocation owner. An owner that cannot answer is 503.
    /// </summary>
    private async Task RequireAllocationsFitAsync(Scope scope, string? permission, SalesOrderDraft draft, SalesOrderDraftLine line,
        decimal newQuantity, RemainderFloor floor, CancellationToken cancellationToken)
    {
        if (newQuantity >= line.Quantity.Value)
        {
            return;
        }

        var allocationsByLine = await ReadAllocationsAsync(scope, permission, draft, cancellationToken)
            ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.RemainderFloorUnavailable);
        var active = allocationsByLine.GetValueOrDefault(line.Id);
        if (active is null || active.Count == 0)
        {
            return;
        }

        var stillNeeded = Math.Max(0m, newQuantity - floor.Shipped);
        if (line.Quantity.Unit != QuantityUnit.Kilogram || active.RemainingKg > stillNeeded)
        {
            throw new RemainderBlocked(SalesCancellationImpactSourcesV1.ActiveAllocations);
        }
    }

    private static bool IsRemainderRefusal(Exception exception) =>
        exception is SalesVersionConflictException or SalesRuleException or RemainderBlocked or ArgumentException;

    /// <summary>
    /// The answer to a refusal: a stale version, a wrong state and an allocation block are the key's TERMINAL 409s (recorded,
    /// replayed); a rule is a 422 on its field (not recorded: the floor may move); a malformed input is 400.
    /// </summary>
    private async Task<SalesDraftServiceException> RemainderRefusalAsync(Scope scope, SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, Guid draftId, Exception exception, CancellationToken cancellationToken) => exception switch
    {
        SalesVersionConflictException conflict => await TerminalAsync(scope, envelope, requestHash,
            new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.VersionConflict, [new("expectedVersion", conflict.Code)]),
            cancellationToken, commandName, draftId),
        SalesRuleException { Code: SalesRuleCodes.StateConflict } rule => await TerminalAsync(scope, envelope, requestHash,
            new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.StateConflict, [new("remainder", rule.Code)]),
            cancellationToken, commandName, draftId),
        RemainderBlocked blocked => await TerminalAsync(scope, envelope, requestHash,
            new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.RemainderBlocked,
                [new SalesDraftFieldError(blocked.BlockingSource, SalesCancellationImpactStatusesV1.Present)]),
            cancellationToken, commandName, draftId),
        SalesRuleException { Code: SalesRuleCodes.ApprovalReasonTooLong } rule =>
            new SalesDraftServiceException(422, SalesDraftProblemCodesV1.ApprovalReasonTooLong, [new("reason", rule.Code)]),
        SalesRuleException rule => new SalesDraftServiceException(422, "validation_failed", [new(RemainderField(rule.Code), rule.Code)]),
        _ => new SalesDraftServiceException(400, "validation_failed"),
    };

    private static string RemainderField(string code) => code switch
    {
        SalesRuleCodes.RevisionReasonRequired or SalesRuleCodes.RevisionReasonTooLong => "reason",
        SalesRuleCodes.RemainderLineUnknown or SalesRuleCodes.RemainderNotStarted or SalesRuleCodes.RemainderNone => "lineId",
        _ => "quantity",
    };

    private static string RemainderName(LineRemainderState state) => state switch
    {
        LineRemainderState.Revised => SalesRemainderChangeNamesV1.Revised,
        LineRemainderState.Cancelled => SalesRemainderChangeNamesV1.Cancelled,
        _ => SalesRemainderChangeNamesV1.Open,
    };

    private static SalesDraftRemainderChangeV1 RemainderChangeView(SalesOrderDraft draft, RemainderChangeFacts change)
    {
        var request = change.Request;
        var decision = change.Decision;
        var unit = draft.Lines.Single(line => line.Id == request.LineId).Quantity.Unit;
        return new SalesDraftRemainderChangeV1(request.ChangeNo, SalesDraftApiV1.RevisionText(request.Revision), request.LineId, request.LineNo,
            request.Kind == RemainderChangeKind.Cancel ? SalesRemainderChangeNamesV1.Cancel : SalesRemainderChangeNamesV1.Revise,
            change.State switch
            {
                RemainderChangeState.Approved => SalesRemainderChangeNamesV1.Approved,
                RemainderChangeState.Rejected => SalesRemainderChangeNamesV1.Rejected,
                _ => SalesRemainderChangeNamesV1.Pending,
            },
            request.OrderedQuantityText, request.RequestedQuantityText, request.Reason, request.RequestedBy, request.RequestedAtUtc,
            Text(request.Floor.Loaded, unit), Text(request.Floor.Shipped, unit), Text(request.Floor.Delivered, unit),
            decision?.DecidedBy, decision?.DecidedAtUtc, decision?.Reason, decision?.ResultQuantityText,
            decision?.Floor is { } decided ? Text(decided.Loaded, unit) : null);
    }
}
