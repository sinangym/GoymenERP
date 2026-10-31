using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

/// <summary>
/// MOD-03 (SAT-D73/D74/D82): the product's VAT class and withholding class in the server-resolved company.
/// <list type="bullet">
/// <item><b>Authority</b>: <see cref="ProductTaxClassificationPermissionsV1.Manage"/> company-wide, measured fresh first and
/// again right before the answer / the save; granted to no role here.</item>
/// <item><b>Classes</b>: checked with the Settings owner (<see cref="ISettingsTaxCategoryReaderV1"/>): the VAT class must be
/// a <c>VatClass</c> of this company, the withholding class (optional; null = no withholding) a <c>WithholdingClass</c>.
/// An absent or failing Settings port is 503; nothing is stored without that check.</item>
/// <item><b>Command</b>: one person, a mandatory reason (≤ 4000), Idempotency-Key (the stored terminal answer replays),
/// the version as decimal text ("0" = none yet), correlation/causation and ONE audit event per real change.</item>
/// <item><b>Read port</b> (<see cref="IProductTaxClassificationReaderV1"/>): access only (authenticated member of the
/// company), no permission: the consumer fences its own operation.</item>
/// </list>
/// </summary>
internal sealed class ProductTaxClassificationService(
    ProductPriceListDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider,
    ISettingsTaxCategoryReaderV1? taxCategories = null) : IProductTaxClassificationServiceV1, IProductTaxClassificationReaderV1
{
    private const string Permission = ProductTaxClassificationPermissionsV1.Manage;
    private const string SetCommandName = "product.tax-classification.set.v1";
    private const string EntityType = "Product.TaxClassification";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ProductTaxClassificationViewV1> GetAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var scope = await MeasureAsync(cancellationToken);
        if (productId == Guid.Empty)
        {
            throw new ProductTaxClassificationException(400, ProductTaxClassificationProblemCodesV1.RequestInvalid);
        }

        if (!await database.Products.AsNoTracking().AnyAsync(product => product.Id == productId, cancellationToken))
        {
            await MeasureAsync(cancellationToken);
            throw new ProductTaxClassificationException(404, null);
        }

        var row = await database.TaxClassifications.AsNoTracking()
            .SingleOrDefaultAsync(classification => classification.ProductId == productId, cancellationToken);
        await MeasureAsync(cancellationToken);
        return View(scope.CompanyId, productId, row);
    }

    public async Task<ProductTaxClassificationResultV1> SetAsync(Guid productId, SetProductTaxClassificationRequestV1 request,
        ProductTaxClassificationCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        if (!companyContext.IsResolved || metadata.CompanyId != companyContext.CompanyId
            || !string.Equals(metadata.ActorId, currentUser.UserId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || string.IsNullOrWhiteSpace(metadata.SourceChannel)
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey))
        {
            throw new InvalidOperationException("Ürün vergi sınıfı komut bağlamı geçersiz.");
        }

        var scope = await MeasureAsync(cancellationToken);
        if (productId == Guid.Empty || !TryParseVersion(request.ExpectedVersion, out var expectedVersion)
            || request.VatClassId is not { } vatClassId || vatClassId == Guid.Empty
            || request.WithholdingClassId == Guid.Empty)
        {
            return Unstored(Problem(400, ProductTaxClassificationProblemCodesV1.RequestInvalid, "İstek geçersiz.",
                "Ürün, beklenen sürüm (\"0\" veya pozitif tam sayı metni) ve KDV sınıfı zorunludur."), metadata);
        }

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            return Unstored(Problem(400, ProductTaxClassificationProblemCodesV1.ReasonRequired, "Gerekçe gerekli.",
                "Bu değişiklik için gerekçe zorunludur."), metadata);
        }

        if (reason.Length > ProductTaxClassificationInputLimitsV1.ReasonMaximumLength)
        {
            return Unstored(Problem(400, ProductTaxClassificationProblemCodesV1.ReasonTooLong, "Gerekçe çok uzun.",
                $"Gerekçe en fazla {ProductTaxClassificationInputLimitsV1.ReasonMaximumLength} karakter olabilir."), metadata);
        }

        var withholdingClassId = request.WithholdingClassId;
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            ProductId = productId,
            ExpectedVersion = expectedVersion,
            VatClassId = vatClassId,
            WithholdingClassId = withholdingClassId,
            Reason = reason,
        });
        if (await FindAttemptAsync(keyHash, cancellationToken) is { } existing)
        {
            await MeasureAsync(cancellationToken);
            return Replay(existing, requestHash, metadata);
        }

        ProductTaxClassificationAnswerV1? refusal = null;
        if (!await database.Products.AsNoTracking().AnyAsync(product => product.Id == productId, cancellationToken))
        {
            refusal = new ProductTaxClassificationAnswerV1(404, null, "Ürün bulunamadı.", "İstenen ürün kartı yok.");
        }

        var row = refusal is null
            ? await database.TaxClassifications.SingleOrDefaultAsync(classification => classification.ProductId == productId,
                cancellationToken)
            : null;
        if (refusal is null && (row?.Version ?? 0) != expectedVersion)
        {
            refusal = VersionConflict();
        }

        if (refusal is null)
        {
            refusal = await ClassRefusalAsync(vatClassId, TaxCategoryKindsV1.VatClass,
                    ProductTaxClassificationProblemCodesV1.VatClassInvalid, "KDV sınıfı bu şirkette yok.", cancellationToken)
                ?? (withholdingClassId is { } withholding
                    ? await ClassRefusalAsync(withholding, TaxCategoryKindsV1.WithholdingClass,
                        ProductTaxClassificationProblemCodesV1.WithholdingClassInvalid, "Tevkifat sınıfı bu şirkette yok.",
                        cancellationToken)
                    : null);
        }

        if (refusal is not null)
        {
            return await StoreAsync(refusal, productId, keyHash, requestHash, metadata, scope, cancellationToken);
        }

        if (row is not null && row.VatClassId == vatClassId && row.WithholdingClassId == withholdingClassId)
        {
            // No change: no version move and no audit event; the key still records its answer.
            return await StoreAsync(NoContent(), productId, keyHash, requestHash, metadata, scope, cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var before = row is null ? null : Snapshot(row);
        var versionBefore = row?.Version;
        if (row is null)
        {
            row = new ProductTaxClassificationRow
            {
                CompanyId = scope.CompanyId, ProductId = productId, VatClassId = vatClassId,
                WithholdingClassId = withholdingClassId, Version = 1, UpdatedAtUtc = now, UpdatedBy = metadata.ActorId,
            };
            database.TaxClassifications.Add(row);
        }
        else
        {
            row.VatClassId = vatClassId;
            row.WithholdingClassId = withholdingClassId;
            row.Version = checked(row.Version + 1);
            row.UpdatedAtUtc = now;
            row.UpdatedBy = metadata.ActorId;
        }

        var after = JsonSerializer.SerializeToNode(Snapshot(row), Json)!.AsObject();
        after["reason"] = reason;
        auditEvents.Enqueue(new AuditEventDraft(scope.CompanyId, "product.tax-classification.changed.v1", EntityType,
            productId.ToString("D"), "SetTaxClassification", versionBefore, row.Version, AuditActorTypes.User, metadata.ActorId,
            Permission, scope.RoleCodes, before is null ? "{}" : JsonSerializer.Serialize(before, Json), after.ToJsonString(Json),
            "Ürünün vergi sınıfı değiştirildi; gerekçe kayıt içeriğindedir", ApprovalContextJson: null,
            AuditAiInvolvement.None, AiContextJson: null, metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent,
            metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
        await MeasureAsync(cancellationToken);
        AddAttempt(NoContent(), productId, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new ProductTaxClassificationResultV1(NoContent(), false, metadata.CorrelationId);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            if (await FindAttemptAsync(keyHash, cancellationToken) is { } winner)
            {
                return Replay(winner, requestHash, metadata);
            }

            return await StoreAsync(VersionConflict(), productId, keyHash, requestHash, metadata, scope, cancellationToken);
        }
    }

    async Task<ProductTaxClassificationReadV1> IProductTaxClassificationReaderV1.GetAsync(Guid productId,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId) || !companyContext.IsResolved
            || companyContext.CompanyId == Guid.Empty || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            return new ProductTaxClassificationReadV1(ProductTaxClassificationOutcomeV1.Denied, null, null, null);
        }

        try
        {
            if (productId == Guid.Empty
                || !await database.Products.AsNoTracking().AnyAsync(product => product.Id == productId, cancellationToken))
            {
                return new ProductTaxClassificationReadV1(ProductTaxClassificationOutcomeV1.NotFound, null, null, null);
            }

            var row = await database.TaxClassifications.AsNoTracking()
                .SingleOrDefaultAsync(classification => classification.ProductId == productId, cancellationToken);
            return row is null
                ? new ProductTaxClassificationReadV1(ProductTaxClassificationOutcomeV1.NotClassified, null, null, null)
                : new ProductTaxClassificationReadV1(ProductTaxClassificationOutcomeV1.Found, row.VatClassId,
                    row.WithholdingClassId, row.Version);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ProductTaxClassificationReadV1(ProductTaxClassificationOutcomeV1.Unavailable, null, null, null);
        }
    }

    private async Task<ProductTaxClassificationAnswerV1?> ClassRefusalAsync(Guid categoryId, string kind, string code,
        string detail, CancellationToken cancellationToken)
    {
        var reader = taxCategories ?? throw new ProductTaxClassificationException(503,
            ProductTaxClassificationProblemCodesV1.ProviderUnavailable);
        SettingsTaxCategoryV1? category;
        try
        {
            category = await reader.GetCategoryAsync(categoryId, cancellationToken);
        }
        catch (SettingsPrerequisiteException exception)
        {
            throw exception.Code == TaxMasterProblemCodesV1.AccessDenied
                ? new UnauthorizedAccessException()
                : new ProductTaxClassificationException(503, ProductTaxClassificationProblemCodesV1.ProviderUnavailable);
        }

        return category is { } found && found.Kind == kind && found.CompanyId == companyContext.CompanyId
            ? null
            : Problem(422, code, "Vergi sınıfı geçersiz.", detail);
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

    private Task<ProductTaxClassificationAttemptRow?> FindAttemptAsync(string keyHash, CancellationToken cancellationToken) =>
        database.TaxClassificationAttempts.AsNoTracking().SingleOrDefaultAsync(
            attempt => attempt.CommandName == SetCommandName && attempt.IdempotencyKeyHash == keyHash, cancellationToken);

    private static ProductTaxClassificationResultV1 Replay(ProductTaxClassificationAttemptRow attempt, string requestHash,
        ProductTaxClassificationCommandMetadataV1 metadata)
    {
        if (!string.Equals(attempt.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return Unstored(Problem(409, ProductTaxClassificationProblemCodesV1.IdempotencyConflict,
                "İşlem anahtarı kullanılmış.", "Bu Idempotency-Key başka bir istekle kullanıldı."), metadata);
        }

        var answer = JsonSerializer.Deserialize<ProductTaxClassificationAnswerV1>(attempt.AnswerJson, Json);
        if (answer is null || answer.StatusCode != attempt.StatusCode)
        {
            throw new InvalidOperationException("Kalıcı idempotency sonucu ürün vergi sınıfı sözleşmesiyle uyumsuz.");
        }

        return new ProductTaxClassificationResultV1(answer, true, attempt.CorrelationId);
    }

    private void AddAttempt(ProductTaxClassificationAnswerV1 answer, Guid productId, string keyHash, string requestHash,
        ProductTaxClassificationCommandMetadataV1 metadata) =>
        database.TaxClassificationAttempts.Add(new ProductTaxClassificationAttemptRow
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

    /// <summary>Stores a refusal or a no-op as the key's terminal answer; a concurrent same-key winner replays instead.</summary>
    private async Task<ProductTaxClassificationResultV1> StoreAsync(ProductTaxClassificationAnswerV1 answer, Guid productId,
        string keyHash, string requestHash, ProductTaxClassificationCommandMetadataV1 metadata, Scope scope,
        CancellationToken cancellationToken)
    {
        _ = scope;
        await MeasureAsync(cancellationToken);
        database.ChangeTracker.Clear();
        AddAttempt(answer, productId, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new ProductTaxClassificationResultV1(answer, false, metadata.CorrelationId);
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

    private static ProductTaxClassificationViewV1 View(Guid companyId, Guid productId, ProductTaxClassificationRow? row) =>
        row is null
            ? new ProductTaxClassificationViewV1(companyId, productId, "0", null, null, null, null)
            : new ProductTaxClassificationViewV1(companyId, productId, row.Version.ToString(CultureInfo.InvariantCulture),
                row.VatClassId, row.WithholdingClassId, row.UpdatedAtUtc, row.UpdatedBy);

    private static object Snapshot(ProductTaxClassificationRow row) => new
    {
        row.CompanyId,
        row.ProductId,
        row.VatClassId,
        row.WithholdingClassId,
        row.Version,
    };

    /// <summary>"0" or a positive decimal version without sign or leading zero.</summary>
    private static bool TryParseVersion(string? text, out long version)
    {
        version = 0;
        return !string.IsNullOrEmpty(text) && text.All(char.IsAsciiDigit) && (text.Length == 1 || text[0] != '0')
            && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out version);
    }

    private static ProductTaxClassificationAnswerV1 NoContent() => new(204, null, "Kaydedildi.", "Ürünün vergi sınıfı kaydedildi.");

    private static ProductTaxClassificationAnswerV1 VersionConflict() =>
        Problem(409, ProductTaxClassificationProblemCodesV1.VersionConflict, "Kayıt değişmiş.",
            "Ürünün vergi sınıfı siz açtıktan sonra değişti; güncel hâlini okuyup yeniden deneyin.");

    private static ProductTaxClassificationAnswerV1 Problem(int status, string code, string title, string detail) =>
        new(status, code, title, detail);

    private static ProductTaxClassificationResultV1 Unstored(ProductTaxClassificationAnswerV1 answer,
        ProductTaxClassificationCommandMetadataV1 metadata) => new(answer, false, metadata.CorrelationId);

    private sealed record Scope(Guid CompanyId, IReadOnlyCollection<string> RoleCodes);
}
