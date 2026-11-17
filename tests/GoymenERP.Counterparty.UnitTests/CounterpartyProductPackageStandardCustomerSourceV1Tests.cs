using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Infrastructure;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class CounterpartyProductPackageStandardCustomerSourceV1Tests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private const string Actor = "SENTETIK-package-actor";

    [Theory]
    [InlineData(ErpPermissions.ProductPackageStandardsManage)]
    [InlineData(ErpPermissions.ProductPackageStandardsApprove)]
    public async Task Empty_request_is_genuine_empty_under_each_existing_function_without_a_database_read(string permission)
    {
        var context = new Context(Company); var user = new User(); var gate = new Gate(permission); var probe = new NoConnection();
        await using var db = Database(context, probe);
        var answer = await new CounterpartyProductPackageStandardCustomerSourceV1(db, context, user, gate)
            .ReadAuthorizedAsync(new(Company, Actor, []));
        Assert.Equal(CounterpartyProductPackageStandardCustomerOutcomeV1.Found, answer.Outcome);
        Assert.Equal((Company, Actor), (answer.CompanyId, answer.ActorId)); Assert.Empty(answer.Items); Assert.Equal(0, probe.Calls);
        Assert.Equal(CounterpartyProductPackageStandardCustomerContractV1.SourceVersion, answer.SourceVersion);
        Assert.DoesNotContain(ErpPermissions.CounterpartyPartiesRead, gate.Permissions);
    }

    [Theory]
    [InlineData(ErpPermissions.CounterpartyPartiesRead)]
    [InlineData("manufacturing.orders.read")]
    [InlineData("no-grant")]
    public async Task Cari_or_Manufacturing_authority_never_aliases_the_Product_function(string granted)
    {
        var context = new Context(Company); var probe = new NoConnection(); var gate = new Gate(granted);
        await using var db = Database(context, probe);
        var answer = await new CounterpartyProductPackageStandardCustomerSourceV1(db, context, new User(), gate)
            .ReadAuthorizedAsync(new(Company, Actor, [Other]));
        Assert.Equal(CounterpartyProductPackageStandardCustomerOutcomeV1.Denied, answer.Outcome); Assert.Empty(answer.Items);
        Assert.Equal(0, probe.Calls);
        Assert.Equal(new[] { ErpPermissions.ProductPackageStandardsManage, ErpPermissions.ProductPackageStandardsApprove }, gate.Permissions);
    }

    [Fact]
    public async Task Scoped_function_authority_cannot_release_company_wide_customer_evidence()
    {
        var context = new Context(Company); var probe = new NoConnection(); var gate = new Gate(ErpPermissions.ProductPackageStandardsManage) { CompanyWide = false };
        await using var db = Database(context, probe);
        var answer = await new CounterpartyProductPackageStandardCustomerSourceV1(db, context, new User(), gate)
            .ReadAuthorizedAsync(new(Company, Actor, [Other]));
        Assert.Equal(CounterpartyProductPackageStandardCustomerOutcomeV1.Denied, answer.Outcome); Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public async Task Invalid_bound_actor_company_and_id_sets_are_refused_before_authority_or_connection()
    {
        var context = new Context(Company); var probe = new NoConnection(); var gate = new Gate(ErpPermissions.ProductPackageStandardsManage);
        await using var db = Database(context, probe); var source = new CounterpartyProductPackageStandardCustomerSourceV1(db, context, new User(), gate);
        var requests = new CounterpartyProductPackageStandardCustomerRequestV1[] {
            new(Other, Actor, [Other]), new(Company, "SENTETIK-other", [Other]), new(Company, Actor, [Guid.Empty]),
            new(Company, Actor, [Other, Other]), new(Company, Actor, Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray()),
        };
        foreach (var request in requests)
        {
            var answer = await source.ReadAuthorizedAsync(request);
            Assert.Equal(CounterpartyProductPackageStandardCustomerOutcomeV1.Denied, answer.Outcome); Assert.Empty(answer.Items);
        }
        Assert.Empty(gate.Permissions); Assert.Equal(0, probe.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Actor_or_company_drift_after_authority_cannot_open_the_customer_query(bool changeCompany)
    {
        var context = new Context(Company); var user = new User(); var probe = new NoConnection();
        var gate = new Gate(ErpPermissions.ProductPackageStandardsManage) { After = () => { if (changeCompany) context.Set(Other); else user.UserId = "SENTETIK-other"; } };
        await using var db = Database(context, probe);
        var answer = await new CounterpartyProductPackageStandardCustomerSourceV1(db, context, user, gate).ReadAuthorizedAsync(new(Company, Actor, [Other]));
        Assert.Equal(CounterpartyProductPackageStandardCustomerOutcomeV1.Denied, answer.Outcome); Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public async Task Cancellation_before_or_during_authority_propagates_without_a_database_read()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var context = new Context(Company); var probe = new NoConnection(); var gate = new Gate(ErpPermissions.ProductPackageStandardsManage);
        await using var db = Database(context, probe); var source = new CounterpartyProductPackageStandardCustomerSourceV1(db, context, new User(), gate);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ReadAuthorizedAsync(new(Company, Actor, []), cancelled.Token));
        using var during = new CancellationTokenSource(); gate.After = during.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ReadAuthorizedAsync(new(Company, Actor, []), during.Token));
        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public void Owner_contract_is_registered_as_a_scoped_read_source_without_a_new_permission_or_command()
    {
        var services = new ServiceCollection();
        services.AddCounterpartyModule(new RegistrationConfiguration(), allowEphemeralCursorKey: true);
        var registration = Assert.Single(services, item => item.ServiceType == typeof(ICounterpartyProductPackageStandardCustomerSourceV1));
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
        Assert.Equal(typeof(CounterpartyProductPackageStandardCustomerSourceV1), registration.ImplementationType);
        Assert.Equal(100, CounterpartyProductPackageStandardCustomerContractV1.MaximumPartyCount);
    }

    private sealed class RegistrationConfiguration : IConfiguration
    {
        public string? this[string key]
        {
            get => key == "DB_CONNECTION_STRING"
                ? "Server=invalid-synthetic;Database=synthetic;Integrated Security=true;TrustServerCertificate=true" : null;
            set => throw new NotSupportedException();
        }
        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);
        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }

    private static CounterpartyDbContext Database(ICompanyContext context, NoConnection probe) => new(
        new DbContextOptionsBuilder<CounterpartyDbContext>().UseSqlServer("Server=invalid-synthetic;Database=synthetic;Integrated Security=true;TrustServerCertificate=true")
            .AddInterceptors(probe).Options, context);
    private sealed class NoConnection : DbConnectionInterceptor
    {
        public int Calls { get; private set; }
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        { Calls++; throw new InvalidOperationException("SENTETIK forbidden database connection"); }
    }
    private sealed class Context(Guid value) : ICompanyContext
    { public bool IsResolved => true; public Guid CompanyId { get; private set; } = value; public void Set(Guid companyId) => CompanyId = companyId; }
    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true; public string? UserId { get; set; } = Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company, Other };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
    private sealed class Gate(string permission) : IErpAuthorityRevalidationService
    {
        public List<string> Permissions { get; } = []; public bool CompanyWide { get; init; } = true; public Action? After { get; set; }
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string value, Guid companyId, CancellationToken cancellationToken = default)
        {
            Permissions.Add(value); After?.Invoke(); return Task.FromResult(value == permission && companyId == Company
                ? new PermissionScope(true, CompanyWide, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) : PermissionScope.Denied);
        }
    }
}
