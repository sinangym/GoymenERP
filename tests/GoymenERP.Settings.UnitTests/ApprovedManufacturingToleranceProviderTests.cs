using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

public sealed class ApprovedManufacturingToleranceProviderTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherCompany = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid Profile = Guid.Parse("70000000-0000-4000-8000-000000000001");
    private static readonly DateOnly Start = new(2026, 9, 30);

    [Fact]
    public async Task Production_has_no_numeric_seed_or_fallback_profile()
    {
        Assert.Empty(ApprovedManufacturingToleranceCatalog.Approved.Entries);

        var failure = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() =>
            Provider(Company).GetManufacturingToleranceAsync(SettingsManufacturingProcessesV1.Slitting, Start));

        Assert.Equal("settings_provider_unavailable", failure.Code);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task Company_access_is_refused_before_source_availability_is_disclosed(
        bool authenticated, bool resolved, bool member)
    {
        var provider = new ApprovedSettingsPrerequisiteProvider(
            new Context(Company, resolved),
            new Actor(authenticated, member ? Company : OtherCompany));

        var failure = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() =>
            provider.GetManufacturingToleranceAsync(SettingsManufacturingProcessesV1.Slitting, Start));

        Assert.Equal("settings_access_denied", failure.Code);
    }

    [Fact]
    public async Task Cancellation_remains_cancellation()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Provider(Company).GetManufacturingToleranceAsync(
                SettingsManufacturingProcessesV1.Slitting, Start, new CancellationToken(true)));
    }

    [Fact]
    public async Task Synthetic_owner_source_returns_one_immutable_exact_snapshot()
    {
        var through = new DateOnly(2026, 12, 31);
        var provider = Provider(Company, new ApprovedManufacturingToleranceCatalog(
        [
            Entry() with { EffectiveThrough = through },
        ]));

        var result = await provider.GetManufacturingToleranceAsync(
            SettingsManufacturingProcessesV1.Slitting, new DateOnly(2026, 10, 1));

        Assert.Equal(Company, result.CompanyId);
        Assert.Equal(SettingsManufacturingProcessesV1.Slitting, result.Process);
        Assert.Equal(new ProfileVersionV1(Profile, 1), result.Profile);
        Assert.Equal(2.5m, result.MaximumFirePercent);
        Assert.True(result.IsActive);
        Assert.Equal(Start, result.EffectiveFrom);
        Assert.Equal(through, result.EffectiveThrough);
    }

    [Fact]
    public async Task Outside_the_window_returns_the_same_exact_version_and_real_window()
    {
        var through = Start.AddDays(10);
        var provider = Provider(Company, new ApprovedManufacturingToleranceCatalog(
        [
            Entry() with { EffectiveThrough = through },
        ]));

        var future = await provider.GetManufacturingToleranceAsync(
            SettingsManufacturingProcessesV1.Slitting, Start.AddDays(-1));
        var expired = await provider.GetManufacturingToleranceAsync(
            SettingsManufacturingProcessesV1.Slitting, through.AddDays(1));

        Assert.Equal(new ProfileVersionV1(Profile, 1), future.Profile);
        Assert.Equal(Start, future.EffectiveFrom);
        Assert.Equal(through, expired.EffectiveThrough);
    }

    [Theory]
    [InlineData("Paint")]
    [InlineData("slitting")]
    [InlineData("")]
    public async Task Reachable_source_without_the_exact_canonical_process_is_not_found(string process)
    {
        var provider = Provider(Company, new ApprovedManufacturingToleranceCatalog([Entry()]));

        var failure = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() =>
            provider.GetManufacturingToleranceAsync(process, Start));

        Assert.Equal(SettingsPrerequisiteCodesV1.ManufacturingToleranceProfileNotFound, failure.Code);
    }

    [Theory]
    [InlineData("empty company")]
    [InlineData("empty process")]
    [InlineData("noncanonical process")]
    [InlineData("empty profile")]
    [InlineData("zero version")]
    [InlineData("negative percent")]
    [InlineData("reversed window")]
    [InlineData("blank source")]
    [InlineData("repeated version")]
    [InlineData("overlapping versions")]
    public void Malformed_or_ambiguous_catalog_is_refused_whole(string fault)
    {
        var first = Entry();
        ApprovedManufacturingToleranceEntry[] entries = fault switch
        {
            "empty company" => [first with { CompanyId = Guid.Empty }],
            "empty process" => [first with { Process = "" }],
            "noncanonical process" => [first with { Process = "slitting" }],
            "empty profile" => [first with { ProfileId = Guid.Empty }],
            "zero version" => [first with { Version = 0 }],
            "negative percent" => [first with { MaximumFirePercent = -0.01m }],
            "reversed window" => [first with { EffectiveThrough = Start.AddDays(-1) }],
            "blank source" => [first with { Source = " " }],
            "repeated version" =>
            [
                first with { EffectiveThrough = Start.AddDays(1) },
                first with { EffectiveFrom = Start.AddDays(2), ProfileId = Guid.NewGuid() },
            ],
            _ =>
            [
                first,
                first with { Version = 2, ProfileId = Guid.NewGuid(), EffectiveFrom = Start.AddDays(1) },
            ],
        };

        Assert.Equal("settings_profile_invalid",
            Assert.Throws<SettingsPrerequisiteException>(
                () => new ApprovedManufacturingToleranceCatalog(entries)).Code);
    }

    [Fact]
    public async Task Unavailable_adapter_implements_the_same_port_and_preserves_refusals()
    {
        ISettingsManufacturingToleranceProviderV1 denied = new UnavailableSettingsPrerequisiteProvider(
            new Context(Company, true), new Actor(true, OtherCompany));
        ISettingsManufacturingToleranceProviderV1 unavailable = new UnavailableSettingsPrerequisiteProvider(
            new Context(Company, true), new Actor(true, Company));

        Assert.Equal("settings_access_denied", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(() =>
            denied.GetManufacturingToleranceAsync(SettingsManufacturingProcessesV1.Slitting, Start))).Code);
        Assert.Equal("settings_provider_unavailable", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(() =>
            unavailable.GetManufacturingToleranceAsync(SettingsManufacturingProcessesV1.Slitting, Start))).Code);
    }

    [Fact]
    public void Public_contract_and_provider_surface_are_narrow()
    {
        var method = Assert.Single(typeof(ISettingsManufacturingToleranceProviderV1).GetMethods());
        Assert.Equal(nameof(ISettingsManufacturingToleranceProviderV1.GetManufacturingToleranceAsync), method.Name);
        Assert.Single(typeof(ApprovedSettingsPrerequisiteProvider).GetConstructors());
        Assert.Contains(typeof(ISettingsManufacturingToleranceProviderV1),
            typeof(ApprovedSettingsPrerequisiteProvider).GetInterfaces());
        Assert.Equal("settings_manufacturing_tolerance_profile_not_found",
            SettingsPrerequisiteCodesV1.ManufacturingToleranceProfileNotFound);
    }

    private static ApprovedManufacturingToleranceEntry Entry() => new(
        Company,
        SettingsManufacturingProcessesV1.Slitting,
        Profile,
        Version: 1,
        MaximumFirePercent: 2.5m,
        IsActive: true,
        EffectiveFrom: Start,
        EffectiveThrough: null,
        Source: "synthetic Settings unit test only");

    private static ApprovedSettingsPrerequisiteProvider Provider(
        Guid company, ApprovedManufacturingToleranceCatalog? catalog = null) =>
        catalog is null
            ? new(new Context(company, true), new Actor(true, company))
            : new(new Context(company, true), new Actor(true, company), catalog);

    private sealed class Context(Guid companyId, bool resolved) : ICompanyContext
    {
        public bool IsResolved => resolved;
        public Guid CompanyId => companyId;
        public void Set(Guid id) => throw new NotSupportedException();
    }

    private sealed class Actor(bool authenticated, Guid allowedCompany) : ICurrentUser
    {
        public bool IsAuthenticated => authenticated;
        public string? UserId => authenticated ? "synthetic-settings-actor" : null;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { allowedCompany };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
