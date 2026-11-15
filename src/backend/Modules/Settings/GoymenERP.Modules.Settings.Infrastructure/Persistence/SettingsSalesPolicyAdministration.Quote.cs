using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// F-029 S5 (URUN_F029 spec §5.4(2)): the company's Sales quote policies — the closed loss reason list (SAT-D25) and the
/// default quote validity in days (SAT-D39) — as two timelines of one family
/// (<see cref="SalesPolicyFamiliesV1.SalesQuotePolicy"/>). The reviewed catalog answers until the one-time baseline transfer
/// (which copies both keys at once, unchanged); afterwards only the stored versions do. No value is chosen here: the person
/// records every new list and day count.
/// </summary>
internal sealed partial class SettingsSalesPolicyAdministration : ISettingsSalesQuotePolicyAdministrationV1
{
    private const string QuotePermission = SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1.SalesQuotePolicyManage;
    private const string QuoteEntityType = "Settings.SalesQuotePolicy";
    private const string QuoteBaselineCommandName = "settings.sales-quote-policy.baseline-transfer.v1";
    private const string QuoteLossReasonsCommandName = "settings.sales-quote-policy.loss-reasons.record.v1";
    private const string QuoteValidityCommandName = "settings.sales-quote-policy.default-validity.record.v1";
    private const string QuoteBaselineEvent = "settings.sales-quote-policy.baseline-transferred.v1";
    private const string QuoteLossReasonsRecordedEvent = "settings.sales-quote-policy.loss-reasons.recorded.v1";
    private const string QuoteValidityRecordedEvent = "settings.sales-quote-policy.default-validity.recorded.v1";

    /// <summary>The reviewed compiled quote policy catalog the baseline comes from (Settings' own tests build another one).</summary>
    internal ApprovedSalesQuotePolicyCatalog QuoteCatalog { get; init; } = ApprovedSalesQuotePolicyCatalog.Approved;

    public async Task<SalesQuotePolicyViewV1> GetSalesQuotePolicyAsync(CancellationToken cancellationToken = default)
    {
        var companyId = RequireContextCompany();
        await RequireAsync(QuotePermission, companyId, cancellationToken);
        var (transferred, lists, validities) = await ReadAsync(async () => (
            await SalesPolicyReads.IsTransferredAsync(database, SalesPolicyFamiliesV1.SalesQuotePolicy, cancellationToken),
            await SalesQuoteAndEInvoicePolicyReads.LoadLossReasonListsAsync(database, cancellationToken),
            await SalesQuoteAndEInvoicePolicyReads.LoadDefaultValiditiesAsync(database, cancellationToken)));
        await RequireStillAsync(QuotePermission, companyId, cancellationToken);
        var today = SettingsBusinessDate.Today(timeProvider);
        if (!transferred)
        {
            var baselineLists = QuoteCatalog.LossReasonLists
                .Where(entry => entry.CompanyId == companyId)
                .OrderBy(entry => entry.EffectiveFrom)
                .ThenBy(entry => entry.Version)
                .Select(entry => new SalesQuoteLossReasonListVersionViewV1(
                    entry.ProfileId,
                    Text(entry.Version),
                    entry.Reasons.Select(reason => new SalesQuoteLossReasonViewV1(reason.ReasonId, reason.Code, reason.Label,
                        reason.RequiresExplanation, reason.IsActive)).ToArray(),
                    entry.IsActive,
                    entry.EffectiveFrom,
                    entry.EffectiveThrough,
                    CatalogState(entry.EffectiveFrom, entry.EffectiveThrough, today),
                    SalesPolicySourceKindsV1.CompiledBaselineV0,
                    entry.Source,
                    RecordedBy: null,
                    RecordedAtUtc: null,
                    Reason: null))
                .ToArray();
            var baselineValidities = QuoteCatalog.DefaultValidities
                .Where(entry => entry.CompanyId == companyId)
                .OrderBy(entry => entry.EffectiveFrom)
                .ThenBy(entry => entry.Version)
                .Select(entry => new SalesQuoteDefaultValidityVersionViewV1(
                    entry.ProfileId,
                    Text(entry.Version),
                    entry.ValidityDays,
                    entry.IsActive,
                    entry.EffectiveFrom,
                    entry.EffectiveThrough,
                    CatalogState(entry.EffectiveFrom, entry.EffectiveThrough, today),
                    SalesPolicySourceKindsV1.CompiledBaselineV0,
                    entry.Source,
                    RecordedBy: null,
                    RecordedAtUtc: null,
                    Reason: null))
                .ToArray();
            return new SalesQuotePolicyViewV1(companyId, today, false, Text(0), baselineLists, Text(0), baselineValidities);
        }

        var listTimeline = SalesQuoteAndEInvoicePolicyReads.LossReasonTimeline(lists);
        var validityTimeline = SalesQuoteAndEInvoicePolicyReads.DefaultValidityTimeline(validities);
        return new SalesQuotePolicyViewV1(
            companyId,
            today,
            true,
            Text(SalesPolicyTimelineV1.LatestVersion(listTimeline)),
            listTimeline.Select(entry => LossReasonListView(entry, today)).ToArray(),
            Text(SalesPolicyTimelineV1.LatestVersion(validityTimeline)),
            validityTimeline.Select(entry => DefaultValidityView(entry, today)).ToArray());
    }

