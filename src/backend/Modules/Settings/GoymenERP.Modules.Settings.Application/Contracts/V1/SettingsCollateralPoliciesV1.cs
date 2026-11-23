using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SettingsBankInstitutionInputV1(Guid? BankInstitutionId, string? Name, string? EftCode, bool Active);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PrepareSettingsBankInstitutionsV1(string? ExpectedVersion, DateOnly ValidFrom, DateOnly? ValidThrough,
    IReadOnlyList<SettingsBankInstitutionInputV1>? Entries, string? Reason);
public sealed record SettingsBankInstitutionSnapshotV1(Guid BankInstitutionId, string Name, string EftCode, bool Active, string SourceVersion);
public sealed record SettingsBankInstitutionPolicyV1(Guid CompanyId, Guid PolicyId, long Version, long StateVersion,
    SettingsFinancePolicyStateV1 State, DateOnly ValidFrom, DateOnly? ValidThrough, DateOnly? RetiredFrom,
    string PreparedBy, DateTimeOffset PreparedAtUtc, string? PublishedBy, DateTimeOffset? PublishedAtUtc,
    string Reason, IReadOnlyList<SettingsBankInstitutionSnapshotV1> Entries);
public sealed record SettingsBankInstitutionsWorkspaceV1(Guid CompanyId, DateOnly BusinessDate, IReadOnlyList<SettingsBankInstitutionPolicyV1> Versions);
public sealed record SettingsBankInstitutionCatalogV1(SettingsFinancePolicyReadOutcomeV1 Outcome, Guid CompanyId,
    IReadOnlyList<SettingsBankInstitutionSnapshotV1> Entries, string? SourceVersion, string? Reason);

public interface ISettingsBankInstitutionAdministrationV1
{
    Task<SettingsBankInstitutionsWorkspaceV1> GetBankInstitutionsAsync(CancellationToken cancellationToken = default);
    Task<SettingsFinancePolicyCommandResultV1> PrepareBankInstitutionsAsync(PrepareSettingsBankInstitutionsV1 request, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
    Task<SettingsFinancePolicyCommandResultV1> PublishBankInstitutionsAsync(Guid policyId, PublishSettingsFinancePolicyV1 request, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
    Task<SettingsFinancePolicyCommandResultV1> RetireBankInstitutionsAsync(Guid policyId, RetireSettingsFinancePolicyV1 request, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}
/// <summary>Real published company catalog; a missing catalog is unavailable, not an implicit empty list. Consumers keep their own authority.</summary>
public interface ISettingsBankInstitutionCatalogV1
{
    Task<SettingsBankInstitutionCatalogV1> GetBankInstitutionsAsync(Guid companyId, DateOnly businessDate, CancellationToken cancellationToken = default);
    Task<SettingsBankInstitutionSnapshotV1?> ReadBankInstitutionProofAsync(Guid companyId, Guid bankInstitutionId, string sourceVersion, CancellationToken cancellationToken = default);
}

[JsonConverter(typeof(JsonStringEnumConverter<SettingsCollateralRateScopeV1>))]
public enum SettingsCollateralRateScopeV1 { Company = 1, TypeBank = 2, TypeBankRemainingDays = 3 }
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PrepareSettingsCollateralRateV1(string? ExpectedVersion, SettingsCollateralRateScopeV1 Scope,
    Guid? CollateralTypeId, string? ExpectedCollateralTypeVersion, Guid? BankInstitutionId, string? ExpectedBankSourceVersion,
    int? MaximumRemainingDays, string? RatePercent, DateOnly ValidFrom, DateOnly? ValidThrough, string? Reason);
public sealed record SettingsCollateralRatePolicyV1(Guid CompanyId, Guid PolicyId, long Version, long StateVersion,
    SettingsFinancePolicyStateV1 State, DateOnly ValidFrom, DateOnly? ValidThrough, DateOnly? RetiredFrom,
    string PreparedBy, DateTimeOffset PreparedAtUtc, string? PublishedBy, DateTimeOffset? PublishedAtUtc, string Reason,
    SettingsCollateralRateScopeV1 Scope, Guid? CollateralTypeId, string? CollateralTypeVersion, Guid? BankInstitutionId,
    string? BankSourceVersion, int? MaximumRemainingDays, string RatePercent);
public sealed record SettingsCollateralRatesWorkspaceV1(Guid CompanyId, DateOnly BusinessDate,
    SettingsCollateralTypesViewV1 Types, SettingsBankInstitutionCatalogV1 Banks, IReadOnlyList<SettingsCollateralRatePolicyV1> Versions);
public interface ISettingsCollateralRateAdministrationV1
{
    Task<SettingsCollateralRatesWorkspaceV1> GetCollateralRatesAsync(CancellationToken cancellationToken = default);
    Task<SettingsFinancePolicyCommandResultV1> PrepareCollateralRateAsync(PrepareSettingsCollateralRateV1 request, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
    Task<SettingsFinancePolicyCommandResultV1> PublishCollateralRateAsync(Guid policyId, PublishSettingsFinancePolicyV1 request, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
    Task<SettingsFinancePolicyCommandResultV1> RetireCollateralRateAsync(Guid policyId, RetireSettingsFinancePolicyV1 request, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
    Task<SettingsCollateralRateReadV1> PreviewCollateralRateAsync(PreviewSettingsCollateralRateV1 request, CancellationToken cancellationToken = default);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewSettingsCollateralRateV1(Guid CollateralTypeId, Guid? BankInstitutionId, string? BankSourceVersion,
    DateOnly? ValidUntil, DateOnly BusinessDate);
/// <summary>K6/K7: bank+remaining-days, bank, existing type owner, company. No current average, bank-name matching or assumed days.</summary>
public sealed record SettingsCollateralRateReadV1(SettingsFinancePolicyReadOutcomeV1 Outcome, string? RatePercent,
    string? SourceVersion, string? Scope, string? Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CollateralTypeVersion = null);
public interface ISettingsCollateralRateProviderV1
{
    Task<SettingsCollateralRateReadV1> GetCollateralRateAsync(Guid companyId, Guid collateralTypeId,
        Guid? bankInstitutionId, string? bankSourceVersion, DateOnly? validUntil, DateOnly businessDate, CancellationToken cancellationToken = default);
}
