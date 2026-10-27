using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D27 (EK3): (2) the reference reads also answer a holder of the request or quote write name, beside their own binding —
/// never in place of a missing one, always in the context company and re-measured after the owner read; (1) the sales-owner
/// picker passes the server's eligibility binding to the Identity adapter and releases only a well-formed answer.
/// </summary>
public sealed partial class SalesReferenceReadHandlerTests
{
    private static readonly SalesReferenceReadBindings PoolBindings = new(
        SalesPermissionManifestV1.DraftsCreate, SalesPermissionManifestV1.DraftsCreate, SalesPermissionManifestV1.DraftsCreate,
        "controlled.operation",
        AlternativePermissions: [SalesPermissionManifestV1.RequestsWrite, SalesPermissionManifestV1.QuotesWrite],
        OwnerEligibilityPermission: SalesPermissionManifestV1.DraftsCreate);

    public static TheoryData<string> PoolWriteNames => new()
    {
        SalesPermissionManifestV1.RequestsWrite,
        SalesPermissionManifestV1.QuotesWrite,
        SalesPermissionManifestV1.DraftsCreate,
    };

    [Theory]
    [MemberData(nameof(PoolWriteNames))]
    public async Task Every_reference_read_answers_the_holder_of_one_write_name_alone(string held)
    {
        var f = new Fixture();
        f.Auth.Granted = [(held, f.Company)];
        var handler = SatD27Handler(f);

        Assert.Equal(200, (await handler.GetAccountsAsync(new(), "server-session")).StatusCode);
        Assert.Equal(200, (await handler.GetCurrencyAsync(f.CurrencyQuery)).StatusCode);
        Assert.Equal(200, (await handler.GetProductAsync(f.ProductQuery)).StatusCode);
        Assert.Equal(200, (await handler.GetContactInformationAsync(f.ContactQuery)).StatusCode);
        Assert.Equal(200, (await handler.GetCurrenciesAsync(f.CurrenciesQuery)).StatusCode);
        Assert.Equal(200, (await handler.GetSalesOwnersAsync(new(null, 10))).StatusCode);
    }

    [Theory]
    [InlineData("sales.requests.read")]
    [InlineData("sales.quotes.read")]
    [InlineData("sales.requests.approve")]
    [InlineData("sales.drafts.read")]
    public async Task A_read_or_decision_name_alone_opens_no_reference_read(string held)
    {
        var f = new Fixture();
        f.Auth.Granted = [(held, f.Company)];
        var owners = new Owners();
        var handler = SatD27Handler(f, owners);

        Assert.Equal(403, (await handler.GetAccountsAsync(new(), "server-session")).StatusCode);
        Assert.Equal(403, (await handler.GetCurrencyAsync(f.CurrencyQuery)).StatusCode);
        Assert.Equal(403, (await handler.GetProductAsync(f.ProductQuery)).StatusCode);
        Assert.Equal(403, (await handler.GetContactInformationAsync(f.ContactQuery)).StatusCode);
        Assert.Equal(403, (await handler.GetCurrenciesAsync(f.CurrenciesQuery)).StatusCode);
        Assert.Equal(403, (await handler.GetSalesOwnersAsync(new(null, 10))).StatusCode);
        Assert.Equal(0, f.Accounts.Calls + f.Settings.Calls + f.Products.Calls + f.ContactInformation.Calls + f.Candidates.Calls + owners.Calls);
    }

    [Fact]
    public async Task A_write_name_held_in_another_company_opens_nothing_in_the_context_company()
    {
        var f = new Fixture();
        var other = Guid.NewGuid();
        f.Auth.Granted = [(SalesPermissionManifestV1.RequestsWrite, other), (SalesPermissionManifestV1.QuotesWrite, other)];
        var owners = new Owners();
        var handler = SatD27Handler(f, owners);

        Assert.Equal(403, (await handler.GetAccountsAsync(new(), "server-session")).StatusCode);
        Assert.Equal(403, (await handler.GetProductAsync(f.ProductQuery)).StatusCode);
        Assert.Equal(403, (await handler.GetSalesOwnersAsync(new(null, 10))).StatusCode);
        Assert.Equal(0, f.Accounts.Calls + f.Products.Calls + owners.Calls);
    }

