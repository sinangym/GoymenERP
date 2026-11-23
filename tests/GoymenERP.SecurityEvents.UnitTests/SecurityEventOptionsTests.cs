using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Writing;
using Microsoft.Extensions.Configuration;
using static GoymenERP.SecurityEvents.UnitTests.SecurityEventFixtures;

namespace GoymenERP.SecurityEvents.UnitTests;

public sealed class SecurityEventOptionsTests
{
    [Fact]
    public void Retention_resolves_years_and_policy_ref_from_configuration()
    {
        var resolver = SecurityEventRetentionResolver.FromConfiguration(Configuration(RetentionSettings()));

        Assert.Equal((OccurredAt.AddYears(2), "CFG-No134"), resolver.Resolve(SecurityEventTypesV1.AuthorizationDenied, OccurredAt));
        Assert.Equal((OccurredAt.AddYears(1), "CFG-No157"), resolver.Resolve(SecurityEventTypesV1.AiToolCalled, OccurredAt));
        Assert.Equal((OccurredAt.AddYears(1), "CFG-No157"), resolver.Resolve(SecurityEventTypesV1.AiRequestRefused, OccurredAt));
        Assert.Equal((OccurredAt.AddYears(2), "CFG-E9"), resolver.Resolve(SecurityEventTypesV1.AiAuthorizationDenied, OccurredAt));
    }

    [Theory]
    [InlineData(SecurityEventRetentionClassesV1.No134)]
    [InlineData(SecurityEventRetentionClassesV1.No157)]
    [InlineData(SecurityEventRetentionClassesV1.AiAuthorizationDenied)]
    public void Retention_with_a_missing_class_fails_closed(string missingClass)
    {
        var settings = RetentionSettings()
            .Where(pair => !pair.Key.StartsWith($"{SecurityEventRetentionResolver.SectionName}:{missingClass}:", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        Assert.Throws<InvalidOperationException>(() => SecurityEventRetentionResolver.FromConfiguration(Configuration(settings)));
    }

    [Theory]
    [InlineData("Years", "0")]
    [InlineData("Years", "-1")]
    [InlineData("PolicyRef", "")]
    [InlineData("PolicyRef", " ")]
    public void Retention_with_an_invalid_class_value_fails_closed(string key, string value)
    {
        var settings = RetentionSettings();
        settings[$"{SecurityEventRetentionResolver.SectionName}:{SecurityEventRetentionClassesV1.No134}:{key}"] = value;

        Assert.Throws<InvalidOperationException>(() => SecurityEventRetentionResolver.FromConfiguration(Configuration(settings)));
    }

    [Fact]
    public void Retention_with_an_overlong_policy_ref_fails_closed()
    {
        var classes = RetentionClasses();
        classes[SecurityEventRetentionClassesV1.No157].PolicyRef = new string('x', 65);

        Assert.Throws<InvalidOperationException>(() => new SecurityEventRetentionResolver(classes));
    }

    [Fact]
    public void Retention_with_an_unknown_class_fails_closed()
    {
        var settings = RetentionSettings();
        settings[$"{SecurityEventRetentionResolver.SectionName}:Forever:Years"] = "99";
        settings[$"{SecurityEventRetentionResolver.SectionName}:Forever:PolicyRef"] = "X";

        Assert.Throws<InvalidOperationException>(() => SecurityEventRetentionResolver.FromConfiguration(Configuration(settings)));
    }

    [Fact]
    public void Retention_with_no_section_fails_closed()
    {
        Assert.Throws<InvalidOperationException>(() => SecurityEventRetentionResolver.FromConfiguration(Configuration([])));
    }

    [Fact]
    public void Aggregation_unset_is_disabled_and_valid()
    {
        var options = SecurityEventAggregationOptions.FromConfiguration(Configuration([]));

        Assert.False(options.IsEnabled);
        Assert.Null(options.Window);
        Assert.Null(options.Threshold);
    }

    [Fact]
    public void Aggregation_with_window_and_threshold_is_enabled()
    {
        var options = SecurityEventAggregationOptions.FromConfiguration(Configuration(new()
        {
            [$"{SecurityEventAggregationOptions.SectionName}:Window"] = "00:05:00",
            [$"{SecurityEventAggregationOptions.SectionName}:Threshold"] = "10",
        }));

        Assert.True(options.IsEnabled);
        Assert.Equal(TimeSpan.FromMinutes(5), options.Window);
        Assert.Equal(10, options.Threshold);
    }

    [Theory]
    [InlineData("Window", "00:05:00")]
    [InlineData("Threshold", "10")]
    public void Aggregation_with_only_one_value_fails_closed(string key, string value)
    {
        var configuration = Configuration(new() { [$"{SecurityEventAggregationOptions.SectionName}:{key}"] = value });

        Assert.Throws<InvalidOperationException>(() => SecurityEventAggregationOptions.FromConfiguration(configuration));
    }

    [Theory]
    [InlineData("00:00:00", "10")]
    [InlineData("-00:01:00", "10")]
    [InlineData("00:05:00", "0")]
    [InlineData("00:05:00", "-3")]
    public void Aggregation_with_non_positive_values_fails_closed(string window, string threshold)
    {
        var configuration = Configuration(new()
        {
            [$"{SecurityEventAggregationOptions.SectionName}:Window"] = window,
            [$"{SecurityEventAggregationOptions.SectionName}:Threshold"] = threshold,
        });

        Assert.Throws<InvalidOperationException>(() => SecurityEventAggregationOptions.FromConfiguration(configuration));
    }

    [Theory]
    [InlineData("bes-dakika", "10")]
    [InlineData("00:05:00", "on")]
    [InlineData("00:05:00", "10.5")]
    public void Aggregation_with_unparsable_values_fails_closed(string window, string threshold)
    {
        var configuration = Configuration(new()
        {
            [$"{SecurityEventAggregationOptions.SectionName}:Window"] = window,
            [$"{SecurityEventAggregationOptions.SectionName}:Threshold"] = threshold,
        });

        Assert.Throws<InvalidOperationException>(() => SecurityEventAggregationOptions.FromConfiguration(configuration));
    }

    private static Dictionary<string, string?> RetentionSettings() => new(StringComparer.Ordinal)
    {
        [$"{SecurityEventRetentionResolver.SectionName}:{SecurityEventRetentionClassesV1.No134}:Years"] = "2",
        [$"{SecurityEventRetentionResolver.SectionName}:{SecurityEventRetentionClassesV1.No134}:PolicyRef"] = "CFG-No134",
        [$"{SecurityEventRetentionResolver.SectionName}:{SecurityEventRetentionClassesV1.No157}:Years"] = "1",
        [$"{SecurityEventRetentionResolver.SectionName}:{SecurityEventRetentionClassesV1.No157}:PolicyRef"] = "CFG-No157",
        [$"{SecurityEventRetentionResolver.SectionName}:{SecurityEventRetentionClassesV1.AiAuthorizationDenied}:Years"] = "2",
        [$"{SecurityEventRetentionResolver.SectionName}:{SecurityEventRetentionClassesV1.AiAuthorizationDenied}:PolicyRef"] = "CFG-E9",
    };

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
