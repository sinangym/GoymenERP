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
    public async Task Unavailable_candidates_fail_explicitly_after_the_company_access_check_and_are_never_an_empty_list(
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
    public async Task An_empty_company_is_denied_on_its_own_even_for_an_actor_allowed_that_identifier()
    {
        // Membership would pass here, so only the empty-company check can deny.
        ISettingsMoneyCandidateReader reader = new UnavailableSettingsPrerequisiteProvider(
            new CompanyContext(Guid.Empty, true), new CurrentUser(true, Guid.Empty));

        Assert.Equal("settings_access_denied", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => reader.GetMoneyCurrencyCandidatesAsync())).Code);
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

        // Business defines both as the owner's real code and visible name, so neither may be null.
        var nullability = new NullabilityInfoContext();
        Assert.All(
            new[] { nameof(MoneyCurrencyCandidateV1.Code), nameof(MoneyCurrencyCandidateV1.DisplayLabel) },
            name => Assert.Equal(NullabilityState.NotNull,
                nullability.Create(typeof(MoneyCurrencyCandidateV1).GetProperty(name)!).ReadState));
    }

    [Fact]
    public void A_reachable_source_with_no_candidates_is_a_well_formed_empty_answer()
    {
        IReadOnlyList<MoneyCurrencyCandidateV1> none = [];

        Assert.Same(none, MoneyCurrencyCandidatesV1.EnsureWellFormed(none));
    }

    [Fact]
    public void A_well_formed_list_is_returned_as_given_without_reordering_or_a_default()
    {
        IReadOnlyList<MoneyCurrencyCandidateV1> candidates =
        [
            new(Guid.NewGuid(), "synthetic-code-b", "Synthetic label B"),
            new(Guid.NewGuid(), "synthetic-code-a", "Synthetic label A"),
        ];

        Assert.Same(candidates, MoneyCurrencyCandidatesV1.EnsureWellFormed(candidates));
    }

    [Theory]
    [InlineData("missing list")]
    [InlineData("missing row")]
    [InlineData("empty identity")]
    [InlineData("empty code")]
    [InlineData("whitespace code")]
    [InlineData("empty label")]
    [InlineData("whitespace label")]
    [InlineData("identity listed twice")]
    public void A_malformed_list_is_rejected_whole_as_unavailable_never_filtered(string malformation)
    {
        var id = Guid.NewGuid();
        var valid = new MoneyCurrencyCandidateV1(Guid.NewGuid(), "synthetic-code", "Synthetic label");
        IReadOnlyList<MoneyCurrencyCandidateV1>? candidates = malformation switch
        {
            "missing list" => null,
            "missing row" => [valid, null!],
            "empty identity" => [valid, valid with { CurrencyId = Guid.Empty }],
            "empty code" => [valid, new(id, "", "Synthetic label")],
            "whitespace code" => [valid, new(id, " \t", "Synthetic label")],
            "empty label" => [valid, new(id, "synthetic-code-2", "")],
            "whitespace label" => [valid, new(id, "synthetic-code-2", "  ")],
            "identity listed twice" => [new(id, "synthetic-code-1", "Label 1"), new(id, "synthetic-code-2", "Label 2")],
            _ => throw new ArgumentOutOfRangeException(nameof(malformation)),
        };

        // A valid row beside the bad one does not survive: the source is unavailable, not partly usable.
        Assert.Equal("settings_provider_unavailable", Assert.Throws<SettingsPrerequisiteException>(
            () => MoneyCurrencyCandidatesV1.EnsureWellFormed(candidates)).Code);
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
        // Finance and Product test doubles implement ISettingsPrerequisiteProvider; a new or
        // re-signed member there would break them. The candidate read is a separate port
        // instead. Whole signatures are pinned here; compiling those doubles remains the
        // cross-module guard.
        Assert.Equal(
            new[]
            {
                Signature(typeof(Task<MoneyProfileV1>), "GetMoneyProfileAsync",
                    typeof(Guid), typeof(string), typeof(DateOnly), typeof(CancellationToken)),
                Signature(typeof(Task<PeriodEligibilityV1>), "GetPeriodEligibilityAsync",
                    typeof(string), typeof(DateOnly), typeof(DateOnly?), typeof(CancellationToken)),
                Signature(typeof(Task<UnitProfileV1>), "GetUnitProfileAsync",
                    typeof(Guid), typeof(DateOnly), typeof(CancellationToken)),
            },
            typeof(ISettingsPrerequisiteProvider).GetMethods()
                .Select(m => Signature(m.ReturnType, m.Name, [.. m.GetParameters().Select(p => p.ParameterType)]))
                .Order(StringComparer.Ordinal));
        Assert.False(typeof(ISettingsPrerequisiteProvider).IsAssignableFrom(typeof(ISettingsMoneyCandidateReader)));
    }

    private static string Signature(Type returns, string name, params Type[] parameters) =>
        $"{returns} {name}({string.Join(", ", parameters.Select(p => p.ToString()))})";

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
