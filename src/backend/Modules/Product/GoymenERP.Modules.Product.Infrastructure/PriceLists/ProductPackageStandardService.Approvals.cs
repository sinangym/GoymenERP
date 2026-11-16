using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.PriceLists;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

/// <summary>
/// SAT-D382 / SAT-D383 (STK-SC36-c): the customer override, the approval decision with its effective date and the kept import
/// files of the package standard table.
/// </summary>
internal sealed partial class ProductPackageStandardService
{
    // The Cari display read answers at most this many identities per call (its own contract bound).
    private const int CustomerLookupBatchSize = 100;

    private ProductPackageStandardApprovalOptionsV3 ApprovalOptions =>
        approvalOptions ?? ProductPackageStandardApprovalOptionsV3.Closed;

    public Task<ProductPackageStandardApprovalViewV3> GetApprovalStateAsync(CancellationToken cancellationToken = default) =>
        ApprovalViewAsync(Permission, cancellationToken);

    public Task<ProductPackageStandardApprovalViewV3> GetApprovalStateForApproverAsync(
        CancellationToken cancellationToken = default) => ApprovalViewAsync(ApprovePermission, cancellationToken);

    public async Task<ProductPackageStandardImportFileListV3> ListImportsAsync(CancellationToken cancellationToken = default)
    {
        var scope = await MeasureAsync(cancellationToken);
        var rows = await database.PackageStandardImportFiles.AsNoTracking()
            .Where(file => file.CompanyId == scope.CompanyId)
            .OrderByDescending(file => file.CreatedAtUtc)
            .ThenByDescending(file => file.ImportId)
            .Take(ProductPackageStandardListLimitsV3.MaximumListedImports)
            .Select(KeptFileProjection)
            .ToListAsync(cancellationToken);
        await MeasureAsync(cancellationToken);
        return new ProductPackageStandardImportFileListV3(scope.CompanyId, [.. rows.Select(row =>
        {
            var report = ReportOf(row.ReportJson);
            return new ProductPackageStandardImportFileV3(row.ImportId, row.FileName, row.FileSha256, row.FileSizeBytes,
                row.Outcome, row.ProblemCode, VersionText(row.Version), row.SourceLabel, report.RowCount, report.Errors.Count,
                row.CreatedAtUtc, row.CreatedBy);
        })]);
    }

    public async Task<ProductPackageStandardImportReportV3?> GetImportReportAsync(Guid importId,
        CancellationToken cancellationToken = default)
    {
        var scope = await MeasureAsync(cancellationToken);
        var row = importId == Guid.Empty
            ? null
            : await database.PackageStandardImportFiles.AsNoTracking()
                .Where(file => file.CompanyId == scope.CompanyId && file.ImportId == importId)
                .Select(KeptFileProjection)
                .SingleOrDefaultAsync(cancellationToken);
        await MeasureAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var report = ReportOf(row.ReportJson);
        return new ProductPackageStandardImportReportV3(row.ImportId, row.FileName, row.FileSha256, row.Outcome, row.ProblemCode,
            report.Detail, report.RowCount, report.AcceptedCount, report.Errors, VersionText(row.Version), row.CreatedAtUtc,
            row.CreatedBy);
    }

