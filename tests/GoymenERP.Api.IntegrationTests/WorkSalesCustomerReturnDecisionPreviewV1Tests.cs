using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class WorkSalesCustomerReturnDecisionPreviewV1Tests
{
    internal static readonly Guid Company = Guid.Parse("6a210000-0000-4000-8000-000000000001"), Id = Guid.Parse("6a210000-0000-4000-8000-000000000002"),
        Actor = Guid.Parse("6a210000-0000-4000-8000-000000000003"), Maker = Guid.Parse("6a210000-0000-4000-8000-000000000004"), Submitter = Guid.Parse("6a210000-0000-4000-8000-000000000005");
    internal static readonly DateTimeOffset At = new(2026, 11, 28, 10, 0, 0, TimeSpan.Zero);
    [Theory] [InlineData(2L)] [InlineData(9007199254740993L)] [InlineData(long.MaxValue - 1)]
    public void Full_native_customer_invoice_return_is_a_three_field_wrapper_with_lossless_34_14_6_fields_and_text_version(long n)
    {
        var d = Seal(Facts() with { CustomerReturn = Facts().CustomerReturn with { Version = n.ToString(CultureInfo.InvariantCulture) } });
        var human = WorkSalesCustomerReturnDecisionPreviewV1.FromNative(d); Assert.Equal(d.CustomerReturn, human.CustomerReturn with { Lines = d.CustomerReturn.Lines });
        var json = JsonSerializer.SerializeToElement(human, SalesDraftJson.Options); Assert.Equal(3, json.EnumerateObject().Count());
        var v = json.GetProperty("customerReturn"); Assert.Equal(34, v.EnumerateObject().Count()); Assert.Equal(JsonValueKind.String, v.GetProperty("version").ValueKind);
        Assert.Equal(n.ToString(CultureInfo.InvariantCulture), v.GetProperty("version").GetString()); Assert.Equal(14, v.GetProperty("lines")[0].EnumerateObject().Count());
        Assert.Equal(6, v.GetProperty("totals").EnumerateObject().Count()); Assert.Equal(6, v.GetProperty("lines")[0].GetProperty("functionalAmounts").EnumerateObject().Count());
        Assert.Equal(d.CustomerReturn.Lines[0].Quantity, v.GetProperty("lines")[0].GetProperty("quantity").GetString());
        Assert.Equal(d.CustomerReturn.Rate, v.GetProperty("rate").GetString()); Assert.Equal(d.CustomerReturn.Totals.LineTotalAmount, v.GetProperty("totals").GetProperty("lineTotalAmount").GetString());
        foreach (var hidden in new[] { "digest", "receipt", "payload", "audit", "sourceJson", "idempotency" }) Assert.DoesNotContain(hidden, json.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }
    [Theory] [InlineData("source")] [InlineData("preview")] [InlineData("version")] [InlineData("submitted")] [InlineData("null")]
    public void Unbound_or_missing_owner_human_facts_never_project(string fault)
    {
        var d = Facts(); d = fault switch { "source" => d with { SourceFactsDigest = new('a',64) }, "preview" => d with { PreviewDigest = new('b',64) },
            "version" => Seal(d with { CustomerReturn = d.CustomerReturn with { Version = "02" } }), "submitted" => Seal(d with { CustomerReturn = d.CustomerReturn with { SubmittedBy = null } }), _ => null! };
        Assert.Equal(503, Assert.Throws<WorkDecisionException>(() => WorkSalesCustomerReturnDecisionPreviewV1.FromNative(d)).Status);
    }
    [Fact]
    public void Work_four_field_body_has_no_native_return_identity_or_private_provenance_selector()
    {
        var options = new JsonSerializerOptions(SalesDraftJson.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true };
        var q = new WorkDecisionRequestV1("2", false, "  SENTETIK ret  "); var text = JsonSerializer.Serialize(q, options);
        Assert.Equal(4, JsonDocument.Parse(text).RootElement.EnumerateObject().Count()); Assert.Equal(q, JsonSerializer.Deserialize<WorkDecisionRequestV1>(text, options));
        foreach (var field in new[] { "returnId", "invoiceId", "submittedBy", "sourceFactsDigest", "originalDecisionReceipt" })
        { var node = System.Text.Json.Nodes.JsonNode.Parse(text)!.AsObject(); node[field] = "SENTETIK"; Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WorkDecisionRequestV1>(node.ToJsonString(), options)); }
    }
    internal static SalesCustomerReturnDecisionDetailV1 Facts()
    {
        var amounts = new SalesCustomerReturnAmountsView("1.00", "0.20", "0.00", "1.20", "1.20", "0.00");
        var line = new SalesCustomerReturnLineView(1, Guid.Parse("6a210000-0000-4000-8000-000000000006"), int.MaxValue, Guid.Parse("6a210000-0000-4000-8000-000000000007"),
            Guid.Parse("6a210000-0000-4000-8000-000000000008"), Guid.Parse("6a210000-0000-4000-8000-000000000009"), "0.0000000000000000000000000001", "1.25", "20", "0.5", false, true, amounts, amounts);
        var v = new SalesCustomerReturnView(Id, "PendingApproval", "2", Guid.Parse("6a210000-0000-4000-8000-000000000010"), "SENTETIK-INV-10", line.ShipmentId,
            "SENTETIK-RETURN-10", new(2026,11,28), Guid.Parse("6a210000-0000-4000-8000-000000000011"), Guid.Parse("6a210000-0000-4000-8000-000000000012"),
            Guid.Parse("6a210000-0000-4000-8000-000000000013"), 2, "1.2345678901234567890123456789", Guid.Parse("6a210000-0000-4000-8000-000000000014"), 2,
            amounts, amounts, true, Maker.ToString("D"), At, Submitter.ToString("D"), At.AddMinutes(1), null, null, null, null, null, null, null, null, null, null, At.AddMinutes(1), [line]);
        return Seal(new(Company, v, true, true, "", ""));
    }
    internal static SalesCustomerReturnDecisionDetailV1 Seal(SalesCustomerReturnDecisionDetailV1 d)
    { d = d with { SourceFactsDigest = SalesCustomerReturnDecisionDigestsV1.SourceFactsDigest(d.CompanyId,d.CustomerReturn) }; return d with { PreviewDigest = SalesCustomerReturnDecisionDigestsV1.PreviewDigest(d) }; }
}
