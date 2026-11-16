using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Returns;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// ALC-S5 (B-19; SAT-D306, SAT-D392, SAT-D393, SAT-D396): the supplier return cost-exception rule and the wire shape. A pending
/// manual-cost return is approved or rejected once, at its version; the decider is someone else unless the (unset, OPEN) policy
/// allows the same person; a rejection needs a bounded reason. The wire accepts only canonical kg / unit cost texts, a known source
/// block, a reason and UTC time. SENTETIK / NOT_ENV.
/// </summary>
public sealed class PurchaseSupplierReturnTests
{
    private const string Maker = "SENTETIK-iade-giren";
    private const string Checker = "SENTETIK-istisna-onaylayan";
    private static readonly Guid ReturnId = Guid.Parse("5a5a5a5a-5a5a-4a5a-8a5a-5a5a5a5a5a5a");

    private static PurchaseSupplierReturn Pending(string? maker = Maker) =>
        new(ReturnId, PurchaseSupplierReturnStatus.PendingCostException, 1, maker);

    private static string CodeOf(Action action) => Assert.Throws<PurchasingRuleException>(action).Code;

    [Fact]
    public void An_approval_by_another_person_records_the_return_and_moves_the_version()
    {
        var item = Pending();
        item.Approve(1, Checker);
        Assert.Equal(PurchaseSupplierReturnStatus.Recorded, item.Status);
        Assert.Equal(2, item.Version);
        Assert.Equal(Checker, item.DecidedBy);
        Assert.Null(item.Reason);
    }

    [Fact]
    public void A_rejection_keeps_the_trimmed_reason_and_closes_the_return()
    {
        var item = Pending();
        item.Reject(1, Checker, "  SENTETIK maliyet belgesi yok  ");
        Assert.Equal(PurchaseSupplierReturnStatus.Rejected, item.Status);
        Assert.Equal("SENTETIK maliyet belgesi yok", item.Reason);
        Assert.Equal(2, item.Version);
    }

    [Fact]
    public void The_person_who_entered_the_return_cannot_decide_it_by_default()
    {
        Assert.Equal(PurchaseSupplierReturnRuleCodes.SameActor, CodeOf(() => Pending().Approve(1, Maker)));
        Assert.Equal(PurchaseSupplierReturnRuleCodes.SameActor, CodeOf(() => Pending().Reject(1, Maker, "SENTETIK")));
        Assert.Equal(PurchaseSupplierReturnRuleCodes.MakerUnknown, CodeOf(() => Pending(maker: null).Approve(1, Checker)));
    }

    [Fact]
    public void The_same_person_decides_only_when_the_policy_input_allows_it()
    {
        var item = Pending();
        item.Approve(1, Maker, allowSameActorDecision: true);
        Assert.Equal(PurchaseSupplierReturnStatus.Recorded, item.Status);
        Assert.False(PurchaseSupplierReturnPolicyV1.Default.AllowSameActorCostExceptionDecision);
    }

    [Theory]
    [InlineData(PurchaseSupplierReturnStatus.Recorded)]
    [InlineData(PurchaseSupplierReturnStatus.Rejected)]
    public void Only_a_pending_return_is_decided(PurchaseSupplierReturnStatus status)
    {
        var item = new PurchaseSupplierReturn(ReturnId, status, 2, Maker);
        Assert.Equal(PurchaseSupplierReturnRuleCodes.NotPending, CodeOf(() => item.Approve(2, Checker)));
        Assert.Equal(PurchaseSupplierReturnRuleCodes.NotPending, CodeOf(() => item.Reject(2, Checker, "SENTETIK")));
    }

    [Fact]
    public void A_stale_version_is_a_conflict_and_changes_nothing()
    {
        var item = Pending();
        Assert.Throws<PurchasingVersionConflictException>(() => item.Approve(2, Checker));
        Assert.Equal(PurchaseSupplierReturnStatus.PendingCostException, item.Status);
        Assert.Equal(1, item.Version);
    }

