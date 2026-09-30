using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.PriceLists;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

/// <summary>
/// AT-018 price lists of the server-resolved company (U-05). The Inventory/Logistics command shape:
/// <list type="bullet">
/// <item><b>Authority:</b> read = <see cref="ProductPriceListPermissionsV1.Read"/>, create / draft revision / new version /
/// product options = <see cref="ProductPriceListPermissionsV1.Write"/>, publication = <see cref="ProductPriceListPermissionsV1.Publish"/>;
/// each company-wide, measured fresh before anything is read (the replay included) and again right before the single
/// save; none after the commit. An unregistered name is 503; a denied, narrower or lost grant, another actor or company
/// is 403; no identity is 401.</item>
/// <item><b>Idempotency (ADR-011):</b> one attempt per (company, command, key hash); the same key + content + actor
/// answers the stored list with <c>isReplay</c>; another content or actor is 409 <c>idempotency_conflict</c>. A stale
/// version and a wrong version state are terminal refusals, recorded under the key and answered again.</item>
/// <item><b>Product checks</b> (the same module's card, read-only): every line's product must be an Active, sellable card
/// priced in its own stock or sales unit — on every draft save and again at publication. A list of another company is
/// invisible (404).</item>
/// </list>
/// </summary>
public sealed class ProductPriceListService : IProductPriceListServiceV1
{
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 100;
    private const int ProductOptionLimit = 50;
    private const int QueryMaximumLength = 200;
    private const int ProductActive = (int)Domain.Products.ProductLifecycle.Active;

    private readonly ICompanyContext companyContext;
    private readonly ICurrentUser currentUser;
    private readonly IErpAuthorityRevalidationService authority;
    private readonly ProductPriceListStore store;
    private readonly TimeProvider timeProvider;
    private readonly ISettingsUnitIdentityCatalogV1? unitCatalog;
    private readonly Func<string, bool> isKnownPermission;

    internal ProductPriceListService(
        ICompanyContext companyContext,
        ICurrentUser currentUser,
        IErpAuthorityRevalidationService authority,
        ProductPriceListStore store,
        TimeProvider timeProvider,
        ISettingsUnitIdentityCatalogV1? unitCatalog,
        Func<string, bool> isKnownPermission)
    {
        this.companyContext = companyContext ?? throw new ArgumentNullException(nameof(companyContext));
        this.currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.unitCatalog = unitCatalog;
        this.isKnownPermission = isKnownPermission ?? throw new ArgumentNullException(nameof(isKnownPermission));
    }

    // ---- reads ---------------------------------------------------------------------------------------

    public async Task<PriceListPageV1> ListAsync(int? pageSize, string? cursor, CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(ProductPriceListPermissionsV1.Read, cancellationToken);
        var size = pageSize ?? DefaultPageSize;
        if (size is < 1 or > MaximumPageSize || (cursor is not null && (cursor.Length is 0 or > 200)))
        {
            throw new ProductPriceListException(400, ProductPriceListCodesV1.ValidationFailed, [new("pageSize", ProductPriceListCodesV1.ValidationFailed)]);
        }

        (IReadOnlyList<PriceList> Items, string? NextCursor) page;
        try
        {
            page = await store.ListAsync(size, cursor, cancellationToken);
        }
        catch (PriceListStoreException exception) when (exception.Code == PriceListStoreCodes.InvalidCursor)
        {
            throw new ProductPriceListException(400, ProductPriceListCodesV1.InvalidCursor, null, exception);
        }

        await RequireStillAsync(ProductPriceListPermissionsV1.Read, scope, cancellationToken);
        var today = Today();
        return new PriceListPageV1(page.Items.Select(list => Summary(list, today)).ToList(), page.NextCursor);
    }

    public async Task<PriceListViewV1> GetAsync(Guid priceListId, CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(ProductPriceListPermissionsV1.Read, cancellationToken);
        var list = await store.LoadAsync(priceListId, cancellationToken)
            ?? throw new ProductPriceListException(404, ProductPriceListCodesV1.NotFound);
        var view = await ViewAsync(list, cancellationToken);
        await RequireStillAsync(ProductPriceListPermissionsV1.Read, scope, cancellationToken);
        return view;
    }

