using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The order-approval amount threshold (SAT-D6), one timeline per company and currency: enabled with a positive amount (the
/// spelling it was entered with, its scale within the currency's money profile) or disabled (no amount-based routing). The
/// reviewed catalog answers until the one-time baseline transfer; afterwards only the stored versions do. Whether a threshold
/// is enabled and its amount are never chosen here (OPEN-009 KISMEN): the person records them.
/// </summary>
internal sealed partial class SettingsSalesPolicyAdministration
{
    private const string ThresholdPermission = SettingsSalesPolicyPermissionManifestV1.ApprovalThresholdManage;
    private const string ThresholdEntityType = "Settings.SalesApprovalAmountThreshold";
    private const string ThresholdBaselineCommandName = "settings.sales-approval-threshold.baseline-transfer.v1";
    private const string ThresholdRecordCommandName = "settings.sales-approval-threshold.record.v1";
    private const string ThresholdBaselineEvent = "settings.sales-approval-threshold.baseline-transferred.v1";
    private const string ThresholdRecordedEvent = "settings.sales-approval-threshold.recorded.v1";

    public async Task<SalesApprovalThresholdsViewV1> GetApprovalThresholdsAsync(CancellationToken cancellationToken = default)
    {
        var companyId = RequireContextCompany();
        await RequireAsync(ThresholdPermission, companyId, cancellationToken);
        var (transferred, rows) = await ReadAsync(async () => (
            await SalesPolicyReads.IsTransferredAsync(database, SalesPolicyFamiliesV1.ApprovalAmountThreshold, cancellationToken),
            await SalesPolicyReads.LoadThresholdsAsync(database, null, cancellationToken)));
        await RequireStillAsync(ThresholdPermission, companyId, cancellationToken);
        var today = SettingsBusinessDate.Today(timeProvider);
        if (!transferred)
        {
            var baseline = Catalog.ApprovalAmountThresholds
                .Where(entry => entry.CompanyId == companyId)
                .GroupBy(entry => entry.CurrencyId)
                .OrderBy(currency => currency.Key)
                .Select(currency => new SalesApprovalThresholdCurrencyViewV1(
                    currency.Key,
                    Text(0),
                    currency
                        .OrderBy(entry => entry.EffectiveFrom)
                        .ThenBy(entry => entry.Version)
                        .Select(entry => new SalesApprovalThresholdVersionViewV1(
                            entry.ProfileId,
                            Text(entry.Version),
                            entry.IsEnabled,
                            entry.ThresholdAmountText,
                            entry.IsActive,
                            entry.EffectiveFrom,
                            entry.EffectiveThrough,
                            CatalogState(entry.EffectiveFrom, entry.EffectiveThrough, today),
                            SalesPolicySourceKindsV1.CompiledBaselineV0,
                            entry.Source,
                            RecordedBy: null,
                            RecordedAtUtc: null,
                            Reason: null))
                        .ToArray()))
                .ToArray();
            return new SalesApprovalThresholdsViewV1(companyId, today, false, baseline);
        }

        var currencies = rows
            .GroupBy(row => row.CurrencyId)
            .OrderBy(currency => currency.Key)
            .Select(currency =>
            {
                var timeline = SalesPolicyReads.ThresholdTimeline(currency);
                return new SalesApprovalThresholdCurrencyViewV1(currency.Key, Text(SalesPolicyTimelineV1.LatestVersion(timeline)),
                    timeline.Select(entry => ThresholdView(entry, today)).ToArray());
            })
            .ToArray();
        return new SalesApprovalThresholdsViewV1(companyId, today, true, currencies);
    }

    public async Task<SalesPolicyCommandResultV1> TransferApprovalThresholdBaselineAsync(
        TransferSalesPolicyBaselineRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(ThresholdPermission, companyId, cancellationToken);
        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var subject = companyId.ToString("D");
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            Family = SalesPolicyFamiliesV1.ApprovalAmountThreshold, Reason = reason,
        });
        if (await FindExecutionAsync(ThresholdBaselineCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(ThresholdPermission, companyId, cancellationToken);
            return Done("threshold-baseline", metadata, subject, Replay(existing, requestHash, metadata));
        }

        var transferred = await ReadAsync(() =>
            SalesPolicyReads.IsTransferredAsync(database, SalesPolicyFamiliesV1.ApprovalAmountThreshold, cancellationToken));
        if (transferred)
        {
            await RequireStillAsync(ThresholdPermission, companyId, cancellationToken);
            return Done("threshold-baseline", metadata, subject, await StoreRefusalAsync(ThresholdBaselineCommandName,
                Answers.AlreadyTransferred(), keyHash, requestHash, metadata, cancellationToken));
        }

        var entries = Catalog.ApprovalAmountThresholds
            .Where(entry => entry.CompanyId == companyId)
            .OrderBy(entry => entry.CurrencyId)
            .ThenBy(entry => entry.EffectiveFrom)
            .ToArray();
        foreach (var currency in entries.GroupBy(entry => entry.CurrencyId))
        {
            RequireChainedWindows(currency.ToArray(), entry => entry.EffectiveFrom, entry => entry.EffectiveThrough,
                entry => entry.Source);
        }

        if (entries.Select(entry => entry.ProfileId).Distinct().Count() != entries.Length)
        {
            throw new SettingsPrerequisiteException("settings_profile_invalid");
        }

        var now = timeProvider.GetUtcNow();
        foreach (var entry in entries)
        {
            decimal? amount = null;
            byte? scale = null;
            if (entry.IsEnabled)
            {
                if (!SalesPolicyTimelineV1.TryParseAmount(entry.ThresholdAmountText, out var parsed, out var parsedScale))
                {
                    throw new SettingsPrerequisiteException("settings_profile_invalid");
                }

                amount = parsed;
                scale = checked((byte)parsedScale);
            }
            else if (entry.ThresholdAmountText is not null)
            {
                throw new SettingsPrerequisiteException("settings_profile_invalid");
            }

            database.SalesApprovalAmountThresholdVersions.Add(new SalesApprovalAmountThresholdVersionRow
            {
                CompanyId = companyId,
                CurrencyId = entry.CurrencyId,
                Version = entry.Version,
                ProfileId = entry.ProfileId,
                IsEnabled = entry.IsEnabled,
                ThresholdAmount = amount,
                ThresholdAmountScale = scale,
                IsActive = entry.IsActive,
                EffectiveFrom = entry.EffectiveFrom,
                SourceKind = SalesPolicySourceKindsV1.CompiledBaselineV0,
                DecisionReference = entry.Source,
                RecordedAtUtc = now,
                RecordedBy = metadata.ActorId,
                Reason = reason,
            });
        }

        database.SalesPolicyBaselineTransfers.Add(new SalesPolicyBaselineTransferRow
        {
            CompanyId = companyId,
            PolicyFamily = SalesPolicyFamiliesV1.ApprovalAmountThreshold,
            EntryCount = entries.Length,
            TransferredAtUtc = now,
            TransferredBy = metadata.ActorId,
            Reason = reason,
        });
        Audit(ThresholdPermission, companyId, ThresholdBaselineEvent, ThresholdEntityType, subject, "TransferBaseline", null,
            null, scope, new
            {
                CompanyId = companyId,
                PolicyFamily = SalesPolicyFamiliesV1.ApprovalAmountThreshold,
                EntryCount = entries.Length,
                Versions = entries.Select(entry => new
                {
                    entry.CurrencyId,
                    entry.ProfileId,
                    entry.Version,
                    entry.IsEnabled,
                    Amount = entry.ThresholdAmountText,
                    entry.IsActive,
                    entry.EffectiveFrom,
                    entry.EffectiveThrough,
                    DecisionReference = entry.Source,
                }).ToArray(),
            }, reason, metadata);
        await RequireStillAsync(ThresholdPermission, companyId, cancellationToken);
        return Done("threshold-baseline", metadata, subject, await SaveChangeAsync(ThresholdBaselineCommandName,
            Answers.NoContent(), keyHash, requestHash, metadata, Answers.AlreadyTransferred(), cancellationToken));
    }

    public async Task<SalesPolicyCommandResultV1> RecordApprovalThresholdAsync(Guid currencyId,
        RecordSalesApprovalThresholdRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(ThresholdPermission, companyId, cancellationToken);
        if (currencyId == Guid.Empty || !TryParseVersion(request.ExpectedVersion, 0, out var expectedVersion))
        {
            return Unstored(Answers.Invalid("Para birimi ve beklenen sürüm (\"0\" veya pozitif tam sayı metni) zorunludur."), metadata);
        }

        decimal amount = 0m;
        var scale = 0;
        if (request.IsEnabled is not { } enabled
            || (enabled && !SalesPolicyTimelineV1.TryParseAmount(request.Amount, out amount, out scale))
            || (!enabled && request.Amount is not null))
        {
            return Unstored(Answers.Invalid(
                $"Eşik açık ya da kapalı olarak belirtilmelidir; açık eşik 0'dan büyük, başında sıfır olmayan, en çok {SalesPolicyInputLimitsV1.AmountMaximumIntegerDigits} tam ve {SalesPolicyInputLimitsV1.AmountMaximumScale} ondalık basamaklı tutar metni taşır, kapalı eşik tutar taşımaz."),
                metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var amountText = enabled ? SalesPolicyTimelineV1.FormatAmount(amount, scale) : null;
        var subject = currencyId.ToString("D");
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            CurrencyId = currencyId,
            ExpectedVersion = expectedVersion,
            IsEnabled = enabled,
            Amount = amountText,
            Reason = reason,
            request.EffectiveFrom,
        });
        if (await FindExecutionAsync(ThresholdRecordCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(ThresholdPermission, companyId, cancellationToken);
            return Done("threshold-record", metadata, subject, Replay(existing, requestHash, metadata));
        }

        var (transferred, rows) = await ReadAsync(async () => (
            await SalesPolicyReads.IsTransferredAsync(database, SalesPolicyFamiliesV1.ApprovalAmountThreshold, cancellationToken),
            await SalesPolicyReads.LoadThresholdsAsync(database, currencyId, cancellationToken)));
        var timeline = SalesPolicyReads.ThresholdTimeline(rows);
        var latest = SalesPolicyTimelineV1.LatestVersion(timeline);
        var today = SettingsBusinessDate.Today(timeProvider);
        var effectiveFrom = request.EffectiveFrom ?? today;
        var profile = MoneyProfiles.VersionFor(companyId, currencyId, effectiveFrom);
        var refusal = !transferred ? Answers.BaselineRequired()
            : latest != expectedVersion ? Answers.VersionConflict()
            : effectiveFrom < today ? Answers.EffectiveFromPast()
            : profile is null || !profile.IsActive || profile.EffectiveFrom > effectiveFrom
              || (profile.EffectiveThrough is { } profileThrough && profileThrough < effectiveFrom) ? Answers.CurrencyNotServed()
            : enabled && scale > profile.Scale ? Answers.AmountScaleExceeded(profile.Scale)
            : null;
        if (refusal is not null)
        {
            await RequireStillAsync(ThresholdPermission, companyId, cancellationToken);
            return Done("threshold-record", metadata, subject,
                await StoreRefusalAsync(ThresholdRecordCommandName, refusal, keyHash, requestHash, metadata, cancellationToken));
        }

        var inForce = SalesPolicyTimelineV1.InForceOn(timeline, effectiveFrom);
        if (inForce is { Value.IsActive: true } && inForce.Value.IsEnabled == enabled
            && (!enabled || (inForce.Value.ThresholdAmount == amount && inForce.Value.ThresholdAmountScale == scale)))
        {
            await RequireStillAsync(ThresholdPermission, companyId, cancellationToken);
            return Done("threshold-record", metadata, subject, await StoreRefusalAsync(ThresholdRecordCommandName,
                Answers.NoContent(), keyHash, requestHash, metadata, cancellationToken));
        }

        if (SettingsBusinessDate.Today(timeProvider) != today)
        {
            return Done("threshold-record", metadata, subject, Unstored(Answers.VersionConflict(), metadata));
        }

        var version = checked(latest + 1);
        var row = new SalesApprovalAmountThresholdVersionRow
        {
            CompanyId = companyId,
            CurrencyId = currencyId,
            Version = version,
            ProfileId = Guid.NewGuid(),
            IsEnabled = enabled,
            ThresholdAmount = enabled ? amount : null,
            ThresholdAmountScale = enabled ? checked((byte)scale) : null,
            IsActive = true,
            EffectiveFrom = effectiveFrom,
            SourceKind = SalesPolicySourceKindsV1.UserMaintained,
            DecisionReference = null,
            RecordedAtUtc = timeProvider.GetUtcNow(),
            RecordedBy = metadata.ActorId,
            Reason = reason,
        };
        database.SalesApprovalAmountThresholdVersions.Add(row);
        Audit(ThresholdPermission, companyId, ThresholdRecordedEvent, ThresholdEntityType, subject, "RecordVersion",
            latest == 0 ? null : latest, version, scope, new
            {
                CompanyId = companyId,
                CurrencyId = currencyId,
                row.ProfileId,
                row.Version,
                IsEnabled = enabled,
                Amount = amountText,
                row.IsActive,
                row.EffectiveFrom,
                SupersedesVersion = timeline
                    .Where(entry => !entry.IsSuperseded && entry.EffectiveFrom == effectiveFrom)
                    .Select(entry => (long?)entry.Version)
                    .SingleOrDefault(),
            }, reason, metadata);
        await RequireStillAsync(ThresholdPermission, companyId, cancellationToken);
        return Done("threshold-record", metadata, subject, await SaveChangeAsync(ThresholdRecordCommandName,
            Answers.NoContent(), keyHash, requestHash, metadata, Answers.VersionConflict(), cancellationToken));
    }

    private static SalesApprovalThresholdVersionViewV1 ThresholdView(
        SalesPolicyTimelineEntryV1<SalesApprovalAmountThresholdVersionRow> entry, DateOnly today) =>
        new SalesApprovalThresholdVersionViewV1(
            entry.Value.ProfileId,
            Text(entry.Version),
            entry.Value.IsEnabled,
            SalesPolicyReads.AmountText(entry.Value),
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
