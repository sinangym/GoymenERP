using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

/// <summary>
/// KLN-D54: the company's structural profile weight table — the kg/m of each structural profile (KLN-D48 (2)) as an administrator
/// imported it from the standard or catalogue in hand, versioned and source-labelled. The dimension read
/// (<see cref="ProductDimensionService"/>) resolves a structural profile's kg/m from the CURRENT version; nothing here estimates,
/// rounds or invents a value, and no value is built into the code.
/// <list type="bullet">
/// <item><b>Authority</b>: <see cref="ProductProfileWeightPermissionsV1.Import"/> company-wide in the server-resolved company,
/// measured fresh first and again right before the answer / the save; granted to no role here.</item>
/// <item><b>Command</b>: the whole table as the next version (insert-only; older versions stay), a mandatory source label and
/// reason, Idempotency-Key (the stored terminal answer replays), the current version as decimal text ("0" = none yet),
/// correlation/causation and ONE audit event per real import (the same rows and label again change nothing).</item>
/// </list>
/// </summary>
internal sealed class ProductProfileWeightTableService(
    ProductPriceListDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider) : IProductProfileWeightTableServiceV1
{
    private const string Permission = ProductProfileWeightPermissionsV1.Import;
    private const string ImportCommandName = "product.profile-weights.import.v1";
    private const string EntityType = "Product.ProfileWeightTable";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ProductProfileWeightTableViewV1> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var scope = await MeasureAsync(cancellationToken);
        var header = await CurrentAsync(cancellationToken);
        List<ProductProfileWeightRow> rows = header is null ? [] : await RowsAsync(header.Version, cancellationToken);
        await MeasureAsync(cancellationToken);
        return header is null
            ? new ProductProfileWeightTableViewV1(scope.CompanyId, "0", null, null, null, null, [])
            : new ProductProfileWeightTableViewV1(scope.CompanyId, header.Version.ToString(CultureInfo.InvariantCulture),
                header.SourceLabel, header.Reason, header.ImportedAtUtc, header.ImportedBy,
                [.. rows.Select(row => new ProductProfileWeightTableRowViewV1(row.ProfileSeries, row.ProfileSize, row.KgPerMetre))]);
    }

    public async Task<ProductDimensionResultV1> ImportAsync(ImportProductProfileWeightTableRequestV1 request,
        ProductDimensionCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        if (!companyContext.IsResolved || metadata.CompanyId != companyContext.CompanyId
            || !string.Equals(metadata.ActorId, currentUser.UserId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || string.IsNullOrWhiteSpace(metadata.SourceChannel)
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey))
        {
            throw new InvalidOperationException("Yapısal profil ağırlık tablosu komut bağlamı geçersiz.");
        }

        var scope = await MeasureAsync(cancellationToken);
        if (!TryParseVersion(request.ExpectedVersion, out var expectedVersion))
        {
            return Unstored(Problem(400, ProductProfileWeightProblemCodesV1.RequestInvalid, "İstek geçersiz.",
                "Beklenen sürüm (\"0\" veya pozitif tam sayı metni) zorunludur."), metadata);
        }

        var source = request.SourceLabel?.Trim() ?? string.Empty;
        if (source.Length == 0)
        {
            return Unstored(Problem(400, ProductProfileWeightProblemCodesV1.SourceRequired, "Kaynak gerekli.",
                "Tablonun kaynağı (standart veya katalog ve baskısı) zorunludur."), metadata);
        }

        if (source.Length > ProductProfileWeightInputLimitsV1.SourceLabelMaximumLength)
        {
            return Unstored(Problem(400, ProductProfileWeightProblemCodesV1.SourceTooLong, "Kaynak çok uzun.",
                $"Kaynak en fazla {ProductProfileWeightInputLimitsV1.SourceLabelMaximumLength} karakter olabilir."), metadata);
        }

        if (RowsRefusal(request.Rows, out var rows) is { } rowsRefusal)
        {
            return Unstored(Problem(400, ProductProfileWeightProblemCodesV1.RowsInvalid, "Tablo satırları geçersiz.", rowsRefusal),
                metadata);
        }

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            return Unstored(Problem(400, ProductProfileWeightProblemCodesV1.ReasonRequired, "Gerekçe gerekli.",
                "Bu içe aktarma için gerekçe zorunludur."), metadata);
        }

        if (reason.Length > ProductProfileWeightInputLimitsV1.ReasonMaximumLength)
        {
            return Unstored(Problem(400, ProductProfileWeightProblemCodesV1.ReasonTooLong, "Gerekçe çok uzun.",
                $"Gerekçe en fazla {ProductProfileWeightInputLimitsV1.ReasonMaximumLength} karakter olabilir."), metadata);
        }

        var ordered = rows!;
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            ExpectedVersion = expectedVersion, SourceLabel = source, Reason = reason,
            Rows = ordered.Select(row => new { row.Series, row.Size, row.KgPerMetre }).ToArray(),
        });
        if (await FindAttemptAsync(keyHash, cancellationToken) is { } existing)
        {
            await MeasureAsync(cancellationToken);
            return Replay(existing, requestHash, metadata);
        }

        var current = await CurrentAsync(cancellationToken);
        if ((current?.Version ?? 0) != expectedVersion)
        {
            return await StoreAsync(VersionConflict(), keyHash, requestHash, metadata, cancellationToken);
        }

        if (current is not null && string.Equals(current.SourceLabel, source, StringComparison.Ordinal)
            && (await RowsAsync(current.Version, cancellationToken))
            .Select(row => new ImportedRow(row.ProfileSeries, row.ProfileSize, ProductDimensionShape.Canonical(row.KgPerMetre)!.Value))
            .SequenceEqual(ordered))
        {
            // The same table again: no new version and no audit event; the key still records its answer.
            return await StoreAsync(NoContent(), keyHash, requestHash, metadata, cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var version = checked((current?.Version ?? 0) + 1);
        database.ProfileWeightTables.Add(new ProductProfileWeightTableRow
        {
            CompanyId = scope.CompanyId,
            Version = version,
            SourceLabel = source,
            Reason = reason,
            RowCount = ordered.Count,
            ImportedAtUtc = now,
            ImportedBy = metadata.ActorId,
            CorrelationId = metadata.CorrelationId,
        });
        database.ProfileWeightRows.AddRange(ordered.Select(row => new ProductProfileWeightRow
        {
            CompanyId = scope.CompanyId,
            TableVersion = version,
            ProfileSeries = row.Series,
            ProfileSize = row.Size,
            KgPerMetre = row.KgPerMetre,
        }));

        var before = current is null
            ? "{}"
            : JsonSerializer.Serialize(new { current.Version, current.SourceLabel, current.RowCount }, Json);
        var after = JsonSerializer.Serialize(new
        {
            Version = version, SourceLabel = source, RowCount = ordered.Count, RowsSha256 = RowsDigest(ordered),
            ActingCompanyId = scope.CompanyId, Reason = reason,
        }, Json);
        auditEvents.Enqueue(new AuditEventDraft(scope.CompanyId, "product.profile-weights.imported.v1", EntityType,
            version.ToString(CultureInfo.InvariantCulture), "ImportProfileWeights", current?.Version, version,
            AuditActorTypes.User, metadata.ActorId, Permission, scope.RoleCodes, before, after,
            "Yapısal profil ağırlık tablosunun yeni sürümü içe aktarıldı; kaynak ve gerekçe kayıt içeriğindedir",
            ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null, metadata.SourceChannel,
            metadata.ClientIpAddress, metadata.UserAgent, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
        await MeasureAsync(cancellationToken);
        AddAttempt(NoContent(), keyHash, requestHash, metadata);
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

            return await StoreAsync(VersionConflict(), keyHash, requestHash, metadata, cancellationToken);
        }
    }

    /// <summary>
    /// Null when every row names a known series, a size designation and a kg/m of at most six decimals, with no series + size
    /// twice; the rows come back ordered (series, size; ordinal) with the kg/m without trailing zeros. Otherwise the reason,
    /// naming the first bad row (1-based, as in the imported file without its header).
    /// </summary>
    internal static string? RowsRefusal(IReadOnlyList<ProductProfileWeightRowV1>? rows, out IReadOnlyList<ImportedRow>? ordered)
    {
        ordered = null;
        if (rows is null || rows.Count == 0)
        {
            return "Tabloda en az bir satır olmalıdır.";
        }

        if (rows.Count > ProductProfileWeightInputLimitsV1.MaximumRows)
        {
            return $"Tabloda en fazla {ProductProfileWeightInputLimitsV1.MaximumRows} satır olabilir.";
        }

        var seen = new HashSet<(string, string)>();
        var accepted = new List<ImportedRow>(rows.Count);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var line = (index + 1).ToString(CultureInfo.InvariantCulture);
            if (row is null || !ProductProfileSeriesV1.IsKnown(row.Series))
            {
                return $"{line}. satır: seri UPN, NPI, IPE, HEA, HEB veya HEM olmalıdır.";
            }

            if (!ProductDimensionShape.IsProfileSize(row.Size))
            {
                return $"{line}. satır: ölçü adı en fazla {ProductDimensionInputLimitsV1.ProfileSizeMaximumLength} karakter; büyük harf, rakam ve nokta olmalıdır.";
            }

            if (row.KgPerMetre is null || !ProductDimensionShape.IsKgPerMetre(row.KgPerMetre))
            {
                return $"{line}. satır: metre ağırlığı sıfırdan büyük ve en fazla {ProductDimensionInputLimitsV1.KgPerMetreDecimals} ondalıklı olmalıdır.";
            }

            if (!seen.Add((row.Series!, row.Size!)))
            {
                return $"{line}. satır: {row.Series} {row.Size} tabloda iki kez geçiyor.";
            }

            accepted.Add(new ImportedRow(row.Series!, row.Size!, ProductDimensionShape.Canonical(row.KgPerMetre)!.Value));
        }

        ordered = [.. accepted.OrderBy(row => row.Series, StringComparer.Ordinal).ThenBy(row => row.Size, StringComparer.Ordinal)];
        return null;
    }

    internal sealed record ImportedRow(string Series, string Size, decimal KgPerMetre);

    private static string RowsDigest(IReadOnlyList<ImportedRow> rows)
    {
        var text = string.Join('\n', rows.Select(row =>
            string.Create(CultureInfo.InvariantCulture, $"{row.Series};{row.Size};{row.KgPerMetre}")));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private Task<ProductProfileWeightTableRow?> CurrentAsync(CancellationToken cancellationToken) =>
        database.ProfileWeightTables.AsNoTracking().OrderByDescending(header => header.Version).FirstOrDefaultAsync(cancellationToken);

    private async Task<List<ProductProfileWeightRow>> RowsAsync(long version, CancellationToken cancellationToken)
    {
        var rows = await database.ProfileWeightRows.AsNoTracking().Where(row => row.TableVersion == version)
            .ToListAsync(cancellationToken);
        return [.. rows.OrderBy(row => row.ProfileSeries, StringComparer.Ordinal).ThenBy(row => row.ProfileSize, StringComparer.Ordinal)];
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

    private Task<ProductProfileWeightAttemptRow?> FindAttemptAsync(string keyHash, CancellationToken cancellationToken) =>
        database.ProfileWeightAttempts.AsNoTracking().SingleOrDefaultAsync(
            attempt => attempt.CommandName == ImportCommandName && attempt.IdempotencyKeyHash == keyHash, cancellationToken);

    private static ProductDimensionResultV1 Replay(ProductProfileWeightAttemptRow attempt, string requestHash,
        ProductDimensionCommandMetadataV1 metadata)
    {
        if (!string.Equals(attempt.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return Unstored(Problem(409, ProductProfileWeightProblemCodesV1.IdempotencyConflict,
                "İşlem anahtarı kullanılmış.", "Bu Idempotency-Key başka bir istekle kullanıldı."), metadata);
        }

        var answer = JsonSerializer.Deserialize<ProductDimensionAnswerV1>(attempt.AnswerJson, Json);
        if (answer is null || answer.StatusCode != attempt.StatusCode)
        {
            throw new InvalidOperationException("Kalıcı idempotency sonucu yapısal profil ağırlık tablosu sözleşmesiyle uyumsuz.");
        }

        return new ProductDimensionResultV1(answer, true, attempt.CorrelationId);
    }

    private void AddAttempt(ProductDimensionAnswerV1 answer, string keyHash, string requestHash,
        ProductDimensionCommandMetadataV1 metadata) =>
        database.ProfileWeightAttempts.Add(new ProductProfileWeightAttemptRow
        {
            CompanyId = metadata.CompanyId,
            CommandName = ImportCommandName,
            IdempotencyKeyHash = keyHash,
            RequestHash = requestHash,
            ActorId = metadata.ActorId,
            StatusCode = answer.StatusCode,
            AnswerJson = JsonSerializer.Serialize(answer, Json),
            CorrelationId = metadata.CorrelationId,
            CreatedAtUtc = timeProvider.GetUtcNow(),
        });

    /// <summary>Stores a refusal or a no-op as the terminal answer of the key; a concurrent same-key winner replays instead.</summary>
    private async Task<ProductDimensionResultV1> StoreAsync(ProductDimensionAnswerV1 answer, string keyHash, string requestHash,
        ProductDimensionCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        await MeasureAsync(cancellationToken);
        database.ChangeTracker.Clear();
        AddAttempt(answer, keyHash, requestHash, metadata);
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

    /// <summary>"0" or a positive decimal version without sign or leading zero.</summary>
    private static bool TryParseVersion(string? text, out long version)
    {
        version = 0;
        return !string.IsNullOrEmpty(text) && text.All(char.IsAsciiDigit) && (text.Length == 1 || text[0] != '0')
            && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out version);
    }

    private static ProductDimensionAnswerV1 NoContent() =>
        new(204, null, "İçe aktarıldı.", "Yapısal profil ağırlık tablosu kaydedildi.");

    private static ProductDimensionAnswerV1 VersionConflict() =>
        Problem(409, ProductProfileWeightProblemCodesV1.VersionConflict, "Tablo değişmiş.",
            "Yapısal profil ağırlık tablosu siz açtıktan sonra değişti; güncel hâlini okuyup yeniden deneyin.");

    private static ProductDimensionAnswerV1 Problem(int status, string code, string title, string detail) =>
        new(status, code, title, detail);

    private static ProductDimensionResultV1 Unstored(ProductDimensionAnswerV1 answer, ProductDimensionCommandMetadataV1 metadata) =>
        new(answer, false, metadata.CorrelationId);

    private sealed record Scope(Guid CompanyId, IReadOnlyCollection<string> RoleCodes);
}
