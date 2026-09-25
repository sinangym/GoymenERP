using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

public sealed class DbsCoverageCalculatorTests
{
    private static readonly Guid CompanyId = Guid.Parse("10000000-0000-0000-0000-000000000008");
    private static readonly Guid CurrencyId = Guid.Parse("20000000-0000-0000-0000-000000000008");

    [Fact]
    public void Assignment_reduces_unsecured_risk_and_available_dbs_without_collecting_cash()
    {
        var result = DbsCoverageCalculator.Calculate(Input(
            totalOpen: "1000.00",
            securedBefore: "200.00",
            availableBefore: "600.00",
            assignment: "300.00"));

        Assert.Empty(result.Issues);
        var projection = Assert.IsType<DbsCoverageProjectionV1>(result.Projection);
        Assert.Equal("1000.00", projection.TotalOpen.Amount);
        Assert.Equal("500.00", projection.DbsSecuredAfter.Amount);
        Assert.Equal("500.00", projection.UnsecuredAfter.Amount);
        Assert.Equal("300.00", projection.AvailableDbsAfter.Amount);
        Assert.Equal("0.00", projection.CashCollected.Amount);
        Assert.False(projection.CreatesMoneyEvent);
    }

    [Fact]
    public void Assignment_above_unsecured_or_available_amount_is_refused_instead_of_clamped()
    {
        var aboveUnsecured = DbsCoverageCalculator.Calculate(Input(
            totalOpen: "100.00", securedBefore: "90.00", availableBefore: "100.00", assignment: "11.00"));
        var aboveAvailable = DbsCoverageCalculator.Calculate(Input(
            totalOpen: "100.00", securedBefore: "0.00", availableBefore: "10.00", assignment: "11.00"));

        Assert.Null(aboveUnsecured.Projection);
        Assert.Contains(aboveUnsecured.Issues, issue => issue.Code == "finance_dbs_assignment_exceeds_unsecured");
        Assert.Null(aboveAvailable.Projection);
        Assert.Contains(aboveAvailable.Issues, issue => issue.Code == "finance_dbs_assignment_exceeds_available");
    }

    [Fact]
    public void Currency_mismatch_is_refused()
    {
        var input = Input("100.00", "0.00", "100.00", "10.00") with
        {
            Assignment = new MoneyText("10.00", Guid.NewGuid()),
        };

        var result = DbsCoverageCalculator.Calculate(input);

        Assert.Null(result.Projection);
        Assert.Contains(result.Issues, issue => issue.Code == "finance_dbs_currency_mismatch");
    }

    [Theory]
    [InlineData("0.001")]
    [InlineData("-1.00")]
    [InlineData("+1.00")]
    [InlineData("01.00")]
    [InlineData("1,00")]
    public void Non_lossless_or_out_of_scale_assignment_is_refused(string assignment)
    {
        var result = DbsCoverageCalculator.Calculate(Input("100.00", "0.00", "100.00", assignment));

        Assert.Null(result.Projection);
        Assert.Contains(result.Issues, issue => issue.Code == "finance_dbs_amount_invalid");
    }

    [Theory]
    [InlineData("not-a-guid", "1")]
    [InlineData("00000000-0000-0000-0000-000000000000", "1")]
    [InlineData("10000000-0000-0000-0000-000000000001", "0")]
    [InlineData("10000000-0000-0000-0000-000000000001", "v1")]
    public void Noncanonical_profile_reference_is_refused(string profileId, string version)
    {
        var input = Input("100.00", "0.00", "100.00", "10.00") with
        {
            Profile = new ProfileReference(profileId, version),
        };

        var result = DbsCoverageCalculator.Calculate(input);

        Assert.Null(result.Projection);
        Assert.Contains(result.Issues, issue => issue.Code == "finance_dbs_profile_invalid");
    }

    private static DbsCoverageProjectionInputV1 Input(
        string totalOpen,
        string securedBefore,
        string availableBefore,
        string assignment) => new(
            CompanyId,
            new MoneyText(totalOpen, CurrencyId),
            new MoneyText(securedBefore, CurrencyId),
            new MoneyText(availableBefore, CurrencyId),
            new MoneyText(assignment, CurrencyId),
            new ProfileReference(Guid.NewGuid().ToString("D"), "1"),
            Scale: 2);
}
