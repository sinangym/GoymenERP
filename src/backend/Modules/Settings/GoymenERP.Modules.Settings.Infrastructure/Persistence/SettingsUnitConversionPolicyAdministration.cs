using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// F-004 (BS-9/BS-13/BS-16/BS-17; URN-D7…D9): the company's unit conversion policy — the steel density, the coefficient and
/// document total decimals and rounding modes, the whole-unit rounding mode and the weighbridge tolerance with its boundary
/// and base — as a persistent, versioned Settings record behind <see cref="ISettingsUnitConversionPolicyProviderV1"/>.
/// <list type="bullet">
/// <item><b>Nothing is chosen here</b>: there is no compiled default and no baseline transfer. The decisions name example and
/// starting values (density 7.85 g/cm³, coefficient 3 decimals, document total 2 decimals, kg to count rounded down, tolerance
/// 1 %, <c>&lt;=</c>, theoretical kg) and make every one of them a Settings value, so the table stays empty until an
/// authorized person records the company's first version; until then every consumer read is refused
/// (<see cref="SettingsPrerequisiteCodesV1.UnitConversionPolicyNotFound"/>).</item>
/// <item><b>Authority</b>: <see cref="SettingsUnitConversionPolicyPermissionManifestV1.Manage"/>, company-wide, measured first
/// and re-measured fresh right before every save; granted to no role here (human gate).</item>
/// <item><b>Every command</b> (SAT-D68 precedent): one person, a mandatory reason (≤ 4000), one Idempotency-Key (the stored
/// terminal answer replays; another body under the key is 409), the expected version as decimal text, correlation/causation
/// and one audit event per real change (none for a no-op).</item>
/// <item><b>Dates</b> (URN-D4 precedent): a version takes effect on its business date (omitted = today, Europe/Istanbul); a
/// date before today is refused, so a past window never changes. Rows are insert-only; a later version on the same date
/// supersedes the earlier one from that date.</item>
/// </list>
/// </summary>
internal sealed class SettingsUnitConversionPolicyAdministration(
    SettingsDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorizationService authorization,
    IErpAuthorityRevalidationService revalidation,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider,
    ILogger<SettingsUnitConversionPolicyAdministration> logger) : ISettingsUnitConversionPolicyAdministrationV1
{
    private const int CommandExecutionSchemaVersion = 1;
    private const string Permission = SettingsUnitConversionPolicyPermissionManifestV1.Manage;
    private const string EntityType = "Settings.UnitConversionPolicy";
    private const string RecordCommandName = "settings.unit-conversion-policy.record.v1";
    private const string RecordedEvent = "settings.unit-conversion-policy.recorded.v1";
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<UnitConversionPolicyViewV1> GetAsync(CancellationToken cancellationToken = default)
    {
        var companyId = RequireContextCompany();
        await RequireAsync(companyId, cancellationToken);
        var rows = await ReadAsync(() => UnitConversionPolicyReads.LoadAsync(database, cancellationToken));
        await RequireStillAsync(companyId, cancellationToken);
        var today = SettingsBusinessDate.Today(timeProvider);
        var timeline = UnitConversionPolicyReads.Timeline(rows);
        return new UnitConversionPolicyViewV1(companyId, today, Text(SalesPolicyTimelineV1.LatestVersion(timeline)),
            timeline.Select(entry => View(entry, today)).ToArray());
    }

    public async Task<UnitConversionPolicyCommandResultV1> RecordAsync(RecordUnitConversionPolicyRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(companyId, cancellationToken);
        if (!TryParseVersion(request.ExpectedVersion, 0, out var expectedVersion))
        {
            return Unstored(Answers.Invalid("Beklenen sürüm (\"0\" veya pozitif tam sayı metni) zorunludur."), metadata);
        }

        if (!UnitConversionPolicyRulesV1.TryParseDensity(request.DensityGramsPerCubicCentimetre, out var density))
        {
            return Unstored(Answers.Invalid(
                $"Yoğunluk (g/cm³) sıfırdan büyük, en çok {UnitConversionPolicyInputLimitsV1.DensityMaximumScale.ToString(CultureInfo.InvariantCulture)} ondalık basamaklı sayı metni olmalıdır (ör. 7.85)."),
                metadata);
        }

        if (!UnitConversionPolicyRulesV1.IsRoundingDecimals(request.CoefficientDecimals)
            || !UnitConversionPolicyRulesV1.IsRoundingDecimals(request.DocumentTotalDecimals))
        {
            return Unstored(Answers.Invalid(
                $"Katsayı ve belge toplamı ondalık basamak sayıları 0 ile {UnitConversionPolicyInputLimitsV1.MaximumRoundingDecimals.ToString(CultureInfo.InvariantCulture)} arasında tam sayı olmalıdır."),
                metadata);
        }

        if (!UnitConversionPolicyRulesV1.TryParseRounding(request.CoefficientRounding, out _)
            || !UnitConversionPolicyRulesV1.TryParseRounding(request.DocumentTotalRounding, out _)
            || !UnitConversionPolicyRulesV1.TryParseRounding(request.WholeUnitRounding, out _))
        {
            return Unstored(Answers.Invalid(
                "Yuvarlama yönleri tanımlı listeden seçilmelidir: " + string.Join(", ", UnitConversionRoundingModesV1.All) + "."),
                metadata);
        }

        if (!UnitConversionPolicyRulesV1.TryParseCoefficientRounding(request.CoefficientRounding, out _))
        {
            return Unstored(Answers.Invalid(
                "Katsayı yuvarlaması karara bağlıdır (KLN-D88): yarım ve üstü sıfırdan uzağa yuvarlanır ("
                + UnitConversionRoundingModesV1.DecidedCoefficientRounding + "). Yalnız katsayı ondalık sayısı şirket ayarıdır."),
                metadata);
        }

        if (!UnitConversionPolicyRulesV1.TryParseTolerancePercent(request.WeighbridgeTolerancePercent, out var tolerance))
        {
            return Unstored(Answers.Invalid(
                $"Kantar toleransı 0 ile 100 arasında, en çok {UnitConversionPolicyInputLimitsV1.PercentMaximumScale.ToString(CultureInfo.InvariantCulture)} ondalık basamaklı yüzde metni olmalıdır."),
                metadata);
        }

        if (!UnitConversionPolicyRulesV1.TryParseBoundary(request.ToleranceBoundary, out _)
            || !UnitConversionPolicyRulesV1.TryParseBase(request.ToleranceBase, out _))
        {
            return Unstored(Answers.Invalid(
                "Tolerans sınırı (" + string.Join(", ", UnitConversionToleranceBoundariesV1.All) + ") ve tolerans tabanı ("
                + string.Join(", ", UnitConversionToleranceBasesV1.All) + ") tanımlı listeden seçilmelidir."),
                metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var coefficientDecimals = request.CoefficientDecimals!.Value;
        var documentTotalDecimals = request.DocumentTotalDecimals!.Value;
        var coefficientRounding = request.CoefficientRounding!;
        var documentTotalRounding = request.DocumentTotalRounding!;
        var wholeUnitRounding = request.WholeUnitRounding!;
        var toleranceBoundary = request.ToleranceBoundary!;
        var toleranceBase = request.ToleranceBase!;
        var subject = companyId.ToString("D");
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            ExpectedVersion = expectedVersion,
            DensityGramsPerCubicCentimetre = UnitConversionPolicyRulesV1.Format(density),
            CoefficientDecimals = coefficientDecimals,
            CoefficientRounding = coefficientRounding,
            DocumentTotalDecimals = documentTotalDecimals,
            DocumentTotalRounding = documentTotalRounding,
            WholeUnitRounding = wholeUnitRounding,
            WeighbridgeTolerancePercent = UnitConversionPolicyRulesV1.Format(tolerance),
            ToleranceBoundary = toleranceBoundary,
            ToleranceBase = toleranceBase,
            Reason = reason,
            request.EffectiveFrom,
        });
        if (await FindExecutionAsync(keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done(metadata, subject, Replay(existing, requestHash, metadata));
        }

        var rows = await ReadAsync(() => UnitConversionPolicyReads.LoadAsync(database, cancellationToken));
        var timeline = UnitConversionPolicyReads.Timeline(rows);
        var latest = SalesPolicyTimelineV1.LatestVersion(timeline);
        var today = SettingsBusinessDate.Today(timeProvider);
        var effectiveFrom = request.EffectiveFrom ?? today;
        var refusal = latest != expectedVersion ? Answers.VersionConflict()
            : effectiveFrom < today ? Answers.EffectiveFromPast()
            : null;
        if (refusal is not null)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done(metadata, subject,
                await StoreRefusalAsync(refusal, keyHash, requestHash, metadata, cancellationToken));
        }

        var inForce = SalesPolicyTimelineV1.InForceOn(timeline, effectiveFrom);
        if (inForce is { Value: var current }
            && current.DensityGramsPerCubicCentimetre == density
            && current.CoefficientDecimals == coefficientDecimals
            && string.Equals(current.CoefficientRounding, coefficientRounding, StringComparison.Ordinal)
            && current.DocumentTotalDecimals == documentTotalDecimals
            && string.Equals(current.DocumentTotalRounding, documentTotalRounding, StringComparison.Ordinal)
            && string.Equals(current.WholeUnitRounding, wholeUnitRounding, StringComparison.Ordinal)
            && current.WeighbridgeTolerancePercent == tolerance
            && string.Equals(current.ToleranceBoundary, toleranceBoundary, StringComparison.Ordinal)
            && string.Equals(current.ToleranceBase, toleranceBase, StringComparison.Ordinal))
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done(metadata, subject,
                await StoreRefusalAsync(Answers.NoContent(), keyHash, requestHash, metadata, cancellationToken));
        }

        if (SettingsBusinessDate.Today(timeProvider) != today)
        {
            return Done(metadata, subject, Unstored(Answers.VersionConflict(), metadata));
        }

        var version = checked(latest + 1);
        var row = new UnitConversionPolicyVersionRow
        {
            CompanyId = companyId,
            Version = version,
            ProfileId = Guid.NewGuid(),
            DensityGramsPerCubicCentimetre = density,
            CoefficientDecimals = coefficientDecimals,
            CoefficientRounding = coefficientRounding,
            DocumentTotalDecimals = documentTotalDecimals,
            DocumentTotalRounding = documentTotalRounding,
            WholeUnitRounding = wholeUnitRounding,
            WeighbridgeTolerancePercent = tolerance,
            ToleranceBoundary = toleranceBoundary,
            ToleranceBase = toleranceBase,
            EffectiveFrom = effectiveFrom,
            RecordedAtUtc = timeProvider.GetUtcNow(),
            RecordedBy = metadata.ActorId,
            Reason = reason,
        };
        database.UnitConversionPolicyVersions.Add(row);
        Audit(companyId, subject, latest == 0 ? null : latest, version, scope, new
        {
            CompanyId = companyId,
            row.ProfileId,
            row.Version,
            DensityGramsPerCubicCentimetre = UnitConversionPolicyRulesV1.Format(density),
            row.CoefficientDecimals,
            row.CoefficientRounding,
            row.DocumentTotalDecimals,
            row.DocumentTotalRounding,
            row.WholeUnitRounding,
            WeighbridgeTolerancePercent = UnitConversionPolicyRulesV1.Format(tolerance),
            row.ToleranceBoundary,
            row.ToleranceBase,
            row.EffectiveFrom,
            SupersedesVersion = timeline
                .Where(entry => !entry.IsSuperseded && entry.EffectiveFrom == effectiveFrom)
                .Select(entry => (long?)entry.Version)
                .SingleOrDefault(),
        }, reason, metadata);
        await RequireStillAsync(companyId, cancellationToken);
        return Done(metadata, subject, await SaveChangeAsync(Answers.NoContent(), keyHash, requestHash, metadata,
            Answers.VersionConflict(), cancellationToken));
    }

    private static UnitConversionPolicyVersionViewV1 View(
        SalesPolicyTimelineEntryV1<UnitConversionPolicyVersionRow> entry, DateOnly today) =>
        new UnitConversionPolicyVersionViewV1(
            entry.Value.ProfileId,
            Text(entry.Version),
            UnitConversionPolicyRulesV1.Format(entry.Value.DensityGramsPerCubicCentimetre),
            entry.Value.CoefficientDecimals,
            entry.Value.CoefficientRounding,
            entry.Value.DocumentTotalDecimals,
            entry.Value.DocumentTotalRounding,
            entry.Value.WholeUnitRounding,
            UnitConversionPolicyRulesV1.Format(entry.Value.WeighbridgeTolerancePercent),
            entry.Value.ToleranceBoundary,
            entry.Value.ToleranceBase,
            entry.EffectiveFrom,
            entry.EffectiveThrough,
            SalesPolicyTimelineV1.State(entry, today),
            entry.Value.RecordedBy,
            entry.Value.RecordedAtUtc,
            entry.Value.Reason);

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

    /// <summary>A read of the store; an unreadable store is <c>settings_provider_unavailable</c> (503), never a 500.</summary>
    private static async Task<T> ReadAsync<T>(Func<Task<T>> read)
    {
        try
        {
            return await read();
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException(UnitConversionPolicyProblemCodesV1.ProviderUnavailable);
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

    private async Task<CommandExecution?> FindExecutionAsync(string keyHash, CancellationToken cancellationToken)
    {
        try
        {
            return await database.CommandExecutions.AsNoTracking()
                .SingleOrDefaultAsync(execution => execution.CommandName == RecordCommandName && execution.IdempotencyKeyHash == keyHash,
                    cancellationToken);
        }
        catch (SqlException)
        {
            // Covers both the initial lookup and reads inside a save-race catch.
            throw new SettingsPrerequisiteException(UnitConversionPolicyProblemCodesV1.ProviderUnavailable);
        }
    }

    private static UnitConversionPolicyCommandResultV1 Replay(CommandExecution execution, string requestHash,
        SettingsCommandMetadataV1 metadata)
    {
        if (!string.Equals(execution.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(execution.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new UnitConversionPolicyCommandResultV1(Answers.Problem(409, UnitConversionPolicyProblemCodesV1.IdempotencyConflict,
                    "İşlem anahtarı kullanılmış.", "Bu Idempotency-Key başka bir istekle kullanıldı."),
                false, metadata.CorrelationId);
        }

        var answer = JsonSerializer.Deserialize<UnitConversionPolicyCommandAnswerV1>(execution.ResponseJson, Json);
        if (answer is null || answer.StatusCode != execution.HttpStatusCode
            || !string.Equals(Outcome(answer), execution.Outcome, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Kalıcı idempotency sonucu tanımlı birim çevrim politikası komut sözleşmesiyle uyumsuz.");
        }

        return new UnitConversionPolicyCommandResultV1(answer, true, execution.OriginalCorrelationId);
    }

    private void AddExecution(UnitConversionPolicyCommandAnswerV1 answer, string keyHash, string requestHash,
        SettingsCommandMetadataV1 metadata) =>
        database.CommandExecutions.Add(CommandExecution.Complete(new CommandExecutionData(
            Guid.NewGuid(),
            metadata.CompanyId,
            RecordCommandName,
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
    private async Task<UnitConversionPolicyCommandResultV1> StoreRefusalAsync(UnitConversionPolicyCommandAnswerV1 answer,
        string keyHash, string requestHash, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        AddExecution(answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new UnitConversionPolicyCommandResultV1(answer, false, metadata.CorrelationId);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.GetBaseException() is SqlException { Number: 2601 or 2627 })
        {
            database.ChangeTracker.Clear();
            var winner = await FindExecutionAsync(keyHash, cancellationToken);
            if (winner is null)
            {
                throw new SettingsPrerequisiteException(UnitConversionPolicyProblemCodesV1.ProviderUnavailable);
            }

            return Replay(winner, requestHash, metadata);
        }
        catch (Exception exception) when (exception is DbUpdateException or SqlException)
        {
            // A provider failure is not a terminal business refusal and never consumes the retry key.
            database.ChangeTracker.Clear();
            throw new SettingsPrerequisiteException(UnitConversionPolicyProblemCodesV1.ProviderUnavailable);
        }
    }

    /// <summary>
    /// Saves the new version with its terminal answer and audit in one unit. A lost race (the version primary key) re-reads:
    /// a same-key winner replays, otherwise <paramref name="onConflict"/> is stored as the key's answer.
    /// </summary>
    private async Task<UnitConversionPolicyCommandResultV1> SaveChangeAsync(UnitConversionPolicyCommandAnswerV1 answer,
        string keyHash, string requestHash, SettingsCommandMetadataV1 metadata, UnitConversionPolicyCommandAnswerV1 onConflict,
        CancellationToken cancellationToken)
    {
        AddExecution(answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new UnitConversionPolicyCommandResultV1(answer, false, metadata.CorrelationId);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.GetBaseException() is SqlException { Number: 2601 or 2627 })
        {
            database.ChangeTracker.Clear();
            var winner = await FindExecutionAsync(keyHash, cancellationToken);
            if (winner is not null)
            {
                return Replay(winner, requestHash, metadata);
            }

            return await StoreRefusalAsync(onConflict, keyHash, requestHash, metadata, cancellationToken);
        }
        catch (Exception exception) when (exception is DbUpdateException or SqlException)
        {
            // A provider failure is not a terminal business refusal and never consumes the retry key.
            database.ChangeTracker.Clear();
            throw new SettingsPrerequisiteException(UnitConversionPolicyProblemCodesV1.ProviderUnavailable);
        }
    }

    private static string Outcome(UnitConversionPolicyCommandAnswerV1 answer) => answer.Code ?? answer.StatusCode switch
    {
        204 => "Succeeded",
        _ => "Refused",
    };

    /// <summary>
    /// One audit event: the person's whole reason in the after-image (<c>reason</c>), a fixed description in the audit row's
    /// reason column (the SAT-D68 / discount limit precedent), so nothing is truncated.
    /// </summary>
    private void Audit(Guid companyId, string entityId, long? versionBefore, long? versionAfter, PermissionScope scope,
        object after, string reason, SettingsCommandMetadataV1 metadata) =>
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            RecordedEvent,
            EntityType,
            entityId,
            "RecordVersion",
            versionBefore,
            versionAfter,
            AuditActorTypes.User,
            metadata.ActorId,
            Permission,
            scope.RoleCodes,
            "{}",
            AfterJson(after, reason),
            "Birim çevrim politikası sürümü kaydedildi; gerekçe kayıt içeriğindedir",
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

    private UnitConversionPolicyCommandResultV1 Done(SettingsCommandMetadataV1 metadata, string subject,
        UnitConversionPolicyCommandResultV1 result)
    {
        logger.LogInformation(
            "Settings unit conversion policy command completed. Command={Command} CompanyId={CompanyId} Subject={Subject} ActorId={ActorId} CorrelationId={CorrelationId} Status={Status} Code={Code} IsReplay={IsReplay}",
            "record", metadata.CompanyId, subject, metadata.ActorId, metadata.CorrelationId, result.Answer.StatusCode,
            result.Answer.Code, result.IsReplay);
        return result;
    }

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A version as decimal text: digits only, no sign, no leading zero (other than "0"), within Int64.</summary>
    private static bool TryParseVersion(string? text, long minimum, out long version)
    {
        version = 0;
        return !string.IsNullOrEmpty(text) && text.All(char.IsAsciiDigit) && (text.Length == 1 || text[0] != '0')
            && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out version) && version >= minimum;
    }

    private static UnitConversionPolicyCommandAnswerV1? ReasonRefusal(string? reason, out string normalized)
    {
        normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return Answers.Problem(400, UnitConversionPolicyProblemCodesV1.ReasonRequired, "Gerekçe gerekli.",
                "Bu değişiklik için gerekçe zorunludur.");
        }

        return normalized.Length > UnitConversionPolicyInputLimitsV1.ReasonMaximumLength
            ? Answers.Problem(400, UnitConversionPolicyProblemCodesV1.ReasonTooLong, "Gerekçe çok uzun.",
                $"Gerekçe en fazla {UnitConversionPolicyInputLimitsV1.ReasonMaximumLength.ToString(CultureInfo.InvariantCulture)} karakter olabilir.")
            : null;
    }

    private static UnitConversionPolicyCommandResultV1 Unstored(UnitConversionPolicyCommandAnswerV1 answer,
        SettingsCommandMetadataV1 metadata) =>
        new UnitConversionPolicyCommandResultV1(answer, false, metadata.CorrelationId);

    private static class Answers
    {
        public static UnitConversionPolicyCommandAnswerV1 NoContent() =>
            new UnitConversionPolicyCommandAnswerV1(204, null, "Kaydedildi.", "Değişiklik kaydedildi.");

        public static UnitConversionPolicyCommandAnswerV1 Invalid(string detail) =>
            Problem(400, UnitConversionPolicyProblemCodesV1.RequestInvalid, "İstek geçersiz.", detail);

        public static UnitConversionPolicyCommandAnswerV1 VersionConflict() =>
            Problem(409, UnitConversionPolicyProblemCodesV1.VersionConflict, "Kayıt değişmiş.",
                "Kayıt siz açtıktan sonra değişti; güncel hâlini okuyup yeniden deneyin.");

        public static UnitConversionPolicyCommandAnswerV1 EffectiveFromPast() =>
            Problem(422, UnitConversionPolicyProblemCodesV1.EffectiveFromPast, "Geçmiş tarih.",
                "Birim çevrim politikası sürümü bugünden önceki bir tarihten başlatılamaz.");

        public static UnitConversionPolicyCommandAnswerV1 Problem(int status, string code, string title, string detail) =>
            new UnitConversionPolicyCommandAnswerV1(status, code, title, detail);
    }
}
