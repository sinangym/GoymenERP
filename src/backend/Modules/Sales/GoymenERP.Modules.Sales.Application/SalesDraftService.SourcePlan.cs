using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// SAT-D251..SAT-D255 (EK50) / API-11: the order source plan. The read is under the draft read (<c>sales.drafts.read</c>; no new
/// read name is invented, EK50 "Uygulama listesi"); the save is under <c>sales.orders.source-plan.write</c> (SAT-D179: a grant
/// decision and a human gate; no role name here).
/// <list type="bullet">
/// <item><b>Stock is derived (SAT-D255):</b> the stock part of a line is the line's active Inventory allocation
/// (<see cref="ISalesOrderAllocationReadPortV1"/>, kilograms); it is never entered and never reserved here. An unreadable allocation
/// is shown as <c>Unreadable</c> ("okunamadı", never zero) and refuses the save (503
/// <see cref="SalesDraftProblemCodesV1.SourcePlanAllocationUnavailable"/>). A recorded plan whose stock part now differs from the
/// allocation is shown with <see cref="SalesSourcePlanWarningsV1.PlanAllocationMismatch"/> — never rewritten by itself.</item>
/// <item><b>Locked part (SAT-D253 (3)):</b> the delivery owner's SHIPPED total of the line (<see cref="ISalesOrderDeliveryReadPortV1"/>;
/// shipped is at or above delivered, so this is the stricter reading — NEEDS-SINAN whether "teslim edilen" means shipped or
/// delivered). It is stored apart and attributed to no source (NEEDS-SINAN, reviewer note (b): which source a delivered part was
/// met from is undecided; fail-closed: no attribution is invented). A fully delivered line refuses (recorded 409
/// <see cref="SalesDraftProblemCodesV1.SourcePlanLineLocked"/>); an unreadable delivery owner refuses (503).</item>
/// <item><b>The ONE equality (SAT-D252, reviewer note (a)):</b> locked + stock + production + purchasing + subcontract == line
/// quantity (<see cref="SourcePlanCalculator.IsBalanced"/>); otherwise 422 <see cref="SalesDraftProblemCodesV1.SourcePlanSumMismatch"/>.</item>
/// <item><b>Linked supply (SAT-D254):</b> every kind of <see cref="ISalesOrderLinkedSupplyReadPortV1"/> is read afresh; an open item
/// on a planned line (or on the whole order) needs <c>confirmLinkedSupply</c> (409
/// <see cref="SalesDraftProblemCodesV1.SourcePlanLinkedSupplyUnconfirmed"/>, not recorded); the confirmation and the items it
/// covered go into the audit. An unreadable kind refuses (503); NoSource warns nothing. Sales changes no linked document.</item>
/// <item><b>Suggestion (SAT-D251):</b> <see cref="SourcePlanCalculator.Suggest"/> with the line's own allocation subtracted from the
/// need (reviewer note (c)). No free-stock port exists on main, so a need not covered by the line's own allocation is
/// "kullanılamıyor" (<see cref="SalesSourcePlanSuggestionReasonsV1.FreeStockUnavailable"/>); the product-type mapping is
/// NEEDS-SINAN.</item>
/// <item><b>Kilograms only (NEEDS-SINAN):</b> the allocation answers kilograms; a line in another unit is not converted — its
/// stock part is "okunamadı" and its save is 422 <see cref="SalesDraftProblemCodesV1.SourcePlanUnitUnsupported"/>.</item>
/// <item><b>Ordering</b> (the line closure's): fence → version shape → parts shape → the key's replay → the store reads the draft →
/// fence → the domain's preconditions → delivery → allocation → linked supply (fence after each owner) → confirmation → the
/// equality → last fence → ONE save (plan rows + header version + attempt + audit).</item>
/// <item><b>Cross-module race (documented):</b> the owners are read before the single save and are not locked across modules (no new
/// port); a later allocation or shipment is shown as a mismatch warning on the next read, and the plan reserves nothing, so the
/// race creates no stock or money event.</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService
{
    public async Task<SalesOrderDraftSourcePlanResponse> GetSourcePlanAsync(Guid draftId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.ReadPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        SalesOrderDraft? draft;
        IReadOnlyList<SourcePlanLineFacts> plan;
        try
        {
            draft = await GuardedAsync(scope, permission, () => store.FindDraftAsync(draftId, cancellationToken), cancellationToken);
            if (draft is null)
            {
                throw new SalesDraftServiceException(404, "not_found");
            }

            plan = await GuardedAsync(scope, permission, () => store.FindSourcePlanAsync(draftId, cancellationToken), cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        var shipments = await RecordedShipmentsAsync(scope, permission, draft, cancellationToken);
        var allocationsByLine = await ReadAllocationsAsync(scope, permission, draft, cancellationToken);
        var linked = new List<(string Kind, LinkedSupplyRead Read)>();
        foreach (var kind in SalesLinkedSupplyKindsV1.All)
        {
            linked.Add((kind, await ReadLinkedSupplyAsync(scope, permission, draft, kind, cancellationToken)));
        }

        var linkedUnreadable = linked.Any(entry => entry.Read.Status == SalesCancellationImpactStatusesV1.Unreadable);
        var orderWritable = true;
        try
        {
            draft.RequireSourcePlanWritable(draft.Version);
        }
        catch (SalesRuleException)
        {
            orderWritable = false;
        }

        var writable = orderWritable && !linkedUnreadable;
        var savedByLine = plan.GroupBy(fact => fact.LineId).ToDictionary(group => group.Key,
            group => group.OrderByDescending(fact => fact.DraftVersion).First());
        var lines = draft.Lines.OrderBy(line => line.LineNo).Select(line => SourcePlanLine(draft, line, shipments, allocationsByLine,
            linked, linkedUnreadable, orderWritable, writable, savedByLine.GetValueOrDefault(line.Id))).ToArray();

        return new SalesOrderDraftSourcePlanResponse(draft.Version.ToString(CultureInfo.InvariantCulture),
            SalesDraftApiV1.RevisionText(draft.Revision), writable,
            [.. linked.Select(entry => new SalesDraftSourcePlanLinkedSupplyV1(entry.Kind, entry.Read.Status))], lines);
    }

    private static SalesDraftSourcePlanLineV1 SourcePlanLine(SalesOrderDraft draft, SalesOrderDraftLine line,
        Dictionary<Guid, (Quantity Shipped, Quantity Delivered)>? shipments, Dictionary<Guid, LineAllocation>? allocationsByLine,
        IReadOnlyList<(string Kind, LinkedSupplyRead Read)> linked, bool linkedUnreadable, bool orderWritable, bool writable,
        SourcePlanLineFacts? saved)
    {
        var unit = line.Quantity.Unit;
        var quantity = line.Quantity.Value;
        var kilogram = line.OrderUnit.IsKilogram && unit == QuantityUnit.Kilogram;
        decimal? shipped = shipments is null ? null : shipments[line.Id].Shipped.Value;
        decimal? allocated = !kilogram || allocationsByLine is null
            ? null
            : allocationsByLine.GetValueOrDefault(line.Id)?.RemainingKg ?? 0m;
        var suggestion = kilogram
            ? SourcePlanCalculator.Suggest(quantity, shipped, allocated, freeStock: null)
            : new SourcePlanSuggestionResult(SourcePlanSuggestion.Unavailable, SourcePlanSuggestionReason.UnitUnsupported);

        string? lockReason = !orderWritable ? SalesSourcePlanLockReasonsV1.OrderState
            : draft.IsLineClosed(line.Id) ? SalesSourcePlanLockReasonsV1.LineClosed
            : !kilogram ? SalesSourcePlanLockReasonsV1.UnitUnsupported
            : shipped is null ? SalesSourcePlanLockReasonsV1.DeliveryUnreadable
            : shipped >= quantity ? SalesSourcePlanLockReasonsV1.LineDelivered
            : allocated is null ? SalesSourcePlanLockReasonsV1.AllocationUnreadable
            : null;

        var warnings = new List<string>();
        if (saved is null)
        {
            warnings.Add(SalesSourcePlanWarningsV1.NoPlan);
        }
        else
        {
            if (allocated is { } stock && saved.Stock != stock)
            {
                warnings.Add(SalesSourcePlanWarningsV1.PlanAllocationMismatch);
            }

            if (shipped is { } locked && saved.Locked != locked)
            {
                warnings.Add(SalesSourcePlanWarningsV1.PlanDeliveryMismatch);
            }

            if (saved.LineQuantity != quantity)
            {
                warnings.Add(SalesSourcePlanWarningsV1.PlanQuantityMismatch);
            }
        }

        if (linked.Any(entry => entry.Read.OpenItems.Any(item => item.OrderLineId is null || item.OrderLineId == line.Id)))
        {
            warnings.Add(SalesSourcePlanWarningsV1.LinkedSupplyOpen);
        }

        if (linkedUnreadable)
        {
            warnings.Add(SalesSourcePlanWarningsV1.LinkedSupplyUnreadable);
        }

        if (shipments is null)
        {
            warnings.Add(SalesSourcePlanWarningsV1.DeliveryUnreadable);
        }

        if (!kilogram)
        {
            warnings.Add(SalesSourcePlanWarningsV1.UnitUnsupported);
        }
        else if (allocationsByLine is null)
        {
            warnings.Add(SalesSourcePlanWarningsV1.AllocationUnreadable);
        }

        return new SalesDraftSourcePlanLineV1(line.Id, line.LineNo, line.OrderUnit.UnitId, line.QuantityText,
            shipped is { } shippedValue ? Text(shippedValue, unit) : null,
            shipped is { } doneValue ? Text(Math.Max(quantity - doneValue, 0m), unit) : null,
            allocated is null ? SalesSourcePlanStockStatusesV1.Unreadable : SalesSourcePlanStockStatusesV1.Derived,
            allocated is { } allocatedValue ? Text(allocatedValue, QuantityUnit.Kilogram) : null,
            suggestion.Suggestion.ToString(), suggestion.Reason.ToString(),
            saved is null ? null : new SalesDraftSourcePlanSavedV1(saved.DraftVersion.ToString(CultureInfo.InvariantCulture),
                Text(saved.LineQuantity, QuantityUnit.Kilogram), Text(saved.Locked, QuantityUnit.Kilogram),
                Text(saved.Stock, QuantityUnit.Kilogram), Text(saved.Production, QuantityUnit.Kilogram),
                Text(saved.Purchasing, QuantityUnit.Kilogram), Text(saved.Subcontract, QuantityUnit.Kilogram),
                saved.LinkedSupplyConfirmed, saved.SavedBy, saved.SavedAtUtc),
            writable && lockReason is null, lockReason, warnings);
    }

    public async Task<SalesOrderDraftResponse> RecordSourcePlanAsync(Guid draftId, SalesDraftSourcePlanRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();

        var permission = bindings.SourcePlanPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (!long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion) || expectedVersion < 1)
        {
            throw new SalesDraftServiceException(400, "validation_failed", [new("expectedVersion", "validation_failed")]);
        }

        var parts = SourcePlanParts(request);
        const string commandName = SalesDraftCommands.RecordSourcePlanV1;
        var requestHash = SalesDraftRequestHash.Compute(commandName, draftId, request);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            // The same key, content and actor: the stored result again — no owner is asked, nothing is recorded twice.
            return await ReplayedAsync(scope, replay, cancellationToken);
        }

        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var audit = new SalesDraftStateAudit("sales.order-draft.source-plan-recorded.v1", "RecordSourcePlan",
            "Sipariş kaynak planı kaydedildi; stok kısmı tahsisten türetildi, revizyon açılmadı (SAT-D252..SAT-D255)");

        SalesDraftStateResult? result;
        try
        {
            result = await store.RecordSourcePlanAsync(envelope, requestHash, commandName, draftId,
                (draft, input, now) => draft.RecordSourcePlan(expectedVersion, scope.ActorId, input, now), audit,
                async (draft, writeCancellationToken) =>
                {
                    scope = await RequireStillAsync(scope, permission, writeCancellationToken);
                    // The domain's own preconditions first: a stale or terminal order, an unknown, closed or non-kilogram line asks
                    // no owner.
                    draft.RequireSourcePlanWritable(expectedVersion);
                    var planned = new List<SalesOrderDraftLine>(parts.Count);
                    for (var index = 0; index < parts.Count; index++)
                    {
                        planned.Add(SourcePlanLineOrRefuse(draft, parts[index].LineId, index));
                    }

                    var input = await SourcePlanInputAsync(scope, permission, draft, planned, parts, request.ConfirmLinkedSupply,
                        writeCancellationToken);
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
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.SourcePlanLineLocked)
        {
            throw await TerminalAsync(scope, permission, envelope, requestHash, new SalesDraftTerminalRefusal(409,
                SalesDraftProblemCodesV1.SourcePlanLineLocked, [new(LineField(parts, exception.LineId), SalesDraftProblemCodesV1.SourcePlanLineLocked)]),
                cancellationToken, commandName, draftId);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.SourcePlanSumMismatch)
        {
            // Defence in depth: the service measures the equality first (with the line's field); not recorded.
            throw new SalesDraftServiceException(422, SalesDraftProblemCodesV1.SourcePlanSumMismatch,
                [new(LineField(parts, exception.LineId), SalesDraftProblemCodesV1.SourcePlanSumMismatch)]);
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

    /// <summary>The entered parts parsed as exact non-negative kilograms (shape refusals are 400 on <c>lines[i]</c>).</summary>
    private static IReadOnlyList<SourcePlanLineInput> SourcePlanParts(SalesDraftSourcePlanRequest request)
    {
        if (request.Lines is null || request.Lines.Count == 0 || request.Lines.Count > SalesDraftSourcePlanJson.MaximumLines)
        {
            throw new SalesDraftServiceException(400, "validation_failed", [new("lines", "validation_failed")]);
        }

        var seen = new HashSet<Guid>();
        var parts = new List<SourcePlanLineInput>(request.Lines.Count);
        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            if (line is null || line.LineId == Guid.Empty || !seen.Add(line.LineId)
                || !TryPart(line.Production, out var production) || !TryPart(line.Purchasing, out var purchasing)
                || !TryPart(line.Subcontract, out var subcontract))
            {
                throw new SalesDraftServiceException(400, "validation_failed", [new(LinesField(index), "validation_failed")]);
            }

            parts.Add(new SourcePlanLineInput(line.LineId, 0m, 0m, production, purchasing, subcontract));
        }

        return parts;
    }

    private static bool TryPart(string? text, out decimal value)
    {
        value = default;
        if (string.IsNullOrEmpty(text) || text.Length > 32
            || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        try
        {
            value = Quantity.Of(value, QuantityUnit.Kilogram).Value;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static SalesOrderDraftLine SourcePlanLineOrRefuse(SalesOrderDraft draft, Guid lineId, int index)
    {
        try
        {
            return draft.RequireSourcePlanLine(lineId);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.SourcePlanLineUnknown)
        {
            // Not recorded: the same key may name the order's line next time.
            throw new SalesDraftServiceException(422, "validation_failed", [new(LinesField(index), exception.Code)]);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.SourcePlanUnitUnsupported)
        {
            throw new SalesDraftServiceException(422, SalesDraftProblemCodesV1.SourcePlanUnitUnsupported,
                [new(LinesField(index), SalesDraftProblemCodesV1.SourcePlanUnitUnsupported)]);
        }
    }

    /// <summary>
    /// The plan read afresh at the command: the locked part (shipped total), the stock part (active allocation), the linked
    /// supply and its confirmation, and the ONE equality per line. Unreadable owners are 503 (not recorded), never a guess.
    /// </summary>
    private async Task<SalesSourcePlanWriteInput> SourcePlanInputAsync(Scope scope, string? permission, SalesOrderDraft draft,
        IReadOnlyList<SalesOrderDraftLine> planned, IReadOnlyList<SourcePlanLineInput> parts, bool confirmLinkedSupply,
        CancellationToken cancellationToken)
    {
        var shipments = await RecordedShipmentsAsync(scope, permission, draft, cancellationToken)
            ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.DeliveryProviderUnavailable);
        for (var index = 0; index < planned.Count; index++)
        {
            if (!planned[index].Quantity.IsGreaterThan(shipments[planned[index].Id].Shipped))
            {
                // SAT-D253 (3): a fully delivered line's plan no longer changes. Terminal for the key (recorded, replayed).
                throw new SalesRuleException(SalesRuleCodes.SourcePlanLineLocked, "Tamamen teslim edilmiş satırın kaynak planı değiştirilmez.",
                    planned[index].Id);
            }
        }

        var allocationsByLine = await ReadAllocationsAsync(scope, permission, draft, cancellationToken)
            ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.SourcePlanAllocationUnavailable);

        var plannedIds = planned.Select(line => line.Id).ToHashSet();
        var linkedItems = new List<SalesSourcePlanLinkedItem>();
        foreach (var kind in SalesLinkedSupplyKindsV1.All)
        {
            var read = await ReadLinkedSupplyAsync(scope, permission, draft, kind, cancellationToken);
            if (read.Status == SalesCancellationImpactStatusesV1.Unreadable)
            {
                // SAT-D254: nothing is confirmed without knowing what is linked (fail-closed).
                throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.SourcePlanLinkedSupplyUnavailable);
            }

            linkedItems.AddRange(read.OpenItems
                .Where(item => item.OrderLineId is null || plannedIds.Contains(item.OrderLineId.Value))
                .Select(item => new SalesSourcePlanLinkedItem(kind, item.DocumentType, item.SupplyId, item.SupplyLineId, item.OrderLineId)));
        }

        if (linkedItems.Count > 0 && !confirmLinkedSupply)
        {
            // SAT-D254: the warning is confirmed explicitly; not recorded — the user confirms and saves again under the same key.
            throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.SourcePlanLinkedSupplyUnconfirmed,
                [new("confirmLinkedSupply", SalesDraftProblemCodesV1.SourcePlanLinkedSupplyUnconfirmed)]);
        }

        var lines = new List<SourcePlanLineInput>(planned.Count);
        for (var index = 0; index < planned.Count; index++)
        {
            var line = planned[index];
            var locked = shipments[line.Id].Shipped.Value;
            var stock = allocationsByLine.GetValueOrDefault(line.Id)?.RemainingKg ?? 0m;
            var entry = parts[index] with { Locked = locked, Stock = stock };
            if (!SourcePlanCalculator.IsBalanced(line.Quantity.Value, entry.Locked, entry.Stock, entry.Production, entry.Purchasing,
                    entry.Subcontract))
            {
                throw new SalesDraftServiceException(422, SalesDraftProblemCodesV1.SourcePlanSumMismatch,
                    [new(LinesField(index), SalesDraftProblemCodesV1.SourcePlanSumMismatch)]);
            }

            lines.Add(entry);
        }

        // SAT-D254: the confirmation is recorded only when there was something to confirm.
        return new SalesSourcePlanWriteInput(new SourcePlanInput(lines, linkedItems.Count > 0 && confirmLinkedSupply), linkedItems);
    }

    private static string LinesField(int index) => $"lines[{index.ToString(CultureInfo.InvariantCulture)}]";

    private static string LineField(IReadOnlyList<SourcePlanLineInput> parts, Guid? lineId)
    {
        for (var index = 0; index < parts.Count; index++)
        {
            if (parts[index].LineId == lineId)
            {
                return LinesField(index);
            }
        }

        return "lines";
    }
}
