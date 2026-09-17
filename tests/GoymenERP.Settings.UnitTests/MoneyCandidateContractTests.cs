using System.Reflection;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>
/// Read-only money currency candidates (Business #66 5702138790). Synthetic identities only:
/// no currency code or list appears here, because none is approved data.
/// </summary>
public sealed class MoneyCandidateContractTests
{
    [Theory]
    [InlineData(true, true, true, true, "settings_provider_unavailable")]
    [InlineData(false, true, true, true, "settings_access_denied")]
    [InlineData(true, false, true, true, "settings_access_denied")]
    [InlineData(true, true, false, true, "settings_access_denied")]
    [InlineData(true, true, true, false, "settings_access_denied")]
    public async Task Candidates_fail_explicitly_after_the_company_access_check_and_are_never_an_empty_list(
        bool authenticated, bool resolved, bool allowed, bool companyBound, string expectedCode)
    {
        var company = Guid.NewGuid();
        ISettingsMoneyCandidateReader reader = new UnavailableSettingsPrerequisiteProvider(
            new CompanyContext(companyBound ? company : Guid.Empty, resolved),
            new CurrentUser(authenticated, allowed ? company : Guid.NewGuid()));

        // Throws rather than answering: an empty list would read as "no currency here".
        var failure = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => reader.GetMoneyCurrencyCandidatesAsync());
        Assert.Equal(expectedCode, failure.Code);
    }

    [Fact]
    public async Task An_actor_without_an_identifier_is_denied_before_availability_is_revealed()
    {
        var company = Guid.NewGuid();
        ISettingsMoneyCandidateReader reader = new UnavailableSettingsPrerequisiteProvider(
            new CompanyContext(company, true), new CurrentUser(true, company, userId: " "));

        Assert.Equal("settings_access_denied", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => reader.GetMoneyCurrencyCandidatesAsync())).Code);
    }

    [Fact]
    public async Task Canceled_resolution_never_returns_candidates()
    {
        var company = Guid.NewGuid();
        ISettingsMoneyCandidateReader reader = new UnavailableSettingsPrerequisiteProvider(
            new CompanyContext(company, true), new CurrentUser(true, company));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.GetMoneyCurrencyCandidatesAsync(new CancellationToken(true)));
    }

    [Fact]
    public void A_candidate_row_is_exactly_identity_code_and_label()
    {
        var properties = typeof(MoneyCurrencyCandidateV1)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.Name != "EqualityContract")
            .ToDictionary(property => property.Name, property => property.PropertyType);

        // No scale, profile, version, operation, effective range or default flag: being
        // listed is not validity, and the resolver decides those for the chosen identity.
        Assert.Equal(
            new Dictionary<string, Type>
            {
                ["CurrencyId"] = typeof(Guid),
                ["Code"] = typeof(string),
                ["DisplayLabel"] = typeof(string),
            },
            properties);
    }

    [Fact]
    public void The_company_is_resolved_on_the_server_never_supplied_by_the_request()
    {
        var method = Assert.Single(typeof(ISettingsMoneyCandidateReader).GetMethods());
        Assert.Equal(nameof(ISettingsMoneyCandidateReader.GetMoneyCurrencyCandidatesAsync), method.Name);
        Assert.Equal(typeof(Task<IReadOnlyList<MoneyCurrencyCandidateV1>>), method.ReturnType);
        // Only cancellation: no company, operation or date input (see the PR's open question).
        Assert.Equal([typeof(CancellationToken)], method.GetParameters().Select(p => p.ParameterType));
    }

    [Fact]
    public void The_existing_prerequisite_port_is_unchanged_for_the_modules_that_implement_it()
    {
        // Finance and Product test doubles implement ISettingsPrerequisiteProvider; a new
        // member there would break them. The candidate read is a separate port instead.
        Assert.Equal(
            ["GetMoneyProfileAsync", "GetPeriodEligibilityAsync", "GetUnitProfileAsync"],
            typeof(ISettingsPrerequisiteProvider).GetMethods().Select(m => m.Name).Order());
        Assert.False(typeof(ISettingsPrerequisiteProvider).IsAssignableFrom(typeof(ISettingsMoneyCandidateReader)));
    }

    private sealed class CompanyContext(Guid companyId, bool resolved) : ICompanyContext
    {
        public bool IsResolved => resolved;
        public Guid CompanyId => companyId;
        public void Set(Guid id) => throw new NotSupportedException();
    }

    private sealed class CurrentUser(bool authenticated, Guid allowedCompany, string? userId = null) : ICurrentUser
    {
        public bool IsAuthenticated => authenticated;
        public string? UserId => authenticated ? userId ?? "synthetic-settings-test-actor" : null;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { allowedCompany };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
