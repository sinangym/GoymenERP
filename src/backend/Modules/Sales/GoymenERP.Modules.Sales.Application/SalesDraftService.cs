using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Quantities;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// The order-draft use cases behind #257's three routes (Sales API slice; card #66 approved with
/// decisions 1–6). The route binds nothing but the server context; this service decides.
/// <list type="bullet">
/// <item><b>Authority</b> is a fresh measurement of the Sales permission through the central
/// <see cref="IErpAuthorityRevalidationService"/> (#276) before anything is read and again after
/// every awaited owner read; a granted, company-wide scope of the request's company and actor is
/// required. An unregistered name is a 503 setup problem, never a grant. Authority comes before
/// the replay is read (ADR-011 §5).</item>
/// <item><b>Replay (ADR-011 §2–§4, §7):</b> the stored attempt of (company, command, key hash)
/// answers first — the stored draft as <c>isReplay</c> with its first correlation, or the recorded
/// terminal refusal (the Product owner's <c>version_conflict</c>) again; another content or actor
/// is one 409.</item>
/// <item><b>Owners, in order:</b> Cari account (party + account + active + this company) →
/// the Cari contact information of that account when the request names a delivery address or a
/// contact (SALES-A2a: the reference must be an ACTIVE address / contact of the account at the
/// owner's version) → Settings money profile for <see cref="SalesMoneyOperations.OrderDraftV1"/> on
/// the business date → per line the Product eligibility (three unit bindings) → the order unit's
/// Settings profile. Nothing is cached, defaulted or copied from an example; each owner refusal is
/// its own answer.</item>
/// <item><b>Fail-closed contract deltas</b> (card decisions 2–3): a commercial field, or an owner
/// reference whose owner port does not exist yet (responsible, payment terms, source strategy,
/// package), is refused with 422, never stored unvalidated.</item>
/// <item><b>Domain decides</b> the draft; the store keeps what the domain accepted, one save with
/// the audit event.</item>
/// </list>
/// </summary>
public sealed class SalesDraftService(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    ICorrelationContext correlation,
    ISalesPartyAccountPortV1 accounts,
    ISettingsPrerequisiteProvider settings,
    IProductSalesEligibilityV1? products,
    ISalesOrderDraftStore store,
    SalesDraftServiceBindings bindings,
    TimeProvider timeProvider,
    ISalesPartyContactInformationPortV1? contactInformation = null) : ISalesDraftService
{
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 100;
    private const int MaximumCursorLength = 8192;

    public async Task<SalesOrderDraftResponse> CreateDraftAsync(CreateSalesOrderDraftRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();

        // 1. Authority first (ADR-011 §5): nothing — not even the replay — is read before it.
        var scope = await RequireAsync(bindings.CreatePermission, cancellationToken);

        // 1b. K3 (Sinan 5748258299): a line that carries a unit price, a price unit or a discount needs the
        // SEPARATE price-write permission of the same actor in the same company — the create permission
        // does not confer it. Asked only when a commercial field is present, still before anything is read
        // or recorded (a 403 is never a recorded terminal result), and re-measured after the awaited owner
        // reads like the create permission (#276).
        var priced = CommercialFields(request);
        if (priced.Count > 0)
        {
            await RequirePriceWriteAsync(scope, priced, cancellationToken);
        }

        // 2. Contract deltas the card fixed: fail-closed before any owner is asked.
        RefuseUnavailableOwnerReferences(request);

        var requestHash = SalesDraftRequestHash.Compute(request);
        var envelope = Envelope(scope, metadata);

        // 3. Replay (ADR-011 §4 / §7): the same key, content and actor answers what it answered before.
        var replay = await FindReplayAsync(scope, envelope, requestHash, cancellationToken);
        if (replay is not null)
        {
            return Replayed(replay);
        }

        // 4. The owners, each answered for this company and business date. A stale product version
        // is recorded as the key's terminal result; if another caller of the same key wins that
        // record meanwhile, the winner's result is answered instead (ADR-011 §8; RaceWon).
        MoneyProfileSnapshot money;
        OwnerReferences references;
        var lineInputs = new List<SalesDraftLineInput>(request.Lines.Count);
        try
        {
            await RequireAccountAsync(scope, request, cancellationToken);
            references = await ReadOwnerReferencesAsync(scope, request, envelope, requestHash, cancellationToken);
            money = await ReadMoneyProfileAsync(scope, request, cancellationToken);
            for (var index = 0; index < request.Lines.Count; index++)
            {
                lineInputs.Add(await ReadLineAsync(scope, request, money, index, envelope, requestHash, cancellationToken));
            }
        }
        catch (RaceWon won)
        {
            return won.Response;
        }

        if (priced.Count > 0)
        {
            // The price fence again after every awaited read, right before the single save (the last fence).
            await RequirePriceWriteAsync(scope, priced, cancellationToken);
        }

        // 5. The domain builds the draft on the store's document number; the store keeps one save.
        var header = new SalesDraftHeader(request.PartyId, request.CompanyAccountId, request.CurrencyId, request.BusinessDate,
            request.RequestedDeliveryDate, request.Note, DeliveryAddress: references.DeliveryAddress, Contact: references.Contact);
        var now = timeProvider.GetUtcNow();
        SalesDraftStoreResult stored;
        try
        {
            stored = await store.StoreDraftAsync(envelope, requestHash, documentNo =>
            {
                var draft = SalesOrderDraft.Create(Guid.NewGuid(), scope.CompanyId, documentNo, header, money, now);
                foreach (var input in lineInputs)
                {
                    draft.AddLine(draft.Version, input, now);
                }

                return draft;
            }, cancellationToken);
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
            // ADR-011 §8: the loser of a same-key race answers the winner's result, read afresh; only a
            // race that left nothing under this key (the number counter, a draft id) is the caller's
            // concurrent_change to retry.
            var won = await FindReplayAsync(scope, envelope, requestHash, cancellationToken);
            if (won is null)
            {
                throw Store(exception);
            }

            return Replayed(won);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        // The committed result is answered as it is. The authority was measured before anything was
        // read, after every awaited owner read and right before the single save (the last fence that
        // can still prevent the write); a fence after the commit could only hide a stored draft behind
        // a 403 — the caller would drop its key and create a second draft (S03, ORG #321 P3-1). The
        // next request of this key meets the fence again before the replay is read (ADR-011 §5).
        return Response(stored.Draft, stored.Outcome == SalesDraftWriteOutcome.Replayed, stored.OriginalCorrelationId);
    }

    /// <summary>
    /// The key's recorded result under the fence; the store's refusals keep their public meaning
    /// (another content or actor under the same key is 409 <c>same_key_payload_mismatch</c>, §3).
    /// </summary>
    private Task<SalesDraftReplay?> FindReplayAsync(Scope scope, SalesDraftWriteEnvelope envelope, string requestHash,
        CancellationToken cancellationToken) =>
        FindReplayAsync(scope, bindings.CreatePermission, envelope, requestHash, SalesDraftCommands.CreateV1, cancellationToken);

    private async Task<SalesDraftReplay?> FindReplayAsync(Scope scope, string? permission, SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, CancellationToken cancellationToken)
    {
        try
        {
            return await GuardedAsync(scope, permission,
                () => store.FindReplayAsync(envelope, requestHash, cancellationToken, commandName), cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }
    }

    /// <summary>ADR-011 §4 / §7: what the key answered before — a stored draft as the 201 replay, a recorded refusal as the same refusal.</summary>
    private static SalesOrderDraftResponse Replayed(SalesDraftReplay replay)
    {
        if (replay.Refusal is { } refusal)
        {
            throw new SalesDraftServiceException(refusal.Status, refusal.Code, refusal.Errors, isReplay: true,
                originalCorrelationId: replay.OriginalCorrelationId);
        }

        return Response(replay.Draft!, isReplay: true, replay.OriginalCorrelationId);
    }

    /// <summary>Raised inside the owner reads when another caller of the same key stored a draft first; carries that draft's replay.</summary>
    private sealed class RaceWon(SalesOrderDraftResponse response) : Exception
    {
        public SalesOrderDraftResponse Response { get; } = response;
    }

    public async Task<SalesOrderDraftDetailView> GetDraftDetailAsync(Guid draftId, CancellationToken cancellationToken = default)
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
            draft = await GuardedAsync(scope, bindings.ReadPermission,
                () => store.FindDraftAsync(draftId, cancellationToken), cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        // Another company's draft is null under the tenant filter: 404, never 403 (#257).
        return draft is null ? throw new SalesDraftServiceException(404, "not_found") : Detail(draft);
    }

    /// <summary>K3 Head B: the archived approved revisions of a draft, oldest first, under the draft read; another company's draft is 404.</summary>
    public async Task<SalesOrderDraftRevisionsResponse> GetDraftRevisionsAsync(Guid draftId, CancellationToken cancellationToken = default)
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
            draft = await GuardedAsync(scope, bindings.ReadPermission,
                () => store.FindDraftAsync(draftId, cancellationToken), cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        if (draft is null)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        return new SalesOrderDraftRevisionsResponse([.. draft.ArchivedRevisions.Select(Revision)]);
    }

    public async Task<SalesOrderDraftListResponse> GetDraftListAsync(SalesOrderDraftListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        var pageSize = query.PageSize == 0 ? DefaultPageSize : query.PageSize;
        if (pageSize is < 1 or > MaximumPageSize || query.Cursor?.Length > MaximumCursorLength
            || (query.Cursor is not null && string.IsNullOrWhiteSpace(query.Cursor)))
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }

        // SALES-A1: an exact approval state name narrows the list; anything else is a shape error, never "all".
        InternalApprovalState? approvalState = null;
        if (query.ApprovalState is not null)
        {
            if (!Enum.TryParse<InternalApprovalState>(query.ApprovalState, ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed)
                || !string.Equals(parsed.ToString(), query.ApprovalState, StringComparison.Ordinal))
            {
                throw new SalesDraftServiceException(400, "validation_failed", [new("approvalState", "validation_failed")]);
            }

            approvalState = parsed;
        }

        SalesDraftPage page;
        try
        {
            page = await GuardedAsync(scope, bindings.ReadPermission,
                () => store.ListDraftsAsync(pageSize, query.Cursor, cancellationToken, approvalState), cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        return new SalesOrderDraftListResponse([.. page.Items.Select(Summary)], page.NextCursor);
    }

    // ---- internal approval (SALES-A1) ------------------------------------------------------------

    public Task<SalesOrderDraftResponse> SubmitDraftAsync(Guid draftId, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        StateCommandAsync(draftId, request, metadata, SalesDraftCommands.SubmitV1, bindings.CreatePermission,
            (draft, actor, expected, now) => draft.Submit(expected, actor, now),
            new SalesDraftStateAudit("sales.order-draft.submitted.v1", "Submit", "Satış sipariş taslağı onaya gönderildi"),
            cancellationToken);

    public Task<SalesOrderDraftResponse> ApproveDraftAsync(Guid draftId, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        StateCommandAsync(draftId, request, metadata, SalesDraftCommands.ApproveV1, bindings.ApprovePermission,
            (draft, actor, expected, now) => draft.Approve(expected, actor, now),
            new SalesDraftStateAudit("sales.order-draft.approved.v1", "Approve", "Satış sipariş taslağı iç onay aldı"),
            cancellationToken);

    public Task<SalesOrderDraftResponse> RejectDraftAsync(Guid draftId, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        StateCommandAsync(draftId, request, metadata, SalesDraftCommands.RejectV1, bindings.ApprovePermission,
            (draft, actor, expected, now) => draft.Reject(expected, actor, request?.Reason, now),
            new SalesDraftStateAudit("sales.order-draft.rejected.v1", "Reject", "Satış sipariş taslağı iç onayda reddedildi"),
            cancellationToken);

    /// <summary>
    /// One internal-approval command: the fence for <paramref name="permission"/> first (§5), the key's
    /// replay next (§4/§7: the stored record, or the recorded terminal refusal), then the fence again right
    /// before the single save, and the store's one save. The domain decides the transition; a stale
    /// version or a wrong state is the key's TERMINAL result (§7: recorded, then replayed); a lost race
    /// answers the winner (§8). Nothing is measured after the commit (S03). Another company's draft, or
    /// none, is 404, never 403.
    /// </summary>
    // ---- commercial revision (K3 Head B, Sinan 5748258299) ---------------------------------------

    /// <summary>
    /// Replaces every line's price, price unit and discount under the price-write permission (the command's
    /// own permission, so a missing grant is a plain 403). The same key with the same content and actor
    /// answers the stored result again (a replay is never the "no change" refusal: the key decides first);
    /// a stale version or a pending draft is the key's terminal 409 (recorded, replayed); a line refusal is
    /// a 422 on `lines[i].<field>` with the domain's code, where i is the line's index in THIS request.
    /// </summary>
    public async Task<SalesOrderDraftResponse> ReviseCommercialAsync(Guid draftId, SalesDraftCommercialRevisionRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();

        var permission = bindings.PricePermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (!long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion) || expectedVersion < 1)
        {
            throw new SalesDraftServiceException(400, "validation_failed", [new("expectedVersion", "validation_failed")]);
        }

        const string commandName = SalesDraftCommands.ReviseCommercialV1;
        var requestHash = SalesDraftRequestHash.Compute(commandName, draftId, request);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            return Replayed(replay);
        }

        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var inputs = request.Lines
            .Select(line => new LineCommercialInput(line.LineId, new CommercialDraftFields(line.UnitPrice, line.PriceUnitId, line.DiscountPercent)))
            .ToArray();
        var audit = new SalesDraftStateAudit("sales.order-draft.commercial-revised.v1", "ReviseCommercial",
            "Satış sipariş taslağının fiyat ve iskontosu revize edildi");

        await RequireStillAsync(scope, permission, cancellationToken);
        SalesDraftStateResult? result;
        try
        {
            result = await store.ReviseDraftCommercialAsync(envelope, requestHash, commandName, draftId,
                (draft, now) => draft.ReviseCommercial(expectedVersion, inputs, scope.ActorId, now), audit, cancellationToken);
        }
        catch (SalesVersionConflictException exception)
        {
            throw await TerminalAsync(envelope, requestHash, new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.VersionConflict,
                [new("expectedVersion", exception.Code)]), cancellationToken, commandName);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.StateConflict)
        {
            throw await TerminalAsync(envelope, requestHash, new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.StateConflict,
                [new("approvalState", exception.Code)]), cancellationToken, commandName);
        }
        catch (SalesRuleException exception)
        {
            throw new SalesDraftServiceException(422, "validation_failed", [new(RevisionField(request, exception), exception.Code)]);
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

            return Replayed(won);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        if (result is null)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        return Response(result.Draft, result.Outcome == SalesDraftWriteOutcome.Replayed, result.OriginalCorrelationId);
    }

    /// <summary>The field a revision refusal is answered on: the line's index in the request and the field its code is about, else the whole `lines` member.</summary>
    private static string RevisionField(SalesDraftCommercialRevisionRequest request, SalesRuleException exception)
    {
        var member = exception.Code switch
        {
            SalesRuleCodes.PriceScaleExceeded or SalesRuleCodes.PriceUnitPairing => "unitPrice",
            SalesRuleCodes.PriceUnitUnsupported => "priceUnitId",
            SalesRuleCodes.DiscountOutOfRange or SalesRuleCodes.DiscountWithoutPrice => "discountPercent",
            _ => null,
        };
        if (member is null || exception.LineId is not { } lineId)
        {
            return "lines";
        }

        var index = request.Lines.ToList().FindIndex(line => line.LineId == lineId);
        return index < 0 ? "lines" : $"lines[{index}].{member}";
    }

    private async Task<SalesOrderDraftResponse> StateCommandAsync(Guid draftId, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, string commandName, string? permission,
        Action<SalesOrderDraft, string, long, DateTimeOffset> transition, SalesDraftStateAudit audit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();

        var scope = await RequireAsync(permission, cancellationToken);
        if (!long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion) || expectedVersion < 1)
        {
            throw new SalesDraftServiceException(400, "validation_failed", [new("expectedVersion", "validation_failed")]);
        }

        var requestHash = SalesDraftRequestHash.Compute(commandName, draftId, request);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            return Replayed(replay);
        }

        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        // The last fence that can still prevent the write; none after the commit (ORG #321 P3-1).
        await RequireStillAsync(scope, permission, cancellationToken);
        SalesDraftStateResult? result;
        try
        {
            result = await store.UpdateDraftStateAsync(envelope, requestHash, commandName, draftId,
                (draft, now) => transition(draft, scope.ActorId, expectedVersion, now), audit, cancellationToken);
        }
        catch (SalesVersionConflictException exception)
        {
            // §7: the caller's expected version is stale — recorded under the key, replayed on retry.
            throw await TerminalAsync(envelope, requestHash, new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.VersionConflict,
                [new("expectedVersion", exception.Code)]), cancellationToken, commandName);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.StateConflict)
        {
            // §7: the transition is not allowed from the draft's state — terminal for this key too.
            throw await TerminalAsync(envelope, requestHash, new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.StateConflict,
                [new("approvalState", exception.Code)]), cancellationToken, commandName);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.ApprovalReasonTooLong)
        {
            throw new SalesDraftServiceException(422, SalesDraftProblemCodesV1.ApprovalReasonTooLong, [new("reason", exception.Code)]);
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
            // §8: a lost same-key race answers the winner, read afresh; a lost draft-version race (another
            // key moved the draft) is the caller's concurrent_change to retry with a fresh read.
            var won = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
            if (won is null)
            {
                throw Store(exception);
            }

            return Replayed(won);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        if (result is null)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        return Response(result.Draft, result.Outcome == SalesDraftWriteOutcome.Replayed, result.OriginalCorrelationId);
    }

    // ---- authority ------------------------------------------------------------------------------

    private sealed record Scope(Guid CompanyId, string ActorId, IReadOnlyCollection<string> RoleCodes);

    /// <summary>The fence: server identity and company, then a fresh central measurement of the name.</summary>
    private async Task<Scope> RequireAsync(string? permission, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new SalesDraftServiceException(401, "unauthenticated");
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }

        if (string.IsNullOrWhiteSpace(permission) || !ErpPermissions.IsKnown(permission))
        {
            // A name the central catalogue does not know is a setup gap, reported as such (never a grant).
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.PermissionUnregistered);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId;
        var scope = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide || !Matches(companyId, actorId))
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }

        return new Scope(companyId, actorId, [.. scope.RoleCodes.Order(StringComparer.Ordinal)]);
    }

    private async Task RequireStillAsync(Scope scope, string? permission, CancellationToken cancellationToken)
    {
        var current = await RequireAsync(permission, cancellationToken);
        if (current.CompanyId != scope.CompanyId || !string.Equals(current.ActorId, scope.ActorId, StringComparison.Ordinal))
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }
    }

    /// <summary>An awaited read is released only under the authority a success would need (the Product fence, #276).</summary>
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

    private SalesDraftWriteEnvelope Envelope(Scope scope, SalesCommandMetadata metadata) =>
        Envelope(scope, metadata, bindings.CreatePermission!);

    private SalesDraftWriteEnvelope Envelope(Scope scope, SalesCommandMetadata metadata, string permission) =>
        new(scope.ActorId, permission, scope.RoleCodes, metadata.CorrelationId, metadata.CausationId,
            metadata.IdempotencyKey, correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent);

    // ---- contract deltas (card decisions 2 and 3) ---------------------------------------------

    // SALES-A2a: the delivery address and the contact have their owner port (the Cari contact information) and
    // are validated in ReadOwnerReferencesAsync; the other four still have none (T28, the Cari terms catalogue,
    // the Identity user list, the source module) and stay refused here, never stored unvalidated.
    private static void RefuseUnavailableOwnerReferences(CreateSalesOrderDraftRequest request)
    {
        var errors = new List<SalesDraftFieldError>();
        foreach (var (field, reference) in new (string, SalesDraftOwnerReferenceV1?)[]
                 {
                     ("responsible", request.Responsible), ("paymentTerms", request.PaymentTerms),
                     ("sourceStrategy", request.SourceStrategy), ("package", request.Package),
                 })
        {
            if (reference is not null)
            {
                errors.Add(new(field, SalesDraftProblemCodesV1.OwnerReferenceUnavailable));
            }
        }

        if (errors.Count > 0)
        {
            throw new SalesDraftServiceException(422, SalesDraftProblemCodesV1.OwnerReferenceUnavailable, errors);
        }
    }

    // ---- manual price and discount (K3, Sinan 5748258299) --------------------------------------

    /// <summary>Every commercial field the request carries, as the field errors a refusal would name.</summary>
    private static List<SalesDraftFieldError> CommercialFields(CreateSalesOrderDraftRequest request)
    {
        var fields = new List<SalesDraftFieldError>();
        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            if (line.UnitPrice is not null) fields.Add(new($"lines[{index}].unitPrice", SalesDraftProblemCodesV1.PriceWriteForbidden));
            if (line.PriceUnitId is not null) fields.Add(new($"lines[{index}].priceUnitId", SalesDraftProblemCodesV1.PriceWriteForbidden));
            if (line.DiscountPercent is not null) fields.Add(new($"lines[{index}].discountPercent", SalesDraftProblemCodesV1.PriceWriteForbidden));
        }

        return fields;
    }

    /// <summary>
    /// The separate price-write fence: the SAME actor in the SAME company as the create scope must hold
    /// the price permission now. Anything else — not granted, another actor, another company, a name the
    /// catalogue does not know (503, a setup gap) — refuses the whole request: nothing of a price is
    /// stored without it, and the create permission alone never confers it.
    /// </summary>
    private async Task RequirePriceWriteAsync(Scope scope, IReadOnlyList<SalesDraftFieldError> fields, CancellationToken cancellationToken)
    {
        try
        {
            await RequireStillAsync(scope, bindings.PricePermission, cancellationToken);
        }
        catch (SalesDraftServiceException exception) when (exception.Status == 403)
        {
            throw new SalesDraftServiceException(403, SalesDraftProblemCodesV1.PriceWriteForbidden, fields);
        }
    }

    // ---- owners ---------------------------------------------------------------------------------

    private async Task RequireAccountAsync(Scope scope, CreateSalesOrderDraftRequest request, CancellationToken cancellationToken)
    {
        var answer = await GuardedAsync(scope, bindings.CreatePermission,
            () => accounts.GetAuthorizedAsync(request.PartyId, request.CompanyAccountId, cancellationToken), cancellationToken);
        switch (answer.Outcome)
        {
            case SalesPartyAccountOutcomeV1.Found when answer.Account is { } account:
                if (account.PartyId != request.PartyId || account.CompanyAccountId != request.CompanyAccountId)
                {
                    throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
                }

                if (account.CompanyId != scope.CompanyId)
                {
                    throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.AccountState,
                        [new("companyAccountId", "company_mismatch")]);
                }

                if (!account.PartyIsActive || !account.AccountIsActive)
                {
                    throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.AccountState,
                        [new("companyAccountId", "passive_company_account")]);
                }

                return;
            case SalesPartyAccountOutcomeV1.NotFound:
                throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.AccountState,
                    [new("partyId", "invalid_reference"), new("companyAccountId", "invalid_reference")]);
            case SalesPartyAccountOutcomeV1.Denied:
                throw new SalesDraftServiceException(403, "forbidden");
            default:
                throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.CounterpartyProviderUnavailable);
        }
    }

    private sealed record OwnerReferences(OwnerReference? DeliveryAddress, OwnerReference? Contact);

    /// <summary>
    /// SALES-A2a (Business 5745076917 item 2): the delivery address and the contact are the Cari owner's
    /// operational address / contact of the draft's party account, read ONCE through
    /// <see cref="ISalesPartyContactInformationPortV1"/> after the account answered, under the same fence
    /// as every owner read. A reference the owner does not list, or lists inactive, is 422
    /// <see cref="SalesDraftProblemCodesV1.OwnerReferenceInvalid"/> (not recorded: the next attempt asks
    /// again); a reference at another version than the owner's is the terminal 409
    /// <see cref="SalesDraftProblemCodesV1.VersionConflict"/> of ADR-011 §7, recorded under the key like the
    /// Product owner's stale version. The draft stores the identity and the owner's version — never a copy of
    /// the address or the person. Without a reference the port is not asked; without a port a reference is 503.
    /// </summary>
    private async Task<OwnerReferences> ReadOwnerReferencesAsync(Scope scope, CreateSalesOrderDraftRequest request,
        SalesDraftWriteEnvelope envelope, string requestHash, CancellationToken cancellationToken)
    {
        if (request.DeliveryAddress is null && request.Contact is null)
        {
            return new OwnerReferences(null, null);
        }

        if (contactInformation is null)
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.CounterpartyProviderUnavailable);
        }

        var answer = await GuardedAsync(scope, bindings.CreatePermission,
            () => contactInformation.GetContactInformationAsync(request.PartyId, request.CompanyAccountId, cancellationToken), cancellationToken);
        SalesPartyContactInformationV1 information;
        switch (answer.Outcome)
        {
            case SalesPartyContactInformationOutcomeV1.Found when answer.Information is { } found:
                // The account read already bound the account to this company; an answer for another party, account or
                // company here is the provider contradicting itself, not the user's input: 503, never a user error.
                if (found.PartyId != request.PartyId || found.CompanyAccountId != request.CompanyAccountId
                    || found.CompanyId != scope.CompanyId)
                {
                    throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
                }

                information = found;
                break;
            case SalesPartyContactInformationOutcomeV1.NotFound:
                throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.AccountState,
                    [new("partyId", "invalid_reference"), new("companyAccountId", "invalid_reference")]);
            case SalesPartyContactInformationOutcomeV1.Denied:
                throw new SalesDraftServiceException(403, "forbidden");
            default:
                throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.CounterpartyProviderUnavailable);
        }

        var invalid = new List<SalesDraftFieldError>();
        var stale = new List<SalesDraftFieldError>();
        var deliveryAddress = Resolve("deliveryAddress", request.DeliveryAddress,
            information.Addresses.Select(address => (address.Id, address.Version, address.IsActive)), invalid, stale);
        var contact = Resolve("contact", request.Contact,
            information.Contacts.Select(person => (person.Id, person.Version, person.IsActive)), invalid, stale);
        if (invalid.Count > 0)
        {
            throw new SalesDraftServiceException(422, SalesDraftProblemCodesV1.OwnerReferenceInvalid, invalid);
        }

        if (stale.Count > 0)
        {
            throw await TerminalAsync(envelope, requestHash,
                new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.VersionConflict, stale), cancellationToken);
        }

        return new OwnerReferences(deliveryAddress, contact);
    }

    /// <summary>One owner item the request named: listed and active → the stored reference; otherwise its field error.</summary>
    private static OwnerReference? Resolve(string field, SalesDraftOwnerReferenceV1? requested,
        IEnumerable<(Guid Id, string Version, bool IsActive)> owned, List<SalesDraftFieldError> invalid, List<SalesDraftFieldError> stale)
    {
        if (requested is null)
        {
            return null;
        }

        var matches = owned.Where(item => item.Id == requested.Id).ToArray();
        if (matches.Length > 1 || (matches.Length == 1 && (matches[0].Id == Guid.Empty || string.IsNullOrWhiteSpace(matches[0].Version))))
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
        }

        if (matches.Length == 0 || !matches[0].IsActive)
        {
            invalid.Add(new(field, SalesDraftProblemCodesV1.OwnerReferenceInvalid));
            return null;
        }

        if (!string.Equals(matches[0].Version, requested.ExpectedVersion.Trim(), StringComparison.Ordinal))
        {
            stale.Add(new(field, SalesDraftProblemCodesV1.VersionConflict));
            return null;
        }

        // The owner's version text as the owner returned it (the comparison was ordinal, so they are equal).
        return new OwnerReference(requested.Id, matches[0].Version);
    }

    private async Task<MoneyProfileSnapshot> ReadMoneyProfileAsync(Scope scope, CreateSalesOrderDraftRequest request,
        CancellationToken cancellationToken)
    {
        MoneyProfileV1 profile;
        try
        {
            profile = await GuardedAsync(scope, bindings.CreatePermission,
                () => settings.GetMoneyProfileAsync(request.CurrencyId, SalesMoneyOperations.OrderDraftV1, request.BusinessDate,
                    cancellationToken), cancellationToken);
        }
        catch (SettingsPrerequisiteException exception)
        {
            throw Settings(exception, "currencyId", SettingsPrerequisiteCodesV1.MoneyProfileNotFound,
                SalesDraftProblemCodesV1.UnsupportedCurrency);
        }

        if (profile.CompanyId != scope.CompanyId || profile.CurrencyId != request.CurrencyId
            || !string.Equals(profile.Operation, SalesMoneyOperations.OrderDraftV1, StringComparison.Ordinal)
            || profile.Profile.ProfileId == Guid.Empty || profile.Profile.Version <= 0 || profile.Scale < 0)
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
        }

        // Validity (active, in effect on the business date) is the domain's rule: sales_money_profile_not_in_effect.
        return new MoneyProfileSnapshot(profile.CompanyId, profile.CurrencyId, profile.Operation, profile.Profile.ProfileId,
            profile.Profile.Version.ToString(CultureInfo.InvariantCulture), request.BusinessDate, profile.IsActive,
            profile.EffectiveFrom, profile.EffectiveThrough, profile.Scale);
    }

    private async Task<SalesDraftLineInput> ReadLineAsync(Scope scope, CreateSalesOrderDraftRequest request, MoneyProfileSnapshot money,
        int index, SalesDraftWriteEnvelope envelope, string requestHash, CancellationToken cancellationToken)
    {
        var line = request.Lines[index];
        var productField = $"lines[{index}].productId";
        var unitField = $"lines[{index}].orderUnitId";
        if (products is null)
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ProductEligibilityUnavailable);
        }

        ProductSalesEligibilityV1 eligibility;
        try
        {
            eligibility = await GuardedAsync(scope, bindings.CreatePermission,
                () => products.EvaluateAsync(new ProductSalesSelectionV1(line.ProductId, line.ExpectedProductVersion, line.OrderUnitId,
                    request.BusinessDate), cancellationToken), cancellationToken);
        }
        catch (ProductOperationException exception)
        {
            throw exception.StatusCode switch
            {
                401 => new SalesDraftServiceException(401, "unauthenticated"),
                403 => new SalesDraftServiceException(403, "forbidden"),
                409 => await TerminalAsync(envelope, requestHash,
                    new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.VersionConflict, [new(productField, exception.Code)]),
                    cancellationToken),
                400 or 404 or 422 => new SalesDraftServiceException(422, "unsupported_product", [new(productField, exception.Code)]),
                _ => new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ProductEligibilityUnavailable),
            };
        }

        if (eligibility.Snapshot is not { } snapshot)
        {
            var errors = (eligibility.Errors ?? []).Select(error => new SalesDraftFieldError($"lines[{index}].{error.Field}", error.Code)).ToArray();
            if (errors.Length == 0)
            {
                throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
            }

            // A stale product version is the terminal refusal of ADR-011 §7: recorded and replayed under this key.
            if (errors.Any(error => error.Code == ProductApiV1.VersionConflict))
            {
                throw await TerminalAsync(envelope, requestHash,
                    new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.VersionConflict, errors), cancellationToken);
            }

            throw new SalesDraftServiceException(422, "unsupported_product", errors);
        }

        if (snapshot.ProductId != line.ProductId || snapshot.EffectiveOn != request.BusinessDate
            || string.IsNullOrWhiteSpace(snapshot.ProductVersion) || string.IsNullOrWhiteSpace(snapshot.Code)
            || string.IsNullOrWhiteSpace(snapshot.Name)
            || new[] { snapshot.StockUnit, snapshot.SalesUnit, snapshot.OrderUnit }.Any(unit =>
                unit is null || unit.CompanyId != scope.CompanyId || unit.UnitId == Guid.Empty || unit.ProfileId == Guid.Empty
                || string.IsNullOrWhiteSpace(unit.Version)))
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
        }

        if (snapshot.OrderUnit.UnitId != line.OrderUnitId)
        {
            throw new SalesDraftServiceException(422, "unsupported_unit", [new(unitField, "unsupported_unit")]);
        }

        UnitProfileV1 profile;
        try
        {
            profile = await GuardedAsync(scope, bindings.CreatePermission,
                () => settings.GetUnitProfileAsync(line.OrderUnitId, request.BusinessDate, cancellationToken), cancellationToken);
        }
        catch (SettingsPrerequisiteException exception)
        {
            throw Settings(exception, unitField, SettingsPrerequisiteCodesV1.UnitProfileNotFound, "unsupported_unit");
        }

        if (profile.CompanyId != scope.CompanyId || profile.UnitId != line.OrderUnitId
            || profile.Profile.ProfileId != snapshot.OrderUnit.ProfileId
            || profile.Profile.Version.ToString(CultureInfo.InvariantCulture) != snapshot.OrderUnit.Version
            || profile.Scale < 0 || string.IsNullOrWhiteSpace(profile.UnitCode) || string.IsNullOrWhiteSpace(profile.DimensionCode))
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
        }

        var product = new ProductSnapshot(snapshot.ProductId, snapshot.ProductVersion, snapshot.Code, snapshot.Name, snapshot.EffectiveOn,
            Binding(snapshot.StockUnit), Binding(snapshot.SalesUnit), Binding(snapshot.OrderUnit));
        var orderUnit = new UnitProfileSnapshot(profile.CompanyId, profile.UnitId, profile.UnitCode, profile.DimensionCode,
            profile.Profile.ProfileId, profile.Profile.Version.ToString(CultureInfo.InvariantCulture), profile.IsKilogram,
            profile.Scale, Math.Min(profile.Scale, Quantity.AllowedScale(profile.IsKilogram ? QuantityUnit.Kilogram : QuantityUnit.Piece)),
            profile.IsActive, profile.EffectiveFrom, profile.EffectiveThrough);
        var slices = line.RequestedDeliverySlices?
            .Select(slice => new DeliverySliceInput(slice.SliceId, slice.OrderUnitId, slice.Quantity, Decimal(slice.Quantity),
                slice.RequestedDeliveryDate))
            .ToArray();
        return new SalesDraftLineInput(line.LineId, product, orderUnit, line.Quantity, Decimal(line.Quantity),
            line.RequestedDeliveryDate, line.Note, Commercial: Commercial(line, index, product, money), RequestedDeliverySlices: slices);
    }

    /// <summary>
    /// K3: the line's commercial fields as the domain takes them, checked here first so a refusal names the
    /// field (`lines[i].unitPrice` …) — the domain applies the same rules again on the draft (its word is
    /// final). Mathematical validity only: the price's decimals against the approved money profile's scale
    /// (Ö2, never rounded; zero is an explicit price), the price unit among the product owner's three unit
    /// bindings, the discount within 0..100 and never without a price. No threshold is invented (D5), and
    /// no amount is computed. Null stays null.
    /// </summary>
    private static CommercialDraftFields? Commercial(SalesOrderDraftLineRequest line, int index, ProductSnapshot product, MoneyProfileSnapshot money)
    {
        if (line.UnitPrice is null && line.PriceUnitId is null && line.DiscountPercent is null)
        {
            return null;
        }

        // The wire already required the pairing and the decimal shape (SalesDraftJson.ReadCreate).
        if (line.UnitPrice is { } price)
        {
            if (SalesOrderDraft.DecimalPlaces(price) > money.Scale)
            {
                throw new SalesDraftServiceException(422, "validation_failed", [new($"lines[{index}].unitPrice", SalesRuleCodes.PriceScaleExceeded)]);
            }

            var priceUnit = line.PriceUnitId!.Value;
            if (priceUnit != product.StockUnit.UnitId && priceUnit != product.SalesUnit.UnitId && priceUnit != product.OrderUnit.UnitId)
            {
                throw new SalesDraftServiceException(422, "validation_failed", [new($"lines[{index}].priceUnitId", SalesRuleCodes.PriceUnitUnsupported)]);
            }
        }

        if (line.DiscountPercent is { } discount)
        {
            if (line.UnitPrice is null)
            {
                throw new SalesDraftServiceException(422, "validation_failed", [new($"lines[{index}].discountPercent", SalesRuleCodes.DiscountWithoutPrice)]);
            }

            if (!SalesOrderDraft.IsPercent(discount))
            {
                throw new SalesDraftServiceException(422, "validation_failed", [new($"lines[{index}].discountPercent", SalesRuleCodes.DiscountOutOfRange)]);
            }
        }

        return new CommercialDraftFields(line.UnitPrice, line.PriceUnitId, line.DiscountPercent);
    }

    private static UnitBindingSnapshot Binding(ProductSalesUnitProfileV1 unit) =>
        new(unit.CompanyId, unit.UnitId, unit.ProfileId, unit.Version);

    // decimal(18,3): at most 15 integer digits; a wider text is refused at the wire, never a provider exception at the save.
    private const int MaximumIntegerDigits = 15;

    /// <summary>
    /// The wire's plain decimal text as the domain's value — Ö2 "never rounded" (FİNANS #350 P2-1, the same pattern here): the
    /// text is validated lexically FIRST — digits and one inner '.', a non-zero digit, at most the kilogram cap of decimals
    /// (Settings' <see cref="ExactPositiveDecimalV1"/>, the Finance / Inventory shape; a count unit's tighter scale is the
    /// domain's <c>AppliedScale</c>) and at most 15 integer digits — and only the validated text is converted, so a text with
    /// more digits than a decimal holds can never round ("2.9999999999999999999999999999999" is refused; it never became 3 with
    /// the verbatim text kept beside it). <see cref="SalesDraftJson"/> checks the shape only.
    /// </summary>
    private static decimal Decimal(string text)
    {
        try
        {
            ExactPositiveDecimalV1.Parse(text, Quantity.KilogramScale);
        }
        catch (SettingsPrerequisiteException exception) when (exception.Code == "settings_decimal_scale_exceeded")
        {
            throw new SalesDraftServiceException(422, "validation_failed", [new("quantity", SalesRuleCodes.QuantityScaleExceeded)]);
        }
        catch (SettingsPrerequisiteException)
        {
            throw new SalesDraftServiceException(400, "validation_failed", [new("quantity", "invalid_decimal_text")]);
        }

        var point = text.IndexOf('.', StringComparison.Ordinal);
        var integerPart = point < 0 ? text : text[..point];
        if (integerPart.TrimStart('0').Length > MaximumIntegerDigits)
        {
            throw new SalesDraftServiceException(422, "validation_failed", [new("quantity", SalesDraftStoreCodes.ValueDoesNotFit)]);
        }

        // At most 18 significant digits by now: the conversion is exact.
        return decimal.Parse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// ADR-011 §7: a terminal refusal is recorded under the key before it is answered. If another
    /// caller of the same key took the record meanwhile (§8), the winner's result is answered: its
    /// refusal as a replay, its draft through <see cref="RaceWon"/>; a key left empty by the race
    /// answers this refusal unrecorded (the retry records or replays).
    /// </summary>
    private async Task<SalesDraftServiceException> TerminalAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken, string commandName = SalesDraftCommands.CreateV1)
    {
        try
        {
            await store.RecordRefusalAsync(envelope, requestHash, refusal, cancellationToken, commandName);
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            SalesDraftReplay? won;
            try
            {
                won = await store.FindReplayAsync(envelope, requestHash, cancellationToken, commandName);
            }
            catch (SalesDraftStoreException raced)
            {
                return Store(raced);
            }

            if (won is not null)
            {
                if (won.Refusal is { } recorded)
                {
                    return new SalesDraftServiceException(recorded.Status, recorded.Code, recorded.Errors, isReplay: true,
                        originalCorrelationId: won.OriginalCorrelationId);
                }

                throw new RaceWon(Response(won.Draft!, isReplay: true, won.OriginalCorrelationId));
            }
        }

        return new SalesDraftServiceException(refusal.Status, refusal.Code, refusal.Errors);
    }

    private static SalesDraftServiceException Settings(SettingsPrerequisiteException exception, string field,
        string notFoundCode, string notFoundAnswer) => exception.Code switch
    {
        "settings_access_denied" => new SalesDraftServiceException(403, "settings_access_denied"),
        _ when exception.Code == notFoundCode => new SalesDraftServiceException(422, notFoundAnswer, [new(field, notFoundAnswer)]),
        _ => new SalesDraftServiceException(503, "settings_provider_unavailable"),
    };

    private static SalesDraftServiceException Store(SalesDraftStoreException exception) => exception.Code switch
    {
        SalesDraftStoreCodes.SameKeyPayloadMismatch => new SalesDraftServiceException(409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch),
        SalesDraftStoreCodes.ConcurrentChange => new SalesDraftServiceException(409, SalesDraftProblemCodesV1.ConcurrentChange),
        SalesDraftStoreCodes.InvalidCursor => new SalesDraftServiceException(400, SalesDraftProblemCodesV1.InvalidCursor),
        SalesDraftStoreCodes.CompanyContextMismatch => new SalesDraftServiceException(403, "forbidden"),
        SalesDraftStoreCodes.ValueDoesNotFit => new SalesDraftServiceException(422, "validation_failed", [new("draft", exception.Code)]),
        _ => new SalesDraftServiceException(503, "service_unavailable"),
    };

    // ---- mapping --------------------------------------------------------------------------------

    private static SalesOrderDraftResponse Response(SalesOrderDraft draft, bool isReplay, string originalCorrelationId) =>
        new(Detail(draft), isReplay, originalCorrelationId);

    private static SalesOrderDraftSummaryView Summary(SalesOrderDraft draft) =>
        new(draft.Id, draft.CompanyId, draft.DocumentNo, draft.Kind.ToString(), draft.State.ToString(),
            draft.Version.ToString(CultureInfo.InvariantCulture), SalesDraftApiV1.RevisionText(draft.Revision), draft.Header.PartyId,
            draft.Header.CompanyAccountId, draft.Header.CurrencyId, draft.Header.BusinessDate, draft.CreatedAtUtc, draft.Lines.Count,
            draft.ApprovalState.ToString());

    private static SalesOrderDraftDetailView Detail(SalesOrderDraft draft) =>
        new(Summary(draft), draft.Header.RequestedDeliveryDate, draft.Header.Note,
            [.. draft.Lines.Select(line => new SalesOrderDraftLineDetail(line.Id, line.LineNo, line.Product.ProductId,
                line.Product.ProductVersion, line.Product.Code, line.Product.Name, line.OrderUnit.UnitId, line.QuantityText,
                line.Commercial.UnitPrice, line.Commercial.PriceUnitId, line.RequestedDeliveryDate, line.Note,
                line.RequestedDeliverySlices.Count == 0
                    ? null
                    : [.. line.RequestedDeliverySlices.Select(slice => new SalesDraftDeliverySliceV1(slice.SliceId, line.OrderUnit.UnitId,
                        slice.QuantityText, slice.RequestedDeliveryDate))],
                line.Commercial.DiscountPercent))],
            Reference(draft.Header.DeliveryAddress), Reference(draft.Header.Contact), Reference(draft.Header.Responsible),
            Reference(draft.Header.PaymentTerms), Reference(draft.Header.SourceStrategy), Reference(draft.Header.Package),
            new SalesDraftApprovalV1(draft.Approval.SubmittedBy, draft.Approval.SubmittedAtUtc, draft.Approval.DecidedBy,
                draft.Approval.DecidedAtUtc, draft.Approval.RejectionReason));

    private static SalesDraftRevisionV1 Revision(ApprovedRevisionSnapshot archived) =>
        new(SalesDraftApiV1.RevisionText(archived.Revision), archived.Approval.SubmittedBy, archived.Approval.SubmittedAtUtc,
            archived.Approval.DecidedBy, archived.Approval.DecidedAtUtc, archived.ArchivedAtUtc, archived.ArchivedBy,
            archived.MoneyProfileId, archived.MoneyProfileVersion, archived.MoneyScale,
            [.. archived.Lines.Select(line => new SalesDraftRevisionLineV1(line.LineId, line.LineNo, line.ProductId, line.ProductVersion,
                line.OrderUnitId, line.QuantityText, line.Commercial.UnitPrice, line.Commercial.PriceUnitId, line.Commercial.DiscountPercent))]);

    private static SalesDraftOwnerReferenceV1? Reference(OwnerReference? reference) =>
        reference is null ? null : new SalesDraftOwnerReferenceV1(reference.Id, reference.ExpectedVersion);
}
