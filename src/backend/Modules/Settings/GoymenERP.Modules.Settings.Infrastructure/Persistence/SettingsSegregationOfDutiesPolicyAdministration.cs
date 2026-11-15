using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// F-024 (NS-01): the company's segregation of duties setting — whether the person who prepared a document may also approve
/// it — as a persistent, versioned Settings record behind <see cref="ISettingsSegregationOfDutiesPolicyProviderV1"/>.
/// <list type="bullet">
/// <item><b>The default is decided</b> (NS-01): the preparer may not approve; a different person is required. It is the
/// absence of a row, so nothing is transferred or seeded; recording <c>true</c> is the relaxation for a company run by one
/// person, and recording <c>false</c> again restores the default.</item>
/// <item><b>Authority</b>: <see cref="SettingsSegregationOfDutiesPolicyPermissionManifestV1.Manage"/>, company-wide, measured
/// first and re-measured fresh right before every save; granted to no role here (human gate).</item>
/// <item><b>Every command</b> (SAT-D68 precedent): a mandatory reason (≤ 4000), one Idempotency-Key (the stored terminal
/// answer replays; another body under the key is 409), the expected version as decimal text, correlation/causation and one
/// audit event per real change (none for a no-op).</item>
/// <item><b>Effect</b>: a version is in force from the moment it is saved; rows are insert-only, so the history of who allowed
/// what and why never changes.</item>
/// </list>
/// </summary>
internal sealed class SettingsSegregationOfDutiesPolicyAdministration(
    SettingsDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorizationService authorization,
    IErpAuthorityRevalidationService revalidation,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider,
    ILogger<SettingsSegregationOfDutiesPolicyAdministration> logger) : ISettingsSegregationOfDutiesPolicyAdministrationV1
{
    private const int CommandExecutionSchemaVersion = 1;
    private const string Permission = SettingsSegregationOfDutiesPolicyPermissionManifestV1.Manage;
    private const string EntityType = "Settings.SegregationOfDutiesPolicy";
    private const string RecordCommandName = "settings.segregation-of-duties.record.v1";
    private const string RecordedEvent = "settings.segregation-of-duties.recorded.v1";
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<SegregationOfDutiesPolicyViewV1> GetAsync(CancellationToken cancellationToken = default)
    {
        var companyId = RequireContextCompany();
        await RequireAsync(companyId, cancellationToken);
        var rows = await ReadAsync(() => SegregationOfDutiesPolicyReads.LoadAsync(database, cancellationToken));
        await RequireStillAsync(companyId, cancellationToken);
        var latest = rows.Length == 0 ? null : rows[0];
        return new SegregationOfDutiesPolicyViewV1(
            companyId,
            latest?.PreparerMayApprove ?? SegregationOfDutiesRuleV1.DefaultPreparerMayApprove,
            Text(latest?.Version ?? 0),
            rows.Select(View).ToArray());
    }

    public async Task<SegregationOfDutiesPolicyCommandResultV1> RecordAsync(RecordSegregationOfDutiesPolicyRequestV1 request,
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

        if (request.PreparerMayApprove is not { } preparerMayApprove)
        {
            return Unstored(Answers.Invalid("\"Hazırlayan onaylayabilir\" seçimi (evet/hayır) zorunludur."), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var subject = companyId.ToString("D");
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            ExpectedVersion = expectedVersion,
            PreparerMayApprove = preparerMayApprove,
            Reason = reason,
        });
        if (await FindExecutionAsync(keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done(metadata, subject, Replay(existing, requestHash, metadata));
        }

        var current = await ReadAsync(() => SegregationOfDutiesPolicyReads.LatestAsync(database, cancellationToken));
        var latest = current?.Version ?? 0;
        if (latest != expectedVersion)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done(metadata, subject,
                await StoreRefusalAsync(Answers.VersionConflict(), keyHash, requestHash, metadata, cancellationToken));
        }

        var inForce = current?.PreparerMayApprove ?? SegregationOfDutiesRuleV1.DefaultPreparerMayApprove;
        if (inForce == preparerMayApprove)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done(metadata, subject,
                await StoreRefusalAsync(Answers.NoContent(), keyHash, requestHash, metadata, cancellationToken));
        }

        var version = checked(latest + 1);
        var row = new SegregationOfDutiesPolicyVersionRow
        {
            CompanyId = companyId,
            Version = version,
            PreparerMayApprove = preparerMayApprove,
            RecordedAtUtc = timeProvider.GetUtcNow(),
            RecordedBy = metadata.ActorId,
            Reason = reason,
        };
        database.SegregationOfDutiesPolicyVersions.Add(row);
        Audit(companyId, subject, latest == 0 ? null : latest, version, scope, new
        {
            CompanyId = companyId,
            row.Version,
            row.PreparerMayApprove,
            PreviousPreparerMayApprove = inForce,
        }, reason, metadata);
        await RequireStillAsync(companyId, cancellationToken);
        return Done(metadata, subject, await SaveChangeAsync(Answers.NoContent(), keyHash, requestHash, metadata,
            Answers.VersionConflict(), cancellationToken));
    }

    private static SegregationOfDutiesPolicyVersionViewV1 View(SegregationOfDutiesPolicyVersionRow row) =>
        new SegregationOfDutiesPolicyVersionViewV1(
            Text(row.Version),
            row.PreparerMayApprove,
            row.RecordedBy,
            row.RecordedAtUtc,
            row.Reason);

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
            throw new SettingsPrerequisiteException(SegregationOfDutiesPolicyProblemCodesV1.ProviderUnavailable);
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

    private static SegregationOfDutiesPolicyCommandResultV1 Replay(CommandExecution execution, string requestHash,
        SettingsCommandMetadataV1 metadata)
    {
        if (!string.Equals(execution.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(execution.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new SegregationOfDutiesPolicyCommandResultV1(Answers.Problem(409,
                    SegregationOfDutiesPolicyProblemCodesV1.IdempotencyConflict,
                    "İşlem anahtarı kullanılmış.", "Bu Idempotency-Key başka bir istekle kullanıldı."),
                false, metadata.CorrelationId);
        }

        var answer = JsonSerializer.Deserialize<SegregationOfDutiesPolicyCommandAnswerV1>(execution.ResponseJson, Json);
        if (answer is null || answer.StatusCode != execution.HttpStatusCode
            || !string.Equals(Outcome(answer), execution.Outcome, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Kalıcı idempotency sonucu tanımlı görev ayrılığı komut sözleşmesiyle uyumsuz.");
        }

        return new SegregationOfDutiesPolicyCommandResultV1(answer, true, execution.OriginalCorrelationId);
    }

    private void AddExecution(SegregationOfDutiesPolicyCommandAnswerV1 answer, string keyHash, string requestHash,
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
    private async Task<SegregationOfDutiesPolicyCommandResultV1> StoreRefusalAsync(
        SegregationOfDutiesPolicyCommandAnswerV1 answer, string keyHash, string requestHash,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        AddExecution(answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new SegregationOfDutiesPolicyCommandResultV1(answer, false, metadata.CorrelationId);
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
    private async Task<SegregationOfDutiesPolicyCommandResultV1> SaveChangeAsync(
        SegregationOfDutiesPolicyCommandAnswerV1 answer, string keyHash, string requestHash,
        SettingsCommandMetadataV1 metadata, SegregationOfDutiesPolicyCommandAnswerV1 onConflict,
        CancellationToken cancellationToken)
    {
        AddExecution(answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new SegregationOfDutiesPolicyCommandResultV1(answer, false, metadata.CorrelationId);
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

    private static string Outcome(SegregationOfDutiesPolicyCommandAnswerV1 answer) => answer.Code ?? answer.StatusCode switch
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
            "Görev ayrılığı ayarı sürümü kaydedildi; gerekçe kayıt içeriğindedir",
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

    private SegregationOfDutiesPolicyCommandResultV1 Done(SettingsCommandMetadataV1 metadata, string subject,
        SegregationOfDutiesPolicyCommandResultV1 result)
    {
        logger.LogInformation(
            "Settings segregation of duties command completed. Command={Command} CompanyId={CompanyId} Subject={Subject} ActorId={ActorId} CorrelationId={CorrelationId} Status={Status} Code={Code} IsReplay={IsReplay}",
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

    private static SegregationOfDutiesPolicyCommandAnswerV1? ReasonRefusal(string? reason, out string normalized)
    {
        normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return Answers.Problem(400, SegregationOfDutiesPolicyProblemCodesV1.ReasonRequired, "Gerekçe gerekli.",
                "Bu değişiklik için gerekçe zorunludur.");
        }

        return normalized.Length > SegregationOfDutiesPolicyInputLimitsV1.ReasonMaximumLength
            ? Answers.Problem(400, SegregationOfDutiesPolicyProblemCodesV1.ReasonTooLong, "Gerekçe çok uzun.",
                $"Gerekçe en fazla {SegregationOfDutiesPolicyInputLimitsV1.ReasonMaximumLength.ToString(CultureInfo.InvariantCulture)} karakter olabilir.")
            : null;
    }

    private static SegregationOfDutiesPolicyCommandResultV1 Unstored(SegregationOfDutiesPolicyCommandAnswerV1 answer,
        SettingsCommandMetadataV1 metadata) =>
        new SegregationOfDutiesPolicyCommandResultV1(answer, false, metadata.CorrelationId);

    private static class Answers
    {
        public static SegregationOfDutiesPolicyCommandAnswerV1 NoContent() =>
            new SegregationOfDutiesPolicyCommandAnswerV1(204, null, "Kaydedildi.", "Değişiklik kaydedildi.");

        public static SegregationOfDutiesPolicyCommandAnswerV1 Invalid(string detail) =>
            Problem(400, SegregationOfDutiesPolicyProblemCodesV1.RequestInvalid, "İstek geçersiz.", detail);

        public static SegregationOfDutiesPolicyCommandAnswerV1 VersionConflict() =>
            Problem(409, SegregationOfDutiesPolicyProblemCodesV1.VersionConflict, "Kayıt değişmiş.",
                "Kayıt siz açtıktan sonra değişti; güncel hâlini okuyup yeniden deneyin.");

        public static SegregationOfDutiesPolicyCommandAnswerV1 Problem(int status, string code, string title, string detail) =>
            new SegregationOfDutiesPolicyCommandAnswerV1(status, code, title, detail);
    }
}
