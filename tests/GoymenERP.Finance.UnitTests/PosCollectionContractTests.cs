using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// DEC-FIN-014 (customer POS collection) on the contract: gross closes the customer's debt, the bank net and the
/// commission are separate exact facts, and there is no supplier POS. SENTETIK / NOT_ENV test inputs only; the
/// scale-2 profile below is a test input, not a production scale.
/// </summary>
public sealed class PosCollectionContractTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Currency = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid BankId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid PartyId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid AccountId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly ProfileReference Reference = new("77777777-7777-7777-7777-777777777777", "1");
    private const string Operation = "SENTETIK-operation";

    [Theory]
    [InlineData("1000.00", "25.50", "974.50")]
    [InlineData("1000", "25.5", "974.5")]
    [InlineData("1000", "0", "1000")]
    [InlineData("1000.5", "0.25", "1000.25")]
    [InlineData("0.10", "0.09", "0.01")]
    [InlineData("900719925474099312345678901234567890.12", "0.01", "900719925474099312345678901234567890.11")]
    public void The_bank_net_is_the_exact_gross_minus_the_commission(string gross, string commission, string net)
    {
        Assert.True(PosCollectionRules.TryBankNet(gross, commission, out var bankNet));
        Assert.Equal(net, bankNet);
    }

    [Theory]
    [InlineData("1000", "1000")]
    [InlineData("1000", "1000.00")]
    [InlineData("1000", "1000.01")]
    [InlineData("1000", "-1")]
    [InlineData("1000", "1e2")]
    [InlineData("1000", "1,5")]
    [InlineData("1000", "")]
    [InlineData("1000", " 1")]
    public void No_net_exists_when_the_commission_is_not_a_decimal_below_the_gross(string gross, string commission) =>
        Assert.False(PosCollectionRules.TryBankNet(gross, commission, out _));

    [Fact]
    public void A_valid_POS_preparation_passes_the_same_validator_as_a_bank_transfer() =>
        Assert.Empty(Validate(Preparation("1000.00", new PosCollectionInput("25.50", PosCommissionBearer.Company))));

    [Theory]
    [InlineData("1000.00", PosCollectionRules.CommissionNotBelowGross)]
    [InlineData("1500", PosCollectionRules.CommissionNotBelowGross)]
    [InlineData("1.005", PosCollectionRules.ScaleExceeded)]
    [InlineData("-1", PosCollectionRules.CommissionInvalid)]
    [InlineData("1e1", PosCollectionRules.CommissionInvalid)]
    [InlineData("", PosCollectionRules.CommissionInvalid)]
    public void A_commission_that_is_malformed_too_precise_or_not_below_the_gross_is_refused(string commission, string code)
    {
        var issue = Assert.Single(Validate(Preparation("1000.00", new PosCollectionInput(commission, PosCommissionBearer.Customer))));
        Assert.Equal(new ValidationIssue(PosCollectionRules.CommissionField, code), issue);
    }

    [Fact]
    public void An_undefined_bearer_is_refused() => Assert.Contains(
        Validate(Preparation("1000", new PosCollectionInput("10", (PosCommissionBearer)7))),
        issue => issue == new ValidationIssue(PosCollectionRules.CommissionBearerField, PosCollectionRules.CommissionBearerInvalid));

    [Fact]
    public void The_gross_is_still_the_profile_checked_amount() => Assert.Contains(
        Validate(Preparation("1000.001", new PosCollectionInput("1", PosCommissionBearer.Company))),
        issue => issue == new ValidationIssue("money.amount", "settings_decimal_scale_exceeded"));

    [Fact]
    public void A_bank_transfer_keeps_the_exact_258_wire_shape_and_payload_hash()
    {
        var options = FinanceJson.CreateOptions();
        var transfer = new DraftSaveRequest(null, null, Preparation("1000", pos: null));
        var json = JsonSerializer.Serialize(transfer, options);
        Assert.DoesNotContain("\"pos\"", json, StringComparison.Ordinal);

        // A client that sends "pos": null explicitly means the same request, so it hashes the same.
        var explicitNull = json.Replace("\"description\":null}", "\"description\":null,\"pos\":null}", StringComparison.Ordinal);
        Assert.NotEqual(json, explicitNull);
        var restored = JsonSerializer.Deserialize<DraftSaveRequest>(explicitNull, options)!;
        Assert.Equal(Hash(json), Hash(JsonSerializer.Serialize(restored, options)));
    }

    [Fact]
    public void A_POS_preparation_round_trips_with_its_bearer_as_a_name_only()
    {
        var options = FinanceJson.CreateOptions();
        var value = Preparation("1000.00", new PosCollectionInput("25.50", PosCommissionBearer.Customer));
        var json = JsonSerializer.Serialize(value, options);
        Assert.Contains("\"pos\":{\"commissionAmount\":\"25.50\",\"commissionBearer\":\"Customer\"}", json, StringComparison.Ordinal);
        Assert.Equal(value, JsonSerializer.Deserialize<ReceiptPreparation>(json, options));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReceiptPreparation>(
            json.Replace("\"Customer\"", "2", StringComparison.Ordinal), options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReceiptPreparation>(
            json.Replace("\"commissionBearer\":\"Customer\"", "\"commissionBearer\":\"Customer\",\"bankNet\":\"974.50\"",
                StringComparison.Ordinal), options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReceiptPreparation>(
            json.Replace(",\"commissionBearer\":\"Customer\"", string.Empty, StringComparison.Ordinal), options));
    }

    [Fact]
    public void A_detail_without_POS_facts_and_a_stored_258_result_read_the_same()
    {
        var options = FinanceJson.CreateOptions();
        var summary = new SourceSummary(Guid.NewGuid(), Company, "n", "1", SourceStage.Draft, PartyId, AccountId, BankId,
            Currency, new DateOnly(2026, 9, 27), null, new AmountView(AmountDisclosure.Visible, new MoneyText("1", Currency)));
        var detail = new SourceDetail(summary, Reference, null, null, null, null, null,
            new UnallocatedView(AllocationKnowledge.Unavailable, new AmountView(AmountDisclosure.Unavailable, null)));
        var json = JsonSerializer.Serialize(detail, options);
        Assert.DoesNotContain("\"pos\"", json, StringComparison.Ordinal);
        Assert.Equal(detail, JsonSerializer.Deserialize<SourceDetail>(json, options));
    }

    [Fact]
    public void There_is_no_supplier_POS_the_outgoing_payment_contract_has_no_POS_member()
    {
        static bool CarriesPos(Type type) => type.GetProperties().Any(property =>
            property.PropertyType == typeof(PosCollectionInput) || property.PropertyType == typeof(PosCollectionView)
            || property.Name.Contains("Pos", StringComparison.Ordinal));

        Assert.False(CarriesPos(typeof(ManualBankPaymentPreparation)));
        Assert.False(CarriesPos(typeof(ManualBankPaymentDraftSaveRequest)));
        Assert.False(CarriesPos(typeof(ManualBankPaymentView)));
        Assert.True(CarriesPos(typeof(ReceiptPreparation)));
    }

    private static ReceiptPreparation Preparation(string amount, PosCollectionInput? pos) => new(PartyId, AccountId,
        BankId, new MoneyText(amount, Currency), Reference, new DateOnly(2026, 9, 27), null, null, null, pos);

    private static IReadOnlyList<ValidationIssue> Validate(ReceiptPreparation preparation) =>
        PreparationValidator.Validate(preparation, Company,
            new BankAccountView(BankId, Company, Currency, "SENTETIK", true, true, "1"),
            new PartyAccountEligibility(PartyId, AccountId, Company, true),
            new MoneyProfileV1(Company, Currency, "SENTETIK", Operation, new ProfileVersionV1(Guid.Parse(Reference.ProfileId), 1),
                2, true, new DateOnly(2026, 1, 1), null),
            Operation);

    private static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
}