    public async Task<PriceListProductOptionsV1> GetProductOptionsAsync(string? query, CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(ProductPriceListPermissionsV1.Write, cancellationToken);
        var text = query?.Trim();
        if (text is { Length: > QueryMaximumLength } || (text is not null && text.Any(char.IsControl)))
        {
            throw new ProductPriceListException(400, ProductPriceListCodesV1.ValidationFailed, [new("q", ProductPriceListCodesV1.ValidationFailed)]);
        }

        var products = await store.SearchProductsAsync(text, ProductOptionLimit + 1, cancellationToken);
        await RequireStillAsync(ProductPriceListPermissionsV1.Write, scope, cancellationToken);
        var codes = await UnitCodesAsync(cancellationToken);
        await RequireStillAsync(ProductPriceListPermissionsV1.Write, scope, cancellationToken);
        var items = products.Take(ProductOptionLimit)
            .Select(product => new PriceListProductOptionV1(product.Id, product.Code, product.Name, UnitOptions(product, codes)))
            .Where(option => option.Units.Count > 0)
            .ToList();
        return new PriceListProductOptionsV1(items, products.Count > ProductOptionLimit);
    }

    // ---- commands ------------------------------------------------------------------------------------

    public async Task<PriceListResultV1> CreateAsync(CreatePriceListRequestV1 request, ProductPriceListCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        const string permission = ProductPriceListPermissionsV1.Write;
        var scope = await RequireAsync(permission, cancellationToken);
        var envelope = Envelope(metadata, permission, scope);
        var validFrom = RequireDate(request.ValidFrom, "validFrom");
        var validThrough = OptionalDate(request.ValidThrough, "validThrough");
        var lines = RequireLines(request.Lines);
        var channel = request.Channel == "Sales" ? PriceListChannel.Sales : (PriceListChannel)0;
        var vatBasis = request.VatBasis == "ExcludingVat" ? PriceListVatBasis.ExcludingVat : (PriceListVatBasis)0;
        var requestHash = RequestHash(ProductPriceListCommandsV1.CreateV1, request.PriceListId, request);

        if (await ReplayAsync(envelope, requestHash, ProductPriceListCommandsV1.CreateV1, permission, scope, cancellationToken) is { } replay)
        {
            return replay;
        }

        var list = Rule(() => PriceList.Create(request.PriceListId, scope.CompanyId, request.Name, request.CurrencyCode, channel, vatBasis,
            request.VersionId, validFrom, validThrough, lines, scope.ActorId, timeProvider.GetUtcNow()));
        await RequireProductsAsync(list.Versions[0].Lines, cancellationToken);
        await RequireStillAsync(permission, scope, cancellationToken);
        if (await store.NameExistsAsync(list.Name, null, cancellationToken))
        {
            throw new ProductPriceListException(409, ProductPriceListCodesV1.DuplicateName, [new("name", ProductPriceListCodesV1.DuplicateName)]);
        }

        await RequireStillAsync(permission, scope, cancellationToken);
        var stored = await StoreAsync(() => store.CreateAsync(envelope, requestHash, list, cancellationToken));
        return new PriceListResultV1(await ViewAsync(stored.PriceList, cancellationToken), false, stored.OriginalCorrelationId);
    }

    public async Task<PriceListResultV1> ReviseDraftAsync(Guid priceListId, RevisePriceListDraftRequestV1 request,
        ProductPriceListCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        const string permission = ProductPriceListPermissionsV1.Write;
        var scope = await RequireAsync(permission, cancellationToken);
        var envelope = Envelope(metadata, permission, scope);
        var expectedVersion = RequireVersion(request.ExpectedVersion);
        var validFrom = RequireDate(request.ValidFrom, "validFrom");
        var validThrough = OptionalDate(request.ValidThrough, "validThrough");
        var lines = RequireLines(request.Lines);
        var name = Rule(() => PriceList.NormalizeName(request.Name));
        var requestHash = RequestHash(ProductPriceListCommandsV1.ReviseDraftV1, priceListId, request);
        if (await ReplayAsync(envelope, requestHash, ProductPriceListCommandsV1.ReviseDraftV1, permission, scope, cancellationToken) is { } replay)
        {
            return replay;
        }

        if (await store.NameExistsAsync(name, priceListId, cancellationToken))
        {
            throw new ProductPriceListException(409, ProductPriceListCodesV1.DuplicateName, [new("name", ProductPriceListCodesV1.DuplicateName)]);
        }

        return await MutateAsync(envelope, requestHash, ProductPriceListCommandsV1.ReviseDraftV1, priceListId, permission, scope,
            new PriceListAudit("product.price-list.draft-revised.v1", "ReviseDraft", "Taslak fiyat sürümü düzenlendi"),
            async (list, now) =>
            {
                list.ReviseDraft(expectedVersion, name, validFrom, validThrough, lines, now);
                await RequireProductsAsync(list.DraftVersion!.Lines, cancellationToken);
            }, cancellationToken);
    }

