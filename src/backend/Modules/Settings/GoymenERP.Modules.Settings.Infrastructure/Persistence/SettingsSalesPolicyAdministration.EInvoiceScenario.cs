using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// F-029 S6 (URUN_F029 spec §5.4(2)): the company's e-Fatura scenario policy — the scenario code list (SAT-D120) and the
/// default scenario (SAT-D102) — as two timelines of one family (<see cref="SalesPolicyFamiliesV1.EInvoiceScenario"/>). The
/// reviewed catalog answers until the one-time baseline transfer (both keys at once, unchanged; the default stays EMPTY:
/// SAT-D102 "Boş kalsın, ben gireyim"); afterwards only the stored versions do. The rule the provider serves — a default is
/// always a code of the list in force on the same date — is kept on every write: a new default outside the list and a new
/// list that drops a stored default's code are refused (<see cref="SalesQuoteAndEInvoicePolicyProblemCodesV1.EInvoiceDefaultNotInList"/>).
/// </summary>
internal sealed partial class SettingsSalesPolicyAdministration : ISettingsEInvoiceScenarioPolicyAdministrationV1
{
    private const string ScenarioPermission = SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1.EInvoiceScenarioManage;
    private const string ScenarioEntityType = "Settings.EInvoiceScenario";
    private const string ScenarioBaselineCommandName = "settings.einvoice-scenario.baseline-transfer.v1";
    private const string ScenarioCodeListCommandName = "settings.einvoice-scenario.code-list.record.v1";
    private const string ScenarioDefaultCommandName = "settings.einvoice-scenario.default.record.v1";
    private const string ScenarioBaselineEvent = "settings.einvoice-scenario.baseline-transferred.v1";
    private const string ScenarioCodeListRecordedEvent = "settings.einvoice-scenario.code-list.recorded.v1";
    private const string ScenarioDefaultRecordedEvent = "settings.einvoice-scenario.default.recorded.v1";

    /// <summary>The reviewed compiled e-Fatura scenario catalog the baseline comes from (Settings' own tests build another one).</summary>
    internal ApprovedEInvoiceScenarioCatalog EInvoiceScenarioCatalog { get; init; } = ApprovedEInvoiceScenarioCatalog.Approved;

    public async Task<EInvoiceScenarioPolicyViewV1> GetEInvoiceScenarioPolicyAsync(CancellationToken cancellationToken = default)
    {
        var companyId = RequireContextCompany();
        await RequireAsync(ScenarioPermission, companyId, cancellationToken);
        var (transferred, lists, defaults) = await ReadAsync(async () => (
            await SalesPolicyReads.IsTransferredAsync(database, SalesPolicyFamiliesV1.EInvoiceScenario, cancellationToken),
            await SalesQuoteAndEInvoicePolicyReads.LoadCodeListsAsync(database, cancellationToken),
            await SalesQuoteAndEInvoicePolicyReads.LoadScenarioDefaultsAsync(database, cancellationToken)));
        await RequireStillAsync(ScenarioPermission, companyId, cancellationToken);
        var today = SettingsBusinessDate.Today(timeProvider);
        if (!transferred)
        {
            var baselineLists = EInvoiceScenarioCatalog.CodeLists
                .Where(entry => entry.CompanyId == companyId)
                .OrderBy(entry => entry.EffectiveFrom)
                .ThenBy(entry => entry.Version)
                .Select(entry => new EInvoiceScenarioCodeListVersionViewV1(
                    entry.ProfileId,
                    Text(entry.Version),
                    entry.Codes.ToArray(),
                    entry.EffectiveFrom,
                    entry.EffectiveThrough,
                    CatalogState(entry.EffectiveFrom, entry.EffectiveThrough, today),
                    SalesPolicySourceKindsV1.CompiledBaselineV0,
                    entry.Source,
                    RecordedBy: null,
                    RecordedAtUtc: null,
                    Reason: null))
                .ToArray();
            var baselineDefaults = EInvoiceScenarioCatalog.Defaults
                .Where(entry => entry.CompanyId == companyId)
                .OrderBy(entry => entry.EffectiveFrom)
                .ThenBy(entry => entry.Version)
                .Select(entry => new EInvoiceScenarioDefaultVersionViewV1(
                    entry.ProfileId,
                    Text(entry.Version),
                    entry.ScenarioCode,
                    entry.EffectiveFrom,
                    entry.EffectiveThrough,
                    CatalogState(entry.EffectiveFrom, entry.EffectiveThrough, today),
                    SalesPolicySourceKindsV1.CompiledBaselineV0,
                    entry.Source,
                    RecordedBy: null,
                    RecordedAtUtc: null,
                    Reason: null))
                .ToArray();
            return new EInvoiceScenarioPolicyViewV1(companyId, today, false, Text(0), baselineLists, Text(0), baselineDefaults);
        }

        var listTimeline = SalesQuoteAndEInvoicePolicyReads.CodeListTimeline(lists);
        var defaultTimeline = SalesQuoteAndEInvoicePolicyReads.ScenarioDefaultTimeline(defaults);
        return new EInvoiceScenarioPolicyViewV1(
            companyId,
            today,
            true,
            Text(SalesPolicyTimelineV1.LatestVersion(listTimeline)),
            listTimeline.Select(entry => CodeListView(entry, today)).ToArray(),
            Text(SalesPolicyTimelineV1.LatestVersion(defaultTimeline)),
            defaultTimeline.Select(entry => ScenarioDefaultView(entry, today)).ToArray());
    }

