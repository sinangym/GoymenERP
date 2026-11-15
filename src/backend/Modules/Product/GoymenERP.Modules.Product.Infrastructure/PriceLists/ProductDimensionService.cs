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
/// BS-9: the "Ebat/kalınlık" row of a product — the section kind and the measures the U-08 unit conversion reads — held ONCE on
/// the shared product card, the same value in every company (like the item type of SAT-D149). No density, no rounding and no
/// calculation here: the density is the company Settings value and the conversion is the Product calculator (STK-S11).
/// <list type="bullet">
/// <item><b>Authority</b>: <see cref="ProductDimensionPermissionsV1.Manage"/> company-wide in the server-resolved company — a
/// holder in ANY company reads and changes the shared row — measured fresh first and again right before the answer / the save;
/// granted to no role here.</item>
/// <item><b>Audit</b>: one event per real change, written in the acting company (the row records it as
/// <c>UpdatedInCompanyId</c>; the before/after images name it).</item>
/// <item><b>Command</b>: one person, a mandatory reason (at most 4000), Idempotency-Key (the stored terminal answer replays), the
/// version as decimal text ("0" = none yet), correlation/causation and ONE audit event per real change.</item>
/// <item><b>Read port</b> (<see cref="IProductDimensionReaderV1"/>): access only (authenticated member of the company), no
/// permission: the consumer fences its own operation; a product without a row is <c>NoSource</c>.</item>
/// <item><b>kg/m</b> (URN-D16, STK-S37R): never taken from the request and never stored on the card; the view and the read port
/// resolve it from the acting company's current imported table (<see cref="ProductProfileWeightTableService.FindAsync"/>) and
/// name its source label and table version. A profile whose (width, height, wall) the current table does not hold is refused
/// (<see cref="ProductDimensionProblemCodesV1.WeightSourceMissing"/>); a round tube's table value is optional.</item>
/// </list>
/// </summary>
internal sealed class ProductDimensionService(
    ProductPriceListDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider) : IProductDimensionServiceV1, IProductDimensionReaderV1
{
    private const string Permission = ProductDimensionPermissionsV1.Manage;
    private const string SetCommandName = "product.dimension.set.v1";
    private const string EntityType = "Product.Dimension";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ProductDimensionViewV1> GetAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var scope = await MeasureAsync(cancellationToken);
        if (productId == Guid.Empty)
        {
            throw new ProductDimensionException(400, ProductDimensionProblemCodesV1.RequestInvalid);
        }

        if (!await database.Products.AsNoTracking().AnyAsync(product => product.Id == productId, cancellationToken))
        {
            await MeasureAsync(cancellationToken);
            throw new ProductDimensionException(404, null);
        }

        var row = await database.Dimensions.AsNoTracking()
            .SingleOrDefaultAsync(dimension => dimension.ProductId == productId, cancellationToken);
        var weight = row is null ? null : await WeightOfAsync(row, scope.CompanyId, cancellationToken);
        await MeasureAsync(cancellationToken);
        return row is null
            ? new ProductDimensionViewV1(scope.CompanyId, productId, "0", null, null, null, null, null, null, null, null, null, null,
                null, null, null)
            : new ProductDimensionViewV1(scope.CompanyId, productId, row.Version.ToString(CultureInfo.InvariantCulture),
                row.SectionKind, row.ThicknessMm, row.WidthMm, row.LengthMm, row.HeightMm, row.OuterDiameterMm,
                row.WallThicknessMm, weight?.KgPerMetre, row.UpdatedAtUtc, row.UpdatedBy, row.UpdatedInCompanyId,
                weight?.SourceLabel, weight?.TableVersion.ToString(CultureInfo.InvariantCulture));
    }

    public async Task<ProductDimensionResultV1> SetAsync(Guid productId, SetProductDimensionRequestV1 request,
        ProductDimensionCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        if (!companyContext.IsResolved || metadata.CompanyId != companyContext.CompanyId
            || !string.Equals(metadata.ActorId, currentUser.UserId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || string.IsNullOrWhiteSpace(metadata.SourceChannel)
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey))
        {
            throw new InvalidOperationException("Ürün ebat/kalınlık komut bağlamı geçersiz.");
        }

        var scope = await MeasureAsync(cancellationToken);
        if (productId == Guid.Empty || !TryParseVersion(request.ExpectedVersion, out var expectedVersion))
        {
            return Unstored(Problem(400, ProductDimensionProblemCodesV1.RequestInvalid, "İstek geçersiz.",
                "Ürün ve beklenen sürüm (\"0\" veya pozitif tam sayı metni) zorunludur."), metadata);
        }

        if (ProductDimensionShape.Refusal(request, out var measures) is { } shapeRefusal)
        {
            return Unstored(Problem(400, ProductDimensionProblemCodesV1.MeasureInvalid, "Ölçüler geçersiz.", shapeRefusal),
                metadata);
        }

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            return Unstored(Problem(400, ProductDimensionProblemCodesV1.ReasonRequired, "Gerekçe gerekli.",
                "Bu değişiklik için gerekçe zorunludur."), metadata);
        }

        if (reason.Length > ProductDimensionInputLimitsV1.ReasonMaximumLength)
        {
            return Unstored(Problem(400, ProductDimensionProblemCodesV1.ReasonTooLong, "Gerekçe çok uzun.",
                $"Gerekçe en fazla {ProductDimensionInputLimitsV1.ReasonMaximumLength} karakter olabilir."), metadata);
        }

        var shape = measures!;
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            ProductId = productId, ExpectedVersion = expectedVersion, shape.SectionKind, shape.ThicknessMm, shape.WidthMm,
            shape.LengthMm, shape.HeightMm, shape.OuterDiameterMm, shape.WallThicknessMm, Reason = reason,
        });
        if (await FindAttemptAsync(keyHash, cancellationToken) is { } existing)
        {
            await MeasureAsync(cancellationToken);
            return Replay(existing, requestHash, metadata);
        }

        ProductDimensionAnswerV1? refusal = null;
        if (!await database.Products.AsNoTracking().AnyAsync(product => product.Id == productId, cancellationToken))
        {
            refusal = new ProductDimensionAnswerV1(404, null, "Ürün bulunamadı.", "İstenen ürün kartı yok.");
        }

        var row = refusal is null
            ? await database.Dimensions.SingleOrDefaultAsync(current => current.ProductId == productId, cancellationToken)
            : null;
        if (refusal is null && (row?.Version ?? 0) != expectedVersion)
        {
            refusal = VersionConflict();
        }

        var weight = refusal is null
            ? await ProductProfileWeightTableService.FindAsync(database, scope.CompanyId, shape.SectionKind, shape.WidthMm,
                shape.HeightMm, shape.OuterDiameterMm, shape.WallThicknessMm, cancellationToken)
            : null;
        if (refusal is null && weight is null
            && string.Equals(shape.SectionKind, ProductDimensionSectionKindsV1.Profile, StringComparison.Ordinal))
        {
            // URN-D16: a profile's kg/m is required and comes only from the company's imported table.
            refusal = Problem(409, ProductDimensionProblemCodesV1.WeightSourceMissing, "Metre ağırlığı kaynağı yok.",
                "Şirketin güncel metre ağırlığı tablosunda bu en, yükseklik ve et kalınlığı yok; önce tabloyu kaynak etiketli "
                + "dosyadan içe aktarın.");
        }

        if (refusal is not null)
        {
            return await StoreAsync(refusal, productId, keyHash, requestHash, metadata, cancellationToken);
        }

        if (row is not null && Same(row, shape))
        {
            // No change: no version move and no audit event; the key still records its answer.
            return await StoreAsync(NoContent(), productId, keyHash, requestHash, metadata, cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var before = row is null ? null : Snapshot(row);
        var versionBefore = row?.Version;
        if (row is null)
        {
            row = new ProductCardDimensionRow { ProductId = productId, Version = 1 };
            Apply(row, shape, now, metadata.ActorId, scope.CompanyId);
            database.Dimensions.Add(row);
        }
        else
        {
            Apply(row, shape, now, metadata.ActorId, scope.CompanyId);
            row.Version = checked(row.Version + 1);
        }

        var after = JsonSerializer.SerializeToNode(Snapshot(row), Json)!.AsObject();
        after["actingCompanyId"] = scope.CompanyId;
        after["reason"] = reason;
        if (weight is not null)
        {
            after["kgPerMetreTableVersion"] = weight.TableVersion;
            after["kgPerMetreSourceLabel"] = weight.SourceLabel;
        }

        auditEvents.Enqueue(new AuditEventDraft(scope.CompanyId, "product.dimension.changed.v1", EntityType,
            productId.ToString("D"), "SetDimension", versionBefore, row.Version, AuditActorTypes.User, metadata.ActorId,
            Permission, scope.RoleCodes, before is null ? "{}" : JsonSerializer.Serialize(before, Json), after.ToJsonString(Json),
            "Ortak ürün kartının ebat/kalınlık satırı değiştirildi; gerekçe kayıt içeriğindedir", ApprovalContextJson: null,
            AuditAiInvolvement.None, AiContextJson: null, metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent,
            metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
        await MeasureAsync(cancellationToken);
        AddAttempt(NoContent(), productId, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new ProductDimensionResultV1(NoContent(), false, metadata.CorrelationId);
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

    async Task<ProductDimensionReadV1> IProductDimensionReaderV1.GetAsync(Guid productId, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId) || !companyContext.IsResolved
            || companyContext.CompanyId == Guid.Empty || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            return ProductDimensionReadV1.Without(ProductDimensionOutcomeV1.Denied);
        }

        try
        {
            if (productId == Guid.Empty
                || !await database.Products.AsNoTracking().AnyAsync(product => product.Id == productId, cancellationToken))
            {
                return ProductDimensionReadV1.Without(ProductDimensionOutcomeV1.NotFound);
            }

            var row = await database.Dimensions.AsNoTracking()
                .SingleOrDefaultAsync(dimension => dimension.ProductId == productId, cancellationToken);
            if (row is null)
            {
                return ProductDimensionReadV1.Without(ProductDimensionOutcomeV1.NoSource);
            }

            var weight = await WeightOfAsync(row, companyContext.CompanyId, cancellationToken);
            return new ProductDimensionReadV1(ProductDimensionOutcomeV1.Found, row.SectionKind, row.ThicknessMm, row.WidthMm,
                row.LengthMm, row.HeightMm, row.OuterDiameterMm, row.WallThicknessMm, weight?.KgPerMetre, row.Version,
                weight?.SourceLabel, weight?.TableVersion);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ProductDimensionReadV1.Without(ProductDimensionOutcomeV1.Unavailable);
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

    /// <summary>The acting company's current table value for the stored measures (URN-D16); null without one.</summary>
    private Task<ProfileWeightMatch?> WeightOfAsync(ProductCardDimensionRow row, Guid companyId,
        CancellationToken cancellationToken) =>
        ProductProfileWeightTableService.FindAsync(database, companyId, row.SectionKind, row.WidthMm, row.HeightMm,
            row.OuterDiameterMm, row.WallThicknessMm, cancellationToken);

    private static bool Same(ProductCardDimensionRow row, ProductDimensionShape.Measures shape) =>
        string.Equals(row.SectionKind, shape.SectionKind, StringComparison.Ordinal)
        && row.ThicknessMm == shape.ThicknessMm && row.WidthMm == shape.WidthMm && row.LengthMm == shape.LengthMm
        && row.HeightMm == shape.HeightMm && row.OuterDiameterMm == shape.OuterDiameterMm
        && row.WallThicknessMm == shape.WallThicknessMm && row.KgPerMetre is null;

    private static void Apply(ProductCardDimensionRow row, ProductDimensionShape.Measures shape, DateTimeOffset now,
        string actorId, Guid companyId)
    {
        row.SectionKind = shape.SectionKind;
        row.ThicknessMm = shape.ThicknessMm;
        row.WidthMm = shape.WidthMm;
        row.LengthMm = shape.LengthMm;
        row.HeightMm = shape.HeightMm;
        row.OuterDiameterMm = shape.OuterDiameterMm;
        row.WallThicknessMm = shape.WallThicknessMm;
        row.KgPerMetre = null; // URN-D16: never typed on the card; a legacy stored value is cleared on the next change.
        row.UpdatedAtUtc = now;
        row.UpdatedBy = actorId;
        row.UpdatedInCompanyId = companyId;
    }

    private Task<ProductDimensionAttemptRow?> FindAttemptAsync(string keyHash, CancellationToken cancellationToken) =>
        database.DimensionAttempts.AsNoTracking().SingleOrDefaultAsync(
            attempt => attempt.CommandName == SetCommandName && attempt.IdempotencyKeyHash == keyHash, cancellationToken);

    private static ProductDimensionResultV1 Replay(ProductDimensionAttemptRow attempt, string requestHash,
        ProductDimensionCommandMetadataV1 metadata)
    {
        if (!string.Equals(attempt.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return Unstored(Problem(409, ProductDimensionProblemCodesV1.IdempotencyConflict,
                "İşlem anahtarı kullanılmış.", "Bu Idempotency-Key başka bir istekle kullanıldı."), metadata);
        }

        var answer = JsonSerializer.Deserialize<ProductDimensionAnswerV1>(attempt.AnswerJson, Json);
        if (answer is null || answer.StatusCode != attempt.StatusCode)
        {
            throw new InvalidOperationException("Kalıcı idempotency sonucu ürün ebat/kalınlık sözleşmesiyle uyumsuz.");
        }

        return new ProductDimensionResultV1(answer, true, attempt.CorrelationId);
    }

    private void AddAttempt(ProductDimensionAnswerV1 answer, Guid productId, string keyHash, string requestHash,
        ProductDimensionCommandMetadataV1 metadata) =>
        database.DimensionAttempts.Add(new ProductDimensionAttemptRow
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
    private async Task<ProductDimensionResultV1> StoreAsync(ProductDimensionAnswerV1 answer, Guid productId, string keyHash,
        string requestHash, ProductDimensionCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        await MeasureAsync(cancellationToken);
        database.ChangeTracker.Clear();
        AddAttempt(answer, productId, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new ProductDimensionResultV1(answer, false, metadata.CorrelationId);
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

    private static object Snapshot(ProductCardDimensionRow row) => new
    {
        row.ProductId,
        row.SectionKind,
        row.ThicknessMm,
        row.WidthMm,
        row.LengthMm,
        row.HeightMm,
        row.OuterDiameterMm,
        row.WallThicknessMm,
        row.KgPerMetre,
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

    private static ProductDimensionAnswerV1 NoContent() =>
        new(204, null, "Kaydedildi.", "Ürünün ebat/kalınlık satırı kaydedildi.");

    private static ProductDimensionAnswerV1 VersionConflict() =>
        Problem(409, ProductDimensionProblemCodesV1.VersionConflict, "Kayıt değişmiş.",
            "Ürünün ebat/kalınlık satırı siz açtıktan sonra değişti; güncel hâlini okuyup yeniden deneyin.");

    private static ProductDimensionAnswerV1 Problem(int status, string code, string title, string detail) =>
        new(status, code, title, detail);

    private static ProductDimensionResultV1 Unstored(ProductDimensionAnswerV1 answer, ProductDimensionCommandMetadataV1 metadata) =>
        new(answer, false, metadata.CorrelationId);

    private sealed record Scope(Guid CompanyId, IReadOnlyCollection<string> RoleCodes);
}
