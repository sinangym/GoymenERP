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
/// SAT-D61…SAT-D70 (<c>SINAN_KARARLARI_2026-10-27_SATIS_EK10.md</c>, <c>…_EK11.md</c>): the Settings owner's discount limit
/// groups (create, rename, deactivate — never deleted), their limit records (a fixed percent or a linked group + extra
/// points, SAT-D62) and the person → group bindings (SAT-D63/D66).
/// <list type="bullet">
/// <item><b>Authority</b> (SAT-D65/D68/D69/D70): ONE name, <see cref="SettingsPermissionManifestV1.SalesDiscountLimitsManage"/>,
/// company-wide, measured first and re-measured (fresh, <see cref="IErpAuthorityRevalidationService"/>) right before every
/// save, in every company a command writes. One person decides: no second approval; the editor may change the limit of a
/// group they belong to and their own binding (no self-block, SAT-D70).</item>
/// <item><b>Every command</b>: a mandatory reason (≤ 4000, SAT-D68), one Idempotency-Key (the stored terminal answer
/// replays; another body under the key is 409), the record's version as decimal text, correlation/causation, and one audit
/// event per real change (none for a no-op).</item>
/// </list>
/// </summary>
internal sealed partial class SettingsSalesDiscountLimitAdministration(
    SettingsDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorizationService authorization,
    IErpAuthorityRevalidationService revalidation,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider,
    ILogger<SettingsSalesDiscountLimitAdministration> logger,
    ISettingsPersonDirectoryV1? personDirectory = null) : ISettingsSalesDiscountLimitAdministrationV1
{
    private const string Permission = SettingsPermissionManifestV1.SalesDiscountLimitsManage;
    private const int CommandExecutionSchemaVersion = 1;
    private const string GroupEntityType = "Settings.SalesDiscountLimitGroup";
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<SalesDiscountLimitGroupListV1> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        var companyId = RequireContextCompany();
        await RequireAsync(companyId, cancellationToken);
        var rows = await database.SalesDiscountLimitGroups.AsNoTracking()
            .OrderBy(group => group.Code)
            .ToListAsync(cancellationToken);
        var versions = await database.SalesDiscountLimitVersions.AsNoTracking().ToListAsync(cancellationToken);
        var data = await SalesDiscountLimitReads.LoadGroupDataAsync(database, cancellationToken);
        await RequireStillAsync(companyId, cancellationToken);

        var today = SettingsBusinessDate.Today(timeProvider);
        var latest = versions
            .GroupBy(version => version.LimitGroupId)
            .ToDictionary(grouping => grouping.Key, grouping => grouping.MaxBy(version => version.Version)!);
        var views = rows.Select(row =>
        {
            var record = latest.GetValueOrDefault(row.Id);
            var resolution = SalesDiscountLimitCalculatorV1.Resolve(data, row.Id, today);
            return new SalesDiscountLimitGroupViewV1(
                row.Id,
                row.Code,
                row.Name,
                row.IsActive,
                Text(row.Version),
                record is null ? null : DefinitionView(record),
                resolution.Outcome == SalesDiscountLimitResolutionOutcomeV1.Defined
                    ? new SalesDiscountLimitEffectiveViewV1(SalesDiscountLimitEffectiveStatesV1.Defined,
                        SalesDiscountLimitCalculatorV1.Format(resolution.Percent!.Value))
                    : new SalesDiscountLimitEffectiveViewV1(SalesDiscountLimitEffectiveStatesV1.None, null));
        }).ToArray();
        return new SalesDiscountLimitGroupListV1(companyId, today, views);
    }

    public async Task<SalesDiscountLimitPersonListV1> SearchPeopleAsync(string? query, int? limit,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireContextCompany();
        await RequireAsync(companyId, cancellationToken);
        var trimmed = query?.Trim();
        var size = limit ?? SalesDiscountLimitInputLimitsV1.PeopleSearchDefaultLimit;
        if (trimmed is { Length: > SalesDiscountLimitInputLimitsV1.PeopleSearchMaximumQueryLength }
            || size is < 1 or > SalesDiscountLimitInputLimitsV1.PeopleSearchMaximumLimit)
        {
            throw new SettingsPrerequisiteException(SalesDiscountLimitProblemCodesV1.RequestInvalid);
        }

        var directory = personDirectory
            ?? throw new SettingsPrerequisiteException(SalesDiscountLimitProblemCodesV1.ProviderUnavailable);
        var answer = await directory.SearchActiveAsync(companyId, currentUser.UserId!,
            string.IsNullOrEmpty(trimmed) ? null : trimmed, size, cancellationToken);
        await RequireStillAsync(companyId, cancellationToken);
        return answer switch
        {
            { Outcome: SettingsPersonDirectoryOutcomeV1.Found, Items: { } items } =>
                new SalesDiscountLimitPersonListV1(items, answer.HasMore),
            { Outcome: SettingsPersonDirectoryOutcomeV1.Denied } => throw new UnauthorizedAccessException(),
            _ => throw new SettingsPrerequisiteException(SalesDiscountLimitProblemCodesV1.ProviderUnavailable),
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

    /// <summary>The name, company-wide, in <paramref name="companyId"/>, which the actor must be able to access.</summary>
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

    /// <summary>The stored terminal answer for the same actor and body; anything else under the key is 409 (never stored).</summary>
    private static SettingsCommandResultV1 Replay(CommandExecution execution, string requestHash, SettingsCommandMetadataV1 metadata)
    {
        if (!string.Equals(execution.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(execution.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new SettingsCommandResultV1(Answers.Problem(409, SalesDiscountLimitProblemCodesV1.IdempotencyConflict,
                    "İşlem anahtarı kullanılmış.", "Bu Idempotency-Key başka bir istekle kullanıldı."),
                false, metadata.CorrelationId);
        }

        var answer = JsonSerializer.Deserialize<SettingsCommandAnswerV1>(execution.ResponseJson, Json);
        if (answer is null || answer.StatusCode != execution.HttpStatusCode
            || !string.Equals(Outcome(answer), execution.Outcome, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Kalıcı idempotency sonucu tanımlı Ayarlar komut sözleşmesiyle uyumsuz.");
        }

        return new SettingsCommandResultV1(answer, true, execution.OriginalCorrelationId);
    }

    /// <summary>Adds the terminal answer to the unit of work (the caller saves it with its own rows).</summary>
    private void AddExecution(SettingsDbContext target, string commandName, SettingsCommandAnswerV1 answer, string keyHash,
        string requestHash, SettingsCommandMetadataV1 metadata) =>
        target.CommandExecutions.Add(CommandExecution.Complete(new CommandExecutionData(
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

    /// <summary>
    /// Stores a refusal (no business change) as the key's terminal answer. A concurrent same-key winner is replayed instead.
    /// </summary>
    private async Task<SettingsCommandResultV1> StoreRefusalAsync(string commandName, SettingsCommandAnswerV1 answer,
        string keyHash, string requestHash, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        AddExecution(database, commandName, answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new SettingsCommandResultV1(answer, false, metadata.CorrelationId);
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
    /// Saves a business change with its terminal answer and audit in one unit (the caller already re-measured authority).
    /// A lost race on the record's version, or on a unique key, re-reads: a same-key winner replays, otherwise
    /// <paramref name="onConflict"/> gives the refusal that is then stored as the key's answer.
    /// </summary>
    private async Task<SettingsCommandResultV1> SaveChangeAsync(string commandName, SettingsCommandAnswerV1 answer,
        string keyHash, string requestHash, SettingsCommandMetadataV1 metadata,
        Func<CancellationToken, Task<SettingsCommandAnswerV1>> onConflict, CancellationToken cancellationToken)
    {
        AddExecution(database, commandName, answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new SettingsCommandResultV1(answer, false, metadata.CorrelationId);
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

    private static string Outcome(SettingsCommandAnswerV1 answer) => answer.Code ?? answer.StatusCode switch
    {
        201 => "Created",
        204 => "Succeeded",
        404 => "NotFound",
        _ => "Refused",
    };

    /// <summary>
    /// One audit event. The person's reason (SAT-D68, up to 4000 characters) is kept whole in the after-image as
    /// <c>reason</c>; the audit row's own reason column (at most 1000) carries the fixed description of the change, the
    /// Sales precedent, so nothing is ever truncated.
    /// </summary>
    private void Audit(IAuditEventCollector collector, Guid companyId, string eventType, string entityType, string entityId,
        string operation, long? versionBefore, long? versionAfter, PermissionScope scope, object? before, object after,
        string reason, SettingsCommandMetadataV1 metadata) =>
        collector.Enqueue(new AuditEventDraft(
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
        "settings.sales-discount-limit-group.created.v1" => "İskonto limit grubu oluşturuldu; gerekçe kayıt içeriğindedir",
        "settings.sales-discount-limit-group.renamed.v1" => "İskonto limit grubunun adı değiştirildi; gerekçe kayıt içeriğindedir",
        "settings.sales-discount-limit-group.deactivated.v1" => "İskonto limit grubu pasife alındı; gerekçe kayıt içeriğindedir",
        "settings.sales-discount-limit.recorded.v1" => "İskonto limit grubunun limiti kaydedildi; gerekçe kayıt içeriğindedir",
        "settings.sales-discount-limit-binding.changed.v1" => "Kişinin iskonto limit grubu bağı değiştirildi; gerekçe kayıt içeriğindedir",
        _ => throw new InvalidOperationException("Tanımsız Ayarlar audit olayı."),
    };

    private void Log(string command, SettingsCommandMetadataV1 metadata, string subject, SettingsCommandResultV1 result) =>
        logger.LogInformation(
            "Settings discount limit command completed. Command={Command} CompanyId={CompanyId} Subject={Subject} ActorId={ActorId} CorrelationId={CorrelationId} Status={Status} Code={Code} IsReplay={IsReplay}",
            command, metadata.CompanyId, subject, metadata.ActorId, metadata.CorrelationId, result.Answer.StatusCode,
            result.Answer.Code, result.IsReplay);

    private static SalesDiscountLimitDefinitionViewV1 DefinitionView(SalesDiscountLimitVersionRow record) => new SalesDiscountLimitDefinitionViewV1(
        Text(record.Version),
        record.Kind,
        record.Percent is { } percent ? SalesDiscountLimitCalculatorV1.Format(percent) : null,
        record.LinkedGroupId,
        record.ExtraPoints is { } extra ? SalesDiscountLimitCalculatorV1.Format(extra) : null,
        record.EffectiveFrom,
        record.RecordedBy,
        record.RecordedAtUtc,
        record.Reason);

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A version as decimal text: digits only, no sign, no leading zero (other than "0"), within Int64.</summary>
    private static bool TryParseVersion(string? text, long minimum, out long version)
    {
        version = 0;
        return !string.IsNullOrEmpty(text) && text.All(char.IsAsciiDigit) && (text.Length == 1 || text[0] != '0')
            && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out version) && version >= minimum;
    }

    /// <summary>SAT-D68: required, trimmed, at most 4000 characters; null when valid, else the 400 answer.</summary>
    private static SettingsCommandAnswerV1? ReasonRefusal(string? reason, out string normalized)
    {
        normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return Answers.Problem(400, SalesDiscountLimitProblemCodesV1.ReasonRequired, "Gerekçe gerekli.",
                "Bu değişiklik için gerekçe zorunludur.");
        }

        return normalized.Length > SalesDiscountLimitInputLimitsV1.ReasonMaximumLength
            ? Answers.Problem(400, SalesDiscountLimitProblemCodesV1.ReasonTooLong, "Gerekçe çok uzun.",
                $"Gerekçe en fazla {SalesDiscountLimitInputLimitsV1.ReasonMaximumLength} karakter olabilir.")
            : null;
    }

    /// <summary>Upper-case ASCII letters, digits, '_' and '-', starting with a letter or digit, 1…32 characters.</summary>
    private static bool TryNormalizeCode(string? code, out string normalized)
    {
        normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;
        var value = normalized;
        return value.Length is > 0 and <= SalesDiscountLimitInputLimitsV1.CodeMaximumLength
            && (char.IsAsciiLetterUpper(value[0]) || char.IsAsciiDigit(value[0]))
            && value.All(character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character)
                || character is '_' or '-');
    }

    private static bool TryNormalizeName(string? name, out string normalized)
    {
        normalized = name?.Trim() ?? string.Empty;
        var value = normalized;
        return value.Length is > 0 and <= SalesDiscountLimitInputLimitsV1.NameMaximumLength && !value.Any(char.IsControl);
    }

    private static SettingsCommandResultV1 Unstored(SettingsCommandAnswerV1 answer, SettingsCommandMetadataV1 metadata) =>
        new SettingsCommandResultV1(answer, false, metadata.CorrelationId);

    private static class Answers
    {
        public static SettingsCommandAnswerV1 NoContent() => new SettingsCommandAnswerV1(204, null, "Kaydedildi.", "Değişiklik kaydedildi.");

        public static SettingsCommandAnswerV1 Created(Guid id) => new SettingsCommandAnswerV1(201, null, "Oluşturuldu.", "Limit grubu oluşturuldu.",
            new SalesDiscountLimitGroupCreatedV1(id, "1"));

        public static SettingsCommandAnswerV1 NotFound() => new SettingsCommandAnswerV1(404, null, "Kayıt bulunamadı.",
            "İstenen limit grubu bu şirkette bulunamadı.");

        public static SettingsCommandAnswerV1 Invalid(string detail) =>
            Problem(400, SalesDiscountLimitProblemCodesV1.RequestInvalid, "İstek geçersiz.", detail);

        public static SettingsCommandAnswerV1 VersionConflict(Guid? companyId = null) =>
            Problem(409, SalesDiscountLimitProblemCodesV1.VersionConflict, "Kayıt değişmiş.",
                "Kayıt siz açtıktan sonra değişti; güncel hâlini okuyup yeniden deneyin.", companyId);

        public static SettingsCommandAnswerV1 GroupInactive() =>
            Problem(409, SalesDiscountLimitProblemCodesV1.GroupInactive, "Grup pasif.", "Pasif limit grubu değiştirilemez.");

        public static SettingsCommandAnswerV1 Problem(int status, string code, string title, string detail,
            Guid? companyId = null, string? groupCode = null) =>
            new SettingsCommandAnswerV1(status, code, title, detail, null, companyId, groupCode);
    }
}