    public async Task<SalesPolicyCommandResultV1> TransferEInvoiceScenarioBaselineAsync(
        TransferSalesPolicyBaselineRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(ScenarioPermission, companyId, cancellationToken);
        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var subject = companyId.ToString("D");
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new { Family = SalesPolicyFamiliesV1.EInvoiceScenario, Reason = reason });
        if (await FindExecutionAsync(ScenarioBaselineCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(ScenarioPermission, companyId, cancellationToken);
            return Done("einvoice-scenario-baseline", metadata, subject, Replay(existing, requestHash, metadata));
        }

        var transferred = await ReadAsync(() =>
            SalesPolicyReads.IsTransferredAsync(database, SalesPolicyFamiliesV1.EInvoiceScenario, cancellationToken));
        if (transferred)
        {
            await RequireStillAsync(ScenarioPermission, companyId, cancellationToken);
            return Done("einvoice-scenario-baseline", metadata, subject, await StoreRefusalAsync(ScenarioBaselineCommandName,
                Answers.AlreadyTransferred(), keyHash, requestHash, metadata, cancellationToken));
        }

        var lists = EInvoiceScenarioCatalog.CodeLists
            .Where(entry => entry.CompanyId == companyId)
            .OrderBy(entry => entry.EffectiveFrom)
            .ToArray();
        var defaults = EInvoiceScenarioCatalog.Defaults
            .Where(entry => entry.CompanyId == companyId)
            .OrderBy(entry => entry.EffectiveFrom)
            .ToArray();
        RequireChainedWindows(lists, entry => entry.EffectiveFrom, entry => entry.EffectiveThrough, entry => entry.Source);
        RequireChainedWindows(defaults, entry => entry.EffectiveFrom, entry => entry.EffectiveThrough, entry => entry.Source);
        if (lists.Select(entry => entry.ProfileId).Distinct().Count() != lists.Length
            || defaults.Select(entry => entry.ProfileId).Distinct().Count() != defaults.Length
            || lists.Any(entry => !IsWellFormedCodeList(entry.Codes))
            || defaults.Any(entry => !EInvoiceScenarioCodeFormatV1.IsWellFormed(entry.ScenarioCode)))
        {
            throw new SettingsPrerequisiteException("settings_profile_invalid");
        }

        var now = timeProvider.GetUtcNow();
        var listRows = lists.Select(entry => new StoredEInvoiceScenarioCodeList(new EInvoiceScenarioCodeListVersionRow
        {
            CompanyId = companyId,
            Version = entry.Version,
            ProfileId = entry.ProfileId,
            EffectiveFrom = entry.EffectiveFrom,
            SourceKind = SalesPolicySourceKindsV1.CompiledBaselineV0,
            DecisionReference = entry.Source,
            RecordedAtUtc = now,
            RecordedBy = metadata.ActorId,
            Reason = reason,
        }, entry.Codes.ToArray())).ToArray();
        var defaultRows = defaults.Select(entry => new EInvoiceScenarioDefaultVersionRow
        {
            CompanyId = companyId,
            Version = entry.Version,
            ProfileId = entry.ProfileId,
            ScenarioCode = entry.ScenarioCode,
            EffectiveFrom = entry.EffectiveFrom,
            SourceKind = SalesPolicySourceKindsV1.CompiledBaselineV0,
            DecisionReference = entry.Source,
            RecordedAtUtc = now,
            RecordedBy = metadata.ActorId,
            Reason = reason,
        }).ToArray();
        var listTimeline = SalesQuoteAndEInvoicePolicyReads.CodeListTimeline(listRows);
        var defaultTimeline = SalesQuoteAndEInvoicePolicyReads.ScenarioDefaultTimeline(defaultRows);
        if (!SalesQuoteAndEInvoicePolicyReads.DefaultsStayInLists(listTimeline, defaultTimeline)
            || !SalesQuoteAndEInvoicePolicyReads.DefaultsCoveredByLists(listTimeline, defaultTimeline))
        {
            throw new SettingsPrerequisiteException("settings_profile_invalid");
        }

        foreach (var list in listRows)
        {
            database.EInvoiceScenarioCodeListVersions.Add(list.Header);
            AddScenarioCodes(companyId, list.Header.Version, list.Codes);
        }

        database.EInvoiceScenarioDefaultVersions.AddRange(defaultRows);
        var entryCount = lists.Length + defaults.Length;
        database.SalesPolicyBaselineTransfers.Add(new SalesPolicyBaselineTransferRow
        {
            CompanyId = companyId,
            PolicyFamily = SalesPolicyFamiliesV1.EInvoiceScenario,
            EntryCount = entryCount,
            TransferredAtUtc = now,
            TransferredBy = metadata.ActorId,
            Reason = reason,
        });
        var maximumVersion = lists.Select(entry => entry.Version).Concat(defaults.Select(entry => entry.Version))
            .Select(version => (long?)version).Max();
        Audit(ScenarioPermission, companyId, ScenarioBaselineEvent, ScenarioEntityType, subject, "TransferBaseline", null,
            maximumVersion, scope, new
            {
                CompanyId = companyId,
                PolicyFamily = SalesPolicyFamiliesV1.EInvoiceScenario,
                EntryCount = entryCount,
                CodeLists = lists.Select(entry => new
                {
                    entry.ProfileId,
                    entry.Version,
                    Codes = entry.Codes.ToArray(),
                    entry.EffectiveFrom,
                    entry.EffectiveThrough,
                    DecisionReference = entry.Source,
                }).ToArray(),
                Defaults = defaults.Select(entry => new
                {
                    entry.ProfileId,
                    entry.Version,
                    entry.ScenarioCode,
                    entry.EffectiveFrom,
                    entry.EffectiveThrough,
                    DecisionReference = entry.Source,
                }).ToArray(),
            }, reason, metadata);
        await RequireStillAsync(ScenarioPermission, companyId, cancellationToken);
        return Done("einvoice-scenario-baseline", metadata, subject, await SaveChangeAsync(ScenarioBaselineCommandName,
            Answers.NoContent(), keyHash, requestHash, metadata, Answers.AlreadyTransferred(), cancellationToken));
    }

