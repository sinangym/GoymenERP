using GoymenERP.Api.ProductRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

// Synthetic owner-contract tests only; actual Customer-role query acceptance is in the SQL fact source.
public sealed class CounterpartyProductPackageStandardCustomerProofTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Party = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Account = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Other = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private const string Actor = "SENTETIK-package-actor";
    private static CounterpartyProductPackageStandardCustomerV1 Customer() => new(Company, Party, Account, "17", "23", true, true, true,
        "CAR-990011", "SENTETIK resmi müşteri", "SENTETIK ticari müşteri");
    private static CounterpartyProductPackageStandardCustomerAnswerV1 Found(params CounterpartyProductPackageStandardCustomerV1[] values) =>
        new(CounterpartyProductPackageStandardCustomerOutcomeV1.Found, Company, Actor, values, CounterpartyProductPackageStandardCustomerContractV1.SourceVersion);

    [Fact]
    public async Task Actual_customer_proof_is_projected_without_account_roles_or_financial_metadata()
    {
        var owner = new Owner { Answer = Found(Customer()) }; var services = new Services(owner);
        var answer = await Port(services).ReadAsync([Party]);
        Assert.Equal(ProductPackageStandardCustomerOutcomeV3.Found, answer.Outcome);
        Assert.Equal(new ProductPackageStandardCustomerV3(Party, "CAR-990011", "SENTETIK ticari müşteri"), Assert.Single(answer.Customers));
        Assert.Equal((Company, Actor), (owner.Request!.CompanyId, owner.Request.ActorId)); Assert.Equal(new[] { Party }, owner.Request.PartyIds);
        Assert.Equal(new[] { typeof(ICounterpartyProductPackageStandardCustomerSourceV1) }, services.Lookups);
    }

    [Fact]
    public async Task Missing_trade_name_uses_actual_legal_name_and_genuine_empty_stays_empty()
    {
        var owner = new Owner { Answer = Found(Customer() with { TradeName = null }) };
        Assert.Equal(Customer().LegalName, Assert.Single((await Port(new(owner)).ReadAsync([Party])).Customers).Name);
        owner.Answer = Found();
        var empty = await Port(new(owner)).ReadAsync([]);
        Assert.Equal(ProductPackageStandardCustomerOutcomeV3.Found, empty.Outcome); Assert.Empty(empty.Customers);
    }

    // Serializable case labels let TestSDK enumerate every negative row before execution.
    // Keep the original twelve malformed owner records; never relax exact discovery counts.
    [Theory]
    [InlineData("foreign-company")]
    [InlineData("foreign-party")]
    [InlineData("empty-account")]
    [InlineData("inactive-party")]
    [InlineData("inactive-account")]
    [InlineData("missing-customer-role")]
    [InlineData("zero-party-version")]
    [InlineData("noncanonical-account-version")]
    [InlineData("overflow-account-version")]
    [InlineData("supplier-display-code")]
    [InlineData("empty-legal-name")]
    [InlineData("control-character-trade-name")]
    public async Task Found_without_actual_bound_active_customer_evidence_is_unavailable(string kind)
    {
        var value = kind switch
        {
            "foreign-company" => Customer() with { CompanyId = Other },
            "foreign-party" => Customer() with { PartyId = Other },
            "empty-account" => Customer() with { CompanyAccountId = Guid.Empty },
            "inactive-party" => Customer() with { PartyIsActive = false },
            "inactive-account" => Customer() with { AccountIsActive = false },
            "missing-customer-role" => Customer() with { HasCustomerRole = false },
            "zero-party-version" => Customer() with { PartyVersion = "0" },
            "noncanonical-account-version" => Customer() with { AccountVersion = "01" },
            "overflow-account-version" => Customer() with { AccountVersion = "9223372036854775808" },
            "supplier-display-code" => Customer() with { Code = "supplier-display-only" },
            "empty-legal-name" => Customer() with { LegalName = "" },
            "control-character-trade-name" => Customer() with { TradeName = "\nSENTETIK" },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var answer = await Port(new(new Owner { Answer = Found(value) })).ReadAsync([Party]);
        Assert.Equal(ProductPackageStandardCustomerOutcomeV3.Unavailable, answer.Outcome); Assert.Empty(answer.Customers);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("actor")]
    [InlineData("source")]
    [InlineData("duplicate")]
    [InlineData("unknown-outcome")]
    public async Task Malformed_owner_frame_or_duplicate_results_never_become_partial_success(string kind)
    {
        var owner = new Owner { Answer = kind switch {
            "company" => Found(Customer()) with { CompanyId = Other }, "actor" => Found(Customer()) with { ActorId = "SENTETIK-other" },
            "source" => Found(Customer()) with { SourceVersion = "display-only" }, "duplicate" => Found(Customer(), Customer()),
            _ => Found(Customer()) with { Outcome = (CounterpartyProductPackageStandardCustomerOutcomeV1)99 },
        } };
        var answer = await Port(new(owner)).ReadAsync([Party]);
        Assert.Equal(ProductPackageStandardCustomerOutcomeV3.Unavailable, answer.Outcome); Assert.Empty(answer.Customers);
    }

    [Fact]
    public async Task A_missing_owner_is_unavailable_and_never_falls_back_to_display_or_Manufacturing()
    {
        var services = new Services(null); var answer = await Port(services).ReadAsync([Party]);
        Assert.Equal(ProductPackageStandardCustomerOutcomeV3.Unavailable, answer.Outcome); Assert.Empty(answer.Customers);
        Assert.Equal(new[] { typeof(ICounterpartyProductPackageStandardCustomerSourceV1) }, services.Lookups);
    }

    [Fact]
    public async Task Invalid_requested_ids_are_refused_without_normalizing_them_into_another_request()
    {
        var owner = new Owner(); var port = Port(new(owner));
        foreach (var ids in new Guid[][] { [Party, Party], [Guid.Empty], Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray() })
        {
            var answer = await port.ReadAsync(ids); Assert.Equal(ProductPackageStandardCustomerOutcomeV3.Denied, answer.Outcome); Assert.Empty(answer.Customers);
        }
        Assert.Equal(0, owner.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Company_or_actor_drift_after_owner_await_discards_all_evidence(bool changeCompany)
    {
        var context = new Context(); var user = new User(); var owner = new Owner { Answer = Found(Customer()),
            After = () => { if (changeCompany) context.Set(Other); else user.UserId = "SENTETIK-other"; } };
        var answer = await new CounterpartyProductPackageStandardCustomerPortV3(new Services(owner), context, user).ReadAsync([Party]);
        Assert.Equal(ProductPackageStandardCustomerOutcomeV3.Denied, answer.Outcome); Assert.Empty(answer.Customers);
    }

    [Fact]
    public async Task Owner_fault_is_unavailable_and_cancellation_before_or_after_await_propagates()
    {
        var owner = new Owner { Error = new InvalidOperationException("SENTETIK owner fault") };
        Assert.Equal(ProductPackageStandardCustomerOutcomeV3.Unavailable, (await Port(new(owner)).ReadAsync([Party])).Outcome);
        using var before = new CancellationTokenSource(); before.Cancel(); var calls = owner.Calls;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Port(new(owner)).ReadAsync([Party], before.Token)); Assert.Equal(calls, owner.Calls);
        using var after = new CancellationTokenSource(); owner.Error = null; owner.Answer = Found(Customer()); owner.After = after.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Port(new(owner)).ReadAsync([Party], after.Token));
    }

    private static CounterpartyProductPackageStandardCustomerPortV3 Port(Services services) => new(services, new Context(), new User());
    private sealed class Services(Owner? owner) : IServiceProvider
    {
        public List<Type> Lookups { get; } = [];
        public object? GetService(Type serviceType) { Lookups.Add(serviceType); return serviceType == typeof(ICounterpartyProductPackageStandardCustomerSourceV1) ? owner : null; }
    }
    private sealed class Owner : ICounterpartyProductPackageStandardCustomerSourceV1
    {
        public CounterpartyProductPackageStandardCustomerAnswerV1 Answer { get; set; } = Found();
        public Action? After { get; set; } public Exception? Error { get; set; } public int Calls { get; private set; }
        public CounterpartyProductPackageStandardCustomerRequestV1? Request { get; private set; }
        public Task<CounterpartyProductPackageStandardCustomerAnswerV1> ReadAuthorizedAsync(CounterpartyProductPackageStandardCustomerRequestV1 request, CancellationToken cancellationToken = default)
        { Calls++; Request = request; After?.Invoke(); return Error is null ? Task.FromResult(Answer) : Task.FromException<CounterpartyProductPackageStandardCustomerAnswerV1>(Error); }
    }
    private sealed class Context : ICompanyContext
    { public bool IsResolved => true; public Guid CompanyId { get; private set; } = Company; public void Set(Guid companyId) => CompanyId = companyId; }
    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true; public string? UserId { get; set; } = Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company, Other };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