    [Fact]
    public async Task An_alternative_name_never_stands_in_for_a_missing_own_binding()
    {
        var f = new Fixture();
        f.Auth.Granted = [(SalesPermissionManifestV1.RequestsWrite, f.Company), (SalesPermissionManifestV1.QuotesWrite, f.Company)];
        var unbound = PoolBindings with { AccountsPermission = null, CurrencyPermission = null, ProductPermission = null };
        var handler = SatD27Handler(f, bindings: unbound);

        Assert.Equal(403, (await handler.GetAccountsAsync(new(), "server-session")).StatusCode);
        Assert.Equal(403, (await handler.GetCurrencyAsync(f.CurrencyQuery)).StatusCode);
        Assert.Equal(403, (await handler.GetProductAsync(f.ProductQuery)).StatusCode);
        Assert.Equal(403, (await handler.GetSalesOwnersAsync(new(null, 10))).StatusCode);
        Assert.Empty(f.Auth.Asked);
    }

    [Fact]
    public async Task A_write_name_lost_during_the_owner_read_discards_the_answer()
    {
        var f = new Fixture();
        f.Auth.Granted = [(SalesPermissionManifestV1.QuotesWrite, f.Company)];
        f.Accounts.OnRead = () => f.Auth.Granted.Clear();
        var result = await SatD27Handler(f).GetAccountsAsync(new(), "server-session");
        Assert.Equal(403, result.StatusCode);
        Assert.Null(result.Value);
    }

    [Fact]
    public void The_answerable_names_are_the_own_binding_first_then_the_distinct_alternatives()
    {
        Assert.Equal([SalesPermissionManifestV1.DraftsCreate, SalesPermissionManifestV1.RequestsWrite, SalesPermissionManifestV1.QuotesWrite],
            PoolBindings.AnswerableBy(SalesPermissionManifestV1.DraftsCreate));
        Assert.Empty(PoolBindings.AnswerableBy(null));
        Assert.Empty(PoolBindings.AnswerableBy(" "));
        Assert.Equal([SalesPermissionManifestV1.QuotesWrite, SalesPermissionManifestV1.RequestsWrite],
            PoolBindings.AnswerableBy(SalesPermissionManifestV1.QuotesWrite));
        Assert.Equal(["own"], new SalesReferenceReadBindings().AnswerableBy("own"));
    }

    // ---- SAT-D27 (1): the sales-owner picker ------------------------------------------------------------------------

    [Fact]
    public async Task The_picker_asks_the_identity_adapter_for_the_eligibility_name_in_the_context_company()
    {
        var f = new Fixture();
        var alice = Guid.NewGuid();
        var bora = Guid.NewGuid();
        var owners = new Owners { Answer = new(SalesOwnerCandidateOutcomeV1.Success, [new(alice, "Alice"), new(bora, "Bora")], true) };

        var result = await SatD27Handler(f, owners).GetSalesOwnersAsync(new("  ali ", 2));

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(new SalesOwnerCandidateRequestV1(f.Company, "actor", "ali", 2, SalesPermissionManifestV1.DraftsCreate), owners.Request);
        Assert.Equal(f.Company, result.Value!.CompanyId);
        Assert.Equal([new SalesOwnerOptionV1(alice, "Alice"), new SalesOwnerOptionV1(bora, "Bora")], result.Value.Owners);
        Assert.True(result.Value.HasMore);
    }

    [Fact]
    public async Task An_empty_picker_is_a_well_formed_answer()
    {
        var f = new Fixture();
        var owners = new Owners { Answer = new(SalesOwnerCandidateOutcomeV1.Success, [], false) };
        var result = await SatD27Handler(f, owners).GetSalesOwnersAsync(new(null, 25));
        Assert.Equal(200, result.StatusCode);
        Assert.Empty(result.Value!.Owners);
        Assert.Null(owners.Request!.Query);
    }

    [Theory]
    [InlineData(SalesOwnerCandidateOutcomeV1.Denied, 403)]
    [InlineData(SalesOwnerCandidateOutcomeV1.Unavailable, 503)]
    public async Task The_owners_refusal_is_never_an_empty_list(SalesOwnerCandidateOutcomeV1 outcome, int status)
    {
        var f = new Fixture();
        var owners = new Owners { Answer = new(outcome, [], false) };
        var result = await SatD27Handler(f, owners).GetSalesOwnersAsync(new(null, 25));
        Assert.Equal((status, null), (result.StatusCode, result.Value));
    }

