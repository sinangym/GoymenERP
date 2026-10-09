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
/// port — Inventory's active allocations, Logistics's loading-instruction lines — never from another module's tables; a
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

    /// <summary>
    /// Reads every DEC-SAL-005 category of <paramref name="draft"/> from its owner, each read released only under the
    /// operation's name (measured after it). An owner that is not composed, throws or answers a malformed value is
    /// Unreadable — its numbers are null, never zero; a lost authority is the caller's 403 (it propagates).
    /// </summary>
    private async Task<Impact> ReadImpactAsync(Scope scope, string? permission, SalesOrderDraft draft, CancellationToken cancellationToken)
    {
        var allocationsByLine = await ReadAllocationsAsync(scope, permission, draft, cancellationToken);
        var loadingByLine = await ReadLoadingAsync(scope, permission, draft, cancellationToken);

        var allocationStatus = allocationsByLine is null
            ? SalesCancellationImpactStatusesV1.Unreadable
            : allocationsByLine.Values.Any(line => line.Count > 0) ? SalesCancellationImpactStatusesV1.Present : SalesCancellationImpactStatusesV1.None;
        var loadingStatus = loadingByLine is null
            ? SalesCancellationImpactStatusesV1.Unreadable
            : loadingByLine.Values.Any(line => line.Count > 0 || line.Instructed > 0m) ? SalesCancellationImpactStatusesV1.Present : SalesCancellationImpactStatusesV1.None;
        var sources = SalesCancellationImpactSourcesV1.All.Select(source => new SalesDraftCancellationImpactSourceV1(source, source switch
        {
            SalesCancellationImpactSourcesV1.ActiveAllocations => allocationStatus,
            SalesCancellationImpactSourcesV1.LoadingWithoutDispatchNote => loadingStatus,
            // No module on main records these links to a Sales order: no owner to ask, shown as such — not as zero.
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

        var lineIds = draft.Lines.Select(line => line.Id).ToHashSet();
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
    /// The loading owner's answer per order line in the line's own order unit, or null when it is not composed, fails or
    /// answers a malformed value (another company or order, an unknown line, another unit — no conversion —, a negative or
    /// over-scale total, a count below zero, or totals out of the owner's own order delivered ≤ shipped ≤ loaded ≤ instructed).
    /// </summary>
    private async Task<Dictionary<Guid, LineLoading>?> ReadLoadingAsync(Scope scope, string? permission, SalesOrderDraft draft,
        CancellationToken cancellationToken)
    {
        if (loadings is null)
        {
            return null;
        }

        SalesOrderLoadingReadAnswerV1? answer;
        try
        {
            answer = await GuardedAsync(scope, permission, () => loadings.ReadOrderLoadingAsync(draft.Id, cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
        {
            return null;
        }

        if (answer is null || answer.Lines is null || answer.CompanyId != scope.CompanyId || answer.OrderId != draft.Id)
        {
            return null;
        }

        var lines = draft.Lines.ToDictionary(line => line.Id);
        var result = new Dictionary<Guid, LineLoading>();
        foreach (var entry in answer.Lines)
        {
            if (entry is null || !lines.TryGetValue(entry.OrderLineId, out var line) || entry.UnitId != line.OrderUnit.UnitId
                || entry.InstructionLineCount < 0 || entry.Delivered < 0m || entry.Delivered > entry.Shipped
                || entry.Shipped > entry.Loaded || entry.Loaded > entry.Instructed
                || !FitsScale(entry.Instructed, line.Quantity.Unit) || !FitsScale(entry.Loaded, line.Quantity.Unit)
                || !FitsScale(entry.Shipped, line.Quantity.Unit) || !FitsScale(entry.Delivered, line.Quantity.Unit)
                || (entry.InstructionLineCount == 0 && entry.Instructed != 0m))
            {
                return null;
            }

            result[line.Id] = result.TryGetValue(line.Id, out var sum)
                ? new LineLoading(checked(sum.Count + entry.InstructionLineCount), sum.Instructed + entry.Instructed, sum.Loaded + entry.Loaded,
                    sum.Shipped + entry.Shipped, sum.Delivered + entry.Delivered)
                : new LineLoading(entry.InstructionLineCount, entry.Instructed, entry.Loaded, entry.Shipped, entry.Delivered);
        }

        return result;
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
