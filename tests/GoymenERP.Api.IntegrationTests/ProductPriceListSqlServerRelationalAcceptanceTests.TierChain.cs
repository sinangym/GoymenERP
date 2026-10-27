using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// MOD-02 (SAT-D9 / SAT-D10) on a REAL SQL Server, every row written through the Product owner's service and store: a
/// customer-specific list beats the company list for its own customer only; V1 stays company-wide; another company sees
/// nothing; the database holds the scope; the Cari owner's answer gates a customer list; the rollback refuses while a
/// customer list exists (51072). The Cari answer is a synthetic port (the host adapter is composed by the central host).
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class ProductPriceListSqlServerRelationalAcceptanceTests
{
    private static readonly Guid TierParty = Guid.Parse("5a5a5a5a-5a5a-4a5a-8a5a-5a5a5a5a5a5a");
    private static readonly Guid TierAccount = Guid.Parse("6b6b6b6b-6b6b-4b6b-8b6b-6b6b6b6b6b6b");
    private static readonly Guid OtherTierParty = Guid.Parse("7c7c7c7c-7c7c-4c7c-8c7c-7c7c7c7c7c7c");
    private static readonly Guid OtherTierAccount = Guid.Parse("8d8d8d8d-8d8d-4d8d-8d8d-8d8d8d8d8d8d");

    [SqlServerFact]
    public async Task A_customer_list_wins_for_its_own_customer_only_V1_stays_company_wide_and_the_database_holds_the_scope()
    {
        await using var run = await Run.StartAsync();
        var today = ProductPriceBusinessDayV2.DateOf(DateTimeOffset.UtcNow);
        var publisher = Guid.NewGuid();
        var customers = new FixedCustomers(TestCompanyIds.CompanyA);
        var companyList = await CreatePublishedAsync(run, publisher, "SENTETIK şirket", "100", today, null, customers);
        var customerList = await CreatePublishedAsync(run, publisher, "SENTETIK cari özel", "90", today,
            new PriceListScopeRequestV1("Customer", TierParty, TierAccount), customers);
        Assert.Equal((TierParty, TierAccount), customers.LastAsked);

        await using (var scope = run.Request())
        {
            var view = await scope.Service.GetAsync(customerList);
            Assert.Equal(new PriceListScopeViewV1("Customer", TierParty, TierAccount), view.Scope);
            Assert.Equal(PriceListScopeViewV1.Company, (await scope.Service.GetAsync(companyList)).Scope);
            Assert.Equal([("SENTETIK cari özel", "Customer"), ("SENTETIK şirket", "Company")], (await scope.Service.ListAsync(null, null)).Items
                .OrderBy(item => item.Name, StringComparer.Ordinal).Select(item => (item.Name, item.Scope.Kind)));

            var own = await scope.ChainReader.ResolveAsync(Ask(today, new ProductPriceCustomerV2(TierParty, TierAccount)));
            Assert.Equal((ProductPriceResolutionOutcomeV2.Resolved, (ProductPriceTierV2?)ProductPriceTierV2.Customer, today, "Europe/Istanbul"),
                (own.Outcome, own.MatchedTier, own.EffectiveOn, own.BusinessTimeZone));
            Assert.Equal((customerList, 1, 90m, ProductPriceTierV2.Customer), (own.Reference!.PriceListId, own.Reference.VersionNo,
                own.Reference.UnitPrice, own.Reference.Tier));
            Assert.Equal([ProductPriceTierStatusV2.Unavailable, ProductPriceTierStatusV2.Matched, ProductPriceTierStatusV2.NotReached,
                ProductPriceTierStatusV2.NotReached], own.Tiers.Select(tier => tier.Status));
            Assert.Equal((ProductPackageStandardStatusV2.Unavailable, ProductPriceResolutionReasonsV2.PackageStandardOwnerMissing),
                (own.PackageStandard.Status, own.PackageStandard.Reason));

            // Another customer — or the right account under another party — gets the company list, never this customer's.
            foreach (var other in (ProductPriceCustomerV2[])[new(OtherTierParty, OtherTierAccount), new(OtherTierParty, TierAccount)])
            {
                var answer = await scope.ChainReader.ResolveAsync(Ask(today, other));
                Assert.Equal((ProductPriceTierV2.Company, companyList), (answer.MatchedTier!.Value, Assert.Single(answer.Candidates).PriceListId));
                Assert.Equal(ProductPriceTierStatusV2.NoMatch, answer.Tiers[1].Status);
            }

            var anonymous = await scope.ChainReader.ResolveAsync(Ask(today, null));
            Assert.Equal((ProductPriceTierV2.Company, ProductPriceTierStatusV2.NotRequested),
                (anonymous.MatchedTier!.Value, anonymous.Tiers[1].Status));

            // Before the start nothing answers: no zero, no default, no older version.
            var before = await scope.ChainReader.ResolveAsync(Ask(today.AddDays(-1), new ProductPriceCustomerV2(TierParty, TierAccount)));
            Assert.Equal((ProductPriceResolutionOutcomeV2.NoPrice, (ProductPriceReferenceV2?)null), (before.Outcome, before.Reference));
            Assert.Empty(before.Candidates);
            Assert.Equal(ProductPriceResolutionOutcomeV2.NoPrice, (await scope.ChainReader.ResolveAsync(
                Ask(today, new ProductPriceCustomerV2(TierParty, TierAccount)) with { CurrencyCode = "USD" })).Outcome);

            // V1 is unchanged for company-wide lists and never answers a customer-specific one.
            Assert.Equal(companyList, Assert.Single(await scope.QuantityReader.SearchAsync(ProductId, Kg, 1m, "TRY", today)).PriceListId);
            Assert.Null(await scope.QuantityReader.GetAsync(customerList, 1, ProductId, Kg, 1m, today));
            Assert.NotNull(await scope.QuantityReader.GetAsync(companyList, 1, ProductId, Kg, 1m, today));
        }

        // Another company sees neither list at any tier.
        await using (var scope = run.Request(TestCompanyIds.CompanyB))
        {
            var foreign = await scope.ChainReader.ResolveAsync(Ask(today, new ProductPriceCustomerV2(TierParty, TierAccount)));
            Assert.Equal(ProductPriceResolutionOutcomeV2.NoPrice, foreign.Outcome);
            Assert.Empty(foreign.Candidates);
        }

        // The database holds the scope: stored as written, no other kind and no half-named customer.
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT ScopeKind FROM product.PriceLists WHERE Id = @id", customerList));
        Assert.Equal(TierAccount.ToString("D"), (await run.ScalarAsync<string>(
            "SELECT CONVERT(nvarchar(36), ScopeCompanyAccountId) FROM product.PriceLists WHERE Id = @id", customerList)).ToLowerInvariant());
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT ScopeKind FROM product.PriceLists WHERE Id = @id AND ScopePartyId IS NULL AND ScopeCompanyAccountId IS NULL", companyList));
        foreach (var update in (string[])["UPDATE product.PriceLists SET ScopeKind = 2 WHERE Id = @id",
                     "UPDATE product.PriceLists SET ScopePartyId = NULL WHERE Id = @id"])
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(update, customerList));
            Assert.Equal(547, refused.Number);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id AND EventType = N'product.price-list.created.v1' "
            + "AND AfterJson LIKE N'%\"kind\":\"Customer\"%'", customerList.ToString("D")));

        // The rollback refuses while a customer-specific list exists (forward fix); nothing is dropped.
        await using (var scope = run.Request())
        {
            var migrator = scope.Context.GetService<IMigrator>();
            var refusal = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync("20260930113416_AddProductPriceLists"));
            Assert.Equal(51072, refusal.Number);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PriceLists WHERE ScopeKind = 3"));
    }

    [SqlServerFact]
    public async Task A_customer_list_needs_the_Cari_owners_answer_for_an_active_customer_of_this_company()
    {
        await using var run = await Run.StartAsync();
        var today = ProductPriceListWire.FormatDate(ProductPriceBusinessDayV2.DateOf(DateTimeOffset.UtcNow));
        var cases = new (string Key, IProductPriceListCustomerPortV1? Port, int Status, string Code, string? FieldCode)[]
        {
            ("no-port", null, 503, ProductPriceListCodesV1.CustomerProviderUnavailable, null),
            ("not-found", new FixedCustomers(TestCompanyIds.CompanyA, ProductPriceListCustomerOutcomeV1.NotFound), 422,
                ProductPriceListCodesV1.ValidationFailed, ProductPriceListCodesV1.CustomerInvalid),
            ("inactive", new FixedCustomers(TestCompanyIds.CompanyA, accountActive: false), 422, ProductPriceListCodesV1.ValidationFailed,
                ProductPriceListCodesV1.CustomerNotActive),
            ("denied", new FixedCustomers(TestCompanyIds.CompanyA, ProductPriceListCustomerOutcomeV1.Denied), 403,
                ProductPriceListCodesV1.Forbidden, null),
            ("other-company", new FixedCustomers(TestCompanyIds.CompanyB), 503, ProductPriceListCodesV1.CustomerProviderUnavailable, null),
            ("unavailable", new FixedCustomers(TestCompanyIds.CompanyA, ProductPriceListCustomerOutcomeV1.Unavailable), 503,
                ProductPriceListCodesV1.CustomerProviderUnavailable, null),
        };

        foreach (var (key, port, status, code, fieldCode) in cases)
        {
            var listId = Guid.NewGuid();
            await using var scope = run.Request(customers: port);
            var refused = await Assert.ThrowsAsync<ProductPriceListException>(() => scope.Service.CreateAsync(new CreatePriceListRequestV1(listId,
                Guid.NewGuid(), $"SENTETIK {key}", "TRY", "Sales", "ExcludingVat", today, null,
                [new PriceListLineRequestV1(Guid.NewGuid(), ProductId, Kg, "10", null)],
                new PriceListScopeRequestV1("Customer", TierParty, TierAccount)), Metadata($"customer-{key}")));
            Assert.Equal((status, code), (refused.Status, refused.Code));
            Assert.Equal(fieldCode, refused.Errors.SingleOrDefault()?.Code);
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PriceLists WHERE Id = @id", listId));
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PriceLists"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Product.PriceList'"));
    }

    private static ProductPriceResolutionRequestV2 Ask(DateOnly on, ProductPriceCustomerV2? customer) =>
        new(ProductId, Kg, 1m, "TRY", on, customer);

    /// <summary>Create (this run's actor) and publish (another actor) one list with one line, through the owner's service.</summary>
    private static async Task<Guid> CreatePublishedAsync(Run run, Guid publisher, string name, string price, DateOnly validFrom,
        PriceListScopeRequestV1? scope, IProductPriceListCustomerPortV1 customers)
    {
        var listId = Guid.NewGuid();
        await using (var creator = run.Request(customers: customers))
        {
            await creator.Service.CreateAsync(new CreatePriceListRequestV1(listId, Guid.NewGuid(), name, "TRY", "Sales", "ExcludingVat",
                ProductPriceListWire.FormatDate(validFrom), null, [new PriceListLineRequestV1(Guid.NewGuid(), ProductId, Kg, price, null)], scope),
                Metadata($"create-{listId:N}"));
        }

        await using (var publishing = run.Request(actor: publisher))
        {
            await publishing.Service.PublishAsync(listId, new PublishPriceListVersionRequestV1("1", 1), Metadata($"publish-{listId:N}"));
        }

        return listId;
    }

    /// <summary>A synthetic Cari owner answer: Found for any asked party/account in <paramref name="companyId"/>, or the given outcome.</summary>
    private sealed class FixedCustomers(Guid companyId, ProductPriceListCustomerOutcomeV1 outcome = ProductPriceListCustomerOutcomeV1.Found,
        bool accountActive = true) : IProductPriceListCustomerPortV1
    {
        public (Guid Party, Guid Account)? LastAsked { get; private set; }

        public Task<ProductPriceListCustomerAnswerV1> GetAuthorizedAsync(Guid partyId, Guid companyAccountId,
            CancellationToken cancellationToken = default)
        {
            LastAsked = (partyId, companyAccountId);
            return Task.FromResult(outcome == ProductPriceListCustomerOutcomeV1.Found
                ? new ProductPriceListCustomerAnswerV1(outcome,
                    new ProductPriceListCustomerV1(partyId, companyAccountId, companyId, PartyIsActive: true, accountActive))
                : new ProductPriceListCustomerAnswerV1(outcome, null));
        }
    }
}
