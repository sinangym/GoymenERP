using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>
/// MOD-03 slice 1: the Settings-owned Sales policy registry. Every approved value asserted here is
/// traced to SAT-D2, SAT-D5 or SAT-D6 (<c>docs/decisions/SINAN_KARARLARI_2026-10-25_SATIS.md</c>) on
/// <see cref="ApprovedSalesPolicyCatalog"/>; synthetic entries are only used for catalog validation
/// and window behaviour.
/// </summary>
public sealed class ApprovedSalesPolicyProviderTests
{
    private static readonly Guid Gdc = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Gsp = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid Gin = Guid.Parse("10000000-0000-0000-0000-000000000003");
    // Not a seed company: since SAT-D14 GDC, GSP and GIN are all served, so only such a company is unserved.
    private static readonly Guid Unserved = Guid.Parse("71000000-0000-4000-8000-0000000000ee");
    private static readonly DateOnly ExtraApproval = new(2026, 10, 26);
    private static readonly Guid Try = Guid.Parse("9e60dcf0-f4bd-5bca-b464-f9a2db98e31a");
    private static readonly Guid SyntheticGroup = Guid.Parse("71000000-0000-4000-8000-000000000001");
    private static readonly Guid SyntheticProfile = Guid.Parse("71000000-0000-4000-8000-0000000000aa");
    private static readonly DateOnly Approval = new(2026, 10, 25);

    [Fact]
    public async Task GDC_delivery_tolerance_is_the_exact_SAT_D5_policy()
    {
        var policy = await Provider(Gdc).GetSalesDeliveryToleranceAsync(Approval);

        Assert.Equal(Gdc, policy.CompanyId);
        // UUIDv5(UUIDv5(URL, ".../settings/sales-policies"), "sales-delivery-tolerance:<GDC>:v1").
        Assert.Equal(new ProfileVersionV1(Guid.Parse("c088f8d5-26c4-5ed9-ae6f-f47e73a26b11"), 1), policy.Profile);
        Assert.Equal(1m, policy.WarnPercent);
        Assert.Equal(2m, policy.ApprovalPercent);
        Assert.Equal(3m, policy.BlockPercent);
        Assert.True(policy.IsActive);
        Assert.Equal(Approval, policy.EffectiveFrom);
        Assert.Null(policy.EffectiveThrough);
    }

    [Theory]
    [InlineData("2026-10-26")]
    [InlineData("2099-12-31")]
    public async Task The_open_ended_tolerance_answers_every_later_date(string date)
    {
        var policy = await Provider(Gdc).GetSalesDeliveryToleranceAsync(DateOnly.Parse(date));

        Assert.Equal(1, policy.Profile.Version);
        Assert.Null(policy.EffectiveThrough);
    }

    [Fact]
    public async Task A_date_before_approval_gets_the_real_window_so_the_consumer_rejects_it()
    {
        var dayBefore = Approval.AddDays(-1);

        var tolerance = await Provider(Gdc).GetSalesDeliveryToleranceAsync(dayBefore);
        var threshold = await Provider(Gdc).GetSalesApprovalAmountThresholdAsync(Try, dayBefore);

        Assert.Equal(Approval, tolerance.EffectiveFrom);
        Assert.True(dayBefore < tolerance.EffectiveFrom);
        Assert.Equal(Approval, threshold.EffectiveFrom);
    }

    [Fact]
    public async Task GDC_TRY_approval_threshold_is_defined_but_disabled_with_no_amount()
    {
        var policy = await Provider(Gdc).GetSalesApprovalAmountThresholdAsync(Try, Approval);

        Assert.Equal(Gdc, policy.CompanyId);
        Assert.Equal(Try, policy.CurrencyId);
        Assert.Equal(SettingsCurrencyIdentitiesV1.TurkishLira, policy.CurrencyId);
        Assert.Equal(new ProfileVersionV1(Guid.Parse("5c0ec7f4-2116-59c6-82fc-9c2a737a4342"), 1), policy.Profile);
        // SAT-D6: the key exists, starts disabled; no amount is invented.
        Assert.False(policy.IsEnabled);
        Assert.Null(policy.ThresholdAmountText);
        Assert.True(policy.IsActive);
        Assert.Equal(Approval, policy.EffectiveFrom);
        Assert.Null(policy.EffectiveThrough);
    }

