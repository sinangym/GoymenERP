using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class FinanceOpeningBalanceService
{
    public static bool MatchesDecisionItem(string kind, WorkInboxItemV1? item, OpeningBalanceDecisionDetailV1? detail)
    {
        if (item is null || detail is null) return false;
        var expected = kind switch
        {
            FinanceOpeningBalanceWorkInboxSourceV1.KindName => FinanceOpeningBalanceWorkInboxSourceV1.DecisionItem(detail),
            FinanceOpeningBalanceReversalWorkInboxSourceV1.KindName => FinanceOpeningBalanceReversalWorkInboxSourceV1.DecisionItem(detail),
            _ => null,
        };
        return expected is not null && item.Module == expected.Module && item.Kind == expected.Kind && item.ItemId == expected.ItemId
            && item.DocumentLabel == expected.DocumentLabel && item.WaitingSinceUtc == expected.WaitingSinceUtc
            && item.PreparedBy == expected.PreparedBy && item.Link == expected.Link && item.Scope is null
            && item.Rules is not null && item.References is not null && item.Rules.SequenceEqual(expected.Rules)
            && item.References.SequenceEqual(expected.References)
            && (item.ExcludedDeciders ?? []).SequenceEqual(expected.ExcludedDeciders ?? []);
    }

    internal static bool DecisionRows(string kind, OpeningBalanceDecisionDetailV1? detail, out OpeningBalanceRow? opening, out OpeningBalanceReversalRow? reversal)
    {
        opening = null; reversal = null;
        if (detail?.OpeningBalance is not { } v || detail.DecisionProof is not { } p
            || kind is not (FinanceOpeningBalanceWorkInboxSourceV1.KindName or FinanceOpeningBalanceReversalWorkInboxSourceV1.KindName)
            || v.CompanyId == Guid.Empty || v.Id == Guid.Empty || v.TargetId == Guid.Empty || v.CurrencyId == Guid.Empty
            || !Enum.IsDefined(v.TargetKind) || !Enum.IsDefined(v.Status) || v.Direction is { } direction && !Enum.IsDefined(direction)
            || !FinancePartyCreditLimitService.IsExactAmount(v.Amount) || !DecisionVersion(v.Version, out var version)
            || !IsCanonical(v.OpeningPeriodVersion) || v.OpeningPeriodVersion.Length > PeriodVersionMaximumLength
            || (v.Status == OpeningBalanceStatus.Proposed ? v.DecidedAtUtc is not null || v.RejectionReason is not null
                : v.DecidedAtUtc is not { Offset: var decisionOffset } || decisionOffset != TimeSpan.Zero
                    || (v.Status == OpeningBalanceStatus.Rejected ? !IsCanonical(v.RejectionReason) || v.RejectionReason!.Length > ReasonMaximumLength : v.RejectionReason is not null))
            || (v.Status == OpeningBalanceStatus.Proposed ? version != 1 : v.Status == OpeningBalanceStatus.Reversed ? version != 3 : version != 2)
            || !OwnerIdentity(p.OpeningProposedBy) || !OwnerIdentity(p.ProposedBy)
            || p.CompanyId != v.CompanyId || p.OpeningBalanceId != v.Id || v.ProposedAtUtc.Offset != TimeSpan.Zero
            || (v.TargetKind == OpeningBalanceTargetKind.PartyAccount ? v.PartyId is null || v.PartyId == Guid.Empty || v.Direction is null : v.PartyId is not null || v.Direction is not null)
            || p.OpeningFactsDigest != OpeningFactsDigest(v, p.OpeningProposedBy) || p.PreviewDigest != DecisionPreviewDigest(detail)) return false;
        var target = v.Reversal;
        if (kind == FinanceOpeningBalanceWorkInboxSourceV1.KindName)
        {
            if (target is not null || p.ReversalId is not null || p.ReversalFactsDigest is not null || p.ProposedBy != p.OpeningProposedBy
                || p.ProposedAtUtc != v.ProposedAtUtc || p.Status != v.Status.ToString() || p.Version != v.Version) return false;
        }
        else
        {
            if (target is null || target.Id == Guid.Empty || p.ReversalId != target.Id || !Enum.IsDefined(target.Status)
                || p.ProposedAtUtc != target.ProposedAtUtc || target.ProposedAtUtc.Offset != TimeSpan.Zero || p.Status != target.Status.ToString() || p.Version != target.Version
                || !DecisionVersion(target.Version, out var targetVersion) || targetVersion != (target.Status == OpeningBalanceReversalStatus.Proposed ? 1 : 2)
                || !IsCanonical(target.Reason) || target.Reason.Length > ReasonMaximumLength
                || !IsCanonical(target.OpeningPeriodVersion) || target.OpeningPeriodVersion.Length > PeriodVersionMaximumLength
                || (target.Status == OpeningBalanceReversalStatus.Proposed ? target.DecidedAtUtc is not null || target.RejectionReason is not null
                    : target.DecidedAtUtc is not { Offset: var targetDecisionOffset } || targetDecisionOffset != TimeSpan.Zero
                        || (target.Status == OpeningBalanceReversalStatus.Rejected ? !IsCanonical(target.RejectionReason) || target.RejectionReason!.Length > ReasonMaximumLength : target.RejectionReason is not null))
                || (target.Status == OpeningBalanceReversalStatus.Proposed ? v.Status != OpeningBalanceStatus.Approved
                    : target.Status == OpeningBalanceReversalStatus.Approved ? v.Status != OpeningBalanceStatus.Reversed : v.Status is not (OpeningBalanceStatus.Approved or OpeningBalanceStatus.Reversed))
                || p.ReversalFactsDigest != ReversalFactsDigest(v.Id, target, p.ProposedBy)) return false;
            reversal = new() { CompanyId = v.CompanyId, Id = target.Id, OpeningBalanceId = v.Id, ProposedBy = p.ProposedBy,
                ProposedAtUtc = target.ProposedAtUtc, Status = (int)target.Status + 1, Version = targetVersion };
        }
        opening = new() { CompanyId = v.CompanyId, Id = v.Id, TargetKind = KindCode(v.TargetKind), TargetId = v.TargetId, PartyId = v.PartyId,
            CurrencyId = v.CurrencyId, Direction = v.Direction is { } d ? DirectionCode(d) : null, AmountText = v.Amount,
            ProposedBy = p.OpeningProposedBy, ProposedAtUtc = v.ProposedAtUtc, Status = (int)v.Status + 1, Version = version };
        return true;
    }
}