    public async Task<SalesPolicyCommandResultV1> TransferSalesQuotePolicyBaselineAsync(
        TransferSalesPolicyBaselineRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(QuotePermission, companyId, cancellationToken);
        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var subject = companyId.ToString("D");
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new { Family = SalesPolicyFamiliesV1.SalesQuotePolicy, Reason = reason });
        if (await FindExecutionAsync(QuoteBaselineCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(QuotePermission, companyId, cancellationToken);
            return Done("quote-baseline", metadata, subject, Replay(existing, requestHash, metadata));
        }

        var transferred = await ReadAsync(() =>
            SalesPolicyReads.IsTransferredAsync(database, SalesPolicyFamiliesV1.SalesQuotePolicy, cancellationToken));
        if (transferred)
        {
            await RequireStillAsync(QuotePermission, companyId, cancellationToken);
            return Done("quote-baseline", metadata, subject, await StoreRefusalAsync(QuoteBaselineCommandName,
                Answers.AlreadyTransferred(), keyHash, requestHash, metadata, cancellationToken));
        }

        var lists = QuoteCatalog.LossReasonLists
            .Where(entry => entry.CompanyId == companyId)
            .OrderBy(entry => entry.EffectiveFrom)
            .ToArray();
        var validities = QuoteCatalog.DefaultValidities
            .Where(entry => entry.CompanyId == companyId)
            .OrderBy(entry => entry.EffectiveFrom)
            .ToArray();
        RequireChainedWindows(lists, entry => entry.EffectiveFrom, entry => entry.EffectiveThrough, entry => entry.Source);
        RequireChainedWindows(validities, entry => entry.EffectiveFrom, entry => entry.EffectiveThrough, entry => entry.Source);
        if (lists.Select(entry => entry.ProfileId).Distinct().Count() != lists.Length
            || validities.Select(entry => entry.ProfileId).Distinct().Count() != validities.Length
            || lists.Any(entry => !FitsLossReasonColumns(entry.Reasons))
            || validities.Any(entry => entry.ValidityDays <= 0)
            || !KeepsLossReasonIdentities(lists.Select(entry => entry.Reasons.Select(reason => (reason.ReasonId, reason.Code)))))
        {
            throw new SettingsPrerequisiteException("settings_profile_invalid");
        }

        var now = timeProvider.GetUtcNow();
        foreach (var entry in lists)
        {
            database.SalesQuoteLossReasonListVersions.Add(new SalesQuoteLossReasonListVersionRow
            {
                CompanyId = companyId,
                Version = entry.Version,
                ProfileId = entry.ProfileId,
                IsActive = entry.IsActive,
                EffectiveFrom = entry.EffectiveFrom,
                SourceKind = SalesPolicySourceKindsV1.CompiledBaselineV0,
                DecisionReference = entry.Source,
                RecordedAtUtc = now,
                RecordedBy = metadata.ActorId,
                Reason = reason,
            });
            AddLossReasonEntries(companyId, entry.Version,
                entry.Reasons.Select(item => (item.ReasonId, item.Code, item.Label, item.RequiresExplanation, item.IsActive)));
        }

        foreach (var entry in validities)
        {
            database.SalesQuoteDefaultValidityVersions.Add(new SalesQuoteDefaultValidityVersionRow
            {
                CompanyId = companyId,
                Version = entry.Version,
                ProfileId = entry.ProfileId,
                ValidityDays = entry.ValidityDays,
                IsActive = entry.IsActive,
                EffectiveFrom = entry.EffectiveFrom,
                SourceKind = SalesPolicySourceKindsV1.CompiledBaselineV0,
                DecisionReference = entry.Source,
                RecordedAtUtc = now,
                RecordedBy = metadata.ActorId,
                Reason = reason,
            });
        }

        var entryCount = lists.Length + validities.Length;
        database.SalesPolicyBaselineTransfers.Add(new SalesPolicyBaselineTransferRow
        {
            CompanyId = companyId,
            PolicyFamily = SalesPolicyFamiliesV1.SalesQuotePolicy,
            EntryCount = entryCount,
            TransferredAtUtc = now,
            TransferredBy = metadata.ActorId,
            Reason = reason,
        });
        var maximumVersion = lists.Select(entry => entry.Version).Concat(validities.Select(entry => entry.Version))
            .Select(version => (long?)version).Max();
        Audit(QuotePermission, companyId, QuoteBaselineEvent, QuoteEntityType, subject, "TransferBaseline", null, maximumVersion,
            scope, new
            {
                CompanyId = companyId,
                PolicyFamily = SalesPolicyFamiliesV1.SalesQuotePolicy,
                EntryCount = entryCount,
                LossReasonLists = lists.Select(entry => new
                {
                    entry.ProfileId,
                    entry.Version,
                    LossReasons = entry.Reasons.Select(item => new
                    {
                        item.ReasonId,
                        item.Code,
                        item.Label,
                        item.RequiresExplanation,
                        item.IsActive,
                    }).ToArray(),
                    entry.IsActive,
                    entry.EffectiveFrom,
                    entry.EffectiveThrough,
                    DecisionReference = entry.Source,
                }).ToArray(),
                DefaultValidities = validities.Select(entry => new
                {
                    entry.ProfileId,
                    entry.Version,
                    entry.ValidityDays,
                    entry.IsActive,
                    entry.EffectiveFrom,
                    entry.EffectiveThrough,
                    DecisionReference = entry.Source,
                }).ToArray(),
            }, reason, metadata);
        await RequireStillAsync(QuotePermission, companyId, cancellationToken);
        return Done("quote-baseline", metadata, subject, await SaveChangeAsync(QuoteBaselineCommandName,
            Answers.NoContent(), keyHash, requestHash, metadata, Answers.AlreadyTransferred(), cancellationToken));
    }

