using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

/// <summary>FNS-D24 permission names: registered centrally, no grant, role or Critical attached here.</summary>
public sealed class CashTransferPermissionMappingTests
{
    [Theory]
    [InlineData(FinanceAction.ReadCashTransfers, FinancePermissionNames.CashTransferRead)]
    [InlineData(FinanceAction.CommitCashTransfer, FinancePermissionNames.CashTransferCommit)]
    public void Transfer_actions_map_to_registered_permission_names(FinanceAction action, string expected)
    {
        Assert.Equal(expected, FinancePermissionPort.NameOf(action));
        Assert.True(ErpPermissions.IsKnown(expected));
    }

    [Fact]
    public void Read_and_commit_are_distinct_names()
    {
        Assert.NotEqual(FinancePermissionNames.CashTransferRead, FinancePermissionNames.CashTransferCommit);
    }
}
