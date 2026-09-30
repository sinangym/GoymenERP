using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.Quantities;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Logistics.Application;

/// <summary>
/// The loading-instruction use cases (LOG-4a). The route binds nothing but the server context; this
/// service decides, the Sales draft service's way:
/// <list type="bullet">
/// <item><b>Authority</b>: a fresh central measurement of the Logistics name (#276) before anything is
/// read (ADR-011 §5), after every awaited owner read, and right before the single save; none after the
/// commit (S03). The Sales port carries no authority of its own (FİNANS INFO-6), so it is fenced here.</item>
/// <item><b>Replay</b> (§2–§4, §7): the attempt of (company, command, key hash) answers first.</item>
/// <item><b>Owners, in order</b>: the Cari account (party + account, active, this company) → per line the
/// APPROVED Sales order line (D4, Business 5737989917 = B: NotApproved / NotFound are fail-closed 409s;
/// the line's product and customer are the Sales owner's answer) → the Settings kilogram profile through
/// <see cref="LoadingUnitVerifier"/> (Ö2 scale, never rounded).</item>
/// <item><b>Terminal refusals</b> (§7, D5): a stale version, a wrong state, the §13 cap and a block are
/// recorded under the key and replayed. Nothing physical is written (D3): loads are shares, never stock.</item>
/// </list>
/// </summary>
public sealed partial class LogisticsInstructionService(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    ICorrelationContext correlation,
    ILogisticsPartyAccountPortV1 accounts,
    ISalesOrderLineReferencePortV1 orderLines,
    LoadingUnitVerifier units,
    ILogisticsInstructionStore store,
    LogisticsInstructionServiceBindings bindings,
    TimeProvider timeProvider,
    ILogisticsRealizationTransactionRunnerV1? realization = null) : ILogisticsInstructionService, ILogisticsRealizationServiceV1
{
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 100;
    private const int MaximumCursorLength = 8192;
    // decimal(18,3): at most 15 integer digits; a wider text is refused at the wire, never a provider exception at the save (FİNANS #350 P3-2).
    private const int MaximumIntegerDigits = 15;

    public async Task<LoadingInstructionResponse> CreateInstructionAsync(CreateLoadingInstructionRequest request,
        LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();

        var scope = await RequireAsync(bindings.CreatePermission, cancellationToken);
        var requestHash = LogisticsRequestHash.Compute(request);
        var envelope = Envelope(scope, metadata, bindings.CreatePermission!);
        var replay = await FindReplayAsync(scope, bindings.CreatePermission, envelope, requestHash, LogisticsCommands.CreateV1, cancellationToken);
        if (replay is not null)
        {
            return Replayed(replay);
        }

        await RequireAccountAsync(scope, bindings.CreatePermission, request.CustomerPartyId, request.CompanyAccountId, cancellationToken);
        var lines = new List<(LoadingInstructionLineRequest Line, SourceOrderLineRef Source, Guid ProductId, Quantity Quantity)>(request.Lines.Count);
        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            var (source, productId) = await ReadOrderLineAsync(scope, request, index, cancellationToken);
            var quantity = await VerifyUnitAsync(scope, bindings.CreatePermission, line.UnitId, line.Quantity, request.EffectiveOn,
                $"lines[{index}]", cancellationToken);
            lines.Add((line, source, productId, quantity));
        }

        // The last fence that can still prevent the write; none after the commit.
        await RequireStillAsync(scope, bindings.CreatePermission, cancellationToken);
        var now = timeProvider.GetUtcNow();
        LogisticsStoreResult stored;
        try
        {
            stored = await store.StoreInstructionAsync(envelope, requestHash, request.EffectiveOn, () =>
            {
                var instruction = LoadingInstruction.Create(Guid.NewGuid(), scope.CompanyId, request.CustomerPartyId, request.CompanyAccountId, now);
                foreach (var (line, source, productId, quantity) in lines)
                {
                    instruction.AddLine(instruction.Version, line.LineId, source, productId, line.UnitId, null, quantity, now);
                }

                return instruction;
            }, cancellationToken);
        }
        catch (LogisticsRuleException exception)
        {
            throw new LogisticsInstructionServiceException(422, "validation_failed", [new("instruction", exception.Code)]);
        }
        catch (ArgumentException)
        {
            throw new LogisticsInstructionServiceException(400, "validation_failed");
        }
        catch (LogisticsStoreException exception) when (exception.Code == LogisticsStoreCodes.ConcurrentChange)
        {
            var won = await FindReplayAsync(scope, bindings.CreatePermission, envelope, requestHash, LogisticsCommands.CreateV1, cancellationToken);
            return won is null ? throw Store(exception) : Replayed(won);
        }
        catch (LogisticsStoreException exception)
        {
            throw Store(exception);
        }

        return Response(stored.Instruction, stored.Outcome == LogisticsWriteOutcome.Replayed, stored.OriginalCorrelationId);
    }

    public Task<LoadingInstructionResponse> IssueInstructionAsync(Guid instructionId, IssueLoadingInstructionRequest request,
        LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return MutateAsync(instructionId, request, request.ExpectedVersion, metadata, LogisticsCommands.IssueV1, bindings.CreatePermission,
            (instruction, expected, now, _, _) => instruction.Issue(expected, now),
            new LogisticsMutationAudit("logistics.loading-instruction.issued.v1", "Issue", "Yükleme talimatı yayımlandı"),
            prepare: null, cancellationToken);
    }

    public Task<LoadingInstructionResponse> RecordLoadAsync(Guid instructionId, Guid lineId, RecordLoadRequest request,
        LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var measurement = request.Measurement switch
        {
            "Measured" => MeasurementKind.Measured,
            "AllocatedFromVehicleWeight" => MeasurementKind.AllocatedFromVehicleWeight,
            _ => throw new LogisticsInstructionServiceException(400, "validation_failed", [new("measurement", "validation_failed")]),
        };
        return MutateAsync(instructionId, new { lineId, request }, request.ExpectedVersion, metadata, LogisticsCommands.RecordLoadV1,
            bindings.RecordPermission,
            (instruction, expected, now, quantity, _) => instruction.RecordLoad(expected, request.LoadId, lineId, quantity!.Value, measurement, now),
            new LogisticsMutationAudit("logistics.loading-instruction.load-recorded.v1", "RecordLoad", "Yükleme payı kaydedildi"),
            // The line's unit is re-verified against the Settings profile on the instruction's date (the
            // verifier-before-share contract): the loaded quantity's scale is the owner's, never guessed.
            prepare: async (scope, stored) =>
            {
                var line = stored.Instruction.Lines.FirstOrDefault(candidate => candidate.Id == lineId)
                    ?? throw new LogisticsInstructionServiceException(404, "not_found");
                var quantity = await VerifyUnitAsync(scope, bindings.RecordPermission, line.UnitId, request.Quantity, stored.EffectiveOn, "load",
                    cancellationToken);
                await RequireCurrentOrderLineAllowsLoadAsync(scope, stored, line, cancellationToken);
                return quantity;
            }, cancellationToken);
    }

    /// <summary>
    /// AT-053: the vehicle's manual weighbridge record. The two weighings are exact kilogram text (Ö2's cap of 3 decimals,
    /// the column's 15 integer digits, never rounded); the domain calculates net and difference (WGT-001/002) and refuses a
    /// pair that contradicts the direction. The operator is the authenticated actor, the time the server's. No Settings
    /// profile is read: a weighbridge weighs kilograms by definition, and the instruction's lines are compared only when
    /// every one of them is a kilogram line. Measured under its own name, <c>logistics.weighings.record</c>.
    /// </summary>
    public Task<LoadingInstructionResponse> RecordWeighingAsync(Guid instructionId, RecordWeighingRequest request, LogisticsCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var direction = request.Direction switch
        {
            "TareFirst" => WeighingDirection.TareFirst,
            "GrossFirst" => WeighingDirection.GrossFirst,
            _ => throw new LogisticsInstructionServiceException(400, "validation_failed", [new("direction", "validation_failed")]),
        };
        return MutateAsync(instructionId, request, request.ExpectedVersion, metadata, LogisticsCommands.RecordWeighingV1, bindings.WeighingPermission,
            (instruction, expected, now, _, actorId) => instruction.RecordWeighing(expected, request.WeighingId, direction,
                Quantity.Positive(ReadWeighingKg(request.FirstWeighingKg, "firstWeighingKg"), QuantityUnit.Kilogram),
                Quantity.Positive(ReadWeighingKg(request.SecondWeighingKg, "secondWeighingKg"), QuantityUnit.Kilogram), actorId, now),
            // The texts are read inside the transition: after the fences and the replay, before anything is saved.
            new LogisticsMutationAudit("logistics.loading-instruction.weighing-recorded.v1", "RecordWeighing", "Kantar tartımı kaydedildi"),
            prepare: null, cancellationToken);
    }

    /// <summary>A weighing in kilograms as exact text: the lexical Ö2 check first (≤ 3 decimals, a non-zero digit), then the column width; never rounded.</summary>
    private static decimal ReadWeighingKg(string text, string field)
    {
        try
        {
            ExactPositiveDecimalV1.Parse(text, Quantity.KilogramScale);
        }
        catch (SettingsPrerequisiteException exception) when (exception.Code == "settings_decimal_scale_exceeded")
        {
            throw new LogisticsInstructionServiceException(422, "validation_failed", [new(field, LoadingUnitCodes.QuantityScaleExceeded)]);
        }
        catch (SettingsPrerequisiteException)
        {
            throw new LogisticsInstructionServiceException(400, "validation_failed", [new(field, "invalid_decimal_text")]);
        }

        var point = text.IndexOf('.', StringComparison.Ordinal);
        if ((point < 0 ? text : text[..point]).TrimStart('0').Length > MaximumIntegerDigits)
        {
            throw new LogisticsInstructionServiceException(422, "validation_failed", [new(field, LogisticsStoreCodes.ValueDoesNotFit)]);
        }

        // At most 18 significant digits by now: the conversion is exact.
        return decimal.Parse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }

    public async Task<LoadingInstructionDetailView> GetInstructionDetailAsync(Guid instructionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        if (instructionId == Guid.Empty)
        {
            throw new LogisticsInstructionServiceException(404, "not_found");
        }

        StoredLoadingInstruction? stored;
        try
        {
            stored = await GuardedAsync(scope, bindings.ReadPermission, () => store.FindInstructionAsync(instructionId, cancellationToken), cancellationToken);
        }
        catch (LogisticsStoreException exception)
        {
            throw Store(exception);
        }

        return stored is null ? throw new LogisticsInstructionServiceException(404, "not_found") : Detail(stored);
    }

    public async Task<LoadingInstructionListResponse> GetInstructionListAsync(LoadingInstructionListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        var pageSize = query.PageSize == 0 ? DefaultPageSize : query.PageSize;
        if (pageSize is < 1 or > MaximumPageSize || query.Cursor?.Length > MaximumCursorLength
            || (query.Cursor is not null && string.IsNullOrWhiteSpace(query.Cursor)))
        {
            throw new LogisticsInstructionServiceException(400, "validation_failed");
        }

        LoadingInstructionStatus? status = null;
        if (query.Status is not null)
        {
            if (!Enum.TryParse<LoadingInstructionStatus>(query.Status, ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed)
                || !string.Equals(parsed.ToString(), query.Status, StringComparison.Ordinal))
            {
                throw new LogisticsInstructionServiceException(400, "validation_failed", [new("status", "validation_failed")]);
            }

            status = parsed;
        }

        LogisticsInstructionPage page;
        try
        {
            page = await GuardedAsync(scope, bindings.ReadPermission,
                () => store.ListInstructionsAsync(pageSize, query.Cursor, status, cancellationToken), cancellationToken);
        }
        catch (LogisticsStoreException exception)
        {
            throw Store(exception);
        }

        return new LoadingInstructionListResponse([.. page.Items.Select(Summary)], page.NextCursor);
    }

    /// <summary>
    /// LOG-4b: the approved order lines for the picker — measured under the CREATE name (the person who may open
    /// an instruction may see what there is to load), read from the Sales owner's price-free port, fenced like every
    /// owner read; every item must be this company's (else the owner answered malformed).
    /// </summary>
    public async Task<LogisticsApprovedOrderLinePage> GetApprovedOrderLinesAsync(LogisticsApprovedOrderLinesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.CreatePermission, cancellationToken);
        var pageSize = query.PageSize == 0 ? DefaultPageSize : query.PageSize;
        if (pageSize is < 1 or > MaximumPageSize || query.Cursor?.Length > MaximumCursorLength
            || (query.Cursor is not null && string.IsNullOrWhiteSpace(query.Cursor)))
        {
            throw new LogisticsInstructionServiceException(400, "validation_failed");
        }

        SalesApprovedLinePageV1 page;
        try
        {
            page = await GuardedAsync(scope, bindings.CreatePermission,
                () => orderLines.ListApprovedLinesAsync(pageSize, query.Cursor, cancellationToken), cancellationToken);
        }
        catch (LogisticsInstructionServiceException)
        {
            throw;
        }
        catch (ArgumentException)
        {
            // The Sales owner refused the cursor (another company's, or malformed): the caller's input, not an outage.
            throw new LogisticsInstructionServiceException(400, LogisticsProblemCodesV1.InvalidCursor);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.SalesProviderUnavailable);
        }

        // FİNANS #358 P2: the page is a LINE page — the Sales owner caps the lines it answers at pageSize; an answer over the
        // cap is the provider contradicting the contract (503), so the customer reads below stay bounded by the cap too.
        if (page is null || page.Items is null || page.Items.Count > pageSize || page.Items.Any(item => item is null || item.CompanyId != scope.CompanyId
                || item.OrderId == Guid.Empty || item.LineId == Guid.Empty || item.ProductId == Guid.Empty || item.OrderUnitId == Guid.Empty
                || string.IsNullOrWhiteSpace(item.QuantityText) || string.IsNullOrWhiteSpace(item.DocumentNo)))
        {
            throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.ReferenceProviderInvalid);
        }

        // Business 5745076917: the customer is shown by NAME (the Cari owner's, through the party-account port), never by an id.
        // One fenced owner read per DISTINCT customer of the page, memoized for this request only (`names` lives in this call;
        // never across requests, never an authority shortcut: the #276 fence is measured around every owner read); the line
        // cap bounds the number of reads. A customer the owner does not answer for (#358 P1, coordinator ruling: the Cari
        // owner answers a missing grant exactly as a missing party — nothing — so a consumer cannot tell them apart) keeps its
        // lines on the page under a LABELLED REFERENCE (the Finance #356/#357 shape, "ref. …" + the last 8 of the party id),
        // never dropped: an empty page would say "nothing to load" while lines exist. The reference reads as a reference, not
        // a name; picking such a line meets the create's own explicit refusal. A denial or an outage is the owner's answer
        // for the whole read.
        var names = new Dictionary<(Guid Party, Guid Account), string>();
        foreach (var customer in page.Items.Select(item => (Party: item.PartyId, Account: item.CompanyAccountId)).Distinct())
        {
            var answer = await GuardedAsync(scope, bindings.CreatePermission,
                () => accounts.GetAuthorizedAsync(customer.Party, customer.Account, cancellationToken), cancellationToken);
            switch (answer.Outcome)
            {
                case LogisticsPartyAccountOutcomeV1.Found when answer.Account is { } account:
                    if (account.PartyId != customer.Party || account.CompanyAccountId != customer.Account || account.CompanyId != scope.CompanyId
                        || string.IsNullOrWhiteSpace(account.DisplayName))
                    {
                        throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.ReferenceProviderInvalid);
                    }

                    names[customer] = account.DisplayName;
                    break;
                case LogisticsPartyAccountOutcomeV1.NotFound:
                    names[customer] = CustomerReference(customer.Party);
                    break;
                case LogisticsPartyAccountOutcomeV1.Denied:
                    throw new LogisticsInstructionServiceException(403, "forbidden");
                default:
                    throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.CounterpartyProviderUnavailable);
            }
        }

        return new LogisticsApprovedOrderLinePage(
            [.. page.Items
                .Select(item => new LogisticsApprovedOrderLineView(item.OrderId, item.DocumentNo, item.LineId, item.LineNo, item.PartyId,
                    item.CompanyAccountId, names[(item.PartyId, item.CompanyAccountId)], item.ProductId, item.ProductVersion, item.ProductCode,
                    item.ProductName, item.OrderUnitId, item.UnitCode, item.QuantityText, item.RequestedDeliveryDate, item.Revision))],
            page.NextCursor);
    }

    /// <summary>The labelled technical reference shown in place of a customer name the Cari owner did not answer: never mistakable for a title.</summary>
    internal static string CustomerReference(Guid partyId) => $"ref. …{partyId.ToString("D")[^8..]}";

    // ---- mutations ------------------------------------------------------------------------------

    private async Task<LoadingInstructionResponse> MutateAsync(Guid instructionId, object content, string expectedVersionText,
        LogisticsCommandMetadata metadata, string commandName, string? permission,
        Action<LoadingInstruction, long, DateTimeOffset, Quantity?, string> transition, LogisticsMutationAudit audit,
        Func<Scope, StoredLoadingInstruction, Task<Quantity>>? prepare, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(permission, cancellationToken);
        if (!long.TryParse(expectedVersionText, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion) || expectedVersion < 1)
        {
            throw new LogisticsInstructionServiceException(400, "validation_failed", [new("expectedVersion", "validation_failed")]);
        }

        var requestHash = LogisticsRequestHash.Compute(commandName, instructionId, content);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            return Replayed(replay);
        }

        if (instructionId == Guid.Empty)
        {
            throw new LogisticsInstructionServiceException(404, "not_found");
        }

        Quantity? prepared = null;
        if (prepare is not null)
        {
            StoredLoadingInstruction? current;
            try
            {
                current = await GuardedAsync(scope, permission, () => store.FindInstructionAsync(instructionId, cancellationToken), cancellationToken);
            }
            catch (LogisticsStoreException exception)
            {
                throw Store(exception);
            }

            if (current is null)
            {
                throw new LogisticsInstructionServiceException(404, "not_found");
            }

            prepared = await prepare(scope, current);
        }

        await RequireStillAsync(scope, permission, cancellationToken);
        LogisticsStoreResult? result;
        try
        {
            result = await store.MutateInstructionAsync(envelope, requestHash, commandName, instructionId,
                (instruction, now) => transition(instruction, expectedVersion, now, prepared, scope.ActorId), audit, cancellationToken);
        }
        catch (LogisticsVersionConflictException)
        {
            return await TerminalAsync(envelope, requestHash, commandName,
                new LogisticsTerminalRefusal(409, LogisticsProblemCodesV1.VersionConflict, [new("expectedVersion", "version_conflict")]), cancellationToken);
        }
        catch (LogisticsRuleException exception) when (exception.Code is LogisticsRuleCodes.InstructionNotDraft or LogisticsRuleCodes.InstructionNotIssued)
        {
            return await TerminalAsync(envelope, requestHash, commandName,
                new LogisticsTerminalRefusal(409, LogisticsProblemCodesV1.InstructionState, [new("status", exception.Code)]), cancellationToken);
        }
        catch (LogisticsRuleException exception) when (exception.Code == LogisticsRuleCodes.LoadExceedsInstructed)
        {
            return await TerminalAsync(envelope, requestHash, commandName,
                new LogisticsTerminalRefusal(409, LogisticsProblemCodesV1.LoadExceedsInstructed, [new("quantity", exception.Code)]), cancellationToken);
        }
        catch (LogisticsRuleException exception) when (exception.Code == LogisticsRuleCodes.LineBlocked)
        {
            return await TerminalAsync(envelope, requestHash, commandName,
                new LogisticsTerminalRefusal(409, LogisticsProblemCodesV1.LineBlocked, [new("lineId", exception.Code)]), cancellationToken);
        }
        catch (LogisticsRuleException exception) when (exception.Code == LogisticsRuleCodes.DuplicateShareId)
        {
            return await TerminalAsync(envelope, requestHash, commandName,
                new LogisticsTerminalRefusal(409, LogisticsProblemCodesV1.DuplicateShare, [new("loadId", exception.Code)]), cancellationToken);
        }
        catch (LogisticsRuleException exception) when (exception.Code == LogisticsRuleCodes.WeighingAlreadyRecorded)
        {
            // AT-053: one weighbridge record per instruction; a second one (another weighing id, another key) is a recorded
            // terminal refusal — never a replacement, since how a wrong entry is corrected is not decided.
            return await TerminalAsync(envelope, requestHash, commandName,
                new LogisticsTerminalRefusal(409, LogisticsProblemCodesV1.WeighingAlreadyRecorded, [new("weighingId", exception.Code)]), cancellationToken);
        }
        catch (LogisticsRuleException exception) when (exception.Code == LogisticsRuleCodes.ShipmentAlreadyDispatched)
        {
            // AT-055: a shipment is on at most one non-cancelled dispatch note; a second link (another draft, another key) is a
            // recorded terminal refusal and the first note stays as it is.
            return await TerminalAsync(envelope, requestHash, commandName,
                new LogisticsTerminalRefusal(409, LogisticsProblemCodesV1.ShipmentAlreadyDispatched, [new("shipmentIds", exception.Code)]), cancellationToken);
        }
        catch (LogisticsRuleException exception) when (exception.Code == LogisticsRuleCodes.DuplicateDispatchNoteId)
        {
            // The note id is taken in this company — on this instruction or (the store's check) on another one, even a cancelled note.
            return await TerminalAsync(envelope, requestHash, commandName,
                new LogisticsTerminalRefusal(409, LogisticsProblemCodesV1.DuplicateDispatchNote, [new("dispatchNoteId", exception.Code)]), cancellationToken);
        }
        catch (LogisticsRuleException exception) when (exception.Code == LogisticsRuleCodes.DispatchNoteState)
        {
            // AT-055 lifecycle: approve needs a Draft, cancel a non-cancelled note, the e-İrsaliye number an Approved note without one.
            return await TerminalAsync(envelope, requestHash, commandName,
                new LogisticsTerminalRefusal(409, LogisticsProblemCodesV1.DispatchNoteState, [new("status", exception.Code)]), cancellationToken);
        }
        catch (LogisticsRuleException exception) when (exception.Code == LogisticsRuleCodes.EDispatchNumberInUse)
        {
            return await TerminalAsync(envelope, requestHash, commandName,
                new LogisticsTerminalRefusal(409, LogisticsProblemCodesV1.EDispatchNumberInUse, [new("eDispatchNumber", exception.Code)]), cancellationToken);
        }
        catch (LogisticsRuleException exception) when (exception.Code == LogisticsRuleCodes.DispatchNoteNotFound)
        {
            throw new LogisticsInstructionServiceException(404, "not_found");
        }
        catch (LogisticsRuleException exception) when (exception.Code == LogisticsRuleCodes.ShipmentNotFound
            && string.Equals(commandName, LogisticsCommands.CreateDispatchNoteV1, StringComparison.Ordinal))
        {
            throw new LogisticsInstructionServiceException(422, LogisticsProblemCodesV1.ShipmentNotFound, [new("shipmentIds", exception.Code)]);
        }
        catch (LogisticsRuleException exception) when (exception.Code == LogisticsRuleCodes.WeighingDirectionInconsistent)
        {
            throw new LogisticsInstructionServiceException(422, LogisticsProblemCodesV1.WeighingDirectionInconsistent,
                [new("secondWeighingKg", exception.Code)]);
        }
        catch (LogisticsRuleException exception) when (exception.Code == LogisticsRuleCodes.WeighingUnitUnsupported)
        {
            throw new LogisticsInstructionServiceException(422, LogisticsProblemCodesV1.UnsupportedUnit, [new("instruction", exception.Code)]);
        }
        catch (LogisticsRuleException exception) when (exception.Code == LogisticsRuleCodes.LineNotFound)
        {
            throw new LogisticsInstructionServiceException(404, "not_found");
        }
        catch (LogisticsRuleException exception)
        {
            throw new LogisticsInstructionServiceException(422, "validation_failed", [new("instruction", exception.Code)]);
        }
        catch (ArgumentException)
        {
            throw new LogisticsInstructionServiceException(400, "validation_failed");
        }
        catch (LogisticsStoreException exception) when (exception.Code == LogisticsStoreCodes.ConcurrentChange)
        {
            var won = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
            return won is null ? throw Store(exception) : Replayed(won);
        }
        catch (LogisticsStoreException exception)
        {
            throw Store(exception);
        }

        return result is null
            ? throw new LogisticsInstructionServiceException(404, "not_found")
            : Response(result.Instruction, result.Outcome == LogisticsWriteOutcome.Replayed, result.OriginalCorrelationId);
    }

    // ---- owners ---------------------------------------------------------------------------------

    private async Task RequireAccountAsync(Scope scope, string? permission, Guid partyId, Guid companyAccountId, CancellationToken cancellationToken)
    {
        var answer = await GuardedAsync(scope, permission, () => accounts.GetAuthorizedAsync(partyId, companyAccountId, cancellationToken), cancellationToken);
        switch (answer.Outcome)
        {
            case LogisticsPartyAccountOutcomeV1.Found when answer.Account is { } account:
                if (account.PartyId != partyId || account.CompanyAccountId != companyAccountId)
                {
                    throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.ReferenceProviderInvalid);
                }

                if (account.CompanyId != scope.CompanyId)
                {
                    throw new LogisticsInstructionServiceException(409, LogisticsProblemCodesV1.AccountState, [new("companyAccountId", "company_mismatch")]);
                }

                if (!account.PartyIsActive || !account.AccountIsActive)
                {
                    throw new LogisticsInstructionServiceException(409, LogisticsProblemCodesV1.AccountState, [new("companyAccountId", "passive_company_account")]);
                }

                return;
            case LogisticsPartyAccountOutcomeV1.NotFound:
                throw new LogisticsInstructionServiceException(409, LogisticsProblemCodesV1.AccountState,
                    [new("customerPartyId", "invalid_reference"), new("companyAccountId", "invalid_reference")]);
            case LogisticsPartyAccountOutcomeV1.Denied:
                throw new LogisticsInstructionServiceException(403, "forbidden");
            default:
                throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.CounterpartyProviderUnavailable);
        }
    }

    /// <summary>D4: only an APPROVED line of this company and this customer is a need; the product and the revision are the Sales owner's answer.</summary>
    private async Task<(SourceOrderLineRef Source, Guid ProductId)> ReadOrderLineAsync(Scope scope,
        CreateLoadingInstructionRequest request, int index,
        CancellationToken cancellationToken)
    {
        var line = request.Lines[index];
        var field = $"lines[{index}].sourceOrderLineId";
        SalesOrderLineReferenceAnswerV1 answer;
        try
        {
            answer = await GuardedAsync(scope, bindings.CreatePermission,
                () => orderLines.GetApprovedLineAsync(line.SourceOrderId, line.SourceOrderLineId, cancellationToken), cancellationToken);
        }
        catch (LogisticsInstructionServiceException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.SalesProviderUnavailable);
        }

        switch (answer.Outcome)
        {
            case SalesOrderLineReferenceOutcomeV1.Approved when answer.Line is { } approved:
                if (approved.CompanyId != scope.CompanyId || approved.OrderId != line.SourceOrderId || approved.LineId != line.SourceOrderLineId
                    || approved.ProductId == Guid.Empty || approved.Revision < 0)
                {
                    throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.ReferenceProviderInvalid);
                }

                if (approved.PartyId != request.CustomerPartyId || approved.CompanyAccountId != request.CompanyAccountId)
                {
                    throw new LogisticsInstructionServiceException(409, LogisticsProblemCodesV1.OrderLineState, [new(field, "customer_mismatch")]);
                }

                RequireKnownRemainderState(approved.RemainderState);
                if (approved.RemainderState == SalesRemainderChangeNamesV1.Cancelled)
                {
                    throw new LogisticsInstructionServiceException(409, LogisticsProblemCodesV1.OrderLineState,
                        [new(field, "remainder_cancelled")]);
                }

                if (approved.OrderUnitId != line.UnitId)
                {
                    throw new LogisticsInstructionServiceException(409, LogisticsProblemCodesV1.OrderLineState,
                        [new($"lines[{index}].unitId", "unit_mismatch")]);
                }

                return (new SourceOrderLineRef(approved.OrderId, approved.LineId, approved.Revision), approved.ProductId);
            case SalesOrderLineReferenceOutcomeV1.NotApproved:
                throw new LogisticsInstructionServiceException(409, LogisticsProblemCodesV1.OrderLineState,
                    [new(field, "not_approved"), new($"lines[{index}].approvalState", answer.ApprovalState ?? "unknown")]);
            case SalesOrderLineReferenceOutcomeV1.NotFound:
                throw new LogisticsInstructionServiceException(409, LogisticsProblemCodesV1.OrderLineState, [new(field, "invalid_reference")]);
            default:
                throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.ReferenceProviderInvalid);
        }
    }

    /// <summary>
    /// An existing instruction is not a licence to load an old quantity forever. Immediately before every load, re-read the
    /// Sales-owned line and enforce its current approved quantity/remainder. This is a read-only cross-module fence: Logistics
    /// creates no Sales record and Sales creates no Logistics share. A residual change after this read is handled by the next
    /// load; there is deliberately no cross-module transaction.
    /// </summary>
    private async Task RequireCurrentOrderLineAllowsLoadAsync(Scope scope, StoredLoadingInstruction stored, LoadingInstructionLine line,
        CancellationToken cancellationToken)
    {
        SalesOrderLineReferenceAnswerV1 answer;
        try
        {
            answer = await GuardedAsync(scope, bindings.RecordPermission,
                () => orderLines.GetApprovedLineAsync(line.Source.OrderId, line.Source.LineId, cancellationToken), cancellationToken);
        }
        catch (LogisticsInstructionServiceException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.SalesProviderUnavailable);
        }

        if (answer.Outcome != SalesOrderLineReferenceOutcomeV1.Approved || answer.Line is not { } approved)
        {
            var code = answer.Outcome == SalesOrderLineReferenceOutcomeV1.NotApproved ? "not_approved" : "invalid_reference";
            throw new LogisticsInstructionServiceException(409, LogisticsProblemCodesV1.OrderLineState,
                [new("sourceOrderLineId", code)]);
        }

        if (approved.CompanyId != scope.CompanyId || approved.OrderId != line.Source.OrderId || approved.LineId != line.Source.LineId
            || approved.PartyId != stored.Instruction.CustomerPartyId || approved.CompanyAccountId != stored.Instruction.CompanyAccountId
            || approved.ProductId != line.ProductId || approved.OrderUnitId != line.UnitId || approved.Revision < 0)
        {
            throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.ReferenceProviderInvalid);
        }

        RequireKnownRemainderState(approved.RemainderState);
        if (approved.RemainderState == SalesRemainderChangeNamesV1.Cancelled)
        {
            throw new LogisticsInstructionServiceException(409, LogisticsProblemCodesV1.OrderLineState,
                [new("sourceOrderLineId", "remainder_cancelled")]);
        }

        // OPEN-004 (user decision 2026-09-30): over/under loading is allowed within a tolerance entered on the order line,
        // defaulted by Settings per product group and piece/kg basis. Those source contracts and the atomic aggregate gate
        // across every instruction of this Sales line do not exist yet. Do not turn their absence into a zero-tolerance cap.
        // Cancelled remainder above is exact and remains blocked; a numeric revised/open cap is added only with that K3 source.
    }

    private static void RequireKnownRemainderState(string state)
    {
        if (state is not (SalesRemainderChangeNamesV1.Open or SalesRemainderChangeNamesV1.Revised
            or SalesRemainderChangeNamesV1.Cancelled))
        {
            throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.ReferenceProviderInvalid);
        }
    }


    private async Task<Quantity> VerifyUnitAsync(Scope scope, string? permission, Guid unitId, string quantityText, DateOnly effectiveOn,
        string field, CancellationToken cancellationToken)
    {
        var value = ReadQuantityText(quantityText, field);
        var verification = await GuardedAsync(scope, permission, () => units.VerifyAsync(scope.CompanyId, unitId, value, effectiveOn, cancellationToken),
            cancellationToken);
        if (verification.Unit is { } unit)
        {
            if (unit.UnitId != unitId)
            {
                throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.ReferenceProviderInvalid);
            }

            return unit.Quantity;
        }

        throw verification.Code switch
        {
            "settings_access_denied" => new LogisticsInstructionServiceException(403, "forbidden"),
            "settings_provider_unavailable" => new LogisticsInstructionServiceException(503, "settings_provider_unavailable"),
            "settings_profile_invalid" => new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.ReferenceProviderInvalid),
            _ => new LogisticsInstructionServiceException(422, LogisticsProblemCodesV1.UnsupportedUnit,
                [new($"{field}.unitId", verification.Code ?? LogisticsProblemCodesV1.UnsupportedUnit)]),
        };
    }

    /// <summary>
    /// Ö2 "never rounded" (FİNANS #350 P2-1): the text is validated lexically FIRST — digits and one inner '.', a non-zero digit,
    /// at most the kilogram cap of decimals (Settings' <see cref="ExactPositiveDecimalV1"/>, the Finance / Inventory shape) and at
    /// most 15 integer digits (the column) — and only the validated text is converted, so a text with more digits than a decimal
    /// holds can never round ("2.9999999999999999999999999999999" is refused, it never becomes 3). The owner's profile scale (≤ 3)
    /// is then measured by the verifier on the exact value.
    /// </summary>
    private static decimal ReadQuantityText(string quantityText, string field)
    {
        try
        {
            ExactPositiveDecimalV1.Parse(quantityText, Quantity.KilogramScale);
        }
        catch (SettingsPrerequisiteException exception) when (exception.Code == "settings_decimal_scale_exceeded")
        {
            throw new LogisticsInstructionServiceException(422, LogisticsProblemCodesV1.UnsupportedUnit,
                [new($"{field}.unitId", LoadingUnitCodes.QuantityScaleExceeded)]);
        }
        catch (SettingsPrerequisiteException)
        {
            throw new LogisticsInstructionServiceException(400, "validation_failed", [new($"{field}.quantity", "invalid_decimal_text")]);
        }

        var point = quantityText.IndexOf('.', StringComparison.Ordinal);
        var integerPart = point < 0 ? quantityText : quantityText[..point];
        if (integerPart.TrimStart('0').Length > MaximumIntegerDigits)
        {
            throw new LogisticsInstructionServiceException(422, "validation_failed", [new($"{field}.quantity", LogisticsStoreCodes.ValueDoesNotFit)]);
        }

        // At most 18 significant digits by now: the conversion is exact.
        return decimal.Parse(quantityText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }

    // ---- replay / terminal ----------------------------------------------------------------------

    private async Task<LogisticsReplay?> FindReplayAsync(Scope scope, string? permission, LogisticsWriteEnvelope envelope, string requestHash,
        string commandName, CancellationToken cancellationToken)
    {
        try
        {
            return await GuardedAsync(scope, permission, () => store.FindReplayAsync(envelope, requestHash, commandName, cancellationToken), cancellationToken);
        }
        catch (LogisticsStoreException exception)
        {
            throw Store(exception);
        }
    }

    private static LoadingInstructionResponse Replayed(LogisticsReplay replay)
    {
        if (replay.Refusal is { } refusal)
        {
            throw new LogisticsInstructionServiceException(refusal.Status, refusal.Code, refusal.Errors, isReplay: true,
                originalCorrelationId: replay.OriginalCorrelationId);
        }

        return Response(replay.Instruction!, isReplay: true, replay.OriginalCorrelationId);
    }

    /// <summary>
    /// §7: the terminal refusal is recorded before it is answered. §8: if the same key was taken meanwhile, the winner's record is
    /// the answer — its refusal replayed, or its stored transition returned as a replay (FİNANS #350 P3-1: never <c>concurrent_change</c>
    /// for a loser that lands on a terminal path after the winner committed).
    /// </summary>
    private async Task<LoadingInstructionResponse> TerminalAsync(LogisticsWriteEnvelope envelope, string requestHash, string commandName,
        LogisticsTerminalRefusal refusal, CancellationToken cancellationToken)
    {
        try
        {
            await store.RecordRefusalAsync(envelope, requestHash, commandName, refusal, cancellationToken);
        }
        catch (LogisticsStoreException exception) when (exception.Code == LogisticsStoreCodes.ConcurrentChange)
        {
            LogisticsReplay? won;
            try
            {
                won = await store.FindReplayAsync(envelope, requestHash, commandName, cancellationToken);
            }
            catch (LogisticsStoreException raced)
            {
                throw Store(raced);
            }

            if (won?.Refusal is { } recorded)
            {
                throw new LogisticsInstructionServiceException(recorded.Status, recorded.Code, recorded.Errors, isReplay: true,
                    originalCorrelationId: won.OriginalCorrelationId);
            }

            if (won?.Instruction is not null)
            {
                return Replayed(won);
            }
        }

        throw new LogisticsInstructionServiceException(refusal.Status, refusal.Code, refusal.Errors);
    }

    private static LogisticsInstructionServiceException Store(LogisticsStoreException exception) => exception.Code switch
    {
        LogisticsStoreCodes.SameKeyPayloadMismatch => new LogisticsInstructionServiceException(409, LogisticsProblemCodesV1.SameKeyPayloadMismatch),
        LogisticsStoreCodes.ConcurrentChange => new LogisticsInstructionServiceException(409, LogisticsProblemCodesV1.ConcurrentChange),
        LogisticsStoreCodes.InvalidCursor => new LogisticsInstructionServiceException(400, LogisticsProblemCodesV1.InvalidCursor),
        LogisticsStoreCodes.CompanyContextMismatch => new LogisticsInstructionServiceException(403, "forbidden"),
        LogisticsStoreCodes.ValueDoesNotFit => new LogisticsInstructionServiceException(422, "validation_failed", [new("instruction", exception.Code)]),
        _ => new LogisticsInstructionServiceException(503, "service_unavailable"),
    };

    // ---- authority ------------------------------------------------------------------------------

    private sealed record Scope(Guid CompanyId, string ActorId, IReadOnlyCollection<string> RoleCodes);

    private async Task<Scope> RequireAsync(string? permission, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new LogisticsInstructionServiceException(401, "unauthenticated");
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw new LogisticsInstructionServiceException(403, "forbidden");
        }

        if (string.IsNullOrWhiteSpace(permission) || !ErpPermissions.IsKnown(permission))
        {
            throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.PermissionUnregistered);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId;
        var scope = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide || !Matches(companyId, actorId))
        {
            throw new LogisticsInstructionServiceException(403, "forbidden");
        }

        return new Scope(companyId, actorId, [.. scope.RoleCodes.Order(StringComparer.Ordinal)]);
    }

    private async Task RequireStillAsync(Scope scope, string? permission, CancellationToken cancellationToken)
    {
        var current = await RequireAsync(permission, cancellationToken);
        if (current.CompanyId != scope.CompanyId || !string.Equals(current.ActorId, scope.ActorId, StringComparison.Ordinal))
        {
            throw new LogisticsInstructionServiceException(403, "forbidden");
        }
    }

    private async Task<T> GuardedAsync<T>(Scope scope, string? permission, Func<Task<T>> read, CancellationToken cancellationToken)
    {
        T result;
        try
        {
            result = await read();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await RequireStillAsync(scope, permission, cancellationToken);
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await RequireStillAsync(scope, permission, cancellationToken);
        return result;
    }

    private bool Matches(Guid companyId, string actorId) =>
        currentUser.IsAuthenticated && string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId == companyId && currentUser.CanAccessCompany(companyId);

    private LogisticsWriteEnvelope Envelope(Scope scope, LogisticsCommandMetadata metadata, string permission) =>
        new(scope.ActorId, permission, scope.RoleCodes, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey,
            correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent);

    // ---- mapping --------------------------------------------------------------------------------

    private static LoadingInstructionResponse Response(StoredLoadingInstruction stored, bool isReplay, string originalCorrelationId) =>
        new(Detail(stored), isReplay, originalCorrelationId);

    private static string Text(Quantity quantity) => quantity.Value.ToString(CultureInfo.InvariantCulture);

    private static LoadingInstructionSummaryView Summary(StoredLoadingInstruction stored)
    {
        var instruction = stored.Instruction;
        return new LoadingInstructionSummaryView(instruction.Id, instruction.CompanyId, instruction.Status.ToString(),
            instruction.Version.ToString(CultureInfo.InvariantCulture), instruction.CustomerPartyId, instruction.CompanyAccountId,
            stored.EffectiveOn, instruction.CreatedAtUtc, instruction.UpdatedAtUtc, instruction.Lines.Count);
    }

    private static LoadingInstructionDetailView Detail(StoredLoadingInstruction stored)
    {
        var instruction = stored.Instruction;
        return new LoadingInstructionDetailView(Summary(stored),
            [.. instruction.Lines.Select((line, index) => new LoadingInstructionLineView(line.Id, index + 1, line.Source.OrderId, line.Source.LineId,
                line.Source.Revision, line.ProductId, line.UnitId, Text(line.InstructedQuantity), Text(line.LoadedQuantity),
                Text(line.ShippedQuantity), Text(line.DeliveredQuantity), Text(line.RemainingToLoad), Text(line.LoadedNotShipped),
                Text(line.ShippedNotDelivered), line.IsBlocked))],
            [.. instruction.Shares.Select(share => new LoadingShareView(share.ShareId, share.LineId, share.Kind.ToString(), Text(share.Quantity),
                share.Measurement?.ToString(), share.SourceShipmentId, share.RecordedAtUtc))],
            instruction.Weighing is { } weighing ? WeighingView(weighing) : null,
            [.. instruction.DispatchNotes.Select(note => DispatchNoteView(instruction, note))]);
    }

    /// <summary>The weighbridge record exactly as the domain calculated it; the difference keeps its sign ("-" = lighter than loaded).</summary>
    private static LoadingInstructionWeighingView WeighingView(VehicleWeighing weighing) =>
        new(weighing.WeighingId, weighing.Direction.ToString(), Text(weighing.FirstWeighing), Text(weighing.SecondWeighing), Text(weighing.Tare),
            Text(weighing.Gross), Text(weighing.Net), Text(weighing.LoadedAtWeighing), weighing.DifferenceKg.ToString(CultureInfo.InvariantCulture),
            weighing.RecordedBy, weighing.RecordedAtUtc);
}
