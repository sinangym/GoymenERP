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
/// and goes on as the next revision, back in <c>Preparing</c>; the existing submit/approve flow approves it. SAT-D103 /
/// SAT-D104 / SAT-D111 (EK13): until then physical shipping goes on under the LAST APPROVED revision — the Sales line port
/// answers that revision with the shipping cap (the last approved quantity; an increase ships only after its approval; a
/// decrease waiting for approval caps at the requested quantity; a rejection restores the last approved quantity) — and the
/// new values reach no consumer.</item>
/// <item><b>The floor (SAT-D104):</b> Logistics' recorded totals of every line are read afresh through the loading port
/// (<see cref="ISalesOrderLoadingReadPortV1"/>, the cancellation's port): a line may not go below what is already SHIPPED
/// (<c>sales_revision_below_shipped</c>) nor — SAT-D112 (EK14): the loaded total is the floor — below what is loaded and
/// not yet shipped (<c>sales_revision_below_loaded</c>); both are a 422 on <c>lines[i].quantity</c>, not recorded (the floor
/// moves with Logistics). An owner that cannot answer is 503 (not recorded): no quantity changes on an unread floor. An
/// active allocation or a loading instruction NO LONGER blocks the revision (SAT-D104 replaces that reading); an active
/// allocation above a decreased quantity is the allocation owner's (Inventory) concern — Sales releases nothing, the S-04
/// form says so.</item>
/// <item><b>Ordering</b> (the cancellation's): fence → version shape → the key's replay (§4/§7; asks no owner) → the store
/// reads the draft → fence → the domain's own preconditions (version, state, reason: a stale or wrong-state command asks no
/// owner) → the loading owner (fence after it) → the floor → last fence → ONE save. Nothing is measured after the commit
/// (S03). Residual race (declared): no transaction spans Sales and Logistics — a load committed between the floor read and
/// the save is not seen by this save; the port's cap then refuses any further load above the requested quantity.</item>
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
                    var loading = await ReadLoadingAsync(scope, permission, draft, writeCancellationToken)
                        ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.RevisionImpactUnavailable);
                    RequireRevisionFloor(draft, inputs, loading);

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

    /// <summary>
    /// SAT-D104 (and the fail-closed SAT-D111 reading): every named line's new quantity against Logistics' recorded totals of
    /// that line in its order unit (absent = nothing recorded = 0). Below the shipped total → <c>sales_revision_below_shipped</c>;
    /// below the loaded (not yet shipped) total → <c>sales_revision_below_loaded</c>; refused on that line, before anything changes.
    /// An unknown line is left to the domain's own <c>sales_revision_lines_mismatch</c>.
    /// </summary>
    private static void RequireRevisionFloor(SalesOrderDraft draft, IReadOnlyList<LineQuantityInput> inputs, IReadOnlyDictionary<Guid, LineLoading> loading)
    {
        foreach (var input in inputs)
        {
            var line = input is null ? null : draft.Lines.FirstOrDefault(candidate => candidate.Id == input.LineId);
            if (input is null || line is null)
            {
                continue;
            }

            // K3-REVIEW-B P3-5: a quantity the domain refuses on its own (zero or below, above the Ö2 scale) answers with the
            // domain's code, never the floor's.
            if (input.Quantity <= 0m || !FitsScale(input.Quantity, line.Quantity.Unit))
            {
                continue;
            }

            var recorded = loading.GetValueOrDefault(input.LineId);
            if (recorded is null)
            {
                continue;
            }

            if (input.Quantity < recorded.Shipped)
            {
                throw new SalesRuleException(SalesRuleCodes.RevisionBelowShipped,
                    "Yeni miktar bu satırda sevk edilmiş miktarın altında; sevk edilmiş miktarın altına inilmez.", input.LineId);
            }

            if (input.Quantity < recorded.Loaded)
            {
                throw new SalesRuleException(SalesRuleCodes.RevisionBelowLoaded,
                    "Yeni miktar bu satırda yüklenmiş miktarın altında; önce Lojistik'te yükleme düzeltilir.", input.LineId);
            }
        }
    }

    /// <summary>
    /// SAT-D111 / SAT-D112 (EK13 / EK14), K3-REVIEW-B P1-1: on an order with an archived approved revision, the submission
    /// (from Preparing / Rejected) and the approval (from PendingApproval) read Logistics' totals afresh and refuse current
    /// quantities below the shipped or loaded total exactly like the quantity revision — 422 <c>validation_failed</c> on
    /// <c>lines[i].quantity</c> (i = the line's position in line order), NOT recorded (the floor moves with Logistics); the
    /// owner not composed or unreadable → 503 (not recorded). A stale version, another lifecycle or another approval state
    /// asks no owner: the domain refuses those itself (the key's terminal 409). Residual race (declared, README): a load
    /// committed in Logistics between this read and the Sales save is not seen — a window of the command's own duration.
    /// </summary>
    private async Task RequireReapprovalFloorAsync(Scope scope, string? permission, SalesOrderDraft current, long expectedVersion,
        string commandName, CancellationToken cancellationToken)
    {
        if (current.ArchivedRevisions.Count == 0 || current.State != SalesDraftState.Draft || current.Version != expectedVersion)
        {
            return;
        }

        var due = commandName == SalesDraftCommands.SubmitV1
            ? current.ApprovalState is InternalApprovalState.Preparing or InternalApprovalState.Rejected
            : current.ApprovalState == InternalApprovalState.PendingApproval;
        if (!due)
        {
            return;
        }

        var loading = await ReadLoadingAsync(scope, permission, current, cancellationToken)
            ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.RevisionImpactUnavailable);
        var lines = current.Lines.OrderBy(line => line.LineNo).ToArray();
        try
        {
            RequireRevisionFloor(current, [.. lines.Select(line => new LineQuantityInput(line.Id, line.QuantityText, line.Quantity.Value))], loading);
        }
        catch (SalesRuleException exception) when (exception.LineId is { } lineId)
        {
            var index = Array.FindIndex(lines, line => line.Id == lineId);
            throw new SalesDraftServiceException(422, "validation_failed", [new($"lines[{index}].quantity", exception.Code)]);
        }
    }
}