    public async Task<PriceListResultV1> StartVersionAsync(Guid priceListId, StartPriceListVersionRequestV1 request,
        ProductPriceListCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        const string permission = ProductPriceListPermissionsV1.Write;
        var scope = await RequireAsync(permission, cancellationToken);
        var envelope = Envelope(metadata, permission, scope);
        var expectedVersion = RequireVersion(request.ExpectedVersion);
        var validFrom = RequireDate(request.ValidFrom, "validFrom");
        var validThrough = OptionalDate(request.ValidThrough, "validThrough");
        var requestHash = RequestHash(ProductPriceListCommandsV1.StartVersionV1, priceListId, request);
        if (await ReplayAsync(envelope, requestHash, ProductPriceListCommandsV1.StartVersionV1, permission, scope, cancellationToken) is { } replay)
        {
            return replay;
        }

        return await MutateAsync(envelope, requestHash, ProductPriceListCommandsV1.StartVersionV1, priceListId, permission, scope,
            new PriceListAudit("product.price-list.version-started.v1", "StartVersion", "Yeni fiyat sürümü açıldı"),
            (list, now) =>
            {
                list.StartNextVersion(expectedVersion, request.VersionId, validFrom, validThrough, scope.ActorId, now);
                return Task.CompletedTask;
            }, cancellationToken);
    }

    public async Task<PriceListResultV1> PublishAsync(Guid priceListId, PublishPriceListVersionRequestV1 request,
        ProductPriceListCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        const string permission = ProductPriceListPermissionsV1.Publish;
        var scope = await RequireAsync(permission, cancellationToken);
        var envelope = Envelope(metadata, permission, scope);
        var expectedVersion = RequireVersion(request.ExpectedVersion);
        if (request.VersionNo < 1)
        {
            throw new ProductPriceListException(400, ProductPriceListCodesV1.ValidationFailed, [new("versionNo", ProductPriceListCodesV1.ValidationFailed)]);
        }

        var requestHash = RequestHash(ProductPriceListCommandsV1.PublishV1, priceListId, request);
        if (await ReplayAsync(envelope, requestHash, ProductPriceListCommandsV1.PublishV1, permission, scope, cancellationToken) is { } replay)
        {
            return replay;
        }

        return await MutateAsync(envelope, requestHash, ProductPriceListCommandsV1.PublishV1, priceListId, permission, scope,
            new PriceListAudit("product.price-list.version-published.v1", "Publish", $"Fiyat sürümü {request.VersionNo} yayımlandı"),
            async (list, now) =>
            {
                // The draft's products are checked again: a card may have been deactivated since the draft was saved.
                if (list.Versions.SingleOrDefault(version => version.VersionNo == request.VersionNo) is { State: PriceListVersionState.Draft } draft
                    && list.Version == expectedVersion)
                {
                    await RequireProductsAsync(draft.Lines, cancellationToken);
                }

                list.Publish(expectedVersion, request.VersionNo, DateOnly.FromDateTime(now.UtcDateTime), scope.ActorId, now);
            }, cancellationToken);
    }

    // ---- the command core ------------------------------------------------------------------------

