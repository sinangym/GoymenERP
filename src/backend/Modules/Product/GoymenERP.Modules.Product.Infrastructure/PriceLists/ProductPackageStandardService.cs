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
/// B-08 / BS-10 (STK-S12): the company's package standard table (pieces per package of a product; 2026-10-21 #9 company +
/// product scope), imported from an Excel file and then edited in the ERP one product at a time.
/// <list type="bullet">
/// <item><b>Authority</b>: <see cref="ProductPackageStandardPermissionsV2.Manage"/> company-wide in the server-resolved company,
/// measured fresh first and again right before the answer and the save; granted to no role here.</item>
/// <item><b>Import</b>: the server reads the rows from the file (<see cref="ProductPackageStandardWorkbook"/>) and resolves every
/// product code against the shared card; any row error refuses the whole file (nothing is partially imported). Only the file's
/// name, SHA-256 and size are kept with the version (the file itself is not stored; whether it must be kept is open).</item>
/// <item><b>Edit</b>: one product's entry is set or removed; the other entries are carried over into the new version. An edit
/// that changes nothing stores its answer but makes no version and no audit event.</item>
/// <item><b>Command</b>: one person, a mandatory reason (and source label for an import), Idempotency-Key (the stored terminal
/// answer replays), the expected table version as decimal text ("0" = none yet), correlation/causation and ONE audit event per
/// new version. Every version is insert-only; the newest is current.</item>
/// <item><b>Read port</b> (<see cref="IProductPackageStandardReaderV2"/>): access only (authenticated member of the company), no
/// permission; NoSource while no table or entry exists; Unavailable on any storage failure (never throws).</item>
/// </list>
/// </summary>
internal sealed class ProductPackageStandardService(
    ProductPriceListDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider) : IProductPackageStandardServiceV2, IProductPackageStandardReaderV2
{
    private const string Permission = ProductPackageStandardPermissionsV2.Manage;
    internal const string ImportCommandName = "product.package-standards.import.v2";
    internal const string SetCommandName = "product.package-standards.set.v2";
    private const string EntityType = "Product.PackageStandardTable";

    // Product codes are resolved in bounded batches so that one query never carries an unbounded parameter list.
    private const int CodeLookupBatchSize = 500;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ProductPackageStandardTableViewV2> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var scope = await MeasureAsync(cancellationToken);
        var header = await CurrentHeaderAsync(scope.CompanyId, cancellationToken);
        if (header is null)
        {
            await MeasureAsync(cancellationToken);
            return new ProductPackageStandardTableViewV2(scope.CompanyId, "0", null, null, null, null, null, null, []);
        }

        var entries = await (
                from line in database.PackageStandardLines.AsNoTracking()
                join product in database.Products.AsNoTracking() on line.ProductId equals product.Id
                where line.CompanyId == scope.CompanyId && line.Version == header.Version
                orderby product.Code, line.ProductId
                select new ProductPackageStandardEntryV2(line.ProductId, product.Code, product.Name, line.PiecesPerPackage))
            .ToListAsync(cancellationToken);
        await MeasureAsync(cancellationToken);
        return new ProductPackageStandardTableViewV2(scope.CompanyId, header.Version.ToString(CultureInfo.InvariantCulture),
            header.SourceKind, header.SourceLabel, header.FileName, header.FileSha256, header.CreatedAtUtc, header.CreatedBy,
            entries);
    }

    public async Task<ProductPackageStandardResultV2> ImportAsync(ImportProductPackageStandardsRequestV2 request,
        ProductPackageStandardCommandMetadataV2 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireMetadata(metadata);
        var scope = await MeasureAsync(cancellationToken);
        if (!TryParseVersion(request.ExpectedVersion, out var expectedVersion))
        {
            return Unstored(ExpectedVersionInvalid(), metadata);
        }

        var sourceLabel = request.SourceLabel?.Trim() ?? string.Empty;
        if (sourceLabel.Length == 0)
        {
            return Unstored(Problem(400, ProductPackageStandardProblemCodesV2.SourceLabelRequired, "Kaynak etiketi gerekli.",
                "Her dosya için kaynak etiketi (ör. dosyanın geldiği liste) zorunludur."), metadata);
        }

        if (sourceLabel.Length > ProductPackageStandardInputLimitsV2.SourceLabelMaximumLength)
        {
            return Unstored(Problem(400, ProductPackageStandardProblemCodesV2.SourceLabelTooLong, "Kaynak etiketi çok uzun.",
                $"Kaynak etiketi en fazla {ProductPackageStandardInputLimitsV2.SourceLabelMaximumLength} karakter olabilir."),
                metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        if (FileRefusal(request, out var fileName, out var content) is { } fileRefusal)
        {
            return Unstored(FileInvalid(fileRefusal), metadata);
        }

        var workbook = ProductPackageStandardWorkbook.Read(content);
        var errors = workbook.Errors.ToList();
        var resolved = await ResolveCodesAsync(workbook.Rows, errors, cancellationToken);
        if (errors.Count > 0)
        {
            return Unstored(FileInvalid(Describe(errors)), metadata);
        }

        var fileSha256 = Convert.ToHexStringLower(SHA256.HashData(content));
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            ExpectedVersion = expectedVersion, SourceLabel = sourceLabel, Reason = reason, FileName = fileName,
            FileSha256 = fileSha256,
        });
        if (await FindAttemptAsync(ImportCommandName, keyHash, cancellationToken) is { } existing)
        {
            await MeasureAsync(cancellationToken);
            return Replay(existing, requestHash, metadata);
        }

        var currentVersion = await CurrentVersionAsync(scope.CompanyId, cancellationToken);
        if (currentVersion != expectedVersion)
        {
            return await StoreAsync(ImportCommandName, VersionConflict(), keyHash, requestHash, metadata, cancellationToken);
        }

        var version = checked(currentVersion + 1);
        var versionId = VersionIdOf(ImportCommandName, scope.CompanyId, keyHash);
        database.PackageStandardVersions.Add(new ProductPackageStandardVersionRow
        {
            CompanyId = scope.CompanyId,
            Version = version,
            VersionId = versionId,
            SourceKind = ProductPackageStandardSourceKindsV2.Import,
            SourceLabel = sourceLabel,
            Reason = reason,
            FileName = fileName,
            FileSha256 = fileSha256,
            FileSizeBytes = content.Length,
            EditedProductId = null,
            EntryCount = resolved.Count,
            CreatedAtUtc = timeProvider.GetUtcNow(),
            CreatedBy = metadata.ActorId,
            CorrelationId = metadata.CorrelationId,
        });
        foreach (var (productId, pieces) in resolved)
        {
            database.PackageStandardLines.Add(new ProductPackageStandardLineRow
            {
                CompanyId = scope.CompanyId, Version = version, ProductId = productId, PiecesPerPackage = pieces,
            });
        }

        var after = new
        {
            VersionId = versionId, Version = version, SourceKind = ProductPackageStandardSourceKindsV2.Import,
            SourceLabel = sourceLabel, Reason = reason, FileName = fileName, FileSha256 = fileSha256,
            FileSizeBytes = content.Length, EntryCount = resolved.Count,
        };
        auditEvents.Enqueue(new AuditEventDraft(scope.CompanyId, "product.package-standards.imported.v2", EntityType,
            versionId.ToString("D"), "ImportPackageStandardTable", currentVersion == 0 ? null : currentVersion, version,
            AuditActorTypes.User, metadata.ActorId, Permission, scope.RoleCodes, "{}", JsonSerializer.Serialize(after, Json),
            "Şirketin paket standardı tablosu Excel dosyasından içe aktarıldı; kaynak etiketi ve gerekçe kayıt içeriğindedir",
            ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null, metadata.SourceChannel,
            metadata.ClientIpAddress, metadata.UserAgent, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
        return await SaveVersionAsync(ImportCommandName, keyHash, requestHash, metadata, cancellationToken);
    }

    public async Task<ProductPackageStandardResultV2> SetAsync(Guid productId, SetProductPackageStandardRequestV2 request,
        ProductPackageStandardCommandMetadataV2 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireMetadata(metadata);
        var scope = await MeasureAsync(cancellationToken);
        if (productId == Guid.Empty)
        {
            return Unstored(Problem(400, ProductPackageStandardProblemCodesV2.RequestInvalid, "İstek geçersiz.",
                "Ürün kimliği zorunludur."), metadata);
        }

        if (!TryParseVersion(request.ExpectedVersion, out var expectedVersion))
        {
            return Unstored(ExpectedVersionInvalid(), metadata);
        }

        if (request.PiecesPerPackage is <= 0)
        {
            return Unstored(Problem(400, ProductPackageStandardProblemCodesV2.PiecesInvalid, "Paket içi adet geçersiz.",
                "Paket içi adet pozitif tam sayı olmalıdır; kaydı kaldırmak için boş bırakın."), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var pieces = request.PiecesPerPackage;
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            ProductId = productId, ExpectedVersion = expectedVersion, PiecesPerPackage = pieces, Reason = reason,
        });
        if (await FindAttemptAsync(SetCommandName, keyHash, cancellationToken) is { } existing)
        {
            await MeasureAsync(cancellationToken);
            return Replay(existing, requestHash, metadata);
        }

        if (!await database.Products.AsNoTracking().AnyAsync(product => product.Id == productId, cancellationToken))
        {
            return await StoreAsync(SetCommandName, new ProductPackageStandardAnswerV2(404, null, "Ürün bulunamadı.",
                "İstenen ürün kartı yok."), keyHash, requestHash, metadata, cancellationToken);
        }

        var currentVersion = await CurrentVersionAsync(scope.CompanyId, cancellationToken);
        if (currentVersion != expectedVersion)
        {
            return await StoreAsync(SetCommandName, VersionConflict(), keyHash, requestHash, metadata, cancellationToken);
        }

        var lines = currentVersion == 0
            ? []
            : await database.PackageStandardLines.AsNoTracking()
                .Where(line => line.CompanyId == scope.CompanyId && line.Version == currentVersion)
                .Select(line => new { line.ProductId, line.PiecesPerPackage })
                .ToListAsync(cancellationToken);
        var entries = lines.ToDictionary(line => line.ProductId, line => line.PiecesPerPackage);
        int? before = entries.TryGetValue(productId, out var current) ? current : null;
        if (before == pieces)
        {
            // No change: no version move and no audit event; the key still records its answer.
            return await StoreAsync(SetCommandName, NoContent(), keyHash, requestHash, metadata, cancellationToken);
        }

        if (pieces is { } value)
        {
            entries[productId] = value;
        }
        else
        {
            entries.Remove(productId);
        }

        var version = checked(currentVersion + 1);
        var versionId = VersionIdOf(SetCommandName, scope.CompanyId, keyHash);
        database.PackageStandardVersions.Add(new ProductPackageStandardVersionRow
        {
            CompanyId = scope.CompanyId,
            Version = version,
            VersionId = versionId,
            SourceKind = ProductPackageStandardSourceKindsV2.Edit,
            SourceLabel = null,
            Reason = reason,
            FileName = null,
            FileSha256 = null,
            FileSizeBytes = null,
            EditedProductId = productId,
            EntryCount = entries.Count,
            CreatedAtUtc = timeProvider.GetUtcNow(),
            CreatedBy = metadata.ActorId,
            CorrelationId = metadata.CorrelationId,
        });
        foreach (var (entryProductId, entryPieces) in entries.OrderBy(entry => entry.Key))
        {
            database.PackageStandardLines.Add(new ProductPackageStandardLineRow
            {
                CompanyId = scope.CompanyId, Version = version, ProductId = entryProductId, PiecesPerPackage = entryPieces,
            });
        }

        var beforeJson = JsonSerializer.Serialize(new { ProductId = productId, PiecesPerPackage = before }, Json);
        var afterJson = JsonSerializer.Serialize(new
        {
            VersionId = versionId, Version = version, SourceKind = ProductPackageStandardSourceKindsV2.Edit,
            ProductId = productId, PiecesPerPackage = pieces, Reason = reason, EntryCount = entries.Count,
        }, Json);
        auditEvents.Enqueue(new AuditEventDraft(scope.CompanyId, "product.package-standards.entry-set.v2", EntityType,
            versionId.ToString("D"), "SetPackageStandardEntry", currentVersion == 0 ? null : currentVersion, version,
            AuditActorTypes.User, metadata.ActorId, Permission, scope.RoleCodes, beforeJson, afterJson,
            pieces is null
                ? "Ürünün paket standardı kaydı şirket tablosundan kaldırıldı; gerekçe kayıt içeriğindedir"
                : "Ürünün paket içi adedi şirket tablosunda ayarlandı; gerekçe kayıt içeriğindedir",
            ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null, metadata.SourceChannel,
            metadata.ClientIpAddress, metadata.UserAgent, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
        return await SaveVersionAsync(SetCommandName, keyHash, requestHash, metadata, cancellationToken);
    }

    async Task<ProductPackageStandardReadV2> IProductPackageStandardReaderV2.GetAsync(Guid productId,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId) || !companyContext.IsResolved
            || companyContext.CompanyId == Guid.Empty || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            return ProductPackageStandardReadV2.Without(ProductPackageStandardReadOutcomeV2.Denied);
        }

        try
        {
            if (productId == Guid.Empty
                || !await database.Products.AsNoTracking().AnyAsync(product => product.Id == productId, cancellationToken))
            {
                return ProductPackageStandardReadV2.Without(ProductPackageStandardReadOutcomeV2.NotFound);
            }

            var companyId = companyContext.CompanyId;
            var header = await CurrentHeaderAsync(companyId, cancellationToken);
            if (header is null)
            {
                return ProductPackageStandardReadV2.Without(ProductPackageStandardReadOutcomeV2.NoSource);
            }

            var pieces = await database.PackageStandardLines.AsNoTracking()
                .Where(line => line.CompanyId == companyId && line.Version == header.Version && line.ProductId == productId)
                .Select(line => (int?)line.PiecesPerPackage)
                .SingleOrDefaultAsync(cancellationToken);
            return pieces is { } value
                ? new ProductPackageStandardReadV2(ProductPackageStandardReadOutcomeV2.Found, value, header.Version,
                    header.VersionId, header.SourceKind, header.SourceLabel)
                : ProductPackageStandardReadV2.Without(ProductPackageStandardReadOutcomeV2.NoSource);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ProductPackageStandardReadV2.Without(ProductPackageStandardReadOutcomeV2.Unavailable);
        }
    }

    private Task<ProductPackageStandardVersionRow?> CurrentHeaderAsync(Guid companyId, CancellationToken cancellationToken) =>
        database.PackageStandardVersions.AsNoTracking()
            .Where(version => version.CompanyId == companyId)
            .OrderByDescending(version => version.Version)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<long> CurrentVersionAsync(Guid companyId, CancellationToken cancellationToken) =>
        await database.PackageStandardVersions.AsNoTracking()
            .Where(version => version.CompanyId == companyId)
            .Select(version => (long?)version.Version)
            .MaxAsync(cancellationToken) ?? 0;

    /// <summary>
    /// Resolves each accepted row's product code against the shared card by exact (ordinal) code; an unknown code is a row error.
    /// </summary>
    private async Task<IReadOnlyList<(Guid ProductId, int Pieces)>> ResolveCodesAsync(
        IReadOnlyList<PackageStandardSourceRow> rows, List<string> errors, CancellationToken cancellationToken)
    {
        var byCode = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
        foreach (var batch in rows.Select(row => row.ProductCode).Distinct(StringComparer.Ordinal).Chunk(CodeLookupBatchSize))
        {
            var found = await database.Products.AsNoTracking()
                .Where(product => batch.Contains(product.Code))
                .Select(product => new { product.Id, product.Code })
                .ToListAsync(cancellationToken);
            foreach (var product in found)
            {
                if (!byCode.TryGetValue(product.Code, out var ids))
                {
                    byCode[product.Code] = ids = [];
                }

                ids.Add(product.Id);
            }
        }

        var resolved = new List<(Guid ProductId, int Pieces)>(rows.Count);
        var seenProducts = new HashSet<Guid>();
        foreach (var row in rows)
        {
            if (!byCode.TryGetValue(row.ProductCode, out var ids) || ids.Count != 1)
            {
                errors.Add(ProductPackageStandardWorkbook.RowError(row.RowNumber,
                    $"'{row.ProductCode}' kodlu ürün kartı bulunamadı."));
                continue;
            }

            if (!seenProducts.Add(ids[0]))
            {
                errors.Add(ProductPackageStandardWorkbook.RowError(row.RowNumber,
                    $"'{row.ProductCode}' ürünü dosyada birden çok kez bulunuyor."));
                continue;
            }

            resolved.Add((ids[0], row.PiecesPerPackage));
        }

        return resolved;
    }

    /// <summary>The refusal detail: the count of all errors and the first <see cref="ProductPackageStandardInputLimitsV2.MaximumListedErrors"/>.</summary>
    private static string Describe(IReadOnlyList<string> errors)
    {
        var listed = errors.Take(ProductPackageStandardInputLimitsV2.MaximumListedErrors);
        var head = errors.Count == 1
            ? "Dosya içe aktarılmadı; tablo değişmedi."
            : string.Create(CultureInfo.InvariantCulture, $"Dosya içe aktarılmadı ({errors.Count} hata); tablo değişmedi.");
        return string.Join(" ", new[] { head }.Concat(listed));
    }

    private async Task<ProductPackageStandardResultV2> SaveVersionAsync(string commandName, string keyHash, string requestHash,
        ProductPackageStandardCommandMetadataV2 metadata, CancellationToken cancellationToken)
    {
        await MeasureAsync(cancellationToken);
        AddAttempt(commandName, NoContent(), keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new ProductPackageStandardResultV2(NoContent(), false, metadata.CorrelationId);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            if (await FindAttemptAsync(commandName, keyHash, cancellationToken) is { } winner)
            {
                return Replay(winner, requestHash, metadata);
            }

            return await StoreAsync(commandName, VersionConflict(), keyHash, requestHash, metadata, cancellationToken);
        }
    }

    private void RequireMetadata(ProductPackageStandardCommandMetadataV2 metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!companyContext.IsResolved || metadata.CompanyId != companyContext.CompanyId
            || !string.Equals(metadata.ActorId, currentUser.UserId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || string.IsNullOrWhiteSpace(metadata.SourceChannel)
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey))
        {
            throw new InvalidOperationException("Paket standardı komut bağlamı geçersiz.");
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

    private static ProductPackageStandardAnswerV2? ReasonRefusal(string? text, out string reason)
    {
        reason = text?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            return Problem(400, ProductPackageStandardProblemCodesV2.ReasonRequired, "Gerekçe gerekli.",
                "Bu değişiklik için gerekçe zorunludur.");
        }

        return reason.Length > ProductPackageStandardInputLimitsV2.ReasonMaximumLength
            ? Problem(400, ProductPackageStandardProblemCodesV2.ReasonTooLong, "Gerekçe çok uzun.",
                $"Gerekçe en fazla {ProductPackageStandardInputLimitsV2.ReasonMaximumLength} karakter olabilir.")
            : null;
    }

    /// <summary>The file name (a bare <c>.xlsx</c> name, no path) and the decoded content within the size bound.</summary>
    private static string? FileRefusal(ImportProductPackageStandardsRequestV2 request, out string fileName, out byte[] content)
    {
        content = [];
        fileName = request.FileName?.Trim() ?? string.Empty;
        if (fileName.Length == 0 || fileName.Length > ProductPackageStandardInputLimitsV2.FileNameMaximumLength
            || fileName.Any(character => char.IsControl(character) || character is '/' or '\\' or ':')
            || !fileName.EndsWith(ProductPackageStandardFileFormatV2.FileExtension, StringComparison.OrdinalIgnoreCase)
            || fileName.Length == ProductPackageStandardFileFormatV2.FileExtension.Length)
        {
            return $"Dosya adı yolsuz ve {ProductPackageStandardFileFormatV2.FileExtension} uzantılı olmalıdır "
                + $"(en fazla {ProductPackageStandardInputLimitsV2.FileNameMaximumLength} karakter).";
        }

        var encoded = request.ContentBase64 ?? string.Empty;
        var buffer = new byte[ProductPackageStandardInputLimitsV2.MaximumFileBytes];
        if (encoded.Length == 0 || !Convert.TryFromBase64String(encoded, buffer, out var written) || written == 0)
        {
            return $"Dosya içeriği boş olamaz ve en fazla {ProductPackageStandardInputLimitsV2.MaximumFileBytes} bayt olabilir.";
        }

        content = buffer[..written];
        return null;
    }

    /// <summary>The version record id for the command, the company and the command key: a retry names the same version.</summary>
    private static Guid VersionIdOf(string commandName, Guid companyId, string keyHash)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{commandName}|{companyId:D}|{keyHash}")));
        return new Guid(hash.AsSpan(0, 16));
    }

    private Task<ProductPackageStandardAttemptRow?> FindAttemptAsync(string commandName, string keyHash,
        CancellationToken cancellationToken) =>
        database.PackageStandardAttempts.AsNoTracking().SingleOrDefaultAsync(
            attempt => attempt.CommandName == commandName && attempt.IdempotencyKeyHash == keyHash, cancellationToken);

    private static ProductPackageStandardResultV2 Replay(ProductPackageStandardAttemptRow attempt, string requestHash,
        ProductPackageStandardCommandMetadataV2 metadata)
    {
        if (!string.Equals(attempt.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return Unstored(Problem(409, ProductPackageStandardProblemCodesV2.IdempotencyConflict,
                "İşlem anahtarı kullanılmış.", "Bu Idempotency-Key başka bir istekle kullanıldı."), metadata);
        }

        var answer = JsonSerializer.Deserialize<ProductPackageStandardAnswerV2>(attempt.AnswerJson, Json);
        if (answer is null || answer.StatusCode != attempt.StatusCode)
        {
            throw new InvalidOperationException("Kalıcı idempotency sonucu paket standardı sözleşmesiyle uyumsuz.");
        }

        return new ProductPackageStandardResultV2(answer, true, attempt.CorrelationId);
    }

    private void AddAttempt(string commandName, ProductPackageStandardAnswerV2 answer, string keyHash, string requestHash,
        ProductPackageStandardCommandMetadataV2 metadata) =>
        database.PackageStandardAttempts.Add(new ProductPackageStandardAttemptRow
        {
            CompanyId = metadata.CompanyId,
            CommandName = commandName,
            IdempotencyKeyHash = keyHash,
            RequestHash = requestHash,
            ActorId = metadata.ActorId,
            StatusCode = answer.StatusCode,
            AnswerJson = JsonSerializer.Serialize(answer, Json),
            CorrelationId = metadata.CorrelationId,
            CreatedAtUtc = timeProvider.GetUtcNow(),
        });

    /// <summary>Stores an answer without a version as the terminal answer of the key; a concurrent same-key winner replays.</summary>
    private async Task<ProductPackageStandardResultV2> StoreAsync(string commandName, ProductPackageStandardAnswerV2 answer,
        string keyHash, string requestHash, ProductPackageStandardCommandMetadataV2 metadata, CancellationToken cancellationToken)
    {
        await MeasureAsync(cancellationToken);
        database.ChangeTracker.Clear();
        AddAttempt(commandName, answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new ProductPackageStandardResultV2(answer, false, metadata.CorrelationId);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            var winner = await FindAttemptAsync(commandName, keyHash, cancellationToken);
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

    private static ProductPackageStandardAnswerV2 ExpectedVersionInvalid() =>
        Problem(400, ProductPackageStandardProblemCodesV2.RequestInvalid, "İstek geçersiz.",
            "Beklenen tablo sürümü (\"0\" veya pozitif tam sayı metni) zorunludur.");

    private static ProductPackageStandardAnswerV2 FileInvalid(string detail) =>
        Problem(400, ProductPackageStandardProblemCodesV2.FileInvalid, "Dosya geçersiz.", detail);

    private static ProductPackageStandardAnswerV2 NoContent() =>
        new(204, null, "Kaydedildi.", "Paket standardı tablosu kaydedildi.");

    private static ProductPackageStandardAnswerV2 VersionConflict() =>
        Problem(409, ProductPackageStandardProblemCodesV2.VersionConflict, "Tablo değişmiş.",
            "Paket standardı tablosu siz açtıktan sonra değişti; güncel hâlini okuyup yeniden deneyin.");

    private static ProductPackageStandardAnswerV2 Problem(int status, string code, string title, string detail) =>
        new(status, code, title, detail);

    private static ProductPackageStandardResultV2 Unstored(ProductPackageStandardAnswerV2 answer,
        ProductPackageStandardCommandMetadataV2 metadata) => new(answer, false, metadata.CorrelationId);

    private sealed record Scope(Guid CompanyId, IReadOnlyCollection<string> RoleCodes);
}
