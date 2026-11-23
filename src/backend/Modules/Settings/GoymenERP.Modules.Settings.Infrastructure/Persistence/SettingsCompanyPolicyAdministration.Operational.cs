using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

internal sealed partial class SettingsCompanyPolicyAdministration : ISettingsOperationalPolicyAdministrationV1
{
    private static readonly KeySpec ScrapCost = new(CompanyPolicyKeysV1.ManufacturingIncludeScrapCost,
        SettingsOperationalPolicyPermissionsV1.ScrapCostManage, "settings.manufacturing-scrap-cost.record.v1",
        "settings.manufacturing-scrap-cost.recorded.v1", "Settings.ManufacturingScrapCost", "Fire maliyet ayarı sürümü kaydedildi");
    private static readonly KeySpec NotificationRetention = new(CompanyPolicyKeysV1.NotificationActiveDays,
        SettingsOperationalPolicyPermissionsV1.NotificationRetentionManage, "settings.notification-retention.record.v1",
        "settings.notification-retention.recorded.v1", "Settings.NotificationRetention", "Bildirim etkinlik süresi sürümü kaydedildi");
    private static readonly KeySpec WorkApprovalDurationHours = new(CompanyPolicyKeysV1.WorkApprovalDurationHours,
        SettingsOperationalPolicyPermissionsV1.WorkApprovalDurationManage, "settings.work-approval-duration-hours.record.v1",
        "settings.work-approval-duration-hours.recorded.v1", "Settings.WorkApprovalDurationHours", "Saat ölçekli onay süresi sürümü kaydedildi");
    private static readonly KeySpec WorkApprovalDurationBusinessDays = new(CompanyPolicyKeysV1.WorkApprovalDurationBusinessDays,
        SettingsOperationalPolicyPermissionsV1.WorkApprovalDurationManage, "settings.work-approval-duration-business-days.record.v1",
        "settings.work-approval-duration-business-days.recorded.v1", "Settings.WorkApprovalDurationBusinessDays", "İş günü ölçekli onay süresi sürümü kaydedildi");
    // YOL-D25 / YOL-D35 (WP2): the digest times are recorded with settings.work-approval-duration.manage; no separate
    // permission is opened (none granted).
    private static readonly IReadOnlyDictionary<string, (KeySpec Spec, int DecidedDefault)> WorkDigestTimes =
        SettingsWorkDigestScheduleLimitsV1.Slots.ToDictionary(slot => slot.Slot, slot => (new KeySpec(slot.Key,
            SettingsOperationalPolicyPermissionsV1.WorkApprovalDurationManage, $"settings.work-digest-time-{slot.Slot}.record.v1",
            $"settings.work-digest-time-{slot.Slot}.recorded.v1", "Settings.WorkDigestTime", "Günlük özet saati sürümü kaydedildi"),
            slot.DecidedDefault), StringComparer.Ordinal);
    private static bool UsesAuditOwner(KeySpec spec) => spec.Key is CompanyPolicyKeysV1.SalesContractDiscountLimit
        or CompanyPolicyKeysV1.ManufacturingIncludeScrapCost or CompanyPolicyKeysV1.NotificationActiveDays
        or CompanyPolicyKeysV1.WorkApprovalDurationHours or CompanyPolicyKeysV1.WorkApprovalDurationBusinessDays
        or CompanyPolicyKeysV1.WorkDigestTimeMorning or CompanyPolicyKeysV1.WorkDigestTimeMidday or CompanyPolicyKeysV1.WorkDigestTimeEvening
        or WorkHolidayCalendarSpecKey or AiGatewaySpecKey;

