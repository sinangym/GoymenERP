using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure.WorkInbox;
using GoymenERP.Modules.Inventory.Infrastructure.Shell;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.BuildingBlocks.Security;
using F = GoymenERP.Inventory.UnitTests.InventoryWorkInboxTestFixture;

namespace GoymenERP.Inventory.UnitTests;

public sealed partial class InventoryTransferLossWorkInboxSourceTests
{
    [Fact]
    public async Task Renewed_request_keeps_item_and_transfer_version_but_exposes_its_own_request_and_retained_maker_proof()
    {
        var f = new F(); f.TransferLine(F.Id(1), F.Id(11), F.Id(21), 1);
        Request(f, F.Id(1), F.Id(11), 1, 1);
        f.Rows.LossRequestRows[0].RequestedAtUtc = F.T0.ToUniversalTime();
        var source = new InventoryTransferLossWorkInboxSourceV1(f.Rows, f.Fence);
        var id = InventoryWorkInboxFence.TransferLineItemId(Kind, F.Id(1), F.Id(11));
        var first = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(f.Context(), id, default));
        f.Rows.LossDecisionRows.Add(new() { CompanyId = F.CompanyA, TransferId = F.Id(1), LineId = F.Id(11), RequestNumber = 1, Decision = 2 });
        Request(f, F.Id(1), F.Id(11), 2, 2, "SENTETIK-new-maker");
        f.Rows.LossRequestRows[1].RequestedAtUtc = F.T0.AddHours(1).ToUniversalTime();
        var renewed = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(f.Context(), id, default));
        Assert.Equal((first.Item.ItemId, first.SourceVersion), (renewed.Item.ItemId, renewed.SourceVersion));
        Assert.Equal(2, renewed.InventoryTransferLoss!.RequestNumber); Assert.True(renewed.Pending);
        Assert.Equal([F.Requester, "SENTETIK-new-maker"], renewed.InventoryTransferLossProof!.Requests.Select(row => row.PreparedBy));
        Assert.Equal(["Rejected", "Pending"], renewed.InventoryTransferLossProof.Requests.Select(row => row.Status));
        Assert.Equal("/stok/transferler/kayip-onaylari", renewed.Item.Link);
    }

    [Fact]
    public async Task Withdrawn_terminal_request_is_readable_for_native_replay_but_not_pending()
    {
        var f = new F(); f.TransferLine(F.Id(1), F.Id(11), F.Id(21), 1); Request(f, F.Id(1), F.Id(11), 1, 1);
        f.Rows.LossRequestRows[0].RequestedAtUtc = F.T0.ToUniversalTime();
        f.Rows.LossWithdrawalRows.Add(new() { CompanyId = F.CompanyA, TransferId = F.Id(1), LineId = F.Id(11), RequestNumber = 1 });
        var source = new InventoryTransferLossWorkInboxSourceV1(f.Rows, f.Fence);
        var snapshot = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(f.Context(), InventoryWorkInboxFence.TransferLineItemId(Kind, F.Id(1), F.Id(11)), default));
        Assert.False(snapshot.Pending); Assert.Equal("Withdrawn", Assert.Single(snapshot.InventoryTransferLossProof!.Requests).Status);
    }

    [Theory]
    [InlineData("missing-history")]
    [InlineData("two-pending")]
    [InlineData("dual-terminal")]
    [InlineData("foreign")]
    public async Task Incomplete_or_ambiguous_native_history_never_becomes_a_decision_proof(string fault)
    {
        var f = new F(); f.TransferLine(F.Id(1), F.Id(11), F.Id(21), 1); Request(f, F.Id(1), F.Id(11), 1, 1);
        f.Rows.LossRequestRows[0].RequestedAtUtc = F.T0.ToUniversalTime();
        if (fault == "missing-history") f.Rows.LossRequestRows[0].RequestNumber = 2;
        if (fault == "two-pending") { Request(f, F.Id(1), F.Id(11), 2, 2); f.Rows.LossRequestRows[1].RequestedAtUtc = F.T0.ToUniversalTime(); }
        if (fault == "dual-terminal")
        {
            f.Rows.LossDecisionRows.Add(new() { CompanyId = F.CompanyA, TransferId = F.Id(1), LineId = F.Id(11), RequestNumber = 1 });
            f.Rows.LossWithdrawalRows.Add(new() { CompanyId = F.CompanyA, TransferId = F.Id(1), LineId = F.Id(11), RequestNumber = 1 });
        }
        if (fault == "foreign") f.Rows.LossRequestRows[0].CompanyId = F.CompanyB;
        var source = new InventoryTransferLossWorkInboxSourceV1(f.Rows, f.Fence);
        var read = () => source.ReadDecisionAsync(f.Context(), InventoryWorkInboxFence.TransferLineItemId(Kind, F.Id(1), F.Id(11)), default);
        if (fault == "foreign") Assert.Null(await read()); else await Assert.ThrowsAsync<InvalidOperationException>(read);
    }

    [Fact]
    public async Task Purpose_locator_bound_refuses_instead_of_returning_a_truncated_or_absent_match()
    {
        var f = new F();
        for (var i = 1; i <= InventoryTransferLossWorkInboxSourceV1.DecisionReadLimit + 1; i++) Request(f, F.Id(i), F.Id(11), 1, 1);
        var source = new InventoryTransferLossWorkInboxSourceV1(f.Rows, f.Fence);
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(f.Context(), Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Purpose_read_rechecks_authority_after_the_locator_owner_read()
    {
        var f = new F(); f.TransferLine(F.Id(1), F.Id(11), F.Id(21), 1); Request(f, F.Id(1), F.Id(11), 1, 1);
        f.Rows.LossRequestRows[0].RequestedAtUtc = F.T0.ToUniversalTime();
        var calls = 0; f.Authority.Answer = (_, _) => ++calls == 1 ? InventoryWorkInboxTestAuthority.CompanyWide : PermissionScope.Denied;
        var source = new InventoryTransferLossWorkInboxSourceV1(f.Rows, f.Fence);
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(f.Context(), InventoryWorkInboxFence.TransferLineItemId(Kind, F.Id(1), F.Id(11)), default));
        Assert.Equal(1, f.Rows.Reads);
    }

    [Fact]
    public void Loss_purpose_declaration_is_staged_and_standalone_under_the_existing_company_wide_native_permission()
    {
        var row = Assert.Single(new InventoryShellCapabilityProvider().GetDeclarations(), row => row.CapabilityId == "inventory.transfer-loss.work-decision");
        Assert.False(row.ImplementationAvailable); Assert.Null(row.ParentCapabilityId);
        Assert.Equal("/onaylarim/inventory.transfer-loss.v1/:approvalId", row.RoutePath);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, row.ScopeRequirement);
        Assert.Equal([ErpPermissions.InventoryStockTransferLossApprove], row.RequiredPermissions);
    }
}
