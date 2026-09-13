using System.Text.Json;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Finance.UnitTests;

public sealed class PreparationTests
{
    // SENTETIK / NOT_ENV. Scale and currency below are test inputs, never production defaults.
    private static readonly Guid Company = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Currency = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid BankId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid PartyId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid AccountId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly ProfileReference Reference = new("77777777-7777-7777-7777-777777777777", "9007199254740993");
    private static ReceiptPreparation Preparation(string amount = "1000") => new(PartyId, AccountId,
        BankId, new(amount, Currency), Reference, new(2026, 9, 13), null, null, null);
    private static BankAccountView Bank => new(BankId, Company, Currency, "SENTETIK/NOT_ENV", true, true, "9007199254740993");
    private static PartyAccountEligibility Party => new(PartyId, AccountId, Company, true);
    private static MoneyProfileV1 Profile => new(Company, Currency, "SENTETIK", "SENTETIK-operation", new(Guid.Parse(Reference.ProfileId), 9007199254740993L), 3, true, new(2026, 1, 1), null);

    [Fact]
    public void LosslessAmountVersionAndDateRoundTrip()
    {
        var value = Preparation("900719925474099312345678901234567890.123");
        var json = JsonSerializer.Serialize(value, FinanceJson.CreateOptions());
        var restored = JsonSerializer.Deserialize<ReceiptPreparation>(json, FinanceJson.CreateOptions());
        Assert.Equal(value, restored);
        Assert.Contains("\"occurredOn\":\"2026-09-13\"", json);
        Assert.Null(restored!.ValueOn);
        Assert.Empty(PreparationValidator.Validate(value, Company, Bank, Party, Profile, "SENTETIK-operation"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1e3")]
    [InlineData("1,000")]
    [InlineData("NaN")]
    [InlineData("")]
    [InlineData(" 1")]
    [InlineData("0.000")]
    public void InvalidAmountRejected(string amount) => Assert.Contains(
        PreparationValidator.Validate(Preparation(amount), Company, Bank, Party, Profile, "SENTETIK-operation"),
        issue => issue.Code == "settings_decimal_invalid");

    [Fact]
    public void ExcessScaleRejectedWithoutRounding() => Assert.Contains(
        PreparationValidator.Validate(Preparation("1.1234"), Company, Bank, Party, Profile, "SENTETIK-operation"),
        issue => issue.Code == "settings_decimal_scale_exceeded");

    [Fact]
    public void UnavailableProfileRejected() => Assert.Contains(
        PreparationValidator.Validate(Preparation(), Company, Bank, Party, null, "SENTETIK-operation"),
        issue => issue.Code == "settings_provider_unavailable");

    [Fact]
    public void ForeignCompanyBankAndPartyRejected()
    {
        var issues = PreparationValidator.Validate(Preparation(), Guid.NewGuid(), Bank, Party, Profile, "SENTETIK-operation");
        Assert.Contains(issues, i => i.Code == "bank_account_ineligible");
        Assert.Contains(issues, i => i.Code == "party_account_ineligible");
    }

    [Fact]
    public void CurrencyMismatchAndInactiveBankRejected() => Assert.Contains(
        PreparationValidator.Validate(Preparation(), Company, Bank with { CurrencyId = Guid.NewGuid(), IsActive = false }, Party, Profile, "SENTETIK-operation"),
        i => i.Code == "bank_account_ineligible");

    [Fact]
    public void NumericAmountAndTimestampRejected()
    {
        var json = JsonSerializer.Serialize(Preparation(), FinanceJson.CreateOptions());
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReceiptPreparation>(
            json.Replace("\"amount\":\"1000\"", "\"amount\":1000"), FinanceJson.CreateOptions()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReceiptPreparation>(
            json.Replace("2026-09-13", "2026-09-13T00:00:00Z"), FinanceJson.CreateOptions()));
    }

    [Fact]
    public void MissingRequiredFieldAndClientCompanyRejected()
    {
        var json = JsonSerializer.Serialize(Preparation(), FinanceJson.CreateOptions());
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReceiptPreparation>(
            json.Replace("\"occurredOn\":\"2026-09-13\",", ""), FinanceJson.CreateOptions()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReceiptPreparation>(
            json.Insert(1, "\"companyId\":\"" + Company + "\","), FinanceJson.CreateOptions()));
    }

    [Fact]
    public void DraftCreateAndUpdateVersionAreDistinct()
    {
        Assert.True(PreparationValidator.IsValidDraftIdentity(new(null, null, Preparation())));
        Assert.False(PreparationValidator.IsValidDraftIdentity(new(null, "1", Preparation())));
        Assert.False(PreparationValidator.IsValidDraftIdentity(new(Guid.NewGuid(), null, Preparation())));
        Assert.True(PreparationValidator.IsValidDraftIdentity(new(Guid.NewGuid(), "9007199254740993", Preparation())));
    }

    [Fact]
    public void UnavailableAllocationSerializesWithoutZero()
    {
        var value = new UnallocatedView(AllocationKnowledge.Unavailable, new(AmountDisclosure.Unavailable, null));
        var json = JsonSerializer.Serialize(value, FinanceJson.CreateOptions());
        Assert.Contains("\"money\":null", json);
        Assert.DoesNotContain("\"amount\":\"0\"", json);
    }

    [Fact]
    public void ReverseRequiresOriginalVersionAndReason()
    {
        Assert.False(PreparationValidator.IsValidReverse(new(Guid.NewGuid(), "", new(2026, 9, 13), "reason")));
        Assert.False(PreparationValidator.IsValidReverse(new(Guid.NewGuid(), "1", new(2026, 9, 13), "")));
        Assert.True(PreparationValidator.IsValidReverse(new(Guid.NewGuid(), "9007199254740993", new(2026, 9, 13), "SENTETIK correction")));
    }
}
