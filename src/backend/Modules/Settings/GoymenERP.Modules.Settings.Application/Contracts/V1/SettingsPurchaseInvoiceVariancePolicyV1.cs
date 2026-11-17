using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>ALS-D8/D15/D63, API-97: registration only. No role, grant, seed or Critical classification is supplied.</summary>
public static class SettingsPurchaseInvoiceVariancePolicyPermissionsV1
{
    public const string Manage = "settings.purchase-invoice-variance-policies.manage";
}

public static class PurchaseInvoiceVariancePolicyCodesV1
{
    public const string RequestInvalid = "settings_purchase_invoice_variance_policy_request_invalid";
    public const string VersionConflict = "settings_purchase_invoice_variance_policy_version_conflict";
    public const string IdempotencyConflict = "settings_purchase_invoice_variance_policy_idempotency_conflict";
    public const string NotPublished = "settings_purchase_invoice_variance_policy_not_published";
    public const string ProviderUnavailable = "settings_provider_unavailable";
    public const string AccessDenied = "settings_access_denied";
    public const string ProfileInvalid = "settings_profile_invalid";
}

/// <summary>
/// One explicitly published company policy. ALS-D8 starts the editor at zero; an absent row is NOT a published zero.
/// ALS-D15 keeps the price and control-rate rounding boundaries in the same version. The invoice owner snapshots the
/// identity, version and both values; subsequent publication never changes the meaning of that invoice's match.
/// No amount, exchange rate or tolerance calculation is performed here.
/// </summary>
public sealed record SettingsPurchaseInvoiceVariancePolicyV1(
    Guid CompanyId, Guid PolicyId, long Version, decimal AmountTolerance, decimal RateTolerance);

public interface ISettingsPurchaseInvoiceVariancePolicyProviderV1
{
    Task<SettingsPurchaseInvoiceVariancePolicyV1> GetAsync(Guid companyId, CancellationToken cancellationToken = default);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PublishPurchaseInvoiceVariancePolicyRequestV1(
    string? ExpectedVersion, string? AmountTolerance, string? RateTolerance, string? Reason);

public sealed record PurchaseInvoiceVariancePolicyVersionViewV1(
    Guid PolicyId, string Version, string AmountTolerance, string RateTolerance,
    string PublishedBy, DateTimeOffset PublishedAtUtc, string Reason);

/// <summary>Null Current and version "0" explicitly mean that no policy has been published.</summary>
public sealed record PurchaseInvoiceVariancePolicyViewV1(
    Guid CompanyId, string Version, PurchaseInvoiceVariancePolicyVersionViewV1? Current,
    IReadOnlyList<PurchaseInvoiceVariancePolicyVersionViewV1> Versions);

public interface ISettingsPurchaseInvoiceVariancePolicyAdministrationV1
{
    Task<PurchaseInvoiceVariancePolicyViewV1> ReadAsync(CancellationToken cancellationToken = default);
    Task<CompanyPolicyCommandResultV1> PublishAsync(PublishPurchaseInvoiceVariancePolicyRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}
