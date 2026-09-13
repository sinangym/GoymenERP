using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Finance.UnitTests;

public sealed class SettingsMetadataTests
{
    // SENTETIK/NOT_ENV. Metadata integrity tests do not approve any financial profile.
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Currency = Guid.NewGuid();
    private static readonly Guid BankId = Guid.NewGuid();
    private static readonly Guid PartyId = Guid.NewGuid();
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly Guid ProfileId = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 9, 13);
    private const string Operation = "SENTETIK-operation";

    [Theory]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("zero")]
    [InlineData("negative")]
    public void InvalidMoneyMetadataRejectedEvenWhenRequestMatches(string defect)
    {
        var metadata = Metadata(defect);
        var profile = MoneyProfile() with { Profile = metadata! };
        var request = Preparation() with
        {
            Profile = metadata is null ? null! : new(metadata.ProfileId.ToString("D"),
                metadata.Version.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
        var issues = Validate(request, profile);
        Assert.Contains(issues, issue => issue.Field == "profile" && issue.Code == "settings_profile_invalid");
    }

    [Fact]
    public void NullRequestMetadataFailsClosed()
    {
        var issues = Validate(Preparation() with { Profile = null! }, MoneyProfile());
        Assert.Contains(issues, issue => issue.Code == "profile_mismatch");
    }

    [Fact]
    public void ValidMoneyMetadataPreservesPositivePath() => Assert.Empty(Validate(Preparation(), MoneyProfile()));

    [Theory]
    [InlineData("null-response")]
    [InlineData("empty-period")]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("zero")]
    [InlineData("negative")]
    public async Task InvalidPeriodMetadataCannotReturnEligibility(string defect)
    {
        PeriodEligibilityV1? period = Period();
        period = defect switch
        {
            "null-response" => null,
            "empty-period" => period with { PeriodId = Guid.Empty },
            _ => period with { Policy = Metadata(defect)! }
        };
        var adapter = new SettingsReceiptPrerequisites(new Provider(period));
        var exception = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() =>
            adapter.RequirePeriodAsync(new(Company, "SENTETIK", "NOT_ENV", null), Operation, Day, null));
        Assert.Equal("finance_period_metadata_invalid", exception.Code);
    }

    [Fact]
    public async Task ValidPeriodMetadataPreservesPositivePath()
    {
        var period = Period();
        var actual = await new SettingsReceiptPrerequisites(new Provider(period))
            .RequirePeriodAsync(new(Company, "SENTETIK", "NOT_ENV", null), Operation, Day, null);
        Assert.Same(period, actual);
    }

    private static ProfileVersionV1? Metadata(string defect) => defect switch
    {
        "null" => null,
        "empty" => new(Guid.Empty, 1),
        "zero" => new(ProfileId, 0),
        "negative" => new(ProfileId, -1),
        _ => throw new ArgumentOutOfRangeException(nameof(defect))
    };

    private static ReceiptPreparation Preparation() => new(PartyId, AccountId, BankId,
        new("1000", Currency), new(ProfileId.ToString("D"), "1"), Day, null, null, null);
    private static MoneyProfileV1 MoneyProfile() => new(Company, Currency, "SENTETIK", Operation,
        new(ProfileId, 1), 0, true, Day, null);
    private static PeriodEligibilityV1 Period() => new(Company, Guid.NewGuid(), new(ProfileId, 1),
        Operation, Day, null, true, null);
    private static IReadOnlyList<ValidationIssue> Validate(ReceiptPreparation request, MoneyProfileV1 profile) =>
        PreparationValidator.Validate(request, Company,
            new(BankId, Company, Currency, "SENTETIK", true, true, "1"),
            new(PartyId, AccountId, Company, true), profile, Operation);

    private sealed class Provider(PeriodEligibilityV1? period) : ISettingsPrerequisiteProvider
    {
        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn,
            DateOnly? valueOn, CancellationToken cancellationToken = default) => Task.FromResult(period!);
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
