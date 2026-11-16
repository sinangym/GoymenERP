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
/// URT-D89: the input capacity (kg/hour, URT-D87) and the optional due-date safety buffer (hours or days, URT-D92: the single
/// buffer is the per-work-center one) of every work center of a company, as a persistent, versioned Settings record behind
/// <see cref="ISettingsWorkCenterCapacityProviderV1"/>.
/// <list type="bullet">
/// <item><b>Nothing is chosen here</b>: nothing is seeded (URT-D91). A work center without a version has no capacity (the
/// due-date suggestion answers "hesaplanamadı"); a version without a buffer counts the buffer as zero.</item>
/// <item><b>Company-scoped</b>: every row carries the server-resolved company in its key and under the company query filter;
/// the work center must exist in that company, asked of the Organization owner through
/// <see cref="ISettingsWorkCenterReferenceV1"/> (no Organization table is read). No composed reference fails closed.</item>
/// <item><b>Authority</b>: <see cref="SettingsWorkCenterCapacityPermissionManifestV1.Manage"/>, company-wide, measured first
/// and re-measured fresh right before every save; granted to no role here (human gate). The decided persona (the production
/// manager) is a persona note only.</item>
/// <item><b>Every command</b> (SAT-D68 precedent): one person, a mandatory reason (≤ 4000), one Idempotency-Key (the stored
/// terminal answer replays; another body under the key is 409), the work center's expected version as decimal text,
/// correlation/causation and one audit event per real change (none for a no-op). A version takes effect when it is
/// saved.</item>
/// </list>
/// </summary>
internal sealed class SettingsWorkCenterCapacityAdministration(
    SettingsDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorizationService authorization,
    IErpAuthorityRevalidationService revalidation,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider,
    ILogger<SettingsWorkCenterCapacityAdministration> logger,
    ISettingsWorkCenterReferenceV1? workCenters = null) : ISettingsWorkCenterCapacityAdministrationV1
{
    private const int CommandExecutionSchemaVersion = 1;
    private const string Permission = SettingsWorkCenterCapacityPermissionManifestV1.Manage;
    private const string EntityType = "Settings.WorkCenterCapacity";
    private const string RecordCommandName = "settings.work-center-capacity.record.v1";
    private const string RecordedEvent = "settings.work-center-capacity.recorded.v1";
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<WorkCenterCapacityViewV1> GetAsync(CancellationToken cancellationToken = default)
    {
        var companyId = RequireContextCompany();
        await RequireAsync(companyId, cancellationToken);
        var rows = await ReadAsync(() => WorkCenterCapacityReads.LoadAsync(database, cancellationToken));
        await RequireStillAsync(companyId, cancellationToken);
        var timelines = rows
            .GroupBy(row => row.WorkCenterId)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var versions = group.OrderByDescending(row => row.Version).ToArray();
                var latest = versions[0].Version;
                return new WorkCenterCapacityTimelineViewV1(group.Key, Text(latest),
                    versions.Select(row => View(row, latest)).ToArray());
            })
            .ToArray();
        return new WorkCenterCapacityViewV1(WorkCenterCapacityKeysV1.InputCapacityUnit, timelines);
    }

    public async Task<WorkCenterCapacityCommandResultV1> RecordAsync(Guid workCenterId,
        RecordWorkCenterCapacityRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(companyId, cancellationToken);
        if (workCenterId == Guid.Empty)
        {
            return Unstored(Answers.Invalid("İş merkezi kimliği zorunludur."), metadata);
        }

        if (!TryParseVersion(request.ExpectedVersion, 0, out var expectedVersion))
        {
            return Unstored(Answers.Invalid("Beklenen sürüm (\"0\" veya pozitif tam sayı metni) zorunludur."), metadata);
        }

        if (!WorkCenterCapacityRulesV1.TryParseCapacity(request.InputCapacityKgPerHour, out var capacity))
        {
            return Unstored(Answers.Invalid(
                $"Giriş kapasitesi (kg/saat) sıfırdan büyük ondalık sayı metni olmalıdır (en fazla {Text(WorkCenterCapacityInputLimitsV1.CapacityMaximumIntegerDigits)} tam, {Text(WorkCenterCapacityInputLimitsV1.CapacityMaximumScale)} ondalık hane; ayraç nokta)."),
                metadata);
        }

        if (!WorkCenterCapacityRulesV1.TryParseSafetyBufferPair(request.SafetyBufferValue, request.SafetyBufferUnit,
                out var bufferValue, out var bufferUnit))
        {
            return Unstored(Answers.Invalid(
                $"Güvenlik payı değeri ve birimi birlikte verilmeli ya da birlikte boş bırakılmalıdır; değer sıfırdan büyük ondalık sayı metni (en fazla {Text(WorkCenterCapacityInputLimitsV1.SafetyBufferMaximumIntegerDigits)} tam, {Text(WorkCenterCapacityInputLimitsV1.SafetyBufferMaximumScale)} ondalık hane), birim tanımlı listeden olmalıdır: "
                + string.Join(", ", WorkCenterSafetyBufferUnitsV1.All) + "."),
                metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            WorkCenterId = workCenterId,
            ExpectedVersion = expectedVersion,
            InputCapacityKgPerHour = WorkCenterCapacityRulesV1.Format(capacity),
            SafetyBufferValue = bufferValue is { } value ? WorkCenterCapacityRulesV1.Format(value) : null,
            SafetyBufferUnit = bufferUnit,
            Reason = reason,
        });
        if (await FindExecutionAsync(keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done(metadata, workCenterId, Replay(existing, requestHash, metadata));
        }

        var reference = await FindWorkCenterAsync(companyId, workCenterId, cancellationToken);
        if (reference == SettingsWorkCenterReferenceOutcomeV1.NotFound)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done(metadata, workCenterId,
                await StoreRefusalAsync(Answers.WorkCenterNotFound(), keyHash, requestHash, metadata, cancellationToken));
        }

        var current = await ReadAsync(() => WorkCenterCapacityReads.LatestAsync(database, workCenterId, cancellationToken));
        var latest = current?.Version ?? 0;
        if (latest != expectedVersion)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done(metadata, workCenterId,
                await StoreRefusalAsync(Answers.VersionConflict(), keyHash, requestHash, metadata, cancellationToken));
        }

        if (current is not null
            && current.InputCapacityKgPerHour == capacity
            && current.SafetyBufferValue == bufferValue
            && string.Equals(current.SafetyBufferUnit, bufferUnit, StringComparison.Ordinal))
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done(metadata, workCenterId,
                await StoreRefusalAsync(Answers.NoContent(), keyHash, requestHash, metadata, cancellationToken));
        }

        var version = checked(latest + 1);
        var row = new WorkCenterCapacityVersionRow
        {
            CompanyId = companyId,
            WorkCenterId = workCenterId,
            Version = version,
            ProfileId = Guid.NewGuid(),
            InputCapacityKgPerHour = capacity,
            SafetyBufferValue = bufferValue,
            SafetyBufferUnit = bufferUnit,
            RecordedAtUtc = timeProvider.GetUtcNow(),
            RecordedBy = metadata.ActorId,
            Reason = reason,
        };
        database.WorkCenterCapacityVersions.Add(row);
        Audit(companyId, workCenterId, latest == 0 ? null : latest, version, scope,
            current is null ? "{}" : JsonSerializer.Serialize(new
            {
                current.ProfileId,
                current.WorkCenterId,
                current.Version,
                InputCapacityKgPerHour = WorkCenterCapacityRulesV1.Format(current.InputCapacityKgPerHour),
                InputCapacityUnit = WorkCenterCapacityKeysV1.InputCapacityUnit,
                SafetyBufferValue = current.SafetyBufferValue is { } previous
                    ? WorkCenterCapacityRulesV1.Format(previous)
                    : null,
                current.SafetyBufferUnit,
            }, Json),
            new
            {
                row.ProfileId,
                row.WorkCenterId,
                row.Version,
                InputCapacityKgPerHour = WorkCenterCapacityRulesV1.Format(row.InputCapacityKgPerHour),
                InputCapacityUnit = WorkCenterCapacityKeysV1.InputCapacityUnit,
                SafetyBufferValue = row.SafetyBufferValue is { } next ? WorkCenterCapacityRulesV1.Format(next) : null,
                row.SafetyBufferUnit,
            }, reason, metadata);
        await RequireStillAsync(companyId, cancellationToken);
        return Done(metadata, workCenterId, await SaveChangeAsync(Answers.NoContent(), keyHash, requestHash, metadata,
            Answers.VersionConflict(), cancellationToken));
    }

    private static WorkCenterCapacityVersionViewV1 View(WorkCenterCapacityVersionRow row, long latest) =>
        new WorkCenterCapacityVersionViewV1(
            row.ProfileId,
            Text(row.Version),
            WorkCenterCapacityRulesV1.Format(row.InputCapacityKgPerHour),
            row.SafetyBufferValue is { } buffer ? WorkCenterCapacityRulesV1.Format(buffer) : null,
            row.SafetyBufferUnit,
            row.Version == latest,
            row.RecordedBy,
            row.RecordedAtUtc,
            row.Reason);

    /// <summary>
    /// Asks the Organization owner (through the host adapter) whether the work center exists in the company. No composed
    /// adapter, an unavailable owner or any failure of the call fails closed with <c>settings_provider_unavailable</c> (503).
    /// </summary>
    private async Task<SettingsWorkCenterReferenceOutcomeV1> FindWorkCenterAsync(Guid companyId, Guid workCenterId,
        CancellationToken cancellationToken)
    {
        if (workCenters is null)
        {
            throw new SettingsPrerequisiteException(WorkCenterCapacityProblemCodesV1.ProviderUnavailable);
        }

        SettingsWorkCenterReferenceOutcomeV1 outcome;
        try
        {
            outcome = await workCenters.FindAsync(companyId, workCenterId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not UnauthorizedAccessException)
        {
            throw new SettingsPrerequisiteException(WorkCenterCapacityProblemCodesV1.ProviderUnavailable);
        }

        return outcome switch
        {
            SettingsWorkCenterReferenceOutcomeV1.Found or SettingsWorkCenterReferenceOutcomeV1.NotFound => outcome,
            _ => throw new SettingsPrerequisiteException(WorkCenterCapacityProblemCodesV1.ProviderUnavailable),
        };
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

    /// <summary>A read of the store; an unreadable store is <c>settings_provider_unavailable</c> (503), never a 500.</summary>
    private static async Task<T> ReadAsync<T>(Func<Task<T>> read)
    {
        try
        {
            return await read();
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException(WorkCenterCapacityProblemCodesV1.ProviderUnavailable);
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

    private Task<CommandExecution?> FindExecutionAsync(string keyHash, CancellationToken cancellationToken) =>
        database.CommandExecutions.AsNoTracking()
            .SingleOrDefaultAsync(execution => execution.CommandName == RecordCommandName && execution.IdempotencyKeyHash == keyHash,
                cancellationToken);

    private static WorkCenterCapacityCommandResultV1 Replay(CommandExecution execution, string requestHash,
        SettingsCommandMetadataV1 metadata)
    {
        if (!string.Equals(execution.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(execution.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new WorkCenterCapacityCommandResultV1(Answers.Problem(409,
                    WorkCenterCapacityProblemCodesV1.IdempotencyConflict,
                    "İşlem anahtarı kullanılmış.", "Bu Idempotency-Key başka bir istekle kullanıldı."),
                false, metadata.CorrelationId);
        }

        var answer = JsonSerializer.Deserialize<WorkCenterCapacityCommandAnswerV1>(execution.ResponseJson, Json);
        if (answer is null || answer.StatusCode != execution.HttpStatusCode
            || !string.Equals(Outcome(answer), execution.Outcome, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Kalıcı idempotency sonucu iş merkezi kapasitesi komut sözleşmesiyle uyumsuz.");
        }

        return new WorkCenterCapacityCommandResultV1(answer, true, execution.OriginalCorrelationId);
    }

    private void AddExecution(WorkCenterCapacityCommandAnswerV1 answer, string keyHash, string requestHash,
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
    private async Task<WorkCenterCapacityCommandResultV1> StoreRefusalAsync(WorkCenterCapacityCommandAnswerV1 answer,
        string keyHash, string requestHash, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        AddExecution(answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new WorkCenterCapacityCommandResultV1(answer, false, metadata.CorrelationId);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            var winner = await FindExecutionAsync(keyHash, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return Replay(winner, requestHash, metadata);
        }
    }

    /// <summary>
    /// Saves the new version with its terminal answer and audit in one unit. A lost race (the version primary key) re-reads:
    /// a same-key winner replays, otherwise <paramref name="onConflict"/> is stored as the key's answer.
    /// </summary>
    private async Task<WorkCenterCapacityCommandResultV1> SaveChangeAsync(WorkCenterCapacityCommandAnswerV1 answer,
        string keyHash, string requestHash, SettingsCommandMetadataV1 metadata, WorkCenterCapacityCommandAnswerV1 onConflict,
        CancellationToken cancellationToken)
    {
        AddExecution(answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new WorkCenterCapacityCommandResultV1(answer, false, metadata.CorrelationId);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            var winner = await FindExecutionAsync(keyHash, cancellationToken);
            if (winner is not null)
            {
                return Replay(winner, requestHash, metadata);
            }

            return await StoreRefusalAsync(onConflict, keyHash, requestHash, metadata, cancellationToken);
        }
    }

    private static string Outcome(WorkCenterCapacityCommandAnswerV1 answer) => answer.Code ?? answer.StatusCode switch
    {
        204 => "Succeeded",
        _ => "Refused",
    };

    /// <summary>
    /// One audit event under the company: the work center as the entity, the previous version in the before-image, the new
    /// version and the person's whole reason in the after-image (<c>reason</c>), a fixed description in the reason column.
    /// </summary>
    private void Audit(Guid companyId, Guid workCenterId, long? versionBefore, long? versionAfter, PermissionScope scope,
        string beforeJson, object after, string reason, SettingsCommandMetadataV1 metadata) =>
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            RecordedEvent,
            EntityType,
            workCenterId.ToString("D"),
            "RecordVersion",
            versionBefore,
            versionAfter,
            AuditActorTypes.User,
            metadata.ActorId,
            Permission,
            scope.RoleCodes,
            beforeJson,
            AfterJson(after, reason),
            "İş merkezi kapasitesi sürümü kaydedildi; gerekçe kayıt içeriğindedir",
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

    private WorkCenterCapacityCommandResultV1 Done(SettingsCommandMetadataV1 metadata, Guid workCenterId,
        WorkCenterCapacityCommandResultV1 result)
    {
        logger.LogInformation(
            "Settings work center capacity command completed. Command={Command} CompanyId={CompanyId} WorkCenterId={WorkCenterId} ActorId={ActorId} CorrelationId={CorrelationId} Status={Status} Code={Code} IsReplay={IsReplay}",
            "record", metadata.CompanyId, workCenterId, metadata.ActorId, metadata.CorrelationId,
            result.Answer.StatusCode, result.Answer.Code, result.IsReplay);
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

    private static WorkCenterCapacityCommandAnswerV1? ReasonRefusal(string? reason, out string normalized)
    {
        normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return Answers.Problem(400, WorkCenterCapacityProblemCodesV1.ReasonRequired, "Gerekçe gerekli.",
                "Bu değişiklik için gerekçe zorunludur.");
        }

        return normalized.Length > WorkCenterCapacityInputLimitsV1.ReasonMaximumLength
            ? Answers.Problem(400, WorkCenterCapacityProblemCodesV1.ReasonTooLong, "Gerekçe çok uzun.",
                $"Gerekçe en fazla {Text(WorkCenterCapacityInputLimitsV1.ReasonMaximumLength)} karakter olabilir.")
            : null;
    }

    private static WorkCenterCapacityCommandResultV1 Unstored(WorkCenterCapacityCommandAnswerV1 answer,
        SettingsCommandMetadataV1 metadata) =>
        new WorkCenterCapacityCommandResultV1(answer, false, metadata.CorrelationId);

    private static class Answers
    {
        public static WorkCenterCapacityCommandAnswerV1 NoContent() =>
            new WorkCenterCapacityCommandAnswerV1(204, null, "Kaydedildi.", "Değişiklik kaydedildi.");

        public static WorkCenterCapacityCommandAnswerV1 Invalid(string detail) =>
            Problem(400, WorkCenterCapacityProblemCodesV1.RequestInvalid, "İstek geçersiz.", detail);

        public static WorkCenterCapacityCommandAnswerV1 WorkCenterNotFound() =>
            Problem(404, WorkCenterCapacityProblemCodesV1.WorkCenterNotFound, "İş merkezi bulunamadı.",
                "Bu şirkette böyle bir iş merkezi yok.");

        public static WorkCenterCapacityCommandAnswerV1 VersionConflict() =>
            Problem(409, WorkCenterCapacityProblemCodesV1.VersionConflict, "Kayıt değişmiş.",
                "Kayıt siz açtıktan sonra değişti; güncel hâlini okuyup yeniden deneyin.");

        public static WorkCenterCapacityCommandAnswerV1 Problem(int status, string code, string title, string detail) =>
            new WorkCenterCapacityCommandAnswerV1(status, code, title, detail);
    }
}
