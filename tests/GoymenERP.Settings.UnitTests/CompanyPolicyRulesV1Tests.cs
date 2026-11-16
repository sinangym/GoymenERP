using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>
/// B-13: the company policy registry value rules only check the technical shape — whole working days 0 or more for the
/// due-date buffer, and the tax master's default shape for the SAT-D381/ALS-D105 subcontract tax default. The numbers below
/// are rule inputs, not tax rates or buffers of any company.
/// </summary>
public sealed class CompanyPolicyRulesV1Tests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(30)]
    public void A_due_date_buffer_of_zero_or_more_whole_working_days_is_valid(int days)
    {
        Assert.True(CompanyPolicyRulesV1.IsValidDueDateBuffer(days));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void A_negative_due_date_buffer_is_refused(int days)
    {
        Assert.False(CompanyPolicyRulesV1.IsValidDueDateBuffer(days));
    }

    [Fact]
    public void The_due_date_buffer_start_value_is_zero_and_the_keys_are_distinct_per_side()
    {
        Assert.Equal(0, CompanyPolicyInputLimitsV1.DefaultDueDateBufferWorkingDays);
        Assert.Equal(CompanyPolicyKeysV1.All.Count, CompanyPolicyKeysV1.All.Distinct(StringComparer.Ordinal).Count());
        Assert.NotEqual(CompanyPolicyKeysV1.SalesSubcontractTaxDefault, CompanyPolicyKeysV1.PurchasingSubcontractTaxDefault);
        Assert.All(CompanyPolicyKeysV1.All, key => Assert.InRange(key.Length, 1, CompanyPolicyKeysV1.MaximumLength));
    }

    [Fact]
    public void A_VAT_with_withholding_and_a_code_is_read_and_has_the_stored_shape()
    {
        Assert.True(CompanyPolicyRulesV1.TryReadTaxDefault(true, "20", null, true, "50", "624", out var value, out var detail));
        Assert.Equal(string.Empty, detail);
        Assert.Equal(new SettingsSubcontractTaxDefaultV1(true, 20m, null, true, 50m, "624"), value);
        Assert.True(CompanyPolicyRulesV1.IsValidTaxDefault(value));
    }

    [Fact]
    public void No_VAT_with_an_exemption_code_and_no_withholding_is_read()
    {
        Assert.True(CompanyPolicyRulesV1.TryReadTaxDefault(false, null, "301", false, null, null, out var value, out _));
        Assert.Equal(new SettingsSubcontractTaxDefaultV1(false, null, "301", false, null, null), value);
        Assert.True(CompanyPolicyRulesV1.IsValidTaxDefault(value));
    }

    [Theory]
    [InlineData(true, "0", null, false, null, null)]
    [InlineData(true, null, null, false, null, null)]
    [InlineData(true, "101", null, false, null, null)]
    [InlineData(true, "20", "301", false, null, null)]
    [InlineData(false, "20", null, false, null, null)]
    [InlineData(false, null, null, true, "50", null)]
    [InlineData(false, null, "abc-1", false, null, null)]
    [InlineData(true, "20", null, true, "0", null)]
    [InlineData(true, "20", null, false, "50", null)]
    [InlineData(true, "20", null, false, null, "624")]
    [InlineData(true, "20", null, true, "50", "bad code")]
    public void A_shape_the_tax_master_would_refuse_is_refused_with_a_detail(bool vatApplies, string? vatPercent,
        string? exemptionCode, bool withholdingApplies, string? withholdingPercent, string? withholdingCode)
    {
        Assert.False(CompanyPolicyRulesV1.TryReadTaxDefault(vatApplies, vatPercent, exemptionCode, withholdingApplies,
            withholdingPercent, withholdingCode, out _, out var detail));
        Assert.False(string.IsNullOrWhiteSpace(detail));
    }

    [Fact]
    public void A_stored_value_outside_the_shape_is_not_valid()
    {
        Assert.False(CompanyPolicyRulesV1.IsValidTaxDefault(new SettingsSubcontractTaxDefaultV1(true, 0m, null, false, null, null)));
        Assert.False(CompanyPolicyRulesV1.IsValidTaxDefault(new SettingsSubcontractTaxDefaultV1(true, 20m, "301", false, null, null)));
        Assert.False(CompanyPolicyRulesV1.IsValidTaxDefault(new SettingsSubcontractTaxDefaultV1(false, null, null, true, 50m, null)));
        Assert.False(CompanyPolicyRulesV1.IsValidTaxDefault(new SettingsSubcontractTaxDefaultV1(true, 20m, null, false, 50m, null)));
        Assert.False(CompanyPolicyRulesV1.IsValidTaxDefault(new SettingsSubcontractTaxDefaultV1(false, null, "ab", false, null, null)));
    }

    [Fact]
    public void The_finance_policy_version_text_changes_with_every_version_and_only_open_is_open()
    {
        var companyId = Guid.NewGuid();
        var opened = new SettingsFinancePolicyV1(companyId, FinanceOpeningPeriodStateV1.Open, 1);
        var closed = new SettingsFinancePolicyV1(companyId, FinanceOpeningPeriodStateV1.Closed, 2);
        var notRecorded = new SettingsFinancePolicyV1(companyId, FinanceOpeningPeriodStateV1.NotRecorded, 0);

        Assert.True(opened.OpeningPeriodOpen);
        Assert.False(closed.OpeningPeriodOpen);
        Assert.False(notRecorded.OpeningPeriodOpen);
        Assert.Equal("finance.opening-period:1", opened.VersionText);
        Assert.Equal("finance.opening-period:2", closed.VersionText);
        Assert.NotEqual(opened.VersionText, closed.VersionText);
    }
}
