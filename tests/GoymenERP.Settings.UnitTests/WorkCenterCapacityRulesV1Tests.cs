using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>
/// URT-D89: the work center capacity input rules only check the technical shape — lossless decimal text greater than zero
/// inside the published column bounds, and a buffer pair that is either fully absent or a value with a published unit. The
/// numbers below are rule inputs, not capacity or buffer values of any work center.
/// </summary>
public sealed class WorkCenterCapacityRulesV1Tests
{
    [Theory]
    [InlineData("1", "1")]
    [InlineData("1250", "1250")]
    [InlineData("1250.5", "1250.5")]
    [InlineData("0.000001", "0.000001")]
    [InlineData("999999999999.999999", "999999999999.999999")]
    public void A_capacity_greater_than_zero_inside_the_column_is_read_exactly(string text, string canonical)
    {
        Assert.True(WorkCenterCapacityRulesV1.TryParseCapacity(text, out var value));
        Assert.Equal(canonical, WorkCenterCapacityRulesV1.Format(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("0.0")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1,5")]
    [InlineData("1.")]
    [InlineData(".5")]
    [InlineData("01")]
    [InlineData("1e3")]
    [InlineData("1.1234567")]
    [InlineData("1000000000000")]
    [InlineData("1.2.3")]
    public void Any_other_capacity_text_is_refused(string? text)
    {
        Assert.False(WorkCenterCapacityRulesV1.TryParseCapacity(text, out _));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1.5")]
    [InlineData("0.001")]
    [InlineData("999999.999")]
    public void A_buffer_greater_than_zero_inside_the_column_is_accepted(string text)
    {
        Assert.True(WorkCenterCapacityRulesV1.TryParseSafetyBuffer(text, out var value));
        Assert.Equal(text, WorkCenterCapacityRulesV1.Format(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("0.000")]
    [InlineData("-1")]
    [InlineData("1.0001")]
    [InlineData("1000000")]
    [InlineData("1,5")]
    public void Any_other_buffer_text_is_refused(string? text)
    {
        Assert.False(WorkCenterCapacityRulesV1.TryParseSafetyBuffer(text, out _));
    }

    [Fact]
    public void Both_buffer_fields_absent_is_no_buffer()
    {
        Assert.True(WorkCenterCapacityRulesV1.TryParseSafetyBufferPair(null, null, out var value, out var unit));
        Assert.Null(value);
        Assert.Null(unit);
    }

    [Theory]
    [InlineData("Hours")]
    [InlineData("Days")]
    public void A_buffer_value_with_a_published_unit_is_a_buffer(string unitText)
    {
        Assert.True(WorkCenterCapacityRulesV1.TryParseSafetyBufferPair("2.5", unitText, out var value, out var unit));
        Assert.Equal(2.5m, value);
        Assert.Equal(unitText, unit);
    }

    [Theory]
    [InlineData("2", null)]
    [InlineData(null, "Hours")]
    [InlineData("", "Hours")]
    [InlineData("2", "")]
    [InlineData("0", "Hours")]
    [InlineData("2", "hours")]
    [InlineData("2", " Days")]
    [InlineData("2", "Weeks")]
    public void A_half_or_malformed_buffer_pair_is_refused(string? valueText, string? unitText)
    {
        Assert.False(WorkCenterCapacityRulesV1.TryParseSafetyBufferPair(valueText, unitText, out var value, out var unit));
        Assert.Null(value);
        Assert.Null(unit);
    }

    [Fact]
    public void The_published_buffer_unit_list_is_closed_and_without_duplicates()
    {
        Assert.Equal(["Hours", "Days"], WorkCenterSafetyBufferUnitsV1.All);
        Assert.All(WorkCenterSafetyBufferUnitsV1.All, unit => Assert.True(WorkCenterCapacityRulesV1.IsSafetyBufferUnit(unit)));
        Assert.False(WorkCenterCapacityRulesV1.IsSafetyBufferUnit(null));
        Assert.Equal("kg/h", WorkCenterCapacityKeysV1.InputCapacityUnit);
    }
}
