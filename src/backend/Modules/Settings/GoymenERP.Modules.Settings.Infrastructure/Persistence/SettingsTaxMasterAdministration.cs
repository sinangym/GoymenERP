using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// MOD-03 tax master (SAT-D73/D74/D82/D83, EK12; SAT-D93/D110, EK13): the company's VAT classes, withholding classes and
/// buyer categories, their effective-dated rate records and the UBL-TR exemption / withholding code lists.
/// <list type="bullet">
/// <item><b>Nothing is seeded</b>: every rate and code needs the mali müşavir's confirmation (EK13 research note); until a
/// value is entered every read answers "kaynak yok" (<see cref="TaxResolutionStatesV1.Unresolved"/>).</item>
/// <item><b>Authority</b> (SAT-D83): ONE name, <see cref="SettingsTaxMasterPermissionManifestV1.TaxMasterManage"/>,
/// company-wide, measured first and re-measured fresh right before every save; granted to no role here.</item>
/// <item><b>Every command</b> (SAT-D68 precedent): one person, a mandatory reason (≤ 4000), one Idempotency-Key (the stored
/// terminal answer replays; another body under the key is 409), the version as decimal text, correlation/causation and one
/// audit event per real change (none for a no-op).</item>
/// <item><b>Dates</b> (SAT-D78/D84 precedent): a record takes effect on its business date (omitted = today, Europe/Istanbul);
/// a date before today is refused; a record on the same date as a not-yet-effective one REPLACES it (withdrawn in the same
/// save, audited as a correction), so a later withdrawal never brings a hidden record back; a record in force never
/// changes, a not-yet-effective one can be withdrawn.</item>
/// </list>
/// </summary>
internal sealed partial class SettingsTaxMasterAdministration(
    SettingsDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorizationService authorization,
    IErpAuthorityRevalidationService revalidation,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider,
    ILogger<SettingsTaxMasterAdministration> logger) : ISettingsTaxMasterAdministrationV1
{
    private const string Permission = SettingsTaxMasterPermissionManifestV1.TaxMasterManage;
    private const int CommandExecutionSchemaVersion = 1;
    private const string CategoryEntityType = "Settings.TaxCategory";
    private const string CodeListEntityType = "Settings.TaxCodeList";
    private const string CategoryCreateCommandName = "settings.tax-category.create.v1";
    private const string CategoryRenameCommandName = "settings.tax-category.rename.v1";
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<TaxMasterViewV1> GetAsync(CancellationToken cancellationToken = default)
    {
        var companyId = RequireContextCompany();
        await RequireAsync(companyId, cancellationToken);
        TaxMasterRows rows;
        try
        {
            rows = await TaxMasterRows.ReadAsync(database, trackCategoriesAndLists: false, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.ProviderUnavailable);
        }

        await RequireStillAsync(companyId, cancellationToken);
        var today = SettingsBusinessDate.Today(timeProvider);
        var data = rows.ToData();
        var recordRows = rows.Records.ToDictionary(row => row.Id);
        var series = data.Records
            .Where(record => !record.IsWithdrawn)
            .Select(record => (record.Kind, record.ClassId, record.BuyerCategoryId))
            .Distinct()
            .Select(key => new TaxRateSeriesViewV1(
                key.Kind,
                key.ClassId,
                key.BuyerCategoryId,
                TaxMasterCalculatorV1.RateInForce(data, key.Kind, key.ClassId, key.BuyerCategoryId, today) is { } inForce
                    ? RecordView(recordRows[inForce.RecordId])
                    : null,
                TaxMasterCalculatorV1.ScheduledRates(data, key.Kind, key.ClassId, key.BuyerCategoryId, today)
                    .Select(record => RecordView(recordRows[record.RecordId]))
                    .ToArray()))
            .OrderBy(view => view.Kind, StringComparer.Ordinal)
            .ThenBy(view => view.ClassId)
            .ThenBy(view => view.BuyerCategoryId)
            .ToArray();
        var versionRows = rows.ListVersions.ToDictionary(row => (row.Kind, row.ListVersion));
        var lists = new[] { TaxCodeListKindsV1.Exemption, TaxCodeListKindsV1.Withholding }
            .Select(kind => new TaxCodeListViewV1(
                kind,
                Text(rows.Lists.SingleOrDefault(list => list.Kind == kind)?.Version ?? 0),
                TaxMasterCalculatorV1.ListInForce(data, kind, today) is { } inForce
                    ? ListView(versionRows[(kind, inForce.ListVersion)], inForce)
                    : null,
                TaxMasterCalculatorV1.ScheduledLists(data, kind, today)
                    .Select(list => ListView(versionRows[(kind, list.ListVersion)], list))
                    .ToArray()))
            .ToArray();
        var categories = rows.Categories
            .OrderBy(row => row.Kind, StringComparer.Ordinal)
            .ThenBy(row => row.Code, StringComparer.Ordinal)
            .Select(row => new TaxCategoryViewV1(row.Id, row.Kind, row.Code, row.Name, Text(row.Version)))
            .ToArray();
        return new TaxMasterViewV1(companyId, today, categories, series, lists);
    }

    public async Task<TaxMasterCommandResultV1> CreateCategoryAsync(CreateTaxCategoryRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(companyId, cancellationToken);
        if (!TaxCategoryKindsV1.IsKnown(request.Kind))
        {
            return Unstored(Answers.Invalid("Tür 'VatClass', 'WithholdingClass' veya 'BuyerCategory' olmalıdır."), metadata);
        }

        if (!TaxMasterCalculatorV1.TryNormalizeCategoryCode(request.Code, out var code))
        {
            return Unstored(Answers.Invalid(
                "Kod 1-32 karakter olmalı; yalnız büyük harf (A-Z), rakam, '_' ve '-' içerebilir."), metadata);
        }

        if (!TryNormalizeName(request.Name, TaxMasterInputLimitsV1.CategoryNameMaximumLength, out var name))
        {
            return Unstored(Answers.Invalid("Ad 1-100 karakter olmalı ve kontrol karakteri içermemelidir."), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var kind = request.Kind!;
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new { Kind = kind, Code = code, Name = name, Reason = reason });
        if (await FindExecutionAsync(CategoryCreateCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("category-create", metadata, code, Replay(existing, requestHash, metadata));
        }

        if (await TakenRefusalAsync(kind, code, name, exceptId: null, cancellationToken) is { } taken)
        {
            return Done("category-create", metadata, code,
                await StoreRefusalAsync(CategoryCreateCommandName, taken, keyHash, requestHash, metadata, cancellationToken));
        }

        var now = timeProvider.GetUtcNow();
        var row = new TaxCategoryRow
        {
            CompanyId = companyId,
            Id = Guid.NewGuid(),
            Kind = kind,
            Code = code,
            Name = name,
            Version = 1,
            RecordVersion = 0,
            CreatedAtUtc = now,
            CreatedBy = metadata.ActorId,
            UpdatedAtUtc = now,
            UpdatedBy = metadata.ActorId,
        };
        database.TaxCategories.Add(row);
        Audit(companyId, "settings.tax-category.created.v1", CategoryEntityType, row.Id.ToString("D"), "Create", null,
            row.Version, scope, null, CategorySnapshot(row), reason, metadata);
        await RequireStillAsync(companyId, cancellationToken);
        return Done("category-create", metadata, code, await SaveChangeAsync(CategoryCreateCommandName,
            Answers.Created(row.Id), keyHash, requestHash, metadata,
            async token => await TakenRefusalAsync(kind, code, name, exceptId: null, token) ?? Answers.VersionConflict(),
            cancellationToken));
    }

    public async Task<TaxMasterCommandResultV1> RenameCategoryAsync(Guid categoryId, RenameTaxCategoryRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(companyId, cancellationToken);
        if (categoryId == Guid.Empty || !TryParseVersion(request.ExpectedVersion, 1, out var expectedVersion))
        {
            return Unstored(Answers.Invalid("Kategori ve beklenen sürüm (pozitif tam sayı metni) zorunludur."), metadata);
        }

        if (!TryNormalizeName(request.Name, TaxMasterInputLimitsV1.CategoryNameMaximumLength, out var name))
        {
            return Unstored(Answers.Invalid("Ad 1-100 karakter olmalı ve kontrol karakteri içermemelidir."), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var subject = categoryId.ToString("D");
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            CategoryId = categoryId, ExpectedVersion = expectedVersion, Name = name, Reason = reason,
        });
        if (await FindExecutionAsync(CategoryRenameCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("category-rename", metadata, subject, Replay(existing, requestHash, metadata));
        }

        var row = await database.TaxCategories.SingleOrDefaultAsync(category => category.Id == categoryId, cancellationToken);
        var refusal = row is null ? Answers.NotFound()
            : row.Version != expectedVersion ? Answers.VersionConflict()
            : string.Equals(row.Name, name, StringComparison.Ordinal) ? null
            : await TakenRefusalAsync(row.Kind, null, name, categoryId, cancellationToken);
        if (refusal is not null)
        {
            return Done("category-rename", metadata, subject,
                await StoreRefusalAsync(CategoryRenameCommandName, refusal, keyHash, requestHash, metadata, cancellationToken));
        }

        if (string.Equals(row!.Name, name, StringComparison.Ordinal))
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("category-rename", metadata, subject, await StoreRefusalAsync(CategoryRenameCommandName,
                Answers.NoContent(), keyHash, requestHash, metadata, cancellationToken));
        }

        var before = CategorySnapshot(row);
        row.Name = name;
        Touch(row, metadata);
        Audit(companyId, "settings.tax-category.renamed.v1", CategoryEntityType, subject, "Rename", expectedVersion,
            row.Version, scope, before, CategorySnapshot(row), reason, metadata);
        await RequireStillAsync(companyId, cancellationToken);
        var kind = row.Kind;
        return Done("category-rename", metadata, subject, await SaveChangeAsync(CategoryRenameCommandName, Answers.NoContent(),
            keyHash, requestHash, metadata,
            async token => await TakenRefusalAsync(kind, null, name, categoryId, token) ?? Answers.VersionConflict(),
            cancellationToken));
    }

    private async Task<TaxMasterCommandAnswerV1?> TakenRefusalAsync(string kind, string? code, string name, Guid? exceptId,
        CancellationToken cancellationToken)
    {
        if (code is not null && await database.TaxCategories.AsNoTracking()
                .AnyAsync(category => category.Kind == kind && category.Code == code && category.Id != exceptId, cancellationToken))
        {
            return Answers.Problem(409, TaxMasterProblemCodesV1.CategoryCodeTaken, "Kod kullanılıyor.",
                "Bu türde aynı kodla bir kategori zaten var.");
        }

        return await database.TaxCategories.AsNoTracking()
            .AnyAsync(category => category.Kind == kind && category.Name == name && category.Id != exceptId, cancellationToken)
            ? Answers.Problem(409, TaxMasterProblemCodesV1.CategoryNameTaken, "Ad kullanılıyor.",
                "Bu türde aynı adla bir kategori zaten var.")
            : null;
    }

    private Guid RequireContextCompany()
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId)
            || !companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw new UnauthorizedAccessException();
        }

        return companyContext.CompanyId;
    }

    private async Task<PermissionScope> RequireAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (!currentUser.CanAccessCompany(companyId))
        {
            throw new UnauthorizedAccessException();
        }

        var scope = await authorization.GetPermissionScopeAsync(Permission, companyId, cancellationToken);
        return scope is { IsGranted: true, IsCompanyWide: true } ? scope : throw new UnauthorizedAccessException();
    }

    /// <summary>The same measurement, fresh from the authority rows (a grant revoked meanwhile is seen).</summary>
    private async Task RequireStillAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (!currentUser.CanAccessCompany(companyId))
        {
            throw new UnauthorizedAccessException();
        }

        var scope = await revalidation.RevalidatePermissionScopeAsync(Permission, companyId, cancellationToken);
        if (scope is not { IsGranted: true, IsCompanyWide: true })
        {
            throw new UnauthorizedAccessException();
        }
    }

    private void ValidateMetadata(SettingsCommandMetadataV1 metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!companyContext.IsResolved || metadata.CompanyId != companyContext.CompanyId
            || !string.Equals(metadata.ActorId, currentUser.UserId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(metadata.CorrelationId)
            || string.IsNullOrWhiteSpace(metadata.SourceChannel)
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey))
        {
            throw new InvalidOperationException("Ayarlar komut bağlamı geçersiz.");
        }
    }

    private Task<CommandExecution?> FindExecutionAsync(string commandName, string keyHash, CancellationToken cancellationToken) =>
        database.CommandExecutions.AsNoTracking()
            .SingleOrDefaultAsync(execution => execution.CommandName == commandName && execution.IdempotencyKeyHash == keyHash,
                cancellationToken);

    private static TaxMasterCommandResultV1 Replay(CommandExecution execution, string requestHash, SettingsCommandMetadataV1 metadata)
    {
        if (!string.Equals(execution.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(execution.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new TaxMasterCommandResultV1(Answers.Problem(409, TaxMasterProblemCodesV1.IdempotencyConflict,
                    "İşlem anahtarı kullanılmış.", "Bu Idempotency-Key başka bir istekle kullanıldı."),
                false, metadata.CorrelationId);
        }

        var answer = JsonSerializer.Deserialize<TaxMasterCommandAnswerV1>(execution.ResponseJson, Json);
        if (answer is null || answer.StatusCode != execution.HttpStatusCode
            || !string.Equals(Outcome(answer), execution.Outcome, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Kalıcı idempotency sonucu tanımlı vergi masterı komut sözleşmesiyle uyumsuz.");
        }

        return new TaxMasterCommandResultV1(answer, true, execution.OriginalCorrelationId);
    }

    private void AddExecution(string commandName, TaxMasterCommandAnswerV1 answer, string keyHash, string requestHash,
        SettingsCommandMetadataV1 metadata) =>
        database.CommandExecutions.Add(CommandExecution.Complete(new CommandExecutionData(
            Guid.NewGuid(),
            metadata.CompanyId,
            commandName,
            keyHash,
            requestHash,
            metadata.ActorId,
            Outcome(answer),
            answer.StatusCode,
            JsonSerializer.Serialize(answer, Json),
            metadata.CorrelationId,
            timeProvider.GetUtcNow(),
            CommandExecutionSchemaVersion)));

    /// <summary>Stores a refusal or no-op (no business change) as the key's terminal answer; a same-key winner replays.</summary>
    private async Task<TaxMasterCommandResultV1> StoreRefusalAsync(string commandName, TaxMasterCommandAnswerV1 answer,
        string keyHash, string requestHash, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        AddExecution(commandName, answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new TaxMasterCommandResultV1(answer, false, metadata.CorrelationId);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            var winner = await FindExecutionAsync(commandName, keyHash, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return Replay(winner, requestHash, metadata);
        }
    }

    /// <summary>
    /// Saves a business change with its terminal answer and audit in one unit. A lost race (version, unique key) re-reads:
    /// a same-key winner replays, otherwise <paramref name="onConflict"/> gives the refusal stored as the key's answer.
    /// </summary>
    private async Task<TaxMasterCommandResultV1> SaveChangeAsync(string commandName, TaxMasterCommandAnswerV1 answer,
        string keyHash, string requestHash, SettingsCommandMetadataV1 metadata,
        Func<CancellationToken, Task<TaxMasterCommandAnswerV1>> onConflict, CancellationToken cancellationToken)
    {
        AddExecution(commandName, answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new TaxMasterCommandResultV1(answer, false, metadata.CorrelationId);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            var winner = await FindExecutionAsync(commandName, keyHash, cancellationToken);
            if (winner is not null)
            {
                return Replay(winner, requestHash, metadata);
            }

            var refusal = await onConflict(cancellationToken);
            return await StoreRefusalAsync(commandName, refusal, keyHash, requestHash, metadata, cancellationToken);
        }
    }

    private static string Outcome(TaxMasterCommandAnswerV1 answer) => answer.Code ?? answer.StatusCode switch
    {
        201 => "Created",
        204 => "Succeeded",
        404 => "NotFound",
        _ => "Refused",
    };

    /// <summary>
    /// One audit event: the person's whole reason in the after-image (<c>reason</c>), the fixed description in the audit
    /// row's reason column (the SAT-D68 / discount limit precedent), so nothing is truncated.
    /// </summary>
    private void Audit(Guid companyId, string eventType, string entityType, string entityId, string operation,
        long? versionBefore, long? versionAfter, PermissionScope scope, object? before, object after, string reason,
        SettingsCommandMetadataV1 metadata) =>
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            eventType,
            entityType,
            entityId,
            operation,
            versionBefore,
            versionAfter,
            AuditActorTypes.User,
            metadata.ActorId,
            Permission,
            scope.RoleCodes,
            before is null ? "{}" : JsonSerializer.Serialize(before, Json),
            AfterJson(after, reason),
            AuditDescription(eventType),
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            metadata.SourceChannel,
            metadata.ClientIpAddress,
            metadata.UserAgent,
            metadata.CorrelationId,
            metadata.CausationId,
            metadata.IdempotencyKey));

    private static string AfterJson(object after, string reason)
    {
        var node = JsonSerializer.SerializeToNode(after, Json)!.AsObject();
        node["reason"] = reason;
        return node.ToJsonString(Json);
    }

    private static string AuditDescription(string eventType) => eventType switch
    {
        "settings.tax-category.created.v1" => "Vergi kategorisi oluşturuldu; gerekçe kayıt içeriğindedir",
        "settings.tax-category.renamed.v1" => "Vergi kategorisinin adı değiştirildi; gerekçe kayıt içeriğindedir",
        "settings.tax-rate.recorded.v1" => "Vergi oranı kaydı girildi; gerekçe kayıt içeriğindedir",
        "settings.tax-rate.corrected.v1" => "Yürürlüğe girmemiş vergi oranı kaydı düzeltildi; gerekçe kayıt içeriğindedir",
        "settings.tax-rate.withdrawn.v1" => "Yürürlüğe girmemiş vergi oranı kaydı geri çekildi; gerekçe kayıt içeriğindedir",
        "settings.tax-code-list.published.v1" => "Vergi kod listesi sürümü yayımlandı; gerekçe kayıt içeriğindedir",
        "settings.tax-code-list.corrected.v1" => "Yürürlüğe girmemiş kod listesi sürümü düzeltildi; gerekçe kayıt içeriğindedir",
        "settings.tax-code-list.withdrawn.v1" => "Yürürlüğe girmemiş kod listesi sürümü geri çekildi; gerekçe kayıt içeriğindedir",
        _ => throw new InvalidOperationException("Tanımsız Ayarlar audit olayı."),
    };

    private TaxMasterCommandResultV1 Done(string command, SettingsCommandMetadataV1 metadata, string subject,
        TaxMasterCommandResultV1 result)
    {
        logger.LogInformation(
            "Settings tax master command completed. Command={Command} CompanyId={CompanyId} Subject={Subject} ActorId={ActorId} CorrelationId={CorrelationId} Status={Status} Code={Code} IsReplay={IsReplay}",
            command, metadata.CompanyId, subject, metadata.ActorId, metadata.CorrelationId, result.Answer.StatusCode,
            result.Answer.Code, result.IsReplay);
        return result;
    }

    private void Touch(TaxCategoryRow row, SettingsCommandMetadataV1 metadata)
    {
        row.Version = checked(row.Version + 1);
        row.UpdatedAtUtc = timeProvider.GetUtcNow();
        row.UpdatedBy = metadata.ActorId;
    }

    private static object CategorySnapshot(TaxCategoryRow row) => new
    {
        CategoryId = row.Id,
        row.CompanyId,
        row.Kind,
        row.Code,
        row.Name,
        row.Version,
    };

    private static TaxRateRecordViewV1 RecordView(TaxRateRecordRow row) => new(
        row.Id,
        Text(row.RecordVersion),
        row.EffectiveFrom,
        row.Applies,
        row.RatePercent is { } rate ? TaxMasterCalculatorV1.Format(rate) : null,
        row.WithholdingCode,
        row.RecordedBy,
        row.RecordedAtUtc,
        row.Reason);

    private static TaxCodeListVersionViewV1 ListView(TaxCodeListVersionRow row, TaxCodeListVersionDataV1 data) => new(
        Text(row.ListVersion),
        row.EffectiveFrom,
        data.Entries,
        row.RecordedBy,
        row.RecordedAtUtc,
        row.Reason);

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A version as decimal text: digits only, no sign, no leading zero (other than "0"), within Int64.</summary>
    private static bool TryParseVersion(string? text, long minimum, out long version)
    {
        version = 0;
        return !string.IsNullOrEmpty(text) && text.All(char.IsAsciiDigit) && (text.Length == 1 || text[0] != '0')
            && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out version) && version >= minimum;
    }

    private static TaxMasterCommandAnswerV1? ReasonRefusal(string? reason, out string normalized)
    {
        normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return Answers.Problem(400, TaxMasterProblemCodesV1.ReasonRequired, "Gerekçe gerekli.",
                "Bu değişiklik için gerekçe zorunludur.");
        }

        return normalized.Length > TaxMasterInputLimitsV1.ReasonMaximumLength
            ? Answers.Problem(400, TaxMasterProblemCodesV1.ReasonTooLong, "Gerekçe çok uzun.",
                $"Gerekçe en fazla {TaxMasterInputLimitsV1.ReasonMaximumLength} karakter olabilir.")
            : null;
    }

    private static bool TryNormalizeName(string? name, int maximumLength, out string normalized)
    {
        normalized = name?.Trim() ?? string.Empty;
        var value = normalized;
        return value.Length > 0 && value.Length <= maximumLength && !value.Any(char.IsControl);
    }

    private static TaxMasterCommandResultV1 Unstored(TaxMasterCommandAnswerV1 answer, SettingsCommandMetadataV1 metadata) =>
        new(answer, false, metadata.CorrelationId);

    private static class Answers
    {
        public static TaxMasterCommandAnswerV1 NoContent() => new(204, null, "Kaydedildi.", "Değişiklik kaydedildi.");

        public static TaxMasterCommandAnswerV1 Created(Guid id) => new(201, null, "Oluşturuldu.", "Vergi kategorisi oluşturuldu.",
            new TaxCategoryCreatedV1(id, "1"));

        public static TaxMasterCommandAnswerV1 NotFound() => new(404, null, "Kayıt bulunamadı.",
            "İstenen vergi kaydı bu şirkette bulunamadı.");

        public static TaxMasterCommandAnswerV1 Invalid(string detail) =>
            Problem(400, TaxMasterProblemCodesV1.RequestInvalid, "İstek geçersiz.", detail);

        public static TaxMasterCommandAnswerV1 VersionConflict() =>
            Problem(409, TaxMasterProblemCodesV1.VersionConflict, "Kayıt değişmiş.",
                "Kayıt siz açtıktan sonra değişti; güncel hâlini okuyup yeniden deneyin.");

        public static TaxMasterCommandAnswerV1 EffectiveFromPast() =>
            Problem(422, TaxMasterProblemCodesV1.EffectiveFromPast, "Geçmiş tarih.",
                "Vergi kaydı bugünden önceki bir tarihten başlatılamaz.");

        public static TaxMasterCommandAnswerV1 RecordInEffect() =>
            Problem(409, TaxMasterProblemCodesV1.RecordInEffect, "Kayıt yürürlükte.",
                "Yürürlüğe girmiş kayıt değiştirilemez veya geri çekilemez; yeni bir kayıt girin.");

        public static TaxMasterCommandAnswerV1 RecordWithdrawn() =>
            Problem(409, TaxMasterProblemCodesV1.RecordWithdrawn, "Kayıt geri çekilmiş.", "Bu kayıt daha önce geri çekildi.");

        public static TaxMasterCommandAnswerV1 Problem(int status, string code, string title, string detail) =>
            new(status, code, title, detail);
    }
}
