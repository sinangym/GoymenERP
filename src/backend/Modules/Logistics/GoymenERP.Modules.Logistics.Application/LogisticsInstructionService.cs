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
public sealed class LogisticsInstructionService(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    ICorrelationContext correlation,
    ILogisticsPartyAccountPortV1 accounts,
    ISalesOrderLineReferencePortV1 orderLines,
    LoadingUnitVerifier units,
    ILogisticsInstructionStore store,
    LogisticsInstructionServiceBindings bindings,
    TimeProvider timeProvider) : ILogisticsInstructionService
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
            (instruction, expected, now, _) => instruction.Issue(expected, now),
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
            (instruction, expected, now, quantity) => instruction.RecordLoad(expected, request.LoadId, lineId, quantity!.Value, measurement, now),
            new LogisticsMutationAudit("logistics.loading-instruction.load-recorded.v1", "RecordLoad", "Yükleme payı kaydedildi"),
            // The line's unit is re-verified against the Settings profile on the instruction's date (the
            // verifier-before-share contract): the loaded quantity's scale is the owner's, never guessed.
            prepare: async (scope, stored) =>
            {
                var line = stored.Instruction.Lines.FirstOrDefault(candidate => candidate.Id == lineId)
                    ?? throw new LogisticsInstructionServiceException(404, "not_found");
                return await VerifyUnitAsync(scope, bindings.RecordPermission, line.UnitId, request.Quantity, stored.EffectiveOn, "load",
                    cancellationToken);
            }, cancellationToken);
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

    // ---- mutations ------------------------------------------------------------------------------

    private async Task<LoadingInstructionResponse> MutateAsync(Guid instructionId, object content, string expectedVersionText,
        LogisticsCommandMetadata metadata, string commandName, string? permission,
        Action<LoadingInstruction, long, DateTimeOffset, Quantity?> transition, LogisticsMutationAudit audit,
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
                (instruction, now) => transition(instruction, expectedVersion, now, prepared), audit, cancellationToken);
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
    private async Task<(SourceOrderLineRef Source, Guid ProductId)> ReadOrderLineAsync(Scope scope, CreateLoadingInstructionRequest request, int index,
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
                share.Measurement?.ToString(), share.SourceShipmentId, share.RecordedAtUtc))]);
    }
}
