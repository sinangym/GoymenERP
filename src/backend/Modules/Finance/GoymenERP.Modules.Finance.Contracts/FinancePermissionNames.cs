namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>
/// The Finance owner's technical permission names for the manual-receipt draft (FIN-T04b; the
/// backend owner's mapping, Business #254 5654539215). A name here grants nothing: the central
/// registry makes it known, a human assigns it. Record and Reverse have no name: their authority is
/// OPEN-009, a human decision.
/// </summary>
public static class FinancePermissionNames
{
    public const string DraftSave = "finance.manual-receipt.draft.save";
    public const string DraftRead = "finance.manual-receipt.draft.read";
    public const string AmountRead = "finance.manual-receipt.amount.read";
}
