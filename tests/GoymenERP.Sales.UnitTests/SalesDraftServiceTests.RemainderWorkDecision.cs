using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Remainder_pending_source_keeps_requester_and_creator_metadata_but_excludes_them_only_for_the_native_SalesSupport_persona(bool support)
    {
        var (w, order) = await RemainderProtocolWorld(); w.Bindings = w.Bindings with { ApprovePermission = SalesPermissionManifestV1.OrdersApprove };
        if (support) w.ActiveRoleCodes.Codes.Add(ErpPersonaRoleCodes.SalesSupport);
        var source = RemainderSource(w); var answer = await ((IWorkInboxSourceV1)source).ListPendingAsync(RemainderContext(), 10, default);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, answer.Outcome); var item = Assert.Single(answer.Items!);
        Assert.Equal((order.Id, Actor, order.PendingRemainderChange!.Request.RequestedAtUtc), (item.ItemId, item.PreparedBy, item.WaitingSinceUtc));
        Assert.Equal($"{SalesRemainderChangeWorkInboxSourceV1.WorkDecisionPath}/{order.Id:D}", item.Link); Assert.False(answer.HasMore);
        if (support) Assert.Equal(Actor, Assert.Single(item.ExcludedDeciders!)); else Assert.Null(item.ExcludedDeciders);
        var rule = Assert.Single(item.Rules); Assert.Equal(SalesRemainderChangeDecisionBindingV1.RequestedRule, rule.RuleCode); Assert.Null(rule.Actual); Assert.Null(rule.Threshold); Assert.Null(rule.Deviation); Assert.Null(rule.Unit);
        Assert.DoesNotContain("SENTETIK kalan", JsonSerializer.Serialize(item, SalesDraftJson.Options), StringComparison.Ordinal);
        Assert.Equal(support, item.Excludes(Actor));
        Assert.True((item with { Kind = "sales.order-draft.v1" }).Excludes(Actor));
        Assert.True((item with { Module = "logistics" }).Excludes(Actor));
        Assert.True((item with { ExcludedDeciders = [Actor] }).Excludes(Actor));
    }

    [Fact]
    public async Task Remainder_current_source_binds_actual_owner_snapshot_and_rejects_altered_identity_rule_reference_and_creator_exclusions()
    {
        var (w, order) = await RemainderProtocolWorld(); w.Bindings = w.Bindings with { ApprovePermission = SalesPermissionManifestV1.OrdersApprove };
        var source = RemainderSource(w); var snapshot = await source.ReadDecisionAsync(RemainderContext(), order.Id, default); Assert.NotNull(snapshot);
        var proof = Assert.IsType<WorkSalesRemainderChangeProofV1>(snapshot.SalesRemainderChangeProof);
        Assert.Equal((6L, 6L, 1), (proof.SelectedExpectedVersion, proof.ObservedCurrentVersion, proof.ChangeNo)); Assert.Equal("6", snapshot.SourceVersion); Assert.True(snapshot.Pending);
        var detail = (await w.Service().WithRemainderDecisionSafety().ReadRemainderDecisionAsync(order.Id, _ => Task.CompletedTask)).Detail!;
        Assert.True(SalesRemainderChangeDecisionBindingV1.MatchesDecisionItem(snapshot.Item, detail));
        Assert.True(SalesRemainderChangeDecisionBindingV1.MatchesDecisionProof(snapshot.Item, proof));
        var lineReference = Assert.Single(snapshot.Item.References, x => x.Role == "sales-order-line");
        Assert.Equal(proof.LineId, lineReference.RecordId); Assert.True(WorkInboxRulesV1.IsValidReference(lineReference));
        Assert.False(SalesRemainderChangeDecisionBindingV1.MatchesDecisionProof(snapshot.Item, proof with { LineId = Guid.NewGuid() }));
        Assert.False(SalesRemainderChangeDecisionBindingV1.MatchesDecisionProof(snapshot.Item with
        { References = [snapshot.Item.References[0], lineReference with { RecordId = Guid.NewGuid() }] }, proof));
        Assert.False(SalesRemainderChangeDecisionBindingV1.MatchesDecisionProof(snapshot.Item with { ItemId = Guid.NewGuid() }, proof));
        Assert.False(SalesRemainderChangeDecisionBindingV1.MatchesDecisionProof(snapshot.Item with { DocumentLabel = "SENTETIK yanlış" }, proof));
        Assert.False(SalesRemainderChangeDecisionBindingV1.MatchesDecisionProof(snapshot.Item with { References = [snapshot.Item.References[0] with { RecordId = Guid.NewGuid() }] }, proof));
        Assert.False(SalesRemainderChangeDecisionBindingV1.MatchesDecisionProof(snapshot.Item with { Rules = [snapshot.Item.Rules[0] with { Actual = "1" }] }, proof));
        Assert.False(SalesRemainderChangeDecisionBindingV1.MatchesDecisionProof(snapshot.Item with { ExcludedDeciders = [Actor] }, proof));
        Assert.False(SalesRemainderChangeDecisionBindingV1.MatchesDecisionItem(snapshot.Item, detail with { DraftCreatedBy = "SENTETIK wrong", IsSalesSupport = true }));
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(snapshot, SalesDraftJson.Options)); Assert.False(wire.RootElement.TryGetProperty("salesRemainderChangeProof", out _));
    }

    [Theory] [InlineData("authority")] [InlineData("actor")] [InlineData("company")] [InlineData("scope")]
    public async Task Remainder_source_refuses_wrong_authority_or_context_before_owner_IO(string fault)
    {
        var (w, order) = await RemainderProtocolWorld(); w.Bindings = w.Bindings with { ApprovePermission = SalesPermissionManifestV1.OrdersApprove }; var context = RemainderContext();
        if (fault == "authority") w.Authority.Denied.Add(SalesPermissionManifestV1.OrdersApprove); else if (fault == "actor") context = context with { UserId = "SENTETIK other" };
        else if (fault == "company") context = context with { CompanyId = Guid.NewGuid() }; else context = context with { GrantScope = new(new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => RemainderSource(w).ReadDecisionAsync(context, order.Id, default)); Assert.Equal(0, w.Store.RemainderOwnerReads);
    }

    [Fact]
    public async Task Remainder_selected_past_without_genuine_receipt_never_falls_back_to_a_current_maker_or_success()
    {
        var (w, order) = await RemainderProtocolWorld(); w.Bindings = w.Bindings with { ApprovePermission = SalesPermissionManifestV1.OrdersApprove };
        await Assert.ThrowsAsync<InvalidOperationException>(() => RemainderSource(w).ReadDecisionVersionAsync(RemainderContext(), order.Id, "5", default));
        Assert.Equal(0, w.Store.RemainderOwnerReads); Assert.Equal(1, w.Store.RemainderOriginalReads);
    }

    private static WorkInboxSourceContextV1 RemainderContext() => new(Company, Actor, "SENTETIK-remainder-work", null);
    private static SalesRemainderChangeWorkInboxSourceV1 RemainderSource(World w)
    { var company = new CompanyContext(); company.Set(Company); return new(company, w.User, w.Authority, w.Service().WithRemainderDecisionSafety()); }
}