    public async Task<SalesPolicyCommandResultV1> RecordSalesQuoteLossReasonsAsync(RecordSalesQuoteLossReasonsRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(QuotePermission, companyId, cancellationToken);
        if (!TryParseVersion(request.ExpectedVersion, 0, out var expectedVersion))
        {
            return Unstored(Answers.Invalid("Beklenen sürüm (\"0\" veya pozitif tam sayı metni) zorunludur."), metadata);
        }

        if (!TryReadLossReasons(request.LossReasons, out var reasons))
        {
            return Unstored(Answers.Invalid(
                $"Kayıp nedeni listesi boş olamaz ve en çok {SalesQuoteAndEInvoicePolicyInputLimitsV1.LossReasonMaximumCount} neden içerebilir; her nedenin kodu (en çok {SalesQuoteAndEInvoicePolicyInputLimitsV1.LossReasonCodeMaximumLength} karakter), adı (en çok {SalesQuoteAndEInvoicePolicyInputLimitsV1.LossReasonLabelMaximumLength} karakter), açıklama zorunluluğu ve etkinlik bilgisi dolu olmalı; kod ve ad baş/son boşluk taşımamalı, kimlikler ve kodlar tekrarlanmamalıdır."),
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
            LossReasons = reasons.Select(item => new
            {
                item.ReasonId,
                item.Code,
                item.Label,
                item.RequiresExplanation,
                item.IsActive,
            }).ToArray(),
            Reason = reason,
            request.EffectiveFrom,
        });
        if (await FindExecutionAsync(QuoteLossReasonsCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(QuotePermission, companyId, cancellationToken);
            return Done("quote-loss-reasons", metadata, subject, Replay(existing, requestHash, metadata));
        }

        var (transferred, lists) = await ReadAsync(async () => (
            await SalesPolicyReads.IsTransferredAsync(database, SalesPolicyFamiliesV1.SalesQuotePolicy, cancellationToken),
            await SalesQuoteAndEInvoicePolicyReads.LoadLossReasonListsAsync(database, cancellationToken)));
        var timeline = SalesQuoteAndEInvoicePolicyReads.LossReasonTimeline(lists);
        var latest = SalesPolicyTimelineV1.LatestVersion(timeline);
        var today = SettingsBusinessDate.Today(timeProvider);
        var effectiveFrom = request.EffectiveFrom ?? today;
        var refusal = !transferred ? Answers.BaselineRequired()
            : latest != expectedVersion ? Answers.VersionConflict()
            : effectiveFrom < today ? Answers.EffectiveFromPast()
            : null;
        if (refusal is not null)
        {
            await RequireStillAsync(QuotePermission, companyId, cancellationToken);
            return Done("quote-loss-reasons", metadata, subject, await StoreRefusalAsync(QuoteLossReasonsCommandName, refusal,
                keyHash, requestHash, metadata, cancellationToken));
        }

        var inForce = SalesPolicyTimelineV1.InForceOn(timeline, effectiveFrom);
        if (inForce is { Value.Header.IsActive: true } && SameLossReasons(inForce.Value.Entries, reasons))
        {
            await RequireStillAsync(QuotePermission, companyId, cancellationToken);
            return Done("quote-loss-reasons", metadata, subject, await StoreRefusalAsync(QuoteLossReasonsCommandName,
                Answers.NoContent(), keyHash, requestHash, metadata, cancellationToken));
        }

        if (!KeepsLossReasonIdentities(lists.Select(list => list.Entries.Select(entry => (entry.ReasonId, entry.Code)))))
        {
            throw new SettingsPrerequisiteException("settings_profile_invalid");
        }

        var stored = lists.SelectMany(list => list.Entries)
            .GroupBy(entry => entry.ReasonId)
            .ToDictionary(group => group.Key, group => group.First().Code);
        var storedCodes = stored.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
        if (reasons.Any(item => item.ReasonId is { } id
                ? !stored.TryGetValue(id, out var code) || !string.Equals(code, item.Code, StringComparison.Ordinal)
                : storedCodes.ContainsKey(item.Code)))
        {
            await RequireStillAsync(QuotePermission, companyId, cancellationToken);
            return Done("quote-loss-reasons", metadata, subject, await StoreRefusalAsync(QuoteLossReasonsCommandName,
                LossReasonIdentityConflict(), keyHash, requestHash, metadata, cancellationToken));
        }

        if (SettingsBusinessDate.Today(timeProvider) != today)
        {
            return Done("quote-loss-reasons", metadata, subject, Unstored(Answers.VersionConflict(), metadata));
        }

        var version = checked(latest + 1);
        var header = new SalesQuoteLossReasonListVersionRow
        {
            CompanyId = companyId,
            Version = version,
            ProfileId = Guid.NewGuid(),
            IsActive = true,
            EffectiveFrom = effectiveFrom,
            SourceKind = SalesPolicySourceKindsV1.UserMaintained,
            DecisionReference = null,
            RecordedAtUtc = timeProvider.GetUtcNow(),
            RecordedBy = metadata.ActorId,
            Reason = reason,
        };
        database.SalesQuoteLossReasonListVersions.Add(header);
        var entries = reasons
            .Select(item => (ReasonId: item.ReasonId ?? Guid.NewGuid(), item.Code, item.Label, item.RequiresExplanation,
                item.IsActive))
            .ToArray();
        AddLossReasonEntries(companyId, version, entries);
        Audit(QuotePermission, companyId, QuoteLossReasonsRecordedEvent, QuoteEntityType, subject, "RecordVersion",
            latest == 0 ? null : latest, version, scope, new
            {
                CompanyId = companyId,
                header.ProfileId,
                header.Version,
                LossReasons = entries.Select(item => new
                {
                    item.ReasonId,
                    item.Code,
                    item.Label,
                    item.RequiresExplanation,
                    item.IsActive,
                }).ToArray(),
                header.IsActive,
                header.EffectiveFrom,
                SupersedesVersion = timeline
                    .Where(entry => !entry.IsSuperseded && entry.EffectiveFrom == effectiveFrom)
                    .Select(entry => (long?)entry.Version)
                    .SingleOrDefault(),
            }, reason, metadata);
        await RequireStillAsync(QuotePermission, companyId, cancellationToken);
        return Done("quote-loss-reasons", metadata, subject, await SaveChangeAsync(QuoteLossReasonsCommandName,
            Answers.NoContent(), keyHash, requestHash, metadata, Answers.VersionConflict(), cancellationToken));
    }

    public async Task<SalesPolicyCommandResultV1> RecordSalesQuoteDefaultValidityAsync(
        RecordSalesQuoteDefaultValidityRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(QuotePermission, companyId, cancellationToken);
        if (!TryParseVersion(request.ExpectedVersion, 0, out var expectedVersion))
        {
            return Unstored(Answers.Invalid("Beklenen sürüm (\"0\" veya pozitif tam sayı metni) zorunludur."), metadata);
        }

        if (request.ValidityDays is not { } days || days <= 0)
        {
            return Unstored(Answers.Invalid("Varsayılan teklif geçerlilik süresi pozitif bir gün sayısı olmalıdır."), metadata);
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
            ValidityDays = days,
            Reason = reason,
            request.EffectiveFrom,
        });
        if (await FindExecutionAsync(QuoteValidityCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(QuotePermission, companyId, cancellationToken);
            return Done("quote-default-validity", metadata, subject, Replay(existing, requestHash, metadata));
        }

        var (transferred, rows) = await ReadAsync(async () => (
            await SalesPolicyReads.IsTransferredAsync(database, SalesPolicyFamiliesV1.SalesQuotePolicy, cancellationToken),
            await SalesQuoteAndEInvoicePolicyReads.LoadDefaultValiditiesAsync(database, cancellationToken)));
        var timeline = SalesQuoteAndEInvoicePolicyReads.DefaultValidityTimeline(rows);
        var latest = SalesPolicyTimelineV1.LatestVersion(timeline);
        var today = SettingsBusinessDate.Today(timeProvider);
        var effectiveFrom = request.EffectiveFrom ?? today;
        var refusal = !transferred ? Answers.BaselineRequired()
            : latest != expectedVersion ? Answers.VersionConflict()
            : effectiveFrom < today ? Answers.EffectiveFromPast()
            : null;
        if (refusal is not null)
        {
            await RequireStillAsync(QuotePermission, companyId, cancellationToken);
            return Done("quote-default-validity", metadata, subject, await StoreRefusalAsync(QuoteValidityCommandName, refusal,
                keyHash, requestHash, metadata, cancellationToken));
        }

        var inForce = SalesPolicyTimelineV1.InForceOn(timeline, effectiveFrom);
        if (inForce is { Value.IsActive: true } && inForce.Value.ValidityDays == days)
        {
            await RequireStillAsync(QuotePermission, companyId, cancellationToken);
            return Done("quote-default-validity", metadata, subject, await StoreRefusalAsync(QuoteValidityCommandName,
                Answers.NoContent(), keyHash, requestHash, metadata, cancellationToken));
        }

        if (SettingsBusinessDate.Today(timeProvider) != today)
        {
            return Done("quote-default-validity", metadata, subject, Unstored(Answers.VersionConflict(), metadata));
        }

        var version = checked(latest + 1);
        var row = new SalesQuoteDefaultValidityVersionRow
        {
            CompanyId = companyId,
            Version = version,
            ProfileId = Guid.NewGuid(),
            ValidityDays = days,
            IsActive = true,
            EffectiveFrom = effectiveFrom,
            SourceKind = SalesPolicySourceKindsV1.UserMaintained,
            DecisionReference = null,
            RecordedAtUtc = timeProvider.GetUtcNow(),
            RecordedBy = metadata.ActorId,
            Reason = reason,
        };
        database.SalesQuoteDefaultValidityVersions.Add(row);
        Audit(QuotePermission, companyId, QuoteValidityRecordedEvent, QuoteEntityType, subject, "RecordVersion",
            latest == 0 ? null : latest, version, scope, new
            {
                CompanyId = companyId,
                row.ProfileId,
                row.Version,
                row.ValidityDays,
                row.IsActive,
                row.EffectiveFrom,
                SupersedesVersion = timeline
                    .Where(entry => !entry.IsSuperseded && entry.EffectiveFrom == effectiveFrom)
                    .Select(entry => (long?)entry.Version)
                    .SingleOrDefault(),
            }, reason, metadata);
        await RequireStillAsync(QuotePermission, companyId, cancellationToken);
        return Done("quote-default-validity", metadata, subject, await SaveChangeAsync(QuoteValidityCommandName,
            Answers.NoContent(), keyHash, requestHash, metadata, Answers.VersionConflict(), cancellationToken));
    }

    /// <summary>One validated loss reason of a request (code and label exactly as sent: no trimming, no case change).</summary>
    private sealed record LossReasonInput(Guid? ReasonId, string Code, string Label, bool RequiresExplanation, bool IsActive);

    private static bool TryReadLossReasons(IReadOnlyList<RecordSalesQuoteLossReasonInputV1>? items,
        out LossReasonInput[] reasons)
    {
        reasons = [];
        if (items is null || items.Count == 0 || items.Count > SalesQuoteAndEInvoicePolicyInputLimitsV1.LossReasonMaximumCount)
        {
            return false;
        }

        var read = new List<LossReasonInput>(items.Count);
        foreach (var item in items)
        {
            if (item is not { Code: { } code, Label: { } label, RequiresExplanation: { } requiresExplanation, IsActive: { } isActive }
                || item.ReasonId == Guid.Empty
                || !IsTrimmedText(code, SalesQuoteAndEInvoicePolicyInputLimitsV1.LossReasonCodeMaximumLength)
                || !IsTrimmedText(label, SalesQuoteAndEInvoicePolicyInputLimitsV1.LossReasonLabelMaximumLength))
            {
                return false;
            }

            read.Add(new LossReasonInput(item.ReasonId, code, label, requiresExplanation, isActive));
        }

        var givenIds = read.Where(item => item.ReasonId is not null).Select(item => item.ReasonId!.Value).ToArray();
        if (givenIds.Distinct().Count() != givenIds.Length
            || read.Select(item => item.Code).Distinct(StringComparer.Ordinal).Count() != read.Count)
        {
            return false;
        }

        reasons = [.. read];
        return true;
    }

    private static bool IsTrimmedText(string text, int maximumLength) =>
        text.Length > 0 && text.Length <= maximumLength && !string.IsNullOrWhiteSpace(text)
        && string.Equals(text, text.Trim(), StringComparison.Ordinal);

    /// <summary>A catalog list that the entry columns hold unchanged (the column checks and lengths).</summary>
    private static bool FitsLossReasonColumns(IReadOnlyList<ApprovedSalesQuoteLossReasonEntry> reasons) =>
        reasons.Count > 0
        && reasons.All(reason => reason.ReasonId != Guid.Empty
            && IsTrimmedText(reason.Code, SalesQuoteAndEInvoicePolicyInputLimitsV1.LossReasonCodeMaximumLength)
            && IsTrimmedText(reason.Label, SalesQuoteAndEInvoicePolicyInputLimitsV1.LossReasonLabelMaximumLength))
        && reasons.Select(reason => reason.ReasonId).Distinct().Count() == reasons.Count
        && reasons.Select(reason => reason.Code).Distinct(StringComparer.Ordinal).Count() == reasons.Count;

    /// <summary>Across all versions of a company one reason id keeps one code and one code belongs to one reason id.</summary>
    private static bool KeepsLossReasonIdentities(IEnumerable<IEnumerable<(Guid ReasonId, string Code)>> versions)
    {
        var pairs = versions.SelectMany(version => version).Distinct().ToArray();
        return pairs.Select(pair => pair.ReasonId).Distinct().Count() == pairs.Length
            && pairs.Select(pair => pair.Code).Distinct(StringComparer.Ordinal).Count() == pairs.Length;
    }

    /// <summary>Whether the request repeats the stored list exactly (same reasons, same order, same texts and flags).</summary>
    private static bool SameLossReasons(IReadOnlyList<SalesQuoteLossReasonEntryRow> stored, IReadOnlyList<LossReasonInput> reasons) =>
        stored.Count == reasons.Count
        && stored.Zip(reasons).All(pair => pair.Second.ReasonId == pair.First.ReasonId
            && string.Equals(pair.First.Code, pair.Second.Code, StringComparison.Ordinal)
            && string.Equals(pair.First.Label, pair.Second.Label, StringComparison.Ordinal)
            && pair.First.RequiresExplanation == pair.Second.RequiresExplanation
            && pair.First.IsActive == pair.Second.IsActive);

    private void AddLossReasonEntries(Guid companyId, long listVersion,
        IEnumerable<(Guid ReasonId, string Code, string Label, bool RequiresExplanation, bool IsActive)> reasons)
    {
        var position = 0;
        foreach (var reason in reasons)
        {
            database.SalesQuoteLossReasonEntries.Add(new SalesQuoteLossReasonEntryRow
            {
                CompanyId = companyId,
                ListVersion = listVersion,
                Position = position++,
                ReasonId = reason.ReasonId,
                Code = reason.Code,
                Label = reason.Label,
                RequiresExplanation = reason.RequiresExplanation,
                IsActive = reason.IsActive,
            });
        }
    }

    private static SalesPolicyCommandAnswerV1 LossReasonIdentityConflict() =>
        Answers.Problem(422, SalesQuoteAndEInvoicePolicyProblemCodesV1.LossReasonIdentityConflict, "Kayıp nedeni kimliği tutarsız.",
            "Kayıtlı bir neden kimliği ve kodu birlikte korunmalıdır: bilinmeyen bir kimlik verilemez, kayıtlı bir nedenin kodu değiştirilemez ve başka bir nedene ait kod yeni nedene verilemez.");

    private static SalesQuoteLossReasonListVersionViewV1 LossReasonListView(
        SalesPolicyTimelineEntryV1<StoredSalesQuoteLossReasonList> entry, DateOnly today) =>
        new SalesQuoteLossReasonListVersionViewV1(
            entry.Value.Header.ProfileId,
            Text(entry.Version),
            entry.Value.Entries.Select(reason => new SalesQuoteLossReasonViewV1(reason.ReasonId, reason.Code, reason.Label,
                reason.RequiresExplanation, reason.IsActive)).ToArray(),
            entry.Value.Header.IsActive,
            entry.EffectiveFrom,
            entry.EffectiveThrough,
            SalesPolicyTimelineV1.State(entry, today),
            entry.Value.Header.SourceKind,
            entry.Value.Header.DecisionReference,
            entry.Value.Header.RecordedBy,
            entry.Value.Header.RecordedAtUtc,
            entry.Value.Header.Reason);

    private static SalesQuoteDefaultValidityVersionViewV1 DefaultValidityView(
        SalesPolicyTimelineEntryV1<SalesQuoteDefaultValidityVersionRow> entry, DateOnly today) =>
        new SalesQuoteDefaultValidityVersionViewV1(
            entry.Value.ProfileId,
            Text(entry.Version),
            entry.Value.ValidityDays,
            entry.Value.IsActive,
            entry.EffectiveFrom,
            entry.EffectiveThrough,
            SalesPolicyTimelineV1.State(entry, today),
            entry.Value.SourceKind,
            entry.Value.DecisionReference,
            entry.Value.RecordedBy,
            entry.Value.RecordedAtUtc,
            entry.Value.Reason);
}
