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
/// FNS-D52: the company's per-bank CSV column mappings of the bank statement import (XLSX and MT940 need none) as a
/// persistent, versioned Settings record behind <see cref="ISettingsBankCsvMappingProviderV1"/>.
/// <list type="bullet">
/// <item><b>Nothing is chosen here</b>: no mapping is compiled in or seeded for any real bank, and no field has a default
/// (not even what a positive signed amount means). The table stays empty until an authorized person records a bank's
/// mapping; until then the CSV import of that bank is refused
/// (<see cref="SettingsPrerequisiteCodesV1.BankCsvMappingNotFound"/>).</item>
/// <item><b>Authority</b>: <see cref="SettingsBankCsvMappingPermissionManifestV1.Manage"/>, company-wide, measured first
/// and re-measured fresh right before every save; granted to no role here (human gate).</item>
/// <item><b>Every command</b> (SAT-D68 precedent): one person, a mandatory reason (≤ 4000), one Idempotency-Key (the stored
/// terminal answer replays; another body under the key is 409), the bank's expected version as decimal text,
/// correlation/causation and one audit event per real change (none for a no-op).</item>
/// <item><b>Versions</b>: rows are insert-only and counted per bank; the latest version of a bank is in force. Stopping a
/// bank's mapping is a new <c>Inactive</c> version, never a delete.</item>
/// </list>
/// </summary>
internal sealed class SettingsBankCsvMappingAdministration(
    SettingsDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorizationService authorization,
    IErpAuthorityRevalidationService revalidation,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider,
    ILogger<SettingsBankCsvMappingAdministration> logger) : ISettingsBankCsvMappingAdministrationV1
{
    private const int CommandExecutionSchemaVersion = 1;
    private const string Permission = SettingsBankCsvMappingPermissionManifestV1.Manage;
    private const string EntityType = "Settings.BankCsvMapping";
    private const string RecordCommandName = "settings.bank-csv-mapping.record.v1";
    private const string RecordedEvent = "settings.bank-csv-mapping.recorded.v1";
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<BankCsvMappingsViewV1> GetAsync(CancellationToken cancellationToken = default)
    {
        var companyId = RequireContextCompany();
        await RequireAsync(companyId, cancellationToken);
        var rows = await ReadAsync(() => BankCsvMappingReads.LoadAllAsync(database, cancellationToken));
        await RequireStillAsync(companyId, cancellationToken);
        return new BankCsvMappingsViewV1(companyId, BankCsvMappingReads.ByBank(rows)
            .Select(bank => new BankCsvMappingBankViewV1(
                bank[bank.Count - 1].BankKey,
                Text(bank[bank.Count - 1].Version),
                bank.Select(View).ToArray()))
            .ToArray());
    }

    public async Task<BankCsvMappingCommandResultV1> RecordAsync(RecordBankCsvMappingRequestV1 request,
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

        if (!BankCsvMappingRulesV1.IsBankKey(request.BankKey))
        {
            return Unstored(Answers.Invalid(
                "Banka anahtarı en çok "
                + BankCsvMappingInputLimitsV1.BankKeyMaximumLength.ToString(CultureInfo.InvariantCulture)
                + " karakterli, büyük harf (A-Z), rakam, '_' veya '-' içeren ve harf ya da rakamla başlayan bir metin olmalıdır."),
                metadata);
        }

        if (!BankCsvMappingRulesV1.TryNormalizeDisplayName(request.BankDisplayName, out var displayName))
        {
            return Unstored(Answers.Invalid(
                "Banka adı zorunludur ve en çok "
                + BankCsvMappingInputLimitsV1.BankDisplayNameMaximumLength.ToString(CultureInfo.InvariantCulture)
                + " karakter olabilir."),
                metadata);
        }

        var values = Values(request);
        if (BankCsvMappingRulesV1.Refusal(values) is { } detail)
        {
            return Unstored(Answers.Invalid(detail), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var bankKey = request.BankKey!;
        var subject = companyId.ToString("D") + "/" + bankKey;
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            ExpectedVersion = expectedVersion,
            BankKey = bankKey,
            BankDisplayName = displayName,
            values.State,
            values.Delimiter,
            values.SkipLeadingRows,
            values.HasHeaderRow,
            values.DecimalSeparator,
            values.ThousandsSeparator,
            values.DateFormat,
            values.AmountLayout,
            values.SignedAmountPositiveMeans,
            values.TransactionDateColumn,
            values.ValueDateColumn,
            values.DescriptionColumn,
            values.AmountColumn,
            values.InflowColumn,
            values.OutflowColumn,
            values.BalanceColumn,
            values.ReferenceColumn,
            values.CurrencyColumn,
            Reason = reason,
        });
        if (await FindExecutionAsync(keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done(metadata, subject, Replay(existing, requestHash, metadata));
        }

        var rows = await ReadAsync(() => BankCsvMappingReads.LoadBankAsync(database, bankKey, cancellationToken));
        var current = rows.Length == 0 ? null : rows[^1];
        var latest = current?.Version ?? 0;
        if (latest != expectedVersion)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done(metadata, subject,
                await StoreRefusalAsync(Answers.VersionConflict(), keyHash, requestHash, metadata, cancellationToken));
        }

        if (current is not null
            && string.Equals(current.BankDisplayName, displayName, StringComparison.Ordinal)
            && BankCsvMappingReads.Values(current) == values)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done(metadata, subject,
                await StoreRefusalAsync(Answers.NoContent(), keyHash, requestHash, metadata, cancellationToken));
        }

        var version = checked(latest + 1);
        var row = new BankCsvMappingVersionRow
        {
            CompanyId = companyId,
            BankKey = bankKey,
            Version = version,
            ProfileId = Guid.NewGuid(),
            BankDisplayName = displayName,
            State = values.State!,
            Delimiter = values.Delimiter!,
            SkipLeadingRows = values.SkipLeadingRows!.Value,
            HasHeaderRow = values.HasHeaderRow!.Value,
            DecimalSeparator = values.DecimalSeparator!,
            ThousandsSeparator = values.ThousandsSeparator!,
            DateFormat = values.DateFormat!,
            AmountLayout = values.AmountLayout!,
            SignedAmountPositiveMeans = values.SignedAmountPositiveMeans,
            TransactionDateColumn = values.TransactionDateColumn!.Value,
            ValueDateColumn = values.ValueDateColumn,
            DescriptionColumn = values.DescriptionColumn!.Value,
            AmountColumn = values.AmountColumn,
            InflowColumn = values.InflowColumn,
            OutflowColumn = values.OutflowColumn,
            BalanceColumn = values.BalanceColumn,
            ReferenceColumn = values.ReferenceColumn,
            CurrencyColumn = values.CurrencyColumn,
            RecordedAtUtc = timeProvider.GetUtcNow(),
            RecordedBy = metadata.ActorId,
            Reason = reason,
        };
        database.BankCsvMappingVersions.Add(row);
        Audit(companyId, subject, latest == 0 ? null : latest, version, scope, new
        {
            CompanyId = companyId,
            row.BankKey,
            row.ProfileId,
            row.Version,
            row.BankDisplayName,
            row.State,
            row.Delimiter,
            row.SkipLeadingRows,
            row.HasHeaderRow,
            row.DecimalSeparator,
            row.ThousandsSeparator,
            row.DateFormat,
            row.AmountLayout,
            row.SignedAmountPositiveMeans,
            row.TransactionDateColumn,
            row.ValueDateColumn,
            row.DescriptionColumn,
            row.AmountColumn,
            row.InflowColumn,
            row.OutflowColumn,
            row.BalanceColumn,
            row.ReferenceColumn,
            row.CurrencyColumn,
        }, reason, metadata);
        await RequireStillAsync(companyId, cancellationToken);
        return Done(metadata, subject, await SaveChangeAsync(Answers.NoContent(), keyHash, requestHash, metadata,
            Answers.VersionConflict(), cancellationToken));
    }

    private static BankCsvMappingValuesV1 Values(RecordBankCsvMappingRequestV1 request) =>
        new BankCsvMappingValuesV1(
            request.State,
            request.Delimiter,
            request.SkipLeadingRows,
            request.HasHeaderRow,
            request.DecimalSeparator,
            request.ThousandsSeparator,
            request.DateFormat,
            request.AmountLayout,
            request.SignedAmountPositiveMeans,
            request.TransactionDateColumn,
            request.ValueDateColumn,
            request.DescriptionColumn,
            request.AmountColumn,
            request.InflowColumn,
            request.OutflowColumn,
            request.BalanceColumn,
            request.ReferenceColumn,
            request.CurrencyColumn);

    private static BankCsvMappingVersionViewV1 View(BankCsvMappingVersionRow row) =>
        new BankCsvMappingVersionViewV1(
            row.ProfileId,
            Text(row.Version),
            row.BankDisplayName,
            row.State,
            row.Delimiter,
            row.SkipLeadingRows,
            row.HasHeaderRow,
            row.DecimalSeparator,
            row.ThousandsSeparator,
            row.DateFormat,
            row.AmountLayout,
            row.SignedAmountPositiveMeans,
            row.TransactionDateColumn,
            row.ValueDateColumn,
            row.DescriptionColumn,
            row.AmountColumn,
            row.InflowColumn,
            row.OutflowColumn,
            row.BalanceColumn,
            row.ReferenceColumn,
            row.CurrencyColumn,
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
            throw new SettingsPrerequisiteException(BankCsvMappingProblemCodesV1.ProviderUnavailable);
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
        catch (Exception exception) when (exception is SqlException
            or InvalidOperationException { InnerException: SqlException })
        {
            // Covers direct and EF-wrapped receipt reads, including reads inside a save-race catch.
            throw new SettingsPrerequisiteException(BankCsvMappingProblemCodesV1.ProviderUnavailable);
        }
    }

    private static BankCsvMappingCommandResultV1 Replay(CommandExecution execution, string requestHash,
        SettingsCommandMetadataV1 metadata)
    {
        if (!string.Equals(execution.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(execution.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new BankCsvMappingCommandResultV1(Answers.Problem(409, BankCsvMappingProblemCodesV1.IdempotencyConflict,
                    "İşlem anahtarı kullanılmış.", "Bu Idempotency-Key başka bir istekle kullanıldı."),
                false, metadata.CorrelationId);
        }

        var answer = JsonSerializer.Deserialize<BankCsvMappingCommandAnswerV1>(execution.ResponseJson, Json);
        if (answer is null || answer.StatusCode != execution.HttpStatusCode
            || !string.Equals(Outcome(answer), execution.Outcome, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Kalıcı idempotency sonucu tanımlı banka CSV eşleme komut sözleşmesiyle uyumsuz.");
        }

        return new BankCsvMappingCommandResultV1(answer, true, execution.OriginalCorrelationId);
    }

    private void AddExecution(BankCsvMappingCommandAnswerV1 answer, string keyHash, string requestHash,
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
    private async Task<BankCsvMappingCommandResultV1> StoreRefusalAsync(BankCsvMappingCommandAnswerV1 answer,
        string keyHash, string requestHash, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        AddExecution(answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new BankCsvMappingCommandResultV1(answer, false, metadata.CorrelationId);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.GetBaseException() is SqlException { Number: 2601 or 2627 })
        {
            database.ChangeTracker.Clear();
            var winner = await FindExecutionAsync(keyHash, cancellationToken);
            if (winner is null)
            {
                throw new SettingsPrerequisiteException(BankCsvMappingProblemCodesV1.ProviderUnavailable);
            }

            return Replay(winner, requestHash, metadata);
        }
        catch (Exception exception) when (exception is DbUpdateException or SqlException
            or InvalidOperationException { InnerException: DbUpdateException or SqlException })
        {
            // A provider failure is not a terminal business refusal and never consumes the retry key.
            database.ChangeTracker.Clear();
            throw new SettingsPrerequisiteException(BankCsvMappingProblemCodesV1.ProviderUnavailable);
        }
    }

    /// <summary>
    /// Saves the new version with its terminal answer and audit in one unit. A lost race (the version primary key) re-reads:
    /// a same-key winner replays, otherwise <paramref name="onConflict"/> is stored as the key's answer.
    /// </summary>
    private async Task<BankCsvMappingCommandResultV1> SaveChangeAsync(BankCsvMappingCommandAnswerV1 answer,
        string keyHash, string requestHash, SettingsCommandMetadataV1 metadata, BankCsvMappingCommandAnswerV1 onConflict,
        CancellationToken cancellationToken)
    {
        AddExecution(answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new BankCsvMappingCommandResultV1(answer, false, metadata.CorrelationId);
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
        catch (Exception exception) when (exception is DbUpdateException or SqlException
            or InvalidOperationException { InnerException: DbUpdateException or SqlException })
        {
            // A provider failure is not a terminal business refusal and never consumes the retry key.
            database.ChangeTracker.Clear();
            throw new SettingsPrerequisiteException(BankCsvMappingProblemCodesV1.ProviderUnavailable);
        }
    }

    private static string Outcome(BankCsvMappingCommandAnswerV1 answer) => answer.Code ?? answer.StatusCode switch
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
            "Banka CSV sütun eşlemesi sürümü kaydedildi; gerekçe kayıt içeriğindedir",
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

    private BankCsvMappingCommandResultV1 Done(SettingsCommandMetadataV1 metadata, string subject,
        BankCsvMappingCommandResultV1 result)
    {
        logger.LogInformation(
            "Settings bank CSV mapping command completed. Command={Command} CompanyId={CompanyId} Subject={Subject} ActorId={ActorId} CorrelationId={CorrelationId} Status={Status} Code={Code} IsReplay={IsReplay}",
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

    private static BankCsvMappingCommandAnswerV1? ReasonRefusal(string? reason, out string normalized)
    {
        normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return Answers.Problem(400, BankCsvMappingProblemCodesV1.ReasonRequired, "Gerekçe gerekli.",
                "Bu değişiklik için gerekçe zorunludur.");
        }

        return normalized.Length > BankCsvMappingInputLimitsV1.ReasonMaximumLength
            ? Answers.Problem(400, BankCsvMappingProblemCodesV1.ReasonTooLong, "Gerekçe çok uzun.",
                $"Gerekçe en fazla {BankCsvMappingInputLimitsV1.ReasonMaximumLength.ToString(CultureInfo.InvariantCulture)} karakter olabilir.")
            : null;
    }

    private static BankCsvMappingCommandResultV1 Unstored(BankCsvMappingCommandAnswerV1 answer,
        SettingsCommandMetadataV1 metadata) =>
        new BankCsvMappingCommandResultV1(answer, false, metadata.CorrelationId);

    private static class Answers
    {
        public static BankCsvMappingCommandAnswerV1 NoContent() =>
            new BankCsvMappingCommandAnswerV1(204, null, "Kaydedildi.", "Değişiklik kaydedildi.");

        public static BankCsvMappingCommandAnswerV1 Invalid(string detail) =>
            Problem(400, BankCsvMappingProblemCodesV1.RequestInvalid, "İstek geçersiz.", detail);

        public static BankCsvMappingCommandAnswerV1 VersionConflict() =>
            Problem(409, BankCsvMappingProblemCodesV1.VersionConflict, "Kayıt değişmiş.",
                "Kayıt siz açtıktan sonra değişti; güncel hâlini okuyup yeniden deneyin.");

        public static BankCsvMappingCommandAnswerV1 Problem(int status, string code, string title, string detail) =>
            new BankCsvMappingCommandAnswerV1(status, code, title, detail);
    }
}
