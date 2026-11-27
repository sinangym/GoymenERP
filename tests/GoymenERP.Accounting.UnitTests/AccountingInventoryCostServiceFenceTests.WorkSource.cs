using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class AccountingInventoryCostServiceFenceTests
{
    [Theory]
    [InlineData(AccountingInventoryCostKindV1.SalesInvoicedShipment, "Prepared")]
    [InlineData(AccountingInventoryCostKindV1.PurchaseInvoiceDifference, "Prepared")]
    [InlineData(AccountingInventoryCostKindV1.PurchaseInvoiceDifference, "Stale")]
    [InlineData(AccountingInventoryCostKindV1.SalesInvoicedShipment, "Posted")]
    public async Task Two_Work_sources_bind_exact_company_kind_native_formatter_and_private_no_cost_proof(AccountingInventoryCostKindV1 kind, string status)
    {
        var retained = DecisionSource(kind, status == "Prepared" ? "Posted" : status);
        if (status == "Prepared") retained = retained with { Proposal = retained.Prepared };
        var company = new MutableCompany(); var user = new MutableUser(); var authority = new SafetyAuthority(); await using var db = SafetyDb(company);
        var reader = DecisionService(new(retained), company, user, authority); var fence = new AccountingWorkInboxFence(company, user, authority);
        IWorkApprovalDecisionSourceV1 source = kind == AccountingInventoryCostKindV1.SalesInvoicedShipment
            ? new AccountingInvoicedShipmentCostWorkInboxSourceV1(db, fence, reader) : new AccountingPurchaseCostDifferenceWorkInboxSourceV1(db, fence, reader);
        var context = new WorkInboxSourceContextV1(Company, Actor.ToString("D"), "SENTETIK", null);
        var snapshot = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(context, SafetyId, default));
        var proof = Assert.IsType<WorkInventoryCostProofV1>(snapshot.InventoryCostProof);
        Assert.Equal((int)kind, proof.CostKind); Assert.Equal(status == "Prepared", snapshot.Pending); Assert.Equal(Company, proof.CompanyId);
        Assert.Equal(retained.PreparedRecordedAtUtc, snapshot.Item.WaitingSinceUtc);
        Assert.NotEqual(retained.Prepared.Preparation!.PreparedAtUtc, snapshot.Item.WaitingSinceUtc);
        Assert.True(AccountingInventoryCostDecisionBindingV1.MatchesDecisionProof(snapshot.Item, proof));
        var detail = (await reader.ReadDecisionAsync(SafetyId, kind, _ => Task.CompletedTask)).Resource!;
        Assert.True(AccountingInventoryCostDecisionBindingV1.MatchesDecisionItem(snapshot.Item, detail));
        Assert.Equal(AccountingInventoryCostDecisionBindingV1.ScreenPath(kind), snapshot.Item.Link);
        Assert.Equal("600", Assert.Single(snapshot.Item.Rules).Actual); Assert.Equal("TRY", snapshot.Item.Rules[0].Unit);
        Assert.Equal(new[]{"invoice","period"}, snapshot.Item.References.Select(x=>x.Role));
        Assert.True(snapshot.Item.Excludes(retained.Prepared.Preparation.PreparedBy.ToString("D"))); Assert.False(snapshot.Item.Excludes(Actor.ToString("D")));
        var wire = JsonSerializer.Serialize(snapshot, DecisionJson);
        foreach (var hidden in new[]{"inventoryCostProof","sourceFactsDigest","previewDigest","sourceProofJson","accountPlanPackageId","signedTlAmount"}) Assert.DoesNotContain(hidden, wire, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(13, JsonSerializer.SerializeToElement(proof, DecisionJson).EnumerateObject().Count());
        Assert.False(AccountingInventoryCostDecisionBindingV1.MatchesDecisionProof(snapshot.Item, proof with { CostKind = kind == AccountingInventoryCostKindV1.SalesInvoicedShipment ? 2 : 1 }));
        Assert.False(AccountingInventoryCostDecisionBindingV1.MatchesDecisionProof(snapshot.Item with { Link = "/invented" }, proof));
        Assert.False(AccountingInventoryCostDecisionBindingV1.MatchesDecisionItem(snapshot.Item with { Rules = [snapshot.Item.Rules[0] with { Actual = "601" }] }, detail));
        Assert.False(AccountingInventoryCostDecisionBindingV1.MatchesDecisionItem(snapshot.Item with { References = [snapshot.Item.References[0] with { RecordId = Guid.NewGuid() }, snapshot.Item.References[1]] }, detail));
    }
    [Fact]
    public async Task Cancelled_pending_human_remains_readable_but_is_not_an_approval_pending_snapshot()
    {
        var retained = DecisionSource(AccountingInventoryCostKindV1.SalesInvoicedShipment, "Posted");
        retained = retained with { Proposal = retained.Prepared, SourceCancelled = true };
        var company = new MutableCompany(); var user = new MutableUser(); var authority = new SafetyAuthority(); await using var db = SafetyDb(company);
        var source = new AccountingInvoicedShipmentCostWorkInboxSourceV1(db, new(company, user, authority), DecisionService(new(retained), company, user, authority));
        var snapshot = await source.ReadDecisionAsync(new(Company, Actor.ToString("D"), "SENTETIK", null), SafetyId, default);
        Assert.NotNull(snapshot); Assert.False(snapshot.Pending); Assert.Equal("Prepared", snapshot.InventoryCostProof?.Status); Assert.True(snapshot.InventoryCostProof?.SourceCancelled);
    }
    [Theory]
    [InlineData("missing-owner")] [InlineData("foreign-kind")] [InlineData("scope")] [InlineData("cancel")]
    public async Task Source_refuses_missing_owner_foreign_kind_or_scope_and_completed_final_cancellation(string fault)
    {
        var kind = AccountingInventoryCostKindV1.SalesInvoicedShipment; var company = new MutableCompany(); var user = new MutableUser(); var authority = new SafetyAuthority();
        await using var db = SafetyDb(company); using var cancellation = new CancellationTokenSource();
        var retained = DecisionSource(kind, "Posted");
        if (fault == "foreign-kind") retained = retained with { Proposal = retained.Proposal with { Kind = AccountingInventoryCostKindV1.PurchaseInvoiceDifference } };
        var source = new AccountingInvoicedShipmentCostWorkInboxSourceV1(db, new(company, user, authority), fault == "missing-owner" ? null : DecisionService(new(retained), company, user, authority));
        var context = new WorkInboxSourceContextV1(Company, Actor.ToString("D"), "SENTETIK", null);
        if (fault == "scope") { authority.CompanyWide = false; await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(context, SafetyId, default)); }
        else if (fault == "cancel") { authority.AfterRead = cancellation.Cancel; await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ReadDecisionAsync(context, SafetyId, cancellation.Token)); }
        else await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(context, SafetyId, default));
    }
    [Fact]
    public void Original_formatter_preserves_native_document_label_limits_whitespace_and_fallback_identity()
    {
        var source = DecisionSource(AccountingInventoryCostKindV1.SalesInvoicedShipment, "Posted"); var p = source.Prepared;
        foreach (var label in new string?[]{null," "," SENTETIK ",new('X',WorkInboxRulesV1.MaxDocumentLabelLength+1)})
        {
            var item = AccountingInventoryCostDecisionBindingV1.FromView(p with { OfficialInvoiceNumber = label }, source.PreparedRecordedAtUtc)!;
            var expected = string.IsNullOrWhiteSpace(label)||label.Length>WorkInboxRulesV1.MaxDocumentLabelLength ? p.ProposalId.ToString("D")[..8] : label;
            Assert.Equal(expected,item.DocumentLabel);
        }
    }
}
