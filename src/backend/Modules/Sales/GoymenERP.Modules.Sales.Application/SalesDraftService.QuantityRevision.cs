using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// DEC-SAL-002 (KILITLI, K0, verbatim): "Cari, ürün, miktar, fiyat, iskonto, para, vade ve risk istisnası değişirse yeni
/// revizyon ve yeniden onay gerekir." — "Rev.0 korunur; iç not/etiket açıklaması yeniden onay istemeyebilir." AT-023:
/// "Onaylı siparişte kritik satır değişikliği eski onayı geçersizleştirir."
/// <list type="bullet">
/// <item><b>What this slice revises:</b> the ORDERED QUANTITY of every line (the price and the discount already have their
/// K3 commercial revision). Party, product, currency, payment term and risk exception are not revised here: the draft does
/// not carry a payment term (no owner port) or a risk exception (not modelled; OPEN-010 is KISMEN), a currency change needs a
/// second approved money profile and a restated price, and a party or product change re-binds the account, the address,
/// the contact and the product snapshot — each its own decision, never guessed (README "Quantity revision").</item>
/// <item><b>Re-approval:</b> an APPROVED order is archived first (its approval facts, its lines as they were and the reason)
/// and goes on as the next revision, back in <c>Preparing</c>; the existing submit/approve flow approves it. Until then the
/// Sales line port answers the order's lines <c>NotApproved</c>, so no owner acts on unapproved figures.</item>
/// <item><b>Fail-closed downstream:</b> the owners that can link a record to an order line are read afresh through their
/// ports (Inventory's ACTIVE allocations, Logistics' loading-instruction lines — the cancellation's ports). Any such record
/// refuses the revision (409 <c>sales_revision_blocked</c>, recorded), an owner that cannot answer is 503 (not recorded):
/// Sales releases nothing — the user resolves the owner's record in its own screen first, the cancellation's rule.</item>
/// <item><b>Ordering</b> (the cancellation's): fence → version shape → the key's replay (§4/§7; asks no owner) → the store
/// reads the draft → fence → the domain's own preconditions (version, state, reason: a stale or wrong-state command asks no
/// owner) → each owner (fence after each) → blockers → last fence → ONE save. Nothing is measured after the commit (S03).
/// Residual race (declared): no transaction spans Sales and the owners.</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService
{
    public async Task<SalesOrderDraftResponse> ReviseQuantityAsync(Guid draftId, SalesDraftQuantityRevisionRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();

        var permission = bindings.QuantityPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (!long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion) || expectedVersion < 1)
        {
            throw new SalesDraftServiceException(400, "validation_failed", [new("expectedVersion", "validation_failed")]);
        }

        const string commandName = SalesDraftCommands.ReviseQuantityV1;
        var requestHash = SalesDraftRequestHash.Compute(commandName, draftId, request);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            // The same key, content and actor: the stored result again — the owners are not asked, nothing changes twice.
            return await ReplayedAsync(scope, replay, cancellationToken);
        }

        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var inputs = new LineQuantityInput[request.Lines.Count];
        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            inputs[index] = new LineQuantityInput(line.LineId, line.Quantity, LineQuantity(line.Quantity, index));
        }

        var audit = new SalesDraftStateAudit("sales.order-draft.quantity-revised.v1", "ReviseQuantity",
            "Satış siparişinin satır miktarları gerekçeyle revize edildi (DEC-SAL-002; onaylı sipariş yeniden onaya girer)");

        SalesDraftStateResult? result;
        try
        {
            result = await store.ReviseDraftQuantityAsync(envelope, requestHash, commandName, draftId,
                (draft, now) => draft.ReviseQuantities(expectedVersion, inputs, scope.ActorId, request.Reason, now), audit,
                async (draft, writeCancellationToken) =>
                {
                    scope = await RequireStillAsync(scope, permission, writeCancellationToken);
                    // The domain's own preconditions first: a stale, cancelled, pending or withdrawn draft asks no owner.
                    draft.RequireQuantityRevisable(expectedVersion, request.Reason);
                    var links = await ReadRevisionLinksAsync(scope, permission, draft, writeCancellationToken);
                    if (links.Unreadable)
                    {
                        throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.RevisionImpactUnavailable);
                    }

                    if (links.Blockers.Count > 0)
                    {
                        throw new RevisionBlocked(links.Blockers);
                    }

                    // The last fence that can still prevent the write; none after the commit (ORG #321 P3-1).
                    scope = await RequireStillAsync(scope, permission, writeCancellationToken);
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
                [new("approvalState", exception.Code)]), cancellationToken, commandName, draftId);
        }
        catch (RevisionBlocked blocked)
        {
            throw await TerminalAsync(scope, permission, envelope, requestHash, new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.RevisionBlocked,
                [.. blocked.Sources.Select(source => new SalesDraftFieldError(source, SalesCancellationImpactStatusesV1.Present))]),
                cancellationToken, commandName, draftId);
        }
        catch (SalesRuleException exception) when (exception.Code is SalesRuleCodes.RevisionReasonRequired or SalesRuleCodes.RevisionReasonTooLong)
        {
            throw new SalesDraftServiceException(422, "validation_failed", [new("reason", exception.Code)]);
        }
        catch (SalesRuleException exception)
        {
            throw new SalesDraftServiceException(422, "validation_failed", [new(QuantityRevisionField(request, exception), exception.Code)]);
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

    /// <summary>The line's quantity text as the domain's exact value (the create's Ö2 lexical rule), refused on <c>lines[i].quantity</c>.</summary>
    private static decimal LineQuantity(string text, int index)
    {
        try
        {
            return Decimal(text);
        }
        catch (SalesDraftServiceException exception)
        {
            throw new SalesDraftServiceException(exception.Status, exception.Code,
                [.. exception.Errors.Select(error => error with { Field = $"lines[{index}].{error.Field}" })]);
        }
    }

    /// <summary>The field a quantity refusal is answered on: <c>lines[i].quantity</c> for a line's rule (i = its index in THIS request), else <c>lines</c>.</summary>
    private static string QuantityRevisionField(SalesDraftQuantityRevisionRequest request, SalesRuleException exception)
    {
        if (exception.LineId is not { } lineId)
        {
            return "lines";
        }

        var index = request.Lines.ToList().FindIndex(line => line.LineId == lineId);
        return index < 0 ? "lines" : $"lines[{index}].quantity";
    }

    private sealed class RevisionBlocked(IReadOnlyList<string> sources) : Exception
    {
        public IReadOnlyList<string> Sources { get; } = sources;
    }

    private sealed record RevisionLinks(IReadOnlyList<string> Blockers, bool Unreadable);

    /// <summary>
    /// The owners' links to the order, read afresh through the cancellation's ports under the quantity name (measured after
    /// each): an active allocation or ANY loading-instruction line of ANY line of the order blocks — the whole order re-enters
    /// approval, and a record keyed on the approved revision would otherwise stand beside figures nobody approved. An owner
    /// not composed, failing or malformed is unreadable (never an assumed "none").
    /// </summary>
    private async Task<RevisionLinks> ReadRevisionLinksAsync(Scope scope, string? permission, SalesOrderDraft draft,
        CancellationToken cancellationToken)
    {
        var allocationsByLine = await ReadAllocationsAsync(scope, permission, draft, cancellationToken);
        var loadingByLine = await ReadLoadingAsync(scope, permission, draft, cancellationToken);
        if (allocationsByLine is null || loadingByLine is null)
        {
            return new RevisionLinks([], Unreadable: true);
        }

        var blockers = new List<string>(2);
        if (allocationsByLine.Values.Any(line => line.Count > 0))
        {
            blockers.Add(SalesCancellationImpactSourcesV1.ActiveAllocations);
        }

        if (loadingByLine.Values.Any(line => line.Count > 0 || line.Instructed > 0m))
        {
            blockers.Add(SalesCancellationImpactSourcesV1.LoadingWithoutDispatchNote);
        }

        return new RevisionLinks(blockers, Unreadable: false);
    }
}