    public async Task<SalesPolicyCommandResultV1> RecordEInvoiceScenarioCodeListAsync(
        RecordEInvoiceScenarioCodeListRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(ScenarioPermission, companyId, cancellationToken);
        if (!TryParseVersion(request.ExpectedVersion, 0, out var expectedVersion))
        {
            return Unstored(Answers.Invalid("Beklenen sürüm (\"0\" veya pozitif tam sayı metni) zorunludur."), metadata);
        }

        var codes = request.Codes is { } requested && requested.All(code => code is not null)
            ? requested.Select(code => code!).ToArray()
            : null;
        if (codes is null || codes.Length > SalesQuoteAndEInvoicePolicyInputLimitsV1.ScenarioCodeMaximumCount
            || !IsWellFormedCodeList(codes))
        {
            return Unstored(Answers.Invalid(
                $"Senaryo kodu listesi boş olamaz ve en çok {SalesQuoteAndEInvoicePolicyInputLimitsV1.ScenarioCodeMaximumCount} kod içerebilir; her kod büyük harfle başlamalı, yalnız büyük harf, rakam ve alt çizgi içermeli, en çok {EInvoiceScenarioCodeFormatV1.MaximumLength} karakter olmalı ve tekrarlanmamalıdır."),
                metadata);
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
            Codes = codes,
            Reason = reason,
            request.EffectiveFrom,
        });
        if (await FindExecutionAsync(ScenarioCodeListCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(ScenarioPermission, companyId, cancellationToken);
            return Done("einvoice-scenario-code-list", metadata, subject, Replay(existing, requestHash, metadata));
        }

        var (transferred, lists, defaults) = await ReadAsync(async () => (
            await SalesPolicyReads.IsTransferredAsync(database, SalesPolicyFamiliesV1.EInvoiceScenario, cancellationToken),
            await SalesQuoteAndEInvoicePolicyReads.LoadCodeListsAsync(database, cancellationToken),
            await SalesQuoteAndEInvoicePolicyReads.LoadScenarioDefaultsAsync(database, cancellationToken)));
        var timeline = SalesQuoteAndEInvoicePolicyReads.CodeListTimeline(lists);
        var latest = SalesPolicyTimelineV1.LatestVersion(timeline);
        var today = SettingsBusinessDate.Today(timeProvider);
        var effectiveFrom = request.EffectiveFrom ?? today;
        var refusal = !transferred ? Answers.BaselineRequired()
            : latest != expectedVersion ? Answers.VersionConflict()
            : effectiveFrom < today ? Answers.EffectiveFromPast()
            : null;
        if (refusal is not null)
        {
            await RequireStillAsync(ScenarioPermission, companyId, cancellationToken);
            return Done("einvoice-scenario-code-list", metadata, subject, await StoreRefusalAsync(ScenarioCodeListCommandName,
                refusal, keyHash, requestHash, metadata, cancellationToken));
        }

        var inForce = SalesPolicyTimelineV1.InForceOn(timeline, effectiveFrom);
        if (inForce is not null && inForce.Value.Codes.SequenceEqual(codes, StringComparer.Ordinal))
        {
            await RequireStillAsync(ScenarioPermission, companyId, cancellationToken);
            return Done("einvoice-scenario-code-list", metadata, subject, await StoreRefusalAsync(ScenarioCodeListCommandName,
                Answers.NoContent(), keyHash, requestHash, metadata, cancellationToken));
        }

        var version = checked(latest + 1);
        var header = new EInvoiceScenarioCodeListVersionRow
        {
            CompanyId = companyId,
            Version = version,
            ProfileId = Guid.NewGuid(),
            EffectiveFrom = effectiveFrom,
            SourceKind = SalesPolicySourceKindsV1.UserMaintained,
            DecisionReference = null,
            RecordedAtUtc = timeProvider.GetUtcNow(),
            RecordedBy = metadata.ActorId,
            Reason = reason,
        };
        var candidate = SalesQuoteAndEInvoicePolicyReads.CodeListTimeline(
            lists.Append(new StoredEInvoiceScenarioCodeList(header, codes)));
        if (!SalesQuoteAndEInvoicePolicyReads.DefaultsStayInLists(candidate,
                SalesQuoteAndEInvoicePolicyReads.ScenarioDefaultTimeline(defaults)))
        {
            await RequireStillAsync(ScenarioPermission, companyId, cancellationToken);
            return Done("einvoice-scenario-code-list", metadata, subject, await StoreRefusalAsync(ScenarioCodeListCommandName,
                DefaultNotInList(), keyHash, requestHash, metadata, cancellationToken));
        }

        if (SettingsBusinessDate.Today(timeProvider) != today)
        {
            return Done("einvoice-scenario-code-list", metadata, subject, Unstored(Answers.VersionConflict(), metadata));
        }

        database.EInvoiceScenarioCodeListVersions.Add(header);
        AddScenarioCodes(companyId, version, codes);
        Audit(ScenarioPermission, companyId, ScenarioCodeListRecordedEvent, ScenarioEntityType, subject, "RecordVersion",
            latest == 0 ? null : latest, version, scope, new
            {
                CompanyId = companyId,
                header.ProfileId,
                header.Version,
                Codes = codes,
                header.EffectiveFrom,
                SupersedesVersion = timeline
                    .Where(entry => !entry.IsSuperseded && entry.EffectiveFrom == effectiveFrom)
                    .Select(entry => (long?)entry.Version)
                    .SingleOrDefault(),
            }, reason, metadata);
        await RequireStillAsync(ScenarioPermission, companyId, cancellationToken);
        return Done("einvoice-scenario-code-list", metadata, subject, await SaveChangeAsync(ScenarioCodeListCommandName,
            Answers.NoContent(), keyHash, requestHash, metadata, Answers.VersionConflict(), cancellationToken));
    }

