using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// FNS-D106 (SINAN_KARARLARI_2026-11-16_DALGA5_EK24.md): a credit group's movements stay CLOSED until Sinan approves that
/// group's field spec. FNS-D124..D127 approved all four lists (v1.2); the host composes that exact approval.
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

    /// <summary>An unconfigured consumer stays closed (FNS-D106); this is not the reviewed host composition.</summary>
    public static FinanceCreditGroupGateV1 NoneApproved { get; } = new([]);

    /// <summary>Field-list approvals only: FNS-D124 cash, D125 non-cash, D126 instruments/factoring, D127 leasing.
    /// This grants no permission, publishes no mapping and enables no deployment or external connection.</summary>
    public static FinanceCreditGroupGateV1 ApprovedFieldListsV12 { get; } = new([
        CreditAccountGroup.Cash, CreditAccountGroup.NonCash,
        CreditAccountGroup.InstrumentAndFactoring, CreditAccountGroup.Leasing]);

    /// <summary>Whether the stored group code of a credit account has its movements open. An unknown code is closed.</summary>
    public bool MovementsOpen(int storedGroup) =>
        _approved.Any(group => FinanceCreditAccountService.GroupCode(group) == storedGroup);
}
