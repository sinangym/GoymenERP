using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Quantities;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// DEC-SAL-005 (KILITLI, K1): "Sipariş iptalinde aktif tahsis, bağlı satın alma/üretim, irsaliyesiz yükleme, faturalanmamış
/// irsaliye ve eşleşmemiş avans/dekont etkisi gösterilir." — "Etki önizlemesi olmadan iptal yok." AT-007: "İptal ve ters işlem
/// geçmişi silmez."
/// <list type="bullet">
/// <item><b>Preview</b> (GET, <c>sales.orders.cancel</c>): each DEC-SAL-005 category read from its OWNER through that owner's
/// port — Inventory's active allocations plus Logistics's loading-instruction lines and approved dispatch-note candidates —
/// never from another module's tables; a
/// category no module on main records is <see cref="SalesCancellationImpactStatusesV1.NoSource"/>, never an assumed zero; an
/// owner that cannot answer is <see cref="SalesCancellationImpactStatusesV1.Unreadable"/> and its numbers are null.</item>
/// <item><b>Fail-closed</b> (what blocks is not decided): an order is cancelled only when it is Approved, not cancelled, and no
/// category is Present or Unreadable. Nothing of another module is released or reversed: the user resolves the owner's record
/// in its own screen first (an allocation is released in Inventory), then cancels.</item>
/// <item><b>The preview is the command's precondition:</b> the command carries the preview's token; the impact is read again
/// inside the single save's last step and must still be exactly that preview, else 409 (the user reads it again).</item>
/// <item><b>Ordering</b> (the delivery schedule's): fence → version shape → the key's replay (§4/§7; a replay asks no owner) →
/// the store reads the draft → fence → the domain's own preconditions (version, state, reason: nothing is asked of an owner
/// for a stale or wrong-state command) → each owner (fence after each) → token → blockers → last fence → ONE save. A stale
/// version, a wrong state, a changed impact and a blocked cancellation are the key's TERMINAL 409s (recorded, replayed); an
/// unreadable owner is 503 (not recorded — the retry reads again). Nothing is measured after the commit (S03).</item>
/// <item><b>Residual race (declared, not closed):</b> no transaction spans Sales and the owners. An allocation or instruction
/// committed by its owner between the last owner read and this save is not seen; after the commit the Sales line port answers
/// the order's lines as not approved, so no owner creates a NEW link to a cancelled order.</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService
{
    private static readonly JsonSerializerOptions ImpactJson = new(JsonSerializerDefaults.Web);

    public async Task<SalesOrderDraftCancellationImpactResponse> GetCancellationImpactAsync(Guid draftId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.CancelPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        SalesOrderDraft? draft;
        try
        {
            draft = await GuardedAsync(scope, permission, () => store.FindDraftAsync(draftId, cancellationToken), cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        if (draft is null)
        {
            // Another company's draft is null under the tenant filter: 404, and no owner is ever asked for it.
            throw new SalesDraftServiceException(404, "not_found");
        }

        return (await ReadImpactAsync(scope, permission, draft, cancellationToken)).Response;
    }

    public async Task<SalesOrderDraftResponse> CancelDraftAsync(Guid draftId, SalesDraftCancelRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();

        var permission = bindings.CancelPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (!long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion) || expectedVersion < 1)
        {
            throw new SalesDraftServiceException(400, "validation_failed", [new("expectedVersion", "validation_failed")]);
        }

        const string commandName = SalesDraftCommands.CancelV1;
        var requestHash = SalesDraftRequestHash.Compute(commandName, draftId, request);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            // The same key, content and actor: the stored result again — the owners are not asked, nothing is cancelled twice.
            return await ReplayedAsync(scope, replay, cancellationToken);
        }

        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var audit = new SalesDraftStateAudit("sales.order-draft.cancelled.v1", "Cancel",
            "Onaylı satış siparişi etki önizlemesiyle iptal edildi (DEC-SAL-005; geçmiş silinmez)");

        SalesDraftStateResult? result;
        try
        {
            result = await store.CancelDraftAsync(envelope, requestHash, commandName, draftId,
                (draft, impact, now) => draft.Cancel(expectedVersion, scope.ActorId, request.Reason, impact, now), audit,
                async (draft, writeCancellationToken) =>
                {
                    scope = await RequireStillAsync(scope, permission, writeCancellationToken);
                    // The domain's own preconditions first: a stale, cancelled or unapproved order asks no owner.
                    draft.RequireCancellable(expectedVersion, request.Reason);
                    var impact = await ReadImpactAsync(scope, permission, draft, writeCancellationToken);
                    if (impact.Unreadable)
                    {
                        throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.CancellationImpactUnavailable);
                    }

                    if (!string.Equals(impact.Response.ImpactToken, request.ImpactToken, StringComparison.Ordinal))
                    {
                        throw new ImpactChanged();
                    }

                    if (impact.Blockers.Count > 0)
                    {
                        throw new CancellationBlocked(impact.Blockers);
                    }

                    // The last fence that can still prevent the write; none after the commit (ORG #321 P3-1).
                    scope = await RequireStillAsync(scope, permission, writeCancellationToken);
                    return impact.Snapshot;
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
        catch (ImpactChanged)
        {
            throw await TerminalAsync(scope, permission, envelope, requestHash, new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.CancellationImpactChanged,
                [new("impactToken", SalesDraftProblemCodesV1.CancellationImpactChanged)]), cancellationToken, commandName, draftId);
        }
        catch (CancellationBlocked blocked)
        {
            throw await TerminalAsync(scope, permission, envelope, requestHash, new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.CancellationBlocked,
                [.. blocked.Sources.Select(source => new SalesDraftFieldError(source, SalesCancellationImpactStatusesV1.Present))]),
                cancellationToken, commandName, draftId);
        }
        catch (SalesRuleException exception) when (exception.Code is SalesRuleCodes.CancellationReasonRequired or SalesRuleCodes.CancellationReasonTooLong)
        {
            throw new SalesDraftServiceException(422, "validation_failed", [new("reason", exception.Code)]);
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

    /// <summary>AT-007: the recorded cancellation (or null) under the draft read; another company's draft is 404.</summary>
    public async Task<SalesOrderDraftCancellationResponse> GetDraftCancellationAsync(Guid draftId, CancellationToken cancellationToken = default)
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
            throw new SalesDraftServiceException(404, "not_found");
        }

        return new SalesOrderDraftCancellationResponse(draft.Cancellation is not { } facts
            ? null
            : new SalesDraftCancellationV1(SalesDraftApiV1.RevisionText(facts.Revision), facts.CancelledBy, facts.CancelledAtUtc, facts.Reason,
                facts.Impact.Token, [.. facts.Impact.Sources.Select(entry => new SalesDraftCancellationImpactSourceV1(entry.Source, entry.Status))]));
    }

    // ---- the impact ---------------------------------------------------------------------------------------------------

    private sealed record Impact(SalesOrderDraftCancellationImpactResponse Response, CancellationImpactSnapshot Snapshot,
        IReadOnlyList<string> Blockers, bool Unreadable);

    private sealed class ImpactChanged : Exception;

    private sealed class CancellationBlocked(IReadOnlyList<string> sources) : Exception
    {
        public IReadOnlyList<string> Sources { get; } = sources;
    }

    /// <summary>Per line: the allocation owner's active count and kilograms, or null when the owner could not be read.</summary>
    private sealed record LineAllocation(int Count, decimal RemainingKg);

    /// <summary>Per line: the loading owner's instruction-line count and totals in the line's order unit.</summary>
    private sealed record LineLoading(int Count, decimal Instructed, decimal Loaded, decimal Shipped, decimal Delivered);

    private sealed record LoadingImpact(Dictionary<Guid, LineLoading> Lines, int ActiveApprovedDispatchNoteCount);

    /// <summary>
    /// Reads every DEC-SAL-005 category of <paramref name="draft"/> from its owner, each read released only under the
    /// operation's name (measured after it). An owner that is not composed, throws or answers a malformed value is
    /// Unreadable — its numbers are null, never zero; a lost authority is the caller's 403 (it propagates).
    /// </summary>
    private async Task<Impact> ReadImpactAsync(Scope scope, string? permission, SalesOrderDraft draft, CancellationToken cancellationToken)
    {
        var allocationsByLine = await ReadAllocationsAsync(scope, permission, draft, cancellationToken);
        var loadingImpact = await ReadLoadingImpactAsync(scope, permission, draft, cancellationToken);
        var loadingByLine = loadingImpact?.Lines;

        var allocationStatus = allocationsByLine is null
            ? SalesCancellationImpactStatusesV1.Unreadable
            : allocationsByLine.Values.Any(line => line.Count > 0) ? SalesCancellationImpactStatusesV1.Present : SalesCancellationImpactStatusesV1.None;
        var loadingStatus = loadingByLine is null
            ? SalesCancellationImpactStatusesV1.Unreadable
            : loadingByLine.Values.Any(line => line.Count > 0 || line.Instructed > 0m) ? SalesCancellationImpactStatusesV1.Present : SalesCancellationImpactStatusesV1.None;
        var dispatchNoteStatus = loadingImpact is null
            ? SalesCancellationImpactStatusesV1.Unreadable
            : loadingImpact.ActiveApprovedDispatchNoteCount > 0
                ? SalesCancellationImpactStatusesV1.Present
                : SalesCancellationImpactStatusesV1.None;
        var purchasingStatus = await ReadLinkedSupplyStatusAsync(scope, permission, draft, SalesLinkedSupplyKindsV1.Purchasing,
            cancellationToken);
        var productionStatus = await ReadLinkedSupplyStatusAsync(scope, permission, draft, SalesLinkedSupplyKindsV1.Production,
            cancellationToken);
        var sources = SalesCancellationImpactSourcesV1.All.Select(source => new SalesDraftCancellationImpactSourceV1(source, source switch
        {
            SalesCancellationImpactSourcesV1.ActiveAllocations => allocationStatus,
            SalesCancellationImpactSourcesV1.LinkedPurchasing => purchasingStatus,
            SalesCancellationImpactSourcesV1.LinkedProduction => productionStatus,
            SalesCancellationImpactSourcesV1.LoadingWithoutDispatchNote => loadingStatus,
            SalesCancellationImpactSourcesV1.UninvoicedDispatchNotes => dispatchNoteStatus,
            // No module on main records the remaining link to a Sales order: no owner to ask, shown as such — not as zero.
            _ => SalesCancellationImpactStatusesV1.NoSource,
        })).ToArray();

        var lines = draft.Lines.OrderBy(line => line.LineNo).Select(line =>
        {
            var allocation = allocationsByLine is null ? null : allocationsByLine.GetValueOrDefault(line.Id) ?? new LineAllocation(0, 0m);
            var loading = loadingByLine is null ? null : loadingByLine.GetValueOrDefault(line.Id) ?? new LineLoading(0, 0m, 0m, 0m, 0m);
            return new SalesDraftCancellationImpactLineV1(line.Id, line.LineNo, line.OrderUnit.UnitId, line.QuantityText,
                allocation?.Count, allocation is null ? null : Text(allocation.RemainingKg, QuantityUnit.Kilogram),
                loading?.Count,
                loading is null ? null : Text(loading.Instructed, line.Quantity.Unit),
                loading is null ? null : Text(loading.Loaded, line.Quantity.Unit),
                loading is null ? null : Text(loading.Shipped, line.Quantity.Unit),
                loading is null ? null : Text(loading.Delivered, line.Quantity.Unit));
        }).ToArray();

        var blockers = sources.Where(source => source.Status == SalesCancellationImpactStatusesV1.Present).Select(source => source.Source).ToArray();
        var unreadable = sources.Any(source => source.Status == SalesCancellationImpactStatusesV1.Unreadable);
        var cancellable = draft.State == SalesDraftState.Draft && draft.ApprovalState == InternalApprovalState.Approved
            && blockers.Length == 0 && !unreadable;
        var version = draft.Version.ToString(CultureInfo.InvariantCulture);
        var revision = SalesDraftApiV1.RevisionText(draft.Revision);
        var token = ImpactToken(draft, version, revision, sources, lines);
        return new Impact(
            new SalesOrderDraftCancellationImpactResponse(version, revision, cancellable, token, sources, lines),
            new CancellationImpactSnapshot(token, [.. sources.Select(source => new CancellationImpactEntry(source.Source, source.Status))]),
            blockers,
            unreadable);
    }

    /// <summary>
    /// The allocation owner's answer per order line, or null when it is not composed, fails or answers a malformed value
    /// (another company or order, an unknown line, a line twice, a negative or an over-scale kilogram, a count below zero).
    /// </summary>
    private async Task<Dictionary<Guid, LineAllocation>?> ReadAllocationsAsync(Scope scope, string? permission, SalesOrderDraft draft,
        CancellationToken cancellationToken)
    {
        if (allocations is null)
        {
            return null;
        }

        SalesOrderAllocationReadAnswerV1? answer;
        try
        {
            answer = await GuardedAsync(scope, permission, () => allocations.ReadOrderAllocationsAsync(draft.Id, cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
        {
            return null;
        }

        if (answer is null || answer.Lines is null || answer.CompanyId != scope.CompanyId || answer.OrderId != draft.Id)
        {
            return null;
        }

        // SAT-D167: an owner may answer a link on a passive line (opened before its removal was recorded); it is the order's
        // line and still a link (fail-closed), never a malformed answer.
        var lineIds = draft.AllLines.Select(line => line.Id).ToHashSet();
        var result = new Dictionary<Guid, LineAllocation>();
        foreach (var entry in answer.Lines)
        {
            if (entry is null || !lineIds.Contains(entry.OrderLineId) || result.ContainsKey(entry.OrderLineId)
                || entry.ActiveCount < 0 || entry.ActiveRemainingKg < 0m || !FitsScale(entry.ActiveRemainingKg, QuantityUnit.Kilogram)
                || (entry.ActiveCount == 0 && entry.ActiveRemainingKg != 0m))
            {
                return null;
            }

            result[entry.OrderLineId] = new LineAllocation(entry.ActiveCount, entry.ActiveRemainingKg);
        }

        return result;
    }

    /// <summary>
    /// S-1 / MOD-03: the "bağlı üretim" / "bağlı satın alma" category from the supply owner's adapter of that kind
    /// (<see cref="ISalesOrderLinkedSupplyReadPortV1"/>), asked under the caller's own name (fenced before and after):
    /// <list type="bullet">
    /// <item>no adapter of the kind → <see cref="SalesCancellationImpactStatusesV1.NoSource"/> (today's behaviour, not a
    /// blocker);</item>
    /// <item>more than one adapter of the kind, a throwing adapter, an Unreadable answer or a malformed one (another company,
    /// order or kind; an empty or repeated id; a document type of another kind; a line not on the order; an invalid kilogram
    /// text) → <see cref="SalesCancellationImpactStatusesV1.Unreadable"/>;</item>
    /// <item>the owner's <see cref="SalesCancellationImpactStatusesV1.Present"/> (with its open lines) or
    /// <see cref="SalesCancellationImpactStatusesV1.None"/> (with none); its Unreadable, a NoSource from a registered adapter, or a
    /// status that contradicts the items → <see cref="SalesCancellationImpactStatusesV1.Unreadable"/>.</item>
    /// </list>
    /// What blocks stays each flow's existing rule (the cancellation: Present or Unreadable; the force-close, SAT-D12: Present or
    /// Unreadable for these categories).
    /// </summary>
    private async Task<string> ReadLinkedSupplyStatusAsync(Scope scope, string? permission, SalesOrderDraft draft, string kind,
        CancellationToken cancellationToken) =>
        (await ReadLinkedSupplyAsync(scope, permission, draft, kind, cancellationToken)).Status;

    /// <summary>
    /// What one supply kind answers for an order: its status in the cancellation preview's vocabulary and, for <c>Present</c>, the
    /// owner's open items. SAT-D219 (EK38): no adapter of the kind is <c>NoSource</c> ONLY while no module declares that it records
    /// links of the kind (<see cref="ISalesOrderLinkedSupplyRecorderV1"/>); a declared recorder without an adapter is
    /// <c>Unreadable</c> (fail-closed). An item may name a PASSIVE line of the order (a line a revision removed) — it is the
    /// order's line and still an open link, never a malformed answer.
    /// </summary>
    private async Task<LinkedSupplyRead> ReadLinkedSupplyAsync(Scope scope, string? permission, SalesOrderDraft draft, string kind,
        CancellationToken cancellationToken)
    {
        var noItems = (IReadOnlyList<SalesOrderLinkedSupplyItemV1>)[];
        var adapters = (linkedSupplies ?? []).Where(adapter => adapter is not null && adapter.SupplyKind == kind).ToArray();
        if (adapters.Length == 0)
        {
            var recorded = (linkedSupplyRecorders ?? []).Any(recorder => recorder is not null && recorder.SupplyKind == kind);
            return new(recorded ? SalesCancellationImpactStatusesV1.Unreadable : SalesCancellationImpactStatusesV1.NoSource, noItems);
        }

        if (adapters.Length > 1)
        {
            return new(SalesCancellationImpactStatusesV1.Unreadable, noItems);
        }

        var query = new SalesOrderLinkedSupplyQueryV1(scope.CompanyId, draft.Id, null);
        SalesOrderLinkedSupplyAnswerV1? answer;
        try
        {
            answer = await GuardedAsync(scope, permission,
                () => adapters[0].ReadOrderLinkedSupplyAsync(query, cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
        {
            return new(SalesCancellationImpactStatusesV1.Unreadable, noItems);
        }

        if (answer is null || answer.CompanyId != scope.CompanyId || answer.OrderId != draft.Id || answer.SupplyKind != kind
            || answer.OpenItems is null
            || answer.Status is not (SalesCancellationImpactStatusesV1.Present or SalesCancellationImpactStatusesV1.None)
            || (answer.Status == SalesCancellationImpactStatusesV1.Present) != (answer.OpenItems.Count > 0))
        {
            // Unreadable, NoSource from an adapter, an unknown status, or a status contradicting the items: fail-closed.
            return new(SalesCancellationImpactStatusesV1.Unreadable, noItems);
        }

        var lineIds = draft.AllLines.Select(line => line.Id).ToHashSet();
        var seen = new HashSet<(Guid, Guid?)>();
        foreach (var item in answer.OpenItems)
        {
            var typeFits = kind == SalesLinkedSupplyKindsV1.Production
                ? item?.DocumentType is SalesLinkedSupplyDocumentTypesV1.ProductionPlan or SalesLinkedSupplyDocumentTypesV1.ProductionOrder
                : item?.DocumentType == SalesLinkedSupplyDocumentTypesV1.PurchaseOrder;
            if (item is null || item.SupplyId == Guid.Empty || item.SupplyLineId == Guid.Empty
                || !seen.Add((item.SupplyId, item.SupplyLineId)) || !typeFits
                || (item.OrderLineId is { } lineId && !lineIds.Contains(lineId))
                || !QuantityTextIsValid(item.OpenQuantity) || (item.OpenQuantity is null) != (item.OpenQuantityUnitId is null)
                || item.OpenQuantityUnitId == Guid.Empty
                || !QuantityTextIsValid(item.PlannedKg) || !QuantityTextIsValid(item.ActualKg))
            {
                return new(SalesCancellationImpactStatusesV1.Unreadable, noItems);
            }
        }

        return new(answer.Status, answer.OpenItems);
    }

    /// <summary>One supply kind's validated answer: its status and (for Present) the owner's open items.</summary>
    private sealed record LinkedSupplyRead(string Status, IReadOnlyList<SalesOrderLinkedSupplyItemV1> OpenItems);

    /// <summary>
    /// SAT-D219 (EK38): a line with an OPEN linked purchase order or production order is neither removed nor given another
    /// product (the shipped-line rule of SAT-D167). Every supply kind is read afresh under the caller's name (fenced after each
    /// owner): <c>None</c> and <c>NoSource</c> (no module records links of the kind) pass; an open item naming one of
    /// <paramref name="lineIds"/> — or the whole order, which cannot be told from any line and so counts for every line — blocks;
    /// an unreadable kind (its owner failed, answered malformed, is ambiguous, or records links without an adapter) is 503
    /// <c>revision_impact_unavailable</c>. Returns the ids of the blocked lines; the caller names them in its refusal.
    /// </summary>
    private async Task<HashSet<Guid>> ReadLinkedSupplyBlockedLinesAsync(Scope scope, string? permission, SalesOrderDraft draft,
        IReadOnlyCollection<Guid> lineIds, CancellationToken cancellationToken)
    {
        var blocked = new HashSet<Guid>();
        foreach (var kind in SalesLinkedSupplyKindsV1.All)
        {
            var read = await ReadLinkedSupplyAsync(scope, permission, draft, kind, cancellationToken);
            if (read.Status == SalesCancellationImpactStatusesV1.Unreadable)
            {
                throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.RevisionImpactUnavailable);
            }

            foreach (var item in read.OpenItems)
            {
                if (item.OrderLineId is null)
                {
                    blocked.UnionWith(lineIds);
                }
                else if (lineIds.Contains(item.OrderLineId.Value))
                {
                    blocked.Add(item.OrderLineId.Value);
                }
            }
        }

        return blocked;
    }

    /// <summary>An owner's quantity text: absent, or a non-negative exact decimal text (no sign, no exponent, no grouping).</summary>
    private static bool QuantityTextIsValid(string? text) =>
        text is null
        || (text.Length > 0 && text.All(character => char.IsAsciiDigit(character) || character == '.')
            && text.Count(character => character == '.') <= 1 && text[0] != '.' && text[^1] != '.'
            && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _));

    /// <summary>
    /// The loading owner's answer per order line in the line's own order unit, or null when it is not composed, fails or
    /// answers a malformed value (another company or order, an unknown line, another unit — no conversion —, a negative or
    /// over-scale total, a count/zero mismatch, or totals outside the owner chain
    /// delivered ≤ shipped ≤ loaded ≤ instructed ≤ ordered).
    /// </summary>
    private async Task<Dictionary<Guid, LineLoading>?> ReadLoadingAsync(Scope scope, string? permission, SalesOrderDraft draft,
        CancellationToken cancellationToken) =>
        (await ReadLoadingAnswerAsync(scope, permission, draft, includeDispatchNoteCandidates: false, cancellationToken))?.Lines;

    /// <summary>
    /// The Logistics owner's combined cancellation-impact answer. Loading totals and active approved dispatch-note
    /// candidate count share one company/order envelope and one guarded owner read.
    /// </summary>
    private Task<LoadingImpact?> ReadLoadingImpactAsync(Scope scope, string? permission, SalesOrderDraft draft,
        CancellationToken cancellationToken) =>
        ReadLoadingAnswerAsync(scope, permission, draft, includeDispatchNoteCandidates: true, cancellationToken);

    private async Task<LoadingImpact?> ReadLoadingAnswerAsync(Scope scope, string? permission, SalesOrderDraft draft,
        bool includeDispatchNoteCandidates, CancellationToken cancellationToken)
    {
        if (loadings is null)
        {
            return null;
        }

        SalesOrderLoadingReadAnswerV1? answer;
        try
        {
            Func<Task<SalesOrderLoadingReadAnswerV1>> read = includeDispatchNoteCandidates
                ? () => loadings.ReadOrderCancellationImpactAsync(draft.Id, cancellationToken)
                : () => loadings.ReadOrderLoadingAsync(draft.Id, cancellationToken);
            answer = await GuardedAsync(scope, permission,
                read, cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
        {
            return null;
        }

        if (answer is null || answer.Lines is null || answer.CompanyId != scope.CompanyId || answer.OrderId != draft.Id
            || (includeDispatchNoteCandidates && answer.ActiveApprovedDispatchNoteCount < 0))
        {
            return null;
        }

        // SAT-D167: a passive line's loading is the order's (a link, fail-closed), never a malformed answer.
        var lines = draft.AllLines.ToDictionary(line => line.Id);
        var result = new Dictionary<Guid, LineLoading>();
        foreach (var entry in answer.Lines)
        {
            if (entry is null || !lines.TryGetValue(entry.OrderLineId, out var line) || entry.UnitId != line.OrderUnit.UnitId
                || entry.InstructionLineCount < 0 || entry.Delivered < 0m || entry.Delivered > entry.Shipped
                || entry.Shipped > entry.Loaded || entry.Loaded > entry.Instructed
                || !FitsScale(entry.Instructed, line.Quantity.Unit) || !FitsScale(entry.Loaded, line.Quantity.Unit)
                || !FitsScale(entry.Shipped, line.Quantity.Unit) || !FitsScale(entry.Delivered, line.Quantity.Unit)
                || ((entry.InstructionLineCount == 0) != (entry.Instructed == 0m)))
            {
                return null;
            }

            result[line.Id] = result.TryGetValue(line.Id, out var sum)
                ? new LineLoading(checked(sum.Count + entry.InstructionLineCount), sum.Instructed + entry.Instructed, sum.Loaded + entry.Loaded,
                    sum.Shipped + entry.Shipped, sum.Delivered + entry.Delivered)
                : new LineLoading(entry.InstructionLineCount, entry.Instructed, entry.Loaded, entry.Shipped, entry.Delivered);
        }

        // The Logistics producer caps the aggregate instructed quantity through the Sales-owned approved-order limit.
        // Validate the fully aggregated answer as well: two individually plausible owner rows must not jointly exceed
        // the canonical Sales quantity. An absent Logistics row remains a valid zero; a positive-count row cannot be zero.
        // SAT-D104 (EK13): a quantity revision no longer waits for the instructions to be resolved, so an instruction made
        // under an earlier APPROVED quantity may stand above a decreased one — the bound is the largest quantity the line was
        // ever approved or now requests (every cap Logistics could have been answered is at most that).
        if (result.Any(pair => pair.Value.Instructed > InstructedBound(draft, lines[pair.Key])
            || ((pair.Value.Count == 0) != (pair.Value.Instructed == 0m))))
        {
            return null;
        }

        return new LoadingImpact(result, includeDispatchNoteCandidates ? answer.ActiveApprovedDispatchNoteCount : 0);
    }

    /// <summary>
    /// The most Logistics may have instructed for <paramref name="line"/>: its current quantity or, when larger, the quantity
    /// any archived approved revision held for it (SAT-D104). An archived quantity that does not read is ignored here — the
    /// bound only widens for a value that reads exactly.
    /// </summary>
    private static decimal InstructedBound(SalesOrderDraft draft, SalesOrderDraftLine line)
    {
        var bound = line.Quantity.Value;
        foreach (var revision in draft.ArchivedRevisions)
        {
            var archived = revision.Lines.FirstOrDefault(candidate => candidate.LineId == line.Id);
            if (archived is not null && archived.OrderUnitId == line.OrderUnit.UnitId
                && decimal.TryParse(archived.QuantityText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var approved)
                && approved > bound)
            {
                bound = approved;
            }
        }

        return bound;
    }

    private static bool FitsScale(decimal value, QuantityUnit unit)
    {
        try
        {
            _ = Quantity.Of(value, unit);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Plain invariant text of an exact, already validated quantity (Ö2: trailing zeros dropped; equal values print equally).</summary>
    private static string Text(decimal value, QuantityUnit unit) =>
        Quantity.Of(value, unit).Value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The preview's identity: SHA-256 (lowercase hex) of the canonical JSON of the draft, its version, revision and lifecycle
    /// and approval states, every category and every line as shown. Any change of any of them is another token.
    /// </summary>
    private static string ImpactToken(SalesOrderDraft draft, string version, string revision,
        IReadOnlyList<SalesDraftCancellationImpactSourceV1> sources, IReadOnlyList<SalesDraftCancellationImpactLineV1> lines)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            shape = "sales.order-draft.cancellation-impact.v1",
            draftId = draft.Id,
            version,
            revision,
            state = draft.State.ToString(),
            approvalState = draft.ApprovalState.ToString(),
            sources,
            lines,
        }, ImpactJson);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