    public async Task<ProductPackageStandardResultV2> SetCustomerAsync(Guid customerId, Guid productId,
        SetProductPackageStandardRequestV2 request, ProductPackageStandardCommandMetadataV2 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireMetadata(metadata);
        var scope = await MeasureAsync(cancellationToken);
        if (customerId == Guid.Empty || productId == Guid.Empty)
        {
            return Unstored(Problem(400, ProductPackageStandardProblemCodesV2.RequestInvalid, "İstek geçersiz.",
                "Müşteri ve ürün kimliği zorunludur."), metadata);
        }

        if (EntryRefusal(request, out var expectedVersion, out var reason) is { } refusal)
        {
            return Unstored(refusal, metadata);
        }

        var pieces = request.PiecesPerPackage;
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            CustomerId = customerId, ProductId = productId, ExpectedVersion = expectedVersion, PiecesPerPackage = pieces,
            Reason = reason,
        });
        if (await FindAttemptAsync(SetCustomerCommandName, keyHash, cancellationToken) is { } existing)
        {
            await MeasureAsync(cancellationToken);
            return Replay(existing, requestHash, metadata);
        }

        // Removing an override needs no customer check (the customer may have been passivated since); setting one does.
        if (pieces is not null)
        {
            var answer = customers is null
                ? new ProductPackageStandardCustomerAnswerV3(ProductPackageStandardCustomerOutcomeV3.Unavailable, [])
                : await customers.ReadAsync([customerId], cancellationToken);
            switch (answer.Outcome)
            {
                case ProductPackageStandardCustomerOutcomeV3.Denied:
                    throw new UnauthorizedAccessException();
                case ProductPackageStandardCustomerOutcomeV3.Found:
                    break;
                default:
                    return Unstored(Problem(503, ProductPackageStandardProblemCodesV3.CustomerProviderUnavailable,
                        "Cari bilgisi okunamadı.", "Müşteri doğrulanamadı; müşteriye özel kayıt yazılmadı. Daha sonra yeniden "
                        + "deneyin."), metadata);
            }

            if (!answer.Customers.Any(customer => customer.CustomerId == customerId))
            {
                return await StoreAsync(SetCustomerCommandName, Problem(409,
                    ProductPackageStandardProblemCodesV3.CustomerNotFound, "Müşteri bulunamadı.",
                    "Bu şirkette etkin bir müşteri hesabı bulunamadı."), keyHash, requestHash, metadata, cancellationToken);
            }
        }

        return await ProposeEntryAsync(scope, SetCustomerCommandName, customerId, productId, pieces, expectedVersion, reason,
            keyHash, requestHash, metadata, cancellationToken);
    }

    public async Task<ProductPackageStandardResultV2> ApproveAsync(long version,
        ApproveProductPackageStandardVersionRequestV3 request, ProductPackageStandardCommandMetadataV2 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireMetadata(metadata);
        var scope = await MeasureAsync(ApprovePermission, cancellationToken);
        if (version < 1)
        {
            return Unstored(VersionInvalid(), metadata);
        }

        if (ProductPriceListWire.ParseDate(request.EffectiveFrom) is not { } effectiveFrom)
        {
            return Unstored(EffectiveDateInvalid(), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            Version = version, EffectiveFrom = ProductPriceListWire.FormatDate(effectiveFrom), Reason = reason,
        });
        if (await FindAttemptAsync(ApproveCommandName, keyHash, cancellationToken) is { } existing)
        {
            await MeasureAsync(ApprovePermission, cancellationToken);
            return Replay(existing, requestHash, metadata);
        }

        // Checked after the replay: a stored approval replays on a later day as well.
        if (effectiveFrom < Today())
        {
            return Unstored(EffectiveDateInvalid(), metadata);
        }

        return await DecideAsync(scope, ApproveCommandName, version, ProductPackageStandardDecisionsV3.Approved, effectiveFrom,
            reason, keyHash, requestHash, metadata, cancellationToken);
    }

    public async Task<ProductPackageStandardResultV2> RejectAsync(long version,
        RejectProductPackageStandardVersionRequestV3 request, ProductPackageStandardCommandMetadataV2 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireMetadata(metadata);
        var scope = await MeasureAsync(ApprovePermission, cancellationToken);
        if (version < 1)
        {
            return Unstored(VersionInvalid(), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new { Version = version, Reason = reason });
        if (await FindAttemptAsync(RejectCommandName, keyHash, cancellationToken) is { } existing)
        {
            await MeasureAsync(ApprovePermission, cancellationToken);
            return Replay(existing, requestHash, metadata);
        }

        return await DecideAsync(scope, RejectCommandName, version, ProductPackageStandardDecisionsV3.Rejected, null, reason,
            keyHash, requestHash, metadata, cancellationToken);
    }

    private async Task<ProductPackageStandardResultV2> DecideAsync(Scope scope, string commandName, long version,
        string decision, DateOnly? effectiveFrom, string reason, string keyHash, string requestHash,
        ProductPackageStandardCommandMetadataV2 metadata, CancellationToken cancellationToken)
    {
        var pending = await PendingHeaderAsync(scope.CompanyId, cancellationToken);
        if (pending is null || pending.Version != version)
        {
            return await StoreAsync(commandName, Problem(409, ProductPackageStandardProblemCodesV3.VersionNotPending,
                "Sürüm onay beklemiyor.", "Bu sürüm onay bekleyen sürüm değil; güncel durumu okuyup yeniden deneyin."),
                keyHash, requestHash, metadata, cancellationToken);
        }

        if (!ApprovalOptions.PreparerMayDecide && string.Equals(pending.CreatedBy, metadata.ActorId, StringComparison.Ordinal))
        {
            return await StoreAsync(commandName, Problem(409, ProductPackageStandardProblemCodesV3.PreparerCannotDecide,
                "Hazırlayan karar veremez.", "Sürümü hazırlayan kişi onu onaylayamaz veya reddedemez."), keyHash, requestHash,
                metadata, cancellationToken);
        }

        if (effectiveFrom is { } from && await LatestEffectiveFromAsync(scope.CompanyId, cancellationToken) is { } latest
            && from < latest)
        {
            // A newer version never takes effect before the approved one it was prepared on (the order stays the version order).
            return await StoreAsync(commandName, Problem(409, ProductPackageStandardProblemCodesV3.EffectiveDateInvalid,
                "Geçerlilik tarihi geçersiz.",
                $"Geçerlilik tarihi, onaylı son sürümün geçerlilik tarihinden ({ProductPriceListWire.FormatDate(latest)}) önce "
                + "olamaz."), keyHash, requestHash, metadata, cancellationToken);
        }

        var decisionId = VersionIdOf(commandName, scope.CompanyId, keyHash);
        var decidedAt = timeProvider.GetUtcNow();
        database.PackageStandardDecisions.Add(new ProductPackageStandardDecisionRow
        {
            CompanyId = scope.CompanyId,
            Version = version,
            DecisionId = decisionId,
            Decision = decision,
            EffectiveFrom = effectiveFrom,
            Reason = reason,
            DecidedAtUtc = decidedAt,
            DecidedBy = metadata.ActorId,
            CorrelationId = metadata.CorrelationId,
        });

        var approved = decision == ProductPackageStandardDecisionsV3.Approved;
        var beforeJson = JsonSerializer.Serialize(new
        {
            pending.VersionId, Version = version, State = ProductPackageStandardVersionStatesV3.Pending,
            PreparedBy = pending.CreatedBy,
        }, Json);
        var afterJson = JsonSerializer.Serialize(new
        {
            pending.VersionId, Version = version, State = decision, DecisionId = decisionId,
            EffectiveFrom = effectiveFrom is { } date ? ProductPriceListWire.FormatDate(date) : null, Reason = reason,
        }, Json);
        auditEvents.Enqueue(new AuditEventDraft(scope.CompanyId,
            approved ? "product.package-standards.version-approved.v3" : "product.package-standards.version-rejected.v3",
            EntityType, pending.VersionId.ToString("D"),
            approved ? "ApprovePackageStandardVersion" : "RejectPackageStandardVersion", version, version,
            AuditActorTypes.User, metadata.ActorId, ApprovePermission, scope.RoleCodes, beforeJson, afterJson,
            approved
                ? "Paket standardı sürümü geçerlilik tarihiyle onaylandı; gerekçe kayıt içeriğindedir"
                : "Paket standardı sürümü reddedildi ve yürürlüğe girmeyecek; gerekçe kayıt içeriğindedir",
            ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null, metadata.SourceChannel,
            metadata.ClientIpAddress, metadata.UserAgent, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
        return await SaveVersionAsync(commandName, keyHash, requestHash, metadata, cancellationToken);
    }

    /// <summary>
    /// A refused import (row errors, a stale expected version or a waiting version): the file is kept with its report, the
    /// refusal is the key's terminal answer and one audit event records the kept file, all in one save.
    /// </summary>
    private async Task<ProductPackageStandardResultV2> RefuseImportAsync(Scope scope, KeptFile file,
        ProductPackageStandardAnswerV2 answer, string keyHash, string requestHash, ProductPackageStandardCommandMetadataV2 metadata,
        CancellationToken cancellationToken)
    {
        await MeasureAsync(cancellationToken);
        database.ChangeTracker.Clear();
        AddKeptFile(scope.CompanyId, file, ProductPackageStandardImportOutcomesV3.Refused, answer.Code, null, answer.Detail,
            metadata);
        AddAttempt(ImportCommandName, answer, keyHash, requestHash, metadata);
        var after = new
        {
            file.ImportId, file.FileName, file.FileSha256, FileSizeBytes = file.Content.Length, file.SourceLabel, file.Reason,
            Outcome = ProductPackageStandardImportOutcomesV3.Refused, ProblemCode = answer.Code, file.RowCount,
            ErrorCount = file.Errors.Count,
        };
        auditEvents.Enqueue(new AuditEventDraft(scope.CompanyId, "product.package-standards.import-file-kept.v3",
            ImportFileEntityType, file.ImportId.ToString("D"), "KeepPackageStandardImportFile", null, null, AuditActorTypes.User,
            metadata.ActorId, Permission, scope.RoleCodes, "{}", JsonSerializer.Serialize(after, Json),
            "Paket standardı dosyası içe aktarılmadı; dosya ve sonuç raporu saklandı",
            ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null, metadata.SourceChannel,
            metadata.ClientIpAddress, metadata.UserAgent, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new ProductPackageStandardResultV2(answer, false, metadata.CorrelationId);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            var winner = await FindAttemptAsync(ImportCommandName, keyHash, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return Replay(winner, requestHash, metadata);
        }
    }

    private void AddKeptFile(Guid companyId, KeptFile file, string outcome, string? problemCode, long? version,
        ProductPackageStandardCommandMetadataV2 metadata) =>
        AddKeptFile(companyId, file, outcome, problemCode, version, "Dosya kabul edildi; yeni sürüm onaya sunuldu.", metadata);

    private void AddKeptFile(Guid companyId, KeptFile file, string outcome, string? problemCode, long? version, string detail,
        ProductPackageStandardCommandMetadataV2 metadata) =>
        database.PackageStandardImportFiles.Add(new ProductPackageStandardImportFileRow
        {
            CompanyId = companyId,
            ImportId = file.ImportId,
            FileName = file.FileName,
            FileSha256 = file.FileSha256,
            FileSizeBytes = file.Content.Length,
            Content = file.Content,
            Outcome = outcome,
            ProblemCode = problemCode,
            ReportJson = JsonSerializer.Serialize(new KeptReport(file.RowCount, file.AcceptedCount, file.Errors, detail), Json),
            Version = version,
            SourceLabel = file.SourceLabel,
            Reason = file.Reason,
            CreatedAtUtc = timeProvider.GetUtcNow(),
            CreatedBy = metadata.ActorId,
            CorrelationId = metadata.CorrelationId,
        });

    private void AddVersion(Guid companyId, long version, Guid versionId, string sourceKind, string? sourceLabel, string reason,
        string? fileName, string? fileSha256, int? fileSizeBytes, Guid? editedProductId, Guid? editedCustomerId,
        IReadOnlyDictionary<Guid, int> defaults, IReadOnlyDictionary<(Guid CustomerId, Guid ProductId), int> overrides,
        ProductPackageStandardCommandMetadataV2 metadata)
    {
        database.PackageStandardVersions.Add(new ProductPackageStandardVersionRow
        {
            CompanyId = companyId,
            Version = version,
            VersionId = versionId,
            SourceKind = sourceKind,
            SourceLabel = sourceLabel,
            Reason = reason,
            FileName = fileName,
            FileSha256 = fileSha256,
            FileSizeBytes = fileSizeBytes,
            EditedProductId = editedProductId,
            EditedCustomerId = editedCustomerId,
            EntryCount = defaults.Count,
            CustomerEntryCount = overrides.Count,
            RequiresApproval = true,
            CreatedAtUtc = timeProvider.GetUtcNow(),
            CreatedBy = metadata.ActorId,
            CorrelationId = metadata.CorrelationId,
        });
        foreach (var (productId, pieces) in defaults)
        {
            database.PackageStandardLines.Add(new ProductPackageStandardLineRow
            {
                CompanyId = companyId, Version = version, ProductId = productId, PiecesPerPackage = pieces,
            });
        }

        foreach (var ((customerId, productId), pieces) in overrides)
        {
            database.PackageStandardCustomerLines.Add(new ProductPackageStandardCustomerLineRow
            {
                CompanyId = companyId, Version = version, CustomerId = customerId, ProductId = productId,
                PiecesPerPackage = pieces,
            });
        }
    }

    /// <summary>The version waiting for a decision (at most one), or null.</summary>
    private Task<ProductPackageStandardVersionRow?> PendingHeaderAsync(Guid companyId, CancellationToken cancellationToken) =>
        database.PackageStandardVersions.AsNoTracking()
            .Where(version => version.CompanyId == companyId && version.RequiresApproval
                && !database.PackageStandardDecisions.Any(decision =>
                    decision.CompanyId == version.CompanyId && decision.Version == version.Version))
            .OrderByDescending(version => version.Version)
            .FirstOrDefaultAsync(cancellationToken);

    private Task<DateOnly?> LatestEffectiveFromAsync(Guid companyId, CancellationToken cancellationToken) =>
        database.PackageStandardDecisions.AsNoTracking()
            .Where(decision => decision.CompanyId == companyId && decision.Decision == ProductPackageStandardDecisionsV3.Approved)
            .MaxAsync(decision => decision.EffectiveFrom, cancellationToken);

    /// <summary>
    /// The version in effect on <paramref name="on"/>: the approved version with the latest effective date not after it (the
    /// higher version on the same date); while none, the newest version written before approvals existed.
    /// </summary>
    private async Task<Effective?> EffectiveHeaderAsync(Guid companyId, DateOnly on, CancellationToken cancellationToken)
    {
        var decision = await database.PackageStandardDecisions.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Decision == ProductPackageStandardDecisionsV3.Approved
                && row.EffectiveFrom <= on)
            .OrderByDescending(row => row.EffectiveFrom)
            .ThenByDescending(row => row.Version)
            .Select(row => new { row.Version, row.EffectiveFrom })
            .FirstOrDefaultAsync(cancellationToken);
        if (decision is not null)
        {
            var approved = await database.PackageStandardVersions.AsNoTracking()
                .SingleAsync(row => row.CompanyId == companyId && row.Version == decision.Version, cancellationToken);
            return new Effective(approved, decision.EffectiveFrom);
        }

        var legacy = await database.PackageStandardVersions.AsNoTracking()
            .Where(row => row.CompanyId == companyId && !row.RequiresApproval)
            .OrderByDescending(row => row.Version)
            .FirstOrDefaultAsync(cancellationToken);
        return legacy is null ? null : new Effective(legacy, null);
    }

    /// <summary>
    /// The newest approved (or pre-approval) version and its entries: the base a new proposal is prepared on and compared with.
    /// </summary>
    private async Task<Basis> ApprovedBaseAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var approved = await database.PackageStandardDecisions.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Decision == ProductPackageStandardDecisionsV3.Approved)
            .MaxAsync(row => (long?)row.Version, cancellationToken);
        var legacy = await database.PackageStandardVersions.AsNoTracking()
            .Where(row => row.CompanyId == companyId && !row.RequiresApproval)
            .MaxAsync(row => (long?)row.Version, cancellationToken);
        var version = Math.Max(approved ?? 0, legacy ?? 0);
        if (version == 0)
        {
            return new Basis(0, new Dictionary<Guid, int>(), new Dictionary<(Guid CustomerId, Guid ProductId), int>());
        }

        var (defaults, overrides) = await EntriesAsync(companyId, version, cancellationToken);
        return new Basis(version, defaults, overrides);
    }

    private async Task<(Dictionary<Guid, int> Defaults, Dictionary<(Guid CustomerId, Guid ProductId), int> Overrides)>
        EntriesAsync(Guid companyId, long version, CancellationToken cancellationToken)
    {
        var defaults = await database.PackageStandardLines.AsNoTracking()
            .Where(line => line.CompanyId == companyId && line.Version == version)
            .ToDictionaryAsync(line => line.ProductId, line => line.PiecesPerPackage, cancellationToken);
        var overrides = (await database.PackageStandardCustomerLines.AsNoTracking()
                .Where(line => line.CompanyId == companyId && line.Version == version)
                .ToListAsync(cancellationToken))
            .ToDictionary(line => (line.CustomerId, line.ProductId), line => line.PiecesPerPackage);
        return (defaults, overrides);
    }

    private async Task<ProductPackageStandardApprovalViewV3> ApprovalViewAsync(string permission,
        CancellationToken cancellationToken)
    {
        var scope = await MeasureAsync(permission, cancellationToken);
        var companyId = scope.CompanyId;
        var today = Today();
        var head = await CurrentVersionAsync(companyId, cancellationToken);
        var effective = await EffectiveHeaderAsync(companyId, today, cancellationToken);
        var pending = await PendingHeaderAsync(companyId, cancellationToken);

        var effectiveOverrides = effective is null
            ? new List<(Guid CustomerId, Guid ProductId, int PiecesPerPackage)>()
            : (await database.PackageStandardCustomerLines.AsNoTracking()
                .Where(line => line.CompanyId == companyId && line.Version == effective.Header.Version)
                .ToListAsync(cancellationToken))
            .Select(line => (line.CustomerId, line.ProductId, line.PiecesPerPackage))
            .ToList();

        var changes = new List<(Guid? CustomerId, Guid ProductId, int? Before, int? After)>();
        if (pending is not null)
        {
            var basis = await ApprovedBaseAsync(companyId, cancellationToken);
            var (defaults, overrides) = await EntriesAsync(companyId, pending.Version, cancellationToken);
            foreach (var productId in basis.Defaults.Keys.Union(defaults.Keys))
            {
                int? before = basis.Defaults.TryGetValue(productId, out var b) ? b : null;
                int? after = defaults.TryGetValue(productId, out var a) ? a : null;
                if (before != after)
                {
                    changes.Add((null, productId, before, after));
                }
            }

            foreach (var key in basis.Overrides.Keys.Union(overrides.Keys))
            {
                int? before = basis.Overrides.TryGetValue(key, out var b) ? b : null;
                int? after = overrides.TryGetValue(key, out var a) ? a : null;
                if (before != after)
                {
                    changes.Add((key.CustomerId, key.ProductId, before, after));
                }
            }
        }

        var productIds = effectiveOverrides.Select(entry => entry.ProductId).Concat(changes.Select(change => change.ProductId))
            .Distinct().ToList();
        var products = new Dictionary<Guid, (string Code, string Name)>();
        foreach (var batch in productIds.Chunk(CodeLookupBatchSize))
        {
            foreach (var product in await database.Products.AsNoTracking()
                         .Where(product => batch.Contains(product.Id))
                         .Select(product => new { product.Id, product.Code, product.Name })
                         .ToListAsync(cancellationToken))
            {
                products[product.Id] = (product.Code, product.Name);
            }
        }

        var customerIds = effectiveOverrides.Select(entry => entry.CustomerId)
            .Concat(changes.Where(change => change.CustomerId is not null).Select(change => change.CustomerId!.Value))
            .Distinct().ToList();
        var (namesAvailable, names) = await CustomerNamesAsync(customerIds, cancellationToken);

        var recent = await (
                from version in database.PackageStandardVersions.AsNoTracking()
                where version.CompanyId == companyId
                join decision in database.PackageStandardDecisions.AsNoTracking()
                    on new { version.CompanyId, version.Version } equals new { decision.CompanyId, decision.Version } into decided
                from decision in decided.DefaultIfEmpty()
                orderby version.Version descending
                select new { version, decision })
            .Take(ProductPackageStandardListLimitsV3.MaximumListedVersions)
            .ToListAsync(cancellationToken);
        var pendingSummary = pending is null ? null : Summary(pending, null);

        await MeasureAsync(permission, cancellationToken);
        return new ProductPackageStandardApprovalViewV3(
            companyId,
            head.ToString(CultureInfo.InvariantCulture),
            ProductPriceListWire.FormatDate(today),
            effective is null ? null : effective.Header.Version.ToString(CultureInfo.InvariantCulture),
            effective?.EffectiveFrom is { } from ? ProductPriceListWire.FormatDate(from) : null,
            namesAvailable,
            [.. effectiveOverrides
                .Select(entry => new ProductPackageStandardCustomerEntryV3(entry.CustomerId,
                    names.TryGetValue(entry.CustomerId, out var customer) ? customer.Code : null,
                    names.TryGetValue(entry.CustomerId, out customer) ? customer.Name : null, entry.ProductId,
                    ProductOf(entry.ProductId).Code, ProductOf(entry.ProductId).Name, entry.PiecesPerPackage))
                .OrderBy(entry => entry.ProductCode, StringComparer.Ordinal)
                .ThenBy(entry => entry.CustomerCode ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(entry => entry.CustomerId)],
            pendingSummary,
            [.. changes
                .Select(change => new ProductPackageStandardChangeV3(change.CustomerId,
                    change.CustomerId is { } id && names.TryGetValue(id, out var customer) ? customer.Name : null,
                    change.ProductId, ProductOf(change.ProductId).Code, ProductOf(change.ProductId).Name, change.Before,
                    change.After))
                .OrderBy(change => change.ProductCode, StringComparer.Ordinal)
                .ThenBy(change => change.CustomerId is null ? 0 : 1)
                .ThenBy(change => change.CustomerId)],
            [.. recent.Select(item => Summary(item.version, item.decision))]);

        (string Code, string Name) ProductOf(Guid productId) =>
            products.TryGetValue(productId, out var product) ? product : (string.Empty, string.Empty);
    }

    /// <summary>The customers' codes and names from the Cari owner; any answer but Found shows none (the view still answers).</summary>
    private async Task<(bool Available, Dictionary<Guid, ProductPackageStandardCustomerV3> Names)> CustomerNamesAsync(
        IReadOnlyList<Guid> customerIds, CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, ProductPackageStandardCustomerV3>();
        if (customerIds.Count == 0)
        {
            return (true, names);
        }

        if (customers is null)
        {
            return (false, names);
        }

        foreach (var batch in customerIds.Chunk(CustomerLookupBatchSize))
        {
            var answer = await customers.ReadAsync(batch, cancellationToken);
            if (answer.Outcome != ProductPackageStandardCustomerOutcomeV3.Found)
            {
                return (false, new Dictionary<Guid, ProductPackageStandardCustomerV3>());
            }

            foreach (var customer in answer.Customers.Where(customer => batch.Contains(customer.CustomerId)))
            {
                names[customer.CustomerId] = customer;
            }
        }

        return (true, names);
    }

    private static ProductPackageStandardVersionSummaryV3 Summary(ProductPackageStandardVersionRow version,
        ProductPackageStandardDecisionRow? decision) =>
        new(version.Version.ToString(CultureInfo.InvariantCulture),
            !version.RequiresApproval
                ? ProductPackageStandardVersionStatesV3.Legacy
                : decision is null
                    ? ProductPackageStandardVersionStatesV3.Pending
                    : decision.Decision == ProductPackageStandardDecisionsV3.Approved
                        ? ProductPackageStandardVersionStatesV3.Approved
                        : ProductPackageStandardVersionStatesV3.Rejected,
            version.SourceKind, version.SourceLabel, version.FileName, version.EditedProductId, version.EditedCustomerId,
            version.Reason, version.EntryCount, version.CustomerEntryCount, version.CreatedAtUtc, version.CreatedBy,
            decision?.EffectiveFrom is { } from ? ProductPriceListWire.FormatDate(from) : null, decision?.DecidedBy,
            decision?.DecidedAtUtc, decision?.Reason);

    /// <summary>The kept files of the company without their content (the content is never read back here).</summary>
    private static readonly Expression<Func<ProductPackageStandardImportFileRow, KeptFileRow>> KeptFileProjection =
        file => new KeptFileRow(file.ImportId, file.FileName, file.FileSha256, file.FileSizeBytes, file.Outcome,
            file.ProblemCode, file.ReportJson, file.Version, file.SourceLabel, file.CreatedAtUtc, file.CreatedBy);

    private static KeptReport ReportOf(string json) =>
        JsonSerializer.Deserialize<KeptReport>(json, Json)
        ?? throw new InvalidOperationException("Saklanan paket standardı dosya raporu okunamadı.");

    private static string? VersionText(long? version) => version?.ToString(CultureInfo.InvariantCulture);

    private DateOnly Today() => PriceListBusinessDay.DateOf(timeProvider.GetUtcNow());

    /// <summary>The expected version, the pieces (positive or null to remove) and the reason of one entry's proposal.</summary>
    private static ProductPackageStandardAnswerV2? EntryRefusal(SetProductPackageStandardRequestV2 request,
        out long expectedVersion, out string reason)
    {
        reason = string.Empty;
        if (!TryParseVersion(request.ExpectedVersion, out expectedVersion))
        {
            return ExpectedVersionInvalid();
        }

        if (request.PiecesPerPackage is <= 0)
        {
            return Problem(400, ProductPackageStandardProblemCodesV2.PiecesInvalid, "Paket içi adet geçersiz.",
                "Paket içi adet pozitif tam sayı olmalıdır; kaydı kaldırmak için boş bırakın.");
        }

        return ReasonRefusal(request.Reason, out reason);
    }

    private static ProductPackageStandardAnswerV2 ApprovalPending() =>
        Problem(409, ProductPackageStandardProblemCodesV3.ApprovalPending, "Onay bekleyen sürüm var.",
            "Onay bekleyen bir paket standardı sürümü var; yeni değişiklik için önce o sürüm onaylanmalı veya reddedilmelidir.");

    private static ProductPackageStandardAnswerV2 EffectiveDateInvalid() =>
        Problem(400, ProductPackageStandardProblemCodesV3.EffectiveDateInvalid, "Geçerlilik tarihi geçersiz.",
            "Geçerlilik tarihi yyyy-AA-gg biçiminde ve bugünün iş gününden önce olmayan bir tarih olmalıdır.");

    private static ProductPackageStandardAnswerV2 VersionInvalid() =>
        Problem(400, ProductPackageStandardProblemCodesV2.RequestInvalid, "İstek geçersiz.",
            "Sürüm pozitif bir tam sayı olmalıdır.");

    /// <summary>The permission a command is measured with on its save: the approver's for decisions, the preparer's otherwise.</summary>
    private static string PermissionOf(string commandName) =>
        commandName is ApproveCommandName or RejectCommandName ? ApprovePermission : Permission;

    private sealed record Effective(ProductPackageStandardVersionRow Header, DateOnly? EffectiveFrom);

    private sealed record Basis(
        long Version,
        IReadOnlyDictionary<Guid, int> Defaults,
        IReadOnlyDictionary<(Guid CustomerId, Guid ProductId), int> Overrides);

    private sealed record KeptFile(
        Guid ImportId,
        string FileName,
        string FileSha256,
        byte[] Content,
        string SourceLabel,
        string Reason,
        int RowCount,
        int AcceptedCount,
        IReadOnlyList<string> Errors);

    private sealed record KeptReport(int RowCount, int AcceptedCount, IReadOnlyList<string> Errors, string Detail);

    private sealed record KeptFileRow(
        Guid ImportId,
        string FileName,
        string FileSha256,
        int FileSizeBytes,
        string Outcome,
        string? ProblemCode,
        string ReportJson,
        long? Version,
        string SourceLabel,
        DateTimeOffset CreatedAtUtc,
        string CreatedBy);
}
