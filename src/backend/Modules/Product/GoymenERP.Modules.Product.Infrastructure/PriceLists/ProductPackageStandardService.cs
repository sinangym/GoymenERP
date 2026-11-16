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
/// B-08 / BS-10 (STK-S12) and SAT-D382 / SAT-D383 (STK-SC36-c): the company's package standard table (pieces per package of a
/// product; 2026-10-21 #9 company + product scope, with a customer's override), imported from an Excel file and then edited in
/// the ERP one entry at a time; every new version takes effect only once approved, from its effective date.
/// <list type="bullet">
/// <item><b>Authority</b>: <see cref="ProductPackageStandardPermissionsV2.Manage"/> (prepare) and
/// <see cref="ProductPackageStandardPermissionsV3.Approve"/> (decide), company-wide in the server-resolved company, measured fresh
/// first and again right before the answer and the save; granted to no role here.</item>
/// <item><b>Import</b>: the server reads the rows from the file (<see cref="ProductPackageStandardWorkbook"/>) and resolves every
/// product code against the shared card; an unknown code or a passive product is a row error and any row error refuses the whole
/// file (nothing is partially imported). Every file that reaches the row check is kept with its result report
/// (<see cref="ProductPackageStandardImportFileRow"/>), accepted or refused (SAT-D383).</item>
/// <item><b>Edit</b>: one product's default or one customer's override is set or removed; the other entries are carried over
/// from the newest approved version. An edit that changes nothing stores its answer but makes no version and no audit event.</item>
/// <item><b>Approval</b>: a new version waits as Pending (only one at a time) until the approver approves it from an effective
/// date (today's business day or later) or rejects it. The versions written before approvals existed stay effective.</item>
/// <item><b>Command</b>: one person, a mandatory reason (and source label for an import), Idempotency-Key (the stored terminal
/// answer replays), the expected table version as decimal text ("0" = none yet; the head version, pending included),
/// correlation/causation and ONE audit event per new version, decision or kept file. Every row is insert-only.</item>
/// <item><b>Read port</b> (<see cref="IProductPackageStandardReaderV3"/>, and V2 for today): access only (authenticated member of
/// the company), no permission; NoSource while no effective entry exists; Unavailable on any storage failure (never throws).</item>
/// </list>
/// </summary>
internal sealed partial class ProductPackageStandardService(
    ProductPriceListDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider,
    IProductPackageStandardCustomerPortV3? customers = null,
    ProductPackageStandardApprovalOptionsV3? approvalOptions = null)
    : IProductPackageStandardServiceV2, IProductPackageStandardReaderV2, IProductPackageStandardServiceV3,
        IProductPackageStandardReaderV3
{
    private const string Permission = ProductPackageStandardPermissionsV2.Manage;
    private const string ApprovePermission = ProductPackageStandardPermissionsV3.Approve;
    internal const string ImportCommandName = "product.package-standards.import.v2";
    internal const string SetCommandName = "product.package-standards.set.v2";
    internal const string SetCustomerCommandName = "product.package-standards.set-customer.v3";
    internal const string ApproveCommandName = "product.package-standards.approve.v3";
    internal const string RejectCommandName = "product.package-standards.reject.v3";
    private const string EntityType = "Product.PackageStandardTable";
    private const string ImportFileEntityType = "Product.PackageStandardImportFile";
    private const int ProductInactive = (int)Domain.Products.ProductLifecycle.Inactive;

    // Product codes are resolved in bounded batches so that one query never carries an unbounded parameter list.
    private const int CodeLookupBatchSize = 500;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ProductPackageStandardTableViewV2> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var scope = await MeasureAsync(cancellationToken);
        var head = await CurrentVersionAsync(scope.CompanyId, cancellationToken);
        var effective = await EffectiveHeaderAsync(scope.CompanyId, Today(), cancellationToken);
        var headText = head.ToString(CultureInfo.InvariantCulture);
        if (effective is null)
        {
            await MeasureAsync(cancellationToken);
            return new ProductPackageStandardTableViewV2(scope.CompanyId, headText, null, null, null, null, null, null, []);
        }

        var header = effective.Header;
        var entries = await (
                from line in database.PackageStandardLines.AsNoTracking()
                join product in database.Products.AsNoTracking() on line.ProductId equals product.Id
                where line.CompanyId == scope.CompanyId && line.Version == header.Version
                orderby product.Code, line.ProductId
                select new ProductPackageStandardEntryV2(line.ProductId, product.Code, product.Name, line.PiecesPerPackage))
            .ToListAsync(cancellationToken);
        await MeasureAsync(cancellationToken);
        return new ProductPackageStandardTableViewV2(scope.CompanyId, headText, header.SourceKind, header.SourceLabel,
            header.FileName, header.FileSha256, header.CreatedAtUtc, header.CreatedBy, entries);
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

        var workbook = ProductPackageStandardWorkbook.Read(content);
        var errors = workbook.Errors.ToList();
        var resolved = await ResolveCodesAsync(workbook.Rows, errors, cancellationToken);
        var file = new KeptFile(VersionIdOf(ImportCommandName + ".file", scope.CompanyId, keyHash), fileName, fileSha256,
            content, sourceLabel, reason, workbook.Rows.Count, resolved.Count, errors);
        if (errors.Count > 0)
        {
            return await RefuseImportAsync(scope, file, FileInvalid(Describe(errors)), keyHash, requestHash, metadata,
                cancellationToken);
        }

        var currentVersion = await CurrentVersionAsync(scope.CompanyId, cancellationToken);
        if (currentVersion != expectedVersion)
        {
            return await RefuseImportAsync(scope, file, VersionConflict(), keyHash, requestHash, metadata, cancellationToken);
        }

        if (await PendingHeaderAsync(scope.CompanyId, cancellationToken) is not null)
        {
            return await RefuseImportAsync(scope, file, ApprovalPending(), keyHash, requestHash, metadata, cancellationToken);
        }

        // The file replaces the product defaults; the customer overrides of the approved base are carried over.
        var basis = await ApprovedBaseAsync(scope.CompanyId, cancellationToken);
        var defaults = resolved.ToDictionary(row => row.ProductId, row => row.Pieces);
        var version = checked(currentVersion + 1);
        var versionId = VersionIdOf(ImportCommandName, scope.CompanyId, keyHash);
        AddVersion(scope.CompanyId, version, versionId, ProductPackageStandardSourceKindsV2.Import, sourceLabel, reason,
            fileName, fileSha256, content.Length, null, null, defaults, basis.Overrides, metadata);
        AddKeptFile(scope.CompanyId, file, ProductPackageStandardImportOutcomesV3.Accepted, null, version, metadata);

        var after = new
        {
            VersionId = versionId, Version = version, SourceKind = ProductPackageStandardSourceKindsV2.Import,
            SourceLabel = sourceLabel, Reason = reason, FileName = fileName, FileSha256 = fileSha256,
            FileSizeBytes = content.Length, EntryCount = defaults.Count, CustomerEntryCount = basis.Overrides.Count,
            ImportId = file.ImportId, State = ProductPackageStandardVersionStatesV3.Pending,
        };
        auditEvents.Enqueue(new AuditEventDraft(scope.CompanyId, "product.package-standards.imported.v2", EntityType,
            versionId.ToString("D"), "ImportPackageStandardTable", currentVersion == 0 ? null : currentVersion, version,
            AuditActorTypes.User, metadata.ActorId, Permission, scope.RoleCodes, "{}", JsonSerializer.Serialize(after, Json),
            "Şirketin paket standardı tablosu Excel dosyasından içe aktarıldı ve onaya sunuldu; kaynak etiketi ve gerekçe kayıt "
            + "içeriğindedir",
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

        if (EntryRefusal(request, out var expectedVersion, out var reason) is { } refusal)
        {
            return Unstored(refusal, metadata);
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

        return await ProposeEntryAsync(scope, SetCommandName, null, productId, pieces, expectedVersion, reason, keyHash,
            requestHash, metadata, cancellationToken);
    }

    /// <summary>
    /// One entry's proposal (a product default when <paramref name="customerId"/> is null, otherwise that customer's override):
    /// the card must exist, a value needs an active card, the table must be at the expected version with no version waiting,
    /// and the entry must change against the approved base. The new version carries every other entry of the base over.
    /// </summary>
    private async Task<ProductPackageStandardResultV2> ProposeEntryAsync(Scope scope, string commandName, Guid? customerId,
        Guid productId, int? pieces, long expectedVersion, string reason, string keyHash, string requestHash,
        ProductPackageStandardCommandMetadataV2 metadata, CancellationToken cancellationToken)
    {
        var lifecycle = await database.Products.AsNoTracking()
            .Where(product => product.Id == productId)
            .Select(product => (int?)product.Lifecycle)
            .SingleOrDefaultAsync(cancellationToken);
        if (lifecycle is null)
        {
            return await StoreAsync(commandName, new ProductPackageStandardAnswerV2(404, null, "Ürün bulunamadı.",
                "İstenen ürün kartı yok."), keyHash, requestHash, metadata, cancellationToken);
        }

        if (pieces is not null && lifecycle == ProductInactive)
        {
            return await StoreAsync(commandName, Problem(409, ProductPackageStandardProblemCodesV3.ProductInactive,
                "Ürün pasif.", "Pasif ürün kartına paket standardı yazılmaz."), keyHash, requestHash, metadata,
                cancellationToken);
        }

        var currentVersion = await CurrentVersionAsync(scope.CompanyId, cancellationToken);
        if (currentVersion != expectedVersion)
        {
            return await StoreAsync(commandName, VersionConflict(), keyHash, requestHash, metadata, cancellationToken);
        }

        if (await PendingHeaderAsync(scope.CompanyId, cancellationToken) is not null)
        {
            return await StoreAsync(commandName, ApprovalPending(), keyHash, requestHash, metadata, cancellationToken);
        }

        var basis = await ApprovedBaseAsync(scope.CompanyId, cancellationToken);
        var defaults = new Dictionary<Guid, int>(basis.Defaults);
        var overrides = new Dictionary<(Guid CustomerId, Guid ProductId), int>(basis.Overrides);
        int? before;
        if (customerId is { } customer)
        {
            before = overrides.TryGetValue((customer, productId), out var current) ? current : null;
            if (pieces is { } value)
            {
                overrides[(customer, productId)] = value;
            }
            else
            {
                overrides.Remove((customer, productId));
            }
        }
        else
        {
            before = defaults.TryGetValue(productId, out var current) ? current : null;
            if (pieces is { } value)
            {
                defaults[productId] = value;
            }
            else
            {
                defaults.Remove(productId);
            }
        }

        if (before == pieces)
        {
            // No change: no version move and no audit event; the key still records its answer.
            return await StoreAsync(commandName, NoContent(), keyHash, requestHash, metadata, cancellationToken);
        }

        var version = checked(currentVersion + 1);
        var versionId = VersionIdOf(commandName, scope.CompanyId, keyHash);
        AddVersion(scope.CompanyId, version, versionId, ProductPackageStandardSourceKindsV2.Edit, null, reason, null, null, null,
            productId, customerId, defaults, overrides, metadata);

        var beforeJson = JsonSerializer.Serialize(new { CustomerId = customerId, ProductId = productId, PiecesPerPackage = before },
            Json);
        var afterJson = JsonSerializer.Serialize(new
        {
            VersionId = versionId, Version = version, SourceKind = ProductPackageStandardSourceKindsV2.Edit,
            CustomerId = customerId, ProductId = productId, PiecesPerPackage = pieces, Reason = reason,
            EntryCount = defaults.Count, CustomerEntryCount = overrides.Count, BasisVersion = basis.Version,
            State = ProductPackageStandardVersionStatesV3.Pending,
        }, Json);
        var (eventType, operation, summary) = (customerId, pieces) switch
        {
            (null, null) => ("product.package-standards.entry-set.v2", "SetPackageStandardEntry",
                "Ürünün paket standardı kaydının kaldırılması onaya sunuldu; gerekçe kayıt içeriğindedir"),
            (null, _) => ("product.package-standards.entry-set.v2", "SetPackageStandardEntry",
                "Ürünün paket içi adedi onaya sunuldu; gerekçe kayıt içeriğindedir"),
            (_, null) => ("product.package-standards.customer-entry-set.v3", "SetPackageStandardCustomerEntry",
                "Müşteriye özel paket standardının kaldırılması onaya sunuldu; gerekçe kayıt içeriğindedir"),
            _ => ("product.package-standards.customer-entry-set.v3", "SetPackageStandardCustomerEntry",
                "Müşteriye özel paket içi adet onaya sunuldu; gerekçe kayıt içeriğindedir"),
        };
        auditEvents.Enqueue(new AuditEventDraft(scope.CompanyId, eventType, EntityType, versionId.ToString("D"), operation,
            currentVersion == 0 ? null : currentVersion, version, AuditActorTypes.User, metadata.ActorId, Permission,
            scope.RoleCodes, beforeJson, afterJson, summary,
            ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null, metadata.SourceChannel,
            metadata.ClientIpAddress, metadata.UserAgent, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
        return await SaveVersionAsync(commandName, keyHash, requestHash, metadata, cancellationToken);
    }

    async Task<ProductPackageStandardReadV2> IProductPackageStandardReaderV2.GetAsync(Guid productId,
        CancellationToken cancellationToken)
    {
        var read = await ReadAsync(productId, null, Today(), cancellationToken);
        return new ProductPackageStandardReadV2(read.Outcome, read.PiecesPerPackage, read.StandardVersion,
            read.StandardVersionId, read.SourceKind, read.SourceLabel);
    }

    Task<ProductPackageStandardReadV3> IProductPackageStandardReaderV3.GetAsync(Guid productId, Guid? customerId, DateOnly on,
        CancellationToken cancellationToken) => ReadAsync(productId, customerId, on, cancellationToken);

    /// <summary>
    /// The value in effect on <paramref name="on"/>: the customer's override in the effective version if any, otherwise the
    /// product default of that version.
    /// </summary>
    private async Task<ProductPackageStandardReadV3> ReadAsync(Guid productId, Guid? customerId, DateOnly on,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId) || !companyContext.IsResolved
            || companyContext.CompanyId == Guid.Empty || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            return ProductPackageStandardReadV3.Without(ProductPackageStandardReadOutcomeV2.Denied);
        }

        try
        {
            if (productId == Guid.Empty || customerId == Guid.Empty
                || !await database.Products.AsNoTracking().AnyAsync(product => product.Id == productId, cancellationToken))
            {
                return ProductPackageStandardReadV3.Without(ProductPackageStandardReadOutcomeV2.NotFound);
            }

            var companyId = companyContext.CompanyId;
            var effective = await EffectiveHeaderAsync(companyId, on, cancellationToken);
            if (effective is null)
            {
                return ProductPackageStandardReadV3.Without(ProductPackageStandardReadOutcomeV2.NoSource);
            }

            var header = effective.Header;
            if (customerId is { } customer)
            {
                var overridden = await database.PackageStandardCustomerLines.AsNoTracking()
                    .Where(line => line.CompanyId == companyId && line.Version == header.Version && line.CustomerId == customer
                        && line.ProductId == productId)
                    .Select(line => (int?)line.PiecesPerPackage)
                    .SingleOrDefaultAsync(cancellationToken);
                if (overridden is { } value)
                {
                    return Found(value, ProductPackageStandardEntryScopesV3.Customer);
                }
            }

            var pieces = await database.PackageStandardLines.AsNoTracking()
                .Where(line => line.CompanyId == companyId && line.Version == header.Version && line.ProductId == productId)
                .Select(line => (int?)line.PiecesPerPackage)
                .SingleOrDefaultAsync(cancellationToken);
            return pieces is { } fallback
                ? Found(fallback, ProductPackageStandardEntryScopesV3.Product)
                : ProductPackageStandardReadV3.Without(ProductPackageStandardReadOutcomeV2.NoSource);

            ProductPackageStandardReadV3 Found(int value, string scope) =>
                new(ProductPackageStandardReadOutcomeV2.Found, value, header.Version, header.VersionId, header.SourceKind,
                    header.SourceLabel, scope, effective.EffectiveFrom);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ProductPackageStandardReadV3.Without(ProductPackageStandardReadOutcomeV2.Unavailable);
        }
    }

    private async Task<long> CurrentVersionAsync(Guid companyId, CancellationToken cancellationToken) =>
        await database.PackageStandardVersions.AsNoTracking()
            .Where(version => version.CompanyId == companyId)
            .Select(version => (long?)version.Version)
            .MaxAsync(cancellationToken) ?? 0;

    /// <summary>
    /// Resolves each accepted row's product code against the shared card by exact (ordinal) code; an unknown code or a passive
    /// (inactive) card is a row error (SAT-D383).
    /// </summary>
    private async Task<IReadOnlyList<(Guid ProductId, int Pieces)>> ResolveCodesAsync(
        IReadOnlyList<PackageStandardSourceRow> rows, List<string> errors, CancellationToken cancellationToken)
    {
        var byCode = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
        var inactive = new HashSet<Guid>();
        foreach (var batch in rows.Select(row => row.ProductCode).Distinct(StringComparer.Ordinal).Chunk(CodeLookupBatchSize))
        {
            var found = await database.Products.AsNoTracking()
                .Where(product => batch.Contains(product.Code))
                .Select(product => new { product.Id, product.Code, product.Lifecycle })
                .ToListAsync(cancellationToken);
            foreach (var product in found)
            {
                if (!byCode.TryGetValue(product.Code, out var ids))
                {
                    byCode[product.Code] = ids = [];
                }

                ids.Add(product.Id);
                if (product.Lifecycle == ProductInactive)
                {
                    inactive.Add(product.Id);
                }
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

            if (inactive.Contains(ids[0]))
            {
                errors.Add(ProductPackageStandardWorkbook.RowError(row.RowNumber,
                    $"'{row.ProductCode}' ürünü pasif; pasif ürüne paket standardı yazılmaz."));
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
        await MeasureAsync(PermissionOf(commandName), cancellationToken);
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

    private Task<Scope> MeasureAsync(CancellationToken cancellationToken) => MeasureAsync(Permission, cancellationToken);

    private async Task<Scope> MeasureAsync(string permission, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId) || !companyContext.IsResolved
            || companyContext.CompanyId == Guid.Empty || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw new UnauthorizedAccessException();
        }

        var measured = await authority.RevalidatePermissionScopeAsync(permission, companyContext.CompanyId, cancellationToken);
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
        await MeasureAsync(PermissionOf(commandName), cancellationToken);
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
