namespace GoymenERP.Modules.Finance.Contracts;

public enum CompanyCardFinancialEventKind
{
    Expense = 1,
    LimitAdjustment = 2,
    Statement = 3,
    Payment = 4,
}

/// <summary>
/// DEC-FIN-015 source contract. A card slip records an expense only; card limit,
/// statement and payment remain separate financial events.
/// </summary>
public sealed record CompanyCardExpenseV1(
    Guid Id,
    Guid CompanyId,
    string Version,
    Guid CardId,
    string CardVersion,
    CompanyCardFinancialEventKind EventKind,
    MoneyText Money,
    ProfileReference Profile,
    DateOnly SpentOn,
    string SlipReference,
    Guid DocumentId,
    string SpentBy,
    string ExpenseType,
    string ContextType,
    string ContextId);
