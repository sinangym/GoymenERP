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
/// API-98/B-14 (ALS-D79, BS-10; KIM-D49, KIM-D51): the company's reference lists — expense types and service types (the
/// purchasing lists) and paint colors and scrap kinds (the production lists) — behind <see cref="ISettingsReferenceListReaderV1"/>.
/// <list type="bullet">
/// <item><b>Nothing is chosen here</b>: nothing is seeded; every list starts empty. The operation type list is not opened
/// (no route, no write). "Satış fasonu" is an operation type value, so it never enters the service type list by any seed or
/// rule here.</item>
/// <item><b>Company-scoped</b>: every row carries the server-resolved company in its key and under the company query
/// filter.</item>
/// <item><b>Authority</b>: the kind's permission (<see cref="SettingsReferenceListKindsV1.PermissionOf"/>), company-wide,
/// measured first and re-measured fresh right before every save; granted to no role here (human gate).</item>
/// <item><b>Every command</b> (SAT-D68 precedent): one person, a mandatory reason (≤ 4000), one Idempotency-Key (the stored
/// terminal answer replays; another body under the key is 409), the entry's expected version for a new version,
/// correlation/causation and one audit event per real change (none for a no-op).</item>
/// </list>
/// </summary>
internal sealed class SettingsReferenceListAdministration(
    SettingsDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorizationService authorization,
    IErpAuthorityRevalidationService revalidation,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider,
    ILogger<SettingsReferenceListAdministration> logger) : ISettingsReferenceListAdministrationV1
{
    private const int CommandExecutionSchemaVersion = 1;
    private const string EntityType = "Settings.ReferenceListEntry";
    private const string CreateCommandName = "settings.reference-list-entry.create.v1";
    private const string RecordCommandName = "settings.reference-list-entry.record.v1";
    private const string CreatedEvent = "settings.reference-list-entry.created.v1";
    private const string RecordedEvent = "settings.reference-list-entry.recorded.v1";
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<SettingsReferenceListViewV1> GetAsync(SettingsReferenceListKindV1 kind,
        CancellationToken cancellationToken = default)
    {
        var (kindCode, permission) = Require(kind);
        var companyId = RequireContextCompany();
        await RequireAsync(permission, companyId, cancellationToken);
        var snapshots = await ReadAsync(() => ReferenceListReads.LoadAsync(database, kindCode, cancellationToken));
        await RequireStillAsync(permission, companyId, cancellationToken);
        var entries = snapshots.Select(snapshot =>
        {
            var latest = Latest(snapshot);
            return new SettingsReferenceListEntryViewV1(snapshot.Entry.EntryId, snapshot.Entry.Code, latest.Name,
                latest.Active, Text(latest.Version), latest.RecordedBy, latest.RecordedAtUtc);
        }).ToArray();
        return new SettingsReferenceListViewV1(kindCode, entries);
    }

    public async Task<SettingsReferenceListCommandResultV1> CreateAsync(SettingsReferenceListKindV1 kind,
        CreateSettingsReferenceListEntryRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (kindCode, permission) = Require(kind);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(permission, companyId, cancellationToken);
        if (!ReferenceListRulesV1.IsCode(request.Code))
        {
            return Unstored(Answers.Invalid(
                $"Kod zorunludur: en fazla {Text(SettingsReferenceListInputLimitsV1.CodeMaximumLength)} karakter; büyük harf (A-Z), rakam, nokta, alt çizgi veya tire; harf ya da rakamla başlar."),
                metadata);
        }

        if (!ReferenceListRulesV1.TryNormalizeName(request.Name, out var name))
        {
            return Unstored(NameInvalid(), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var code = request.Code!;
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new { Kind = kindCode, Code = code, Name = name, Reason = reason });
        if (await FindExecutionAsync(CreateCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(permission, companyId, metadata, cancellationToken);
            return Done("create", metadata, kindCode, null, Replay(existing, requestHash, metadata));
        }

        if (await ReadAsync(() => ReferenceListReads.CodeExistsAsync(database, kindCode, code, cancellationToken)))
        {
            return Done("create", metadata, kindCode, null, await StoreRefusalAsync(CreateCommandName, Answers.CodeConflict(),
                keyHash, requestHash, metadata, permission, companyId, cancellationToken));
        }

        var now = timeProvider.GetUtcNow();
        var entry = new ReferenceListEntryRow
        {
            CompanyId = companyId,
            EntryId = Guid.NewGuid(),
            Kind = kindCode,
            Code = code,
            CreatedAtUtc = now,
            CreatedBy = metadata.ActorId,
        };
        var version = new ReferenceListEntryVersionRow
        {
            CompanyId = companyId,
            EntryId = entry.EntryId,
            Version = 1,
            Name = name,
            Active = true,
            RecordedAtUtc = now,
            RecordedBy = metadata.ActorId,
            Reason = reason,
        };
        database.ReferenceListEntries.Add(entry);
        database.ReferenceListEntryVersions.Add(version);
        Audit(CreatedEvent, "Create", companyId, entry.EntryId, null, version.Version, permission, scope, "{}",
            Image(entry, version), reason, "Referans listesi kaydı oluşturuldu; gerekçe kayıt içeriğindedir", metadata);
        await RequireStillAsync(permission, companyId, metadata, cancellationToken);
        return Done("create", metadata, kindCode, entry.EntryId, await SaveChangeAsync(CreateCommandName,
            Answers.NoContent(), keyHash, requestHash, metadata, Answers.CodeConflict(), permission, companyId, cancellationToken));
    }

    public async Task<SettingsReferenceListCommandResultV1> RecordAsync(SettingsReferenceListKindV1 kind, Guid entryId,
        RecordSettingsReferenceListEntryRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (kindCode, permission) = Require(kind);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(permission, companyId, cancellationToken);
        if (entryId == Guid.Empty)
        {
            return Unstored(Answers.Invalid("Kayıt kimliği zorunludur."), metadata);
        }

        if (!TryParseVersion(request.ExpectedVersion, 1, out var expectedVersion))
        {
            return Unstored(Answers.Invalid("Beklenen sürüm (pozitif tam sayı metni) zorunludur."), metadata);
        }

        if (!ReferenceListRulesV1.TryNormalizeName(request.Name, out var name))
        {
            return Unstored(NameInvalid(), metadata);
        }

        if (request.Active is not { } active)
        {
            return Unstored(Answers.Invalid("Etkinlik durumu (true/false) zorunludur."), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            Kind = kindCode,
            EntryId = entryId,
            ExpectedVersion = expectedVersion,
            Name = name,
            Active = active,
            Reason = reason,
        });
        if (await FindExecutionAsync(RecordCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(permission, companyId, metadata, cancellationToken);
            return Done("record", metadata, kindCode, entryId, Replay(existing, requestHash, metadata));
        }

        var snapshot = await ReadAsync(() => ReferenceListReads.FindAsync(database, kindCode, entryId, cancellationToken));
        if (snapshot is null)
        {
            return Done("record", metadata, kindCode, entryId, await StoreRefusalAsync(RecordCommandName,
                Answers.EntryNotFound(), keyHash, requestHash, metadata, permission, companyId, cancellationToken));
        }

        var current = Latest(snapshot);
        if (current.Version != expectedVersion)
        {
            return Done("record", metadata, kindCode, entryId, await StoreRefusalAsync(RecordCommandName,
                Answers.VersionConflict(), keyHash, requestHash, metadata, permission, companyId, cancellationToken));
        }

        if (current.Active == active && string.Equals(current.Name, name, StringComparison.Ordinal))
        {
            return Done("record", metadata, kindCode, entryId, await StoreRefusalAsync(RecordCommandName,
                Answers.NoContent(), keyHash, requestHash, metadata, permission, companyId, cancellationToken));
        }

        var next = new ReferenceListEntryVersionRow
        {
            CompanyId = companyId,
            EntryId = entryId,
            Version = checked(current.Version + 1),
            Name = name,
            Active = active,
            RecordedAtUtc = timeProvider.GetUtcNow(),
            RecordedBy = metadata.ActorId,
            Reason = reason,
        };
        database.ReferenceListEntryVersions.Add(next);
        Audit(RecordedEvent, "RecordVersion", companyId, entryId, current.Version, next.Version, permission, scope,
            JsonSerializer.Serialize(Image(snapshot.Entry, current), Json), Image(snapshot.Entry, next), reason,
            "Referans listesi kaydı sürümü kaydedildi; gerekçe kayıt içeriğindedir", metadata);
        await RequireStillAsync(permission, companyId, metadata, cancellationToken);
        return Done("record", metadata, kindCode, entryId, await SaveChangeAsync(RecordCommandName, Answers.NoContent(),
            keyHash, requestHash, metadata, Answers.VersionConflict(), permission, companyId, cancellationToken));
    }

    private static object Image(ReferenceListEntryRow entry, ReferenceListEntryVersionRow version) => new
    {
        entry.EntryId,
        entry.Kind,
        entry.Code,
        version.Version,
        version.Name,
        version.Active,
    };

    /// <summary>The enabled kind's stored code and permission; any other kind is a programming error (no route maps it).</summary>
    private static (string KindCode, string Permission) Require(SettingsReferenceListKindV1 kind) =>
        SettingsReferenceListKindsV1.CodeOf(kind) is { } code && SettingsReferenceListKindsV1.PermissionOf(kind) is { } permission
            ? (code, permission)
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Bu referans listesi açık değil.");

    /// <summary>The latest version of a stored entry; an entry without a well-formed version is malformed data.</summary>
    private static ReferenceListEntryVersionRow Latest(ReferenceListEntrySnapshot snapshot) =>
        snapshot.Latest is { Version: > 0 } latest && ReferenceListRulesV1.IsStoredName(latest.Name)
            && ReferenceListRulesV1.IsCode(snapshot.Entry.Code)
            ? latest
            : throw new SettingsPrerequisiteException(SettingsReferenceListProblemCodesV1.ProfileInvalid);

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

    private async Task RequireStillAsync(string permission, Guid companyId, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken)
    {
        RequireCommandContext();
        await RequireStillAsync(permission, companyId, cancellationToken);
        RequireCommandContext();

        void RequireCommandContext()
        {
            if (!currentUser.IsAuthenticated || !companyContext.IsResolved || companyContext.CompanyId != companyId
                || metadata.CompanyId != companyId
                || !currentUser.CanAccessCompany(companyId)
                || !string.Equals(currentUser.UserId, metadata.ActorId, StringComparison.Ordinal))
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
            throw new SettingsPrerequisiteException(SettingsReferenceListProblemCodesV1.ProviderUnavailable);
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

    private Task<CommandExecution?> FindExecutionAsync(string commandName, string keyHash,
        CancellationToken cancellationToken) =>
        database.CommandExecutions.AsNoTracking()
            .SingleOrDefaultAsync(execution => execution.CommandName == commandName && execution.IdempotencyKeyHash == keyHash,
                cancellationToken);

    private static SettingsReferenceListCommandResultV1 Replay(CommandExecution execution, string requestHash,
        SettingsCommandMetadataV1 metadata)
    {
        if (!string.Equals(execution.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(execution.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new SettingsReferenceListCommandResultV1(Answers.Problem(409,
                    SettingsReferenceListProblemCodesV1.IdempotencyConflict,
                    "İşlem anahtarı kullanılmış.", "Bu Idempotency-Key başka bir istekle kullanıldı."),
                false, metadata.CorrelationId);
        }

        var answer = JsonSerializer.Deserialize<SettingsReferenceListCommandAnswerV1>(execution.ResponseJson, Json);
        if (answer is null || answer.StatusCode != execution.HttpStatusCode
            || !string.Equals(Outcome(answer), execution.Outcome, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Kalıcı idempotency sonucu referans listesi komut sözleşmesiyle uyumsuz.");
        }

        return new SettingsReferenceListCommandResultV1(answer, true, execution.OriginalCorrelationId);
    }

    private void AddExecution(string commandName, SettingsReferenceListCommandAnswerV1 answer, string keyHash,
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

    /// <summary>Stores a refusal or no-op (no business change) as the key's terminal answer; a same-key winner replays.</summary>
    private async Task<SettingsReferenceListCommandResultV1> StoreRefusalAsync(string commandName,
        SettingsReferenceListCommandAnswerV1 answer, string keyHash, string requestHash, SettingsCommandMetadataV1 metadata,
        string permission, Guid companyId, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        await RequireStillAsync(permission, companyId, metadata, cancellationToken);
        AddExecution(commandName, answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new SettingsReferenceListCommandResultV1(answer, false, metadata.CorrelationId);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            var winner = await FindExecutionAsync(commandName, keyHash, cancellationToken);
            await RequireStillAsync(permission, companyId, metadata, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return Replay(winner, requestHash, metadata);
        }
    }

    /// <summary>
    /// Saves the change with its terminal answer and audit in one unit. A lost race (the code's unique index or the version
    /// primary key) re-reads: a same-key winner replays, otherwise <paramref name="onConflict"/> is stored as the key's answer.
    /// </summary>
    private async Task<SettingsReferenceListCommandResultV1> SaveChangeAsync(string commandName,
        SettingsReferenceListCommandAnswerV1 answer, string keyHash, string requestHash, SettingsCommandMetadataV1 metadata,
        SettingsReferenceListCommandAnswerV1 onConflict, string permission, Guid companyId, CancellationToken cancellationToken)
    {
        AddExecution(commandName, answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new SettingsReferenceListCommandResultV1(answer, false, metadata.CorrelationId);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            var winner = await FindExecutionAsync(commandName, keyHash, cancellationToken);
            await RequireStillAsync(permission, companyId, metadata, cancellationToken);
            if (winner is not null)
            {
                return Replay(winner, requestHash, metadata);
            }

            return await StoreRefusalAsync(commandName, onConflict, keyHash, requestHash, metadata,
                permission, companyId, cancellationToken);
        }
    }

    private static string Outcome(SettingsReferenceListCommandAnswerV1 answer) => answer.Code ?? answer.StatusCode switch
    {
        204 => "Succeeded",
        _ => "Refused",
    };

    /// <summary>
    /// One audit event under the company: the entry as the entity, the previous version in the before-image, the new version
    /// and the person's whole reason in the after-image (<c>reason</c>), a fixed description in the reason column.
    /// </summary>
    private void Audit(string eventName, string action, Guid companyId, Guid entryId, long? versionBefore, long versionAfter,
        string permission, PermissionScope scope, string beforeJson, object after, string reason, string description,
        SettingsCommandMetadataV1 metadata) =>
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            eventName,
            EntityType,
            entryId.ToString("D"),
            action,
            versionBefore,
            versionAfter,
            AuditActorTypes.User,
            metadata.ActorId,
            permission,
            scope.RoleCodes,
            beforeJson,
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

    private SettingsReferenceListCommandResultV1 Done(string command, SettingsCommandMetadataV1 metadata, string kindCode,
        Guid? entryId, SettingsReferenceListCommandResultV1 result)
    {
        logger.LogInformation(
            "Settings reference list command completed. Command={Command} Kind={Kind} CompanyId={CompanyId} EntryId={EntryId} ActorId={ActorId} CorrelationId={CorrelationId} Status={Status} Code={Code} IsReplay={IsReplay}",
            command, kindCode, metadata.CompanyId, entryId, metadata.ActorId, metadata.CorrelationId,
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

    private static SettingsReferenceListCommandAnswerV1 NameInvalid() =>
        Answers.Invalid(
            $"Ad zorunludur: boşluklar kırpıldıktan sonra 1-{Text(SettingsReferenceListInputLimitsV1.NameMaximumLength)} karakter, kontrol karakteri içermez.");

    private static SettingsReferenceListCommandAnswerV1? ReasonRefusal(string? reason, out string normalized)
    {
        normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return Answers.Problem(400, SettingsReferenceListProblemCodesV1.ReasonRequired, "Gerekçe gerekli.",
                "Bu değişiklik için gerekçe zorunludur.");
        }

        return normalized.Length > SettingsReferenceListInputLimitsV1.ReasonMaximumLength
            ? Answers.Problem(400, SettingsReferenceListProblemCodesV1.ReasonTooLong, "Gerekçe çok uzun.",
                $"Gerekçe en fazla {Text(SettingsReferenceListInputLimitsV1.ReasonMaximumLength)} karakter olabilir.")
            : null;
    }

    private static SettingsReferenceListCommandResultV1 Unstored(SettingsReferenceListCommandAnswerV1 answer,
        SettingsCommandMetadataV1 metadata) =>
        new SettingsReferenceListCommandResultV1(answer, false, metadata.CorrelationId);

    private static class Answers
    {
        public static SettingsReferenceListCommandAnswerV1 NoContent() =>
            new SettingsReferenceListCommandAnswerV1(204, null, "Kaydedildi.", "Değişiklik kaydedildi.");

        public static SettingsReferenceListCommandAnswerV1 Invalid(string detail) =>
            Problem(400, SettingsReferenceListProblemCodesV1.RequestInvalid, "İstek geçersiz.", detail);

        public static SettingsReferenceListCommandAnswerV1 CodeConflict() =>
            Problem(409, SettingsReferenceListProblemCodesV1.CodeConflict, "Kod kullanılıyor.",
                "Bu listede aynı kodla bir kayıt zaten var; kod değiştirilemez, mevcut kaydı güncelleyin.");

        public static SettingsReferenceListCommandAnswerV1 EntryNotFound() =>
            Problem(404, SettingsReferenceListProblemCodesV1.EntryNotFound, "Kayıt bulunamadı.",
                "Bu şirketin bu listesinde böyle bir kayıt yok.");

        public static SettingsReferenceListCommandAnswerV1 VersionConflict() =>
            Problem(409, SettingsReferenceListProblemCodesV1.VersionConflict, "Kayıt değişmiş.",
                "Kayıt siz açtıktan sonra değişti; güncel hâlini okuyup yeniden deneyin.");

        public static SettingsReferenceListCommandAnswerV1 Problem(int status, string code, string title, string detail) =>
            new SettingsReferenceListCommandAnswerV1(status, code, title, detail);
    }
}
