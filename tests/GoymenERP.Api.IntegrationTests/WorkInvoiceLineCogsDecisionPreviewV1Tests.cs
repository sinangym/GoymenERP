using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.IntegrationTests;

// Lossless typed transport assertions, not HTTP or financial calculation acceptance.
public sealed class WorkInvoiceLineCogsDecisionPreviewV1Tests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [Theory] [InlineData(1L)] [InlineData(9007199254740993L)] [InlineData(long.MaxValue)]
    public void Every_native_Int64_at_all_nested_cost_sources_is_Work_text_without_changing_native_wire(long version)
    {
        var detail = FullDetail(version); var before = JsonSerializer.Serialize(detail.Proposal, Json);
        var work = WorkInvoiceLineCogsPreviewV1.FromNative(detail); var wire = JsonSerializer.SerializeToNode(work, Json)!.AsObject();
        var expected = JsonSerializer.SerializeToNode(detail.Proposal, Json)!; NormalizeNativeLongs(expected);
        Assert.True(JsonNode.DeepEquals(expected, wire["proposal"]));
        var expectedSource = JsonSerializer.SerializeToNode(detail.Source, Json)!; NormalizeNativeLongs(expectedSource);
        Assert.True(JsonNode.DeepEquals(expectedSource, wire["source"]));
        Assert.Equal(version.ToString(CultureInfo.InvariantCulture), work.CurrentLineVersion);
        Assert.Equal(before, JsonSerializer.Serialize(detail.Proposal, Json));
        Assert.Equal(version, JsonSerializer.SerializeToElement(detail.Proposal, Json).GetProperty("finalizedVersion").GetInt64());
        Assert.Equal(version.ToString(CultureInfo.InvariantCulture), work.Proposal.AttributedEvidence![0].Source.MoneyProfileVersion);
        Assert.Equal(version.ToString(CultureInfo.InvariantCulture), work.Proposal.AttributedEvidence[0].Source.Shipment.ProvenOriginalSequence);
        Assert.Equal(version.ToString(CultureInfo.InvariantCulture), work.Proposal.PurchaseAttributedEvidence![0].Source.Approval.ProposalVersion);
        Assert.Equal(version.ToString(CultureInfo.InvariantCulture), work.Proposal.PurchaseAttributedEvidence[0].Source.Approval.RecordedRounding.MoneyProfileVersion);
        Assert.Equal(version.ToString(CultureInfo.InvariantCulture), work.Proposal.PurchaseAttributedEvidence[0].Source.Candidates[0].Shipment.ProvenOriginalSequence);
    }
    [Fact]
    public void Whole_five_field_wrapper_and_native_twenty_five_header_fields_keep_all_rows_and_no_private_receipt()
    {
        var detail = FullDetail(long.MaxValue); var p = detail.Proposal;
        var receipt = new InvoiceLineCogsApprovalReceiptV1(p.CompanyId, p.ProposalId, p.InvoiceId, p.InvoiceLineId, p.PreparedBy, p.PreparedAtUtc,
            InvoiceLineCogsCorrectionServiceV1.CogsApproveCommand, new('A', 64), new('B', 64), Guid.NewGuid(), 1, 2, "SENTETIK PRIVATE CORRELATION",
            p.PreparedAtUtc, new('C', 64), new('D', 64));
        var rows = Enumerable.Range(0, 137).Select(_ => p.AttributedEvidence![0]).ToArray();
        p = p with { ApprovalReceipt = receipt, AttributedEvidence = rows }; detail = detail with { Proposal = p };
        var work = WorkInvoiceLineCogsPreviewV1.FromNative(detail); var wire = JsonSerializer.SerializeToElement(work, Json);
        Assert.Equal(5, wire.EnumerateObject().Count()); Assert.Equal(25, wire.GetProperty("proposal").EnumerateObject().Count());
        Assert.Equal(14, wire.GetProperty("source").EnumerateObject().Count()); Assert.Equal(137, wire.GetProperty("proposal").GetProperty("attributedEvidence").GetArrayLength());
        Assert.Equal(p.PostedEvidence!.Amount, work.Proposal.PostedEvidence!.Amount); Assert.Equal(p.PurchaseAttributedEvidence![0].Source.SignedAmount, work.Proposal.PurchaseAttributedEvidence![0].Source.SignedAmount);
        Assert.Equal(p.AttributedEvidence![0].Source.ExactSignedAmount, work.Proposal.AttributedEvidence![0].Source.ExactSignedAmount);
        Assert.Equal(p.AttributedEvidence[0].Source.Shipment.PhysicalProofJson, work.Proposal.AttributedEvidence[0].Source.Shipment.PhysicalProofJson);
        Assert.DoesNotContain("approvalReceipt", wire.GetRawText(), StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain(receipt.OriginalCorrelationId, wire.GetRawText()); Assert.Same(receipt, p.ApprovalReceipt);
    }
    [Fact]
    public void Nullable_legacy_branches_dates_UTC_ticks_and_raw_text_are_carried_without_new_policy()
    {
        var f = InvoiceLineCogsWorkDecisionHostTests.NewFacts(); var p = f.Proposal with { AccountPlan = f.Proposal.AccountPlan with { CatalogVersion = null },
            TransactionDate = new(1, 1, 2), AccountingDate = new(9999, 12, 31), ValueDate = null, PreparedAtUtc = DateTimeOffset.UnixEpoch.AddTicks(1) };
        var work = WorkInvoiceLineCogsPreviewV1.FromNative(new(p, f.Source, 0, new('A', 64), new('B', 64)));
        var json = JsonSerializer.SerializeToElement(work.Proposal, Json); Assert.Equal(22, json.EnumerateObject().Count());
        Assert.False(json.TryGetProperty("attributedEvidence", out _)); Assert.False(json.TryGetProperty("purchaseAttributedEvidence", out _)); Assert.False(json.TryGetProperty("attributedCorrections", out _));
        Assert.Null(work.Proposal.AccountPlan.CatalogVersion); Assert.Null(work.Proposal.ValueDate); Assert.Equal(p.PreparedAtUtc, work.Proposal.PreparedAtUtc);
        Assert.Equal(p.TransactionDate, work.Proposal.TransactionDate); Assert.Equal(p.AccountingDate, work.Proposal.AccountingDate); Assert.Equal("100.0100", work.Proposal.PostedEvidence!.Amount);
    }
    private static InvoiceLineCogsDecisionDetailV1 FullDetail(long version)
    {
        var f = InvoiceLineCogsWorkDecisionHostTests.NewFacts(); var p = f.Proposal; var id = p.ProposalId; var at = p.PreparedAtUtc;
        var ratio = new AccountingInventoryCostRatioV1("1", "3");
        var manufacturingShipment = new ManufacturingAttributionShipmentV1(id, id, 1, id, id, id, 100.001m, ratio, ratio, at, version, false, "{\"SENTETIK\":true}", []);
        var manufacturing = new PostedManufacturingCostShareV1(p.CompanyId, id, id, id, "9223372036854775807", id, id, id, id, id,
            "100.0100", "-2.0100", ratio, "0.0000", id, version, 2, p.AccountingDate, manufacturingShipment, new('A', 64), new('B', 64), new('C', 64), new('D', 64));
        var purchaseShipment = new PurchaseFirstCostShipmentV1(id, id, 1, id, id, id, 100.001m, ratio, ratio, at, version, [], "{\"SENTETIK\":true}");
        var rounding = new AccountingInventoryCostRecordedRoundingV1(id, id, version, 2, new(ratio, "0.3300", ratio, "SENTETIK-rule"));
        var approval = new PurchaseFirstCostApprovalV1(p.CompanyId, id, id, id, version, null, "RoundedNoCost", "0.0000", id, id, version, 2, id, id,
            p.AccountingDate, p.PreparedBy, id, at, rounding, new('A', 64), new('B', 64));
        var candidate = new PurchaseFirstCostCandidateV1(p.CompanyId, id, id, id, id, id, id, ratio, ratio, ratio, purchaseShipment, new('A', 64), new('B', 64), new('C', 64));
        var purchased = new PostedPurchaseCostShareV1(approval, id, id, 1, id, 100.001m, "0.0000", ratio, ratio, [candidate], new('D', 64));
        p = p with { FinalizedVersion = version, Version = version, ExpectedLineVersion = version, AccountPlan = p.AccountPlan with { PackageVersion = version, CatalogVersion = version },
            PostedEvidence = p.PostedEvidence! with { FinalizedVersion = version }, AttributedEvidence = [new(manufacturing, id, id, id, "-2.0100")],
            PurchaseAttributedEvidence = [new(purchased, null, id, id, "0.0000")], AttributedCorrections = [new(new('A', 64), id, id, id)] };
        return new(p, f.Source with { FinalizedVersion = version }, version, new('A', 64), new('B', 64));
    }
    private static readonly HashSet<string> LongFields = ["version", "finalizedVersion", "expectedLineVersion", "packageVersion", "catalogVersion", "moneyProfileVersion", "proposalVersion", "provenOriginalSequence"];
    // Only the closed native Int64 property names change representation; every other native field must compare exactly.
    private static void NormalizeNativeLongs(JsonNode node)
    {
        if (node is JsonArray array) { foreach (var row in array) if (row is not null) NormalizeNativeLongs(row); return; }
        if (node is not JsonObject value) return;
        foreach (var pair in value.ToArray())
        {
            if (pair.Value is null) continue;
            if (LongFields.Contains(pair.Key)) value[pair.Key] = pair.Value.GetValue<long>().ToString(CultureInfo.InvariantCulture);
            else NormalizeNativeLongs(pair.Value);
        }
    }
}
