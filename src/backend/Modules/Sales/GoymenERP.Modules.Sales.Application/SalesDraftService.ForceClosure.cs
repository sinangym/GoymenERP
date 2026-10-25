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
/// SINAN_KARARLARI_2026-10-21 No 30 (force-close, "zorla kapatma") as narrowed by SAT-D12 (Sinan, 2026-10-25): the cancellation
/// (DEC-SAL-005) applied to a CLOSURE — the same six categories, owners and ports, the same ordering and refusal semantics, with
/// these differences only:
/// <list type="bullet">
/// <item><b>Each source says whether it blocks.</b> <c>inventory.active-allocations</c> Present blocks (DEC-STK-005: no silent
/// release — the allocation is closed in Inventory first); <c>logistics.loading-without-dispatch-note</c> is Present and blocks
/// when any line's instructed total exceeds its shipped total (open loading — instructed or loaded but not yet shipped; the
/// fail-closed reading of "irsaliyesiz yükleme" for a closure: fully shipped instruction lines are the shipped part and do not
/// block); <c>invoicing.uninvoiced-dispatch-notes</c> Present does NOT block (SAT-D12: a warning, open invoicing work — the
/// financial-closing axis stays open); NoSource does not block; ANY Unreadable blocks (and is the command's 503).</item>
/// <item><b>The closed remainder</b> of every line — ordered − shipped from the Logistics answer, in the line's order unit,
/// exact — is shown in the preview and recorded by the command ("iptal edilen kalan", DEC-SAL-001 separate axis). The line
/// quantities do not move; no stock or money event is produced; nothing of another module is released or touched.</item>
/// <item><b>Ordering</b> (the cancellation's): fence → version shape → the key's replay → the store reads the draft → fence → the
/// domain's own preconditions → each owner (fence after each) → unreadable (503, not recorded) → token → blockers → last fence →
/// ONE save. A stale version, a wrong state, a changed impact and a blocked closure are the key's TERMINAL 409s.</item>
/// <item><b>Residual race (declared, as the cancellation's):</b> no transaction spans Sales and the owners; after the commit the
/// Sales line port answers the order's lines as not approved ("Closed"), so no owner creates a NEW link to a closed order.</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService
{
    public async Task<SalesOrderDraftForceCloseImpactResponse> GetForceCloseImpactAsync(Guid draftId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.ForceClosePermission;
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

        return (await ReadForceCloseImpactAsync(scope, permission, draft, cancellationToken)).Response;
    }

    public async Task<SalesOrderDraftResponse> ForceCloseDraftAsync(Guid draftId, SalesDraftForceCloseRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();

        var permission = bindings.ForceClosePermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (!long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion) || expectedVersion < 1)
        {
            throw new SalesDraftServiceException(400, "validation_failed", [new("expectedVersion", "validation_failed")]);
        }

        const string commandName = SalesDraftCommands.ForceCloseV1;
        var requestHash = SalesDraftRequestHash.Compute(commandName, draftId, request);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            // The same key, content and actor: the stored result again — the owners are not asked, nothing is closed twice.
            return await ReplayedAsync(scope, replay, cancellationToken);
        }

        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var audit = new SalesDraftStateAudit("sales.order-draft.force-closed.v1", "ForceClose",
            "Onaylı satış siparişi etki önizlemesiyle zorla kapatıldı; kalan iptal edilen kalan olarak kaydedildi (No 30, SAT-D12; geçmiş silinmez)");

        SalesDraftStateResult? result;
        try
        {
            result = await store.ForceCloseDraftAsync(envelope, requestHash, commandName, draftId,
                (draft, plan, now) => draft.ForceClose(expectedVersion, scope.ActorId, request.Reason, plan.Impact, plan.Lines, now), audit,
                async (draft, writeCancellationToken) =>
                {
                    scope = await RequireStillAsync(scope, permission, writeCancellationToken);
                    // The domain's own preconditions first: a stale, terminal, unapproved or remainder-pending order asks no owner.
                    draft.RequireForceClosable(expectedVersion, request.Reason);
                    var impact = await ReadForceCloseImpactAsync(scope, permission, draft, writeCancellationToken);
                    if (impact.Unreadable)
                    {
                        throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ForceCloseImpactUnavailable);
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
                    return impact.Plan;
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
            throw await TerminalAsync(scope, permission, envelope, requestHash, new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.ForceCloseImpactChanged,
                [new("impactToken", SalesDraftProblemCodesV1.ForceCloseImpactChanged)]), cancellationToken, commandName, draftId);
        }
        catch (CancellationBlocked blocked)
        {
            throw await TerminalAsync(scope, permission, envelope, requestHash, new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.ForceCloseBlocked,
                [.. blocked.Sources.Select(source => new SalesDraftFieldError(source, SalesCancellationImpactStatusesV1.Present))]),
                cancellationToken, commandName, draftId);
        }
        catch (SalesRuleException exception) when (exception.Code is SalesRuleCodes.ForceCloseReasonRequired or SalesRuleCodes.ForceCloseReasonTooLong)
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

    /// <summary>No 30: the recorded force-closure (or null) under the draft read; another company's draft is 404.</summary>
    public async Task<SalesOrderDraftForceClosureResponse> GetDraftForceClosureAsync(Guid draftId, CancellationToken cancellationToken = default)
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

        return new SalesOrderDraftForceClosureResponse(draft.ForceClosure is not { } facts
            ? null
            : new SalesDraftForceClosureV1(SalesDraftApiV1.RevisionText(facts.Revision), facts.ClosedBy, facts.ClosedAtUtc, facts.Reason,
                facts.Impact.Token,
                [.. facts.Impact.Sources.Select(entry => new SalesDraftForceCloseImpactSourceV1(entry.Source, entry.Status, entry.Blocks))],
                [.. facts.Lines.Select(line => new SalesDraftForceClosureLineV1(line.LineId, line.LineNo, line.Ordered, line.Shipped,
                    line.ClosedRemainder))]));
    }

    // ---- the force-close impact -----------------------------------------------------------------------------------------

    private sealed record ForceCloseImpact(SalesOrderDraftForceCloseImpactResponse Response, SalesForceClosurePlan Plan,
        IReadOnlyList<string> Blockers, bool Unreadable);

    /// <summary>
    /// Reads every No 30 / DEC-SAL-005 category of <paramref name="draft"/> from its owner (the cancellation's ports and
    /// validation: an owner that is not composed, throws or answers a malformed value is Unreadable — its numbers are null,
    /// never zero) and decides per source whether it blocks the closure (SAT-D12: an uninvoiced dispatch note does not).
    /// </summary>
    private async Task<ForceCloseImpact> ReadForceCloseImpactAsync(Scope scope, string? permission, SalesOrderDraft draft,
        CancellationToken cancellationToken)
    {
        var allocationsByLine = await ReadAllocationsAsync(scope, permission, draft, cancellationToken);
        var loadingImpact = await ReadLoadingImpactAsync(scope, permission, draft, cancellationToken);
        var loadingByLine = loadingImpact?.Lines;

        var allocationStatus = allocationsByLine is null
            ? SalesCancellationImpactStatusesV1.Unreadable
            : allocationsByLine.Values.Any(line => line.Count > 0) ? SalesCancellationImpactStatusesV1.Present : SalesCancellationImpactStatusesV1.None;
        // Open loading only: instructed (or loaded) but not yet shipped. A fully shipped instruction line is the shipped part.
        var loadingStatus = loadingByLine is null
            ? SalesCancellationImpactStatusesV1.Unreadable
            : loadingByLine.Values.Any(line => line.Instructed > line.Shipped) ? SalesCancellationImpactStatusesV1.Present : SalesCancellationImpactStatusesV1.None;
        var dispatchNoteStatus = loadingImpact is null
            ? SalesCancellationImpactStatusesV1.Unreadable
            : loadingImpact.ActiveApprovedDispatchNoteCount > 0
                ? SalesCancellationImpactStatusesV1.Present
                : SalesCancellationImpactStatusesV1.None;
        var sources = SalesCancellationImpactSourcesV1.All.Select(source =>
        {
            var status = source switch
            {
                SalesCancellationImpactSourcesV1.ActiveAllocations => allocationStatus,
                SalesCancellationImpactSourcesV1.LoadingWithoutDispatchNote => loadingStatus,
                SalesCancellationImpactSourcesV1.UninvoicedDispatchNotes => dispatchNoteStatus,
                // No module on main records these remaining links to a Sales order: no owner to ask — not zero, not a blocker.
                _ => SalesCancellationImpactStatusesV1.NoSource,
            };
            return new SalesDraftForceCloseImpactSourceV1(source, status, ForceCloseBlocks(source, status));
        }).ToArray();

        var lines = draft.Lines.OrderBy(line => line.LineNo).Select(line =>
        {
            var allocation = allocationsByLine is null ? null : allocationsByLine.GetValueOrDefault(line.Id) ?? new LineAllocation(0, 0m);
            var loading = loadingByLine is null ? null : loadingByLine.GetValueOrDefault(line.Id) ?? new LineLoading(0, 0m, 0m, 0m, 0m);
            return new SalesDraftForceCloseImpactLineV1(line.Id, line.LineNo, line.OrderUnit.UnitId, line.QuantityText,
                allocation?.Count, allocation is null ? null : Text(allocation.RemainingKg, QuantityUnit.Kilogram),
                loading?.Count,
                loading is null ? null : Text(loading.Instructed, line.Quantity.Unit),
                loading is null ? null : Text(loading.Loaded, line.Quantity.Unit),
                loading is null ? null : Text(loading.Shipped, line.Quantity.Unit),
                loading is null ? null : Text(loading.Delivered, line.Quantity.Unit),
                loading is null ? null : SalesOrderDraft.ClosedRemainderText(line, loading.Shipped));
        }).ToArray();

        var unreadable = sources.Any(source => source.Status == SalesCancellationImpactStatusesV1.Unreadable)
            // The owner chain is validated on read (shipped ≤ instructed ≤ ordered); a remainder that still cannot be derived is
            // an unreadable answer, never a guessed zero.
            || (loadingByLine is not null && lines.Any(line => line.ClosedRemainder is null));
        var blockers = sources.Where(source => source.Blocks && source.Status == SalesCancellationImpactStatusesV1.Present)
            .Select(source => source.Source).ToArray();
        var closable = draft.State == SalesDraftState.Draft && draft.ApprovalState == InternalApprovalState.Approved
            && draft.PendingRemainderChange is null && !sources.Any(source => source.Blocks) && !unreadable;
        var version = draft.Version.ToString(CultureInfo.InvariantCulture);
        var revision = SalesDraftApiV1.RevisionText(draft.Revision);
        var token = ForceCloseImpactToken(draft, version, revision, sources, lines);
        var plan = new SalesForceClosurePlan(
            new ForceClosureImpactSnapshot(token, [.. sources.Select(source => new ForceClosureImpactEntry(source.Source, source.Status, source.Blocks))]),
            loadingByLine is null
                ? []
                : [.. draft.Lines.Select(line => new ForceClosureLineInput(line.Id, loadingByLine.GetValueOrDefault(line.Id)?.Shipped ?? 0m))]);
        return new ForceCloseImpact(
            new SalesOrderDraftForceCloseImpactResponse(version, revision, closable, token, sources, lines),
            plan,
            blockers,
            unreadable);
    }

    /// <summary>
    /// The closure's blocking rule per source (No 30 as narrowed by SAT-D12): an unreadable owner always blocks; a present
    /// active allocation or open loading blocks; a present uninvoiced dispatch note is a warning only; no source never blocks.
    /// </summary>
    private static bool ForceCloseBlocks(string source, string status) =>
        status == SalesCancellationImpactStatusesV1.Unreadable
        || (status == SalesCancellationImpactStatusesV1.Present && source != SalesCancellationImpactSourcesV1.UninvoicedDispatchNotes);

    /// <summary>
    /// The force-close preview's identity: SHA-256 (lowercase hex) of the canonical JSON of the draft, its version, revision and
    /// lifecycle and approval states, every category (status and blocking flag) and every line as shown, under its own shape
    /// name — a cancellation token is never a force-close token.
    /// </summary>
    private static string ForceCloseImpactToken(SalesOrderDraft draft, string version, string revision,
        IReadOnlyList<SalesDraftForceCloseImpactSourceV1> sources, IReadOnlyList<SalesDraftForceCloseImpactLineV1> lines)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            shape = "sales.order-draft.force-close-impact.v1",
            draftId = draft.Id,
            version,
            revision,
            state = draft.State.ToString(),
            approvalState = draft.ApprovalState.ToString(),
            pendingRemainderChange = draft.PendingRemainderChange?.Request.ChangeNo,
            sources,
            lines,
        }, ImpactJson);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