    [Fact]
    public async Task A_currency_without_an_approved_threshold_is_not_found()
    {
        var currencyId = Guid.Parse("0f3bd0a1-6e2c-4d77-a3a5-0b1c2d3e4f51");

        var failure = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => Provider(Gdc).GetSalesApprovalAmountThresholdAsync(currencyId, Approval));

        Assert.Equal(SettingsPrerequisiteCodesV1.SalesPolicyNotFound, failure.Code);
    }

    [Theory]
    [InlineData("71000000-0000-4000-8000-000000000001")]
    [InlineData("0e9a3f0c-7d2b-4f1a-9c3e-5b6d7e8f9a0b")]
    public async Task Every_discount_limit_group_is_fail_closed_because_no_limit_is_approved(string group)
    {
        // SAT-D2: a group without a defined limit sends every discount to approval. The catalog is
        // empty on purpose; not-found is that answer, never "unlimited" or a zero limit.
        Assert.Empty(ApprovedSalesPolicyCatalog.Approved.DiscountLimits);

        var failure = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => Provider(Gdc).GetSalesDiscountLimitAsync(Guid.Parse(group), Approval));

        Assert.Equal(SettingsPrerequisiteCodesV1.SalesPolicyNotFound, failure.Code);
        Assert.Equal("settings_sales_policy_not_found", failure.Code);
    }

    [Theory]
    [InlineData("discount", "gdc")]
    [InlineData("threshold", "gdc")]
    [InlineData("discount", "unserved")]
    [InlineData("threshold", "unserved")]
    public async Task An_empty_identity_is_an_invalid_request_never_not_found(string read, string company)
    {
        var provider = Provider(company == "gdc" ? Gdc : Unserved);

        var failure = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => read == "discount"
            ? provider.GetSalesDiscountLimitAsync(Guid.Empty, Approval)
            : provider.GetSalesApprovalAmountThresholdAsync(Guid.Empty, Approval));

        Assert.Equal(SettingsPrerequisiteCodesV1.SalesPolicyRequestInvalid, failure.Code);
        Assert.Equal("settings_sales_policy_request_invalid", failure.Code);
    }

    [Theory]
    [InlineData("discount")]
    [InlineData("threshold")]
    public async Task An_empty_identity_is_still_refused_access_first(string read)
    {
        var provider = new ApprovedSettingsPrerequisiteProvider(new Context(Gdc, true), new Actor(true, "x", Gsp));

        var failure = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => read == "discount"
            ? provider.GetSalesDiscountLimitAsync(Guid.Empty, Approval)
            : provider.GetSalesApprovalAmountThresholdAsync(Guid.Empty, Approval));

        Assert.Equal("settings_access_denied", failure.Code);
    }

    [Fact]
    public async Task Synthetic_tolerance_versions_are_selected_by_date_and_a_closed_window_keeps_its_real_end()
    {
        var firstEnd = Approval.AddDays(30);
        var secondStart = firstEnd.AddDays(1);
        var secondEnd = secondStart.AddDays(60);
        var provider = Provider(Gdc, new ApprovedSalesPolicyCatalog(
            [
                Tolerance() with { EffectiveThrough = firstEnd },
                Tolerance() with
                {
                    Version = 2, ProfileId = Guid.Parse("71000000-0000-4000-8000-0000000000ac"),
                    WarnPercent = 0.5m, ApprovalPercent = 1.5m, BlockPercent = 2.5m,
                    EffectiveFrom = secondStart, EffectiveThrough = secondEnd,
                },
            ],
            [],
            []));

        var first = await provider.GetSalesDeliveryToleranceAsync(firstEnd);
        var second = await provider.GetSalesDeliveryToleranceAsync(secondStart);
        var afterClosed = await provider.GetSalesDeliveryToleranceAsync(secondEnd.AddDays(1));

        Assert.Equal((1L, 1m, 2m, 3m),
            (first.Profile.Version, first.WarnPercent, first.ApprovalPercent, first.BlockPercent));
        Assert.Equal((2L, 0.5m, 1.5m, 2.5m),
            (second.Profile.Version, second.WarnPercent, second.ApprovalPercent, second.BlockPercent));
        // Never widened: the last version comes back with its real end, so the consumer rejects it.
        Assert.Equal(2, afterClosed.Profile.Version);
        Assert.Equal(secondEnd, afterClosed.EffectiveThrough);
    }

    [Fact]
    public async Task Synthetic_threshold_versions_are_selected_by_date_and_a_closed_window_keeps_its_real_end()
    {
        var firstEnd = Approval.AddDays(10);
        var secondStart = firstEnd.AddDays(1);
        var secondEnd = secondStart.AddDays(10);
        var provider = Provider(Gdc, new ApprovedSalesPolicyCatalog(
            [],
            [],
            [
                Threshold() with { EffectiveThrough = firstEnd },
                Threshold() with
                {
                    Version = 2, ProfileId = Guid.Parse("71000000-0000-4000-8000-0000000000ad"),
                    IsEnabled = true, ThresholdAmountText = "0.50",
                    EffectiveFrom = secondStart, EffectiveThrough = secondEnd,
                },
            ]));

        var first = await provider.GetSalesApprovalAmountThresholdAsync(Try, firstEnd);
        var second = await provider.GetSalesApprovalAmountThresholdAsync(Try, secondStart);
        var afterClosed = await provider.GetSalesApprovalAmountThresholdAsync(Try, secondEnd.AddDays(1));

        Assert.Equal((1L, false, (string?)null), (first.Profile.Version, first.IsEnabled, first.ThresholdAmountText));
        Assert.Equal((2L, true, (string?)"0.50"), (second.Profile.Version, second.IsEnabled, second.ThresholdAmountText));
        Assert.Equal(2, afterClosed.Profile.Version);
        Assert.Equal(secondEnd, afterClosed.EffectiveThrough);
    }

    [Theory]
    [InlineData("tolerance")]
    [InlineData("discount")]
    [InlineData("threshold")]
    public async Task A_company_without_an_approved_sales_source_is_unavailable_not_given_GDC_s_policy(string read)
    {
        var failure = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => Read(Provider(Unserved), read));

        Assert.Equal("settings_provider_unavailable", failure.Code);
    }

    [Theory]
    [InlineData("gsp", "e5ba2cba-1f1e-5ba0-b801-7b66d21190af", "320e06e3-0408-51f1-92e6-f8905e7b674f")]
    [InlineData("gin", "c5fd5cfa-b37f-5464-a920-152eb96c8c84", "3cd01981-8511-5dc1-b21b-6c632425d915")]
    public async Task GSP_and_GIN_get_GDC_s_sales_policies_from_SAT_D14(
        string company, string toleranceProfile, string thresholdProfile)
    {
        var companyId = company == "gsp" ? Gsp : Gin;
        var provider = Provider(companyId);

        var tolerance = await provider.GetSalesDeliveryToleranceAsync(ExtraApproval);
        var threshold = await provider.GetSalesApprovalAmountThresholdAsync(Try, ExtraApproval);
        var discount = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetSalesDiscountLimitAsync(SyntheticGroup, ExtraApproval));

        Assert.Equal(companyId, tolerance.CompanyId);
        // UUIDv5 in the sales-policies namespace, names "sales-delivery-tolerance:<company>:v1" and
        // "sales-approval-amount-threshold:<company>:TRY:v1".
        Assert.Equal(new ProfileVersionV1(Guid.Parse(toleranceProfile), 1), tolerance.Profile);
        Assert.Equal((1m, 2m, 3m), (tolerance.WarnPercent, tolerance.ApprovalPercent, tolerance.BlockPercent));
        Assert.True(tolerance.IsActive);
        Assert.Equal(ExtraApproval, tolerance.EffectiveFrom);
        Assert.Null(tolerance.EffectiveThrough);

        Assert.Equal(companyId, threshold.CompanyId);
        Assert.Equal(Try, threshold.CurrencyId);
        Assert.Equal(new ProfileVersionV1(Guid.Parse(thresholdProfile), 1), threshold.Profile);
        Assert.False(threshold.IsEnabled);
        Assert.Null(threshold.ThresholdAmountText);
        Assert.True(threshold.IsActive);
        Assert.Equal(ExtraApproval, threshold.EffectiveFrom);
        Assert.Null(threshold.EffectiveThrough);

        // SAT-D2 via SAT-D14: no limit defined, every discount goes to approval.
        Assert.Equal(SettingsPrerequisiteCodesV1.SalesPolicyNotFound, discount.Code);
    }

    [Theory]
    [InlineData("gsp")]
    [InlineData("gin")]
    public async Task GSP_and_GIN_before_SAT_D14_get_their_real_window_so_the_consumer_rejects_it(string company)
    {
        var provider = Provider(company == "gsp" ? Gsp : Gin);

        // GDC's start date (SAT-D5) is the day before GSP/GIN's (SAT-D14); never widened.
        var tolerance = await provider.GetSalesDeliveryToleranceAsync(Approval);
        var threshold = await provider.GetSalesApprovalAmountThresholdAsync(Try, Approval);

        Assert.Equal(ExtraApproval, tolerance.EffectiveFrom);
        Assert.Equal(ExtraApproval, threshold.EffectiveFrom);
        Assert.True(Approval < tolerance.EffectiveFrom);
    }

    [Theory]
    [InlineData("unauthenticated", "tolerance")]
    [InlineData("no actor id", "discount")]
    [InlineData("unresolved company", "threshold")]
    [InlineData("empty company", "tolerance")]
    [InlineData("not a member", "discount")]
    [InlineData("not a member", "threshold")]
    [InlineData("not a member of an unserved company", "tolerance")]
    public async Task Access_is_checked_before_anything_about_the_source_is_revealed(string fault, string read)
    {
        var provider = fault switch
        {
            "unauthenticated" => new ApprovedSettingsPrerequisiteProvider(new Context(Gdc, true), new Actor(false, "x", Gdc)),
            "no actor id" => new ApprovedSettingsPrerequisiteProvider(new Context(Gdc, true), new Actor(true, " ", Gdc)),
            "unresolved company" => new ApprovedSettingsPrerequisiteProvider(new Context(Gdc, false), new Actor(true, "x", Gdc)),
            "empty company" => new ApprovedSettingsPrerequisiteProvider(new Context(Guid.Empty, true), new Actor(true, "x", Guid.Empty)),
            "not a member" => new ApprovedSettingsPrerequisiteProvider(new Context(Gdc, true), new Actor(true, "x", Gsp)),
            _ => new ApprovedSettingsPrerequisiteProvider(new Context(Unserved, true), new Actor(true, "x", Gdc)),
        };

        var failure = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => Read(provider, read));

        Assert.Equal("settings_access_denied", failure.Code);
    }

    [Theory]
    [InlineData("tolerance")]
    [InlineData("discount")]
    [InlineData("threshold")]
    public async Task Cancellation_remains_cancellation(string read)
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Read(Provider(Gdc), read, new CancellationToken(true)));
    }

    [Fact]
    public async Task A_synthetic_discount_limit_is_served_exactly_with_its_versions()
    {
        var firstEnd = new DateOnly(2026, 12, 31);
        var provider = Provider(Gdc, new ApprovedSalesPolicyCatalog(
            [],
            [
                Discount() with { EffectiveThrough = firstEnd },
                Discount() with
                {
                    Version = 2, ProfileId = Guid.Parse("71000000-0000-4000-8000-0000000000ab"),
                    MaximumDiscountPercent = 7.5m, EffectiveFrom = firstEnd.AddDays(1),
                },
            ],
            []));

        var first = await provider.GetSalesDiscountLimitAsync(SyntheticGroup, Approval);
        var second = await provider.GetSalesDiscountLimitAsync(SyntheticGroup, firstEnd.AddDays(1));

        Assert.Equal(Gdc, first.CompanyId);
        Assert.Equal(SyntheticGroup, first.LimitGroupId);
        Assert.Equal(new ProfileVersionV1(SyntheticProfile, 1), first.Profile);
        Assert.Equal(5m, first.MaximumDiscountPercent);
        Assert.Equal(firstEnd, first.EffectiveThrough);
        Assert.Equal(2, second.Profile.Version);
        Assert.Equal(7.5m, second.MaximumDiscountPercent);
        // A served company whose other families are empty answers not-found for them, not unavailable.
        Assert.Equal(SettingsPrerequisiteCodesV1.SalesPolicyNotFound,
            (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
                () => provider.GetSalesDeliveryToleranceAsync(Approval))).Code);
    }

    [Fact]
    public async Task An_enabled_synthetic_threshold_keeps_its_amount_text_byte_for_byte()
    {
        var provider = Provider(Gdc, new ApprovedSalesPolicyCatalog(
            [],
            [],
            [Threshold() with { IsEnabled = true, ThresholdAmountText = "250000.00" }]));

        var policy = await provider.GetSalesApprovalAmountThresholdAsync(Try, Approval);

        Assert.True(policy.IsEnabled);
        Assert.Equal("250000.00", policy.ThresholdAmountText);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 1)]
    [InlineData(0, 2, 3)]
    [InlineData(1, 2, 3)]
    [InlineData(0, 0, 100)]
    public void Tolerance_invariant_accepts_warn_le_approval_le_block(int warn, int approval, int block)
    {
        var catalog = new ApprovedSalesPolicyCatalog(
            [Tolerance() with { WarnPercent = warn, ApprovalPercent = approval, BlockPercent = block }], [], []);

        Assert.Single(catalog.DeliveryTolerances);
    }

    [Theory]
    [InlineData("negative warn")]
    [InlineData("warn above approval")]
    [InlineData("approval above block")]
    [InlineData("block above 100")]
    [InlineData("tolerance empty company")]
    [InlineData("tolerance empty profile")]
    [InlineData("tolerance zero version")]
    [InlineData("tolerance reversed window")]
    [InlineData("tolerance blank source")]
    [InlineData("tolerance repeated version")]
    [InlineData("tolerance overlapping versions")]
    [InlineData("tolerance open-ended predecessor")]
    [InlineData("discount empty group")]
    [InlineData("discount negative")]
    [InlineData("discount above 100")]
    [InlineData("discount overlapping versions")]
    [InlineData("threshold empty currency")]
    [InlineData("threshold disabled with amount")]
    [InlineData("threshold enabled without amount")]
    [InlineData("threshold enabled with zero")]
    [InlineData("threshold enabled with signed amount")]
    [InlineData("threshold enabled with grouped amount")]
    [InlineData("threshold enabled with leading zero")]
    [InlineData("threshold enabled with leading zero before the point")]
    [InlineData("threshold blank source")]
    public void Malformed_or_ambiguous_catalog_is_refused_whole(string fault)
    {
        var tolerance = Tolerance();
        var discount = Discount();
        var threshold = Threshold();
        ApprovedSalesDeliveryToleranceEntry[] tolerances = fault switch
        {
            "negative warn" => [tolerance with { WarnPercent = -0.01m }],
            "warn above approval" => [tolerance with { WarnPercent = 2.01m }],
            "approval above block" => [tolerance with { ApprovalPercent = 3.01m }],
            "block above 100" => [tolerance with { BlockPercent = 100.01m }],
            "tolerance empty company" => [tolerance with { CompanyId = Guid.Empty }],
            "tolerance empty profile" => [tolerance with { ProfileId = Guid.Empty }],
            "tolerance zero version" => [tolerance with { Version = 0 }],
            "tolerance reversed window" => [tolerance with { EffectiveThrough = Approval.AddDays(-1) }],
            "tolerance blank source" => [tolerance with { Source = " " }],
            "tolerance repeated version" =>
            [
                tolerance with { EffectiveThrough = Approval.AddDays(1) },
                tolerance with { EffectiveFrom = Approval.AddDays(2), ProfileId = Guid.NewGuid() },
            ],
            "tolerance overlapping versions" =>
            [
                tolerance with { EffectiveThrough = Approval.AddDays(5) },
                tolerance with { Version = 2, ProfileId = Guid.NewGuid(), EffectiveFrom = Approval.AddDays(5) },
            ],
            "tolerance open-ended predecessor" =>
            [
                tolerance,
                tolerance with { Version = 2, ProfileId = Guid.NewGuid(), EffectiveFrom = Approval.AddDays(30) },
            ],
            _ => [],
        };
        ApprovedSalesDiscountLimitEntry[] discounts = fault switch
        {
            "discount empty group" => [discount with { LimitGroupId = Guid.Empty }],
            "discount negative" => [discount with { MaximumDiscountPercent = -1m }],
            "discount above 100" => [discount with { MaximumDiscountPercent = 100.01m }],
            "discount overlapping versions" =>
            [
                discount,
                discount with { Version = 2, ProfileId = Guid.NewGuid(), EffectiveFrom = Approval.AddDays(1) },
            ],
            _ => [],
        };
        ApprovedSalesApprovalAmountThresholdEntry[] thresholds = fault switch
        {
            "threshold empty currency" => [threshold with { CurrencyId = Guid.Empty }],
            "threshold disabled with amount" => [threshold with { ThresholdAmountText = "1000.00" }],
            "threshold enabled without amount" => [threshold with { IsEnabled = true }],
            "threshold enabled with zero" => [threshold with { IsEnabled = true, ThresholdAmountText = "0.00" }],
            "threshold enabled with signed amount" => [threshold with { IsEnabled = true, ThresholdAmountText = "-5" }],
            "threshold enabled with grouped amount" => [threshold with { IsEnabled = true, ThresholdAmountText = "1,000" }],
            "threshold enabled with leading zero" => [threshold with { IsEnabled = true, ThresholdAmountText = "0100" }],
            "threshold enabled with leading zero before the point" =>
                [threshold with { IsEnabled = true, ThresholdAmountText = "00.50" }],
            "threshold blank source" => [threshold with { Source = "" }],
            _ => [],
        };

        Assert.Equal("settings_profile_invalid", Assert.Throws<SettingsPrerequisiteException>(
            () => new ApprovedSalesPolicyCatalog(tolerances, discounts, thresholds)).Code);
    }

    [Fact]
    public void The_approved_catalog_holds_exactly_the_reviewed_entries()
    {
        var catalog = ApprovedSalesPolicyCatalog.Approved;

        Assert.True(new HashSet<ApprovedSalesDeliveryToleranceEntry>
        {
            ApprovedSalesPolicyCatalog.GdcDeliveryToleranceV1,
            ApprovedSalesPolicyCatalog.GspDeliveryToleranceV1,
            ApprovedSalesPolicyCatalog.GinDeliveryToleranceV1,
        }.SetEquals(catalog.DeliveryTolerances));
        Assert.Equal(3, catalog.DeliveryTolerances.Count);
        Assert.Empty(catalog.DiscountLimits);
        Assert.True(new HashSet<ApprovedSalesApprovalAmountThresholdEntry>
        {
            ApprovedSalesPolicyCatalog.GdcTurkishLiraApprovalThresholdV1,
            ApprovedSalesPolicyCatalog.GspTurkishLiraApprovalThresholdV1,
            ApprovedSalesPolicyCatalog.GinTurkishLiraApprovalThresholdV1,
        }.SetEquals(catalog.ApprovalAmountThresholds));
        Assert.Equal(3, catalog.ApprovalAmountThresholds.Count);
        Assert.True(catalog.Serves(Gdc));
        Assert.True(catalog.Serves(Gsp));
        Assert.True(catalog.Serves(Gin));
        Assert.False(catalog.Serves(Unserved));
        Assert.Equal(Gsp, ApprovedSalesPolicyCatalog.GspCompanyId);
        Assert.Equal(Gin, ApprovedSalesPolicyCatalog.GinCompanyId);
        Assert.Contains("SAT-D14", ApprovedSalesPolicyCatalog.GspDeliveryToleranceV1.Source, StringComparison.Ordinal);
        Assert.Contains("SAT-D14", ApprovedSalesPolicyCatalog.GinTurkishLiraApprovalThresholdV1.Source, StringComparison.Ordinal);
        Assert.Contains("SAT-D5", ApprovedSalesPolicyCatalog.GdcDeliveryToleranceV1.Source, StringComparison.Ordinal);
        Assert.Contains("SAT-D6", ApprovedSalesPolicyCatalog.GdcTurkishLiraApprovalThresholdV1.Source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tolerance")]
    [InlineData("discount")]
    [InlineData("threshold")]
    public async Task Unavailable_adapter_implements_the_same_port_and_preserves_refusals(string read)
    {
        ISettingsSalesPolicyProviderV1 denied = new UnavailableSettingsPrerequisiteProvider(
            new Context(Gdc, true), new Actor(true, "x", Gsp));
        ISettingsSalesPolicyProviderV1 unavailable = new UnavailableSettingsPrerequisiteProvider(
            new Context(Gdc, true), new Actor(true, "x", Gdc));

        Assert.Equal("settings_access_denied",
            (await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => Read(denied, read))).Code);
        Assert.Equal("settings_provider_unavailable",
            (await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => Read(unavailable, read))).Code);
    }

    [Fact]
    public void Public_contract_and_provider_surface_are_narrow()
    {
        Assert.Equal(
            [
                nameof(ISettingsSalesPolicyProviderV1.GetSalesApprovalAmountThresholdAsync),
                nameof(ISettingsSalesPolicyProviderV1.GetSalesDeliveryToleranceAsync),
                nameof(ISettingsSalesPolicyProviderV1.GetSalesDiscountLimitAsync),
            ],
            typeof(ISettingsSalesPolicyProviderV1).GetMethods().Select(method => method.Name).Order().ToArray());
        // No company parameter anywhere: the company is server context.
        Assert.DoesNotContain(typeof(ISettingsSalesPolicyProviderV1).GetMethods().SelectMany(m => m.GetParameters()),
            parameter => parameter.Name!.Contains("company", StringComparison.OrdinalIgnoreCase));
        Assert.Single(typeof(ApprovedSettingsPrerequisiteProvider).GetConstructors());
        Assert.Empty(typeof(ApprovedSalesPolicyCatalog).GetConstructors());
        Assert.Contains(typeof(ISettingsSalesPolicyProviderV1), typeof(ApprovedSettingsPrerequisiteProvider).GetInterfaces());
    }

    private static Task Read(ISettingsSalesPolicyProviderV1 provider, string read,
        CancellationToken cancellationToken = default) => read switch
    {
        "tolerance" => provider.GetSalesDeliveryToleranceAsync(Approval, cancellationToken),
        "discount" => provider.GetSalesDiscountLimitAsync(SyntheticGroup, Approval, cancellationToken),
        _ => provider.GetSalesApprovalAmountThresholdAsync(Try, Approval, cancellationToken),
    };

    private static ApprovedSalesDeliveryToleranceEntry Tolerance() => new(
        Gdc, SyntheticProfile, Version: 1, WarnPercent: 1m, ApprovalPercent: 2m, BlockPercent: 3m,
        IsActive: true, EffectiveFrom: Approval, EffectiveThrough: null, Source: "synthetic Settings unit test only");

    private static ApprovedSalesDiscountLimitEntry Discount() => new(
        Gdc, SyntheticGroup, SyntheticProfile, Version: 1, MaximumDiscountPercent: 5m,
        IsActive: true, EffectiveFrom: Approval, EffectiveThrough: null, Source: "synthetic Settings unit test only");

    private static ApprovedSalesApprovalAmountThresholdEntry Threshold() => new(
        Gdc, Try, SyntheticProfile, Version: 1, IsEnabled: false, ThresholdAmountText: null,
        IsActive: true, EffectiveFrom: Approval, EffectiveThrough: null, Source: "synthetic Settings unit test only");

    private static ApprovedSettingsPrerequisiteProvider Provider(Guid company, ApprovedSalesPolicyCatalog? catalog = null) =>
        catalog is null
            ? new(new Context(company, true), new Actor(true, "x", company))
            : new(new Context(company, true), new Actor(true, "x", company), catalog);

    private sealed class Context(Guid companyId, bool resolved) : ICompanyContext
    {
        public bool IsResolved => resolved;
        public Guid CompanyId => companyId;
        public void Set(Guid id) => throw new NotSupportedException();
    }

    private sealed class Actor(bool authenticated, string? userId, Guid allowedCompany) : ICurrentUser
    {
        public bool IsAuthenticated => authenticated;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { allowedCompany };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
