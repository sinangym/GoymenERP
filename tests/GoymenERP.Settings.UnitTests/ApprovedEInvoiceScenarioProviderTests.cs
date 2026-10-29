using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>
/// SAT-D102: the Settings-owned e-Fatura scenario keys. The reviewed catalog is EMPTY on purpose (the default starts
/// empty and Sinan enters it; no code list is decided), so every real company answers "not found" for both keys.
/// The codes in the synthetic catalogs below exist only to exercise validation and windows; they are not a policy.
/// </summary>
public sealed class ApprovedEInvoiceScenarioProviderTests
{
    private static readonly Guid Gdc = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Gsp = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid Gin = Guid.Parse("10000000-0000-0000-0000-000000000003");
    private static readonly Guid SyntheticList = Guid.Parse("71000000-0000-4000-8000-0000000000b1");
    private static readonly Guid SyntheticDefault = Guid.Parse("71000000-0000-4000-8000-0000000000b2");
    private static readonly DateOnly Start = new(2026, 11, 1);

    [Fact]
    public void The_reviewed_catalog_is_empty_no_code_and_no_default_is_seeded()
    {
        Assert.Empty(ApprovedEInvoiceScenarioCatalog.Approved.CodeLists);
        Assert.Empty(ApprovedEInvoiceScenarioCatalog.Approved.Defaults);
    }

