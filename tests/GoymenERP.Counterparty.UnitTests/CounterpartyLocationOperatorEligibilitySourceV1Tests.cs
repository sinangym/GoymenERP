using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Infrastructure;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class CounterpartyLocationOperatorEligibilitySourceV1Tests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid Party = Guid.Parse("71000000-0000-4000-8000-000000000001");

    [Fact]
    public void Module_exposes_only_the_internal_exact_source_without_a_permission_or_reader()
    {
        var services = new ServiceCollection();
        services.AddCounterpartyModule(new TestConfiguration());

        var source = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(ICounterpartyLocationOperatorEligibilitySourceV1));
        Assert.Equal(typeof(CounterpartyLocationOperatorEligibilitySourceV1), source.ImplementationType);
        Assert.False(source.ImplementationType!.IsPublic);
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType.Name.Contains("LocationOperatorReader", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_or_context_mismatched_requests_are_denied_without_a_database_read()
    {
        var context = new TestCompanyContext(Company);
        var user = new TestCurrentUser("organization-location-writer", new HashSet<Guid> { Company });
        var source = new CounterpartyLocationOperatorEligibilitySourceV1(
            null!,
            context,
            user,
            null!);
        var requests = new CounterpartyLocationOperatorEligibilityRequestV1[]
        {
            new(Guid.Empty, "organization-location-writer", Party),
            new(Company, " ", Party),
            new(Company, " organization-location-writer", Party),
            new(Company, "organization-location-writer", Guid.Empty),
            new(Guid.NewGuid(), "organization-location-writer", Party),
            new(Company, "other-writer", Party),
        };

        foreach (var request in requests)
        {
            var answer = await source.ReadForLocationOperatorAsync(request);
            Assert.Equal(CounterpartyLocationOperatorEligibilityOutcomeV1.Denied, answer.Outcome);
            Assert.Null(answer.Operator);
        }
    }

    [Fact]
    public async Task Cancellation_is_not_collapsed_into_a_closed_outcome()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = new CounterpartyLocationOperatorEligibilitySourceV1(
            null!, null!, null!, null!);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            source.ReadForLocationOperatorAsync(
                new(Company, "organization-location-writer", Party),
                cancellation.Token));
    }

    private sealed class TestCompanyContext(Guid companyId)
        : GoymenERP.BuildingBlocks.Multitenancy.ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid companyId) => CompanyId = companyId;
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
