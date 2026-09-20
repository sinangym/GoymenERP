using System.Net;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class SalesReferenceHttpAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid PartyId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid AccountId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid CurrencyId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid ProductId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid UnitId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private const string AccountsQuery = "?q=CARI&pageSize=50&cursor=owner-cursor";
    // Test-host bindings only; not accepted Sales permission names or user grants.
    private const string ControlledAccountsPermission = "controlled.sales.accounts";
    private const string ControlledProductPermission = "controlled.sales.product";

    [Fact]
    public async Task Default_runtime_denies_without_an_accepted_sales_permission_binding()
    {
        using var client = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
        using var response = await client.GetAsync(SalesEndpoints.AccountsPath + "?pageSize=50");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertPrivateNoStore(response);
    }

    [Fact]
    public async Task Accounts_success_is_lossless_private_and_uses_server_session_binding()
    {
        var service = new StubService
        {
            Accounts = new(200, new(TestCompanyIds.CompanyA,
                [new(PartyId, AccountId, "CARI - Cari", "7", "9007199254740993")],
                "next-owner-cursor"), null)
        };
        using var host = CreateHost(service, new StubSessionBinding("trusted-session"));
        using var client = CreateClient(host, true);
        using var response = await client.GetAsync(SalesEndpoints.AccountsPath + AccountsQuery);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivateNoStore(response);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("9007199254740993", document.RootElement.GetProperty("accounts")[0]
            .GetProperty("accountVersion").GetString());
        Assert.Equal("next-owner-cursor", document.RootElement.GetProperty("nextCursor").GetString());
        Assert.Equal("trusted-session", Assert.Single(service.Sessions));
    }

    [Theory]
    [InlineData("?q=a&pageSize=50&pageSize=51", "validation_failed")]
    [InlineData("?pageSize=50&companyId=10000000-0000-0000-0000-000000000001", "validation_failed")]
    public async Task Accounts_reject_repeated_or_unknown_query_before_owner_service(
        string query, string expectedCode)
    {
        var service = new StubService();
        using var host = CreateHost(service, new StubSessionBinding("trusted-session"));
        using var client = CreateClient(host, true);
        using var response = await client.GetAsync(SalesEndpoints.AccountsPath + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        if (body.Length != 0)
        {
            using var document = JsonDocument.Parse(body);
            Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
        }
        Assert.Empty(service.Sessions);
    }

    [Fact]
    public async Task Missing_server_session_fails_closed_before_owner_service()
    {
        var service = new StubService();
        using var host = CreateHost(service, new StubSessionBinding(null));
        using var client = CreateClient(host, true);
        using var response = await client.GetAsync(SalesEndpoints.AccountsPath + "?pageSize=50");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(service.Sessions);
    }

    [Theory]
    [InlineData(400, "validation_failed", "validation_failed")]
    [InlineData(403, "private-provider-code", "forbidden")]
    [InlineData(422, "unsupported_currency", "unsupported_currency")]
    [InlineData(503, "settings_provider_unavailable", "service_unavailable")]
    public async Task Currency_status_boundary_exposes_only_accepted_public_codes(
        int status, string sourceCode, string expectedPublicCode)
    {
        var service = new StubService
        {
            Currency = new(status, null, sourceCode)
        };
        using var host = CreateHost(service, new StubSessionBinding("trusted-session"));
        using var client = CreateClient(host, true);
        using var response = await client.GetAsync(SalesEndpoints.CurrencyPath
            + $"?currencyId={CurrencyId:D}&effectiveOn=2026-09-13");
        Assert.Equal(status, (int)response.StatusCode);
        AssertPrivateNoStore(response);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal(expectedPublicCode, document.RootElement.GetProperty("code").GetString());
        if (!string.Equals(sourceCode, expectedPublicCode, StringComparison.Ordinal))
            Assert.DoesNotContain(sourceCode, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Product_rejects_non_iso_business_date_without_calling_service()
    {
        var service = new StubService();
        using var host = CreateHost(service, new StubSessionBinding("trusted-session"));
        using var client = CreateClient(host, true);
        using var response = await client.GetAsync(SalesEndpoints.ProductPath
            + $"?productId={ProductId:D}&expectedProductVersion=7&orderUnitId={UnitId:D}&effectiveOn=09%2F13%2F2026");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, service.ProductCalls);
    }

    [Fact]
    public async Task Counterparty_adapter_forwards_every_server_bound_field_and_maps_owner_values()
    {
        var companyContext = new CompanyContext();
        companyContext.Set(TestCompanyIds.CompanyA);
        var owner = new StubCounterpartyReadService(CounterpartyPageExecution.Succeeded(new(
            [new(PartyId, "CARI", PartyKind.LegalEntity, "Cari", "Cari Ticaret", true, 7,
                [new(AccountId, TestCompanyIds.CompanyA, PartyRole.None, true, 9007199254740993)])],
            "owner-next", true, null, null)));
        var adapter = new CounterpartySalesAccountSelectionSourceV1(companyContext,
            new StubCurrentUser("sales-reader", TestCompanyIds.CompanyA), owner);

        var result = await adapter.GetPageAsync(new(TestCompanyIds.CompanyA, "sales-reader",
            "trusted-session", "CARI", 25, "owner-cursor"));

        Assert.Equal(200, result.StatusCode);
        Assert.Null(result.Code);
        Assert.Equal(new CounterpartyReadRequest(ExpectedOwnerQuery("CARI", 25, "owner-cursor"),
            "sales-reader", "trusted-session"), Assert.Single(owner.Requests));
        var party = Assert.Single(result.Value!.Items);
        Assert.Equal((PartyId, "CARI", (int)PartyKind.LegalEntity, "Cari", "Cari Ticaret", true, "7"),
            (party.Id, party.Code, party.Kind, party.LegalName, party.TradeName, party.IsActive, party.Version));
        Assert.Equal(new SalesAccountSelectionAccountV1(AccountId, TestCompanyIds.CompanyA,
            (int)PartyRole.None, true, "9007199254740993"), Assert.Single(party.CompanyAccounts));
        Assert.Equal("owner-next", result.Value.NextCursor);
    }

    [Fact]
    public async Task Counterparty_adapter_denies_foreign_company_before_owner_read()
    {
        var companyContext = new CompanyContext();
        companyContext.Set(TestCompanyIds.CompanyA);
        var owner = new StubCounterpartyReadService(CounterpartyPageExecution.ServiceUnavailable());
        var adapter = new CounterpartySalesAccountSelectionSourceV1(companyContext,
            new StubCurrentUser("sales-reader", TestCompanyIds.CompanyA), owner);
        var result = await adapter.GetPageAsync(new(TestCompanyIds.CompanyB, "sales-reader",
            "trusted-session", null, 50, null));
        Assert.Equal(403, result.StatusCode);
        Assert.Empty(owner.Requests);
    }

    [Theory]
    [InlineData(CounterpartyPageResult.Forbidden, 403, null)]
    [InlineData(CounterpartyPageResult.InvalidCursor, 400, "invalid_cursor")]
    [InlineData(CounterpartyPageResult.ServiceUnavailable, 503, null)]
    [InlineData(CounterpartyPageResult.Succeeded, 503, null)]
    public async Task Counterparty_adapter_maps_owner_outcomes_without_a_value(
        CounterpartyPageResult ownerResult, int expectedStatus, string? expectedCode)
    {
        var owner = new StubCounterpartyReadService(new CounterpartyPageExecution(ownerResult, null));
        var adapter = new CounterpartySalesAccountSelectionSourceV1(new StubCompanyContext(TestCompanyIds.CompanyA),
            new StubCurrentUser("sales-reader", TestCompanyIds.CompanyA), owner);

        var result = await adapter.GetPageAsync(new(TestCompanyIds.CompanyA, "sales-reader",
            "trusted-session", null, 50, null));

        Assert.Equal(expectedStatus, result.StatusCode);
        Assert.Equal(expectedCode, result.Code);
        Assert.Null(result.Value);
        Assert.Single(owner.Requests);
    }

    [Fact]
    public async Task Counterparty_adapter_turns_owner_authorization_exception_into_forbidden()
    {
        var owner = new StubCounterpartyReadService(CounterpartyPageExecution.ServiceUnavailable())
        {
            Error = new UnauthorizedAccessException("owner-internal-detail")
        };
        var adapter = new CounterpartySalesAccountSelectionSourceV1(new StubCompanyContext(TestCompanyIds.CompanyA),
            new StubCurrentUser("sales-reader", TestCompanyIds.CompanyA), owner);

        var result = await adapter.GetPageAsync(new(TestCompanyIds.CompanyA, "sales-reader",
            "trusted-session", null, 50, null));

        Assert.Equal(403, result.StatusCode);
        Assert.Null(result.Code);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("actor")]
    public async Task Counterparty_adapter_discards_owner_success_after_late_context_change(string change)
    {
        var companyContext = new StubCompanyContext(TestCompanyIds.CompanyA);
        var user = new StubCurrentUser("sales-reader", TestCompanyIds.CompanyA, TestCompanyIds.CompanyB);
        var owner = new StubCounterpartyReadService(CounterpartyPageExecution.Succeeded(new(
            [new(PartyId, "CARI", PartyKind.LegalEntity, "Cari", null, true, 7,
                [new(AccountId, TestCompanyIds.CompanyA, PartyRole.None, true, 1)])],
            null, false, null, null)))
        {
            OnRead = () =>
            {
                if (change == "company") companyContext.CompanyId = TestCompanyIds.CompanyB;
                else user.UserId = "other-reader";
            }
        };
        var adapter = new CounterpartySalesAccountSelectionSourceV1(companyContext, user, owner);

        var result = await adapter.GetPageAsync(new(TestCompanyIds.CompanyA, "sales-reader",
            "trusted-session", null, 50, null));

        Assert.Equal(403, result.StatusCode);
        Assert.Null(result.Value);
        Assert.Single(owner.Requests);
    }

    [Fact]
    public async Task Accounts_request_reaches_owner_through_the_real_handler_and_adapter()
    {
        var owner = new StubCounterpartyReadService(CounterpartyPageExecution.Succeeded(new(
            [new(PartyId, "CARI", PartyKind.LegalEntity, "Cari", null, true, 7,
                [new(AccountId, TestCompanyIds.CompanyA, PartyRole.None, true, 9007199254740993),
                 new(Guid.NewGuid(), TestCompanyIds.CompanyB, PartyRole.None, true, 3)])],
            "owner-next", true, null, null)));
        using var host = CreateRealHandlerHost(owner);
        using var client = CreateClient(host, true);

        using var response = await client.GetAsync(SalesEndpoints.AccountsPath
            + "?q=CARI&pageSize=25&cursor=owner-cursor");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivateNoStore(response);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(TestCompanyIds.CompanyA, document.RootElement.GetProperty("companyId").GetGuid());
        var account = Assert.Single(document.RootElement.GetProperty("accounts").EnumerateArray());
        Assert.Equal(PartyId, account.GetProperty("partyId").GetGuid());
        Assert.Equal(AccountId, account.GetProperty("companyAccountId").GetGuid());
        Assert.Equal("CARI - Cari", account.GetProperty("label").GetString());
        Assert.Equal("7", account.GetProperty("partyVersion").GetString());
        Assert.Equal("9007199254740993", account.GetProperty("accountVersion").GetString());
        Assert.Equal("owner-next", document.RootElement.GetProperty("nextCursor").GetString());

        var request = Assert.Single(owner.Requests);
        Assert.Equal(ExpectedOwnerQuery("CARI", 25, "owner-cursor"), request.Query);
        // The actor is the server-resolved local user, never the bootstrap header subject.
        Assert.True(Guid.TryParse(request.ActorId, out _));
        Assert.Equal("trusted-session", request.SessionBinding);
    }

    [Theory]
    [InlineData("invalidCursor", CounterpartyPageResult.InvalidCursor)]
    [InlineData("forbidden", CounterpartyPageResult.Forbidden)]
    [InlineData("providerUnavailable", CounterpartyPageResult.ServiceUnavailable)]
    public async Task Documented_account_error_examples_match_the_real_pipeline(
        string exampleName, CounterpartyPageResult ownerResult)
    {
        var example = ReadReferenceExample(exampleName);
        var owner = new StubCounterpartyReadService(new CounterpartyPageExecution(ownerResult, null));
        using var host = CreateRealHandlerHost(owner);
        using var client = CreateClient(host, true);

        using var response = await client.GetAsync(example.GetProperty("path").GetString());

        await AssertMatchesExampleAsync(example, response);
        Assert.Single(owner.Requests);
    }

    [Theory]
    [InlineData("productSelectionChanged", ProductApiV1.VersionConflict)]
    [InlineData("unsupportedProduct", "not_sellable")]
    public async Task Documented_product_error_examples_match_the_real_pipeline(
        string exampleName, string ownerErrorCode)
    {
        var example = ReadReferenceExample(exampleName);
        var products = new StubProductEligibility(new(null, [new("productId", ownerErrorCode)]));
        var owner = new StubCounterpartyReadService(CounterpartyPageExecution.ServiceUnavailable());
        using var host = CreateRealHandlerHost(owner, products);
        using var client = CreateClient(host, true);

        using var response = await client.GetAsync(example.GetProperty("path").GetString());

        await AssertMatchesExampleAsync(example, response);
        Assert.Equal(1, products.Calls);
    }

    [Fact]
    public async Task Documented_unsupported_unit_example_matches_the_real_pipeline()
    {
        // The approved Settings provider (#279) reports a unit the company's source has no
        // profile for with its own code; the handler turns that into 422 unsupported_unit and
        // the product route passes the code through.
        var example = ReadReferenceExample("unsupportedUnit");
        var productId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var orderUnitId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var products = new StubProductEligibility(new(new ProductSalesSnapshotV1(productId, "7", "EXAMPLE-CODE", "Example",
            Unit(Guid.NewGuid()), Unit(Guid.NewGuid()), Unit(orderUnitId), new DateOnly(2026, 9, 13)), []));
        var settings = new StubSettings(new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.UnitProfileNotFound));
        var owner = new StubCounterpartyReadService(CounterpartyPageExecution.ServiceUnavailable());
        using var host = CreateRealHandlerHost(owner, products, settings);
        using var client = CreateClient(host, true);

        using var response = await client.GetAsync(example.GetProperty("path").GetString());

        await AssertMatchesExampleAsync(example, response);
        Assert.Equal(1, products.Calls);
        Assert.Equal(1, settings.UnitReads);
        Assert.DoesNotContain(SettingsPrerequisiteCodesV1.UnitProfileNotFound,
            await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        static ProductSalesUnitProfileV1 Unit(Guid unitId) => new(TestCompanyIds.CompanyA, unitId, Guid.NewGuid(), "1");
    }

    // ---- SALES-A2a: the two list routes and the contact-information adapter --------------------------

    private static readonly Guid AddressId = Guid.Parse("aaaaaaaa-1111-4111-8111-aaaaaaaaaaaa");
    private static readonly Guid ContactId = Guid.Parse("bbbbbbbb-1111-4111-8111-bbbbbbbbbbbb");
    private static readonly Guid ProfileId = Guid.Parse("cccccccc-1111-4111-8111-cccccccccccc");

    [Fact]
    public async Task Contact_information_success_is_lossless_and_private()
    {
        var service = new StubService
        {
            ContactInformation = new(200, new(TestCompanyIds.CompanyA, PartyId, AccountId, "9007199254740993",
                [new(AddressId, "3", "delivery", "Sanayi Cad. 1, Gebze Kocaeli 41400, TR", "TR", "Sanayi Cad. 1", null, "Kocaeli", "Gebze", "41400")],
                [new(ContactId, "2", "Ayşe Yükleme", null, "delivery")]), null)
        };
        using var host = CreateHost(service, new StubSessionBinding("trusted-session"));
        using var client = CreateClient(host, true);
        using var response = await client.GetAsync(SalesEndpoints.ContactInformationPath
            + $"?partyId={PartyId:D}&companyAccountId={AccountId:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivateNoStore(response);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(new[] { "companyId", "partyId", "companyAccountId", "accountVersion", "addresses", "contacts" }, Keys(root));
        Assert.Equal("9007199254740993", root.GetProperty("accountVersion").GetString());
        var address = Assert.Single(root.GetProperty("addresses").EnumerateArray());
        Assert.Equal(new[] { "id", "version", "purpose", "text", "countryCode", "line1", "line2", "city", "district", "postalCode" }, Keys(address));
        Assert.Equal((AddressId, "3", "Sanayi Cad. 1, Gebze Kocaeli 41400, TR", JsonValueKind.Null),
            (address.GetProperty("id").GetGuid(), address.GetProperty("version").GetString(), address.GetProperty("text").GetString(),
                address.GetProperty("line2").ValueKind));
        var contact = Assert.Single(root.GetProperty("contacts").EnumerateArray());
        Assert.Equal(new[] { "id", "version", "displayName", "jobTitle", "purpose" }, Keys(contact));
        Assert.Equal((ContactId, "2", "Ayşe Yükleme"), (contact.GetProperty("id").GetGuid(), contact.GetProperty("version").GetString(),
            contact.GetProperty("displayName").GetString()));
        Assert.Equal(new SalesContactInformationQueryV1(PartyId, AccountId), Assert.Single(service.ContactQueries));
    }

    [Theory]
    [InlineData("?partyId=44444444-4444-4444-4444-444444444444")]
    [InlineData("?companyAccountId=55555555-5555-5555-5555-555555555555")]
    [InlineData("?partyId=44444444-4444-4444-4444-444444444444&companyAccountId=55555555-5555-5555-5555-555555555555&companyAccountId=55555555-5555-5555-5555-555555555555")]
    [InlineData("?partyId=44444444-4444-4444-4444-444444444444&companyAccountId=55555555-5555-5555-5555-555555555555&companyId=10000000-0000-0000-0000-000000000001")]
    [InlineData("?partyId=00000000-0000-0000-0000-000000000000&companyAccountId=55555555-5555-5555-5555-555555555555")]
    [InlineData("?partyId=not-a-guid&companyAccountId=55555555-5555-5555-5555-555555555555")]
    public async Task Contact_information_rejects_a_missing_repeated_unknown_or_empty_query_before_the_service(string query)
    {
        var service = new StubService();
        using var host = CreateHost(service, new StubSessionBinding("trusted-session"));
        using var client = CreateClient(host, true);
        using var response = await client.GetAsync(SalesEndpoints.ContactInformationPath + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(service.ContactQueries);
    }

    [Theory]
    [InlineData(403, null, "forbidden")]
    [InlineData(503, "counterparty_provider_unavailable", "service_unavailable")]
    [InlineData(503, "reference_provider_invalid", "service_unavailable")]
    public async Task Contact_information_status_boundary_exposes_only_accepted_public_codes(
        int status, string? sourceCode, string expectedPublicCode)
    {
        var service = new StubService { ContactInformation = new(status, null, sourceCode) };
        using var host = CreateHost(service, new StubSessionBinding("trusted-session"));
        using var client = CreateClient(host, true);
        using var response = await client.GetAsync(SalesEndpoints.ContactInformationPath
            + $"?partyId={PartyId:D}&companyAccountId={AccountId:D}");
        Assert.Equal(status, (int)response.StatusCode);
        AssertPrivateNoStore(response);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal(expectedPublicCode, document.RootElement.GetProperty("code").GetString());
        if (sourceCode is not null) Assert.DoesNotContain(sourceCode, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Contact_information_not_found_is_a_code_less_404()
    {
        var service = new StubService { ContactInformation = new(404, null, null) };
        using var host = CreateHost(service, new StubSessionBinding("trusted-session"));
        using var client = CreateClient(host, true);
        using var response = await client.GetAsync(SalesEndpoints.ContactInformationPath
            + $"?partyId={PartyId:D}&companyAccountId={AccountId:D}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Single(service.ContactQueries);
    }

    [Fact]
    public async Task Currencies_success_is_lossless_and_an_empty_list_is_a_200()
    {
        var service = new StubService
        {
            Currencies = new(200, new(TestCompanyIds.CompanyA, new DateOnly(2026, 9, 13),
                [new(TestCompanyIds.CompanyA, new DateOnly(2026, 9, 13), CurrencyId, "TRY", "TRY", ProfileId, "9007199254740993")]), null)
        };
        using var host = CreateHost(service, new StubSessionBinding("trusted-session"));
        using var client = CreateClient(host, true);
        using var response = await client.GetAsync(SalesEndpoints.CurrenciesPath + "?effectiveOn=2026-09-13");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivateNoStore(response);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(new[] { "companyId", "effectiveOn", "currencies" }, Keys(root));
        Assert.Equal("2026-09-13", root.GetProperty("effectiveOn").GetString());
        var currency = Assert.Single(root.GetProperty("currencies").EnumerateArray());
        Assert.Equal(new[] { "companyId", "effectiveOn", "currencyId", "code", "label", "profileId", "profileVersion" }, Keys(currency));
        Assert.Equal((CurrencyId, "TRY", "9007199254740993"), (currency.GetProperty("currencyId").GetGuid(),
            currency.GetProperty("code").GetString(), currency.GetProperty("profileVersion").GetString()));
        Assert.Equal(new SalesCurrencyListQueryV1(new DateOnly(2026, 9, 13)), Assert.Single(service.CurrencyQueries));

        service.Currencies = new(200, new(TestCompanyIds.CompanyA, new DateOnly(2026, 9, 13), []), null);
        using var empty = await client.GetAsync(SalesEndpoints.CurrenciesPath + "?effectiveOn=2026-09-13");
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        using var emptyDocument = JsonDocument.Parse(await empty.Content.ReadAsStringAsync());
        Assert.Empty(emptyDocument.RootElement.GetProperty("currencies").EnumerateArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData("?effectiveOn=09%2F13%2F2026")]
    [InlineData("?effectiveOn=2026-09-13&effectiveOn=2026-09-14")]
    [InlineData("?effectiveOn=2026-09-13&currencyId=66666666-6666-6666-6666-666666666666")]
    public async Task Currencies_reject_a_missing_non_iso_repeated_or_unknown_query_before_the_service(string query)
    {
        var service = new StubService();
        using var host = CreateHost(service, new StubSessionBinding("trusted-session"));
        using var client = CreateClient(host, true);
        using var response = await client.GetAsync(SalesEndpoints.CurrenciesPath + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(service.CurrencyQueries);
    }

    [Theory]
    [InlineData(403, "settings_access_denied", "forbidden")]
    [InlineData(503, "settings_provider_unavailable", "service_unavailable")]
    [InlineData(503, "settings_operation_unavailable", "service_unavailable")]
    public async Task Currencies_status_boundary_exposes_only_accepted_public_codes(int status, string sourceCode, string expectedPublicCode)
    {
        var service = new StubService { Currencies = new(status, null, sourceCode) };
        using var host = CreateHost(service, new StubSessionBinding("trusted-session"));
        using var client = CreateClient(host, true);
        using var response = await client.GetAsync(SalesEndpoints.CurrenciesPath + "?effectiveOn=2026-09-13");
        Assert.Equal(status, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal(expectedPublicCode, document.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain(sourceCode, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Contact_information_adapter_maps_the_owners_account_addresses_and_contacts_as_given()
    {
        var owner = new StubCounterpartyContactInformationService(OwnerView(TestCompanyIds.CompanyA));
        var adapter = new CounterpartySalesPartyAccountPortV1(new StubCompanyContext(TestCompanyIds.CompanyA),
            new StubCurrentUser("sales-reader", TestCompanyIds.CompanyA), new NoPartyRead(), owner);

        var answer = await adapter.GetContactInformationAsync(PartyId, AccountId);

        Assert.Equal(SalesPartyContactInformationOutcomeV1.Found, answer.Outcome);
        var information = answer.Information!;
        Assert.Equal((PartyId, AccountId, TestCompanyIds.CompanyA, "9007199254740993"),
            (information.PartyId, information.CompanyAccountId, information.CompanyId, information.AccountVersion));
        Assert.Equal(2, information.Addresses.Count);
        Assert.Equal(new SalesPartyAddressV1(AddressId, "3", true, "delivery", "TR", "Sanayi Cad. 1", null, "Kocaeli", "Gebze", "41400"),
            information.Addresses[0]);
        Assert.False(information.Addresses[1].IsActive);
        Assert.Equal(new SalesPartyContactV1(ContactId, "2", true, "Ayşe Yükleme", "Sevkiyat", "delivery"),
            Assert.Single(information.Contacts));
        Assert.Equal(PartyId, Assert.Single(owner.Reads));
    }

    [Fact]
    public async Task Contact_information_adapter_answers_not_found_for_a_missing_party_another_account_or_another_company()
    {
        var context = new StubCompanyContext(TestCompanyIds.CompanyA);
        var user = new StubCurrentUser("sales-reader", TestCompanyIds.CompanyA);

        var none = new CounterpartySalesPartyAccountPortV1(context, user, new NoPartyRead(), new StubCounterpartyContactInformationService(null));
        Assert.Equal(SalesPartyContactInformationOutcomeV1.NotFound, (await none.GetContactInformationAsync(PartyId, AccountId)).Outcome);

        var owner = new StubCounterpartyContactInformationService(OwnerView(TestCompanyIds.CompanyA));
        var adapter = new CounterpartySalesPartyAccountPortV1(context, user, new NoPartyRead(), owner);
        Assert.Equal(SalesPartyContactInformationOutcomeV1.NotFound, (await adapter.GetContactInformationAsync(PartyId, Guid.NewGuid())).Outcome);

        var foreign = new CounterpartySalesPartyAccountPortV1(context, user, new NoPartyRead(),
            new StubCounterpartyContactInformationService(OwnerView(TestCompanyIds.CompanyB)));
        Assert.Equal(SalesPartyContactInformationOutcomeV1.NotFound, (await foreign.GetContactInformationAsync(PartyId, AccountId)).Outcome);
    }

    [Fact]
    public async Task Contact_information_adapter_denies_before_the_owner_read_without_a_current_context_or_ids()
    {
        var owner = new StubCounterpartyContactInformationService(OwnerView(TestCompanyIds.CompanyA));
        var foreignUser = new CounterpartySalesPartyAccountPortV1(new StubCompanyContext(TestCompanyIds.CompanyA),
            new StubCurrentUser("sales-reader", TestCompanyIds.CompanyB), new NoPartyRead(), owner);
        Assert.Equal(SalesPartyContactInformationOutcomeV1.Denied, (await foreignUser.GetContactInformationAsync(PartyId, AccountId)).Outcome);

        var adapter = new CounterpartySalesPartyAccountPortV1(new StubCompanyContext(TestCompanyIds.CompanyA),
            new StubCurrentUser("sales-reader", TestCompanyIds.CompanyA), new NoPartyRead(), owner);
        Assert.Equal(SalesPartyContactInformationOutcomeV1.Denied, (await adapter.GetContactInformationAsync(Guid.Empty, AccountId)).Outcome);
        Assert.Equal(SalesPartyContactInformationOutcomeV1.Denied, (await adapter.GetContactInformationAsync(PartyId, Guid.Empty)).Outcome);
        Assert.Empty(owner.Reads);
    }

    [Fact]
    public async Task Contact_information_adapter_turns_the_owners_authorization_exception_into_denied()
    {
        var owner = new StubCounterpartyContactInformationService(null) { Error = new UnauthorizedAccessException("owner-internal-detail") };
        var adapter = new CounterpartySalesPartyAccountPortV1(new StubCompanyContext(TestCompanyIds.CompanyA),
            new StubCurrentUser("sales-reader", TestCompanyIds.CompanyA), new NoPartyRead(), owner);
        var answer = await adapter.GetContactInformationAsync(PartyId, AccountId);
        Assert.Equal(SalesPartyContactInformationOutcomeV1.Denied, answer.Outcome);
        Assert.Null(answer.Information);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("actor")]
    public async Task Contact_information_adapter_discards_the_owners_answer_after_a_late_context_change(string change)
    {
        var context = new StubCompanyContext(TestCompanyIds.CompanyA);
        var user = new StubCurrentUser("sales-reader", TestCompanyIds.CompanyA, TestCompanyIds.CompanyB);
        var owner = new StubCounterpartyContactInformationService(OwnerView(TestCompanyIds.CompanyA))
        {
            OnRead = () =>
            {
                if (change == "company") context.CompanyId = TestCompanyIds.CompanyB;
                else user.UserId = "other-reader";
            }
        };
        var adapter = new CounterpartySalesPartyAccountPortV1(context, user, new NoPartyRead(), owner);
        var answer = await adapter.GetContactInformationAsync(PartyId, AccountId);
        Assert.Equal(SalesPartyContactInformationOutcomeV1.Denied, answer.Outcome);
        Assert.Null(answer.Information);
        Assert.Single(owner.Reads);
    }

    private static CounterpartyContactInformationView OwnerView(Guid companyId) => new(PartyId, AccountId, companyId, "9007199254740993", true,
        new CounterpartyContactInformation(null, null, null, null,
            [
                new(AddressId, "3", true, "delivery", "TR", "Sanayi Cad. 1", null, "Kocaeli", "Gebze", "41400"),
                new(Guid.NewGuid(), "1", false, "delivery", "TR", "Old street", null, null, null, null),
            ],
            [new(ContactId, "2", true, "Ayşe Yükleme", "Sevkiyat", "delivery", [])]));

    private static string[] Keys(JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name)];

    /// <summary>The party read is not part of the contact-information member; reaching it is a test failure.</summary>
    private sealed class NoPartyRead : ICounterpartyReadService
    {
        public Task<CounterpartyPageExecution> GetAuthorizedPageAsync(CounterpartyReadRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CounterpartyDetailView?> GetAuthorizedPartyAsync(Guid partyId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static CounterpartyPageQuery ExpectedOwnerQuery(string? q, int pageSize, string? cursor) =>
        new(q, null, PartyRole.None, CounterpartyRoleMatch.Any, CounterpartyRecordState.Active,
            CounterpartyRecordState.Active, CounterpartySort.Code, CounterpartySortDirection.Ascending,
            pageSize, cursor, false);

    private static JsonElement ReadReferenceExample(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GoymenERP.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var path = Path.Combine(directory.FullName, "tests", "GoymenERP.Sales.UnitTests", "Examples", "reference-http.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty(name).Clone();
    }

    private static async Task AssertMatchesExampleAsync(JsonElement example, HttpResponseMessage response)
    {
        Assert.Equal(example.GetProperty("status").GetInt32(), (int)response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(example.GetProperty("body").GetProperty("status").GetInt32(),
            body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(example.GetProperty("body").GetProperty("code").GetString(),
            body.RootElement.GetProperty("code").GetString());
    }

    private WebApplicationFactory<Program> CreateRealHandlerHost(ICounterpartyReadService owner,
        IProductSalesEligibilityV1? products = null, ISettingsPrerequisiteProvider? settings = null) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICounterpartyReadService>();
            services.AddSingleton(owner);
            if (products is not null)
            {
                services.RemoveAll<IProductSalesEligibilityV1>();
                services.AddSingleton(products);
            }
            if (settings is not null)
            {
                services.RemoveAll<ISettingsPrerequisiteProvider>();
                services.AddSingleton(settings);
            }
            services.RemoveAll<SalesReferenceReadBindings>();
            services.AddSingleton(new SalesReferenceReadBindings(
                AccountsPermission: ControlledAccountsPermission, ProductPermission: ControlledProductPermission));
            services.RemoveAll<IErpAuthorizationService>();
            services.AddSingleton<IErpAuthorizationService>(new ControlledSalesAuthority());
            services.RemoveAll<IActorSessionBindingResolver>();
            services.AddSingleton<IActorSessionBindingResolver>(new StubSessionBinding("trusted-session"));
        }));

    private WebApplicationFactory<Program> CreateHost(ISalesReferenceReadService service,
        IActorSessionBindingResolver resolver) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISalesReferenceReadService>();
            services.AddSingleton(service);
            services.RemoveAll<IActorSessionBindingResolver>();
            services.AddSingleton(resolver);
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host, bool includeCompany)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        if (includeCompany)
            client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        return client;
    }

    private static void AssertPrivateNoStore(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("no-cache", response.Headers.Pragma.ToString(), StringComparison.Ordinal);
    }

    private sealed class StubSessionBinding(string? binding) : IActorSessionBindingResolver
    {
        public Task<string?> ResolveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(binding);
    }

    private sealed class StubService : ISalesReferenceReadService
    {
        public SalesReferenceReadResult<SalesAccountOptionsV1> Accounts { get; set; } = new(403, null, null);
        public SalesReferenceReadResult<SalesCurrencyReferenceV1> Currency { get; set; } = new(403, null, null);
        public SalesReferenceReadResult<SalesProductReferenceV1> Product { get; set; } = new(403, null, null);
        public SalesReferenceReadResult<SalesContactInformationOptionsV1> ContactInformation { get; set; } = new(403, null, null);
        public SalesReferenceReadResult<SalesCurrencyOptionsV1> Currencies { get; set; } = new(403, null, null);
        public List<string> Sessions { get; } = [];
        public int ProductCalls { get; private set; }
        public List<SalesContactInformationQueryV1> ContactQueries { get; } = [];
        public List<SalesCurrencyListQueryV1> CurrencyQueries { get; } = [];
        public Task<SalesReferenceReadResult<SalesAccountOptionsV1>> GetAccountsAsync(
            SalesAccountQueryV1 query, string sessionBinding, CancellationToken cancellationToken = default)
        { Sessions.Add(sessionBinding); return Task.FromResult(Accounts); }
        public Task<SalesReferenceReadResult<SalesCurrencyReferenceV1>> GetCurrencyAsync(
            SalesCurrencyQueryV1 query, CancellationToken cancellationToken = default) => Task.FromResult(Currency);
        public Task<SalesReferenceReadResult<SalesProductReferenceV1>> GetProductAsync(
            SalesProductQueryV1 query, CancellationToken cancellationToken = default)
        { ProductCalls++; return Task.FromResult(Product); }
        public Task<SalesReferenceReadResult<SalesContactInformationOptionsV1>> GetContactInformationAsync(
            SalesContactInformationQueryV1 query, CancellationToken cancellationToken = default)
        { ContactQueries.Add(query); return Task.FromResult(ContactInformation); }
        public Task<SalesReferenceReadResult<SalesCurrencyOptionsV1>> GetCurrenciesAsync(
            SalesCurrencyListQueryV1 query, CancellationToken cancellationToken = default)
        { CurrencyQueries.Add(query); return Task.FromResult(Currencies); }
    }

    /// <summary>SALES-A2a: the Cari owner's contact-information read, answered as configured; the command is never reached.</summary>
    private sealed class StubCounterpartyContactInformationService(CounterpartyContactInformationView? view)
        : ICounterpartyContactInformationService
    {
        public List<Guid> Reads { get; } = [];
        public Action? OnRead { get; init; }
        public Exception? Error { get; init; }
        public Task<CounterpartyContactInformationView?> GetContactInformationAsync(Guid partyId, CancellationToken cancellationToken)
        {
            Reads.Add(partyId);
            OnRead?.Invoke();
            return Error is null ? Task.FromResult(view) : Task.FromException<CounterpartyContactInformationView?>(Error);
        }
        public Task<CounterpartyCommandExecution> UpdateContactInformationAsync(Guid partyId, UpdateCounterpartyContactInformation command,
            CounterpartyCommandMetadata metadata, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubCurrentUser(string userId, params Guid[] companies) : ICurrentUser
    {
        private readonly HashSet<Guid> _companies = [.. companies];
        public bool IsAuthenticated => true;
        public string? UserId { get; set; } = userId;
        public IReadOnlySet<Guid> AllowedCompanyIds => _companies;
        public bool CanAccessCompany(Guid companyId) => _companies.Contains(companyId);
    }

    // Unlike the request-scoped CompanyContext, this can simulate a late company switch.
    private sealed class StubCompanyContext(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; set; } = companyId;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class StubCounterpartyReadService(CounterpartyPageExecution execution)
        : ICounterpartyReadService
    {
        public List<CounterpartyReadRequest> Requests { get; } = [];
        public Action? OnRead { get; init; }
        public Exception? Error { get; init; }
        public Task<CounterpartyPageExecution> GetAuthorizedPageAsync(CounterpartyReadRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            OnRead?.Invoke();
            return Error is null ? Task.FromResult(execution) : Task.FromException<CounterpartyPageExecution>(Error);
        }
        public Task<CounterpartyDetailView?> GetAuthorizedPartyAsync(Guid partyId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    // Raises the given Settings error on every unit read; the money read is never reached here.
    private sealed class StubSettings(SettingsPrerequisiteException error) : ISettingsPrerequisiteProvider
    {
        public int UnitReads { get; private set; }
        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default)
        { UnitReads++; return Task.FromException<UnitProfileV1>(error); }
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubProductEligibility(ProductSalesEligibilityV1 result) : IProductSalesEligibilityV1
    {
        public int Calls { get; private set; }
        public Task<ProductSalesEligibilityV1> EvaluateAsync(ProductSalesSelectionV1 selection,
            CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(result); }
    }

    // Grants only the two test-host bindings in company A; everything else is denied.
    private sealed class ControlledSalesAuthority : IErpAuthorizationService
    {
        private static PermissionScope Scope(string permission, Guid companyId) =>
            companyId == TestCompanyIds.CompanyA
            && permission is ControlledAccountsPermission or ControlledProductPermission
                ? new(true, true, new HashSet<string>(StringComparer.Ordinal), new HashSet<Guid>(),
                    new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied;

        public Task<bool> HasPermissionAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) => Task.FromResult(Scope(permission, companyId).IsGranted);

        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) => Task.FromResult(Scope(permission, companyId));

        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId,
            IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthorizationSnapshot("controlled-sales-reference",
                companyId == TestCompanyIds.CompanyA ? new HashSet<Guid> { companyId } : new HashSet<Guid>(),
                permissions.ToDictionary(permission => permission, permission => Scope(permission, companyId))));
    }
}
