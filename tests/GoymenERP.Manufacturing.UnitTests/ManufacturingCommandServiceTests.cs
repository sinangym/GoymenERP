using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// MFG-API command surface without a database or HTTP: request grammar, the Settings mass check,
/// the fresh authority fence and the envelope it builds, and the mapping of every refusal to one
/// status. What SQL and the host do is proven by the relational HTTP acceptance tests.
/// </summary>
public sealed class ManufacturingCommandServiceTests
{
    private const string Permission = "manufacturing.facts.record";
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Actor = Guid.Parse("a1000000-0000-0000-0000-00000000000a");
    private static readonly Guid WorkCenter = Guid.Parse("c1000000-0000-0000-0000-00000000000c");
    private static readonly Guid Roll = Guid.Parse("b1000000-0000-0000-0000-00000000000b");
    private static readonly DateTimeOffset At = new(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("1e3")]
    [InlineData("-1")]
    [InlineData("1,5")]
    [InlineData(" 10")]
    [InlineData("10.")]
    [InlineData("+10")]
    [InlineData("0010")]
    [InlineData("")]
    public async Task A_kilogram_outside_plain_decimal_text_is_invalid_and_nothing_is_asked_or_stored(string text)
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();

        var result = await fixture.Service.IssueInputAsync(order.Id, Fact(order.Version, text), "key-1", Permission,
            CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, ManufacturingApiCodes.RequestInvalid, "kg"),
            (result.Status, result.Code, result.Field));
        Assert.Equal(0, fixture.Settings.Asked);
        Assert.Empty(fixture.Store.Writes);
    }

    [Theory]
    [InlineData("settings_access_denied", ManufacturingApiStatus.Denied)]
    [InlineData("settings_provider_unavailable", ManufacturingApiStatus.Unavailable)]
    [InlineData("settings_profile_invalid", ManufacturingApiStatus.Unavailable)]
    [InlineData(SettingsPrerequisiteCodesV1.UnitProfileNotFound, ManufacturingApiStatus.Unprocessable)]
    public async Task A_settings_refusal_keeps_its_meaning_and_nothing_is_stored(string code, ManufacturingApiStatus expected)
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        fixture.Settings.Refusal = code;

        var result = await fixture.Service.IssueInputAsync(order.Id, Fact(order.Version, "10"), "key-1", Permission,
            CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Equal(code, result.Code);
        Assert.Empty(fixture.Store.Writes);
        Assert.Equal(0, fixture.Authority.Asked);
    }

    [Fact]
    public async Task More_decimals_than_the_profile_allows_is_refused_never_rounded()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();

        var result = await fixture.Service.IssueInputAsync(order.Id, Fact(order.Version, "10.0001"), "key-1", Permission,
            CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unprocessable, ManufacturingMassCodes.ScaleExceeded),
            (result.Status, result.Code));
        Assert.Empty(fixture.Store.Writes);
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("site-scoped")]
    [InlineData("company-lost-access")]
    public async Task Only_a_fresh_company_wide_grant_writes(string fault)
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        switch (fault)
        {
            case "revoked":
                fixture.Authority.Scope = PermissionScope.Denied;
                break;
            case "site-scoped":
                fixture.Authority.Scope = Scope(companyWide: false);
                break;
            default:
                fixture.User.Companies.Clear();
                break;
        }

        var result = await fixture.Service.IssueInputAsync(order.Id, Fact(order.Version, "10"), "key-1", Permission,
            CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Denied, ManufacturingApiCodes.AuthorityDenied), (result.Status, result.Code));
        Assert.Empty(fixture.Store.Writes);
    }

    [Fact]
    public async Task The_envelope_is_the_servers_and_the_authority_is_measured_after_the_settings_read()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        fixture.Authority.Scope = Scope(companyWide: true, "PRODUCTION_OPERATOR", "AUDITOR");

        var result = await fixture.Service.IssueInputAsync(order.Id, Fact(order.Version, "10.500"), " key-1 ".Trim(),
            Permission, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        Assert.Equal(new[] { "settings", "authority" }, fixture.Calls);
        var metadata = Assert.Single(fixture.Store.Writes);
        Assert.Equal((Actor.ToString("D"), Permission, "corr-1", "key-1", "Web"),
            (metadata.ActorId, metadata.PermissionCode, metadata.CorrelationId, metadata.IdempotencyKey, metadata.SourceChannel));
        Assert.Equal(new[] { "AUDITOR", "PRODUCTION_OPERATOR" }, metadata.RoleCodes);
        Assert.Equal((Permission, Company), fixture.Authority.LastAsked);

        // The view: lossless text, no trailing zeros, and the roll's balance from the facts.
        var roll = Assert.Single(result.Resource!.Rolls);
        Assert.Equal(("10.5", "10.5", "0", "10.5"), (roll.IssuedKg, roll.NetIssuedKg, roll.WorkInProgressKg, roll.UnexplainedKg));
        Assert.False(roll.IsFullyAccounted);
        Assert.Equal("NotCalculated", result.Resource.CostStatus);
    }

    [Fact]
    public async Task A_fact_time_must_be_utc()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        var local = new RecordFactRequestV1(Guid.NewGuid(), order.Version, Roll, "10", new DateTimeOffset(2026, 9, 18, 13, 0, 0,
            TimeSpan.FromHours(3)));

        var result = await fixture.Service.IssueInputAsync(order.Id, local, "key-1", Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, "occurredAtUtc"), (result.Status, result.Field));
        Assert.Empty(fixture.Store.Writes);
    }

    [Theory]
    [InlineData("scrap")]
    [InlineData("4")]
    [InlineData("GoodProduct ")]
    [InlineData(null)]
    public async Task An_output_class_is_only_one_of_the_five_names(string? outputClass)
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();

        var result = await fixture.Service.DeclareOutputAsync(order.Id,
            new DeclareOutputRequestV1(Guid.NewGuid(), order.Version, Roll, outputClass, "1", At), "key-1", Permission,
            CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, "outputClass"), (result.Status, result.Field));
    }

    [Fact]
    public async Task Refusals_map_to_one_status_each()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();

        // A stale version: 409 with both versions.
        var stale = await fixture.Service.IssueInputAsync(order.Id, Fact(order.Version + 5, "10"), "k", Permission,
            CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Conflict, ManufacturingRuleCodes.VersionConflict, order.Version + 5, order.Version),
            (stale.Status, stale.Code, stale.ExpectedVersion, stale.CurrentVersion));

        // A domain rule: 422 with its code (a roll the order does not have).
        var foreignRoll = new RecordFactRequestV1(Guid.NewGuid(), order.Version, Guid.NewGuid(), "10", At);
        var rule = await fixture.Service.IssueInputAsync(order.Id, foreignRoll, "k", Permission, CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Unprocessable, ManufacturingRuleCodes.RollNotInOrder), (rule.Status, rule.Code));

        // A store refusal: another company's or an unknown order is 404, never a hint.
        var unknown = await fixture.Service.IssueInputAsync(Guid.NewGuid(), Fact(1, "10"), "k", Permission,
            CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.NotFound, unknown.Status);

        fixture.Store.Fail = ManufacturingStoreCodes.FactIdConflict;
        var conflict = await fixture.Service.IssueInputAsync(order.Id, Fact(order.Version, "10"), "k", Permission,
            CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Conflict, ManufacturingStoreCodes.FactIdConflict), (conflict.Status, conflict.Code));

        fixture.Store.Fail = ManufacturingStoreCodes.ValueDoesNotFit;
        var tooLarge = await fixture.Service.IssueInputAsync(order.Id, Fact(order.Version, "10"), "k", Permission,
            CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Unprocessable, ManufacturingStoreCodes.ValueDoesNotFit),
            (tooLarge.Status, tooLarge.Code));
    }

    [Fact]
    public async Task A_plan_is_saved_for_the_context_company_at_the_servers_date_with_its_declared_target()
    {
        var fixture = new Fixture();
        var request = new SavePlanRequestV1(Guid.NewGuid(), WorkCenter,
            [new PlannedInputRequestV1(Roll, "1000.250")], new TargetRequestV1("GoodOutputMass", "950"));

        var result = await fixture.Service.SavePlanAsync(request, "key-1", "manufacturing.plans.write", CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Created, result.Status);
        Assert.Equal((Company, WorkCenter, "Slitting", "Draft", 1L), (result.Resource!.CompanyId, result.Resource.WorkCenterId,
            result.Resource.Process, result.Resource.Status, result.Resource.Version));
        Assert.Equal("1000.25", Assert.Single(result.Resource.Inputs).PlannedKg);
        Assert.Equal(("GoodOutputMass", "950"), (result.Resource.Target!.Basis, result.Resource.Target.Kg));
        Assert.Equal(new[] { DateOnly.FromDateTime(At.UtcDateTime), DateOnly.FromDateTime(At.UtcDateTime) }, fixture.Settings.Days);
    }

    [Theory]
    [InlineData("inputMass")]
    [InlineData("1")]
    [InlineData("Unknown")]
    public async Task A_target_basis_is_only_its_own_name(string basis)
    {
        var fixture = new Fixture();
        var request = new SavePlanRequestV1(Guid.NewGuid(), WorkCenter, [new PlannedInputRequestV1(Roll, "10")],
            new TargetRequestV1(basis, "5"));

        var result = await fixture.Service.SavePlanAsync(request, "key-1", "manufacturing.plans.write", CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, "target.basis"), (result.Status, result.Field));
        Assert.Empty(fixture.Store.Writes);
    }

    [Fact]
    public async Task A_zero_planned_mass_is_the_domains_caller_error_behind_the_fence()
    {
        var fixture = new Fixture();
        var request = new SavePlanRequestV1(Guid.NewGuid(), WorkCenter, [new PlannedInputRequestV1(Roll, "0")], null);

        var result = await fixture.Service.SavePlanAsync(request, "key-1", "manufacturing.plans.write", CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Invalid, result.Status);
        Assert.Empty(fixture.Store.Writes);
    }

    [Fact]
    public async Task A_read_without_the_company_context_is_denied()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        fixture.User.Companies.Clear();

        Assert.Equal(ManufacturingApiStatus.Denied, (await fixture.Service.GetOrderAsync(order.Id, CancellationToken.None)).Status);
    }

    private static RecordFactRequestV1 Fact(long version, string kg) => new(Guid.NewGuid(), version, Roll, kg, At);

    private static PermissionScope Scope(bool companyWide, params string[] roles) => new(true, companyWide,
        new HashSet<string>(roles, StringComparer.Ordinal), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

    private sealed class Fixture
    {
        public Fixture()
        {
            var company = new CompanyContext();
            company.Set(Company);
            var correlation = new CorrelationContext();
            correlation.Set("corr-1", null, "Web", null, null);
            Settings = new SettingsPort(Calls);
            Authority = new AuthorityPort(Calls) { Scope = Scope(companyWide: true, "PRODUCTION_OPERATOR") };
            Service = new ManufacturingCommandService(Store, Store, new ManufacturingMassVerifier(Settings), Authority, company,
                User, correlation, new FixedTime());
        }

        public List<string> Calls { get; } = [];

        public FakeStore Store { get; } = new();

        public SettingsPort Settings { get; }

        public AuthorityPort Authority { get; }

        public User User { get; } = new();

        public ManufacturingCommandService Service { get; }

        public async Task<ProductionOrder> ApprovedOrderAsync()
        {
            var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
                [new PlannedInput(Roll, new Kilograms(1000m))]);
            plan.Approve(plan.Version, Actor, At);
            var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
            Store.Orders[order.Id] = order;
            await Task.CompletedTask;
            return order;
        }
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => At;
    }

    private sealed class User : ICurrentUser
    {
        public HashSet<Guid> Companies { get; } = [Company];

        public bool IsAuthenticated => true;

        public string? UserId => Actor.ToString("D");

        public IReadOnlySet<Guid> AllowedCompanyIds => Companies;

        public bool CanAccessCompany(Guid companyId) => Companies.Contains(companyId);
    }

    private sealed class AuthorityPort(List<string> calls) : IErpAuthorityRevalidationService
    {
        public PermissionScope Scope { get; set; } = PermissionScope.Denied;

        public int Asked { get; private set; }

        public (string Permission, Guid Company) LastAsked { get; private set; }

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Asked++;
            calls.Add("authority");
            LastAsked = (permission, companyId);
            return Task.FromResult(Scope);
        }
    }

    private sealed class SettingsPort(List<string> calls) : ISettingsPrerequisiteProvider
    {
        public string? Refusal { get; set; }

        public int Asked { get; private set; }

        public List<DateOnly> Days { get; } = [];

        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default)
        {
            Asked++;
            Days.Add(effectiveOn);
            if (!calls.Contains("settings"))
            {
                calls.Add("settings");
            }

            if (Refusal is { } code)
            {
                throw new SettingsPrerequisiteException(code);
            }

            return Task.FromResult(new UnitProfileV1(Company, SettingsUnitIdentitiesV1.Kilogram, "kg", "mass", true,
                new ProfileVersionV1(Guid.Parse("4a790fdc-2a6b-5126-835f-d3a13d28b71c"), 1), 3, true,
                new DateOnly(2026, 9, 18), null));
        }

        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>An in-memory store with the store's contract: the company's records, the domain's rules.</summary>
    private sealed class FakeStore : IManufacturingPlanStore, IManufacturingOrderStore
    {
        public Dictionary<Guid, ProductionPlan> Plans { get; } = [];

        public Dictionary<Guid, ProductionOrder> Orders { get; } = [];

        public List<ManufacturingCommandMetadata> Writes { get; } = [];

        public string? Fail { get; set; }

        public Task<ManufacturingWriteOutcome> SavePlanAsync(ProductionPlan plan, ManufacturingCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            Guard();
            Writes.Add(metadata);
            Plans[plan.Id] = plan;
            return Task.FromResult(ManufacturingWriteOutcome.Recorded);
        }

        public Task<ProductionPlan> ApprovePlanAsync(Guid planId, long expectedVersion, Guid approvedByUserId,
            DateTimeOffset approvedAt, ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProductionPlan?> FindPlanAsync(Guid planId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Plans.GetValueOrDefault(planId));

        public Task<ManufacturingWriteOutcome> CreateOrderAsync(Guid orderId, Guid planId, ManufacturingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ManufacturingWriteOutcome> IssueInputAsync(Guid orderId, long expectedVersion, Guid issueId,
            Guid parentRollId, Kilograms mass, DateTimeOffset issuedAt, ManufacturingCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            Guard();
            var order = Orders.GetValueOrDefault(orderId)
                ?? throw new ManufacturingStoreException(ManufacturingStoreCodes.OrderNotFound, "yok");
            order.IssueInput(expectedVersion, issueId, parentRollId, mass, issuedAt);
            Writes.Add(metadata);
            return Task.FromResult(ManufacturingWriteOutcome.Recorded);
        }

        public Task<ManufacturingWriteOutcome> ReturnUnusedInputAsync(Guid orderId, long expectedVersion, Guid returnId,
            Guid parentRollId, Kilograms mass, DateTimeOffset returnedAt, ManufacturingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ManufacturingWriteOutcome> DeclareOutputAsync(Guid orderId, long expectedVersion, Guid outputId,
            Guid parentRollId, OutputClass outputClass, Kilograms mass, DateTimeOffset declaredAt,
            ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ManufacturingWriteOutcome> RecordWorkInProgressAsync(Guid orderId, long expectedVersion, Guid measurementId,
            Guid parentRollId, Kilograms mass, DateTimeOffset measuredAt, ManufacturingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task CloseOrderAsync(Guid orderId, long expectedVersion, Guid closedByUserId, DateTimeOffset closedAt,
            ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProductionOrder?> FindOrderAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Orders.GetValueOrDefault(orderId));

        private void Guard()
        {
            if (Fail is { } code)
            {
                throw new ManufacturingStoreException(code, "sentetik ret");
            }
        }
    }
}
