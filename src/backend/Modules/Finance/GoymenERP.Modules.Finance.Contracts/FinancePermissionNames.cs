namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>
/// The Finance owner's technical permission names for the manual receipt (FIN-T04b; the backend
/// owner's mapping, Business #254 5654539215). A name here grants nothing: the central registry makes
/// it known, a human assigns it.
/// <para>
/// <see cref="Record"/> (FIN-T05a): Ö4 (Sinan #66 5722637822) answers the "who may record" part of OPEN-009
/// for a realized routine receipt — one narrow authorised operator, no second approval, no amount
/// threshold (card #66 5743678389). The central registry now knows the name, so the port MEASURES it: an
/// actor without it is Denied, not Unavailable. A name is still not a grant — nobody holds it until a human
/// assigns it. Legacy direct Reverse has no name; FIN-T06 uses the separately declared
/// propose/approve permissions and enforces maker≠checker (Ö4).
/// </para>
/// </summary>
public static class FinancePermissionNames
{
    public const string DraftSave = "finance.manual-receipt.draft.save";
    public const string DraftRead = "finance.manual-receipt.draft.read";
    public const string AmountRead = "finance.manual-receipt.amount.read";
    public const string Record = "finance.manual-receipt.record";

    // FIN-T06 / B1: declaration only. Action mapping and central permission
    // registration remain unavailable until the Cari-owned authority tuple lands.
    public const string ReversePropose = "finance.manual-receipt.reverse.propose";

    public const string ReverseApprove = "finance.manual-receipt.reverse.approve";
}
