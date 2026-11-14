using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The company's delivery tolerance (SAT-D5): warn ≤ approval ≤ block percents of an over-delivery, one timeline per
/// company. The reviewed catalog answers until the one-time baseline transfer; afterwards only the stored versions do.
/// </summary>
internal sealed partial class SettingsSalesPolicyAdministration
{
    private const string TolerancePermission = SettingsSalesPolicyPermissionManifestV1.DeliveryToleranceManage;
    private const string ToleranceEntityType = "Settings.SalesDeliveryTolerance";
    private const string ToleranceBaselineCommandName = "settings.sales-delivery-tolerance.baseline-transfer.v1";
    private const string ToleranceRecordCommandName = "settings.sales-delivery-tolerance.record.v1";
    private const string ToleranceBaselineEvent = "settings.sales-delivery-tolerance.baseline-transferred.v1";
    private const string ToleranceRecordedEvent = "settings.sales-delivery-tolerance.recorded.v1";

    /// <summary>A percent's upper bound (the column check's 100; the same bound the reviewed catalog applies).</summary>
    private const decimal MaximumPercent = 100m;

    public async Task<SalesDeliveryToleranceViewV1> GetDeliveryToleranceAsync(CancellationToken cancellationToken = default)
    {
        var companyId = RequireContextCompany();
        await RequireAsync(TolerancePermission, companyId, cancellationToken);
        var (transferred, rows) = await ReadAsync(async () => (
            await SalesPolicyReads.IsTransferredAsync(database, SalesPolicyFamiliesV1.DeliveryTolerance, cancellationToken),
            await SalesPolicyReads.LoadTolerancesAsync(database, cancellationToken)));
        await RequireStillAsync(TolerancePermission, companyId, cancellationToken);
        var today = SettingsBusinessDate.Today(timeProvider);
        if (!transferred)
        {
            var baseline = Catalog.DeliveryTolerances
                .Where(entry => entry.CompanyId == companyId)
                .OrderBy(entry => entry.EffectiveFrom)
                .ThenBy(entry => entry.Version)
                .Select(entry => new SalesDeliveryToleranceVersionViewV1(
                    entry.ProfileId,
                    Text(entry.Version),
                    SalesDiscountLimitCalculatorV1.Format(entry.WarnPercent),
                    SalesDiscountLimitCalculatorV1.Format(entry.ApprovalPercent),
                    SalesDiscountLimitCalculatorV1.Format(entry.BlockPercent),
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
            return new SalesDeliveryToleranceViewV1(companyId, today, false, Text(0), baseline);
        }

        var timeline = SalesPolicyReads.ToleranceTimeline(rows);
        return new SalesDeliveryToleranceViewV1(companyId, today, true, Text(SalesPolicyTimelineV1.LatestVersion(timeline)),
            timeline.Select(entry => ToleranceView(entry, today)).ToArray());
    }

    public async Task<SalesPolicyCommandResultV1> TransferDeliveryToleranceBaselineAsync(
        TransferSalesPolicyBaselineRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(TolerancePermission, companyId, cancellationToken);
        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var subject = companyId.ToString("D");
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new { Family = SalesPolicyFamiliesV1.DeliveryTolerance, Reason = reason });
        if (await FindExecutionAsync(ToleranceBaselineCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(TolerancePermission, companyId, cancellationToken);
            return Done("tolerance-baseline", metadata, subject, Replay(existing, requestHash, metadata));
        }

        var transferred = await ReadAsync(() =>
            SalesPolicyReads.IsTransferredAsync(database, SalesPolicyFamiliesV1.DeliveryTolerance, cancellationToken));
        if (transferred)
        {
            await RequireStillAsync(TolerancePermission, companyId, cancellationToken);
            return Done("tolerance-baseline", metadata, subject, await StoreRefusalAsync(ToleranceBaselineCommandName,
                Answers.AlreadyTransferred(), keyHash, requestHash, metadata, cancellationToken));
        }

        var entries = Catalog.DeliveryTolerances
            .Where(entry => entry.CompanyId == companyId)
            .OrderBy(entry => entry.EffectiveFrom)
            .ToArray();
        RequireChainedWindows(entries, entry => entry.EffectiveFrom, entry => entry.EffectiveThrough, entry => entry.Source);
        if (entries.Select(entry => entry.ProfileId).Distinct().Count() != entries.Length
            || entries.Any(entry => !FitsPercentColumn(entry.WarnPercent) || !FitsPercentColumn(entry.ApprovalPercent)
                                    || !FitsPercentColumn(entry.BlockPercent)))
        {
            throw new SettingsPrerequisiteException("settings_profile_invalid");
        }

        var now = timeProvider.GetUtcNow();
        foreach (var entry in entries)
        {
            database.SalesDeliveryToleranceVersions.Add(new SalesDeliveryToleranceVersionRow
            {
                CompanyId = companyId,
                Version = entry.Version,
                ProfileId = entry.ProfileId,
                WarnPercent = entry.WarnPercent,
                ApprovalPercent = entry.ApprovalPercent,
                BlockPercent = entry.BlockPercent,
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
            PolicyFamily = SalesPolicyFamiliesV1.DeliveryTolerance,
            EntryCount = entries.Length,
            TransferredAtUtc = now,
            TransferredBy = metadata.ActorId,
            Reason = reason,
        });
        Audit(TolerancePermission, companyId, ToleranceBaselineEvent, ToleranceEntityType, subject, "TransferBaseline", null,
            entries.Length == 0 ? null : entries.Max(entry => entry.Version), scope, new
            {
                CompanyId = companyId,
                PolicyFamily = SalesPolicyFamiliesV1.DeliveryTolerance,
                EntryCount = entries.Length,
                Versions = entries.Select(entry => new
                {
                    entry.ProfileId,
                    entry.Version,
                    WarnPercent = SalesDiscountLimitCalculatorV1.Format(entry.WarnPercent),
                    ApprovalPercent = SalesDiscountLimitCalculatorV1.Format(entry.ApprovalPercent),
                    BlockPercent = SalesDiscountLimitCalculatorV1.Format(entry.BlockPercent),
                    entry.IsActive,
                    entry.EffectiveFrom,
                    entry.EffectiveThrough,
                    DecisionReference = entry.Source,
                }).ToArray(),
            }, reason, metadata);
        await RequireStillAsync(TolerancePermission, companyId, cancellationToken);
        return Done("tolerance-baseline", metadata, subject, await SaveChangeAsync(ToleranceBaselineCommandName,
            Answers.NoContent(), keyHash, requestHash, metadata, Answers.AlreadyTransferred(), cancellationToken));
    }

    public async Task<SalesPolicyCommandResultV1> RecordDeliveryToleranceAsync(RecordSalesDeliveryToleranceRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(TolerancePermission, companyId, cancellationToken);
        if (!TryParseVersion(request.ExpectedVersion, 0, out var expectedVersion))
        {
            return Unstored(Answers.Invalid("Beklenen sürüm (\"0\" veya pozitif tam sayı metni) zorunludur."), metadata);
        }

        if (!SalesDiscountLimitCalculatorV1.TryParsePercent(request.WarnPercent, out var warn)
            || !SalesDiscountLimitCalculatorV1.TryParsePercent(request.ApprovalPercent, out var approval)
            || !SalesDiscountLimitCalculatorV1.TryParsePercent(request.BlockPercent, out var block)
            || warn > approval || approval > block)
        {
            return Unstored(Answers.Invalid(
                "Uyarı, onay ve engel yüzdeleri 0 ile 100 arasında, en çok 4 ondalık basamaklı sayı metni olmalı; uyarı ≤ onay ≤ engel sırası korunmalıdır."),
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
            WarnPercent = SalesDiscountLimitCalculatorV1.Format(warn),
            ApprovalPercent = SalesDiscountLimitCalculatorV1.Format(approval),
            BlockPercent = SalesDiscountLimitCalculatorV1.Format(block),
            Reason = reason,
            request.EffectiveFrom,
        });
        if (await FindExecutionAsync(ToleranceRecordCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(TolerancePermission, companyId, cancellationToken);
            return Done("tolerance-record", metadata, subject, Replay(existing, requestHash, metadata));
        }

        var (transferred, rows) = await ReadAsync(async () => (
            await SalesPolicyReads.IsTransferredAsync(database, SalesPolicyFamiliesV1.DeliveryTolerance, cancellationToken),
            await SalesPolicyReads.LoadTolerancesAsync(database, cancellationToken)));
        var timeline = SalesPolicyReads.ToleranceTimeline(rows);
        var latest = SalesPolicyTimelineV1.LatestVersion(timeline);
        var today = SettingsBusinessDate.Today(timeProvider);
        var effectiveFrom = request.EffectiveFrom ?? today;
        var refusal = !transferred ? Answers.BaselineRequired()
            : latest != expectedVersion ? Answers.VersionConflict()
            : effectiveFrom < today ? Answers.EffectiveFromPast()
            : null;
        if (refusal is not null)
        {
            await RequireStillAsync(TolerancePermission, companyId, cancellationToken);
            return Done("tolerance-record", metadata, subject,
                await StoreRefusalAsync(ToleranceRecordCommandName, refusal, keyHash, requestHash, metadata, cancellationToken));
        }

        var inForce = SalesPolicyTimelineV1.InForceOn(timeline, effectiveFrom);
        if (inForce is { Value.IsActive: true } && inForce.Value.WarnPercent == warn && inForce.Value.ApprovalPercent == approval
            && inForce.Value.BlockPercent == block)
        {
            await RequireStillAsync(TolerancePermission, companyId, cancellationToken);
            return Done("tolerance-record", metadata, subject, await StoreRefusalAsync(ToleranceRecordCommandName,
                Answers.NoContent(), keyHash, requestHash, metadata, cancellationToken));
        }

        if (SettingsBusinessDate.Today(timeProvider) != today)
        {
            return Done("tolerance-record", metadata, subject, Unstored(Answers.VersionConflict(), metadata));
        }

        var version = checked(latest + 1);
        var row = new SalesDeliveryToleranceVersionRow
        {
            CompanyId = companyId,
            Version = version,
            ProfileId = Guid.NewGuid(),
            WarnPercent = warn,
            ApprovalPercent = approval,
            BlockPercent = block,
            IsActive = true,
            EffectiveFrom = effectiveFrom,
            SourceKind = SalesPolicySourceKindsV1.UserMaintained,
            DecisionReference = null,
            RecordedAtUtc = timeProvider.GetUtcNow(),
            RecordedBy = metadata.ActorId,
            Reason = reason,
        };
        database.SalesDeliveryToleranceVersions.Add(row);
        Audit(TolerancePermission, companyId, ToleranceRecordedEvent, ToleranceEntityType, subject, "RecordVersion",
            latest == 0 ? null : latest, version, scope, new
            {
                CompanyId = companyId,
                row.ProfileId,
                row.Version,
                WarnPercent = SalesDiscountLimitCalculatorV1.Format(warn),
                ApprovalPercent = SalesDiscountLimitCalculatorV1.Format(approval),
                BlockPercent = SalesDiscountLimitCalculatorV1.Format(block),
                row.IsActive,
                row.EffectiveFrom,
                SupersedesVersion = timeline
                    .Where(entry => !entry.IsSuperseded && entry.EffectiveFrom == effectiveFrom)
                    .Select(entry => (long?)entry.Version)
                    .SingleOrDefault(),
            }, reason, metadata);
        await RequireStillAsync(TolerancePermission, companyId, cancellationToken);
        return Done("tolerance-record", metadata, subject, await SaveChangeAsync(ToleranceRecordCommandName,
            Answers.NoContent(), keyHash, requestHash, metadata, Answers.VersionConflict(), cancellationToken));
    }

    private static SalesDeliveryToleranceVersionViewV1 ToleranceView(
        SalesPolicyTimelineEntryV1<SalesDeliveryToleranceVersionRow> entry, DateOnly today) =>
        new SalesDeliveryToleranceVersionViewV1(
            entry.Value.ProfileId,
            Text(entry.Version),
            SalesDiscountLimitCalculatorV1.Format(entry.Value.WarnPercent),
            SalesDiscountLimitCalculatorV1.Format(entry.Value.ApprovalPercent),
            SalesDiscountLimitCalculatorV1.Format(entry.Value.BlockPercent),
            entry.Value.IsActive,
            entry.EffectiveFrom,
            entry.EffectiveThrough,
            SalesPolicyTimelineV1.State(entry, today),
            entry.Value.SourceKind,
            entry.Value.DecisionReference,
            entry.Value.RecordedBy,
            entry.Value.RecordedAtUtc,
            entry.Value.Reason);

    /// <summary>A catalog entry's place relative to today (the catalog windows are explicit).</summary>
    private static string CatalogState(DateOnly from, DateOnly? through, DateOnly today) =>
        from > today ? SalesPolicyVersionStatesV1.Scheduled
        : through is { } end && end < today ? SalesPolicyVersionStatesV1.Ended
        : SalesPolicyVersionStatesV1.InForce;

    /// <summary>A catalog percent the decimal(7,4) column holds without rounding (0…100, at most four decimals).</summary>
    private static bool FitsPercentColumn(decimal value) =>
        value >= 0m && value <= MaximumPercent && SalesPolicyTimelineV1.Canonical(value).Scale <= SalesPolicyInputLimitsV1.PercentMaximumScale;
}
