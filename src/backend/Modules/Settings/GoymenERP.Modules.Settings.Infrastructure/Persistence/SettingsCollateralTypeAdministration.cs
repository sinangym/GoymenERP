using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>FNS-D31/D70/FNS-D68 and KLN-D109: explicit company catalogue, no seed, immutable versions, Audit-owned atomic writes.</summary>
internal sealed class SettingsCollateralTypeAdministration(
    SettingsDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorizationService authorization,
    IErpAuthorityRevalidationService revalidation,
    IAuditEventCollector auditEvents,
    IAuditTransactionWriterV1 auditWriter,
    TimeProvider timeProvider,
    ILogger<SettingsCollateralTypeAdministration> logger) : ISettingsCollateralTypeAdministrationV1
{
    public Task<SettingsCollateralTypeCommandResultV1> CreateAsync(CreateSettingsCollateralTypeRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default) =>
        RunAsync(() => CreateCoreAsync(request, metadata, cancellationToken));

    public Task<SettingsCollateralTypeCommandResultV1> RecordAsync(Guid entryId, RecordSettingsCollateralTypeRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default) =>
        RunAsync(() => RecordCoreAsync(entryId, request, metadata, cancellationToken));

    private async Task<SettingsCollateralTypeCommandResultV1> RunAsync(Func<Task<SettingsCollateralTypeCommandResultV1>> command)
    {
        try { return await command(); }
        catch (Exception exception) when (exception is not OperationCanceledException and not UnauthorizedAccessException
            and not SettingsPrerequisiteException)
        { throw new SettingsPrerequisiteException(SettingsCollateralTypeProblemCodesV1.ProviderUnavailable); }
        finally { auditWriter.DiscardPending(); database.ChangeTracker.Clear(); }
    }

    private const int CommandExecutionSchemaVersion = 1;
    private const string EntityType = "Settings.CollateralType";
    private const string CreateCommandName = "settings.collateral-type.create.v1";
    private const string RecordCommandName = "settings.collateral-type.record.v1";
    private const string CreatedEvent = "settings.collateral-type.created.v1";
    private const string RecordedEvent = "settings.collateral-type.recorded.v1";
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<SettingsCollateralTypesViewV1> GetAsync(CancellationToken cancellationToken = default)
    {
        const string permission = SettingsCollateralTypePermissionsV1.Manage;
        var companyId = RequireContextCompany();
        await RequireAsync(permission, companyId, cancellationToken);
        var snapshots = await ReadAsync(() => CollateralTypeReads.LoadAsync(database, cancellationToken));
        await RequireStillAsync(permission, companyId, cancellationToken);
        var entries = snapshots.Select(snapshot =>
        {
            var latest = Latest(snapshot);
            return new SettingsCollateralTypeViewV1(snapshot.Entry.EntryId, snapshot.Entry.Code, latest.Name,
                Enum.Parse<SettingsCollateralKindV1>(snapshot.Entry.Kind), latest.Active, Rate(latest), Text(latest.Version), latest.RecordedBy, latest.RecordedAtUtc);
        }).ToArray();
        return new SettingsCollateralTypesViewV1(companyId, entries);
    }

    private async Task<SettingsCollateralTypeCommandResultV1> CreateCoreAsync(
        CreateSettingsCollateralTypeRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        const string kindCode = "CollateralType";
        const string permission = SettingsCollateralTypePermissionsV1.Manage;
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

        if (!CollateralTypeRulesV1.IsKind(request.Kind)) return Unstored(Answers.Invalid("Onaylı bir teminat sınıfı gereklidir; çek/senet teminat değildir."), metadata);

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var code = request.Code!;
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new { Kind = request.Kind, Code = code, Name = name, Reason = reason });
        if (await FindExecutionAsync(CreateCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(permission, companyId, cancellationToken);
            return Done("create", metadata, kindCode, null, Replay(existing, requestHash, metadata));
        }

        if (await ReadAsync(() => CollateralTypeReads.CodeExistsAsync(database, code, cancellationToken)))
        {
            await RequireStillAsync(permission, companyId, cancellationToken);
            return Done("create", metadata, kindCode, null, await StoreRefusalAsync(CreateCommandName, Answers.CodeConflict(),
                keyHash, requestHash, metadata, cancellationToken));
        }

        var now = timeProvider.GetUtcNow();
        var entry = new CollateralTypeRow
        {
            CompanyId = companyId,
            EntryId = Guid.NewGuid(),
            Kind = request.Kind!.Value.ToString(),
            Code = code,
            CreatedAtUtc = now,
            CreatedBy = metadata.ActorId,
        };
        var version = new CollateralTypeVersionRow
        {
            CompanyId = companyId,
            EntryId = entry.EntryId,
            Version = 1,
            Name = name,
            Active = true,
            RatePercent = "100",
            RecordedAtUtc = now,
            RecordedBy = metadata.ActorId,
            Reason = reason,
        };
        database.CollateralTypes.Add(entry);
        database.CollateralTypeVersions.Add(version);
        Audit(CreatedEvent, "Create", companyId, entry.EntryId, null, version.Version, permission, scope, "{}",
            Image(entry, version), reason, "Teminat türü kaydı oluşturuldu; gerekçe kayıt içeriğindedir", metadata);
        await RequireStillAsync(permission, companyId, cancellationToken);
        return Done("create", metadata, kindCode, entry.EntryId, await SaveChangeAsync(CreateCommandName,
            Answers.NoContent(), keyHash, requestHash, metadata, Answers.CodeConflict(), cancellationToken));
    }

    private async Task<SettingsCollateralTypeCommandResultV1> RecordCoreAsync(Guid entryId,
        RecordSettingsCollateralTypeRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        const string kindCode = "CollateralType";
        const string permission = SettingsCollateralTypePermissionsV1.Manage;
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

        if (!CollateralTypeRulesV1.TryRate(request.RatePercent, out var rate)) return Unstored(Answers.Invalid("Oran 0–100 arasında noktalı ondalık metin olmalıdır."), metadata);

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
            RatePercent = rate,
            Reason = reason,
        });
        if (await FindExecutionAsync(RecordCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(permission, companyId, cancellationToken);
            return Done("record", metadata, kindCode, entryId, Replay(existing, requestHash, metadata));
        }

        var snapshot = await ReadAsync(() => CollateralTypeReads.FindAsync(database, entryId, cancellationToken));
        if (snapshot is null)
        {
            await RequireStillAsync(permission, companyId, cancellationToken);
            return Done("record", metadata, kindCode, entryId, await StoreRefusalAsync(RecordCommandName,
                Answers.EntryNotFound(), keyHash, requestHash, metadata, cancellationToken));
        }

        var current = Latest(snapshot);
        if (current.Version != expectedVersion)
        {
            await RequireStillAsync(permission, companyId, cancellationToken);
            return Done("record", metadata, kindCode, entryId, await StoreRefusalAsync(RecordCommandName,
                Answers.VersionConflict(), keyHash, requestHash, metadata, cancellationToken));
        }

        if (current.Active == active && string.Equals(current.Name, name, StringComparison.Ordinal) && Rate(current) == rate)
        {
            await RequireStillAsync(permission, companyId, cancellationToken);
            return Done("record", metadata, kindCode, entryId, await StoreRefusalAsync(RecordCommandName,
                Answers.NoContent(), keyHash, requestHash, metadata, cancellationToken));
        }

        var next = new CollateralTypeVersionRow
        {
            CompanyId = companyId,
            EntryId = entryId,
            Version = checked(current.Version + 1),
            Name = name,
            Active = active,
            RatePercent = rate,
            RecordedAtUtc = timeProvider.GetUtcNow(),
            RecordedBy = metadata.ActorId,
            Reason = reason,
        };
        database.CollateralTypeVersions.Add(next);
        Audit(RecordedEvent, "RecordVersion", companyId, entryId, current.Version, next.Version, permission, scope,
            JsonSerializer.Serialize(Image(snapshot.Entry, current), Json), Image(snapshot.Entry, next), reason,
            "Teminat türü kaydı sürümü kaydedildi; gerekçe kayıt içeriğindedir", metadata);
        await RequireStillAsync(permission, companyId, cancellationToken);
        return Done("record", metadata, kindCode, entryId, await SaveChangeAsync(RecordCommandName, Answers.NoContent(),
            keyHash, requestHash, metadata, Answers.VersionConflict(), cancellationToken));
    }

    private static object Image(CollateralTypeRow entry, CollateralTypeVersionRow version) => new
    {
        entry.EntryId,
        entry.Kind,
        entry.Code,
        version.Version,
        version.Name,
        version.Active,
        version.RatePercent,
    };

    /// <summary>The latest version of a stored entry; an entry without a well-formed version is malformed data.</summary>
    private static CollateralTypeVersionRow Latest(CollateralTypeSnapshot snapshot) =>
        snapshot.Latest is { Version: > 0 } latest && ReferenceListRulesV1.IsStoredName(latest.Name)
            && ReferenceListRulesV1.IsCode(snapshot.Entry.Code)
            && Enum.TryParse<SettingsCollateralKindV1>(snapshot.Entry.Kind, out var kind) && CollateralTypeRulesV1.IsKind(kind)
            && CollateralTypeRulesV1.TryRate(latest.RatePercent, out var rate)
            ? latest
            : throw new SettingsPrerequisiteException(SettingsCollateralTypeProblemCodesV1.ProfileInvalid);

    private static string Rate(CollateralTypeVersionRow row) => CollateralTypeRulesV1.TryRate(row.RatePercent, out var rate)
        ? rate : throw new SettingsPrerequisiteException(SettingsCollateralTypeProblemCodesV1.ProfileInvalid);

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

    private async Task RequireReplayStillAsync(SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        RequireOriginalIdentity();
        await RequireStillAsync(SettingsCollateralTypePermissionsV1.Manage, metadata.CompanyId, cancellationToken);
        RequireOriginalIdentity();

        void RequireOriginalIdentity()
        {
            if (!currentUser.IsAuthenticated || !companyContext.IsResolved || companyContext.CompanyId != metadata.CompanyId
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
            throw new SettingsPrerequisiteException(SettingsCollateralTypeProblemCodesV1.ProviderUnavailable);
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

    private static SettingsCollateralTypeCommandResultV1 Replay(CommandExecution execution, string requestHash,
        SettingsCommandMetadataV1 metadata)
    {
        if (!string.Equals(execution.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(execution.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new SettingsCollateralTypeCommandResultV1(Answers.Problem(409,
                    SettingsCollateralTypeProblemCodesV1.IdempotencyConflict,
                    "İşlem anahtarı kullanılmış.", "Bu Idempotency-Key başka bir istekle kullanıldı."),
                false, metadata.CorrelationId);
        }

        var answer = JsonSerializer.Deserialize<SettingsCollateralTypeCommandAnswerV1>(execution.ResponseJson, Json);
        if (answer is null || answer.StatusCode != execution.HttpStatusCode
            || !string.Equals(Outcome(answer), execution.Outcome, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Kalıcı idempotency sonucu teminat türü komut sözleşmesiyle uyumsuz.");
        }

        return new SettingsCollateralTypeCommandResultV1(answer, true, execution.OriginalCorrelationId);
    }

    private void AddExecution(string commandName, SettingsCollateralTypeCommandAnswerV1 answer, string keyHash,
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
    private async Task<SettingsCollateralTypeCommandResultV1> StoreRefusalAsync(string commandName,
        SettingsCollateralTypeCommandAnswerV1 answer, string keyHash, string requestHash, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        AddExecution(commandName, answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new SettingsCollateralTypeCommandResultV1(answer, false, metadata.CorrelationId);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            var winner = await FindExecutionAsync(commandName, keyHash, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            await RequireReplayStillAsync(metadata, cancellationToken);
            return Replay(winner, requestHash, metadata);
        }
    }

    // The owner writes FIRST: the legacy Settings interceptor sees an empty collector and cannot attach Audit rows here.
    private async Task<SettingsCollateralTypeCommandResultV1> SaveChangeAsync(string commandName,
        SettingsCollateralTypeCommandAnswerV1 answer, string keyHash, string requestHash, SettingsCommandMetadataV1 metadata,
        SettingsCollateralTypeCommandAnswerV1 onConflict, CancellationToken cancellationToken)
    {
        AddExecution(commandName, answer, keyHash, requestHash, metadata);
        var businessConflict = false;
        try
        {
            await using (var transaction = await database.Database.BeginTransactionAsync(cancellationToken))
            {
                await auditWriter.WritePendingAsync(transaction.GetDbTransaction(), cancellationToken);
                await RequireStillAsync(SettingsCollateralTypePermissionsV1.Manage, metadata.CompanyId, cancellationToken);
                try { await database.SaveChangesAsync(cancellationToken); }
                catch (DbUpdateException exception) when (exception.GetBaseException() is SqlException { Number: 2601 or 2627 })
                { businessConflict = true; }
                if (businessConflict) await transaction.RollbackAsync(CancellationToken.None);
                else await transaction.CommitAsync(cancellationToken);
            }
            if (!businessConflict) return new SettingsCollateralTypeCommandResultV1(answer, false, metadata.CorrelationId);
            database.ChangeTracker.Clear();
            var winner = await FindExecutionAsync(commandName, keyHash, cancellationToken);
            if (winner is not null)
            {
                await RequireReplayStillAsync(metadata, cancellationToken);
                return Replay(winner, requestHash, metadata);
            }
            await RequireStillAsync(SettingsCollateralTypePermissionsV1.Manage, metadata.CompanyId, cancellationToken);
            return await StoreRefusalAsync(commandName, onConflict, keyHash, requestHash, metadata, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not UnauthorizedAccessException
            and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException(SettingsCollateralTypeProblemCodesV1.ProviderUnavailable);
        }
    }

    private static string Outcome(SettingsCollateralTypeCommandAnswerV1 answer) => answer.Code ?? answer.StatusCode switch
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

    private SettingsCollateralTypeCommandResultV1 Done(string command, SettingsCommandMetadataV1 metadata, string kindCode,
        Guid? entryId, SettingsCollateralTypeCommandResultV1 result)
    {
        logger.LogInformation(
            "Settings collateral type command completed. Command={Command} Kind={Kind} CompanyId={CompanyId} EntryId={EntryId} ActorId={ActorId} CorrelationId={CorrelationId} Status={Status} Code={Code} IsReplay={IsReplay}",
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

    private static SettingsCollateralTypeCommandAnswerV1 NameInvalid() =>
        Answers.Invalid(
            $"Ad zorunludur: boşluklar kırpıldıktan sonra 1-{Text(SettingsReferenceListInputLimitsV1.NameMaximumLength)} karakter, kontrol karakteri içermez.");

    private static SettingsCollateralTypeCommandAnswerV1? ReasonRefusal(string? reason, out string normalized)
    {
        normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return Answers.Problem(400, SettingsCollateralTypeProblemCodesV1.ReasonRequired, "Gerekçe gerekli.",
                "Bu değişiklik için gerekçe zorunludur.");
        }

        return normalized.Length > SettingsReferenceListInputLimitsV1.ReasonMaximumLength
            ? Answers.Problem(400, SettingsCollateralTypeProblemCodesV1.ReasonTooLong, "Gerekçe çok uzun.",
                $"Gerekçe en fazla {Text(SettingsReferenceListInputLimitsV1.ReasonMaximumLength)} karakter olabilir.")
            : null;
    }

    private static SettingsCollateralTypeCommandResultV1 Unstored(SettingsCollateralTypeCommandAnswerV1 answer,
        SettingsCommandMetadataV1 metadata) =>
        new SettingsCollateralTypeCommandResultV1(answer, false, metadata.CorrelationId);

    private static class Answers
    {
        public static SettingsCollateralTypeCommandAnswerV1 NoContent() =>
            new SettingsCollateralTypeCommandAnswerV1(204, null, "Kaydedildi.", "Değişiklik kaydedildi.");

        public static SettingsCollateralTypeCommandAnswerV1 Invalid(string detail) =>
            Problem(400, SettingsCollateralTypeProblemCodesV1.RequestInvalid, "İstek geçersiz.", detail);

        public static SettingsCollateralTypeCommandAnswerV1 CodeConflict() =>
            Problem(409, SettingsCollateralTypeProblemCodesV1.CodeConflict, "Kod kullanılıyor.",
                "Bu listede aynı kodla bir kayıt zaten var; kod değiştirilemez, mevcut kaydı güncelleyin.");

        public static SettingsCollateralTypeCommandAnswerV1 EntryNotFound() =>
            Problem(404, SettingsCollateralTypeProblemCodesV1.EntryNotFound, "Kayıt bulunamadı.",
                "Bu şirketin bu listesinde böyle bir kayıt yok.");

        public static SettingsCollateralTypeCommandAnswerV1 VersionConflict() =>
            Problem(409, SettingsCollateralTypeProblemCodesV1.VersionConflict, "Kayıt değişmiş.",
                "Kayıt siz açtıktan sonra değişti; güncel hâlini okuyup yeniden deneyin.");

        public static SettingsCollateralTypeCommandAnswerV1 Problem(int status, string code, string title, string detail) =>
            new SettingsCollateralTypeCommandAnswerV1(status, code, title, detail);
    }
}