    [Theory]
    [InlineData("10000000-0000-0000-0000-000000000001")]
    [InlineData("10000000-0000-0000-0000-000000000002")]
    [InlineData("10000000-0000-0000-0000-000000000003")]
    public async Task Every_company_has_the_keys_and_both_answer_not_found_until_Sinan_enters_them(string company)
    {
        var provider = new ApprovedSettingsPrerequisiteProvider(
            new Context(Guid.Parse(company), true), new Actor(true, "x", Guid.Parse(company)));

        var list = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetEInvoiceScenarioCodesAsync(Start));
        var defaultValue = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetEInvoiceScenarioDefaultAsync(Start));

        Assert.Equal(SettingsPrerequisiteCodesV1.EInvoiceScenarioCodeListNotFound, list.Code);
        Assert.Equal(SettingsPrerequisiteCodesV1.EInvoiceScenarioDefaultNotFound, defaultValue.Code);
        Assert.Contains(Guid.Parse(company), new[] { Gdc, Gsp, Gin });
    }

    [Theory]
    [InlineData("unauthenticated")]
    [InlineData("no actor id")]
    [InlineData("unresolved company")]
    [InlineData("not a member")]
    public async Task Access_is_checked_before_anything_about_the_source_is_revealed(string reason)
    {
        var provider = reason switch
        {
            "unauthenticated" => Provider(new Context(Gdc, true), new Actor(false, "x", Gdc)),
            "no actor id" => Provider(new Context(Gdc, true), new Actor(true, " ", Gdc)),
            "unresolved company" => Provider(new Context(Gdc, false), new Actor(true, "x", Gdc)),
            _ => Provider(new Context(Gdc, true), new Actor(true, "x", Gsp)),
        };

        var list = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => provider.GetEInvoiceScenarioCodesAsync(Start));
        var defaultValue = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetEInvoiceScenarioDefaultAsync(Start));
        Assert.Equal("settings_access_denied", list.Code);
        Assert.Equal("settings_access_denied", defaultValue.Code);
    }

    [Fact]
    public async Task A_reviewed_list_and_default_are_served_for_their_own_company_and_window_only()
    {
        var provider = Provider(new Context(Gdc, true), new Actor(true, "x", Gdc));

        var list = await provider.GetEInvoiceScenarioCodesAsync(Start);
        Assert.Equal(Gdc, list.CompanyId);
        Assert.Equal(new ProfileVersionV1(SyntheticList, 1), list.Profile);
        Assert.Equal(["CODE_A", "CODE_B"], list.Codes);
        var defaultValue = await provider.GetEInvoiceScenarioDefaultAsync(Start.AddDays(30));
        Assert.Equal("CODE_B", defaultValue.ScenarioCode);
        Assert.Equal(new ProfileVersionV1(SyntheticDefault, 1), defaultValue.Profile);

        // Before the window: not found, never the nearest version.
        var early = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetEInvoiceScenarioCodesAsync(Start.AddDays(-1)));
        Assert.Equal(SettingsPrerequisiteCodesV1.EInvoiceScenarioCodeListNotFound, early.Code);
        var earlyDefault = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetEInvoiceScenarioDefaultAsync(Start.AddDays(-1)));
        Assert.Equal(SettingsPrerequisiteCodesV1.EInvoiceScenarioDefaultNotFound, earlyDefault.Code);

        // Another company sees nothing of GDC's keys.
        var other = Provider(new Context(Gsp, true), new Actor(true, "x", Gsp));
        var foreign = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => other.GetEInvoiceScenarioCodesAsync(Start));
        Assert.Equal(SettingsPrerequisiteCodesV1.EInvoiceScenarioCodeListNotFound, foreign.Code);
    }

    [Fact]
    public async Task A_default_whose_code_a_later_list_drops_is_never_served()
    {
        var catalog = new ApprovedEInvoiceScenarioCatalog(
            [
                List(["CODE_A", "CODE_B"], Start, Start.AddDays(9), version: 1),
                List(["CODE_A"], Start.AddDays(10), null, version: 2),
            ],
            [Default("CODE_B", Start)]);
        var provider = new ApprovedSettingsPrerequisiteProvider(
            new Context(Gdc, true), new Actor(true, "x", Gdc), catalog);

        Assert.Equal("CODE_B", (await provider.GetEInvoiceScenarioDefaultAsync(Start)).ScenarioCode);
        var invalid = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetEInvoiceScenarioDefaultAsync(Start.AddDays(10)));
        Assert.Equal("settings_profile_invalid", invalid.Code);
    }

    public static TheoryData<string> MalformedCatalogs { get; } = new()
    {
        "empty list", "repeated code", "lower-case code", "too long code", "digit first", "overlapping windows",
        "repeated version", "blank source", "default not listed", "default without list", "window ends before start",
    };

    [Theory]
    [MemberData(nameof(MalformedCatalogs))]
    public void A_malformed_catalog_is_refused_as_a_whole(string reason)
    {
        var failure = Assert.Throws<SettingsPrerequisiteException>(() => reason switch
        {
            "empty list" => new ApprovedEInvoiceScenarioCatalog([List([], Start)], []),
            "repeated code" => new ApprovedEInvoiceScenarioCatalog([List(["CODE_A", "CODE_A"], Start)], []),
            "lower-case code" => new ApprovedEInvoiceScenarioCatalog([List(["code_a"], Start)], []),
            "too long code" => new ApprovedEInvoiceScenarioCatalog([List([new string('A', 33)], Start)], []),
            "digit first" => new ApprovedEInvoiceScenarioCatalog([List(["1CODE"], Start)], []),
            "overlapping windows" => new ApprovedEInvoiceScenarioCatalog(
                [List(["CODE_A"], Start, version: 1), List(["CODE_A"], Start.AddDays(5), version: 2)], []),
            "repeated version" => new ApprovedEInvoiceScenarioCatalog(
                [List(["CODE_A"], Start, Start.AddDays(1), version: 1), List(["CODE_A"], Start.AddDays(2), version: 1)], []),
            "blank source" => new ApprovedEInvoiceScenarioCatalog([List(["CODE_A"], Start) with { Source = " " }], []),
            "default not listed" => new ApprovedEInvoiceScenarioCatalog([List(["CODE_A"], Start)], [Default("CODE_B", Start)]),
            "default without list" => new ApprovedEInvoiceScenarioCatalog([], [Default("CODE_A", Start)]),
            _ => new ApprovedEInvoiceScenarioCatalog([List(["CODE_A"], Start, Start.AddDays(-1))], []),
        });
        Assert.Equal("settings_profile_invalid", failure.Code);
    }

    [Theory]
    [InlineData("TEMELFATURA", true)]
    [InlineData("A", true)]
    [InlineData("CODE_9", true)]
    [InlineData("", false)]
    [InlineData("temel", false)]
    [InlineData("_CODE", false)]
    [InlineData("İHRACAT", false)]
    [InlineData("CODE-1", false)]
    public void The_code_format_is_lexical_only(string code, bool expected) =>
        Assert.Equal(expected, EInvoiceScenarioCodeFormatV1.IsWellFormed(code));

    [Fact]
    public async Task Cancellation_is_honoured_before_any_answer()
    {
        var provider = Provider(new Context(Gdc, true), new Actor(true, "x", Gdc));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetEInvoiceScenarioCodesAsync(Start, new CancellationToken(true)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetEInvoiceScenarioDefaultAsync(Start, new CancellationToken(true)));
    }

    private static ApprovedSettingsPrerequisiteProvider Provider(ICompanyContext context, ICurrentUser actor) =>
        new(context, actor, new ApprovedEInvoiceScenarioCatalog(
            [List(["CODE_A", "CODE_B"], Start)],
            [Default("CODE_B", Start)]));

    private static ApprovedEInvoiceScenarioCodeListEntry List(
        string[] codes, DateOnly from, DateOnly? through = null, long version = 1) =>
        new(Gdc, SyntheticList, version, codes, from, through, "synthetic Settings unit test only");

    private static ApprovedEInvoiceScenarioDefaultEntry Default(string code, DateOnly from) =>
        new(Gdc, SyntheticDefault, 1, code, from, null, "synthetic Settings unit test only");

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
