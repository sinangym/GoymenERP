using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

public sealed class ManualBankPaymentPermissionMappingTests
{
    [Theory]
    [InlineData(FinanceAction.SaveManualBankPaymentDraft, FinancePermissionNames.ManualBankPaymentDraftSave)]
    [InlineData(FinanceAction.ReadManualBankPayments, FinancePermissionNames.ManualBankPaymentDraftRead)]
    [InlineData(FinanceAction.ApproveManualBankPayment, FinancePermissionNames.ManualBankPaymentApprove)]
    [InlineData(FinanceAction.RecordManualBankPayment, FinancePermissionNames.ManualBankPaymentRecord)]
    public void Owner_actions_map_to_the_declared_permission_names(FinanceAction action, string expected)
    {
        Assert.Equal(expected, FinancePermissionPort.NameOf(action));
    }
}
