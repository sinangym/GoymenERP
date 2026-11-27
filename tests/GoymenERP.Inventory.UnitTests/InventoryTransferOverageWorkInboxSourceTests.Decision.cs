using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Inventory.Infrastructure.Shell;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Inventory.Infrastructure.WorkInbox;
using F = GoymenERP.Inventory.UnitTests.InventoryWorkInboxTestFixture;

namespace GoymenERP.Inventory.UnitTests;

public sealed partial class InventoryTransferOverageWorkInboxSourceTests
{
    [Fact]
    public async Task Purpose_retains_original_receiver_and_each_numbered_request_without_exposing_reason_in_generic_metadata()
    {
        var f = PurposeFixture(); var source = new InventoryTransferOverageWorkInboxSourceV1(f.Rows, f.Fence);
        var id = InventoryWorkInboxFence.TransferLineItemId(Kind, F.Id(1), F.Id(11));
        var first = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(f.Context(), id, default));
        Decision(f, F.Id(1), F.Id(11), 1, 2);
        Renewal(f, F.Id(1), F.Id(11), 2, 2m, 2, pieces: 0);
        f.Rows.OverageRenewalRows[0].RequestedAtUtc = F.T0.ToUniversalTime();
        var next = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(f.Context(), id, default));
        Assert.Equal((first.Item.ItemId, first.SourceVersion), (next.Item.ItemId, next.SourceVersion));
        Assert.True(next.Pending); Assert.Equal(F.Requester, next.Item.PreparedBy); Assert.Equal([F.Receiver], next.Item.ExcludedDeciders);
        var proof = next.InventoryTransferOverageProof!;
        Assert.Equal(F.Receiver, proof.ReceivedBy); Assert.Equal([1, 2], proof.Requests.Select(row => row.RequestNumber));
        Assert.Equal(["Rejected", "Pending"], proof.Requests.Select(row => row.Status));
        Assert.Equal(0, proof.Requests[1].PieceCount);
        Assert.DoesNotContain("SENTETIK human-only reason", JsonSerializer.Serialize(next));
        Assert.DoesNotContain("InventoryTransferOverageProof", JsonSerializer.Serialize(next));
        Assert.Equal("/onaylarim/inventory.transfer-overage.v1/" + id.ToString("D"), next.Item.Link);
        Assert.Equal("/stok/transferler/fazla-kabul-onaylari", next.Item.References.Single(reference => reference.Role == "inventory-transfer").Link);
    }

    [Theory]
    [InlineData("missing-number")] [InlineData("two-pending")] [InlineData("invalid-decision")]
    [InlineData("missing-reason")] [InlineData("foreign")]
    public async Task Missing_or_malformed_owner_history_never_becomes_a_complete_proof(string fault)
    {
        var f = PurposeFixture();
        if (fault == "foreign") f.Rows.ReceiptRows[0].CompanyId = F.CompanyB;
        if (fault == "missing-reason") f.Rows.ReceiptRows[0].OverageReason = null;
        if (fault is "missing-number" or "two-pending")
        {
            Renewal(f, F.Id(1), F.Id(11), fault == "missing-number" ? 3 : 2, 2m, 2);
            f.Rows.OverageRenewalRows[0].RequestedAtUtc = F.T0.ToUniversalTime();
        }
        if (fault == "invalid-decision") Decision(f, F.Id(1), F.Id(11), 1, 99);
        var source = new InventoryTransferOverageWorkInboxSourceV1(f.Rows, f.Fence);
        var read = () => source.ReadDecisionAsync(f.Context(), InventoryWorkInboxFence.TransferLineItemId(Kind, F.Id(1), F.Id(11)), default);
        if (fault == "foreign") Assert.Null(await read()); else await Assert.ThrowsAsync<InvalidOperationException>(read);
    }

    [Fact]
    public async Task Purpose_locator_bound_is_unavailable_instead_of_an_absent_or_truncated_match()
    {
        var f = new F();
        for (var i = 0; i <= InventoryTransferOverageWorkInboxSourceV1.DecisionReadLimit; i++) Receipt(f, F.Id(i + 1), F.Id(11), 2m, 1);
        var source = new InventoryTransferOverageWorkInboxSourceV1(f.Rows, f.Fence);
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(f.Context(), Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Purpose_checks_fresh_authority_after_owner_locator_read()
    {
        var f = PurposeFixture(); var calls = 0;
        f.Authority.Answer = (_, _) => ++calls == 1 ? InventoryWorkInboxTestAuthority.CompanyWide : PermissionScope.Denied;
        var source = new InventoryTransferOverageWorkInboxSourceV1(f.Rows, f.Fence);
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(f.Context(), InventoryWorkInboxFence.TransferLineItemId(Kind, F.Id(1), F.Id(11)), default));
        Assert.Equal(1, f.Rows.Reads);
    }

    [Fact]
    public void Overage_purpose_is_standalone_under_the_existing_native_permission_and_preserves_native_browse()
    {
        var declarations = new InventoryShellCapabilityProvider().GetDeclarations();
        var row = Assert.Single(declarations, row => row.CapabilityId == "inventory.transfer-overage.work-decision");
        Assert.True(row.ImplementationAvailable); Assert.Null(row.ParentCapabilityId);
        Assert.Equal(InventoryTransferOverageWorkInboxSourceV1.DecisionScreenPath, row.RoutePath);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, row.ScopeRequirement);
        Assert.Equal([ErpPermissions.InventoryStockTransferOverageApprove], row.RequiredPermissions);
        var native = Assert.Single(declarations, row => row.CapabilityId == ErpPermissions.InventoryStockTransferOverageApprove);
        Assert.True(native.ImplementationAvailable);
        Assert.Equal("/stok/transferler/fazla-kabul-onaylari", native.RoutePath);
    }

    private static F PurposeFixture()
    {
        var f = new F(); f.TransferLine(F.Id(1), F.Id(11), F.Id(21), 1); Receipt(f, F.Id(1), F.Id(11), 5m, 1);
        f.Rows.ReceiptRows[0].ReceivedAtUtc = F.T0.ToUniversalTime();
        f.Rows.ReceiptRows[0].OverageReason = "SENTETIK human-only reason";
        return f;
    }
}
