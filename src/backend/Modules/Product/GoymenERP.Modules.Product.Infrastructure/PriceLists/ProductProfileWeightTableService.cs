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
/// URN-D13…D16 (STK-S37R), KLN-D54: the company's kg/m table of profiles and round tubes, imported from a file only.
/// <list type="bullet">
/// <item><b>Authority</b>: <see cref="ProductProfileWeightPermissionsV1.Import"/> company-wide in the server-resolved company,
/// measured fresh first and again right before the vault call, the answer and the save; granted to no role here.</item>
/// <item><b>File</b>: the server reads the rows from the file (<see cref="ProductProfileWeightFile"/>); the file itself goes to the
/// document vault through <see cref="IProductProfileWeightFileVaultV1"/> and only a stored, allowlisted, scan-clean file is
/// imported (URN-D13 fail-closed: an unavailable vault or scanner imports nothing and stores no answer, so the same key can be
/// retried).</item>
/// <item><b>Command</b>: one person, a mandatory source label (URN-D14) and reason, Idempotency-Key (the stored terminal answer
/// replays), the expected table version as decimal text ("0" = none yet), correlation/causation and ONE audit event per
/// import. Each import is a new insert-only version; the newest is current. No draft table is seeded (URN-D16).</item>
/// </list>
/// </summary>
internal sealed class ProductProfileWeightTableService(
    ProductPriceListDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    IAuditEventCollector auditEvents,
    IProductProfileWeightFileVaultV1 vault,
    TimeProvider timeProvider) : IProductProfileWeightTableServiceV1
{
    private const string Permission = ProductProfileWeightPermissionsV1.Import;
    private const string ImportCommandName = "product.profile-weights.import.v1";
    private const string EntityType = "Product.ProfileWeightTable";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ProductProfileWeightTableViewV1> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var scope = await MeasureAsync(cancellationToken);
        var header = await database.ProfileWeightImports.AsNoTracking()
            .Where(import => import.CompanyId == scope.CompanyId)
            .OrderByDescending(import => import.Version)
            .FirstOrDefaultAsync(cancellationToken);
        if (header is null)
        {
            await MeasureAsync(cancellationToken);
            return new ProductProfileWeightTableViewV1(scope.CompanyId, "0", null, null, null, null, null, null, []);
        }

        var lines = await database.ProfileWeightLines.AsNoTracking()
            .Where(line => line.CompanyId == scope.CompanyId && line.ImportVersion == header.Version)
            .OrderBy(line => line.LineNo)
            .ToListAsync(cancellationToken);
        await MeasureAsync(cancellationToken);
        return new ProductProfileWeightTableViewV1(scope.CompanyId, header.Version.ToString(CultureInfo.InvariantCulture),
            header.SourceLabel, header.FileName, header.FileSha256, header.DocumentId, header.ImportedAtUtc, header.ImportedBy,
            [.. lines.Select(line => new ProductProfileWeightRowV1(line.SectionKind, line.WidthMm, line.HeightMm,
                line.OuterDiameterMm, line.WallThicknessMm, line.KgPerMetre, line.ProfileSeries, line.ProfileSize))]);
    }

    public async Task<ProductProfileWeightResultV1> ImportAsync(ImportProductProfileWeightTableRequestV1 request,
        ProductProfileWeightCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        if (!companyContext.IsResolved || metadata.CompanyId != companyContext.CompanyId
            || !string.Equals(metadata.ActorId, currentUser.UserId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || string.IsNullOrWhiteSpace(metadata.SourceChannel)
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey))
        {
            throw new InvalidOperationException("Metre ağırlığı tablosu komut bağlamı geçersiz.");
        }

        var scope = await MeasureAsync(cancellationToken);
        if (!TryParseVersion(request.ExpectedVersion, out var expectedVersion))
        {
            return Unstored(Problem(400, ProductProfileWeightProblemCodesV1.RequestInvalid, "İstek geçersiz.",
                "Beklenen tablo sürümü (\"0\" veya pozitif tam sayı metni) zorunludur."), metadata);
        }

        var sourceLabel = request.SourceLabel?.Trim() ?? string.Empty;
        if (sourceLabel.Length == 0)
        {
            return Unstored(Problem(400, ProductProfileWeightProblemCodesV1.SourceLabelRequired, "Kaynak etiketi gerekli.",
                "Her dosya için kaynak etiketi (ör. katalog adı) zorunludur."), metadata);
        }

        if (sourceLabel.Length > ProductProfileWeightInputLimitsV1.SourceLabelMaximumLength)
        {
            return Unstored(Problem(400, ProductProfileWeightProblemCodesV1.SourceLabelTooLong, "Kaynak etiketi çok uzun.",
                $"Kaynak etiketi en fazla {ProductProfileWeightInputLimitsV1.SourceLabelMaximumLength} karakter olabilir."),
                metadata);
        }

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            return Unstored(Problem(400, ProductProfileWeightProblemCodesV1.ReasonRequired, "Gerekçe gerekli.",
                "Bu içe aktarım için gerekçe zorunludur."), metadata);
        }

        if (reason.Length > ProductProfileWeightInputLimitsV1.ReasonMaximumLength)
        {
            return Unstored(Problem(400, ProductProfileWeightProblemCodesV1.ReasonTooLong, "Gerekçe çok uzun.",
                $"Gerekçe en fazla {ProductProfileWeightInputLimitsV1.ReasonMaximumLength} karakter olabilir."), metadata);
        }

        if (FileRefusal(request, out var fileName, out var content) is { } fileRefusal)
        {
            return Unstored(Problem(400, ProductProfileWeightProblemCodesV1.FileInvalid, "Dosya geçersiz.", fileRefusal), metadata);
        }

        if (ProductProfileWeightFile.Refusal(content, out var rows) is { } rowsRefusal)
        {
            return Unstored(Problem(400, ProductProfileWeightProblemCodesV1.FileInvalid, "Dosya geçersiz.", rowsRefusal), metadata);
        }

        var fileSha256 = Convert.ToHexStringLower(SHA256.HashData(content));
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            ExpectedVersion = expectedVersion, SourceLabel = sourceLabel, Reason = reason, FileName = fileName,
            FileSha256 = fileSha256,
        });
        if (await FindAttemptAsync(keyHash, cancellationToken) is { } existing)
        {
            await MeasureAsync(cancellationToken);
            return Replay(existing, requestHash, metadata);
        }

        var currentVersion = await CurrentVersionAsync(scope.CompanyId, cancellationToken);
        if (currentVersion != expectedVersion)
        {
            return await StoreAsync(VersionConflict(), keyHash, requestHash, metadata, cancellationToken);
        }

        var importId = ImportIdOf(scope.CompanyId, keyHash);
        await MeasureAsync(cancellationToken);
        ProductProfileWeightFileStoreResultV1 stored;
        try
        {
            stored = await vault.StoreAsync(new ProductProfileWeightFileV1(scope.CompanyId, importId, fileName,
                ProductProfileWeightFileFormatV1.ContentType, content, fileSha256, metadata.ActorId, metadata.IdempotencyKey,
                metadata.CorrelationId, metadata.CausationId), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stored = ProductProfileWeightFileStoreResultV1.Unavailable;
        }

        if (stored.Outcome == ProductProfileWeightFileStoreOutcomeV1.Rejected)
        {
            return await StoreAsync(Problem(400, ProductProfileWeightProblemCodesV1.FileRejected, "Dosya kabul edilmedi.",
                "Belge kasası dosyayı kabul etmedi (izin verilmeyen tür veya temiz olmayan tarama sonucu); tablo değişmedi."),
                keyHash, requestHash, metadata, cancellationToken);
        }

        if (stored.Outcome != ProductProfileWeightFileStoreOutcomeV1.Stored || stored.DocumentId is not { } documentId
            || documentId == Guid.Empty)
        {
            // URN-D13 fail-closed: no answer is stored, so the same key can be retried once the vault and its scanner answer.
            return Unstored(Problem(503, ProductProfileWeightProblemCodesV1.FileVaultUnavailable, "Belge kasası kullanılamıyor.",
                "Dosya şu anda belge kasasına alınıp taranamıyor; tablo değişmedi. Daha sonra yeniden deneyin."), metadata);
        }

        var now = timeProvider.GetUtcNow();
        var version = checked(currentVersion + 1);
        database.ProfileWeightImports.Add(new ProductProfileWeightImportRow
        {
            CompanyId = scope.CompanyId,
            Version = version,
            ImportId = importId,
            SourceLabel = sourceLabel,
            Reason = reason,
            FileName = fileName,
            FileSha256 = fileSha256,
            FileSizeBytes = content.Length,
            DocumentId = documentId,
            RowCount = rows.Count,
            ImportedAtUtc = now,
            ImportedBy = metadata.ActorId,
            CorrelationId = metadata.CorrelationId,
        });
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            database.ProfileWeightLines.Add(new ProductProfileWeightLineRow
            {
                CompanyId = scope.CompanyId,
                ImportVersion = version,
                LineNo = index + 1,
                SectionKind = row.SectionKind,
                WidthMm = row.WidthMm,
                HeightMm = row.HeightMm,
                OuterDiameterMm = row.OuterDiameterMm,
                WallThicknessMm = row.WallThicknessMm,
                ProfileSeries = row.ProfileSeries,
                ProfileSize = row.ProfileSize,
                KgPerMetre = row.KgPerMetre,
            });
        }

        var after = new
        {
            ImportId = importId, Version = version, SourceLabel = sourceLabel, Reason = reason, FileName = fileName,
            FileSha256 = fileSha256, FileSizeBytes = content.Length, DocumentId = documentId, RowCount = rows.Count,
        };
        auditEvents.Enqueue(new AuditEventDraft(scope.CompanyId, "product.profile-weights.imported.v1", EntityType,
            importId.ToString("D"), "ImportProfileWeightTable", currentVersion == 0 ? null : currentVersion, version,
            AuditActorTypes.User, metadata.ActorId, Permission, scope.RoleCodes, "{}", JsonSerializer.Serialize(after, Json),
            "Şirketin metre ağırlığı tablosu dosyadan içe aktarıldı; kaynak etiketi ve gerekçe kayıt içeriğindedir",
            ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null, metadata.SourceChannel,
            metadata.ClientIpAddress, metadata.UserAgent, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
        await MeasureAsync(cancellationToken);
        AddAttempt(NoContent(), keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new ProductProfileWeightResultV1(NoContent(), false, metadata.CorrelationId);
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

    /// <summary>The newest import version of the company ("0" while none), read under the company query filter.</summary>
    internal static async Task<long> CurrentVersionAsync(ProductPriceListDbContext database, Guid companyId,
        CancellationToken cancellationToken) =>
        await database.ProfileWeightImports.AsNoTracking()
            .Where(import => import.CompanyId == companyId)
            .Select(import => (long?)import.Version)
            .MaxAsync(cancellationToken) ?? 0;

    /// <summary>
    /// URN-D16 / KLN-D87: the kg/m of the company's CURRENT table for a card's measures — exact values, no tolerance. The card's
    /// kind reads the rows of <see cref="ProductDimensionSectionKindsV1.WeightTableKind"/>: a profile / ERW profile by (width,
    /// height, wall), a round tube / ERW tube by (outer diameter, wall), a structural profile by (series, size designation).
    /// <see cref="ProfileWeightLookup.None"/> for any other kind, without a table or without that row.
    /// <para>
    /// URN-D19 (SINAN-CEVAP 34.10): a profile also matches with width and height swapped (40×60 finds the table's 60×40 row); the
    /// wall must be the same. When the two directions of the same measures stand in the table with different kg/m the lookup is
    /// a table conflict (<see cref="ProfileWeightLookup.IsConflict"/>), never a pick of one of them. A round tube and a structural
    /// profile have no direction.
    /// </para>
    /// </summary>
    internal static async Task<ProfileWeightLookup> FindAsync(ProductPriceListDbContext database, Guid companyId,
        string? sectionKind, decimal? widthMm, decimal? heightMm, decimal? outerDiameterMm, decimal? wallThicknessMm,
        CancellationToken cancellationToken, string? profileSeries = null, string? profileSize = null)
    {
        var tableKind = ProductDimensionSectionKindsV1.WeightTableKind(sectionKind);
        if (tableKind is null)
        {
            return ProfileWeightLookup.None;
        }

        IQueryable<ProductProfileWeightLineRow>? lines = null;
        var header = await database.ProfileWeightImports.AsNoTracking()
            .Where(import => import.CompanyId == companyId)
            .OrderByDescending(import => import.Version)
            .Select(import => new { import.Version, import.SourceLabel })
            .FirstOrDefaultAsync(cancellationToken);
        if (header is null)
        {
            return ProfileWeightLookup.None;
        }

        var current = database.ProfileWeightLines.AsNoTracking().Where(line => line.CompanyId == companyId
            && line.ImportVersion == header.Version && line.SectionKind == tableKind);
        if (tableKind == ProductDimensionSectionKindsV1.StructuralProfile)
        {
            if (profileSeries is { } series && profileSize is { } size)
            {
                lines = current.Where(line => line.ProfileSeries == series && line.ProfileSize == size);
            }
        }
        else if (wallThicknessMm is { } wall)
        {
            if (tableKind == ProductDimensionSectionKindsV1.Profile)
            {
                if (widthMm is { } width && heightMm is { } height)
                {
                    // URN-D19: either direction of the section; a square profile finds its one row through both branches.
                    lines = current.Where(line => line.WallThicknessMm == wall
                        && ((line.WidthMm == width && line.HeightMm == height)
                            || (line.WidthMm == height && line.HeightMm == width)));
                }
            }
            else if (outerDiameterMm is { } diameter)
            {
                lines = current.Where(line => line.WallThicknessMm == wall && line.OuterDiameterMm == diameter);
            }
        }

        if (lines is null)
        {
            return ProfileWeightLookup.None;
        }

        // At most two distinct values are needed to tell "one value" from "a table conflict".
        var weights = await lines.Select(line => line.KgPerMetre).Distinct().Take(2).ToListAsync(cancellationToken);
        return weights.Count switch
        {
            0 => ProfileWeightLookup.None,
            1 => ProfileWeightLookup.Found(new ProfileWeightMatch(weights[0], header.SourceLabel, header.Version)),
            _ => ProfileWeightLookup.Conflict(header.SourceLabel, header.Version),
        };
    }

    private Task<long> CurrentVersionAsync(Guid companyId, CancellationToken cancellationToken) =>
        CurrentVersionAsync(database, companyId, cancellationToken);

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

    /// <summary>The file name (a bare <c>.csv</c> name, no path) and the decoded content within the size bound.</summary>
    private static string? FileRefusal(ImportProductProfileWeightTableRequestV1 request, out string fileName,
        out byte[] content)
    {
        content = [];
        fileName = request.FileName?.Trim() ?? string.Empty;
        if (fileName.Length == 0 || fileName.Length > ProductProfileWeightInputLimitsV1.FileNameMaximumLength
            || fileName.Any(character => char.IsControl(character) || character is '/' or '\\' or ':')
            || !fileName.EndsWith(ProductProfileWeightFileFormatV1.FileExtension, StringComparison.OrdinalIgnoreCase)
            || fileName.Length == ProductProfileWeightFileFormatV1.FileExtension.Length)
        {
            return $"Dosya adı yolsuz ve {ProductProfileWeightFileFormatV1.FileExtension} uzantılı olmalıdır "
                + $"(en fazla {ProductProfileWeightInputLimitsV1.FileNameMaximumLength} karakter).";
        }

        var encoded = request.ContentBase64 ?? string.Empty;
        var buffer = new byte[ProductProfileWeightInputLimitsV1.MaximumFileBytes];
        if (encoded.Length == 0 || !Convert.TryFromBase64String(encoded, buffer, out var written) || written == 0)
        {
            return $"Dosya içeriği boş olamaz ve en fazla {ProductProfileWeightInputLimitsV1.MaximumFileBytes} bayt olabilir.";
        }

        content = buffer[..written];
        return null;
    }

    /// <summary>The import record id for the company and the command key: a retry of the same command links the same record.</summary>
    private static Guid ImportIdOf(Guid companyId, string keyHash)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{ImportCommandName}|{companyId:D}|{keyHash}")));
        return new Guid(hash.AsSpan(0, 16));
    }

    private Task<ProductProfileWeightAttemptRow?> FindAttemptAsync(string keyHash, CancellationToken cancellationToken) =>
        database.ProfileWeightAttempts.AsNoTracking().SingleOrDefaultAsync(
            attempt => attempt.CommandName == ImportCommandName && attempt.IdempotencyKeyHash == keyHash, cancellationToken);

    private static ProductProfileWeightResultV1 Replay(ProductProfileWeightAttemptRow attempt, string requestHash,
        ProductProfileWeightCommandMetadataV1 metadata)
    {
        if (!string.Equals(attempt.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return Unstored(Problem(409, ProductProfileWeightProblemCodesV1.IdempotencyConflict,
                "İşlem anahtarı kullanılmış.", "Bu Idempotency-Key başka bir istekle kullanıldı."), metadata);
        }

        var answer = JsonSerializer.Deserialize<ProductProfileWeightAnswerV1>(attempt.AnswerJson, Json);
        if (answer is null || answer.StatusCode != attempt.StatusCode)
        {
            throw new InvalidOperationException("Kalıcı idempotency sonucu metre ağırlığı tablosu sözleşmesiyle uyumsuz.");
        }

        return new ProductProfileWeightResultV1(answer, true, attempt.CorrelationId);
    }

    private void AddAttempt(ProductProfileWeightAnswerV1 answer, string keyHash, string requestHash,
        ProductProfileWeightCommandMetadataV1 metadata) =>
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

    /// <summary>Stores a refusal as the terminal answer of the key; a concurrent same-key winner replays instead.</summary>
    private async Task<ProductProfileWeightResultV1> StoreAsync(ProductProfileWeightAnswerV1 answer, string keyHash,
        string requestHash, ProductProfileWeightCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        await MeasureAsync(cancellationToken);
        database.ChangeTracker.Clear();
        AddAttempt(answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new ProductProfileWeightResultV1(answer, false, metadata.CorrelationId);
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

    private static ProductProfileWeightAnswerV1 NoContent() =>
        new(204, null, "İçe aktarıldı.", "Metre ağırlığı tablosunun yeni sürümü kaydedildi.");

    private static ProductProfileWeightAnswerV1 VersionConflict() =>
        Problem(409, ProductProfileWeightProblemCodesV1.VersionConflict, "Tablo değişmiş.",
            "Metre ağırlığı tablosu siz açtıktan sonra değişti; güncel hâlini okuyup yeniden deneyin.");

    private static ProductProfileWeightAnswerV1 Problem(int status, string code, string title, string detail) =>
        new(status, code, title, detail);

    private static ProductProfileWeightResultV1 Unstored(ProductProfileWeightAnswerV1 answer,
        ProductProfileWeightCommandMetadataV1 metadata) => new(answer, false, metadata.CorrelationId);

    private sealed record Scope(Guid CompanyId, IReadOnlyCollection<string> RoleCodes);
}

/// <summary>A kg/m read from the company's current table, with that table's source label and version (URN-D14/D16).</summary>
internal sealed record ProfileWeightMatch(decimal KgPerMetre, string SourceLabel, long TableVersion);

/// <summary>
/// The answer of one table lookup: a single value (<see cref="Match"/>), nothing (<see cref="None"/>), or a URN-D19 table conflict
/// — the two directions of the same profile measures hold different kg/m — with the conflicting table's source label and version.
/// A conflict never carries a value.
/// </summary>
internal sealed record ProfileWeightLookup(ProfileWeightMatch? Match, bool IsConflict, string? ConflictSourceLabel,
    long? ConflictTableVersion)
{
    public static ProfileWeightLookup None { get; } = new(null, false, null, null);

    public static ProfileWeightLookup Found(ProfileWeightMatch match) =>
        new(match ?? throw new ArgumentNullException(nameof(match)), false, null, null);

    public static ProfileWeightLookup Conflict(string sourceLabel, long tableVersion) => new(null, true, sourceLabel, tableVersion);
}

/// <summary>
/// URN-D13 fail-closed default of the vault port: until Platform offers a port that keeps the import file under the user-upload
/// rules (an allowlisted text kind, an owner link to the import record and a refusal while no malware scanner runs), every
/// import answers "vault unavailable" and nothing is imported.
/// </summary>
internal sealed class UnavailableProductProfileWeightFileVault : IProductProfileWeightFileVaultV1
{
    public Task<ProductProfileWeightFileStoreResultV1> StoreAsync(ProductProfileWeightFileV1 file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ProductProfileWeightFileStoreResultV1.Unavailable);
    }
}
