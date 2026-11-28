using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Companies;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using GoymenERP.Modules.Idempotency.Domain.Executions;

namespace GoymenERP.Modules.Organization.Infrastructure.WorkInbox;

/// <summary>One existing native formatter; current stale presentation is distinct from original command provenance.</summary>
public static class OrganizationCompanyTaxIdDecisionBindingV1
{
    public const string ModuleName = OrganizationCompanyTaxIdChangeWorkInboxSourceV1.ModuleName;
    public const string KindName = OrganizationCompanyTaxIdChangeWorkInboxSourceV1.KindName;
    public const string ScreenPath = OrganizationCompanyTaxIdChangeWorkInboxSourceV1.ScreenPath;
    public const string PurposePath = "/onaylarim/organization.company-tax-id-change.v1";
    public const string DecisionCommand = OrganizationMasterDataService.DecideCompanyTaxIdChangeCommandName;
    public static string DecisionKeyHash(string key) => IdempotencyHash.ComputeKey(key);
    public static string DecisionPayloadHash(Guid companyId, Guid id, DecideCompanyTaxIdChange command) =>
        OrganizationMasterDataService.TaxIdDecisionPayloadHash(companyId, id, command);
    public static string OriginalResultDigest(CommandResponseSnapshot response) =>
        OrganizationMasterDataService.TaxIdOriginalResultDigest(response);
    public static WorkInboxItemV1? Item(Guid id, string proposedVkn, string requestedBy, DateTimeOffset requestedAtUtc, bool firstVkn, bool isCurrent, bool pending = true)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(requestedBy) || string.IsNullOrWhiteSpace(proposedVkn)) return null;
        var rules = new List<WorkApprovalRuleResultV1>(3) { new(OrganizationCompanyTaxIdChangeWorkInboxSourceV1.ApprovalRequiredRule, null, null, null, null) };
        if (firstVkn) rules.Add(new(OrganizationCompanyTaxIdChangeWorkInboxSourceV1.FirstVknRule, null, null, null, null));
        if (pending && !isCurrent) rules.Add(new(OrganizationCompanyTaxIdChangeWorkInboxSourceV1.StaleRule, null, null, null, null));
        return new(ModuleName, KindName, id, proposedVkn, requestedAtUtc.ToUniversalTime(), $"{PurposePath}/{id:D}", requestedBy, rules, []);
    }
    // Scalar proof certifies only identity/current presentation. Full immutable facts bind through owner/receipt.
    public static bool MatchesDecisionProof(WorkInboxItemV1? item, WorkOrganizationCompanyTaxIdProofV1? p) => p is not null
        && p.CompanyId != Guid.Empty && p.RequestId != Guid.Empty && p.CurrentCompanyVersion > 0 && p.CompanyVersionAtRequest > 0
        && Status(p.Status) && !string.IsNullOrWhiteSpace(p.RequestedBy) && p.RequestedBy == p.RequestedBy.Trim()
        && Company.IsCanonicalVkn(p.ProposedVkn) && OrganizationCompanyTaxIdRules.HasValidCheckDigit(p.ProposedVkn)
        && (!p.IsCurrent || p.CurrentCompanyVersion == p.CompanyVersionAtRequest)
        && Digest(p.SourceFactsDigest) && Digest(p.PreviewDigest)
        && Item(p.RequestId, p.ProposedVkn, p.RequestedBy, p.RequestedAtUtc, p.FirstVkn, p.IsCurrent, p.Status == "pending") is { } expected && Same(item, expected);
    public static bool MatchesDecisionItem(WorkInboxItemV1? item, OrganizationCompanyTaxIdDecisionDetailV1? detail) => ValidDetail(detail)
        && Item(detail!.Request.Id, detail.Request.ProposedVkn, detail.Request.RequestedBy, detail.Request.RequestedAtUtc,
            detail.Request.CurrentVkn is null, detail.IsCurrent, detail.Request.Status == "pending") is { } expected && Same(item, expected);
    public static bool ValidDetail(OrganizationCompanyTaxIdDecisionDetailV1? d) => d?.Request is { } r
        && d.CompanyId != Guid.Empty && d.CompanyVersion > 0 && d.CompanyVersionAtRequest > 0 && r.Id != Guid.Empty
        && Status(r.Status) && Company.IsCanonicalVkn(r.ProposedVkn) && OrganizationCompanyTaxIdRules.HasValidCheckDigit(r.ProposedVkn)
        && (r.CurrentVkn is null || Company.IsCanonicalVkn(r.CurrentVkn)) && r.ProposedVkn != r.CurrentVkn
        && (d.CompanyVkn is null || Company.IsCanonicalVkn(d.CompanyVkn))
        && OrganizationCompanyTaxIdRules.IsValidReason(r.Reason) && r.Reason == r.Reason.Trim()
        && !string.IsNullOrWhiteSpace(r.RequestedBy) && r.RequestedBy == r.RequestedBy.Trim()
        && (r.Status == "pending" ? r.DecidedBy is null && r.DecidedAtUtc is null && r.DecisionNote is null
            : !string.IsNullOrWhiteSpace(r.DecidedBy) && r.DecidedBy != r.RequestedBy && r.DecidedAtUtc is not null
                && OrganizationCompanyTaxIdRules.IsValidNote(r.DecisionNote)
                && (r.DecisionNote is null || r.DecisionNote.Length > 0 && r.DecisionNote == r.DecisionNote.Trim()))
        && d.IsCurrent == (d.CompanyVersion == d.CompanyVersionAtRequest && d.CompanyVkn == r.CurrentVkn)
        && d.SourceFactsDigest == OrganizationCompanyTaxIdDecisionFactsV1.SourceFactsDigest(d.CompanyId, r, d.CompanyVersionAtRequest)
        && d.PreviewDigest == OrganizationCompanyTaxIdDecisionFactsV1.PreviewDigest(d);
    // Current company version/VKN and the stale rule are NOT historical receipt facts. The frozen native command
    // separately binds ExpectedCompanyVersion; this helper retains current presentation but binds primary facts to the receipt.
    public static bool MatchesOriginalDecisionItem(WorkInboxItemV1? item, WorkOrganizationCompanyTaxIdProofV1? p,
        OrganizationCompanyTaxIdDecisionReceiptV1? receipt) => MatchesDecisionProof(item, p) && p is not null && receipt is { } r
        && r.CompanyId == p.CompanyId && r.RequestId == p.RequestId && r.CompanyVersionAtRequest == p.CompanyVersionAtRequest
        && r.RequestedBy == p.RequestedBy && r.RequestedAtUtc == p.RequestedAtUtc && r.SourceFactsDigest == p.SourceFactsDigest
        && r.ProposedVkn is not null && r.ProposedVkn == p.ProposedVkn && r.FirstVkn is { } first && first == p.FirstVkn
        && r.RequestVersion == 2 && r.ExpectedCompanyVersion > 0 && r.Decision is "approve" or "reject"
        && (p.Status == "pending" || p.Status == (r.Decision == "approve" ? "approved" : "rejected"))
        && (r.Decision != "approve" || r.ExpectedCompanyVersion == r.CompanyVersionAtRequest)
        && !string.IsNullOrWhiteSpace(r.ActorId) && r.ActorId == r.DecidedBy && r.ActorId != r.RequestedBy
        && OrganizationCompanyTaxIdRules.IsValidNote(r.Note) && (r.Note is null || r.Note.Length > 0 && r.Note == r.Note.Trim())
        && r.CommandName == "organization.company.tax-id.change-decision.v1" && !string.IsNullOrWhiteSpace(r.OriginalCorrelationId)
        && Digest(r.IdempotencyKeyHash) && Digest(r.PayloadHash) && Digest(r.OriginalResultDigest);
    private static bool Status(string? value) => value is "pending" or "approved" or "rejected";
    private static bool Digest(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static bool Same(WorkInboxItemV1? item, WorkInboxItemV1 expected) => item is not null && item.Module == expected.Module
        && item.Kind == expected.Kind && item.ItemId == expected.ItemId && item.DocumentLabel == expected.DocumentLabel
        && item.WaitingSinceUtc == expected.WaitingSinceUtc && item.Link == expected.Link && item.PreparedBy == expected.PreparedBy
        && item.Scope is null && item.Rules is not null && item.References is not null
        && item.Rules.SequenceEqual(expected.Rules) && item.References.SequenceEqual(expected.References)
        && (item.ExcludedDeciders ?? []).SequenceEqual(expected.ExcludedDeciders ?? []);
}
