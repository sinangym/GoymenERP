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
/// F-029 S1/S2 (URUN_F029 spec §5.4(4); URN-D2…D5): the company's unit profiles — which unit the company may use, from when
/// and at what quantity scale — as a persistent, versioned Settings record behind the unchanged
/// <see cref="ISettingsPrerequisiteProvider.GetUnitProfileAsync"/> port (<see cref="SettingsUnitProfilePrerequisiteProvider"/>).
/// <list type="bullet">
/// <item><b>Two sources, one switch</b> (URN-D5): until the server-resolved company's one-time baseline transfer the reviewed
/// <see cref="ApprovedUnitProfileCatalog"/> answers every read; the transfer copies the company's approved entries unchanged
/// (same profile ids, versions, scales, activity and start dates; <c>CompiledBaselineV0</c> with the catalog source text) and
/// the Settings table is the only source from then on. Nothing is seeded and no value is invented.</item>
/// <item><b>Unit identity</b> (S2): a profile may be recorded only for a unit the Settings unit identity catalog knows; its
/// code, dimension and kilogram flag are copied from that identity, never typed. The identity catalog itself stays the
/// approved, company-independent source (spec P2: its port does not change).</item>
/// <item><b>Authority</b> (URN-D3): <see cref="SettingsUnitProfilePermissionManifestV1.Manage"/>, company-wide, measured first
/// and re-measured fresh right before every save; one person decides, no second approver; granted to no role here.</item>
/// <item><b>Every command</b>: a mandatory reason (≤ 4000), one Idempotency-Key (the stored terminal answer replays; another
/// body under the key is 409), the unit's expected version as decimal text, correlation/causation and one audit event per
/// real change (none for a no-op).</item>
/// <item><b>Dates</b> (URN-D4): a version takes effect on its business date (omitted = today, Europe/Istanbul); a date before
/// today is refused, so a past window and every document already written against it never change.</item>
/// <item><b>Bounds</b>: the scale is 0…<see cref="UnitProfileInputLimitsV1.MaximumScale"/> (the decimal type's own limit) and
/// a kilogram profile at most <see cref="UnitProfileInputLimitsV1.KilogramMaximumScale"/> (Ö2); the table checks repeat both.</item>
/// </list>
/// </summary>
internal sealed class SettingsUnitProfileAdministration(
    SettingsDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorizationService authorization,
    IErpAuthorityRevalidationService revalidation,
    IAuditEventCollector auditEvents,
    ISettingsUnitIdentityCatalogV1 unitIdentities,
    TimeProvider timeProvider,
    ILogger<SettingsUnitProfileAdministration> logger) : ISettingsUnitProfileAdministrationV1
{
    private const int CommandExecutionSchemaVersion = 1;
    private const string Permission = SettingsUnitProfilePermissionManifestV1.Manage;
    private const string EntityType = "Settings.UnitProfile";
    private const string BaselineCommandName = "settings.unit-profile.baseline-transfer.v1";
    private const string RecordCommandName = "settings.unit-profile.record.v1";
    private const string BaselineEvent = "settings.unit-profile.baseline-transferred.v1";
    private const string RecordedEvent = "settings.unit-profile.recorded.v1";
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    /// <summary>The reviewed catalog the one-time transfer copies (test seam: Settings' own tests may give another).</summary>
    internal ApprovedUnitProfileCatalog Catalog { get; init; } = ApprovedUnitProfileCatalog.Approved;

    public async Task<UnitProfileViewV1> GetAsync(CancellationToken cancellationToken = default)
    {
        var companyId = RequireContextCompany();
        await RequireAsync(companyId, cancellationToken);
        var (transferred, rows) = await ReadAsync(async () => (
            await UnitProfileReads.IsTransferredAsync(database, cancellationToken),
            await UnitProfileReads.LoadAsync(database, cancellationToken)));
        var identities = await ReadAsync(() => unitIdentities.ListAsync(cancellationToken));
        await RequireStillAsync(companyId, cancellationToken);
        var today = SettingsBusinessDate.Today(timeProvider);
        var options = identities
            .Select(identity => new UnitProfileUnitOptionV1(identity.UnitId, identity.Code, identity.DimensionCode,
                identity.IsKilogram, MaximumScale(identity.IsKilogram)))
            .ToArray();
        if (!transferred)
        {
            var baseline = Catalog.Entries
                .Where(entry => entry.CompanyId == companyId)
                .GroupBy(entry => entry.UnitId)
                .Select(unit =>
                {
                    var ordered = unit.OrderBy(entry => entry.EffectiveFrom).ThenBy(entry => entry.Version).ToArray();
                    var first = ordered[0];
                    return new UnitProfileUnitViewV1(first.UnitId, first.UnitCode, first.DimensionCode, first.IsKilogram,
                        Text(0),
                        ordered.Select(entry => new UnitProfileVersionViewV1(
                            entry.ProfileId,
                            Text(entry.Version),
                            entry.Scale,
                            entry.IsActive,
                            entry.EffectiveFrom,
                            entry.EffectiveThrough,
                            CatalogState(entry.EffectiveFrom, entry.EffectiveThrough, today),
                            SalesPolicySourceKindsV1.CompiledBaselineV0,
                            entry.Source,
                            RecordedBy: null,
                            RecordedAtUtc: null,
                            Reason: null)).ToArray());
                })
                .OrderBy(unit => unit.UnitCode, StringComparer.Ordinal)
                .ToArray();
            return new UnitProfileViewV1(companyId, today, false, baseline, options);
        }

        var units = rows
            .GroupBy(row => row.UnitId)
            .Select(unit =>
            {
                var timeline = UnitProfileReads.Timeline(unit);
                var latest = unit.MaxBy(row => row.Version)!;
                return new UnitProfileUnitViewV1(latest.UnitId, latest.UnitCode, latest.DimensionCode, latest.IsKilogram,
                    Text(SalesPolicyTimelineV1.LatestVersion(timeline)),
                    timeline.Select(entry => View(entry, today)).ToArray());
            })
            .OrderBy(unit => unit.UnitCode, StringComparer.Ordinal)
            .ToArray();
        return new UnitProfileViewV1(companyId, today, true, units, options);
    }

    public async Task<UnitProfileCommandResultV1> TransferBaselineAsync(TransferUnitProfileBaselineRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(companyId, cancellationToken);
        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var subject = companyId.ToString("D");
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new { Reason = reason });
        if (await FindExecutionAsync(BaselineCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("baseline", metadata, subject, Replay(existing, requestHash, metadata));
        }

        var transferred = await ReadAsync(() => UnitProfileReads.IsTransferredAsync(database, cancellationToken));
        if (transferred)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("baseline", metadata, subject, await StoreRefusalAsync(BaselineCommandName,
                Answers.AlreadyTransferred(), keyHash, requestHash, metadata, cancellationToken));
        }

        var entries = Catalog.Entries
            .Where(entry => entry.CompanyId == companyId)
            .OrderBy(entry => entry.UnitId)
            .ThenBy(entry => entry.EffectiveFrom)
            .ToArray();
        foreach (var unit in entries.GroupBy(entry => entry.UnitId))
        {
            RequireChainedWindows(unit.OrderBy(entry => entry.EffectiveFrom).ToArray());
        }

        if (entries.Select(entry => entry.ProfileId).Distinct().Count() != entries.Length
            || entries.Any(entry => !FitsScale(entry.Scale, entry.IsKilogram)))
        {
            throw new SettingsPrerequisiteException("settings_profile_invalid");
        }

        var now = timeProvider.GetUtcNow();
        foreach (var entry in entries)
        {
            database.UnitProfileVersions.Add(new UnitProfileVersionRow
            {
                CompanyId = companyId,
                UnitId = entry.UnitId,
                Version = entry.Version,
                ProfileId = entry.ProfileId,
                UnitCode = entry.UnitCode,
                DimensionCode = entry.DimensionCode,
                IsKilogram = entry.IsKilogram,
                Scale = entry.Scale,
                IsActive = entry.IsActive,
                EffectiveFrom = entry.EffectiveFrom,
                SourceKind = SalesPolicySourceKindsV1.CompiledBaselineV0,
                DecisionReference = entry.Source,
                RecordedAtUtc = now,
                RecordedBy = metadata.ActorId,
                Reason = reason,
            });
        }

        database.UnitProfileBaselineTransfers.Add(new UnitProfileBaselineTransferRow
        {
            CompanyId = companyId,
            EntryCount = entries.Length,
            TransferredAtUtc = now,
            TransferredBy = metadata.ActorId,
            Reason = reason,
        });
        Audit(BaselineEvent, "TransferBaseline", companyId, subject, null,
            entries.Length == 0 ? null : entries.Max(entry => entry.Version), scope, new
            {
                CompanyId = companyId,
                EntryCount = entries.Length,
                Versions = entries.Select(entry => new
                {
                    entry.UnitId,
                    entry.UnitCode,
                    entry.DimensionCode,
                    entry.IsKilogram,
                    entry.ProfileId,
                    entry.Version,
                    entry.Scale,
                    entry.IsActive,
                    entry.EffectiveFrom,
                    entry.EffectiveThrough,
                    DecisionReference = entry.Source,
                }).ToArray(),
            }, reason, "Birim profili başlangıç aktarımı yapıldı; gerekçe kayıt içeriğindedir", metadata);
        await RequireStillAsync(companyId, cancellationToken);
        return Done("baseline", metadata, subject, await SaveChangeAsync(BaselineCommandName, Answers.NoContent(), keyHash,
            requestHash, metadata, Answers.AlreadyTransferred(), cancellationToken));
    }

    public async Task<UnitProfileCommandResultV1> RecordAsync(RecordUnitProfileRequestV1 request,
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

        if (request.UnitId is not { } unitId || unitId == Guid.Empty)
        {
            return Unstored(Answers.Invalid("Birim seçilmelidir."), metadata);
        }

        var identity = await ReadAsync(() => unitIdentities.ReadAsync(unitId, cancellationToken));
        if (identity is null)
        {
            return Unstored(Answers.Problem(400, UnitProfileProblemCodesV1.UnitUnknown, "Birim tanımsız.",
                "Seçilen birim Ayarlar birim kimlikleri arasında yok."), metadata);
        }

        if (request.Scale is not { } scale || !FitsScale(scale, identity.IsKilogram))
        {
            return Unstored(Answers.Invalid(
                $"Ondalık basamak sayısı 0 ile {MaximumScale(identity.IsKilogram).ToString(CultureInfo.InvariantCulture)} arasında tam sayı olmalıdır."),
                metadata);
        }

        if (request.IsActive is not { } isActive)
        {
            return Unstored(Answers.Invalid("Birimin kullanımda olup olmadığı belirtilmelidir."), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var subject = companyId.ToString("D") + ":" + unitId.ToString("D");
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            ExpectedVersion = expectedVersion,
            UnitId = unitId,
            Scale = scale,
            IsActive = isActive,
            Reason = reason,
            request.EffectiveFrom,
        });
        if (await FindExecutionAsync(RecordCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("record", metadata, subject, Replay(existing, requestHash, metadata));
        }

        var (transferred, rows) = await ReadAsync(async () => (
            await UnitProfileReads.IsTransferredAsync(database, cancellationToken),
            await UnitProfileReads.LoadUnitAsync(database, unitId, cancellationToken)));
        var timeline = UnitProfileReads.Timeline(rows);
        var latest = SalesPolicyTimelineV1.LatestVersion(timeline);
        var today = SettingsBusinessDate.Today(timeProvider);
        var effectiveFrom = request.EffectiveFrom ?? today;
        var refusal = !transferred ? Answers.BaselineRequired()
            : latest != expectedVersion ? Answers.VersionConflict()
            : effectiveFrom < today ? Answers.EffectiveFromPast()
            : null;
        if (refusal is not null)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("record", metadata, subject,
                await StoreRefusalAsync(RecordCommandName, refusal, keyHash, requestHash, metadata, cancellationToken));
        }

        var inForce = SalesPolicyTimelineV1.InForceOn(timeline, effectiveFrom);
        if (inForce is { Value: var current } && current.Scale == scale && current.IsActive == isActive)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("record", metadata, subject, await StoreRefusalAsync(RecordCommandName, Answers.NoContent(), keyHash,
                requestHash, metadata, cancellationToken));
        }

        if (SettingsBusinessDate.Today(timeProvider) != today)
        {
            return Done("record", metadata, subject, Unstored(Answers.VersionConflict(), metadata));
        }

        var version = checked(latest + 1);
        var row = new UnitProfileVersionRow
        {
            CompanyId = companyId,
            UnitId = unitId,
            Version = version,
            ProfileId = Guid.NewGuid(),
            UnitCode = identity.Code,
            DimensionCode = identity.DimensionCode,
            IsKilogram = identity.IsKilogram,
            Scale = scale,
            IsActive = isActive,
            EffectiveFrom = effectiveFrom,
            SourceKind = SalesPolicySourceKindsV1.UserMaintained,
            DecisionReference = null,
            RecordedAtUtc = timeProvider.GetUtcNow(),
            RecordedBy = metadata.ActorId,
            Reason = reason,
        };
        database.UnitProfileVersions.Add(row);
        Audit(RecordedEvent, "RecordVersion", companyId, subject, latest == 0 ? null : latest, version, scope, new
        {
            CompanyId = companyId,
            row.UnitId,
            row.UnitCode,
            row.DimensionCode,
            row.IsKilogram,
            row.ProfileId,
            row.Version,
            row.Scale,
            row.IsActive,
            row.EffectiveFrom,
            PreviousScale = inForce?.Value.Scale,
            PreviousIsActive = inForce?.Value.IsActive,
            SupersedesVersion = timeline
                .Where(entry => !entry.IsSuperseded && entry.EffectiveFrom == effectiveFrom)
                .Select(entry => (long?)entry.Version)
                .SingleOrDefault(),
        }, reason, "Birim profili sürümü kaydedildi; gerekçe kayıt içeriğindedir", metadata);
        await RequireStillAsync(companyId, cancellationToken);
        return Done("record", metadata, subject, await SaveChangeAsync(RecordCommandName, Answers.NoContent(), keyHash,
            requestHash, metadata, Answers.VersionConflict(), cancellationToken));
    }

    private static int MaximumScale(bool isKilogram) =>
        isKilogram ? UnitProfileInputLimitsV1.KilogramMaximumScale : UnitProfileInputLimitsV1.MaximumScale;

    private static bool FitsScale(int scale, bool isKilogram) => scale >= 0 && scale <= MaximumScale(isKilogram);

    private static string CatalogState(DateOnly from, DateOnly? through, DateOnly today) =>
        from > today ? SalesPolicyVersionStatesV1.Scheduled
        : through is { } end && end < today ? SalesPolicyVersionStatesV1.Ended
        : SalesPolicyVersionStatesV1.InForce;

    /// <summary>
    /// One unit's catalog versions must chain without gap or overlap (each ends the day before the next starts, the last is
    /// open) — the only shape the stored timeline reproduces byte-identically — and each source must fit the column.
    /// </summary>
    private static void RequireChainedWindows(IReadOnlyList<ApprovedUnitProfileEntry> ordered)
    {
        for (var index = 0; index < ordered.Count; index++)
        {
            var expected = index + 1 < ordered.Count ? ordered[index + 1].EffectiveFrom.AddDays(-1) : (DateOnly?)null;
            if (ordered[index].EffectiveThrough != expected
                || ordered[index].Source.Length > UnitProfileInputLimitsV1.DecisionReferenceMaximumLength)
            {
                throw new SettingsPrerequisiteException("settings_profile_invalid");
            }
        }
    }

    private static UnitProfileVersionViewV1 View(SalesPolicyTimelineEntryV1<UnitProfileVersionRow> entry, DateOnly today) =>
        new UnitProfileVersionViewV1(
            entry.Value.ProfileId,
            Text(entry.Version),
            entry.Value.Scale,
            entry.Value.IsActive,
            entry.EffectiveFrom,
            entry.EffectiveThrough,
            SalesPolicyTimelineV1.State(entry, today),
            entry.Value.SourceKind,
            entry.Value.DecisionReference,
            entry.Value.RecordedBy,
            entry.Value.RecordedAtUtc,
            entry.Value.Reason);

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
            throw new SettingsPrerequisiteException(UnitProfileProblemCodesV1.ProviderUnavailable);
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

    private static UnitProfileCommandResultV1 Replay(CommandExecution execution, string requestHash,
        SettingsCommandMetadataV1 metadata)
    {
        if (!string.Equals(execution.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(execution.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new UnitProfileCommandResultV1(Answers.Problem(409, UnitProfileProblemCodesV1.IdempotencyConflict,
                    "İşlem anahtarı kullanılmış.", "Bu Idempotency-Key başka bir istekle kullanıldı."),
                false, metadata.CorrelationId);
        }

        var answer = JsonSerializer.Deserialize<UnitProfileCommandAnswerV1>(execution.ResponseJson, Json);
        if (answer is null || answer.StatusCode != execution.HttpStatusCode
            || !string.Equals(Outcome(answer), execution.Outcome, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Kalıcı idempotency sonucu tanımlı birim profili komut sözleşmesiyle uyumsuz.");
        }

        return new UnitProfileCommandResultV1(answer, true, execution.OriginalCorrelationId);
    }

    private void AddExecution(string commandName, UnitProfileCommandAnswerV1 answer, string keyHash, string requestHash,
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
    private async Task<UnitProfileCommandResultV1> StoreRefusalAsync(string commandName, UnitProfileCommandAnswerV1 answer,
        string keyHash, string requestHash, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        AddExecution(commandName, answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new UnitProfileCommandResultV1(answer, false, metadata.CorrelationId);
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
    /// Saves the change with its terminal answer and audit in one unit. A lost race (the version primary key, or the transfer
    /// marker's company key) re-reads: a same-key winner replays, otherwise <paramref name="onConflict"/> is stored as the
    /// key's answer.
    /// </summary>
    private async Task<UnitProfileCommandResultV1> SaveChangeAsync(string commandName, UnitProfileCommandAnswerV1 answer,
        string keyHash, string requestHash, SettingsCommandMetadataV1 metadata, UnitProfileCommandAnswerV1 onConflict,
        CancellationToken cancellationToken)
    {
        AddExecution(commandName, answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new UnitProfileCommandResultV1(answer, false, metadata.CorrelationId);
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

    private static string Outcome(UnitProfileCommandAnswerV1 answer) => answer.Code ?? answer.StatusCode switch
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

    private UnitProfileCommandResultV1 Done(string command, SettingsCommandMetadataV1 metadata, string subject,
        UnitProfileCommandResultV1 result)
    {
        logger.LogInformation(
            "Settings unit profile command completed. Command={Command} CompanyId={CompanyId} Subject={Subject} ActorId={ActorId} CorrelationId={CorrelationId} Status={Status} Code={Code} IsReplay={IsReplay}",
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

    private static UnitProfileCommandAnswerV1? ReasonRefusal(string? reason, out string normalized)
    {
        normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return Answers.Problem(400, UnitProfileProblemCodesV1.ReasonRequired, "Gerekçe gerekli.",
                "Bu değişiklik için gerekçe zorunludur.");
        }

        return normalized.Length > UnitProfileInputLimitsV1.ReasonMaximumLength
            ? Answers.Problem(400, UnitProfileProblemCodesV1.ReasonTooLong, "Gerekçe çok uzun.",
                $"Gerekçe en fazla {UnitProfileInputLimitsV1.ReasonMaximumLength.ToString(CultureInfo.InvariantCulture)} karakter olabilir.")
            : null;
    }

    private static UnitProfileCommandResultV1 Unstored(UnitProfileCommandAnswerV1 answer, SettingsCommandMetadataV1 metadata) =>
        new UnitProfileCommandResultV1(answer, false, metadata.CorrelationId);

    private static class Answers
    {
        public static UnitProfileCommandAnswerV1 NoContent() =>
            new UnitProfileCommandAnswerV1(204, null, "Kaydedildi.", "Değişiklik kaydedildi.");

        public static UnitProfileCommandAnswerV1 Invalid(string detail) =>
            Problem(400, UnitProfileProblemCodesV1.RequestInvalid, "İstek geçersiz.", detail);

        public static UnitProfileCommandAnswerV1 VersionConflict() =>
            Problem(409, UnitProfileProblemCodesV1.VersionConflict, "Kayıt değişmiş.",
                "Kayıt siz açtıktan sonra değişti; güncel hâlini okuyup yeniden deneyin.");

        public static UnitProfileCommandAnswerV1 EffectiveFromPast() =>
            Problem(422, UnitProfileProblemCodesV1.EffectiveFromPast, "Geçmiş tarih.",
                "Birim profili sürümü bugünden önceki bir tarihten başlatılamaz.");

        public static UnitProfileCommandAnswerV1 BaselineRequired() =>
            Problem(409, UnitProfileProblemCodesV1.BaselineRequired, "Başlangıç aktarımı gerekli.",
                "Yeni sürüm girmeden önce onaylı katalog değerlerini Ayarlar tablosuna aktarın.");

        public static UnitProfileCommandAnswerV1 AlreadyTransferred() =>
            Problem(409, UnitProfileProblemCodesV1.AlreadyTransferred, "Aktarım yapılmış.",
                "Onaylı katalog değerleri bu şirket için daha önce aktarıldı; aktarım tek seferliktir.");

        public static UnitProfileCommandAnswerV1 Problem(int status, string code, string title, string detail) =>
            new UnitProfileCommandAnswerV1(status, code, title, detail);
    }
}
