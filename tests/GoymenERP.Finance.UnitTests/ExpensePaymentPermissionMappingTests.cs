using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

public sealed class ExpensePaymentPermissionMappingTests
{
    [Theory]
    [InlineData(FinanceAction.SaveExpensePaymentDraft, FinancePermissionNames.ExpensePaymentDraftSave)]
    [InlineData(FinanceAction.ReadExpensePayments, FinancePermissionNames.ExpensePaymentDraftRead)]
    [InlineData(FinanceAction.ApproveExpensePayment, FinancePermissionNames.ExpensePaymentApprove)]
    [InlineData(FinanceAction.RecordExpensePayment, FinancePermissionNames.ExpensePaymentRecord)]
    public void Expense_payment_actions_map_to_their_own_permission_names(FinanceAction action, string expected)
    {
        Assert.Equal(expected, FinancePermissionPort.NameOf(action));
    }

    [Fact]
    public void Expense_payment_permission_literals_are_owner_declarations_not_grants()
    {
        Assert.Equal("finance.expense-payment.draft.save", FinancePermissionNames.ExpensePaymentDraftSave);
        Assert.Equal("finance.expense-payment.draft.read", FinancePermissionNames.ExpensePaymentDraftRead);
        Assert.Equal("finance.expense-payment.approve", FinancePermissionNames.ExpensePaymentApprove);
        Assert.Equal("finance.expense-payment.record", FinancePermissionNames.ExpensePaymentRecord);
    }
}
