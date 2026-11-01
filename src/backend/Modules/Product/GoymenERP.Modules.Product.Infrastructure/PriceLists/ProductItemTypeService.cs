using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

/// <summary>
/// SAT-D134 (EK19) / SAT-D149 (EK20): the item type (Steel / Scrap / Service = demir / hurda / hizmet) of a product, held ONCE on
/// the shared product card — the same value in every company. The card holds no tax class and no tax exception; the tax is the
/// Settings default per item type (SAT-D139).
/// <list type="bullet">
/// <item><b>Authority</b> (SAT-D149): <see cref="ProductItemTypePermissionsV1.Manage"/> company-wide in the server-resolved
/// company — a holder in ANY company reads and changes the shared value — measured fresh first and again right before the
/// answer / the save; granted to no role here.</item>
/// <item><b>Audit</b>: one event per real change, written in the acting company (the row records it as
/// <c>UpdatedInCompanyId</c>; the before/after images name it).</item>
/// <item><b>Command</b>: one person, a mandatory reason (at most 4000), Idempotency-Key (the stored terminal answer replays), the
/// version as decimal text ("0" = none yet), correlation/causation and ONE audit event per real change.</item>
/// <item><b>Read port</b> (<see cref="IProductItemTypeReaderV1"/>): access only (authenticated member of the company), no
/// permission: the consumer fences its own operation.</item>
/// </list>
/// </summary>
internal sealed class ProductItemTypeService(
    ProductPriceListDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider) : IProductItemTypeServiceV1, IProductItemTypeReaderV1
{
    private const string Permission = ProductItemTypePermissionsV1.Manage;
    private const string SetCommandName = "product.item-type.set.v1";
    private const string EntityType = "Product.ItemType";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ProductItemTypeViewV1> GetAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var scope = await MeasureAsync(cancellationToken);
        if (productId == Guid.Empty)
        {
            throw new ProductItemTypeException(400, ProductItemTypeProblemCodesV1.RequestInvalid);
        }

        if (!await database.Products.AsNoTracking().AnyAsync(product => product.Id == productId, cancellationToken))
        {
            await MeasureAsync(cancellationToken);
            throw new ProductItemTypeException(404, null);
        }

        var row = await database.ItemTypes.AsNoTracking()
            .SingleOrDefaultAsync(itemType => itemType.ProductId == productId, cancellationToken);
        await MeasureAsync(cancellationToken);
        return row is null
            ? new ProductItemTypeViewV1(scope.CompanyId, productId, "0", null, null, null, null)
            : new ProductItemTypeViewV1(scope.CompanyId, productId, row.Version.ToString(CultureInfo.InvariantCulture),
                row.ItemType, row.UpdatedAtUtc, row.UpdatedBy, row.UpdatedInCompanyId);
    }

    public async Task<ProductItemTypeResultV1> SetAsync(Guid productId, SetProductItemTypeRequestV1 request,
        ProductItemTypeCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        if (!companyContext.IsResolved || metadata.CompanyId != companyContext.CompanyId
            || !string.Equals(metadata.ActorId, currentUser.UserId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || string.IsNullOrWhiteSpace(metadata.SourceChannel)
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey))
        {
            throw new InvalidOperationException("Ürün kalem tipi komut bağlamı geçersiz.");
        }

        var scope = await MeasureAsync(cancellationToken);
        if (productId == Guid.Empty || !TryParseVersion(request.ExpectedVersion, out var expectedVersion)
            || !ProductItemTypesV1.IsKnown(request.ItemType))
        {
            return Unstored(Problem(400, ProductItemTypeProblemCodesV1.RequestInvalid, "İstek geçersiz.",
                "Ürün, beklenen sürüm (\"0\" veya pozitif tam sayı metni) ve kalem tipi (Steel, Scrap, Service) zorunludur."), metadata);
        }

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            return Unstored(Problem(400, ProductItemTypeProblemCodesV1.ReasonRequired, "Gerekçe gerekli.",
                "Bu değişiklik için gerekçe zorunludur."), metadata);
        }

        if (reason.Length > ProductItemTypeInputLimitsV1.ReasonMaximumLength)
        {
            return Unstored(Problem(400, ProductItemTypeProblemCodesV1.ReasonTooLong, "Gerekçe çok uzun.",
                $"Gerekçe en fazla {ProductItemTypeInputLimitsV1.ReasonMaximumLength} karakter olabilir."), metadata);
        }

        var itemType = request.ItemType!;
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            ProductId = productId, ExpectedVersion = expectedVersion, ItemType = itemType, Reason = reason,
        });
        if (await FindAttemptAsync(keyHash, cancellationToken) is { } existing)
        {
            await MeasureAsync(cancellationToken);
            return Replay(existing, requestHash, metadata);
        }

        ProductItemTypeAnswerV1? refusal = null;
        if (!await database.Products.AsNoTracking().AnyAsync(product => product.Id == productId, cancellationToken))
        {
            refusal = new ProductItemTypeAnswerV1(404, null, "Ürün bulunamadı.", "İstenen ürün kartı yok.");
        }

        var row = refusal is null
            ? await database.ItemTypes.SingleOrDefaultAsync(current => current.ProductId == productId, cancellationToken)
            : null;
        if (refusal is null && (row?.Version ?? 0) != expectedVersion)
        {
            refusal = VersionConflict();
        }

        if (refusal is not null)
        {
            return await StoreAsync(refusal, productId, keyHash, requestHash, metadata, cancellationToken);
        }

        if (row is not null && string.Equals(row.ItemType, itemType, StringComparison.Ordinal))
        {
            // No change: no version move and no audit event; the key still records its answer.
            return await StoreAsync(NoContent(), productId, keyHash, requestHash, metadata, cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var before = row is null ? null : Snapshot(row);
        var versionBefore = row?.Version;
        if (row is null)
        {
            row = new ProductCardItemTypeRow
            {
                ProductId = productId, ItemType = itemType, Version = 1, UpdatedAtUtc = now, UpdatedBy = metadata.ActorId,
                UpdatedInCompanyId = scope.CompanyId,
            };
            database.ItemTypes.Add(row);
        }
        else
        {
            row.ItemType = itemType;
            row.Version = checked(row.Version + 1);
            row.UpdatedAtUtc = now;
            row.UpdatedBy = metadata.ActorId;
            row.UpdatedInCompanyId = scope.CompanyId;
        }

        var after = JsonSerializer.SerializeToNode(Snapshot(row), Json)!.AsObject();
        after["actingCompanyId"] = scope.CompanyId;
        after["reason"] = reason;
        auditEvents.Enqueue(new AuditEventDraft(scope.CompanyId, "product.item-type.changed.v1", EntityType,
            productId.ToString("D"), "SetItemType", versionBefore, row.Version, AuditActorTypes.User, metadata.ActorId,
            Permission, scope.RoleCodes, before is null ? "{}" : JsonSerializer.Serialize(before, Json), after.ToJsonString(Json),
            "Ortak ürün kartının kalem tipi değiştirildi; gerekçe kayıt içeriğindedir", ApprovalContextJson: null,
            AuditAiInvolvement.None, AiContextJson: null, metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent,
            metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
        await MeasureAsync(cancellationToken);
        AddAttempt(NoContent(), productId, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new ProductItemTypeResultV1(NoContent(), false, metadata.CorrelationId);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            if (await FindAttemptAsync(keyHash, cancellationToken) is { } winner)
            {
                return Replay(winner, requestHash, metadata);
            }

            return await StoreAsync(VersionConflict(), productId, keyHash, requestHash, metadata, cancellationToken);
        }
    }

    async Task<ProductItemTypeReadV1> IProductItemTypeReaderV1.GetAsync(Guid productId, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId) || !companyContext.IsResolved
            || companyContext.CompanyId == Guid.Empty || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            return new ProductItemTypeReadV1(ProductItemTypeOutcomeV1.Denied, null, null);
        }

        try
        {
            if (productId == Guid.Empty
                || !await database.Products.AsNoTracking().AnyAsync(product => product.Id == productId, cancellationToken))
            {
                return new ProductItemTypeReadV1(ProductItemTypeOutcomeV1.NotFound, null, null);
            }

            var row = await database.ItemTypes.AsNoTracking()
                .SingleOrDefaultAsync(itemType => itemType.ProductId == productId, cancellationToken);
            return row is null
                ? new ProductItemTypeReadV1(ProductItemTypeOutcomeV1.NotSet, null, null)
                : new ProductItemTypeReadV1(ProductItemTypeOutcomeV1.Found, row.ItemType, row.Version);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ProductItemTypeReadV1(ProductItemTypeOutcomeV1.Unavailable, null, null);
        }
    }

    private async Task<Scope> MeasureAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId) || !companyContext.IsResolved
            || companyContext.CompanyId == Guid.Empty || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw new UnauthorizedAccessException();
        }

        var measured = await authority.RevalidatePermissionScopeAsync(Permission, companyContext.CompanyId, cancellationToken);
        if (!measured.IsGranted || !measured.IsCompanyWide)
        {
            throw new UnauthorizedAccessException();
        }

        return new Scope(companyContext.CompanyId, [.. measured.RoleCodes.Order(StringComparer.Ordinal)]);
    }

    private Task<ProductItemTypeAttemptRow?> FindAttemptAsync(string keyHash, CancellationToken cancellationToken) =>
        database.ItemTypeAttempts.AsNoTracking().SingleOrDefaultAsync(
            attempt => attempt.CommandName == SetCommandName && attempt.IdempotencyKeyHash == keyHash, cancellationToken);

    private static ProductItemTypeResultV1 Replay(ProductItemTypeAttemptRow attempt, string requestHash,
        ProductItemTypeCommandMetadataV1 metadata)
    {
        if (!string.Equals(attempt.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return Unstored(Problem(409, ProductItemTypeProblemCodesV1.IdempotencyConflict,
                "İşlem anahtarı kullanılmış.", "Bu Idempotency-Key başka bir istekle kullanıldı."), metadata);
        }

        var answer = JsonSerializer.Deserialize<ProductItemTypeAnswerV1>(attempt.AnswerJson, Json);
        if (answer is null || answer.StatusCode != attempt.StatusCode)
        {
            throw new InvalidOperationException("Kalıcı idempotency sonucu ürün kalem tipi sözleşmesiyle uyumsuz.");
        }

        return new ProductItemTypeResultV1(answer, true, attempt.CorrelationId);
    }

    private void AddAttempt(ProductItemTypeAnswerV1 answer, Guid productId, string keyHash, string requestHash,
        ProductItemTypeCommandMetadataV1 metadata) =>
        database.ItemTypeAttempts.Add(new ProductItemTypeAttemptRow
        {
            CompanyId = metadata.CompanyId,
            CommandName = SetCommandName,
            IdempotencyKeyHash = keyHash,
            RequestHash = requestHash,
            ActorId = metadata.ActorId,
            ProductId = productId,
            StatusCode = answer.StatusCode,
            AnswerJson = JsonSerializer.Serialize(answer, Json),
            CorrelationId = metadata.CorrelationId,
            CreatedAtUtc = timeProvider.GetUtcNow(),
        });

    /// <summary>Stores a refusal or a no-op as the terminal answer of the key; a concurrent same-key winner replays instead.</summary>
    private async Task<ProductItemTypeResultV1> StoreAsync(ProductItemTypeAnswerV1 answer, Guid productId, string keyHash,
        string requestHash, ProductItemTypeCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        await MeasureAsync(cancellationToken);
        database.ChangeTracker.Clear();
        AddAttempt(answer, productId, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new ProductItemTypeResultV1(answer, false, metadata.CorrelationId);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            var winner = await FindAttemptAsync(keyHash, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return Replay(winner, requestHash, metadata);
        }
    }

    private static object Snapshot(ProductCardItemTypeRow row) => new
    {
        row.ProductId,
        row.ItemType,
        row.Version,
        row.UpdatedInCompanyId,
    };

    /// <summary>"0" or a positive decimal version without sign or leading zero.</summary>
    private static bool TryParseVersion(string? text, out long version)
    {
        version = 0;
        return !string.IsNullOrEmpty(text) && text.All(char.IsAsciiDigit) && (text.Length == 1 || text[0] != '0')
            && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out version);
    }

    private static ProductItemTypeAnswerV1 NoContent() => new(204, null, "Kaydedildi.", "Ürünün kalem tipi kaydedildi.");

    private static ProductItemTypeAnswerV1 VersionConflict() =>
        Problem(409, ProductItemTypeProblemCodesV1.VersionConflict, "Kayıt değişmiş.",
            "Ürünün kalem tipi siz açtıktan sonra değişti; güncel hâlini okuyup yeniden deneyin.");

    private static ProductItemTypeAnswerV1 Problem(int status, string code, string title, string detail) =>
        new(status, code, title, detail);

    private static ProductItemTypeResultV1 Unstored(ProductItemTypeAnswerV1 answer, ProductItemTypeCommandMetadataV1 metadata) =>
        new(answer, false, metadata.CorrelationId);

    private sealed record Scope(Guid CompanyId, IReadOnlyCollection<string> RoleCodes);
}
