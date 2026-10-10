using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Infrastructure;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class CounterpartyInventoryStockOwnerDisplaySourceTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Party = Guid.Parse("71000000-0000-0000-0000-000000000001");

    [Fact]
    public void Module_exposes_only_the_public_record_bound_source_and_keeps_its_runtime_internal()
    {
        var configuration = new TestConfiguration();
        var services = new ServiceCollection();

        services.AddCounterpartyModule(configuration);

        var source = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(ICounterpartyInventoryStockOwnerDisplaySourceV1));
        Assert.Equal(typeof(CounterpartyInventoryStockOwnerDisplaySourceV1), source.ImplementationType);
        Assert.False(source.ImplementationType!.IsPublic);
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType.Name.Contains("OwnerReader", StringComparison.Ordinal));
        Assert.Null(typeof(ICounterpartyInventoryStockOwnerDisplaySourceV1).Assembly.GetType(
            "GoymenERP.Modules.Counterparty.Application.ICounterpartyInventoryStockOwnerDisplayOwnerReaderV1"));
        Assert.Null(typeof(CounterpartyInventoryStockOwnerDisplayV1).GetProperty("IsActive"));
    }

    [Fact]
    public async Task Invalid_or_unbound_requests_are_denied_before_database_or_organization_access()
    {
        var context = new TestCompanyContext(Company);
        var user = new TestCurrentUser("inventory-stock-owner-reader", new HashSet<Guid> { Company });
        var source = new CounterpartyInventoryStockOwnerDisplaySourceV1(
            null!,
            context,
            user,
            null!,
            null!);
        var requests = new CounterpartyInventoryStockOwnerDisplayRequestV1[]
        {
            new(Guid.Empty, "inventory-stock-owner-reader", [Party]),
            new(Company, " ", [Party]),
            new(Company, "inventory-stock-owner-reader", []),
            new(Company, "inventory-stock-owner-reader", [Guid.Empty]),
            new(Company, "inventory-stock-owner-reader", [Party, Party]),
            new(Company, "inventory-stock-owner-reader", Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray()),
            new(Guid.NewGuid(), "inventory-stock-owner-reader", [Party]),
            new(Company, "other-reader", [Party]),
        };

        foreach (var request in requests)
        {
            var answer = await source.ReadForAuthorizedInventoryStockOwnersAsync(request);
            Assert.Equal(CounterpartyInventoryStockOwnerDisplayOutcomeV1.Denied, answer.Outcome);
            Assert.Empty(answer.Items);
        }
    }

    [Fact]
    public async Task Cancellation_is_not_collapsed_into_a_closed_business_outcome()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = new CounterpartyInventoryStockOwnerDisplaySourceV1(null!, null!, null!, null!, null!);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            source.ReadForAuthorizedInventoryStockOwnersAsync(
                new(Company, "inventory-stock-owner-reader", [Party]),
                cancellation.Token));
    }

    private sealed class TestCompanyContext(Guid companyId)
        : GoymenERP.BuildingBlocks.Multitenancy.ICompanyContext
    {
        private Guid currentCompanyId = companyId;

        public bool IsResolved => currentCompanyId != Guid.Empty;
        public Guid CompanyId => currentCompanyId;

        public void Set(Guid value) => currentCompanyId = value;
    }

    private sealed class TestCurrentUser(string actorId, IReadOnlySet<Guid> companies)
        : GoymenERP.BuildingBlocks.Security.ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actorId;
        public IReadOnlySet<Guid> AllowedCompanyIds => companies;
        public bool CanAccessCompany(Guid companyId) => companies.Contains(companyId);
    }

    private sealed class TestConfiguration : IConfiguration
    {
        public string? this[string key]
        {
            get => key switch
            {
                "DB_PROVIDER" => "SqlServer",
                "DB_CONNECTION_STRING" => "Server=127.0.0.1;Database=not-opened;User Id=x;Password=x;",
                _ => null,
            };
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);
        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }
}
