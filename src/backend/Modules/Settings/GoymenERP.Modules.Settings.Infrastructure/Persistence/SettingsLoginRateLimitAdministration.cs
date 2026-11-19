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
/// KIM-D34: the group-wide login attempt limit — attempts per fixed window and what a window counts per — as a persistent,
/// versioned Settings record behind <see cref="ISettingsLoginRateLimitProviderV1"/>.
/// <list type="bullet">
/// <item><b>Nothing is chosen here</b>: nothing is seeded. Until the first version is recorded the host keeps its configured
/// starting value (<c>Identity:LocalAccount:RateLimit:Login</c>, KIM-D29: 10 attempts per minute per IP + normalized user
/// name).</item>
/// <item><b>Group-wide</b>: one record for the whole group (no company key, no company filter). The command still runs in the
/// server-resolved company: the permission is measured there and the audit event and the command execution are recorded
/// there (the Counterparty e-document profile precedent).</item>
/// <item><b>Authority</b>: <see cref="SettingsLoginRateLimitPermissionManifestV1.Manage"/>, company-wide, measured first and
/// re-measured fresh right before every save; granted to no role here (human gate).</item>
/// <item><b>Every command</b> (SAT-D68 precedent): one person, a mandatory reason (≤ 4000), one Idempotency-Key (the stored
/// terminal answer replays; another body under the key is 409), the expected version as decimal text, correlation/causation
/// and one audit event per real change (none for a no-op). A version takes effect when it is saved.</item>
/// </list>
/// </summary>
internal sealed class SettingsLoginRateLimitAdministration(
    SettingsDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorizationService authorization,
    IErpAuthorityRevalidationService revalidation,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider,
    ILogger<SettingsLoginRateLimitAdministration> logger) : ISettingsLoginRateLimitAdministrationV1
{
    private const int CommandExecutionSchemaVersion = 1;
    private const string Permission = SettingsLoginRateLimitPermissionManifestV1.Manage;
    private const string EntityType = "Settings.LoginRateLimit";
    private const string EntityId = "group";
    private const string RecordCommandName = "settings.login-rate-limit.record.v1";
    private const string RecordedEvent = "settings.login-rate-limit.recorded.v1";
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<LoginRateLimitViewV1> GetAsync(CancellationToken cancellationToken = default)
    {
        var companyId = RequireContextCompany();
        await RequireAsync(companyId, cancellationToken);
        var rows = await ReadAsync(() => LoginRateLimitReads.LoadAsync(database, cancellationToken));
        await RequireStillAsync(companyId, cancellationToken);
        var latest = rows.Length == 0 ? 0 : rows[0].Version;
        return new LoginRateLimitViewV1(Text(latest), rows.Select(row => View(row, latest)).ToArray());
    }

    public async Task<LoginRateLimitCommandResultV1> RecordAsync(RecordLoginRateLimitRequestV1 request,
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

        if (!LoginRateLimitRulesV1.IsPermitLimit(request.PermitLimit))
        {
            return Unstored(Answers.Invalid(
                $"Deneme sayısı {Text(LoginRateLimitInputLimitsV1.MinimumPermitLimit)} ile {Text(LoginRateLimitInputLimitsV1.MaximumPermitLimit)} arasında tam sayı olmalıdır."),
                metadata);
        }

        if (!LoginRateLimitRulesV1.IsWindowSeconds(request.WindowSeconds))
        {
            return Unstored(Answers.Invalid(
                $"Pencere süresi {Text(LoginRateLimitInputLimitsV1.MinimumWindowSeconds)} ile {Text(LoginRateLimitInputLimitsV1.MaximumWindowSeconds)} saniye arasında tam sayı olmalıdır."),
                metadata);
        }

        if (!LoginRateLimitRulesV1.IsPartition(request.PartitionBy))
        {
            return Unstored(Answers.Invalid(
                "Sayım kapsamı tanımlı listeden seçilmelidir: " + string.Join(", ", LoginRateLimitPartitionsV1.All) + "."),
                metadata);
        }

        if (!LoginRateLimitRulesV1.TryNormalizeTrustedProxies(request.TrustedProxies, out var trustedProxies))
        {
            return Unstored(Answers.Invalid(
                $"Güvenilir proxy listesi zorunludur (boş olabilir) ve en çok {Text(LoginRateLimitInputLimitsV1.MaximumTrustedProxyCount)} IP adresi veya CIDR bloğu (ör. 10.0.0.0/8) içerebilir."),
                metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var permitLimit = request.PermitLimit!.Value;
        var windowSeconds = request.WindowSeconds!.Value;
        var partitionBy = request.PartitionBy!;
        var trustedProxiesText = LoginRateLimitRulesV1.JoinTrustedProxies(trustedProxies);
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);

        // An empty trusted proxy list stays out of the hash, so a key stored before KIM-D42 still replays.
        var requestHash = trustedProxies.Count == 0
            ? IdempotencyHash.ComputePayload(new
            {
                ExpectedVersion = expectedVersion,
                PermitLimit = permitLimit,
                WindowSeconds = windowSeconds,
                PartitionBy = partitionBy,
                Reason = reason,
            })
            : IdempotencyHash.ComputePayload(new
            {
                ExpectedVersion = expectedVersion,
                PermitLimit = permitLimit,
                WindowSeconds = windowSeconds,
                PartitionBy = partitionBy,
                TrustedProxies = trustedProxies,
                Reason = reason,
            });
        if (await FindExecutionAsync(keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done(metadata, Replay(existing, requestHash, metadata));
        }

        var current = await ReadAsync(() => LoginRateLimitReads.LatestAsync(database, cancellationToken));
        var latest = current?.Version ?? 0;
        if (latest != expectedVersion)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done(metadata,
                await StoreRefusalAsync(Answers.VersionConflict(), keyHash, requestHash, metadata, cancellationToken));
        }

        if (current is not null
            && current.PermitLimit == permitLimit
            && current.WindowSeconds == windowSeconds
            && string.Equals(current.PartitionBy, partitionBy, StringComparison.Ordinal)
            && string.Equals(current.TrustedProxies, trustedProxiesText, StringComparison.Ordinal))
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done(metadata,
                await StoreRefusalAsync(Answers.NoContent(), keyHash, requestHash, metadata, cancellationToken));
        }

        var version = checked(latest + 1);
        var row = new LoginRateLimitVersionRow
        {
            Version = version,
            ProfileId = Guid.NewGuid(),
            PermitLimit = permitLimit,
            WindowSeconds = windowSeconds,
            PartitionBy = partitionBy,
            TrustedProxies = trustedProxiesText,
            RecordedAtUtc = timeProvider.GetUtcNow(),
            RecordedBy = metadata.ActorId,
            RecordedInCompanyId = companyId,
            Reason = reason,
        };
        database.LoginRateLimitVersions.Add(row);
        Audit(companyId, latest == 0 ? null : latest, version, scope,
            current is null ? "{}" : JsonSerializer.Serialize(new
            {
                current.ProfileId,
                current.Version,
                current.PermitLimit,
                current.WindowSeconds,
                current.PartitionBy,
                TrustedProxies = TrustedProxiesOf(current),
            }, Json),
            new
            {
                row.ProfileId,
                row.Version,
                row.PermitLimit,
                row.WindowSeconds,
                row.PartitionBy,
                TrustedProxies = trustedProxies,
                Scope = "Group",
            }, reason, metadata);
        await RequireStillAsync(companyId, cancellationToken);
        return Done(metadata, await SaveChangeAsync(Answers.NoContent(), keyHash, requestHash, metadata,
            Answers.VersionConflict(), cancellationToken));
    }

    private static LoginRateLimitVersionViewV1 View(LoginRateLimitVersionRow row, long latest) =>
        new LoginRateLimitVersionViewV1(
            row.ProfileId,
            Text(row.Version),
            row.PermitLimit,
            row.WindowSeconds,
            row.PartitionBy,
            TrustedProxiesOf(row),
            row.Version == latest,
            row.RecordedBy,
            row.RecordedInCompanyId,
            row.RecordedAtUtc,
            row.Reason);

    /// <summary>The stored trusted proxy list of a version; malformed stored data is <c>settings_profile_invalid</c>.</summary>
    private static IReadOnlyList<string> TrustedProxiesOf(LoginRateLimitVersionRow row) =>
        LoginRateLimitRulesV1.TryReadTrustedProxies(row.TrustedProxies, out var entries)
            ? entries
            : throw new SettingsPrerequisiteException(LoginRateLimitProblemCodesV1.ProfileInvalid);

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
            throw new SettingsPrerequisiteException(LoginRateLimitProblemCodesV1.ProviderUnavailable);
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
            throw new SettingsPrerequisiteException(LoginRateLimitProblemCodesV1.ProviderUnavailable);
        }
    }

    private static LoginRateLimitCommandResultV1 Replay(CommandExecution execution, string requestHash,
        SettingsCommandMetadataV1 metadata)
    {
        if (!string.Equals(execution.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(execution.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new LoginRateLimitCommandResultV1(Answers.Problem(409, LoginRateLimitProblemCodesV1.IdempotencyConflict,
                    "İşlem anahtarı kullanılmış.", "Bu Idempotency-Key başka bir istekle kullanıldı."),
                false, metadata.CorrelationId);
        }

        var answer = JsonSerializer.Deserialize<LoginRateLimitCommandAnswerV1>(execution.ResponseJson, Json);
        if (answer is null || answer.StatusCode != execution.HttpStatusCode
            || !string.Equals(Outcome(answer), execution.Outcome, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Kalıcı idempotency sonucu tanımlı giriş deneme sınırı komut sözleşmesiyle uyumsuz.");
        }

        return new LoginRateLimitCommandResultV1(answer, true, execution.OriginalCorrelationId);
    }

    private void AddExecution(LoginRateLimitCommandAnswerV1 answer, string keyHash, string requestHash,
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
    private async Task<LoginRateLimitCommandResultV1> StoreRefusalAsync(LoginRateLimitCommandAnswerV1 answer,
        string keyHash, string requestHash, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        AddExecution(answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new LoginRateLimitCommandResultV1(answer, false, metadata.CorrelationId);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.GetBaseException() is SqlException { Number: 2601 or 2627 })
        {
            database.ChangeTracker.Clear();
            var winner = await FindExecutionAsync(keyHash, cancellationToken);
            if (winner is null)
            {
                throw new SettingsPrerequisiteException(LoginRateLimitProblemCodesV1.ProviderUnavailable);
            }

            return Replay(winner, requestHash, metadata);
        }
        catch (Exception exception) when (exception is DbUpdateException or SqlException)
        {
            // A provider failure is not a terminal business refusal and never consumes the retry key.
            database.ChangeTracker.Clear();
            throw new SettingsPrerequisiteException(LoginRateLimitProblemCodesV1.ProviderUnavailable);
        }
    }

    /// <summary>
    /// Saves the new version with its terminal answer and audit in one unit. A lost race (the version primary key) re-reads:
    /// a same-key winner replays, otherwise <paramref name="onConflict"/> is stored as the key's answer.
    /// </summary>
    private async Task<LoginRateLimitCommandResultV1> SaveChangeAsync(LoginRateLimitCommandAnswerV1 answer,
        string keyHash, string requestHash, SettingsCommandMetadataV1 metadata, LoginRateLimitCommandAnswerV1 onConflict,
        CancellationToken cancellationToken)
    {
        AddExecution(answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new LoginRateLimitCommandResultV1(answer, false, metadata.CorrelationId);
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
            throw new SettingsPrerequisiteException(LoginRateLimitProblemCodesV1.ProviderUnavailable);
        }
    }

    private static string Outcome(LoginRateLimitCommandAnswerV1 answer) => answer.Code ?? answer.StatusCode switch
    {
        204 => "Succeeded",
        _ => "Refused",
    };

    /// <summary>
    /// One audit event under the company the person worked in: the previous version in the before-image, the new version and
    /// the person's whole reason in the after-image (<c>reason</c>), a fixed description in the audit row's reason column.
    /// </summary>
    private void Audit(Guid companyId, long? versionBefore, long? versionAfter, PermissionScope scope, string beforeJson,
        object after, string reason, SettingsCommandMetadataV1 metadata) =>
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            RecordedEvent,
            EntityType,
            EntityId,
            "RecordVersion",
            versionBefore,
            versionAfter,
            AuditActorTypes.User,
            metadata.ActorId,
            Permission,
            scope.RoleCodes,
            beforeJson,
            AfterJson(after, reason),
            "Grup geneli giriş deneme sınırı sürümü kaydedildi; gerekçe kayıt içeriğindedir",
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

    private LoginRateLimitCommandResultV1 Done(SettingsCommandMetadataV1 metadata, LoginRateLimitCommandResultV1 result)
    {
        logger.LogInformation(
            "Settings login rate limit command completed. Command={Command} CompanyId={CompanyId} ActorId={ActorId} CorrelationId={CorrelationId} Status={Status} Code={Code} IsReplay={IsReplay}",
            "record", metadata.CompanyId, metadata.ActorId, metadata.CorrelationId, result.Answer.StatusCode,
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

    private static LoginRateLimitCommandAnswerV1? ReasonRefusal(string? reason, out string normalized)
    {
        normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return Answers.Problem(400, LoginRateLimitProblemCodesV1.ReasonRequired, "Gerekçe gerekli.",
                "Bu değişiklik için gerekçe zorunludur.");
        }

        return normalized.Length > LoginRateLimitInputLimitsV1.ReasonMaximumLength
            ? Answers.Problem(400, LoginRateLimitProblemCodesV1.ReasonTooLong, "Gerekçe çok uzun.",
                $"Gerekçe en fazla {Text(LoginRateLimitInputLimitsV1.ReasonMaximumLength)} karakter olabilir.")
            : null;
    }

    private static LoginRateLimitCommandResultV1 Unstored(LoginRateLimitCommandAnswerV1 answer,
        SettingsCommandMetadataV1 metadata) =>
        new LoginRateLimitCommandResultV1(answer, false, metadata.CorrelationId);

    private static class Answers
    {
        public static LoginRateLimitCommandAnswerV1 NoContent() =>
            new LoginRateLimitCommandAnswerV1(204, null, "Kaydedildi.", "Değişiklik kaydedildi.");

        public static LoginRateLimitCommandAnswerV1 Invalid(string detail) =>
            Problem(400, LoginRateLimitProblemCodesV1.RequestInvalid, "İstek geçersiz.", detail);

        public static LoginRateLimitCommandAnswerV1 VersionConflict() =>
            Problem(409, LoginRateLimitProblemCodesV1.VersionConflict, "Kayıt değişmiş.",
                "Kayıt siz açtıktan sonra değişti; güncel hâlini okuyup yeniden deneyin.");

        public static LoginRateLimitCommandAnswerV1 Problem(int status, string code, string title, string detail) =>
            new LoginRateLimitCommandAnswerV1(status, code, title, detail);
    }
}
