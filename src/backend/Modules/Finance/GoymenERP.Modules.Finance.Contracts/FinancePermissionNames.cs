namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>
/// The Finance owner's technical permission names for the manual receipt (FIN-T04b; the backend
/// owner's mapping, Business #254 5654539215). A name here grants nothing: the central registry makes
/// it known, a human assigns it.
/// <para>
/// <see cref="Record"/> (FIN-T05a): Ö4 (Sinan #66 5722637822) answers the "who may record" part of OPEN-009
/// for a realized routine receipt — one narrow authorised operator, no second approval, no amount
/// threshold (card #66 5743678389). Until the central registry knows the name, the port answers
/// Unavailable. Reverse has no name: it is FIN-T06, maker≠checker (Ö4).
/// </para>
/// </summary>
public static class FinancePermissionNames
{
    public const string DraftSave = "finance.manual-receipt.draft.save";
    public const string DraftRead = "finance.manual-receipt.draft.read";
    public const string AmountRead = "finance.manual-receipt.amount.read";
    public const string Record = "finance.manual-receipt.record";
}
