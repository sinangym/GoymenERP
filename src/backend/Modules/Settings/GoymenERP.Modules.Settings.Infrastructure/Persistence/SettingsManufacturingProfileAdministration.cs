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
/// API-01/02 (Üretim U1-a; URT-D6, D7, D9, D10; DEC-SET-001): the write path of the Manufacturing parameter profiles and of the
/// Manufacturing tolerance registry, on the D-MFG-PROFILE-AUTH-01=A lifecycle (<see cref="ManufacturingToleranceProfileLifecyclePolicyV1"/>:
/// prepare → approve → publish; the approver and the publisher are not the preparer).
/// <list type="bullet">
/// <item><b>Authority</b>: each action its own name (<see cref="ManufacturingParameterProfileActionsV1"/>,
/// <see cref="ManufacturingToleranceProfileActionsV1"/>, <see cref="ManufacturingToleranceProfileReadActionV1"/>), company-wide,
/// measured first and re-measured fresh right before every save; granted to no role here.</item>
/// <item><b>Every command</b>: a mandatory reason (≤ 500, the lifecycle's bound), one Idempotency-Key (the stored terminal
/// answer replays; another body under the key is 409), the expected version as decimal text, correlation/causation and one
/// audit event, saved in the same transaction as the change and the command answer.</item>
/// <item><b>Dates</b>: a version may not take effect before today (Europe/Istanbul), at preparation nor at publication; nothing
/// is changed retroactively. Published rows never change (DB triggers).</item>
/// </list>
/// </summary>
internal sealed partial class SettingsManufacturingProfileAdministration(
    SettingsDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorizationService authorization,
    IErpAuthorityRevalidationService revalidation,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider,
    ILogger<SettingsManufacturingProfileAdministration> logger)
    : ISettingsManufacturingParameterProfileAdministrationV1, ISettingsManufacturingToleranceProfileAdministrationV1
{
    private const int CommandExecutionSchemaVersion = 1;
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

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

    /// <summary>A read's store failure is <c>settings_provider_unavailable</c> (503), never a 500.</summary>
    private static async Task<T> ReadAsync<T>(Func<Task<T>> read)
    {
        try
        {
            return await read();
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not UnauthorizedAccessException)
        {
            throw new SettingsPrerequisiteException(ManufacturingProfileProblemCodesV1.ProviderUnavailable);
        }
    }

    private Task<CommandExecution?> FindExecutionAsync(string commandName, string keyHash, CancellationToken cancellationToken) =>
        ReadAsync(() => database.CommandExecutions.AsNoTracking()
            .SingleOrDefaultAsync(execution => execution.CommandName == commandName && execution.IdempotencyKeyHash == keyHash,
                cancellationToken));

    private static ManufacturingProfileCommandResultV1 Replay(CommandExecution execution, string requestHash,
        SettingsCommandMetadataV1 metadata)
    {
        if (!string.Equals(execution.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(execution.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new ManufacturingProfileCommandResultV1(Answers.Problem(409,
                    ManufacturingProfileProblemCodesV1.IdempotencyConflict, "İşlem anahtarı kullanılmış.",
                    "Bu Idempotency-Key başka bir istekle kullanıldı."),
                false, metadata.CorrelationId);
        }

        var answer = JsonSerializer.Deserialize<ManufacturingProfileCommandAnswerV1>(execution.ResponseJson, Json);
        if (answer is null || answer.StatusCode != execution.HttpStatusCode
            || !string.Equals(Outcome(answer), execution.Outcome, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Kalıcı idempotency sonucu üretim profili komut sözleşmesiyle uyumsuz.");
        }

        return new ManufacturingProfileCommandResultV1(answer, true, execution.OriginalCorrelationId);
    }

    private void AddExecution(string commandName, ManufacturingProfileCommandAnswerV1 answer, string keyHash,
        string requestHash, SettingsCommandMetadataV1 metadata) =>
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

    /// <summary>Stores a refusal (no business change) as the key's terminal answer; a same-key winner replays.</summary>
    private async Task<ManufacturingProfileCommandResultV1> StoreRefusalAsync(string commandName,
        ManufacturingProfileCommandAnswerV1 answer, string keyHash, string requestHash, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        AddExecution(commandName, answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new ManufacturingProfileCommandResultV1(answer, false, metadata.CorrelationId);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            var winner = await FindExecutionAsync(commandName, keyHash, cancellationToken);
            if (winner is null)
            {
                throw new SettingsPrerequisiteException(ManufacturingProfileProblemCodesV1.ProviderUnavailable);
            }

            return Replay(winner, requestHash, metadata);
        }
    }

    /// <summary>
    /// Saves a business change with its terminal answer and audit in one unit. A lost race (state version, unique key, a
    /// registry trigger) re-reads: a same-key winner replays, otherwise <paramref name="onConflict"/> gives the refusal stored
    /// as the key's answer.
    /// </summary>
    private async Task<ManufacturingProfileCommandResultV1> SaveChangeAsync(string commandName,
        ManufacturingProfileCommandAnswerV1 answer, string keyHash, string requestHash, SettingsCommandMetadataV1 metadata,
        Func<CancellationToken, Task<ManufacturingProfileCommandAnswerV1>> onConflict, CancellationToken cancellationToken)
    {
        AddExecution(commandName, answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new ManufacturingProfileCommandResultV1(answer, false, metadata.CorrelationId);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            var winner = await FindExecutionAsync(commandName, keyHash, cancellationToken);
            if (winner is not null)
            {
                return Replay(winner, requestHash, metadata);
            }

            var refusal = await ReadAsync(() => onConflict(cancellationToken));
            return await StoreRefusalAsync(commandName, refusal, keyHash, requestHash, metadata, cancellationToken);
        }
    }

    private static string Outcome(ManufacturingProfileCommandAnswerV1 answer) => answer.Code ?? answer.StatusCode switch
    {
        200 => "Succeeded",
        404 => "NotFound",
        _ => "Refused",
    };

    /// <summary>One audit event: the person's whole reason in the after-image, the fixed description in the reason column.</summary>
    private void Audit(Guid companyId, string permission, string eventType, string entityType, Guid profileId,
        string operation, long? versionBefore, long versionAfter, PermissionScope scope, object? before, object after,
        string reason, SettingsCommandMetadataV1 metadata) =>
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            eventType,
            entityType,
            profileId.ToString("D", CultureInfo.InvariantCulture),
            operation,
            versionBefore,
            versionAfter,
            AuditActorTypes.User,
            metadata.ActorId,
            permission,
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
        ParameterPreparedEvent => "Üretim parametre profili taslağı hazırlandı; gerekçe kayıt içeriğindedir",
        ParameterApprovedEvent => "Üretim parametre profili onaylandı; gerekçe kayıt içeriğindedir",
        ParameterPublishedEvent => "Üretim parametre profili yayımlandı; gerekçe kayıt içeriğindedir",
        TolerancePreparedEvent => "Üretim tolerans profili taslağı hazırlandı; gerekçe kayıt içeriğindedir",
        ToleranceApprovedEvent => "Üretim tolerans profili onaylandı; gerekçe kayıt içeriğindedir",
        TolerancePublishedEvent => "Üretim tolerans profili yayımlandı; gerekçe kayıt içeriğindedir",
        _ => throw new InvalidOperationException("Tanımsız Ayarlar audit olayı."),
    };

    private ManufacturingProfileCommandResultV1 Done(string command, SettingsCommandMetadataV1 metadata, string subject,
        ManufacturingProfileCommandResultV1 result)
    {
        logger.LogInformation(
            "Settings manufacturing profile command completed. Command={Command} CompanyId={CompanyId} Subject={Subject} ActorId={ActorId} CorrelationId={CorrelationId} Status={Status} Code={Code} IsReplay={IsReplay}",
            command, metadata.CompanyId, subject, metadata.ActorId, metadata.CorrelationId, result.Answer.StatusCode,
            result.Answer.Code, result.IsReplay);
        return result;
    }

    private ManufacturingToleranceProfileCommandContextV1 LifecycleContext(SettingsCommandMetadataV1 metadata,
        long expectedStateVersion, string reason) =>
        new ManufacturingToleranceProfileCommandContextV1(metadata.CompanyId, metadata.ActorId, expectedStateVersion, metadata.IdempotencyKey, reason,
            timeProvider.GetUtcNow().ToUniversalTime());

    /// <summary>The lifecycle refusal as a stored answer (an invalid context is the caller's 400).</summary>
    private static ManufacturingProfileCommandAnswerV1 LifecycleRefusal(ManufacturingToleranceProfileLifecycleException exception) =>
        exception.Code switch
        {
            ManufacturingToleranceProfileLifecycleCodesV1.VersionConflict => Answers.VersionConflict(),
            ManufacturingToleranceProfileLifecycleCodesV1.InvalidTransition => Answers.Problem(409,
                ManufacturingProfileProblemCodesV1.TransitionInvalid, "Bu adım uygulanamaz.",
                "Profil bu adım için uygun durumda değil (taslak onaylanır, onaylı profil yayımlanır)."),
            ManufacturingToleranceProfileLifecycleCodesV1.MakerSeparationRequired => Answers.Problem(422,
                ManufacturingProfileProblemCodesV1.MakerSeparationRequired, "Hazırlayan onaylayamaz.",
                "Profili hazırlayan kişi onaylayamaz veya yayımlayamaz; başka bir yetkili gerekir."),
            _ => Answers.Invalid("Komut bilgileri profil yaşam döngüsü kurallarına uymuyor."),
        };

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A version as decimal text: digits only, no sign, no leading zero (other than "0"), within Int64.</summary>
    private static bool TryParseVersion(string? text, long minimum, out long version)
    {
        version = 0;
        return !string.IsNullOrEmpty(text) && text.All(char.IsAsciiDigit) && (text.Length == 1 || text[0] != '0')
            && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out version) && version >= minimum;
    }

    private static ManufacturingProfileCommandAnswerV1? ReasonRefusal(string? reason, out string normalized)
    {
        normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return Answers.Problem(400, ManufacturingProfileProblemCodesV1.ReasonRequired, "Gerekçe gerekli.",
                "Bu değişiklik için gerekçe zorunludur.");
        }

        if (normalized.Any(char.IsControl))
        {
            return Answers.Invalid("Gerekçe kontrol karakteri içeremez.");
        }

        return normalized.Length > ManufacturingProfileInputLimitsV1.ReasonMaximumLength
            ? Answers.Problem(400, ManufacturingProfileProblemCodesV1.ReasonTooLong, "Gerekçe çok uzun.",
                $"Gerekçe en fazla {ManufacturingProfileInputLimitsV1.ReasonMaximumLength} karakter olabilir.")
            : null;
    }

    private static ManufacturingProfileCommandResultV1 Unstored(ManufacturingProfileCommandAnswerV1 answer,
        SettingsCommandMetadataV1 metadata) => new ManufacturingProfileCommandResultV1(answer, false, metadata.CorrelationId);

    private static ManufacturingProfileCommandReceiptV1 Receipt(Guid profileId, long profileVersion, long stateVersion,
        string status) => new ManufacturingProfileCommandReceiptV1(profileId, Text(profileVersion), Text(stateVersion), status);

    private static class Answers
    {
        public static ManufacturingProfileCommandAnswerV1 Ok(ManufacturingProfileCommandReceiptV1 receipt) =>
            new ManufacturingProfileCommandAnswerV1(200, null, "Kaydedildi.", "Değişiklik kaydedildi.", receipt);

        public static ManufacturingProfileCommandAnswerV1 NotFound() => new ManufacturingProfileCommandAnswerV1(404, null, "Kayıt bulunamadı.",
            "İstenen üretim profili bu şirkette bulunamadı.", null);

        public static ManufacturingProfileCommandAnswerV1 Invalid(string detail) =>
            Problem(400, ManufacturingProfileProblemCodesV1.RequestInvalid, "İstek geçersiz.", detail);

        public static ManufacturingProfileCommandAnswerV1 VersionConflict() =>
            Problem(409, ManufacturingProfileProblemCodesV1.VersionConflict, "Kayıt değişmiş.",
                "Profil siz açtıktan sonra değişti; güncel hâlini okuyup yeniden deneyin.");

        public static ManufacturingProfileCommandAnswerV1 EffectiveFromPast() =>
            Problem(422, ManufacturingProfileProblemCodesV1.EffectiveFromPast, "Geçmiş tarih.",
                "Profil bugünden önceki bir tarihten başlatılamaz; yeni bir sürüm hazırlayın.");

        public static ManufacturingProfileCommandAnswerV1 WindowOverlap() =>
            Problem(409, ManufacturingProfileProblemCodesV1.WindowOverlap, "Geçerlilik aralığı çakışıyor.",
                "Bu prosesin yayımlanmış bir tolerans profili aynı tarihleri kapsıyor; yayımlanmış profil değiştirilemez.");

        public static ManufacturingProfileCommandAnswerV1 Problem(int status, string code, string title, string detail) =>
            new ManufacturingProfileCommandAnswerV1(status, code, title, detail, null);
    }
}