    [Fact]
    public void A_rejection_needs_a_bounded_reason()
    {
        Assert.Equal(PurchaseSupplierReturnRuleCodes.RejectionReasonRequired, CodeOf(() => Pending().Reject(1, Checker, "   ")));
        Assert.Equal(PurchaseSupplierReturnRuleCodes.RejectionReasonRequired, CodeOf(() => Pending().Reject(1, Checker, null)));
        Assert.Equal(PurchaseSupplierReturnRuleCodes.RejectionReasonTooLong,
            CodeOf(() => Pending().Reject(1, Checker, new string('x', PurchaseSupplierReturn.MaximumReasonLength + 1))));
    }

    // ---- wire shape ---------------------------------------------------------------------------------

    private static string CreateJson(string quantity = "\"12.5\"", string cost = "null", string block = "\"Free\"",
        string replacement = "false", string reason = "\"SENTETIK hatalı ölçü\"", string occurred = "\"2026-11-16T06:00:00+00:00\"") =>
        $$"""
        {"returnId":"5a5a5a5a-5a5a-4a5a-8a5a-5a5a5a5a5a5a","returnLineId":"6b6b6b6b-6b6b-4b6b-8b6b-6b6b6b6b6b6b",
         "goodsAcceptanceId":"7c7c7c7c-7c7c-4c7c-8c7c-7c7c7c7c7c7c","inventoryMovementId":"8d8d8d8d-8d8d-4d8d-8d8d-8d8d8d8d8d8d",
         "sourceBlock":{{block}},"quantityKg":{{quantity}},"manualUnitCostTryPerKg":{{cost}},"replacementExpected":{{replacement}},
         "reason":{{reason}},"occurredAtUtc":{{occurred}}}
        """;

    [Fact]
    public void A_canonical_create_body_is_read()
    {
        var request = PurchaseSupplierReturnJsonV1.ReadCreate(CreateJson(cost: "\"41.2575\"", block: "\"Blocked\""));
        Assert.Equal("Blocked", request.SourceBlock);
        Assert.Equal("12.5", request.QuantityKg);
        Assert.Equal("41.2575", request.ManualUnitCostTryPerKg);
        Assert.False(request.ReplacementExpected);
    }

    [Theory]
    [InlineData("\"0\"", "null", "\"Free\"", "\"SENTETIK\"", "\"2026-11-16T06:00:00+00:00\"")]
    [InlineData("\"1.2345\"", "null", "\"Free\"", "\"SENTETIK\"", "\"2026-11-16T06:00:00+00:00\"")]
    [InlineData("\"01\"", "null", "\"Free\"", "\"SENTETIK\"", "\"2026-11-16T06:00:00+00:00\"")]
    [InlineData("\"1.50\"", "null", "\"Free\"", "\"SENTETIK\"", "\"2026-11-16T06:00:00+00:00\"")]
    [InlineData("\"1\"", "\"1.23456\"", "\"Free\"", "\"SENTETIK\"", "\"2026-11-16T06:00:00+00:00\"")]
    [InlineData("\"1\"", "\"0\"", "\"Free\"", "\"SENTETIK\"", "\"2026-11-16T06:00:00+00:00\"")]
    [InlineData("\"1\"", "null", "\"free\"", "\"SENTETIK\"", "\"2026-11-16T06:00:00+00:00\"")]
    [InlineData("\"1\"", "null", "\"Free\"", "\"   \"", "\"2026-11-16T06:00:00+00:00\"")]
    [InlineData("\"1\"", "null", "\"Free\"", "\"SENTETIK\"", "\"2026-11-16T09:00:00+03:00\"")]
    public void A_non_canonical_create_body_is_refused(string quantity, string cost, string block, string reason, string occurred) =>
        Assert.Throws<JsonException>(() => PurchaseSupplierReturnJsonV1.ReadCreate(
            CreateJson(quantity: quantity, cost: cost, block: block, reason: reason, occurred: occurred)));

    [Fact]
    public void A_decision_body_needs_a_canonical_version()
    {
        Assert.Equal("3", PurchaseSupplierReturnJsonV1.ReadDecision("""{"expectedVersion":"3","reason":null}""").ExpectedVersion);
        Assert.Throws<JsonException>(() => PurchaseSupplierReturnJsonV1.ReadDecision("""{"expectedVersion":"03","reason":null}"""));
        Assert.Throws<JsonException>(() => PurchaseSupplierReturnJsonV1.ReadDecision("""{"expectedVersion":"0","reason":null}"""));
    }
}