    [Theory]
    [InlineData("empty-id")]
    [InlineData("blank-name")]
    [InlineData("duplicate")]
    [InlineData("over-limit")]
    [InlineData("null-items")]
    public async Task A_malformed_owner_answer_is_unavailable(string fault)
    {
        var f = new Fixture();
        var id = Guid.NewGuid();
        IReadOnlyList<SalesOwnerOptionV1> items = fault switch
        {
            "empty-id" => [new(Guid.Empty, "Alice")],
            "blank-name" => [new(id, " ")],
            "duplicate" => [new(id, "Alice"), new(id, "Alice")],
            "over-limit" => [new(id, "Alice"), new(Guid.NewGuid(), "Bora"), new(Guid.NewGuid(), "Cem")],
            _ => null!,
        };
        var owners = new Owners { Answer = new(SalesOwnerCandidateOutcomeV1.Success, items, false) };
        var result = await SatD27Handler(f, owners).GetSalesOwnersAsync(new(null, 2));
        Assert.Equal((503, null), (result.StatusCode, result.Value));
    }

    [Fact]
    public async Task A_missing_eligibility_binding_or_source_is_a_setup_problem_never_everyone()
    {
        var f = new Fixture();
        var owners = new Owners();
        Assert.Equal(503, (await SatD27Handler(f, owners, PoolBindings with { OwnerEligibilityPermission = null })
            .GetSalesOwnersAsync(new(null, 25))).StatusCode);
        Assert.Equal(503, (await SatD27Handler(f, owners, PoolBindings with { OwnerEligibilityPermission = " " })
            .GetSalesOwnersAsync(new(null, 25))).StatusCode);
        Assert.Equal(0, owners.Calls);
        Assert.Equal(503, (await new SalesReferenceReadHandler(f.Context, f.User, f.Auth, f.Accounts, f.Settings, f.Products, PoolBindings)
            .GetSalesOwnersAsync(new(null, 25))).StatusCode);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(51, null)]
    [InlineData(10, "x")]
    public async Task The_picker_bounds_are_checked_before_the_owner_is_asked(int limit, string? fragment)
    {
        var f = new Fixture();
        var owners = new Owners();
        var query = fragment is null ? null : new string('a', SalesOwnerPickerLimitsV1.MaximumQueryLength + 1);
        var result = await SatD27Handler(f, owners).GetSalesOwnersAsync(new(query, limit));
        Assert.Equal((400, "validation_failed"), (result.StatusCode, result.Code));
        Assert.Equal(0, owners.Calls);
    }

    [Fact]
    public async Task The_picker_answer_is_discarded_when_the_authority_is_lost_or_the_company_moves_during_the_owner_read()
    {
        var f = new Fixture();
        f.Auth.Granted = [(SalesPermissionManifestV1.RequestsWrite, f.Company)];
        var owners = new Owners
        {
            Answer = new(SalesOwnerCandidateOutcomeV1.Success, [new(Guid.NewGuid(), "Alice")], false),
            OnRead = () => f.Auth.Granted.Clear(),
        };
        Assert.Equal(403, (await SatD27Handler(f, owners).GetSalesOwnersAsync(new(null, 25))).StatusCode);

        var g = new Fixture();
        var moved = new Owners
        {
            Answer = new(SalesOwnerCandidateOutcomeV1.Success, [new(Guid.NewGuid(), "Alice")], false),
            OnRead = () => g.Context.Set(Guid.NewGuid()),
        };
        Assert.Equal(403, (await SatD27Handler(g, moved).GetSalesOwnersAsync(new(null, 25))).StatusCode);
    }

    private static SalesReferenceReadHandler SatD27Handler(Fixture f, Owners? owners = null, SalesReferenceReadBindings? bindings = null) =>
        new(f.Context, f.User, f.Auth, f.Accounts, f.Settings, f.Products, bindings ?? PoolBindings,
            f.Candidates, f.ContactInformation, null,
            owners ?? new Owners { Answer = new(SalesOwnerCandidateOutcomeV1.Success, [], false) });

    private sealed class Owners : ISalesOwnerCandidateSourceV1
    {
        public int Calls { get; private set; }
        public SalesOwnerCandidateRequestV1? Request { get; private set; }
        public Action? OnRead { get; init; }
        public SalesOwnerCandidateAnswerV1 Answer { get; init; } = new(SalesOwnerCandidateOutcomeV1.Unavailable, [], false);
        public Task<SalesOwnerCandidateAnswerV1> SearchAsync(SalesOwnerCandidateRequestV1 request, CancellationToken cancellationToken = default)
        { Calls++; Request = request; OnRead?.Invoke(); return Task.FromResult(Answer); }
    }
}