    public async Task<SalesPolicyCommandResultV1> RecordEInvoiceScenarioDefaultAsync(
        RecordEInvoiceScenarioDefaultRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(ScenarioPermission, companyId, cancellationToken);
        if (!TryParseVersion(request.ExpectedVersion, 0, out var expectedVersion))
        {
            return Unstored(Answers.Invalid("Beklenen sürüm (\"0\" veya pozitif tam sayı metni) zorunludur."), metadata);
        }

        if (!EInvoiceScenarioCodeFormatV1.IsWellFormed(request.ScenarioCode))
        {
            return Unstored(Answers.Invalid(
                $"Varsayılan senaryo kodu büyük harfle başlamalı, yalnız büyük harf, rakam ve alt çizgi içermeli ve en çok {EInvoiceScenarioCodeFormatV1.MaximumLength} karakter olmalıdır."),
                metadata);
        }

        var scenarioCode = request.ScenarioCode!;
        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var subject = companyId.ToString("D");
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            ExpectedVersion = expectedVersion,
            ScenarioCode = scenarioCode,
            Reason = reason,
            request.EffectiveFrom,
        });
        if (await FindExecutionAsync(ScenarioDefaultCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(ScenarioPermission, companyId, cancellationToken);
            return Done("einvoice-scenario-default", metadata, subject, Replay(existing, requestHash, metadata));
        }

        var (transferred, lists, defaults) = await ReadAsync(async () => (
            await SalesPolicyReads.IsTransferredAsync(database, SalesPolicyFamiliesV1.EInvoiceScenario, cancellationToken),
            await SalesQuoteAndEInvoicePolicyReads.LoadCodeListsAsync(database, cancellationToken),
            await SalesQuoteAndEInvoicePolicyReads.LoadScenarioDefaultsAsync(database, cancellationToken)));
        var timeline = SalesQuoteAndEInvoicePolicyReads.ScenarioDefaultTimeline(defaults);
        var latest = SalesPolicyTimelineV1.LatestVersion(timeline);
        var today = SettingsBusinessDate.Today(timeProvider);
        var effectiveFrom = request.EffectiveFrom ?? today;
        var refusal = !transferred ? Answers.BaselineRequired()
            : latest != expectedVersion ? Answers.VersionConflict()
            : effectiveFrom < today ? Answers.EffectiveFromPast()
            : null;
        if (refusal is not null)
        {
            await RequireStillAsync(ScenarioPermission, companyId, cancellationToken);
            return Done("einvoice-scenario-default", metadata, subject, await StoreRefusalAsync(ScenarioDefaultCommandName,
                refusal, keyHash, requestHash, metadata, cancellationToken));
        }

        var inForce = SalesPolicyTimelineV1.InForceOn(timeline, effectiveFrom);
        if (inForce is not null && string.Equals(inForce.Value.ScenarioCode, scenarioCode, StringComparison.Ordinal))
        {
            await RequireStillAsync(ScenarioPermission, companyId, cancellationToken);
            return Done("einvoice-scenario-default", metadata, subject, await StoreRefusalAsync(ScenarioDefaultCommandName,
                Answers.NoContent(), keyHash, requestHash, metadata, cancellationToken));
        }

        var version = checked(latest + 1);
        var row = new EInvoiceScenarioDefaultVersionRow
        {
            CompanyId = companyId,
            Version = version,
            ProfileId = Guid.NewGuid(),
            ScenarioCode = scenarioCode,
            EffectiveFrom = effectiveFrom,
            SourceKind = SalesPolicySourceKindsV1.UserMaintained,
            DecisionReference = null,
            RecordedAtUtc = timeProvider.GetUtcNow(),
            RecordedBy = metadata.ActorId,
            Reason = reason,
        };
        var listTimeline = SalesQuoteAndEInvoicePolicyReads.CodeListTimeline(lists);
        var candidate = SalesQuoteAndEInvoicePolicyReads.ScenarioDefaultTimeline(defaults.Append(row));
        if (SalesPolicyTimelineV1.InForceOn(listTimeline, effectiveFrom) is null
            || !SalesQuoteAndEInvoicePolicyReads.DefaultsStayInLists(listTimeline, candidate)
            || !SalesQuoteAndEInvoicePolicyReads.DefaultsCoveredByLists(listTimeline, candidate))
        {
            await RequireStillAsync(ScenarioPermission, companyId, cancellationToken);
            return Done("einvoice-scenario-default", metadata, subject, await StoreRefusalAsync(ScenarioDefaultCommandName,
                DefaultNotInList(), keyHash, requestHash, metadata, cancellationToken));
        }

        if (SettingsBusinessDate.Today(timeProvider) != today)
        {
            return Done("einvoice-scenario-default", metadata, subject, Unstored(Answers.VersionConflict(), metadata));
        }

        database.EInvoiceScenarioDefaultVersions.Add(row);
        Audit(ScenarioPermission, companyId, ScenarioDefaultRecordedEvent, ScenarioEntityType, subject, "RecordVersion",
            latest == 0 ? null : latest, version, scope, new
            {
                CompanyId = companyId,
                row.ProfileId,
                row.Version,
                row.ScenarioCode,
                row.EffectiveFrom,
                SupersedesVersion = timeline
                    .Where(entry => !entry.IsSuperseded && entry.EffectiveFrom == effectiveFrom)
                    .Select(entry => (long?)entry.Version)
                    .SingleOrDefault(),
            }, reason, metadata);
        await RequireStillAsync(ScenarioPermission, companyId, cancellationToken);
        return Done("einvoice-scenario-default", metadata, subject, await SaveChangeAsync(ScenarioDefaultCommandName,
            Answers.NoContent(), keyHash, requestHash, metadata, Answers.VersionConflict(), cancellationToken));
    }

    /// <summary>A non-empty list of distinct, well-formed scenario codes (exactly as given: no trimming, no case change).</summary>
    private static bool IsWellFormedCodeList(IReadOnlyCollection<string> codes) =>
        codes.Count > 0
        && codes.All(EInvoiceScenarioCodeFormatV1.IsWellFormed)
        && codes.Distinct(StringComparer.Ordinal).Count() == codes.Count;

    private void AddScenarioCodes(Guid companyId, long listVersion, IEnumerable<string> codes)
    {
        var position = 0;
        foreach (var code in codes)
        {
            database.EInvoiceScenarioCodes.Add(new EInvoiceScenarioCodeRow
            {
                CompanyId = companyId,
                ListVersion = listVersion,
                Position = position++,
                Code = code,
            });
        }
    }

    private static SalesPolicyCommandAnswerV1 DefaultNotInList() =>
        Answers.Problem(422, SalesQuoteAndEInvoicePolicyProblemCodesV1.EInvoiceDefaultNotInList,
            "Varsayılan senaryo listede değil.",
            "Varsayılan e-Fatura senaryosu, kapsadığı her tarihte yürürlükteki senaryo kodu listesinde bulunmalıdır; listeden çıkarılan kod varsayılan olarak kalamaz.");

    private static EInvoiceScenarioCodeListVersionViewV1 CodeListView(
        SalesPolicyTimelineEntryV1<StoredEInvoiceScenarioCodeList> entry, DateOnly today) =>
        new EInvoiceScenarioCodeListVersionViewV1(
            entry.Value.Header.ProfileId,
            Text(entry.Version),
            entry.Value.Codes.ToArray(),
            entry.EffectiveFrom,
            entry.EffectiveThrough,
            SalesPolicyTimelineV1.State(entry, today),
            entry.Value.Header.SourceKind,
            entry.Value.Header.DecisionReference,
            entry.Value.Header.RecordedBy,
            entry.Value.Header.RecordedAtUtc,
            entry.Value.Header.Reason);

    private static EInvoiceScenarioDefaultVersionViewV1 ScenarioDefaultView(
        SalesPolicyTimelineEntryV1<EInvoiceScenarioDefaultVersionRow> entry, DateOnly today) =>
        new EInvoiceScenarioDefaultVersionViewV1(
            entry.Value.ProfileId,
            Text(entry.Version),
            entry.Value.ScenarioCode,
            entry.EffectiveFrom,
            entry.EffectiveThrough,
            SalesPolicyTimelineV1.State(entry, today),
            entry.Value.SourceKind,
            entry.Value.DecisionReference,
            entry.Value.RecordedBy,
            entry.Value.RecordedAtUtc,
            entry.Value.Reason);
}
