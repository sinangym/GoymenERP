using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Synthetic source fence tests; the Settings provider's SQL persistence has its own acceptance facts.</summary>
public sealed class ManufacturingSettingsScrapCostPolicyReaderTests
{
    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 7)]
    public async Task The_consumer_keeps_the_real_owner_value_and_version_including_the_approved_empty_catalog_version(bool enabled, long version)
    {
        var companyId = Guid.NewGuid(); var company = new CompanyContext(); company.Set(companyId); var user = new User(companyId);
        var owner = new Owner { Value = new(companyId, enabled, version) };
        var result = await new ManufacturingSettingsScrapCostPolicyReader(company, user, owner).ReadAsync(companyId, CancellationToken.None);
        Assert.Equal((companyId, enabled, $"manufacturing.include-scrap-cost:{version}"), (result!.CompanyId, result.IncludeScrapCost, result.SourceVersion));
    }

    [Theory]
    [InlineData("company")]
    [InlineData("actor")]
    [InlineData("membership")]
    [InlineData("owner-company")]
    [InlineData("owner-version")]
    [InlineData("owner-failure")]
    public async Task Scope_changes_and_unreadable_owner_answers_never_become_a_default_true(string fault)
    {
        var companyId = Guid.NewGuid(); var company = new CompanyContext(); company.Set(companyId); var user = new User(companyId);
        var owner = new Owner { Value = new(companyId, true, 0), BeforeRead = () =>
        {
            if (fault == "company") company.Set(Guid.NewGuid());
            if (fault == "actor") user.Actor = "another-synthetic-actor";
            if (fault == "membership") user.Allowed = false;
        }};
        if (fault == "owner-company") owner.Value = owner.Value with { CompanyId = Guid.NewGuid() };
        if (fault == "owner-version") owner.Value = owner.Value with { Version = -1 };
        if (fault == "owner-failure") owner.Failure = new InvalidOperationException("SENTETIK owner unavailable");
        Assert.Null(await new ManufacturingSettingsScrapCostPolicyReader(company, user, owner).ReadAsync(companyId, CancellationToken.None));
    }
    private sealed class Owner : ISettingsManufacturingScrapCostPolicyProviderV1
    {
        public required SettingsManufacturingScrapCostPolicyV1 Value { get; set; }
        public Action? BeforeRead { get; init; } public Exception? Failure { get; set; }
        public Task<SettingsManufacturingScrapCostPolicyV1> GetScrapCostPolicyAsync(Guid companyId, CancellationToken cancellationToken = default)
        { BeforeRead?.Invoke(); if (Failure is not null) throw Failure; return Task.FromResult(Value); }
    }
    private sealed class User(Guid companyId) : ICurrentUser
    {
        public string Actor { get; set; } = "synthetic-cost-reader"; public bool Allowed { get; set; } = true;
        public bool IsAuthenticated => true; public string UserId => Actor; public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { companyId };
        public bool CanAccessCompany(Guid value) => Allowed && value == companyId;
    }
}
