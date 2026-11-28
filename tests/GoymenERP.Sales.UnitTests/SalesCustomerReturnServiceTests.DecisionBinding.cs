using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using static GoymenERP.Sales.UnitTests.SalesCustomerReturnFixtures;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesCustomerReturnServiceTests
{
    [Fact]
    public async Task Return_selected_source_reuses_native_item_and_binds_all_rules_references_and_both_actual_makers()
    {
        var f = SafetyWorld(); var detail = (await f.Service.ReadCustomerReturnDecisionAsync(ReturnId, _ => Task.CompletedTask)).Detail!;
        var source = new SalesCustomerReturnWorkInboxSourceV1(null!, f.Company, f.User, f.Authority, f.Service);
        var snapshot = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(new(Company, Approver, "SENTETIK-work", null), ReturnId, default));
        Assert.True(SalesCustomerReturnDecisionBindingV1.MatchesDecisionSnapshot(snapshot, detail)); Assert.True(snapshot.Pending); Assert.Equal("2", snapshot.SourceVersion);
        Assert.Equal("/satis/iadeler/" + ReturnId.ToString("D"), snapshot.Item.Link); Assert.True(snapshot.Item.Excludes(Preparer)); Assert.True(snapshot.Item.Excludes("SENTETIK-submitter"));
        var privateWire = JsonSerializer.Serialize(snapshot, SalesDraftJson.Options); Assert.DoesNotContain("sourceFactsDigest", privateWire, StringComparison.Ordinal);
        Assert.False(SalesCustomerReturnDecisionBindingV1.MatchesDecisionProof(snapshot.Item with { DocumentLabel = "SENTETIK wrong" }, snapshot.SalesCustomerReturnProof));
        Assert.False(SalesCustomerReturnDecisionBindingV1.MatchesDecisionProof(snapshot.Item with { Rules = [new("sales.customer-return-approval-required", null, "1", null, null)] }, snapshot.SalesCustomerReturnProof));
        Assert.False(SalesCustomerReturnDecisionBindingV1.MatchesDecisionProof(snapshot.Item with { References = [] }, snapshot.SalesCustomerReturnProof));
        Assert.False(SalesCustomerReturnDecisionBindingV1.MatchesDecisionProof(snapshot.Item with { ExcludedDeciders = null }, snapshot.SalesCustomerReturnProof));
    }
    [Fact]
    public async Task Return_historical_source_is_private_and_uses_original_decision_identity_separate_from_current_native_version()
    {
        var artifact = await ReturnReceiptFixture(true, "2"); var r = Assert.IsType<SalesCustomerReturnOriginalReceiptV1>(RestoreReturn(artifact)); var f = SafetyWorld();
        f.Store.DecisionOriginalAnswer = new(SalesCustomerReturnDecisionReadStateV1.Found, r) { CurrentVersion = 4 };
        var source = new SalesCustomerReturnWorkInboxSourceV1(null!, f.Company, f.User, f.Authority, f.Service);
        var snapshot = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionVersionAsync(new(Company, Approver, "SENTETIK-work", null), ReturnId, "2", default));
        Assert.False(snapshot.Pending); Assert.Equal("2", snapshot.SourceVersion); Assert.Equal((2L, 4L, "Approved"),
            (snapshot.SalesCustomerReturnProof!.SelectedExpectedVersion, snapshot.SalesCustomerReturnProof.CurrentVersion, snapshot.SalesCustomerReturnProof.State));
        Assert.True(SalesCustomerReturnDecisionBindingV1.MatchesOriginalDecisionReceipt(snapshot, artifact.Answer with { OriginalDecisionReceipt = r, IsReplay = true }, artifact.Request, Metadata.IdempotencyKey, Approver, true));
        Assert.False(SalesCustomerReturnDecisionBindingV1.MatchesOriginalDecisionReceipt(snapshot, artifact.Answer with { OriginalDecisionReceipt = r }, artifact.Request, Metadata.IdempotencyKey, Approver, true));
        Assert.False(SalesCustomerReturnDecisionBindingV1.MatchesOriginalDecisionReceipt(snapshot, artifact.Answer with { OriginalDecisionReceipt = r, IsReplay = true }, artifact.Request, "SENTETIK other-key", Approver, true));
    }
    [Theory] [InlineData("02")] [InlineData("0")]
    public async Task Return_selected_source_rejects_noncanonical_versions_before_native_owner_read(string n)
    {
        var f = SafetyWorld(); var source = new SalesCustomerReturnWorkInboxSourceV1(null!, f.Company, f.User, f.Authority, f.Service);
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionVersionAsync(new(Company, Approver, "SENTETIK-work", null), ReturnId, n, default));
        Assert.Equal(0, f.Store.DecisionOriginalReads); Assert.Empty(f.Store.Calls);
    }
}