    public async Task<ManufacturingScrapCostPolicyViewV1> GetScrapCostAsync(CancellationToken cancellationToken = default)
    {
        var (company, rows) = await ReadHistoryAsync(ScrapCost, cancellationToken);
        if (rows.Any(row => row.BooleanValue is null)) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
        return new(company, rows.FirstOrDefault()?.BooleanValue ?? true, Text(rows.FirstOrDefault()?.Version ?? 0),
            rows.Select(row => new BooleanCompanyPolicyVersionViewV1(Text(row.Version), row.BooleanValue!.Value,
                row.RecordedBy, row.RecordedAtUtc, row.Reason)).ToArray());
    }
    public Task<CompanyPolicyCommandResultV1> RecordScrapCostAsync(RecordManufacturingScrapCostRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (auditWriter is null) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
        return request.IncludeScrapCost is not { } value
            ? RecordAsync(ScrapCost, request.ExpectedVersion, request.Reason, metadata, Answers.Invalid("Fire maliyet ayarı açıkça seçilmelidir."), null, cancellationToken)
            : RecordAsync(ScrapCost, request.ExpectedVersion, request.Reason, metadata, null,
                new Change(new { IncludeScrapCost = value }, row => row is not null && row.BooleanValue == value, null,
                    row => row.BooleanValue = value, row => new { IncludeScrapCost = value, PreviousValue = row?.BooleanValue }), cancellationToken);
    }
    public async Task<NotificationRetentionPolicyViewV1> GetNotificationRetentionAsync(CancellationToken cancellationToken = default)
    {
        var (company, rows) = await ReadHistoryAsync(NotificationRetention, cancellationToken);
        if (rows.Any(row => row.WholeNumberValue is null or < 0)) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
        return new(company, rows.FirstOrDefault()?.WholeNumberValue ?? 90, Text(rows.FirstOrDefault()?.Version ?? 0),
            rows.Select(row => new WholeNumberCompanyPolicyVersionViewV1(Text(row.Version), row.WholeNumberValue!.Value,
                row.RecordedBy, row.RecordedAtUtc, row.Reason)).ToArray());
    }
    public Task<CompanyPolicyCommandResultV1> RecordNotificationRetentionAsync(RecordNotificationRetentionRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (auditWriter is null) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
        return request.ActiveDays is not { } days || days < 0
            ? RecordAsync(NotificationRetention, request.ExpectedVersion, request.Reason, metadata, Answers.Invalid("Bildirim süresi sıfır veya pozitif tam gün olmalıdır."), null, cancellationToken)
            : RecordAsync(NotificationRetention, request.ExpectedVersion, request.Reason, metadata, null,
                new Change(new { ActiveDays = days }, row => row is not null && row.WholeNumberValue == days, null,
                    row => row.WholeNumberValue = days, row => new { ActiveDays = days, PreviousDays = row?.WholeNumberValue }), cancellationToken);
    }

