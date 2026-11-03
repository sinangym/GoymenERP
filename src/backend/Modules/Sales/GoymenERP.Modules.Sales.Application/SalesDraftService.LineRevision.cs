using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// SAT-D167 (EK24): the line revision — add a line, remove an unshipped line (it goes passive, never deleted) or change an unshipped
/// line's product (a removal replaced by an added line), with a reason, under the quantity revision's name
/// <c>sales.orders.quantity.write</c> (the order's line-change name; NEEDS-SINAN whether a separate name is wanted). DEC-SAL-002: an
/// APPROVED order is archived and goes on as the next revision, back to internal approval and a new customer acceptance.
/// <list type="bullet">
/// <item><b>Unshipped (SAT-D167's reading):</b> no stock allocation (Inventory) and no loading-instruction line (Logistics — every
/// dispatch note and shipment hangs on one) for the line, read afresh from the owners; any → 422 <c>removed[i].lineId</c>
/// <see cref="SalesRuleCodes.LineRevisionLineShipped"/> (not recorded: the owners move); an owner not composed or unreadable → 503
/// <see cref="SalesDraftProblemCodesV1.RevisionImpactUnavailable"/> (fail-closed).</item>
/// <item><b>Added lines:</b> the create's line rules and owners — SAT-D212 (EK36): the Product eligibility, the Settings unit
/// profile and the line tax as of the REVISION DAY (the server's Europe/Istanbul business day the line is added), not the order's
/// business date; existing lines keep theirs; a price, discount or tax change needs the price-write name (SAT-D151); the tax from the Settings line
/// tax port and lists (SAT-D93/D181) for the order's buyer situation; on a subcontract order only service products (the Product
/// owner's item type), sold per kg, never from the price list and with no discount check (SAT-D176/D195). A replacement line's
/// price, discount and tax are its own, i.e. re-resolved for the new product (SAT-D167).</item>
/// <item><b>Ordering</b> (the quantity revision's): fence → version shape → price fence → the key's replay → the store reads the
/// draft → fence → the domain's preconditions → the owners (fence after each) → last fence → ONE save.</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService
{
    public async Task<SalesOrderDraftResponse> ReviseLinesAsync(Guid draftId, SalesDraftLineRevisionRequest request,
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

        var added = request.Added ?? [];
        var removed = request.Removed ?? [];
        // SAT-D212: the day an added line is read for — the server's business day now (an unresolvable zone is 503, never UTC).
        // A removal alone reads nothing dated.
        var revisionDay = added.Count > 0 ? ServerBusinessDay() : default;

        // SAT-D151: a price, a discount or a tax change on an added line needs the separate price-write name of the same actor.
        var priced = added.SelectMany((line, index) => PricedFields(line, $"added[{index}]")).ToList();
        if (priced.Count > 0)
        {
            await RequirePriceWriteAsync(scope, priced, cancellationToken);
        }

        const string commandName = SalesDraftCommands.ReviseLinesV1;
        var requestHash = SalesDraftRequestHash.Compute(commandName, draftId, request);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            return await ReplayedAsync(scope, replay, cancellationToken);
        }

        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var audit = new SalesDraftStateAudit("sales.order-draft.lines-revised.v1", "ReviseLines",
            "Satış siparişine satır eklendi, satır pasif yapıldı ya da ürünü değişti (SAT-D167; onaylı sipariş yeniden onaya girer)");
        var inputs = new List<SalesDraftLineInput>(added.Count);
        var subcontract = false;
        SalesDraftStateResult? result;
        try
        {
            result = await store.ReviseDraftLinesAsync(envelope, requestHash, commandName, draftId,
                (draft, now) => draft.ReviseLines(expectedVersion,
                    [.. inputs.Select(input => input.Commercial is { } commercial && !subcontract
                        ? input with { Commercial = SalesOrderDraft.WithDiscountLimitCheck(commercial, SalesDiscountLimit.Undefined, scope.ActorId, now) }
                        : input)],
                    [.. removed.Select(removal => new LineRemovalInput(removal.LineId, removal.ReplacedByLineId))],
                    scope.ActorId, request.Reason, now, revisionDay), audit,
                async (draft, writeCancellationToken) =>
                {
                    scope = await RequireStillAsync(scope, permission, writeCancellationToken);
                    // The domain's own preconditions first: a stale, cancelled, pending or withdrawn order asks no owner.
                    draft.RequireQuantityRevisable(expectedVersion, request.Reason);
                    subcontract = draft.IsSubcontractService;
                    if (removed.Count > 0)
                    {
                        await RequireRemovedLinesUnshippedAsync(scope, permission, draft, removed, writeCancellationToken);
                    }

                    inputs.AddRange(await ReadAddedLinesAsync(scope, permission, draft, added, revisionDay, envelope, requestHash,
                        commandName, writeCancellationToken));
                    // The last fence that can still prevent the write; none after the commit (ORG #321 P3-1).
                    scope = await RequireStillAsync(scope, permission, writeCancellationToken);
                    if (priced.Count > 0)
                    {
                        await RequirePriceWriteAsync(scope, priced, writeCancellationToken);
                    }
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
            throw new SalesDraftServiceException(422, "validation_failed", [new(LineRevisionField(request, exception), exception.Code)]);
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

    /// <summary>The price-write fields of one added line: its unit price, price unit, discount or tax change.</summary>
    private static IEnumerable<SalesDraftFieldError> PricedFields(SalesOrderDraftLineRequest line, string prefix)
    {
        if (line.UnitPrice is not null || line.PriceUnitId is not null)
        {
            yield return new($"{prefix}.unitPrice", SalesDraftProblemCodesV1.PriceWriteForbidden);
        }

        if (line.DiscountPercent is not null)
        {
            yield return new($"{prefix}.discountPercent", SalesDraftProblemCodesV1.PriceWriteForbidden);
        }

        if (line.TaxOverride is not null)
        {
            yield return new($"{prefix}.taxOverride", SalesDraftProblemCodesV1.PriceWriteForbidden);
        }
    }

    /// <summary>
    /// SAT-D167: every line to remove (or re-product) must be unshipped — no active allocation and no loading-instruction line, read
    /// afresh from both owners. Unreadable → 503; a linked line → 422 on <c>removed[i].lineId</c>. An id that is not an active line
    /// of the order is left to the domain (<c>sales_revision_lines_mismatch</c>).
    /// </summary>
    private async Task RequireRemovedLinesUnshippedAsync(Scope scope, string? permission, SalesOrderDraft draft,
        IReadOnlyList<SalesDraftLineRemovalV1> removed, CancellationToken cancellationToken, string fieldPrefix = "removed")
    {
        var allocated = await ReadAllocationsAsync(scope, permission, draft, cancellationToken)
            ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.RevisionImpactUnavailable);
        var loaded = await ReadLoadingAsync(scope, permission, draft, cancellationToken)
            ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.RevisionImpactUnavailable);
        var errors = new List<SalesDraftFieldError>();
        for (var index = 0; index < removed.Count; index++)
        {
            var lineId = removed[index].LineId;
            var allocation = allocated.GetValueOrDefault(lineId);
            var loading = loaded.GetValueOrDefault(lineId);
            if ((allocation is not null && (allocation.Count > 0 || allocation.RemainingKg > 0m))
                || (loading is not null && (loading.Count > 0 || loading.Instructed > 0m || loading.Shipped > 0m)))
            {
                errors.Add(new($"{fieldPrefix}[{index}].lineId", SalesRuleCodes.LineRevisionLineShipped));
            }
        }

        if (errors.Count > 0)
        {
            throw new SalesDraftServiceException(422, "validation_failed", errors);
        }
    }

    /// <summary>
    /// The added lines as the domain takes them: each line's product and order unit from their owners on the revision day (SAT-D212),
    /// its commercial fields and plan (the create's checks), on a subcontract order the Product owner's item type (only services),
    /// and its tax from the Settings line tax port for the order's buyer situation (a reasoned change checked against the lists).
    /// Field errors name <c>added[i]</c>.
    /// </summary>
    private async Task<List<SalesDraftLineInput>> ReadAddedLinesAsync(Scope scope, string? permission, SalesOrderDraft draft,
        IReadOnlyList<SalesOrderDraftLineRequest> added, DateOnly revisionDay, SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, CancellationToken cancellationToken)
    {
        var inputs = new List<SalesDraftLineInput>(added.Count);
        if (added.Count == 0)
        {
            return inputs;
        }

        // SAT-D212 (EK36): every dated read of an added line is for the revision day.
        var businessDate = revisionDay;
        for (var index = 0; index < added.Count; index++)
        {
            var line = added[index];
            var at = index;
            await AsAddedAsync(index, async () =>
            {
                var (product, orderUnit) = await ReadProductUnitAsync(scope, permission, at, line.ProductId, line.ExpectedProductVersion,
                    line.OrderUnitId, businessDate,
                    refusal => TerminalAsync(scope, permission, envelope, requestHash,
                        refusal with { Errors = [.. refusal.Errors.Select(error => error with { Field = AddedField(error.Field, at) })] },
                        cancellationToken, commandName, draft.Id),
                    cancellationToken);
                var slices = line.RequestedDeliverySlices?
                    .Select(slice => new DeliverySliceInput(slice.SliceId, slice.OrderUnitId, slice.Quantity, Decimal(slice.Quantity),
                        slice.RequestedDeliveryDate))
                    .ToArray();
                inputs.Add(new SalesDraftLineInput(line.LineId, product, orderUnit, line.Quantity, LineQuantity(line.Quantity, at),
                    line.RequestedDeliveryDate, line.Note, Commercial: Commercial(line, at, product, draft.Money), RequestedDeliverySlices: slices));
            });
        }

        if (draft.IsSubcontractService)
        {
            var confirmed = await AsAddedAsync(-1, () => ConfirmServiceLinesAsync(scope, permission, inputs, cancellationToken));
            inputs = confirmed;
        }

        // MOD-03 → Sales (SAT-D93/D181): each added line's tax for the order's buyer situation (or its reasoned change from the lists).
        var buyer = draft.Header.BuyerTax ?? SalesBuyerTax.Create(StartBuyerSituation);
        if (lineTaxes is null)
        {
            if (added.Any(line => line.TaxOverride is not null))
            {
                throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.TaxProviderUnavailable);
            }

            return inputs;
        }

        var lists = new TaxLists(businessDate);
        var now = timeProvider.GetUtcNow();
        for (var index = 0; index < added.Count; index++)
        {
            var line = added[index];
            var change = line.TaxOverride is { } requested
                ? await RequireLineTaxChangeAsync(scope, permission, requested.ItemType, requested.BuyerSituation, requested.ExemptionCode,
                    requested.Reason, lists, $"added[{index}].taxOverride", now, cancellationToken, requested.VatRateCode,
                    requested.WithholdingRatioCode)
                : null;
            var tax = await ResolveLineTaxAsync(scope, permission, line.ProductId, buyer, change, businessDate, cancellationToken, lists,
                $"added[{index}].taxOverride");
            inputs[index] = inputs[index] with { Tax = tax };
        }

        return inputs;
    }

    /// <summary>Runs a create-shaped read for added line <paramref name="index"/> and answers its field errors on <c>added[i]</c>.</summary>
    private static async Task AsAddedAsync(int index, Func<Task> read) =>
        await AsAddedAsync(index, async () => { await read(); return true; });

    private static async Task<T> AsAddedAsync<T>(int index, Func<Task<T>> read)
    {
        try
        {
            return await read();
        }
        catch (SalesDraftServiceException exception) when (exception.Errors.Count > 0)
        {
            throw new SalesDraftServiceException(exception.Status, exception.Code,
                [.. exception.Errors.Select(error => error with { Field = AddedField(error.Field, index) })], exception.IsReplay,
                exception.OriginalCorrelationId);
        }
    }

    /// <summary>The create's field names (<c>lines[i].x</c>, or a bare <c>quantity</c>) as an added line's (<c>added[i].x</c>).</summary>
    private static string AddedField(string field, int index) =>
        field.StartsWith("lines[", StringComparison.Ordinal) ? "added" + field["lines".Length..]
        : index >= 0 && !field.StartsWith("added[", StringComparison.Ordinal) ? $"added[{index}].{field}"
        : field;

    /// <summary>A domain refusal's field: the added (<c>added[i]</c>) or removed (<c>removed[i].lineId</c>) line it names, else <c>lines</c>.</summary>
    private static string LineRevisionField(SalesDraftLineRevisionRequest request, SalesRuleException exception)
    {
        if (exception.LineId is not { } lineId)
        {
            return exception.Code == SalesRuleCodes.TooManyLines ? "added" : "lines";
        }

        var addedIndex = (request.Added ?? []).ToList().FindIndex(line => line.LineId == lineId);
        if (addedIndex >= 0)
        {
            return exception.Code is SalesRuleCodes.SubcontractUnitNotKilogram ? $"added[{addedIndex}].orderUnitId"
                : exception.Code is SalesRuleCodes.SubcontractLineNotService ? $"added[{addedIndex}].productId"
                : $"added[{addedIndex}]";
        }

        var removedIndex = (request.Removed ?? []).ToList().FindIndex(removal => removal.LineId == lineId);
        return removedIndex >= 0 ? $"removed[{removedIndex}].lineId" : "lines";
    }
}
