using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Accounting.Domain.Journals;

namespace GoymenERP.Accounting.UnitTests;

public sealed class AccountingCogsAccountMappingTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Product = Guid.NewGuid();
    private static readonly Guid Account = Guid.NewGuid();

    [Fact]
    public void Defaults_use_only_the_exact_codes_of_the_actual_approved_plan_without_inventing_accounts()
    {
        var manufactured = Guid.NewGuid();
        var plan = new AccountPlanMirrorV1(Company, "SENTETIK-plan", new string('a', 64), Guid.NewGuid(), DateTimeOffset.UtcNow,
            [new(new AccountRef(Company, manufactured), "620", "SENTETIK mamul"), new(new AccountRef(Company, Account), "621.01", "SENTETIK alt hesap")]);
        var defaults = AccountingCogsMappingDefaultsV1.FromApprovedPlan(plan);
        Assert.Null(defaults.Single(x => x.MappingKey == "trade-goods").AccountId);
        Assert.Equal(manufactured, defaults.Single(x => x.MappingKey == "manufactured").AccountId);
        Assert.All(AccountingCogsMappingDefaultsV1.FromApprovedPlan(null), value => Assert.Null(value.AccountId));
    }

    [Fact]
    public void Cogs_mapping_keys_never_accept_item_type_export_or_tax_as_a_classification_substitute()
    {
        var valid = new AccountingPostingAccountMappingRowRequestV1("cost-of-goods-sold", "trade-goods", null, null, null, Account);
        Assert.Null(AccountingPostingAccountMappingPayloadV1.Validate([valid], out _));
        foreach (var invalid in new[] { valid with { ItemType = "Steel" }, valid with { LinePurpose = "resale" },
            valid with { IsExport = false }, valid with { VatRateCode = "KDV20" } })
            Assert.Equal(AccountingPostingAccountMappingCodesV1.SelectorInvalid,
                AccountingPostingAccountMappingPayloadV1.Validate([invalid], out _)!.Code);
        Assert.False(AccountingPostingAccountMappingKeysV1.IsAllowed("sales-invoice", "trade-goods"));
        Assert.False(AccountingPostingAccountMappingKeysV1.IsAllowed("cost-of-goods-sold", "inventory"));
    }

    [Fact]
    public async Task The_real_classification_and_mapping_versions_are_retained_for_an_immutable_proposal_snapshot()
    {
        var company = Context(); var product = new Products(); var mappings = new Mappings();
        var result = await new AccountingCogsAccountResolverV1(product, mappings, company).ResolveAsync(Company, Product, new(2026, 11, 17));
        Assert.Equal("Available", result.Outcome);
        Assert.Equal(new AccountingCogsAccountSnapshotV1(Company, Product, InvoiceLineCogsClassificationV1.Manufactured,
            "7", "3", Account, 5), result.Value);
        Assert.Equal(("cost-of-goods-sold", "manufactured"), mappings.Question);
    }

    [Fact]
    public async Task Missing_class_or_mapping_never_becomes_the_620_or_621_default()
    {
        var company = Context(); var product = new Products { Answer = new(Company, Product, "Missing", null, null, null) };
        var mappings = new Mappings(); var resolver = new AccountingCogsAccountResolverV1(product, mappings, company);
        Assert.Equal("ClassificationMissing", (await resolver.ResolveAsync(Company, Product, new(2026, 11, 17))).Outcome);
        Assert.Equal(0, mappings.Calls);
        product.Answer = Products.Valid(); mappings.Answer = AccountingPostingAccountMappingResolutionV1.Unmapped(5);
        var missing = await resolver.ResolveAsync(Company, Product, new(2026, 11, 17));
        Assert.Equal("MappingMissing", missing.Outcome); Assert.Null(missing.Value);
    }

    [Fact]
    public async Task Company_drift_during_owner_reads_never_returns_an_account_snapshot()
    {
        var company = new MutableCompanyContextForFenceTests(Company); var mappings = new Mappings { AfterRead = () => company.Set(Guid.NewGuid()) };
        var result = await new AccountingCogsAccountResolverV1(new Products(), mappings, company).ResolveAsync(Company, Product, new(2026, 11, 17));
        Assert.Equal("Unavailable", result.Outcome); Assert.Null(result.Value);
    }

    private static CompanyContext Context() { var context = new CompanyContext(); context.Set(Company); return context; }
    private sealed class Products : IAccountingInvoiceLineProductReferenceV1
    {
        public InvoiceLineCogsProductReferenceV1 Answer { get; set; } = Valid();
        public static InvoiceLineCogsProductReferenceV1 Valid() => new(Company, Product, "Available", InvoiceLineCogsClassificationV1.Manufactured, "7", "3");
        public Task<InvoiceLineCogsProductReferenceV1> GetAsync(Guid companyId, Guid productId, CancellationToken cancellationToken = default) => Task.FromResult(Answer);
    }
    private sealed class Mappings : IAccountingPostingAccountMappingResolverV1
    {
        public AccountingPostingAccountMappingResolutionV1 Answer { get; set; } = AccountingPostingAccountMappingResolutionV1.Mapped(Account, 5);
        public Action? AfterRead { get; init; }
        public int Calls { get; private set; }
        public (string Purpose, string Key)? Question { get; private set; }
        public Task<AccountingPostingAccountMappingResolutionV1> ResolveAsync(Guid companyId, string purpose, DateOnly businessDate,
            string mappingKey, string? itemType = null, string? linePurpose = null, bool? isExport = null, string? vatRateCode = null,
            CancellationToken cancellationToken = default)
        {
            Calls++; Question = (purpose, mappingKey);
            Assert.Null(itemType); Assert.Null(linePurpose); Assert.Null(isExport); Assert.Null(vatRateCode);
            AfterRead?.Invoke(); return Task.FromResult(Answer);
        }
    }
}
