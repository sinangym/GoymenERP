namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>SAT-D358, SAT-D364(1), KLN-D109: separate company setting. Registration only; the designated human enters it
/// after an explicit grant. No role, person identity, default, order-limit grant or Critical classification is inferred.</summary>
public static class SettingsSalesContractDiscountLimitPermissionsV1
{
    public const string Manage = "settings.sales-contract-discount-limit.manage";
}

public sealed record RecordSalesContractDiscountLimitRequestV1(string? ExpectedVersion, string? LimitPercent, string? Reason);
public sealed record SalesContractDiscountLimitVersionViewV1(string Version, string LimitPercent, string RecordedBy,
    DateTimeOffset RecordedAtUtc, string Reason);
public sealed record SalesContractDiscountLimitViewV1(Guid CompanyId, string? LimitPercent, string Version,
    IReadOnlyList<SalesContractDiscountLimitVersionViewV1> Versions);

public interface ISettingsSalesContractDiscountLimitAdministrationV1
{
    Task<SalesContractDiscountLimitViewV1> GetSalesContractDiscountLimitAsync(CancellationToken cancellationToken = default);
    Task<CompanyPolicyCommandResultV1> RecordSalesContractDiscountLimitAsync(RecordSalesContractDiscountLimitRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}

/// <summary>A missing row is null/version0, never zero or unlimited. Consumer reads require company access, not editor authority.</summary>
public sealed record SettingsSalesContractDiscountLimitV1(Guid CompanyId, decimal? LimitPercent, long Version);
public interface ISettingsSalesContractDiscountLimitProviderV1
{
    Task<SettingsSalesContractDiscountLimitV1> GetContractDiscountLimitAsync(Guid companyId, CancellationToken cancellationToken = default);
}
