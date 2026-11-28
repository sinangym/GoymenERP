using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Organization.Application.Contracts;

// Purpose-only reads: the existing company-wide tax-id approval permission, never Organization.Read.
public interface IOrganizationCompanyTaxIdDecisionReadV1
{
    Task<OrganizationCompanyTaxIdDecisionReadResultV1> ReadDecisionAsync(Guid requestId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
    Task<OrganizationCompanyTaxIdDecisionReceiptV1?> ReadDecisionReceiptAsync(Guid requestId,
        DecideCompanyTaxIdChange command, string key, CommandExecutionResult original,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}

public enum OrganizationCompanyTaxIdDecisionReadStatusV1 { Found, NotFound, Denied, Unavailable }
public sealed record OrganizationCompanyTaxIdDecisionReadResultV1(
    OrganizationCompanyTaxIdDecisionReadStatusV1 Status, OrganizationCompanyTaxIdDecisionDetailV1? Detail);
public sealed record OrganizationCompanyTaxIdDecisionDetailV1(Guid CompanyId, long CompanyVersion, string? CompanyVkn,
    CompanyTaxIdChangeView Request, long CompanyVersionAtRequest, bool IsCurrent, string SourceFactsDigest, string PreviewDigest);
public sealed record OrganizationCompanyTaxIdDecisionReceiptV1(Guid CompanyId, Guid RequestId,
    long CompanyVersionAtRequest, long ExpectedCompanyVersion, long RequestVersion, string Decision, string? Note,
    string RequestedBy, DateTimeOffset RequestedAtUtc, string DecidedBy, DateTimeOffset DecidedAtUtc,
    string CommandName, string IdempotencyKeyHash, string PayloadHash, string ActorId, string OriginalCorrelationId,
    DateTimeOffset CommandCompletedAtUtc, string SourceFactsDigest, string OriginalResultDigest)
{
    [JsonIgnore] public string? ProposedVkn { get; init; }
    [JsonIgnore] public bool? FirstVkn { get; init; }
}
