using System.Text.Json.Serialization;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Organization.Domain.Companies;

namespace GoymenERP.Modules.Organization.Application.Contracts;

/// <summary>
/// KIM-S23 (KIM-D28): the two permission names of the company VKN change flow. They are registered names only; who
/// holds them (grant/role) is a human gate and neither is Critical. KIM-D36: <see cref="Approve"/> is deliberately not
/// Critical; its intended holder is the administrator persona, but no grant is made here, and the maker-checker rule
/// (the requester never decides their own request) stays in force. KIM-D35: the first real VKN of the placeholder
/// company ("VKN girilmemiş") goes through this same flow.
/// </summary>
public static class OrganizationCompanyTaxIdPermissionsV1
{
    /// <summary>Prepare (request) a change of the active company's VKN.</summary>
    public const string Request = ErpPermissions.OrganizationCompanyTaxIdRequest;

    /// <summary>Approve or reject another person's pending VKN change request.</summary>
    public const string Approve = ErpPermissions.OrganizationCompanyTaxIdApprove;
}

/// <summary>
/// POST /api/v1/organization/company/tax-id/change-requests. <paramref name="ExpectedVersion"/> is the company version
/// the preparer saw; <paramref name="ProposedVkn"/> is exactly ten ASCII digits after trim.
/// </summary>
public sealed record RequestCompanyTaxIdChange(
    [property: JsonRequired] long ExpectedVersion,
    string ProposedVkn,
    string Reason);

/// <summary>
/// POST /api/v1/organization/company/tax-id/change-requests/{requestId}/decision. <paramref name="Decision"/> is
/// <c>approve</c> or <c>reject</c>; <paramref name="ExpectedVersion"/> is the company version the decider saw.
/// </summary>
public sealed record DecideCompanyTaxIdChange(
    [property: JsonRequired] long ExpectedVersion,
    string Decision,
    string? Note);

/// <summary>One VKN change request as the tax-id screen shows it. <paramref name="Status"/> is the status token.</summary>
public sealed record CompanyTaxIdChangeView(
    Guid Id,
    string? CurrentVkn,
    string ProposedVkn,
    string Reason,
    string Status,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    string? DecidedBy,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionNote);

/// <summary>
/// What the reader may do on the screen. The server still decides every command; this only hides buttons that would
/// be refused (the preparer of the pending request never sees approve/reject).
/// </summary>
public sealed record CompanyTaxIdViewer(
    bool CanRequest,
    bool CanApprove,
    bool IsRequesterOfPending);

/// <summary>
/// GET /api/v1/organization/company/tax-id: the active company's VKN, its version (the <c>expectedVersion</c> of the
/// next command), the pending change and the last decided one.
/// </summary>
public sealed record CompanyTaxIdView(
    Guid CompanyId,
    string Code,
    string LegalName,
    string? Vkn,
    bool IsPlaceholder,
    long Version,
    CompanyTaxIdChangeView? Pending,
    CompanyTaxIdChangeView? LastDecided,
    CompanyTaxIdViewer Viewer);

/// <summary>Request-shape rules shared by the endpoint (400 before anything runs) and the service.</summary>
public static class OrganizationCompanyTaxIdRules
{
    public const string ApproveDecision = "approve";
    public const string RejectDecision = "reject";

    public static int ReasonMaximumLength => CompanyTaxIdChangeRequest.ReasonMaximumLength;

    public static int NoteMaximumLength => CompanyTaxIdChangeRequest.NoteMaximumLength;

    public static int VknLength => Company.VknLength;

    /// <summary>The VKN after trim is exactly <see cref="Company.VknLength"/> ASCII digits.</summary>
    public static bool IsValidVkn(string? vkn) => Company.IsCanonicalVkn(vkn?.Trim());

    public static bool IsDecision(string? decision) =>
        string.Equals(decision, ApproveDecision, StringComparison.Ordinal)
        || string.Equals(decision, RejectDecision, StringComparison.Ordinal);

    public static bool IsValidReason(string? reason) => OrganizationMasterDataRules.IsValidReason(reason);

    public static bool IsValidNote(string? note)
    {
        var normalized = note?.Trim();
        return normalized is null
            || (normalized.Length <= NoteMaximumLength && !normalized.Any(char.IsControl));
    }
}

/// <summary>KIM-S23: the active company's VKN and its maker-checker change flow.</summary>
public interface IOrganizationCompanyTaxIdService
{
    /// <summary>
    /// The active company's tax-id view, or null when the caller holds neither tax-id permission in the company (the
    /// VKN is not part of the plain organization read; the directory never exposes it).
    /// </summary>
    Task<CompanyTaxIdView?> GetCompanyTaxIdAsync(CancellationToken cancellationToken);

    Task<CommandExecutionResult> RequestCompanyTaxIdChangeAsync(
        RequestCompanyTaxIdChange command,
        CommandMetadata metadata,
        CancellationToken cancellationToken);

    Task<CommandExecutionResult> DecideCompanyTaxIdChangeAsync(
        Guid requestId,
        DecideCompanyTaxIdChange command,
        CommandMetadata metadata,
        CancellationToken cancellationToken);
}
