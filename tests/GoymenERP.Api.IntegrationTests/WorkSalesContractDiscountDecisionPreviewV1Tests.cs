using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.SalesContracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class WorkSalesContractDiscountDecisionPreviewV1Tests
{
    internal static readonly Guid Company = Guid.Parse("d5110000-0000-4000-8000-000000000001"), Id = Guid.Parse("d5110000-0000-4000-8000-000000000002"),
        Party = Guid.Parse("d5110000-0000-4000-8000-000000000003"), Account = Guid.Parse("d5110000-0000-4000-8000-000000000004"),
        Currency = Guid.Parse("d5110000-0000-4000-8000-000000000005"), Profile = Guid.Parse("d5110000-0000-4000-8000-000000000006"),
        Actor = Guid.Parse("d5110000-0000-4000-8000-000000000007"), Maker = Guid.Parse("d5110000-0000-4000-8000-000000000008");
    internal static readonly DateTimeOffset At = new DateTimeOffset(2026, 11, 28, 10, 0, 0, TimeSpan.Zero).AddTicks(1);
    internal static readonly DateOnly Today = new(2026, 11, 28);
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [Theory] [InlineData(1L)] [InlineData(9007199254740993L)] [InlineData(long.MaxValue)]
    public void Work_four_human_fields_keep_native_twenty_four_full_collections_and_exact_text_version(long version)
    {
        var d = Facts(version); var view = WorkSalesContractDiscountDecisionPreviewV1.FromNative(d);
        Assert.True(SalesContractDiscountDecisionDigestsV1.Same(d.Contract, view.Contract)); Assert.Equal(2, view.Contract.Installments.Count); Assert.Equal(2, view.Contract.DiscountRules.Count);
        Assert.Equal(("3000.25", "5", "7.25", "3"), (view.Contract.PlannedTotalAmount, view.Contract.DiscountRules[0].RatePercent, view.Contract.DiscountRules[1].RatePercent, view.Contract.DiscountLimitPercent));
        Assert.Equal("  SENTETIK sözleşme maddeleri  ", view.Contract.Clauses); Assert.Equal(At, view.Contract.CreatedAtUtc); Assert.Equal(new DateOnly(2026, 11, 1), view.Contract.ValidFrom); Assert.Null(view.Contract.ValidThrough);
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(view, Json)); var root = wire.RootElement; Assert.Equal(4, root.EnumerateObject().Count());
        Assert.True(root.TryGetProperty("contract", out var contract)); Assert.Equal(24, contract.EnumerateObject().Count()); Assert.True(contract.TryGetProperty("version", out var v));
        Assert.Equal(JsonValueKind.String, v.ValueKind); Assert.Equal(version.ToString(CultureInfo.InvariantCulture), v.GetString());
        Assert.Equal(version < long.MaxValue, view.ApprovalSupported); Assert.Equal(view.ApprovalSupported, view.RejectionSupported);
        foreach (var hidden in new[] { "sourceFactsDigest", "previewDigest", "originalDecisionReceipt", "auditIntegrityHash", "payloadHash", "moneyProfileId", "salesContractDiscountProof" })
            Assert.DoesNotContain(hidden, root.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void Withheld_contract_keeps_labels_dates_and_all_lines_but_no_amount_rate_or_limit()
    {
        var d = Facts(disclosure: SalesPriceDisclosure.Withheld); var human = WorkSalesContractDiscountDecisionPreviewV1.FromNative(d);
        Assert.Equal(SalesPriceDisclosure.Withheld, human.Contract.AmountDisclosure); Assert.Null(human.Contract.PlannedTotalAmount); Assert.Null(human.Contract.DiscountLimitPercent);
        Assert.All(human.Contract.Installments, x => Assert.Null(x.Amount)); Assert.All(human.Contract.DiscountRules, x => Assert.Null(x.RatePercent));
        Assert.Equal(["SENTETIK satır iskontosu", "SENTETIK toplam iskontosu"], human.Contract.DiscountRules.Select(x => x.Label)); Assert.True(human.Contract.DiscountLimitDefined);
    }
    [Theory] [InlineData("null")] [InlineData("contract")] [InlineData("installments")] [InlineData("rules")] [InlineData("null-installment")]
    [InlineData("null-rule")] [InlineData("source")] [InlineData("null-source")] [InlineData("preview")] [InlineData("flags")] [InlineData("price")] [InlineData("version")]
    public void Malformed_human_boundary_is_503_before_hashing_null_native_collections(string fault)
    {
        var d = Facts(); d = fault switch
        {
            "contract" => d with { Contract = null! }, "installments" => d with { Contract = d.Contract with { Installments = null! } },
            "rules" => d with { Contract = d.Contract with { DiscountRules = null! } }, "null-installment" => d with { Contract = d.Contract with { Installments = [null!] } },
            "null-rule" => d with { Contract = d.Contract with { DiscountRules = [null!] } }, "source" => d with { SourceFactsDigest = new('a', 64) },
            "null-source" => d with { Source = null }, "preview" => d with { PreviewDigest = new('b', 64) }, "flags" => d with { RejectionSupported = false },
            "price" => d with { Contract = d.Contract with { PlannedTotalAmount = "1" } }, "version" => d with { Contract = d.Contract with { Version = "01" } }, _ => null!
        };
        Assert.Equal(503, Assert.Throws<WorkDecisionException>(() => WorkSalesContractDiscountDecisionPreviewV1.FromNative(d)).Status);
    }
    [Fact]
    public void Native_contract_result_remains_three_fields_and_Work_body_remains_four_with_no_private_target_or_prices()
    {
        var d = Facts(); var body = new WorkDecisionRequestV1("1", false, "SENTETIK ret"); var options = new JsonSerializerOptions(Json) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true };
        var text = JsonSerializer.Serialize(body, options); using var wire = JsonDocument.Parse(text); Assert.Equal(4, wire.RootElement.EnumerateObject().Count()); Assert.Equal(body, JsonSerializer.Deserialize<WorkDecisionRequestV1>(text, options));
        foreach (var field in new[] { "contractNo", "companyId", "actorId", "amountDisclosure", "sourceFactsDigest", "salesContractDiscountProof" })
        { var node = System.Text.Json.Nodes.JsonNode.Parse(text)!.AsObject(); node[field] = "SENTETIK"; Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WorkDecisionRequestV1>(node.ToJsonString(), options)); }
        using var native = JsonDocument.Parse(JsonSerializer.Serialize(new SalesContractCommandResult(d.Contract, false, "SENTETIK original"), SalesDraftJson.Options));
        Assert.Equal(3, native.RootElement.EnumerateObject().Count()); Assert.True(native.RootElement.TryGetProperty("contract", out var contract)); Assert.Equal(24, contract.EnumerateObject().Count());
    }
    internal static SalesContractDiscountDecisionDetailV1 Facts(long version = 1, SalesPriceDisclosure disclosure = SalesPriceDisclosure.Visible)
    {
        var c = SalesContract.Create(Id, Company, 11, Party, Account,
            new MoneyProfileSnapshot(Company, Currency, SalesOrderDraft.MoneyOperation, Profile, "3", Today, true, new DateOnly(2026, 1, 1), null, 2),
            new DateOnly(2026, 11, 1), null, [new(new DateOnly(2026, 12, 1), 1500m), new(new DateOnly(2027, 1, 1), 1500.25m)],
            [new(SalesContractDiscountKind.Line, "SENTETIK satır iskontosu", 5m), new(SalesContractDiscountKind.Total, "SENTETIK toplam iskontosu", 7.25m)],
            "  SENTETIK sözleşme maddeleri  ", 3m, Maker.ToString("D"), At);
        if (version != 1) c = SalesContract.Restore(c.Id, c.CompanyId, c.ContractNo, c.PartyId, c.CompanyAccountId, c.CurrencyId, c.MoneyProfileId, c.MoneyProfileVersion, c.MoneyScale,
            c.ValidFrom, c.ValidThrough, c.Installments, c.DiscountRules, c.Clauses, c.DiscountLimitPercent, c.State, c.CreatedBy, c.CreatedAtUtc, null, version, c.UpdatedAtUtc);
        return Seal(new(Company, c), disclosure: disclosure);
    }
    internal static SalesContractDiscountDecisionDetailV1 Seal(SalesContractDiscountDecisionSourceV1 source, DateOnly? today = null, SalesPriceDisclosure disclosure = SalesPriceDisclosure.Visible) =>
        SalesContractService.ProjectContractDiscountDecision(source, today ?? Today, disclosure);
}
