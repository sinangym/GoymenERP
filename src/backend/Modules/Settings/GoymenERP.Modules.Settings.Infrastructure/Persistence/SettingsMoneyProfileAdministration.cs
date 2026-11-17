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

/// <summary>URN-D2/D3/D4: prospective money profile versions; catalog identities and existing document snapshots stay stable.</summary>
internal sealed class SettingsMoneyProfileAdministration(SettingsDbContext database, ICompanyContext companyContext,
    ICurrentUser currentUser, IErpAuthorizationService authorization, IErpAuthorityRevalidationService revalidation,
    IAuditEventCollector auditEvents, TimeProvider timeProvider, ILogger<SettingsMoneyProfileAdministration> logger)
    : ISettingsMoneyProfileAdministrationV1
{
    private const int CommandExecutionSchemaVersion = 1;
    private const string Permission = SettingsMoneyProfilePermissionManifestV1.Manage;
    private const string EntityType = "Settings.MoneyProfile";
    private const string RecordCommandName = "settings.money-profile.record.v1";
    private const string RecordedEvent = "settings.money-profile.recorded.v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<MoneyProfileViewV1> GetAsync(CancellationToken cancellationToken = default)
    {
        var company = RequireContextCompany();
        var actor = currentUser.UserId!;
        await RequireAsync(company, cancellationToken);
        var today = SettingsBusinessDate.Today(timeProvider);
        var stored = await ReadAsync(() => database.MoneyProfileVersions.AsNoTracking().ToArrayAsync(cancellationToken));
        var markers = await ReadAsync(() => database.MoneyProfileBaselineTransfers.AsNoTracking().ToArrayAsync(cancellationToken));
        var keys = MoneyProfileAuthoringCatalogV1.Targets(company)
            .Distinct().OrderBy(key => key.CurrencyId).ThenBy(key => key.Operation, StringComparer.Ordinal).ToArray();
        if (stored.Any(row => !keys.Contains((row.CurrencyId, row.Operation)))
            || markers.Any(marker => !stored.Any(row => row.CurrencyId == marker.CurrencyId && row.Operation == marker.Operation))
            || stored.Any(row => !markers.Any(marker => row.CurrencyId == marker.CurrencyId && row.Operation == marker.Operation)))
            throw new SettingsPrerequisiteException("settings_profile_invalid");
        var profiles = keys.Select(key =>
        {
            var rows = stored.Where(row => row.CurrencyId == key.CurrencyId && row.Operation == key.Operation).ToArray();
            var isStored = rows.Length != 0;
            if (!isStored) rows = MoneyProfileReads.Baseline(company, key.CurrencyId, key.Operation);
            MoneyProfileReads.Validate(rows, company, key.CurrencyId, key.Operation);
            var timeline = MoneyProfileReads.Timeline(rows);
            return new MoneyProfileSeriesViewV1(key.CurrencyId, MoneyProfileAuthoringCatalogV1.CurrencyCode(key.CurrencyId)!, key.Operation,
                Text(SalesPolicyTimelineV1.LatestVersion(timeline)), timeline.Select(entry => new MoneyProfileVersionViewV1(
                    entry.Value.ProfileId, Text(entry.Version), entry.Value.Scale, entry.Value.UnitPriceScale,
                    entry.Value.IsActive, entry.EffectiveFrom, entry.EffectiveThrough, SalesPolicyTimelineV1.State(entry, today),
                    entry.Value.SourceKind, entry.Value.DecisionReference, isStored ? entry.Value.RecordedBy : null,
                    isStored ? entry.Value.RecordedAtUtc : null, isStored ? entry.Value.Reason : null)).ToArray());
        }).ToArray();
        await RequireStillAsync(company, actor, cancellationToken);
        return new MoneyProfileViewV1(company, today, profiles);
    }

    public async Task<MoneyProfileCommandResultV1> RecordAsync(RecordMoneyProfileRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var company = RequireContextCompany();
        var actor = metadata.ActorId;
        var scope = await RequireAsync(company, cancellationToken);
        if (!TryParseVersion(request.ExpectedVersion, 0, out var expected) || request.CurrencyId is not { } currency
            || currency == Guid.Empty || string.IsNullOrWhiteSpace(request.Operation) || request.Operation.Length > 128
            || request.Scale is not { } scale || scale is < 0 or > 28 || request.UnitPriceScale is < 0 or > 28
            || request.IsActive is not { } active)
            return Unstored(Answers.Invalid("Para birimi, işlem, beklenen sürüm ve 0–28 arası ölçekler zorunludur."), metadata);
        if (ReasonRefusal(request.Reason, out var reason) is { } invalid) return Unstored(invalid, metadata);
        var operation = request.Operation;
        var baseline = MoneyProfileReads.Baseline(company, currency, operation);
        if (!MoneyProfileAuthoringCatalogV1.Allows(company, currency, operation))
            return Unstored(Answers.Problem(400, MoneyProfileProblemCodesV1.ProfileUnknown,
                "Para profili tanımsız.", "Bu şirket, para birimi ve işlem için kayıtlı yönetim hedefi yok."), metadata);
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new { ExpectedVersion = expected, CurrencyId = currency,
            Operation = operation, Scale = scale, request.UnitPriceScale, IsActive = active, Reason = reason, request.EffectiveFrom });
        if (await ReadAsync(() => FindExecutionAsync(RecordCommandName, keyHash, cancellationToken)) is { } replay)
        {
            await RequireStillAsync(company, actor, cancellationToken);
            return Replay(replay, requestHash, metadata);
        }
        var rows = await ReadAsync(() => MoneyProfileReads.LoadAsync(database, currency, operation, cancellationToken));
        var imported = rows.Length == 0;
        var source = imported ? baseline : rows;
        MoneyProfileReads.Validate(source, company, currency, operation);
        var timeline = MoneyProfileReads.Timeline(source);
        var latest = SalesPolicyTimelineV1.LatestVersion(timeline);
        var today = SettingsBusinessDate.Today(timeProvider);
        var from = request.EffectiveFrom ?? today;
        var refusal = expected != latest ? Answers.VersionConflict() : from < today ? Answers.EffectiveFromPast() : null;
        if (refusal is not null)
        {
            await RequireStillAsync(company, actor, cancellationToken);
            return await StoreRefusalAsync(RecordCommandName, refusal, keyHash, requestHash, metadata, cancellationToken);
        }
        var current = SalesPolicyTimelineV1.InForceOn(timeline, from)?.Value;
        if (current is not null && current.Scale == scale && current.UnitPriceScale == request.UnitPriceScale && current.IsActive == active)
        {
            await RequireStillAsync(company, actor, cancellationToken);
            return await StoreRefusalAsync(RecordCommandName, Answers.NoContent(), keyHash, requestHash, metadata, cancellationToken);
        }
        if (SettingsBusinessDate.Today(timeProvider) != today) return Unstored(Answers.VersionConflict(), metadata);
        if (imported)
        {
            database.MoneyProfileBaselineTransfers.Add(new MoneyProfileBaselineTransferRow
                { CompanyId = company, CurrencyId = currency, Operation = operation });
            foreach (var row in baseline)
            {
                row.RecordedAtUtc = timeProvider.GetUtcNow(); row.RecordedBy = metadata.ActorId; row.Reason = reason;
                database.MoneyProfileVersions.Add(row);
            }
        }
        var next = new MoneyProfileVersionRow { CompanyId = company, CurrencyId = currency, Operation = operation,
            Version = checked(latest + 1), ProfileId = Guid.NewGuid(), CurrencyCode = MoneyProfileAuthoringCatalogV1.CurrencyCode(currency)!,
            Scale = scale, UnitPriceScale = request.UnitPriceScale, IsActive = active, EffectiveFrom = from,
            SourceKind = SalesPolicySourceKindsV1.UserMaintained, RecordedAtUtc = timeProvider.GetUtcNow(),
            RecordedBy = metadata.ActorId, Reason = reason };
        database.MoneyProfileVersions.Add(next);
        var subject = currency.ToString("D") + ":" + operation;
        Audit(RecordedEvent, "RecordMoneyProfile", company, subject, latest, next.Version, scope,
            new { CurrencyId = currency, Operation = operation, next.ProfileId, next.Version, next.Scale, next.UnitPriceScale,
                next.IsActive, next.EffectiveFrom, PreviousScale = current?.Scale, PreviousUnitPriceScale = current?.UnitPriceScale,
                PreviousIsActive = current?.IsActive, BaselineCopied = imported }, reason,
            "Para profili sürümü kaydedildi; gerekçe kayıt içeriğindedir", metadata);
        await RequireStillAsync(company, actor, cancellationToken);
        return Done("record", metadata, subject, await SaveChangeAsync(RecordCommandName, Answers.NoContent(), keyHash,
            requestHash, metadata, Answers.VersionConflict(), cancellationToken));
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
    private async Task RequireStillAsync(Guid companyId, string actor, CancellationToken cancellationToken)
    {
        RequireSameActorCompany(companyId, actor);
        var scope = await revalidation.RevalidatePermissionScopeAsync(Permission, companyId, cancellationToken);
        RequireSameActorCompany(companyId, actor);
        if (scope is not { IsGranted: true, IsCompanyWide: true })
        {
            throw new UnauthorizedAccessException();
        }
    }

    private void RequireSameActorCompany(Guid companyId, string actor)
    {
        if (!companyContext.IsResolved || companyContext.CompanyId != companyId || !currentUser.IsAuthenticated
            || !string.Equals(currentUser.UserId, actor, StringComparison.Ordinal) || !currentUser.CanAccessCompany(companyId))
            throw new UnauthorizedAccessException();
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
            throw new SettingsPrerequisiteException(MoneyProfileProblemCodesV1.ProviderUnavailable);
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

    private static MoneyProfileCommandResultV1 Replay(CommandExecution execution, string requestHash,
        SettingsCommandMetadataV1 metadata)
    {
        if (!string.Equals(execution.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(execution.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new MoneyProfileCommandResultV1(Answers.Problem(409, MoneyProfileProblemCodesV1.IdempotencyConflict,
                    "İşlem anahtarı kullanılmış.", "Bu Idempotency-Key başka bir istekle kullanıldı."),
                false, metadata.CorrelationId);
        }

        var answer = JsonSerializer.Deserialize<MoneyProfileCommandAnswerV1>(execution.ResponseJson, Json);
        if (answer is null || answer.StatusCode != execution.HttpStatusCode
            || !string.Equals(Outcome(answer), execution.Outcome, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Kalıcı idempotency sonucu tanımlı para profili komut sözleşmesiyle uyumsuz.");
        }

        return new MoneyProfileCommandResultV1(answer, true, execution.OriginalCorrelationId);
    }

    private void AddExecution(string commandName, MoneyProfileCommandAnswerV1 answer, string keyHash, string requestHash,
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
    private async Task<MoneyProfileCommandResultV1> StoreRefusalAsync(string commandName, MoneyProfileCommandAnswerV1 answer,
        string keyHash, string requestHash, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        AddExecution(commandName, answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new MoneyProfileCommandResultV1(answer, false, metadata.CorrelationId);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            var winner = await ReadAsync(() => FindExecutionAsync(commandName, keyHash, cancellationToken));
            if (winner is null)
            {
                throw new SettingsPrerequisiteException(MoneyProfileProblemCodesV1.ProviderUnavailable);
            }

            return Replay(winner, requestHash, metadata);
        }
    }

    /// <summary>
    /// Saves the change with its terminal answer and audit in one unit. A lost race (the version primary key, or the transfer
    /// marker's company key) re-reads: a same-key winner replays, otherwise <paramref name="onConflict"/> is stored as the
    /// key's answer.
    /// </summary>
    private async Task<MoneyProfileCommandResultV1> SaveChangeAsync(string commandName, MoneyProfileCommandAnswerV1 answer,
        string keyHash, string requestHash, SettingsCommandMetadataV1 metadata, MoneyProfileCommandAnswerV1 onConflict,
        CancellationToken cancellationToken)
    {
        AddExecution(commandName, answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new MoneyProfileCommandResultV1(answer, false, metadata.CorrelationId);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            var winner = await ReadAsync(() => FindExecutionAsync(commandName, keyHash, cancellationToken));
            if (winner is not null)
            {
                return Replay(winner, requestHash, metadata);
            }

            return await StoreRefusalAsync(commandName, onConflict, keyHash, requestHash, metadata, cancellationToken);
        }
    }

    private static string Outcome(MoneyProfileCommandAnswerV1 answer) => answer.Code ?? answer.StatusCode switch
    {
        204 => "Succeeded",
        _ => "Refused",
    };

    /// <summary>
    /// One audit event: the person's whole reason in the after-image (<c>reason</c>), a fixed description in the audit row's
    /// reason column (the SAT-D68 precedent), so nothing is truncated.
    /// </summary>
    private void Audit(string eventType, string action, Guid companyId, string entityId, long? versionBefore,
        long? versionAfter, PermissionScope scope, object after, string reason, string description,
        SettingsCommandMetadataV1 metadata) =>
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            eventType,
            EntityType,
            entityId,
            action,
            versionBefore,
            versionAfter,
            AuditActorTypes.User,
            metadata.ActorId,
            Permission,
            scope.RoleCodes,
            "{}",
            AfterJson(after, reason),
            description,
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

    private MoneyProfileCommandResultV1 Done(string command, SettingsCommandMetadataV1 metadata, string subject,
        MoneyProfileCommandResultV1 result)
    {
        logger.LogInformation(
            "Settings money profile command completed. Command={Command} CompanyId={CompanyId} Subject={Subject} ActorId={ActorId} CorrelationId={CorrelationId} Status={Status} Code={Code} IsReplay={IsReplay}",
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

    private static MoneyProfileCommandAnswerV1? ReasonRefusal(string? reason, out string normalized)
    {
        normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return Answers.Problem(400, MoneyProfileProblemCodesV1.ReasonRequired, "Gerekçe gerekli.",
                "Bu değişiklik için gerekçe zorunludur.");
        }

        return normalized.Length > MoneyProfileInputLimitsV1.ReasonMaximumLength
            ? Answers.Problem(400, MoneyProfileProblemCodesV1.ReasonTooLong, "Gerekçe çok uzun.",
                $"Gerekçe en fazla {MoneyProfileInputLimitsV1.ReasonMaximumLength.ToString(CultureInfo.InvariantCulture)} karakter olabilir.")
            : null;
    }

    private static MoneyProfileCommandResultV1 Unstored(MoneyProfileCommandAnswerV1 answer, SettingsCommandMetadataV1 metadata) =>
        new MoneyProfileCommandResultV1(answer, false, metadata.CorrelationId);

    private static class Answers
    {
        public static MoneyProfileCommandAnswerV1 NoContent() =>
            new MoneyProfileCommandAnswerV1(204, null, "Kaydedildi.", "Değişiklik kaydedildi.");

        public static MoneyProfileCommandAnswerV1 Invalid(string detail) =>
            Problem(400, MoneyProfileProblemCodesV1.RequestInvalid, "İstek geçersiz.", detail);

        public static MoneyProfileCommandAnswerV1 VersionConflict() =>
            Problem(409, MoneyProfileProblemCodesV1.VersionConflict, "Kayıt değişmiş.",
                "Kayıt siz açtıktan sonra değişti; güncel hâlini okuyup yeniden deneyin.");

        public static MoneyProfileCommandAnswerV1 EffectiveFromPast() =>
            Problem(422, MoneyProfileProblemCodesV1.EffectiveFromPast, "Geçmiş tarih.",
                "Para profili sürümü bugünden önceki bir tarihten başlatılamaz.");

        public static MoneyProfileCommandAnswerV1 Problem(int status, string code, string title, string detail) =>
            new MoneyProfileCommandAnswerV1(status, code, title, detail);
    }
}
