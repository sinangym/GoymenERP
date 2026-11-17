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
    private static bool UsesAuditOwner(KeySpec spec) => spec.Key is CompanyPolicyKeysV1.SalesContractDiscountLimit
        or CompanyPolicyKeysV1.ManufacturingIncludeScrapCost or CompanyPolicyKeysV1.NotificationActiveDays;

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
}
