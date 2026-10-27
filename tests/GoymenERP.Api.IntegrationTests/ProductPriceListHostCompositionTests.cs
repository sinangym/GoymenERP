using GoymenERP.Api.ProductRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ProductPriceListHostCompositionTests
{
    [Fact]
    public async Task Disabled_product_runtime_registers_only_the_fail_closed_service_and_never_database_readers()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new ProductRuntimeOptions("unused", null, null));
        services.AddProductPriceListHost(new ConfigurationBuilder().Build());

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ProductPriceListDbContext));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IProductPriceReferenceReaderV1));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IProductQuantityPriceReferenceReaderV1));
        // MOD-02: neither the tier chain nor the Cari customer port is composed on a disabled runtime.
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IProductQuantityPriceReferenceReaderV2));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IProductPriceListCustomerPortV1));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IProductPriceListServiceV1>();
        var exception = await Assert.ThrowsAsync<ProductPriceListException>(() => service.ListAsync(null, null));
        Assert.Equal((StatusCodes.Status503ServiceUnavailable, ProductPriceListCodesV1.Unavailable),
            (exception.Status, exception.Code));
    }

    [Fact]
    public void Enabled_product_runtime_keeps_the_owner_service_readers_and_database_registration()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new ProductRuntimeOptions(
            "unused",
            new ProductPilotIssuerBinding(Guid.NewGuid(), Guid.NewGuid()),
            new byte[32]));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DB_PROVIDER"] = "SqlServer",
                ["DB_CONNECTION_STRING"] = "Server=localhost;Database=GoymenERP_CompositionOnly;Integrated Security=true;TrustServerCertificate=true",
            })
            .Build();

        services.AddProductPriceListHost(configuration, (_, options) => options.EnableDetailedErrors());

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ProductPriceListDbContext));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IProductPriceListServiceV1));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IProductPriceReferenceReaderV1));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IProductQuantityPriceReferenceReaderV1));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IProductQuantityPriceReferenceReaderV2));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IProductPriceListCustomerPortV1)
            && descriptor.ImplementationType == typeof(CounterpartyProductPriceListCustomerPortV1));
    }

    [Fact]
    public void Final_test_runtime_override_recomposes_price_lists_once_without_weakening_disabled_hosts()
    {
        const string connection =
            "Server=localhost;Database=GoymenERP_CompositionOnly;Integrated Security=true;TrustServerCertificate=true";
        var services = new ServiceCollection();
        services.AddSingleton(new ProductRuntimeOptions("unused", null, null));
        services.AddProductPriceListHost(new ConfigurationBuilder().Build());
        var failClosedDescriptor = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IProductPriceListServiceV1));

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ProductPriceListDbContext));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IProductPriceReferenceReaderV1));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IProductQuantityPriceReferenceReaderV1));

        services.RemoveAll<ProductRuntimeOptions>();
        services.AddSingleton(new ProductRuntimeOptions(
            connection,
            new ProductPilotIssuerBinding(Guid.NewGuid(), Guid.NewGuid()),
            new byte[32]));

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ProductPriceListDbContext));

        SqlServerRelationalApiFactory.EnsureFinalProductPriceListHostComposition(services, connection);
        AssertFinalComposition(services, failClosedDescriptor);

        SqlServerRelationalApiFactory.EnsureFinalProductPriceListHostComposition(services, connection);
        AssertFinalComposition(services, failClosedDescriptor);

        static void AssertFinalComposition(
            IServiceCollection descriptors,
            ServiceDescriptor failClosedDescriptor)
        {
            Assert.DoesNotContain(failClosedDescriptor, descriptors);
            Assert.Single(descriptors, descriptor => descriptor.ServiceType == typeof(ProductPriceListDbContext));
            Assert.Single(descriptors, descriptor => descriptor.ServiceType == typeof(IProductPriceListServiceV1));
            Assert.Single(descriptors, descriptor => descriptor.ServiceType == typeof(IProductPriceReferenceReaderV1));
            Assert.Single(descriptors, descriptor => descriptor.ServiceType == typeof(IProductQuantityPriceReferenceReaderV1));
            Assert.Single(descriptors, descriptor => descriptor.ServiceType == typeof(IProductQuantityPriceReferenceReaderV2));
            Assert.Single(descriptors, descriptor => descriptor.ServiceType == typeof(IProductPriceListCustomerPortV1));
        }
    }
    private static readonly Guid CustomerParty = Guid.Parse("5a5a5a5a-5a5a-4a5a-8a5a-5a5a5a5a5a5a");
    private static readonly Guid CustomerAccount = Guid.Parse("6b6b6b6b-6b6b-4b6b-8b6b-6b6b6b6b6b6b");

    [Fact]
    public async Task MOD_02_customer_port_maps_the_Cari_owners_party_and_account_of_this_company()
    {
        var company = new StubCompanyContext(TestCompanyIds.CompanyA);
        var user = new StubCurrentUser("price-writer", TestCompanyIds.CompanyA);
        var owner = new StubParties(Party(TestCompanyIds.CompanyA, partyActive: true, accountActive: false));

        var found = await Port(owner, company, user).GetAuthorizedAsync(CustomerParty, CustomerAccount);
        Assert.Equal(ProductPriceListCustomerOutcomeV1.Found, found.Outcome);
        Assert.Equal(new ProductPriceListCustomerV1(CustomerParty, CustomerAccount, TestCompanyIds.CompanyA, true, false), found.Customer);

        Assert.Equal(ProductPriceListCustomerOutcomeV1.NotFound,
            (await Port(owner, company, user).GetAuthorizedAsync(CustomerParty, Guid.NewGuid())).Outcome);
        Assert.Equal(ProductPriceListCustomerOutcomeV1.NotFound,
            (await Port(new StubParties(Party(TestCompanyIds.CompanyB)), company, user).GetAuthorizedAsync(CustomerParty, CustomerAccount)).Outcome);
        Assert.Equal(ProductPriceListCustomerOutcomeV1.NotFound,
            (await Port(new StubParties(null), company, user).GetAuthorizedAsync(CustomerParty, CustomerAccount)).Outcome);
    }

    [Fact]
    public async Task MOD_02_customer_port_fails_closed_without_the_owner_a_current_context_or_the_owners_consent()
    {
        var company = new StubCompanyContext(TestCompanyIds.CompanyA);
        var user = new StubCurrentUser("price-writer", TestCompanyIds.CompanyA);

        // The Counterparty module is not composed in this host: unavailable, never a pass.
        await using (var empty = new ServiceCollection().BuildServiceProvider())
        {
            Assert.Equal(ProductPriceListCustomerOutcomeV1.Unavailable,
                (await new CounterpartyProductPriceListCustomerPortV1(empty, company, user).GetAuthorizedAsync(CustomerParty, CustomerAccount)).Outcome);
        }

        var owner = new StubParties(Party(TestCompanyIds.CompanyA));
        Assert.Equal(ProductPriceListCustomerOutcomeV1.Denied,
            (await Port(owner, company, new StubCurrentUser("price-writer", TestCompanyIds.CompanyB)).GetAuthorizedAsync(CustomerParty, CustomerAccount)).Outcome);
        Assert.Equal(ProductPriceListCustomerOutcomeV1.Denied, (await Port(owner, company, user).GetAuthorizedAsync(Guid.Empty, CustomerAccount)).Outcome);
        Assert.Empty(owner.Reads);

        var refusing = new StubParties(null) { Error = new UnauthorizedAccessException("owner-detail") };
        Assert.Equal(ProductPriceListCustomerOutcomeV1.Denied, (await Port(refusing, company, user).GetAuthorizedAsync(CustomerParty, CustomerAccount)).Outcome);

        // A context that changes while the owner answers releases nothing.
        var switching = new StubParties(Party(TestCompanyIds.CompanyA)) { OnRead = () => company.CompanyId = TestCompanyIds.CompanyB };
        var multiCompanyUser = new StubCurrentUser("price-writer", TestCompanyIds.CompanyA, TestCompanyIds.CompanyB);
        Assert.Equal(ProductPriceListCustomerOutcomeV1.Denied,
            (await Port(switching, company, multiCompanyUser).GetAuthorizedAsync(CustomerParty, CustomerAccount)).Outcome);
    }

    private static CounterpartyProductPriceListCustomerPortV1 Port(ICounterpartyReadService owner, ICompanyContext company, ICurrentUser user)
    {
        var services = new ServiceCollection();
        services.AddSingleton(owner);
        return new CounterpartyProductPriceListCustomerPortV1(services.BuildServiceProvider(), company, user);
    }

    private static CounterpartyDetailView Party(Guid accountCompany, bool partyActive = true, bool accountActive = true) =>
        new(CustomerParty, "C-0001", PartyKind.LegalEntity, "SENTETIK Müşteri A.Ş.", null, partyActive, "3",
            [new CounterpartyDetailAccountView(CustomerAccount, accountCompany, PartyRole.Customer, accountActive, "5")]);

    private sealed class StubParties(CounterpartyDetailView? party) : ICounterpartyReadService
    {
        public List<Guid> Reads { get; } = [];
        public Exception? Error { get; init; }
        public Action? OnRead { get; init; }

        public Task<CounterpartyPageExecution> GetAuthorizedPageAsync(CounterpartyReadRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<CounterpartyDetailView?> GetAuthorizedPartyAsync(Guid partyId, CancellationToken cancellationToken = default)
        {
            Reads.Add(partyId);
            OnRead?.Invoke();
            return Error is null ? Task.FromResult(party) : Task.FromException<CounterpartyDetailView?>(Error);
        }
    }

    private sealed class StubCurrentUser(string userId, params Guid[] companies) : ICurrentUser
    {
        private readonly HashSet<Guid> _companies = [.. companies];
        public bool IsAuthenticated => true;
        public string? UserId { get; set; } = userId;
        public IReadOnlySet<Guid> AllowedCompanyIds => _companies;
        public bool CanAccessCompany(Guid companyId) => _companies.Contains(companyId);
    }

    private sealed class StubCompanyContext(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; set; } = companyId;
        public void Set(Guid companyId) => CompanyId = companyId;
    }
}
