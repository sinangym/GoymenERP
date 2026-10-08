using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Infrastructure;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class CounterpartySalesRecordPartyDisplaySourceTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Party = Guid.Parse("71000000-0000-0000-0000-000000000001");

    [Fact]
    public void Module_exposes_only_the_public_record_bound_source_and_keeps_its_runtime_internal()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DB_PROVIDER"] = "SqlServer",
                ["DB_CONNECTION_STRING"] = "Server=127.0.0.1;Database=not-opened;User Id=x;Password=x;",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddCounterpartyModule(configuration);

        var source = Assert.Single(services.Where(descriptor =>
            descriptor.ServiceType == typeof(ICounterpartySalesRecordPartyDisplaySourceV1)));
        Assert.Equal(typeof(CounterpartySalesRecordPartyDisplaySourceV1), source.ImplementationType);
        Assert.False(source.ImplementationType!.IsPublic);
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType.Name.Contains("OwnerReader", StringComparison.Ordinal));
        Assert.Null(typeof(ICounterpartySalesRecordPartyDisplaySourceV1).Assembly.GetType(
            "GoymenERP.Modules.Counterparty.Application.ICounterpartySalesRecordPartyDisplayOwnerReaderV1"));
    }

    [Fact]
    public async Task Invalid_or_unbound_requests_are_denied_before_database_or_organization_access()
    {
        var context = new TestCompanyContext(Company);
        var user = new TestCurrentUser("sales-reader", [Company]);
        var source = new CounterpartySalesRecordPartyDisplaySourceV1(
            null!,
            context,
            user,
            null!,
            null!);
        var requests = new CounterpartySalesRecordPartyDisplayRequestV1[]
        {
            new(Guid.Empty, "sales-reader", [Party]),
            new(Company, " ", [Party]),
            new(Company, "sales-reader", []),
            new(Company, "sales-reader", [Guid.Empty]),
            new(Company, "sales-reader", [Party, Party]),
            new(Company, "sales-reader", Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray()),
            new(Guid.NewGuid(), "sales-reader", [Party]),
            new(Company, "other-reader", [Party]),
        };

        foreach (var request in requests)
        {
            var answer = await source.ReadForAuthorizedSalesRecordsAsync(request);
            Assert.Equal(CounterpartySalesRecordPartyDisplayOutcomeV1.Denied, answer.Outcome);
            Assert.Empty(answer.Items);
        }
    }

    [Fact]
    public async Task Cancellation_is_not_collapsed_into_a_closed_business_outcome()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = new CounterpartySalesRecordPartyDisplaySourceV1(null!, null!, null!, null!, null!);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            source.ReadForAuthorizedSalesRecordsAsync(
                new(Company, "sales-reader", [Party]),
                cancellation.Token));
    }

    private sealed class TestCompanyContext(Guid companyId)
        : GoymenERP.BuildingBlocks.Multitenancy.ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId => companyId;
    }

    private sealed class TestCurrentUser(string actorId, IReadOnlySet<Guid> companies)
        : GoymenERP.BuildingBlocks.Security.ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actorId;
        public IReadOnlySet<Guid> AllowedCompanyIds => companies;
        public bool CanAccessCompany(Guid companyId) => companies.Contains(companyId);
    }
}
