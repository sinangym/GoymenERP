using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductUnitProfileAdapterTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid UnitId = Guid.NewGuid();
    private static readonly DateOnly Date = new(2026, 9, 13);
    private static UnitProfileV1 Profile => new(CompanyId, UnitId, "synthetic-kg", "synthetic-mass", true,
        new(Guid.NewGuid(), 9007199254740993L), 4, true, Date.AddDays(-1), Date.AddDays(1));

    [Fact]
    public async Task Exact_owner_profile_and_date_are_preserved_without_quantity_conversion()
    {
        var profile = Profile;
        var provider = new Provider((id, date, _) =>
        {
            Assert.Equal(UnitId, id);
            Assert.Equal(Date, date);
            return Task.FromResult(profile);
        });
        var read = await new ProductUnitProfileAdapter(provider, new Context()).ReadAsync(UnitId, Date, true);
        Assert.Same(profile, read.Profile);
        Assert.True(read.IsUsable);
    }

    [Fact]
    public async Task Kilogram_is_required_only_where_the_caller_requires_it()
    {
        var profile = Profile with { IsKilogram = false };
        var adapter = new ProductUnitProfileAdapter(new Provider((_, _, _) => Task.FromResult(profile)), new Context());
        Assert.True((await adapter.ReadAsync(UnitId, Date, requiresKilogram: false)).IsUsable);
        Assert.False((await adapter.ReadAsync(UnitId, Date, requiresKilogram: true)).IsUsable);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("unit")]
    [InlineData("profileId")]
    [InlineData("version")]
    [InlineData("scale")]
    [InlineData("reversed")]
    [InlineData("dimension")]
    [InlineData("code")]
    public async Task Malformed_foreign_or_contradictory_provider_data_is_an_invalid_profile(string fault)
    {
        var profile = Profile;
        profile = fault switch
        {
            "company" => profile with { CompanyId = Guid.NewGuid() },
            "unit" => profile with { UnitId = Guid.NewGuid() },
            "profileId" => profile with { Profile = profile.Profile with { ProfileId = Guid.Empty } },
            "version" => profile with { Profile = profile.Profile with { Version = 0 } },
            "scale" => profile with { Scale = -1 },
            // Ends before it starts, and also before the date: still corrupt, never "expired".
            "reversed" => profile with { EffectiveThrough = Date.AddDays(-2) },
            "dimension" => profile with { DimensionCode = " " },
            "code" => profile with { UnitCode = " " },
            _ => throw new InvalidOperationException(),
        };
        var adapter = new ProductUnitProfileAdapter(new Provider((_, _, _) => Task.FromResult(profile)), new Context());
        Assert.Equal("settings_profile_invalid", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => adapter.ReadAsync(UnitId, Date, true))).Code);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("future")]
    [InlineData("expired")]
    [InlineData("kilogram")]
    public async Task Well_formed_profile_the_owner_does_not_allow_on_the_date_is_unusable_not_invalid(string fault)
    {
        var profile = Profile;
        profile = fault switch
        {
            "inactive" => profile with { IsActive = false },
            "future" => profile with { EffectiveFrom = Date.AddDays(1), EffectiveThrough = Date.AddDays(2) },
            "expired" => profile with { EffectiveThrough = Date.AddDays(-1) },
            "kilogram" => profile with { IsKilogram = false },
            _ => throw new InvalidOperationException(),
        };
        var adapter = new ProductUnitProfileAdapter(new Provider((_, _, _) => Task.FromResult(profile)), new Context());
        var read = await adapter.ReadAsync(UnitId, Date, true);
        Assert.False(read.IsUsable);
        Assert.Same(profile, read.Profile);
    }

    [Fact]
    public async Task Both_ends_of_the_effective_window_are_inclusive()
    {
        var profile = Profile with { EffectiveFrom = Date, EffectiveThrough = Date };
        var adapter = new ProductUnitProfileAdapter(new Provider((_, _, _) => Task.FromResult(profile)), new Context());
        Assert.True((await adapter.ReadAsync(UnitId, Date, true)).IsUsable);
    }

    [Theory]
    [InlineData("settings_provider_unavailable")]
    [InlineData("settings_access_denied")]
    public async Task Provider_failure_is_preserved_without_default_profile(string code)
    {
        var error = new SettingsPrerequisiteException(code);
        var adapter = new ProductUnitProfileAdapter(new Provider((_, _, _) => Task.FromException<UnitProfileV1>(error)), new Context());
        Assert.Same(error, await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => adapter.ReadAsync(UnitId, Date, true)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Context_change_during_pending_provider_hides_success_and_failure(bool fail)
    {
        var context = new Context();
        var pending = new TaskCompletionSource<UnitProfileV1>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new ProductUnitProfileAdapter(new Provider((_, _, _) => pending.Task), context);
        var read = adapter.ReadAsync(UnitId, Date, true);
        context.Set(Guid.NewGuid());
        if (fail) pending.SetException(new SettingsPrerequisiteException("settings_provider_unavailable"));
        else pending.SetResult(Profile);
        Assert.Equal("settings_access_denied", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => read)).Code);
    }

    [Fact]
    public async Task Cancellation_during_provider_does_not_release_profile()
    {
        using var cancellation = new CancellationTokenSource();
        var provider = new Provider((_, _, _) => { cancellation.Cancel(); return Task.FromResult(Profile); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ProductUnitProfileAdapter(provider, new Context()).ReadAsync(UnitId, Date, true, cancellation.Token));
    }

    private sealed class Context : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = ProductUnitProfileAdapterTests.CompanyId;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class Provider(Func<Guid, DateOnly, CancellationToken, Task<UnitProfileV1>> read) : ISettingsPrerequisiteProvider
    {
        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default) => read(unitId, effectiveOn, cancellationToken);
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
