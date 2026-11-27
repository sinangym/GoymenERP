using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.IntegrationTests;

// Transport shape only; original native result/hash serialization is explicitly compared below.
public sealed class WorkManufacturingCostDifferenceDecisionPreviewV1Tests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [Theory] [InlineData(2L)] [InlineData(9007199254740993L)] [InlineData(long.MaxValue)]
    public void Native_and_plan_Int64_versions_are_lossless_Work_text_without_mutating_native_wire(long version)
    {
        var source = ManufacturingCostDifferenceWorkDecisionHostTests.NewSource(version);
        var native = source.Proposal with { AccountPlan = source.Proposal.AccountPlan! with { PackageVersion = version, CatalogVersion = version } };
        var before = JsonSerializer.Serialize(native, Json);
        var detail = new ManufacturingCostDifferenceDecisionDetailV1(native, source.PreparedAtUtc!.Value, new('A', 64), new('B', 64));
        var work = WorkManufacturingCostDifferencePreviewV1.FromNative(detail);
        var wire = JsonSerializer.SerializeToElement(work, Json); var text = version.ToString(CultureInfo.InvariantCulture);
        Assert.Equal(text, wire.GetProperty("proposal").GetProperty("version").GetString());
        Assert.Equal(text, wire.GetProperty("proposal").GetProperty("accountPlan").GetProperty("packageVersion").GetString());
        Assert.Equal(text, wire.GetProperty("proposal").GetProperty("accountPlan").GetProperty("catalogVersion").GetString());
        Assert.Equal(before, JsonSerializer.Serialize(native, Json));
        var expected = JsonSerializer.SerializeToNode(native, Json)!.AsObject(); expected["version"] = text;
        expected["accountPlan"]!["packageVersion"] = text; expected["accountPlan"]!["catalogVersion"] = text;
        Assert.True(JsonNode.DeepEquals(expected, JsonSerializer.SerializeToNode(work.Proposal, Json)));
        Assert.Equal(version, JsonSerializer.SerializeToElement(native, Json).GetProperty("version").GetInt64());
    }

    [Fact]
    public void Full_twenty_header_eleven_output_and_seven_plan_fields_preserve_raw_strings_rows_and_nulls_without_private_receipt()
    {
        var source = ManufacturingCostDifferenceWorkDecisionHostTests.NewSource(3); var original = source.Proposal;
        var outputs = Enumerable.Range(0, 143).Select(_ => original.Outputs[0] with { OutputId = Guid.NewGuid() }).ToArray();
        original = original with { Outputs = outputs, AccountPlan = original.AccountPlan! with { CatalogVersion = null } };
        var receipt = new ManufacturingCostDifferenceApprovalReceiptV1(original.CompanyId, original.ProposalId, Guid.NewGuid(),
            original.PreparedBy!.Value, source.PreparedAtUtc!.Value, ManufacturingCostDifferenceCommandNamesV1.Approve, new('C', 64), new('D', 64),
            Guid.NewGuid(), 2, 3, "SENTETIK private receipt correlation", source.PreparedAtUtc.Value, new('E', 64), new('F', 64));
        original = original with { ApprovalReceipt = receipt };
        var detail = new ManufacturingCostDifferenceDecisionDetailV1(original, source.PreparedAtUtc.Value, new('A', 64), new('B', 64));
        var work = WorkManufacturingCostDifferencePreviewV1.FromNative(detail); var wire = JsonSerializer.SerializeToElement(work, Json);
        Assert.Equal(4, wire.EnumerateObject().Count()); Assert.Equal(20, wire.GetProperty("proposal").EnumerateObject().Count());
        Assert.Equal(7, wire.GetProperty("proposal").GetProperty("accountPlan").EnumerateObject().Count());
        Assert.Equal(143, wire.GetProperty("proposal").GetProperty("outputs").GetArrayLength());
        Assert.All(wire.GetProperty("proposal").GetProperty("outputs").EnumerateArray(), row => Assert.Equal(11, row.EnumerateObject().Count()));
        Assert.Same(outputs, work.Proposal.Outputs); Assert.Same(original.AccountPlan!.Debit, work.Proposal.AccountPlan!.Debit);
        Assert.Equal(JsonValueKind.Null, wire.GetProperty("proposal").GetProperty("accountPlan").GetProperty("catalogVersion").ValueKind);
        Assert.Equal("2 / 3", work.Proposal.Outputs[0].RemainingQuantityKg); Assert.Equal("600", work.Proposal.SignedTlDifference);
        Assert.Equal(original.Reason, work.Proposal.Reason); Assert.Equal(original.TransactionDate, work.Proposal.TransactionDate);
        Assert.DoesNotContain("approvalReceipt", wire.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(receipt.OriginalCorrelationId, wire.GetRawText(), StringComparison.Ordinal);
        Assert.Same(receipt, original.ApprovalReceipt);
    }

    [Fact]
    public void Native_UTC_ticks_dates_and_status_are_carried_without_new_chronology_or_default_policy()
    {
        var source = ManufacturingCostDifferenceWorkDecisionHostTests.NewSource(2);
        var proposal = source.Proposal with { SourceOccurredAtUtc = DateTimeOffset.UnixEpoch.AddTicks(1),
            AccountingDate = new(1, 1, 2), TransactionDate = new(9999, 12, 31) };
        var detail = new ManufacturingCostDifferenceDecisionDetailV1(proposal, DateTimeOffset.UnixEpoch, new('A', 64), new('B', 64));
        var work = WorkManufacturingCostDifferencePreviewV1.FromNative(detail);
        Assert.Equal(proposal.SourceOccurredAtUtc, work.Proposal.SourceOccurredAtUtc); Assert.Equal(detail.PreparedAtUtc, work.PreparedAtUtc);
        Assert.Equal(proposal.AccountingDate, work.Proposal.AccountingDate); Assert.Equal(proposal.TransactionDate, work.Proposal.TransactionDate);
        Assert.Equal("Prepared", work.Proposal.Status); Assert.Null(work.Proposal.ApprovedBy); Assert.Null(work.Proposal.JournalId);
    }
}
