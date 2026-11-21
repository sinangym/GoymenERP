using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// KLN-D85 (STK-SC33-a): the company-wide average cost in another unit. Only a known company-wide figure is converted; a
/// kilogram unit is that figure as is and reads no policy; any other unit needs the Settings unit conversion policy in force
/// on the caller's date and the product's kilograms per unit, and every missing or unreadable input refuses the conversion
/// with a code (fail-closed, never a zero). The contract shape, codes and registration are pinned here so a change is a
/// deliberate V2. SENTETIK / NOT_ENV.
/// </summary>
public sealed class InventoryAverageCostUnitReadPortTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("11111111-1111-4111-8111-111111111112");
    private static readonly Guid Location = Guid.Parse("22222222-2222-4222-8222-222222222221");
    private static readonly Guid Product = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Kilogram = Guid.Parse("44444444-4444-4444-8444-444444444441");
    private static readonly Guid Piece = Guid.Parse("44444444-4444-4444-8444-444444444442");
    private static readonly Guid PolicyProfile = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly DateOnly PolicyDate = new(2026, 11, 16);

    private static InventoryCompanyWideAverageCostReadAnswerV1 KnownPerKg(decimal average = 12.5m) =>
        InventoryCompanyWideAverageCostReadAnswerV1.ForValue(CompanyA, Product, average, [new(Location, 40m, 3)]);

    private static SettingsUnitConversionPolicyV1 Policy(Guid? company = null, int decimals = 3,
        MidpointRounding rounding = MidpointRounding.AwayFromZero, decimal density = 7.85m) =>
        new(company ?? CompanyA, new ProfileVersionV1(PolicyProfile, 2), density, decimals, rounding, 2,
            MidpointRounding.AwayFromZero, MidpointRounding.AwayFromZero, 1m, SettingsWeighbridgeToleranceBoundaryV1.Inclusive,
            SettingsWeighbridgeToleranceBaseV1.TheoreticalKg, new DateOnly(2026, 1, 1), null);

    private static Units KnownUnits => new(
        new UnitIdentityV1(Kilogram, "KG", "mass", true), new UnitIdentityV1(Piece, "AD", "count", false));

    [Fact]
    public void Contract_version_one_pins_its_basis_source_and_scale()
    {
        Assert.Equal((1, "purchase-moving-average", "all-locations-on-hand-kg-weighted", "company-base-currency",
                "settings-unit-conversion-policy", 4),
            (InventoryAverageCostUnitReadContractV1.Version, InventoryAverageCostUnitReadContractV1.CostBasis,
                InventoryAverageCostUnitReadContractV1.Aggregation, InventoryAverageCostUnitReadContractV1.CostCurrencyBasis,
                InventoryAverageCostUnitReadContractV1.ConversionSource, InventoryAverageCostUnitReadContractV1.InternalScale));
        Assert.Equal(
            [
                "kilograms_per_unit_no_source", "kilograms_per_unit_unavailable", "unit_coefficient_not_positive",
                "unit_conversion_policy_not_found", "unit_conversion_policy_unreadable", "unit_identity_unavailable",
                "unit_unknown",
            ],
            InventoryAverageCostUnitConversionRefusalCodesV1.All.Order(StringComparer.Ordinal));
        Assert.Equal([1, 2, 3, 4], Enum.GetValues<InventoryAverageCostUnitReadStatusV1>().Select(status => (int)status));
    }

    [Fact]
    public void The_answer_shape_is_exactly_the_published_properties_and_the_port_takes_no_company()
    {
        Assert.Equal(
            [
                "CompanyId", "CompanyWide", "ContractVersion", "ConversionRefusalCode", "CostCurrencyBasis", "CostCurrencyId", "CostPerUnit",
                "KilogramsPerUnitCoefficient", "PolicyProfileId", "PolicyVersion", "ProductId", "Status", "UnitId",
            ],
            typeof(InventoryAverageCostUnitReadAnswerV1).GetProperties().Select(property => property.Name)
                .Where(name => name != "EqualityContract").Order(StringComparer.Ordinal));

        var method = Assert.Single(typeof(IInventoryAverageCostUnitReadPortV1).GetMethods());
        Assert.Equal("ReadCompanyWideInUnitAsync", method.Name);
        Assert.Equal([typeof(Guid), typeof(Guid), typeof(DateOnly), typeof(CancellationToken)],
            method.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Theory]
    [InlineData(InventoryAverageCostReadStatusV1.NoSource)]
    [InlineData(InventoryAverageCostReadStatusV1.Unreadable)]
    public async Task A_company_wide_figure_that_is_not_known_is_never_converted_and_nothing_else_is_read(
        InventoryAverageCostReadStatusV1 status)
    {
        var perKg = status == InventoryAverageCostReadStatusV1.NoSource
            ? InventoryCompanyWideAverageCostReadAnswerV1.ForNoSource(CompanyA, Product)
            : InventoryCompanyWideAverageCostReadAnswerV1.ForUnreadable(
                CompanyA, Product, InventoryAverageCostUnreadableCodesV1.All.Order(StringComparer.Ordinal).First(), Location,
                [new(Location, 40m, null)]);
        var policies = new Policies(Policy());
        var weights = new Weights(new InventoryKilogramsPerUnitV1(InventoryKilogramsPerUnitOutcomeV1.Found, 2m, "v1"));

        var units = KnownUnits;

        var answer = await Port(perKg, units, policies, weights).ReadCompanyWideInUnitAsync(Product, Piece, PolicyDate);

        Assert.Equal(((int)status, null, null, null, null), ((int)answer.Status, answer.CostPerUnit,
            answer.KilogramsPerUnitCoefficient, answer.ConversionRefusalCode, answer.PolicyVersion));
        Assert.Same(perKg, answer.CompanyWide);
        Assert.Equal((0, 0, 0), (units.Reads, policies.Reads, weights.Reads));
    }

    [Fact]
    public async Task A_kilogram_unit_is_the_per_kilogram_figure_as_is_and_reads_no_policy()
    {
        var policies = new Policies(Policy());
        var weights = new Weights(new InventoryKilogramsPerUnitV1(InventoryKilogramsPerUnitOutcomeV1.Found, 2m, "v1"));

        var answer = await Port(KnownPerKg(), KnownUnits, policies, weights).ReadCompanyWideInUnitAsync(Product, Kilogram, PolicyDate);

        Assert.Equal((InventoryAverageCostUnitReadStatusV1.Value, 12.5m, 1m, null, null, CompanyA, Product, Kilogram),
            (answer.Status, answer.CostPerUnit, answer.KilogramsPerUnitCoefficient, answer.PolicyProfileId, answer.PolicyVersion,
                answer.CompanyId, answer.ProductId, answer.UnitId));
        Assert.Equal((0, 0), (policies.Reads, weights.Reads));
    }

    [Fact]
    public async Task Another_unit_is_converted_with_the_policy_in_force_on_the_callers_date_and_names_its_version()
    {
        var policies = new Policies(Policy(decimals: 3));
        var weights = new Weights(new InventoryKilogramsPerUnitV1(InventoryKilogramsPerUnitOutcomeV1.Found, 1.2345m, "product-v4"));

        var answer = await Port(KnownPerKg(10m), KnownUnits, policies, weights).ReadCompanyWideInUnitAsync(Product, Piece, PolicyDate);

        // KLN-D88: 1.2345 → 1.235 (3 decimals, AwayFromZero, the policy's values); 10 · 1.235 = 12.35.
        Assert.Equal((InventoryAverageCostUnitReadStatusV1.Value, 12.35m, 1.235m, null, PolicyProfile, 2L),
            (answer.Status, answer.CostPerUnit, answer.KilogramsPerUnitCoefficient, answer.ConversionRefusalCode,
                answer.PolicyProfileId, answer.PolicyVersion));
        Assert.Equal([PolicyDate], policies.Dates);
        Assert.Equal([(Product, Piece, 7.85m)], weights.Calls);
        // AK-1: the weight source answers for the same policy date the conversion policy was read for.
        Assert.Equal([PolicyDate], weights.PolicyDates);
    }

    [Fact]
    public async Task No_policy_in_force_refuses_the_conversion_and_asks_no_product_weight()
    {
        var policies = new Policies(new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.UnitConversionPolicyNotFound));
        var weights = new Weights(new InventoryKilogramsPerUnitV1(InventoryKilogramsPerUnitOutcomeV1.Found, 2m, "v1"));

        var answer = await Port(KnownPerKg(), KnownUnits, policies, weights).ReadCompanyWideInUnitAsync(Product, Piece, PolicyDate);

        AssertRefused(answer, InventoryAverageCostUnitConversionRefusalCodesV1.PolicyNotFound, policy: false);
        Assert.Equal(0, weights.Reads);
    }

    [Theory]
    [InlineData("settings_access_denied")]
    [InlineData("settings_provider_unavailable")]
    [InlineData("settings_profile_invalid")]
    public async Task An_unreadable_policy_refuses_the_conversion(string code)
    {
        var weights = new Weights(new InventoryKilogramsPerUnitV1(InventoryKilogramsPerUnitOutcomeV1.Found, 2m, "v1"));

        var answer = await Port(KnownPerKg(), KnownUnits, new Policies(new SettingsPrerequisiteException(code)), weights)
            .ReadCompanyWideInUnitAsync(Product, Piece, PolicyDate);

        AssertRefused(answer, InventoryAverageCostUnitConversionRefusalCodesV1.PolicyUnreadable, policy: false);
        Assert.Equal(0, weights.Reads);
    }

    [Fact]
    public async Task A_missing_provider_another_companys_policy_or_a_malformed_policy_refuses_the_conversion()
    {
        var weights = new Weights(new InventoryKilogramsPerUnitV1(InventoryKilogramsPerUnitOutcomeV1.Found, 2m, "v1"));
        foreach (var policies in new Policies?[]
                 {
                     null, new(Policy(company: CompanyB)), new(Policy(decimals: -1)), new(Policy(rounding: (MidpointRounding)99)),
                     new(Policy(density: 0m)),
                 })
        {
            var answer = await Port(KnownPerKg(), KnownUnits, policies, weights).ReadCompanyWideInUnitAsync(Product, Piece, PolicyDate);
            AssertRefused(answer, InventoryAverageCostUnitConversionRefusalCodesV1.PolicyUnreadable, policy: false);
        }

        Assert.Equal(0, weights.Reads);
    }

    [Fact]
    public async Task An_unknown_or_unreadable_unit_refuses_the_conversion_before_the_policy()
    {
        var policies = new Policies(Policy());
        var weights = new Weights(new InventoryKilogramsPerUnitV1(InventoryKilogramsPerUnitOutcomeV1.Found, 2m, "v1"));

        var unknown = await Port(KnownPerKg(), KnownUnits, policies, weights)
            .ReadCompanyWideInUnitAsync(Product, Guid.Parse("44444444-4444-4444-8444-444444444449"), PolicyDate);
        AssertRefused(unknown, InventoryAverageCostUnitConversionRefusalCodesV1.UnitUnknown, policy: false);

        var noCatalog = await Port(KnownPerKg(), null, policies, weights).ReadCompanyWideInUnitAsync(Product, Piece, PolicyDate);
        AssertRefused(noCatalog, InventoryAverageCostUnitConversionRefusalCodesV1.UnitIdentityUnavailable, policy: false);

        var failing = await Port(KnownPerKg(), new Units(fails: true), policies, weights)
            .ReadCompanyWideInUnitAsync(Product, Piece, PolicyDate);
        AssertRefused(failing, InventoryAverageCostUnitConversionRefusalCodesV1.UnitIdentityUnavailable, policy: false);

        Assert.Equal((0, 0), (policies.Reads, weights.Reads));
    }

    [Theory]
    [InlineData(InventoryKilogramsPerUnitOutcomeV1.NoSource, null, InventoryAverageCostUnitConversionRefusalCodesV1.KilogramsPerUnitNoSource)]
    [InlineData(InventoryKilogramsPerUnitOutcomeV1.Unavailable, null, InventoryAverageCostUnitConversionRefusalCodesV1.KilogramsPerUnitUnavailable)]
    [InlineData(InventoryKilogramsPerUnitOutcomeV1.Found, null, InventoryAverageCostUnitConversionRefusalCodesV1.KilogramsPerUnitUnavailable)]
    [InlineData(InventoryKilogramsPerUnitOutcomeV1.Found, "0", InventoryAverageCostUnitConversionRefusalCodesV1.KilogramsPerUnitUnavailable)]
    [InlineData(InventoryKilogramsPerUnitOutcomeV1.Found, "-1", InventoryAverageCostUnitConversionRefusalCodesV1.KilogramsPerUnitUnavailable)]
    [InlineData((InventoryKilogramsPerUnitOutcomeV1)9, "2", InventoryAverageCostUnitConversionRefusalCodesV1.KilogramsPerUnitUnavailable)]
    public async Task A_product_weight_that_is_missing_or_unreadable_refuses_the_conversion_naming_the_policy_read(
        InventoryKilogramsPerUnitOutcomeV1 outcome, string? kilograms, string expected)
    {
        var weights = new Weights(new InventoryKilogramsPerUnitV1(outcome, kilograms is null ? null : decimal.Parse(kilograms,
            System.Globalization.CultureInfo.InvariantCulture), null));

        var answer = await Port(KnownPerKg(), KnownUnits, new Policies(Policy()), weights)
            .ReadCompanyWideInUnitAsync(Product, Piece, PolicyDate);

        AssertRefused(answer, expected, policy: true);
    }

    [Fact]
    public async Task A_failing_product_weight_source_refuses_the_conversion()
    {
        var answer = await Port(KnownPerKg(), KnownUnits, new Policies(Policy()), new Weights(fails: true))
            .ReadCompanyWideInUnitAsync(Product, Piece, PolicyDate);

        AssertRefused(answer, InventoryAverageCostUnitConversionRefusalCodesV1.KilogramsPerUnitUnavailable, policy: true);
    }

    [Fact]
    public async Task A_coefficient_that_rounds_to_zero_refuses_the_conversion_with_its_coefficient()
    {
        var weights = new Weights(new InventoryKilogramsPerUnitV1(InventoryKilogramsPerUnitOutcomeV1.Found, 0.0004m, "v1"));

        var answer = await Port(KnownPerKg(), KnownUnits, new Policies(Policy(decimals: 3)), weights)
            .ReadCompanyWideInUnitAsync(Product, Piece, PolicyDate);

        Assert.Equal((InventoryAverageCostUnitReadStatusV1.ConversionRefused, null, 0m,
                InventoryAverageCostUnitConversionRefusalCodesV1.CoefficientNotPositive, PolicyProfile, 2L),
            (answer.Status, answer.CostPerUnit, answer.KilogramsPerUnitCoefficient, answer.ConversionRefusalCode,
                answer.PolicyProfileId, answer.PolicyVersion));
    }

    [Fact]
    public async Task The_module_default_product_weight_source_refuses_every_non_kilogram_conversion_today()
    {
        var answer = await Port(KnownPerKg(), KnownUnits, new Policies(Policy()), new NoSourceInventoryKilogramsPerUnitSourceV1())
            .ReadCompanyWideInUnitAsync(Product, Piece, PolicyDate);

        AssertRefused(answer, InventoryAverageCostUnitConversionRefusalCodesV1.KilogramsPerUnitNoSource, policy: true);
    }

    [Fact]
    public async Task An_empty_unit_is_refused_as_a_caller_fault_before_any_read()
    {
        var perKg = new PerKg(KnownPerKg());
        var port = new InventoryAverageCostUnitReadPort(perKg, KnownUnits, new Policies(Policy()),
            new NoSourceInventoryKilogramsPerUnitSourceV1());

        await Assert.ThrowsAsync<ArgumentException>(() => port.ReadCompanyWideInUnitAsync(Product, Guid.Empty, PolicyDate));
        Assert.Equal(0, perKg.Reads);
    }

    [Fact]
    public void Answers_keep_their_invariants()
    {
        var known = KnownPerKg();
        var noSource = InventoryCompanyWideAverageCostReadAnswerV1.ForNoSource(CompanyA, Product);

        Assert.Throws<ArgumentException>(() => InventoryAverageCostUnitReadAnswerV1.ForUnconvertible(known, Piece));
        Assert.Throws<ArgumentException>(() => InventoryAverageCostUnitReadAnswerV1.ForKilogram(noSource, Kilogram));
        Assert.Throws<ArgumentException>(() => InventoryAverageCostUnitReadAnswerV1.ForKilogram(known, Guid.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InventoryAverageCostUnitReadAnswerV1.ForConverted(known, Piece, 1.00001m, 1m, PolicyProfile, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InventoryAverageCostUnitReadAnswerV1.ForConverted(known, Piece, 1m, 0m, PolicyProfile, 1));
        Assert.Throws<ArgumentException>(() =>
            InventoryAverageCostUnitReadAnswerV1.ForConverted(known, Piece, 1m, 1m, Guid.Empty, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InventoryAverageCostUnitReadAnswerV1.ForConverted(known, Piece, 1m, 1m, PolicyProfile, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InventoryAverageCostUnitReadAnswerV1.ForRefused(known, Piece, "unknown_code"));
        Assert.Throws<ArgumentException>(() => InventoryAverageCostUnitReadAnswerV1.ForRefused(
            known, Piece, InventoryAverageCostUnitConversionRefusalCodesV1.UnitUnknown, kilogramsPerUnitCoefficient: 1m));
        Assert.Throws<ArgumentException>(() => InventoryAverageCostUnitReadAnswerV1.ForRefused(
            known, Piece, InventoryAverageCostUnitConversionRefusalCodesV1.CoefficientNotPositive));
        Assert.Throws<ArgumentException>(() => InventoryAverageCostUnitReadAnswerV1.ForRefused(
            known, Piece, InventoryAverageCostUnitConversionRefusalCodesV1.KilogramsPerUnitNoSource, policyProfileId: PolicyProfile));
        Assert.Throws<ArgumentException>(() => InventoryAverageCostUnitReadAnswerV1.ForRefused(
            noSource, Piece, InventoryAverageCostUnitConversionRefusalCodesV1.UnitUnknown));
    }

    [Fact]
    public void The_inventory_module_registers_the_unit_port_and_a_fail_closed_product_weight_default()
    {
        var services = new ServiceCollection().AddInventoryModule(new Settings("Server=unused.invalid;Database=unused;"));

        var port = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IInventoryAverageCostUnitReadPortV1));
        Assert.Equal(ServiceLifetime.Scoped, port.Lifetime);
        var weight = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IInventoryKilogramsPerUnitSourceV1));
        Assert.Equal((ServiceLifetime.Scoped, typeof(NoSourceInventoryKilogramsPerUnitSourceV1)),
            (weight.Lifetime, weight.ImplementationType));
    }

    private static void AssertRefused(InventoryAverageCostUnitReadAnswerV1 answer, string code, bool policy)
    {
        Assert.Equal((InventoryAverageCostUnitReadStatusV1.ConversionRefused, null, null, code),
            (answer.Status, answer.CostPerUnit, answer.KilogramsPerUnitCoefficient, answer.ConversionRefusalCode));
        Assert.Equal(policy ? (PolicyProfile, 2L) : ((Guid?)null, (long?)null), (answer.PolicyProfileId, answer.PolicyVersion));
        Assert.Equal(InventoryAverageCostReadStatusV1.Value, answer.CompanyWide.Status);
    }

    private static InventoryAverageCostUnitReadPort Port(InventoryCompanyWideAverageCostReadAnswerV1 perKg,
        ISettingsUnitIdentityCatalogV1? units, ISettingsUnitConversionPolicyProviderV1? policies,
        IInventoryKilogramsPerUnitSourceV1 weights) =>
        new(new PerKg(perKg), units, policies, weights);

    private sealed class PerKg(InventoryCompanyWideAverageCostReadAnswerV1 answer) : IInventoryCompanyWideAverageCostReadPortV1
    {
        public int Reads { get; private set; }

        public Task<InventoryCompanyWideAverageCostReadAnswerV1> ReadCompanyWideAsync(
            Guid productId, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(answer);
        }
    }

    private sealed class Units : ISettingsUnitIdentityCatalogV1
    {
        private readonly UnitIdentityV1[] identities;
        private readonly bool fails;

        public Units(params UnitIdentityV1[] identities) => this.identities = identities;

        public Units(bool fails)
        {
            identities = [];
            this.fails = fails;
        }

        public int Reads { get; private set; }

        public Task<UnitIdentityV1?> ReadAsync(Guid unitId, CancellationToken cancellationToken = default)
        {
            Reads++;
            return fails
                ? throw new SettingsPrerequisiteException("settings_provider_unavailable")
                : Task.FromResult(identities.SingleOrDefault(identity => identity.UnitId == unitId));
        }

        public Task<IReadOnlyList<UnitIdentityV1>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UnitIdentityV1>>(identities);
    }

    private sealed class Policies : ISettingsUnitConversionPolicyProviderV1
    {
        private readonly SettingsUnitConversionPolicyV1? policy;
        private readonly Exception? failure;

        public Policies(SettingsUnitConversionPolicyV1 policy) => this.policy = policy;

        public Policies(Exception failure) => this.failure = failure;

        public int Reads => Dates.Count;

        public List<DateOnly> Dates { get; } = [];

        public Task<SettingsUnitConversionPolicyV1> GetUnitConversionPolicyAsync(DateOnly effectiveOn,
            CancellationToken cancellationToken = default)
        {
            Dates.Add(effectiveOn);
            return failure is not null ? throw failure : Task.FromResult(policy!);
        }
    }

    private sealed class Weights : IInventoryKilogramsPerUnitSourceV1
    {
        private readonly InventoryKilogramsPerUnitV1? answer;
        private readonly bool fails;

        public Weights(InventoryKilogramsPerUnitV1 answer) => this.answer = answer;

        public Weights(bool fails) => this.fails = fails;

        public int Reads => Calls.Count;

        public List<(Guid ProductId, Guid UnitId, decimal Density)> Calls { get; } = [];

        public List<DateOnly> PolicyDates { get; } = [];

        public Task<InventoryKilogramsPerUnitV1> ReadAsync(Guid productId, Guid unitId, DateOnly policyDate,
            decimal densityGramsPerCubicCentimetre, CancellationToken cancellationToken = default)
        {
            Calls.Add((productId, unitId, densityGramsPerCubicCentimetre));
            PolicyDates.Add(policyDate);
            return fails ? throw new InvalidOperationException("synthetic failure") : Task.FromResult(answer!);
        }
    }

    private sealed class Settings(string connectionString) : IConfiguration
    {
        public string? this[string key]
        {
            get => key == "DB_CONNECTION_STRING" ? connectionString : null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => throw new NotSupportedException();

        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }
}