    public async Task<WorkApprovalDurationPolicyViewV1> GetWorkApprovalDurationAsync(CancellationToken cancellationToken = default)
    {
        var (company, hours) = await ReadHistoryAsync(WorkApprovalDurationHours, cancellationToken);
        var (businessCompany, days) = await ReadHistoryAsync(WorkApprovalDurationBusinessDays, cancellationToken);
        if (company != businessCompany || hours.Concat(days).Any(row => row.WholeNumberValue is null or < 1))
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
        return new(company, DurationView(hours, SettingsWorkApprovalDurationLimitsV1.DecidedDefaultHours),
            DurationView(days, SettingsWorkApprovalDurationLimitsV1.DecidedDefaultBusinessDays));
    }
    public Task<CompanyPolicyCommandResultV1> RecordWorkApprovalDurationHoursAsync(RecordWorkApprovalDurationRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default) =>
        RecordWorkApprovalDurationAsync(WorkApprovalDurationHours, SettingsWorkApprovalDurationLimitsV1.MaximumHours,
            $"Saat ölçekli onay süresi 1 ile {SettingsWorkApprovalDurationLimitsV1.MaximumHours} arasında tam saat olmalıdır.",
            request, metadata, cancellationToken);
    public Task<CompanyPolicyCommandResultV1> RecordWorkApprovalDurationBusinessDaysAsync(RecordWorkApprovalDurationRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default) =>
        RecordWorkApprovalDurationAsync(WorkApprovalDurationBusinessDays, SettingsWorkApprovalDurationLimitsV1.MaximumBusinessDays,
            $"İş günü ölçekli onay süresi 1 ile {SettingsWorkApprovalDurationLimitsV1.MaximumBusinessDays} arasında tam iş günü olmalıdır.",
            request, metadata, cancellationToken);
    private Task<CompanyPolicyCommandResultV1> RecordWorkApprovalDurationAsync(KeySpec spec, int maximum, string invalidText,
        RecordWorkApprovalDurationRequestV1 request, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (auditWriter is null) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
        return request.Value is not { } value || value < 1 || value > maximum
            ? RecordAsync(spec, request.ExpectedVersion, request.Reason, metadata, Answers.Invalid(invalidText), null, cancellationToken)
            : RecordAsync(spec, request.ExpectedVersion, request.Reason, metadata, null,
                new Change(new { Value = value }, row => row is not null && row.WholeNumberValue == value, null,
                    row => row.WholeNumberValue = value, row => new { Value = value, PreviousValue = row?.WholeNumberValue }), cancellationToken);
    }
    /// <summary>YOL-D25: the three digest times; without a recorded version the decided default is in force as version "0".</summary>
    public async Task<WorkDigestScheduleViewV1> GetWorkDigestScheduleAsync(CancellationToken cancellationToken = default)
    {
        Guid? company = null;
        var times = new List<WorkDigestTimeViewV1>(WorkDigestTimes.Count);
        foreach (var (slot, _, _) in SettingsWorkDigestScheduleLimitsV1.Slots)
        {
            var (spec, decidedDefault) = WorkDigestTimes[slot];
            var (current, rows) = await ReadHistoryAsync(spec, cancellationToken);
            if (company is { } known && known != current
                || rows.Any(row => row.WholeNumberValue is null or < 0 or > SettingsWorkDigestScheduleLimitsV1.MaximumMinute))
                throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
            company = current;
            times.Add(new(slot, rows.FirstOrDefault()?.WholeNumberValue ?? decidedDefault, Text(rows.FirstOrDefault()?.Version ?? 0),
                rows.Select(row => new WholeNumberCompanyPolicyVersionViewV1(Text(row.Version), row.WholeNumberValue!.Value,
                    row.RecordedBy, row.RecordedAtUtc, row.Reason)).ToArray()));
        }
        return new(company!.Value, true, times);
    }
    public Task<CompanyPolicyCommandResultV1> RecordWorkDigestTimeAsync(string slot, RecordWorkDigestTimeRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (auditWriter is null) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
        if (slot is null || !WorkDigestTimes.TryGetValue(slot, out var entry))
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.RequestInvalid);
        return request.Minute is not { } minute || minute < 0 || minute > SettingsWorkDigestScheduleLimitsV1.MaximumMinute
            ? RecordAsync(entry.Spec, request.ExpectedVersion, request.Reason, metadata,
                Answers.Invalid("Özet saati gece yarısından sonra 0 ile 1439 arasında tam dakika olmalıdır."), null, cancellationToken)
            : RecordAsync(entry.Spec, request.ExpectedVersion, request.Reason, metadata, null,
                new Change(new { Minute = minute }, row => row is not null && row.WholeNumberValue == minute, null,
                    row => row.WholeNumberValue = minute, row => new { Minute = minute, PreviousMinute = row?.WholeNumberValue }), cancellationToken);
    }
    /// <summary>YOL-D18 (WP1 D3): without a recorded version the decided default is in force as version "0".</summary>
    private static WorkApprovalDurationClassViewV1 DurationView(CompanyPolicyVersionRow[] rows, int decidedDefault) =>
        new(rows.FirstOrDefault()?.WholeNumberValue ?? decidedDefault, Text(rows.FirstOrDefault()?.Version ?? 0),
            rows.Select(row => new WholeNumberCompanyPolicyVersionViewV1(Text(row.Version), row.WholeNumberValue!.Value,
                row.RecordedBy, row.RecordedAtUtc, row.Reason)).ToArray());
}