    private async Task<PriceListResultV1> MutateAsync(PriceListWriteEnvelope envelope, string requestHash, string commandName, Guid priceListId,
        string permission, Scope scope, PriceListAudit audit, Func<PriceList, DateTimeOffset, Task> transition, CancellationToken cancellationToken)
    {
        PriceListTerminalRefusal? refusal = null;
        PriceListStoreResult? stored;
        try
        {
            stored = await StoreAsync(() => store.MutateAsync(envelope, requestHash, commandName, priceListId, async (list, now) =>
            {
                await RequireStillAsync(permission, scope, cancellationToken);
                try
                {
                    await transition(list, now);
                }
                catch (PriceListVersionConflictException exception)
                {
                    refusal = new PriceListTerminalRefusal(409, ProductPriceListCodesV1.VersionConflict, [new("expectedVersion", ProductPriceListCodesV1.VersionConflict)]);
                    throw new ProductPriceListException(409, ProductPriceListCodesV1.VersionConflict, refusal.Errors, exception);
                }
                catch (PriceListRuleException exception) when (IsStateRule(exception.Code))
                {
                    refusal = new PriceListTerminalRefusal(409, ProductPriceListCodesV1.StateConflict, [new(exception.Field, exception.Code)]);
                    throw new ProductPriceListException(409, ProductPriceListCodesV1.StateConflict, refusal.Errors, exception);
                }
                catch (PriceListRuleException exception)
                {
                    throw new ProductPriceListException(422, ProductPriceListCodesV1.ValidationFailed, [new(exception.Field, exception.Code)], exception);
                }

                await RequireStillAsync(permission, scope, cancellationToken);
            }, audit, cancellationToken));
        }
        catch (ProductPriceListException) when (refusal is not null)
        {
            // ADR-011 §7: a stale version or a wrong version state is the key's terminal answer.
            await StoreAsync(async () =>
            {
                await store.RecordRefusalAsync(envelope, requestHash, commandName, refusal, cancellationToken);
                return (PriceListStoreResult?)null;
            });
            throw new ProductPriceListException(refusal.Status, refusal.Code, refusal.Errors);
        }

        if (stored is null)
        {
            throw new ProductPriceListException(404, ProductPriceListCodesV1.NotFound);
        }

        return new PriceListResultV1(await ViewAsync(stored.PriceList, cancellationToken), false, stored.OriginalCorrelationId);
    }

    private async Task<PriceListResultV1?> ReplayAsync(PriceListWriteEnvelope envelope, string requestHash, string commandName, string permission,
        Scope scope, CancellationToken cancellationToken)
    {
        var replay = await StoreAsync(() => store.FindReplayAsync(envelope, requestHash, commandName, cancellationToken));
        if (replay is null)
        {
            return null;
        }

        await RequireStillAsync(permission, scope, cancellationToken);
        if (replay.Refusal is { } refusal)
        {
            throw new ProductPriceListException(refusal.Status, refusal.Code, refusal.Errors);
        }

        return new PriceListResultV1(await ViewAsync(replay.PriceList!, cancellationToken), true, replay.OriginalCorrelationId);
    }

