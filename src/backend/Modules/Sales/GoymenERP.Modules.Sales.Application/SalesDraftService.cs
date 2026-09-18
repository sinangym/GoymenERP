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
/// Settings money profile for <see cref="SalesMoneyOperations.OrderDraftV1"/> on the business date →
/// per line the Product eligibility (three unit bindings) → the order unit's Settings profile.
/// Nothing is cached, defaulted or copied from an example; each owner refusal is its own answer.</item>
/// <item><b>Fail-closed contract deltas</b> (card decisions 2–3): a commercial field or an owner
/// reference that has no owner port yet is refused with 422, never stored unvalidated.</item>
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
    TimeProvider timeProvider) : ISalesDraftService
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

        // 2. Contract deltas the card fixed: fail-closed before any owner is asked.
        RefuseUnavailableOwnerReferences(request);
        RefuseCommercialFields(request);

        var requestHash = SalesDraftRequestHash.Compute(request);
        var envelope = Envelope(scope, metadata);

        // 3. Replay (ADR-011 §4 / §7): the same key, content and actor answers what it answered before.
        var replay = await GuardedAsync(scope, bindings.CreatePermission,
            () => store.FindReplayAsync(envelope, requestHash, cancellationToken), cancellationToken);
        if (replay is not null)
        {
            return Replayed(replay);
        }

        // 4. The owners, each answered for this company and business date. A stale product version
        // is recorded as the key's terminal result; if another caller of the same key wins that
        // record meanwhile, the winner's result is answered instead (ADR-011 §8; RaceWon).
        MoneyProfileSnapshot money;
        var lineInputs = new List<SalesDraftLineInput>(request.Lines.Count);
        try
        {
            await RequireAccountAsync(scope, request, cancellationToken);
            money = await ReadMoneyProfileAsync(scope, request, cancellationToken);
            for (var index = 0; index < request.Lines.Count; index++)
            {
                lineInputs.Add(await ReadLineAsync(scope, request, index, envelope, requestHash, cancellationToken));
            }
        }
        catch (RaceWon won)
        {
            return won.Response;
        }

        // 5. The domain builds the draft on the store's document number; the store keeps one save.
        var header = new SalesDraftHeader(request.PartyId, request.CompanyAccountId, request.CurrencyId, request.BusinessDate,
            request.RequestedDeliveryDate, request.Note);
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
            var won = await GuardedAsync(scope, bindings.CreatePermission,
                () => store.FindReplayAsync(envelope, requestHash, cancellationToken), cancellationToken);
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

        await RequireStillAsync(scope, bindings.CreatePermission, cancellationToken);
        return Response(stored.Draft, stored.Outcome == SalesDraftWriteOutcome.Replayed, stored.OriginalCorrelationId);
    }

    /// <summary>ADR-011 §4 / §7: what the key answered before — a stored draft as a 200 replay, a recorded refusal as the same refusal.</summary>
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

        SalesDraftPage page;
        try
        {
            page = await GuardedAsync(scope, bindings.ReadPermission,
                () => store.ListDraftsAsync(pageSize, query.Cursor, cancellationToken), cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        return new SalesOrderDraftListResponse([.. page.Items.Select(Summary)], page.NextCursor);
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
        new(scope.ActorId, bindings.CreatePermission!, scope.RoleCodes, metadata.CorrelationId, metadata.CausationId,
            metadata.IdempotencyKey, correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent);

    // ---- contract deltas (card decisions 2 and 3) ---------------------------------------------

    private static void RefuseUnavailableOwnerReferences(CreateSalesOrderDraftRequest request)
    {
        var errors = new List<SalesDraftFieldError>();
        foreach (var (field, reference) in new (string, SalesDraftOwnerReferenceV1?)[]
                 {
                     ("deliveryAddress", request.DeliveryAddress), ("contact", request.Contact), ("responsible", request.Responsible),
                     ("paymentTerms", request.PaymentTerms), ("sourceStrategy", request.SourceStrategy), ("package", request.Package),
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

    private static void RefuseCommercialFields(CreateSalesOrderDraftRequest request)
    {
        var errors = new List<SalesDraftFieldError>();
        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            if (line.UnitPrice is not null) errors.Add(new($"lines[{index}].unitPrice", SalesDraftProblemCodesV1.CommercialNotAvailable));
            if (line.PriceUnitId is not null) errors.Add(new($"lines[{index}].priceUnitId", SalesDraftProblemCodesV1.CommercialNotAvailable));
            if (line.DiscountPercent is not null) errors.Add(new($"lines[{index}].discountPercent", SalesDraftProblemCodesV1.CommercialNotAvailable));
        }

        if (errors.Count > 0)
        {
            throw new SalesDraftServiceException(422, SalesDraftProblemCodesV1.CommercialNotAvailable, errors);
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

    private async Task<SalesDraftLineInput> ReadLineAsync(Scope scope, CreateSalesOrderDraftRequest request, int index,
        SalesDraftWriteEnvelope envelope, string requestHash, CancellationToken cancellationToken)
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
            line.RequestedDeliveryDate, line.Note, Commercial: null, RequestedDeliverySlices: slices);
    }

    private static UnitBindingSnapshot Binding(ProductSalesUnitProfileV1 unit) =>
        new(unit.CompanyId, unit.UnitId, unit.ProfileId, unit.Version);

    /// <summary>The wire's plain decimal text (already shape-checked by <see cref="SalesDraftJson"/>), as the domain's value.</summary>
    private static decimal Decimal(string text) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new SalesDraftServiceException(400, "validation_failed", [new("quantity", "invalid_decimal_text")]);

    /// <summary>
    /// ADR-011 §7: a terminal refusal is recorded under the key before it is answered. If another
    /// caller of the same key took the record meanwhile (§8), the winner's result is answered: its
    /// refusal as a replay, its draft through <see cref="RaceWon"/>; a key left empty by the race
    /// answers this refusal unrecorded (the retry records or replays).
    /// </summary>
    private async Task<SalesDraftServiceException> TerminalAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken)
    {
        try
        {
            await store.RecordRefusalAsync(envelope, requestHash, refusal, cancellationToken);
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            var won = await store.FindReplayAsync(envelope, requestHash, cancellationToken);
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
            draft.Version.ToString(CultureInfo.InvariantCulture), SalesDraftApiV1.Rev0, draft.Header.PartyId,
            draft.Header.CompanyAccountId, draft.Header.CurrencyId, draft.Header.BusinessDate, draft.CreatedAtUtc, draft.Lines.Count);

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
            Reference(draft.Header.PaymentTerms), Reference(draft.Header.SourceStrategy), Reference(draft.Header.Package));

    private static SalesDraftOwnerReferenceV1? Reference(OwnerReference? reference) =>
        reference is null ? null : new SalesDraftOwnerReferenceV1(reference.Id, reference.ExpectedVersion);
}
