using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// The Sales operation identifiers the Settings money port is asked with
/// (<c>ISettingsPrerequisiteProvider.GetMoneyProfileAsync(currencyId, operation, effectiveOn)</c>).
/// A technical mapping the backend owner sets (Business #254 5654539215: "operation / route /
/// status / replay / permission technical mapping belongs to the existing backend owners; no
/// human decision is awaited"), in the Finance pattern (<c>finance.manual-bank-receipt.v1</c>).
/// The key chooses no rate, scale or date: those are the Settings owner's approved money profile
/// (T02-b), which is what a real draft waits for.
/// </summary>
public static class SalesMoneyOperations
{
    /// <summary>The sales order draft's currency read; the value the domain requires on <see cref="MoneyProfileSnapshot.Operation"/>.</summary>
    public const string OrderDraftV1 = SalesOrderDraft.MoneyOperation;
}
