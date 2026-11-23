using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// FNS-D106 (SINAN_KARARLARI_2026-11-16_DALGA5_EK24.md): a credit group's movements stay CLOSED until Sinan approves that
/// group's field spec. No group's spec is approved today (docs/features/FINANCE_CREDIT_GROUP_FIELDS_DRAFT_v1.md is a draft),
/// so the host registers <see cref="NoneApproved"/>: every credit movement proposal and approval is refused fail-closed.
/// Opening a group is a reviewed change that names the approval, never a runtime switch or a guess.
/// </summary>
public sealed class FinanceCreditGroupGateV1
{
    public const string GroupClosedCode = "credit_group_spec_not_approved";

    private readonly HashSet<CreditAccountGroup> _approved;

    public FinanceCreditGroupGateV1(IEnumerable<CreditAccountGroup> approvedGroups)
    {
        ArgumentNullException.ThrowIfNull(approvedGroups);
        _approved = [.. approvedGroups];
        if (_approved.Any(group => !Enum.IsDefined(group))) throw new ArgumentOutOfRangeException(nameof(approvedGroups));
    }

    /// <summary>The production default: no group's field spec is approved (FNS-D106).</summary>
    public static FinanceCreditGroupGateV1 NoneApproved { get; } = new([]);

    /// <summary>Whether the stored group code of a credit account has its movements open. An unknown code is closed.</summary>
    public bool MovementsOpen(int storedGroup) =>
        _approved.Any(group => FinanceCreditAccountService.GroupCode(group) == storedGroup);
}