    private static async Task<T> StoreAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (PriceListStoreException exception)
        {
            throw exception.Code switch
            {
                PriceListStoreCodes.SameKeyPayloadMismatch => new ProductPriceListException(409, ProductPriceListCodesV1.IdempotencyConflict, null, exception),
                PriceListStoreCodes.ConcurrentChange => new ProductPriceListException(409, ProductPriceListCodesV1.ConcurrentChange, null, exception),
                PriceListStoreCodes.InvalidCursor => new ProductPriceListException(400, ProductPriceListCodesV1.InvalidCursor, null, exception),
                _ => new ProductPriceListException(403, ProductPriceListCodesV1.Forbidden, null, exception),
            };
        }
    }

    private static bool IsStateRule(string code) => code is PriceListRuleCodes.NoDraftVersion or PriceListRuleCodes.DraftVersionOpen
        or PriceListRuleCodes.NoPublishedVersion or PriceListRuleCodes.VersionNotFound or PriceListRuleCodes.VersionNotDraft
        or PriceListRuleCodes.TooManyVersions;

    private static T Rule<T>(Func<T> build)
    {
        try
        {
            return build();
        }
        catch (PriceListRuleException exception)
        {
            throw new ProductPriceListException(422, ProductPriceListCodesV1.ValidationFailed, [new(exception.Field, exception.Code)], exception);
        }
    }

    /// <summary>Every line's product: an Active, sellable card, priced in its own stock or sales unit. All refusals together.</summary>
    private async Task RequireProductsAsync(IReadOnlyList<PriceListLine> lines, CancellationToken cancellationToken)
    {
        var products = await store.ReadProductsAsync(lines.Select(line => line.ProductId).ToList(), cancellationToken);
        var errors = new List<ProductPriceListFieldErrorV1>();
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var field = $"lines[{index}]";
            if (!products.TryGetValue(line.ProductId, out var product))
            {
                errors.Add(new($"{field}.productId", ProductPriceListCodesV1.ProductInvalid));
                continue;
            }

            if (product.Lifecycle != ProductActive)
            {
                errors.Add(new($"{field}.productId", ProductPriceListCodesV1.ProductNotActive));
            }
            else if (product.Sellable != true)
            {
                errors.Add(new($"{field}.productId", ProductPriceListCodesV1.ProductNotSellable));
            }

            if (line.PriceUnitId != product.StockUnitId && line.PriceUnitId != product.SalesUnitId)
            {
                errors.Add(new($"{field}.priceUnitId", ProductPriceListCodesV1.PriceUnitUnsupported));
            }
        }

        if (errors.Count > 0)
        {
            throw new ProductPriceListException(422, ProductPriceListCodesV1.ValidationFailed, errors);
        }
    }

    // ---- wire → domain ---------------------------------------------------------------------------

    private static List<PriceListLineInput> RequireLines(IReadOnlyList<PriceListLineRequestV1>? lines)
    {
        if (lines is null || lines.Count > PriceList.MaximumLines)
        {
            throw new ProductPriceListException(400, ProductPriceListCodesV1.ValidationFailed, [new("lines", ProductPriceListCodesV1.ValidationFailed)]);
        }

        var errors = new List<ProductPriceListFieldErrorV1>();
        var inputs = new List<PriceListLineInput>(lines.Count);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line is null)
            {
                errors.Add(new($"lines[{index}]", ProductPriceListCodesV1.Required));
                continue;
            }

            var price = ProductPriceListWire.ParsePrice(line.UnitPrice);
            decimal? floor = null;
            if (price is null)
            {
                errors.Add(new($"lines[{index}].unitPrice", ProductPriceListCodesV1.InvalidDecimal));
            }

            if (line.FloorUnitPrice is not null)
            {
                floor = ProductPriceListWire.ParsePrice(line.FloorUnitPrice);
                if (floor is null)
                {
                    errors.Add(new($"lines[{index}].floorUnitPrice", ProductPriceListCodesV1.InvalidDecimal));
                }
            }

            if (price is { } unitPrice)
            {
                inputs.Add(new PriceListLineInput(line.LineId, line.ProductId, line.PriceUnitId, unitPrice, floor));
            }
        }

        if (errors.Count > 0)
        {
            throw new ProductPriceListException(400, ProductPriceListCodesV1.ValidationFailed, errors);
        }

        return inputs;
    }

    private static DateOnly RequireDate(string? text, string field) => ProductPriceListWire.ParseDate(text)
        ?? throw new ProductPriceListException(400, ProductPriceListCodesV1.ValidationFailed, [new(field, ProductPriceListCodesV1.InvalidDate)]);

    private static DateOnly? OptionalDate(string? text, string field) => text is null ? null : RequireDate(text, field);

    private static long RequireVersion(string? text) => ProductPriceListWire.ParseVersion(text)
        ?? throw new ProductPriceListException(400, ProductPriceListCodesV1.ValidationFailed, [new("expectedVersion", ProductPriceListCodesV1.ValidationFailed)]);

    private static string RequestHash(string commandName, Guid priceListId, object request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { command = commandName, priceListId, request }, ProductPriceListWire.Json)))).ToLowerInvariant();

    private static PriceListWriteEnvelope Envelope(ProductPriceListCommandMetadata metadata, string permission, Scope scope)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (string.IsNullOrWhiteSpace(metadata.IdempotencyKey) || metadata.IdempotencyKey.Length > 128
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || metadata.CorrelationId.Length > 128
            || string.IsNullOrWhiteSpace(metadata.SourceChannel))
        {
            throw new ProductPriceListException(400, ProductPriceListCodesV1.ValidationFailed, [new("Idempotency-Key", ProductPriceListCodesV1.ValidationFailed)]);
        }

        if (!string.IsNullOrEmpty(metadata.ActorId) && !string.Equals(metadata.ActorId, scope.ActorId, StringComparison.Ordinal))
        {
            throw Forbidden();
        }

        return new PriceListWriteEnvelope(scope.ActorId, permission, scope.RoleCodes, metadata.CorrelationId, metadata.CausationId,
            metadata.IdempotencyKey, metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent);
    }

    // ---- views -------------------------------------------------------------------------------------

    private DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    private async Task<PriceListViewV1> ViewAsync(PriceList list, CancellationToken cancellationToken)
    {
        var products = await store.ReadProductsAsync(list.Versions.SelectMany(version => version.Lines).Select(line => line.ProductId).ToList(),
            cancellationToken);
        var today = Today();
        return new PriceListViewV1(list.Id, list.Name, list.CurrencyCode, list.Channel.ToString(), list.VatBasis.ToString(),
            list.Version.ToString(CultureInfo.InvariantCulture), list.CreatedAtUtc, list.UpdatedAtUtc, list.ResolveOn(today)?.VersionNo,
            list.DraftVersion?.VersionNo,
            list.Versions.Select(version => new PriceListVersionViewV1(version.Id, version.VersionNo, version.State.ToString(),
                list.StatusOf(version, today).ToString(), ProductPriceListWire.FormatDate(version.ValidFrom),
                version.ValidThrough is { } through ? ProductPriceListWire.FormatDate(through) : null, version.CreatedAtUtc, version.CreatedBy,
                version.PublishedAtUtc, version.PublishedBy,
                version.Lines.Select(line => new PriceListLineViewV1(line.LineId, line.LineNo, line.ProductId,
                    products.TryGetValue(line.ProductId, out var product) ? product.Code : null,
                    products.TryGetValue(line.ProductId, out var named) ? named.Name : null,
                    line.PriceUnitId, ProductPriceListWire.FormatPrice(line.UnitPrice),
                    line.FloorUnitPrice is { } floor ? ProductPriceListWire.FormatPrice(floor) : null)).ToList())).ToList());
    }

    private static PriceListSummaryV1 Summary(PriceList list, DateOnly today)
    {
        var active = list.ResolveOn(today);
        return new PriceListSummaryV1(list.Id, list.Name, list.CurrencyCode, list.Channel.ToString(), list.VatBasis.ToString(),
            list.Version.ToString(CultureInfo.InvariantCulture), active?.VersionNo,
            active is null ? null : ProductPriceListWire.FormatDate(active.ValidFrom), list.DraftVersion?.VersionNo, list.Versions.Count,
            list.UpdatedAtUtc);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> UnitCodesAsync(CancellationToken cancellationToken)
    {
        if (unitCatalog is null)
        {
            return new Dictionary<Guid, string>();
        }

        var units = await unitCatalog.ListAsync(cancellationToken);
        return units.GroupBy(unit => unit.UnitId).ToDictionary(group => group.Key, group => group.First().Code);
    }

    private static List<PriceListProductUnitOptionV1> UnitOptions(PriceListProductFacts product, IReadOnlyDictionary<Guid, string> codes)
    {
        var options = new List<PriceListProductUnitOptionV1>();
        if (product.SalesUnitId is { } sales)
        {
            options.Add(new PriceListProductUnitOptionV1(sales, "Sales", codes.TryGetValue(sales, out var code) ? code : null));
        }

        if (product.StockUnitId is { } stock && stock != product.SalesUnitId)
        {
            options.Add(new PriceListProductUnitOptionV1(stock, "Stock", codes.TryGetValue(stock, out var code) ? code : null));
        }

        return options;
    }

    // ---- authority -------------------------------------------------------------------------------

    private async Task<Scope> RequireAsync(string permission, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new ProductPriceListException(401, ProductPriceListCodesV1.Unauthenticated);
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw Forbidden();
        }

        if (!isKnownPermission(permission))
        {
            // A name the central catalogue does not know is a setup gap, never a grant.
            throw new ProductPriceListException(503, ProductPriceListCodesV1.PermissionUnregistered);
        }

        var scope = new Scope(companyContext.CompanyId, currentUser.UserId!, []);
        return await MeasureAsync(permission, scope, cancellationToken);
    }

    /// <summary>A fence: the same actor and company, still granted company-wide, measured now.</summary>
    private Task RequireStillAsync(string permission, Scope scope, CancellationToken cancellationToken) =>
        MeasureAsync(permission, scope, cancellationToken);

    private async Task<Scope> MeasureAsync(string permission, Scope scope, CancellationToken cancellationToken)
    {
        if (!Matches(scope))
        {
            throw Forbidden();
        }

        var measured = await authority.RevalidatePermissionScopeAsync(permission, scope.CompanyId, cancellationToken);
        if (!measured.IsGranted || !measured.IsCompanyWide || !Matches(scope))
        {
            throw Forbidden();
        }

        return scope with { RoleCodes = [.. measured.RoleCodes.Order(StringComparer.Ordinal)] };
    }

    private bool Matches(Scope scope) => currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, scope.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId == scope.CompanyId
        && currentUser.CanAccessCompany(scope.CompanyId);

    private static ProductPriceListException Forbidden() => new(403, ProductPriceListCodesV1.Forbidden);

    private sealed record Scope(Guid CompanyId, string ActorId, IReadOnlyCollection<string> RoleCodes);
}
