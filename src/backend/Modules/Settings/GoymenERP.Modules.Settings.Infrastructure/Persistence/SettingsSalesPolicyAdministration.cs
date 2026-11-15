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
/// F-029 S4b (URUN_F029 spec §5.4(1); URN-D2…D5): the Sales delivery tolerance (SAT-D5) and order-approval amount threshold
/// (SAT-D6) as persistent, versioned Settings records behind the unchanged <see cref="ISettingsSalesPolicyProviderV1"/>.
/// <list type="bullet">
/// <item><b>Nothing is chosen here</b>: the only values that ever enter the table without a person are the reviewed compiled
/// catalog entries, copied unchanged (same profile id, version, values, active flag and window) by the one-time, explicit,
/// audited baseline transfer ("sürüm 0" source, <see cref="SalesPolicySourceKindsV1.CompiledBaselineV0"/>). Until a family is
/// transferred the catalog answers for it, so the consumer's answer does not change by deploying this slice.</item>
/// <item><b>Authority</b>: one name per family (<see cref="SettingsSalesPolicyPermissionManifestV1"/>), company-wide, measured
/// first and re-measured fresh right before every save; granted to no role here (human gate).</item>
/// <item><b>Every command</b> (SAT-D68 precedent): one person, a mandatory reason (≤ 4000), one Idempotency-Key (the stored
/// terminal answer replays; another body under the key is 409), the version as decimal text, correlation/causation and one
/// audit event per real change (none for a no-op).</item>
/// <item><b>Dates</b> (URN-D4): a version takes effect on its business date (omitted = today, Europe/Istanbul); a date before
/// today is refused, so a past window never changes. Rows are insert-only; a later version on the same date supersedes the
/// earlier one from that date.</item>
/// </list>
/// </summary>
internal sealed partial class SettingsSalesPolicyAdministration(
    SettingsDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorizationService authorization,
    IErpAuthorityRevalidationService revalidation,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider,
    ILogger<SettingsSalesPolicyAdministration> logger) : ISettingsSalesPolicyAdministrationV1
{
    private const int CommandExecutionSchemaVersion = 1;
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    /// <summary>The reviewed compiled catalog the baseline comes from (Settings' own tests build another one).</summary>
    internal ApprovedSalesPolicyCatalog Catalog { get; init; } = ApprovedSalesPolicyCatalog.Approved;

    /// <summary>The money profiles a threshold currency and its amount scale are checked against.</summary>
    internal ApprovedMoneyProfileCatalog MoneyProfiles { get; init; } = ApprovedMoneyProfileCatalog.Approved;

    private Guid RequireContextCompany()
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId)
            || !companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw new UnauthorizedAccessException();
        }

        return companyContext.CompanyId;
    }

    private async Task<PermissionScope> RequireAsync(string permission, Guid companyId, CancellationToken cancellationToken)
    {
        if (!currentUser.CanAccessCompany(companyId))
        {
            throw new UnauthorizedAccessException();
        }

        var scope = await authorization.GetPermissionScopeAsync(permission, companyId, cancellationToken);
        return scope is { IsGranted: true, IsCompanyWide: true } ? scope : throw new UnauthorizedAccessException();
    }

    /// <summary>The same measurement, fresh from the authority rows (a grant revoked meanwhile is seen).</summary>
    private async Task RequireStillAsync(string permission, Guid companyId, CancellationToken cancellationToken)
    {
        if (!currentUser.CanAccessCompany(companyId))
        {
            throw new UnauthorizedAccessException();
        }

        var scope = await revalidation.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
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
            throw new SettingsPrerequisiteException(SalesPolicyProblemCodesV1.ProviderUnavailable);
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

    private static SalesPolicyCommandResultV1 Replay(CommandExecution execution, string requestHash, SettingsCommandMetadataV1 metadata)
    {
        if (!string.Equals(execution.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(execution.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new SalesPolicyCommandResultV1(Answers.Problem(409, SalesPolicyProblemCodesV1.IdempotencyConflict,
                    "İşlem anahtarı kullanılmış.", "Bu Idempotency-Key başka bir istekle kullanıldı."),
                false, metadata.CorrelationId);
        }

        var answer = JsonSerializer.Deserialize<SalesPolicyCommandAnswerV1>(execution.ResponseJson, Json);
        if (answer is null || answer.StatusCode != execution.HttpStatusCode
            || !string.Equals(Outcome(answer), execution.Outcome, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Kalıcı idempotency sonucu tanımlı satış politikası komut sözleşmesiyle uyumsuz.");
        }

        return new SalesPolicyCommandResultV1(answer, true, execution.OriginalCorrelationId);
    }

    private void AddExecution(string commandName, SalesPolicyCommandAnswerV1 answer, string keyHash, string requestHash,
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
    private async Task<SalesPolicyCommandResultV1> StoreRefusalAsync(string commandName, SalesPolicyCommandAnswerV1 answer,
        string keyHash, string requestHash, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        AddExecution(commandName, answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new SalesPolicyCommandResultV1(answer, false, metadata.CorrelationId);
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
    /// Saves a business change with its terminal answer and audit in one unit. A lost race (the version or marker primary
    /// key) re-reads: a same-key winner replays, otherwise <paramref name="onConflict"/> gives the refusal stored as the key's
    /// answer.
    /// </summary>
    private async Task<SalesPolicyCommandResultV1> SaveChangeAsync(string commandName, SalesPolicyCommandAnswerV1 answer,
        string keyHash, string requestHash, SettingsCommandMetadataV1 metadata, SalesPolicyCommandAnswerV1 onConflict,
        CancellationToken cancellationToken)
    {
        AddExecution(commandName, answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new SalesPolicyCommandResultV1(answer, false, metadata.CorrelationId);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            var winner = await FindExecutionAsync(commandName, keyHash, cancellationToken);
            if (winner is not null)
            {
                return Replay(winner, requestHash, metadata);
            }

            return await StoreRefusalAsync(commandName, onConflict, keyHash, requestHash, metadata, cancellationToken);
        }
    }

    private static string Outcome(SalesPolicyCommandAnswerV1 answer) => answer.Code ?? answer.StatusCode switch
    {
        204 => "Succeeded",
        404 => "NotFound",
        _ => "Refused",
    };

    /// <summary>
    /// One audit event: the person's whole reason in the after-image (<c>reason</c>), the fixed description in the audit
    /// row's reason column (the SAT-D68 / discount limit precedent), so nothing is truncated.
    /// </summary>
    private void Audit(string permission, Guid companyId, string eventType, string entityType, string entityId, string operation,
        long? versionBefore, long? versionAfter, PermissionScope scope, object after, string reason,
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
            permission,
            scope.RoleCodes,
            "{}",
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
        ToleranceBaselineEvent =>
            "Sevk toleransı onaylı katalog değerleri Ayarlar tablosuna aktarıldı; gerekçe kayıt içeriğindedir",
        ToleranceRecordedEvent => "Sevk toleransı sürümü kaydedildi; gerekçe kayıt içeriğindedir",
        ThresholdBaselineEvent =>
            "Sipariş onay tutar eşiği onaylı katalog değerleri Ayarlar tablosuna aktarıldı; gerekçe kayıt içeriğindedir",
        ThresholdRecordedEvent => "Sipariş onay tutar eşiği sürümü kaydedildi; gerekçe kayıt içeriğindedir",
        QuoteBaselineEvent =>
            "Teklif politikası onaylı katalog değerleri Ayarlar tablosuna aktarıldı; gerekçe kayıt içeriğindedir",
        QuoteLossReasonsRecordedEvent => "Teklif kayıp nedeni listesi sürümü kaydedildi; gerekçe kayıt içeriğindedir",
        QuoteValidityRecordedEvent => "Varsayılan teklif geçerlilik süresi sürümü kaydedildi; gerekçe kayıt içeriğindedir",
        ScenarioBaselineEvent =>
            "e-Fatura senaryo politikası onaylı katalog değerleri Ayarlar tablosuna aktarıldı; gerekçe kayıt içeriğindedir",
        ScenarioCodeListRecordedEvent => "e-Fatura senaryo kodu listesi sürümü kaydedildi; gerekçe kayıt içeriğindedir",
        ScenarioDefaultRecordedEvent => "Varsayılan e-Fatura senaryosu sürümü kaydedildi; gerekçe kayıt içeriğindedir",
        _ => throw new InvalidOperationException("Tanımsız Ayarlar audit olayı."),
    };

    private SalesPolicyCommandResultV1 Done(string command, SettingsCommandMetadataV1 metadata, string subject,
        SalesPolicyCommandResultV1 result)
    {
        logger.LogInformation(
            "Settings sales policy command completed. Command={Command} CompanyId={CompanyId} Subject={Subject} ActorId={ActorId} CorrelationId={CorrelationId} Status={Status} Code={Code} IsReplay={IsReplay}",
            command, metadata.CompanyId, subject, metadata.ActorId, metadata.CorrelationId, result.Answer.StatusCode,
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

    private static SalesPolicyCommandAnswerV1? ReasonRefusal(string? reason, out string normalized)
    {
        normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return Answers.Problem(400, SalesPolicyProblemCodesV1.ReasonRequired, "Gerekçe gerekli.",
                "Bu değişiklik için gerekçe zorunludur.");
        }

        return normalized.Length > SalesPolicyInputLimitsV1.ReasonMaximumLength
            ? Answers.Problem(400, SalesPolicyProblemCodesV1.ReasonTooLong, "Gerekçe çok uzun.",
                $"Gerekçe en fazla {SalesPolicyInputLimitsV1.ReasonMaximumLength} karakter olabilir.")
            : null;
    }

    /// <summary>
    /// The catalog windows of one family key must chain (each ends the day before the next starts, the last is open), so that
    /// copying only the start dates gives the very same windows from the table. Anything else is a malformed source.
    /// </summary>
    private static void RequireChainedWindows<T>(IReadOnlyList<T> ordered, Func<T, DateOnly> from, Func<T, DateOnly?> through,
        Func<T, string> source)
    {
        for (var index = 0; index < ordered.Count; index++)
        {
            var expected = index + 1 < ordered.Count ? from(ordered[index + 1]).AddDays(-1) : (DateOnly?)null;
            if (through(ordered[index]) != expected
                || source(ordered[index]).Length > SalesPolicyInputLimitsV1.DecisionReferenceMaximumLength)
            {
                throw new SettingsPrerequisiteException("settings_profile_invalid");
            }
        }
    }

    private static SalesPolicyCommandResultV1 Unstored(SalesPolicyCommandAnswerV1 answer, SettingsCommandMetadataV1 metadata) =>
        new SalesPolicyCommandResultV1(answer, false, metadata.CorrelationId);

    private static class Answers
    {
        public static SalesPolicyCommandAnswerV1 NoContent() =>
            new SalesPolicyCommandAnswerV1(204, null, "Kaydedildi.", "Değişiklik kaydedildi.");

        public static SalesPolicyCommandAnswerV1 Invalid(string detail) =>
            Problem(400, SalesPolicyProblemCodesV1.RequestInvalid, "İstek geçersiz.", detail);

        public static SalesPolicyCommandAnswerV1 VersionConflict() =>
            Problem(409, SalesPolicyProblemCodesV1.VersionConflict, "Kayıt değişmiş.",
                "Kayıt siz açtıktan sonra değişti; güncel hâlini okuyup yeniden deneyin.");

        public static SalesPolicyCommandAnswerV1 EffectiveFromPast() =>
            Problem(422, SalesPolicyProblemCodesV1.EffectiveFromPast, "Geçmiş tarih.",
                "Politika sürümü bugünden önceki bir tarihten başlatılamaz.");

        public static SalesPolicyCommandAnswerV1 BaselineRequired() =>
            Problem(409, SalesPolicyProblemCodesV1.BaselineRequired, "Başlangıç aktarımı gerekli.",
                "Yeni sürüm girmeden önce onaylı katalog değerlerini Ayarlar tablosuna aktarın.");

        public static SalesPolicyCommandAnswerV1 AlreadyTransferred() =>
            Problem(409, SalesPolicyProblemCodesV1.AlreadyTransferred, "Aktarım yapılmış.",
                "Onaylı katalog değerleri bu şirket için daha önce aktarıldı; aktarım tek seferliktir.");

        public static SalesPolicyCommandAnswerV1 CurrencyNotServed() =>
            Problem(422, SalesPolicyProblemCodesV1.CurrencyNotServed, "Para birimi kullanılamaz.",
                "Bu para birimi için başlangıç tarihini kapsayan etkin bir para profili yok.");

        public static SalesPolicyCommandAnswerV1 AmountScaleExceeded(int scale) =>
            Problem(422, SalesPolicyProblemCodesV1.AmountScaleExceeded, "Fazla ondalık basamak.",
                $"Tutar bu para biriminde en fazla {scale.ToString(CultureInfo.InvariantCulture)} ondalık basamak taşıyabilir.");

        public static SalesPolicyCommandAnswerV1 Problem(int status, string code, string title, string detail) =>
            new SalesPolicyCommandAnswerV1(status, code, title, detail);
    }
}
