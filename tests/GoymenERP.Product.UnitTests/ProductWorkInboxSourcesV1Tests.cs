using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.PriceLists;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using GoymenERP.Modules.Product.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Product.UnitTests;

/// <summary>
/// WP7-2 (AI-D31/AI-D32, SAT-D382/SAT-D416): the package standard version and price list publication Work inbox sources list
/// only the caller company's waiting records with the owner's maker and "may not decide" set, under the owner's company-wide
/// decision permission. SENTETIK / NOT_ENV.
/// </summary>
public sealed class ProductWorkInboxSourcesV1Tests
{
    private static readonly Guid CompanyA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 10, 0, 0, TimeSpan.Zero);
    private readonly string database = "product-work-inbox-" + Guid.NewGuid().ToString("N");
    private bool granted = true;
    private readonly List<string> asked = [];

    [Fact]
    public async Task Only_the_newest_undecided_package_standard_version_waits_with_its_preparer_and_entry_counts()
    {
        var product = Guid.NewGuid();
        await SeedAsync(CompanyA, db =>
        {
            db.PackageStandardVersions.AddRange(Package(CompanyA, 1, Start.AddDays(-2), null), Package(CompanyA, 2, Start, product));
            db.PackageStandardDecisions.Add(new ProductPackageStandardDecisionRow
            {
                CompanyId = CompanyA, Version = 1, DecisionId = Guid.NewGuid(), Decision = "Approved", Reason = "SENTETIK",
                DecidedAtUtc = Start.AddDays(-1), DecidedBy = "SENTETIK-checker", CorrelationId = "SENTETIK-corr",
            });
        });
        await SeedAsync(CompanyB, db => db.PackageStandardVersions.Add(Package(CompanyB, 1, Start.AddDays(-3), null)));

        var source = new ProductPackageStandardWorkInboxSourceV1(Context(CompanyA), Fence());
        var item = Assert.Single((await source.ListPendingAsync(Caller(), 5, CancellationToken.None)).Items!);

        Assert.Equal(("product", "product.package-standard-version.v1", "v2"), (item.Module, item.Kind, item.DocumentLabel));
        Assert.Equal("/urun/" + product.ToString("D"), item.Link);
        Assert.Equal("SENTETIK-maker", item.PreparedBy);
        Assert.Equal(Start, item.WaitingSinceUtc);
        Assert.Equal("12", item.Rules.Single(rule => rule.RuleCode == ProductPackageStandardWorkInboxSourceV1.EntryCountRule).Actual);
        Assert.All(item.Rules, rule => Assert.True(WorkInboxRulesV1.IsValidRule("product", rule)));
        Assert.All(item.References, reference => Assert.True(WorkInboxRulesV1.IsValidReference(reference)));
        Assert.Equal([ProductPackageStandardPermissionsV3.Approve], asked.Distinct());
    }

    [Fact]
    public async Task A_fully_decided_package_standard_table_has_nothing_waiting()
    {
        await SeedAsync(CompanyA, db =>
        {
            db.PackageStandardVersions.Add(Package(CompanyA, 1, Start, null));
            db.PackageStandardDecisions.Add(new ProductPackageStandardDecisionRow
            {
                CompanyId = CompanyA, Version = 1, DecisionId = Guid.NewGuid(), Decision = "Rejected", Reason = "SENTETIK",
                DecidedAtUtc = Start.AddHours(1), DecidedBy = "SENTETIK-checker", CorrelationId = "SENTETIK-corr",
            });
        });

        var result = await new ProductPackageStandardWorkInboxSourceV1(Context(CompanyA), Fence()).ListPendingAsync(Caller(), 5, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        Assert.Empty(result.Items!);
    }

    [Fact]
    public async Task Draft_price_list_versions_wait_and_every_editor_besides_the_creator_may_not_publish()
    {
        var list = PriceList(CompanyA, "SENTETIK Liste");
        var draft = Version(CompanyA, list.Id, 2, PriceListVersionState.Draft, Start, """["SENTETIK-maker","SENTETIK-editor"]""");
        await SeedAsync(CompanyA, db =>
        {
            db.PriceLists.Add(list);
            db.Versions.AddRange(Version(CompanyA, list.Id, 1, PriceListVersionState.Published, Start.AddDays(-5), """["SENTETIK-maker"]"""), draft);
        });
        var foreign = PriceList(CompanyB, "SENTETIK B");
        await SeedAsync(CompanyB, db =>
        {
            db.PriceLists.Add(foreign);
            db.Versions.Add(Version(CompanyB, foreign.Id, 1, PriceListVersionState.Draft, Start.AddDays(-1), """["SENTETIK-maker"]"""));
        });

        var source = new ProductPriceListVersionWorkInboxSourceV1(Context(CompanyA), Fence());
        var item = Assert.Single((await source.ListPendingAsync(Caller(), 5, CancellationToken.None)).Items!);

        Assert.Equal(draft.VersionId, item.ItemId);
        Assert.Equal(("product.price-list-version.v1", "SENTETIK Liste v2"), (item.Kind, item.DocumentLabel));
        Assert.Equal("/fiyat-listeleri/" + list.Id.ToString("D"), item.Link);
        Assert.Equal("SENTETIK-maker", item.PreparedBy);
        Assert.Equal(["SENTETIK-editor"], item.ExcludedDeciders);
        Assert.True(item.Excludes("SENTETIK-editor"));
        Assert.Equal([ProductPriceListVersionWorkInboxSourceV1.PublicationRequiredRule, ProductPriceListVersionWorkInboxSourceV1.NextVersionRule],
            item.Rules.Select(rule => rule.RuleCode));
        Assert.Contains(ProductPriceListPermissionsV1.Publish, asked);
    }

    [Fact]
    public async Task Without_the_owner_decision_permission_both_sources_are_denied()
    {
        granted = false;

        var package = await new ProductPackageStandardWorkInboxSourceV1(Context(CompanyA), Fence()).ListPendingAsync(Caller(), 5, CancellationToken.None);
        var price = await new ProductPriceListVersionWorkInboxSourceV1(Context(CompanyA), Fence()).ListPendingAsync(Caller(), 5, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, package.Outcome);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, price.Outcome);
    }

    private static ProductPackageStandardVersionRow Package(Guid company, long version, DateTimeOffset at, Guid? product) => new()
    {
        CompanyId = company, Version = version, VersionId = Guid.NewGuid(), SourceKind = product is null ? "Import" : "Edit",
        SourceLabel = product is null ? "SENTETIK kaynak" : null, Reason = "SENTETIK", EditedProductId = product, EntryCount = 12,
        CustomerEntryCount = 0, RequiresApproval = true, CreatedAtUtc = at, CreatedBy = "SENTETIK-maker", CorrelationId = "SENTETIK-corr",
    };

    private static PriceListRow PriceList(Guid company, string name) => new()
    {
        Id = Guid.NewGuid(), CompanyId = company, Name = name, CurrencyCode = "TRY", Channel = 1, VatBasis = 1, ScopeKind = 1,
        Version = 1, RowVersion = [1], CreatedAtUtc = Start.AddDays(-10), UpdatedAtUtc = Start.AddDays(-10),
        CreatedBy = "SENTETIK-maker", CorrelationId = "SENTETIK-corr",
    };

    private static PriceListVersionRow Version(Guid company, Guid listId, int number, PriceListVersionState state, DateTimeOffset at,
        string preparers) => new()
    {
        CompanyId = company, PriceListId = listId, VersionNo = number, VersionId = Guid.NewGuid(), ValidFrom = new DateOnly(2026, 12, number),
        State = (int)state, CreatedAtUtc = at, CreatedBy = "SENTETIK-maker", PreparerActorIdsJson = preparers,
        PublishedAtUtc = state == PriceListVersionState.Published ? at.AddHours(1) : null,
        PublishedBy = state == PriceListVersionState.Published ? "SENTETIK-publisher" : null,
    };

    private async Task SeedAsync(Guid company, Action<ProductPriceListDbContext> seed)
    {
        await using var context = Context(company);
        seed(context);
        await context.SaveChangesAsync();
    }

    private ProductPriceListDbContext Context(Guid company) =>
        new(new DbContextOptionsBuilder<ProductPriceListDbContext>().UseInMemoryDatabase(database).Options, Resolved(company));

    private WorkInboxCompanyWideFenceV1 Fence() => new(Resolved(CompanyA), new User(), new Authority(this));

    private static CompanyContext Resolved(Guid company)
    {
        var context = new CompanyContext();
        context.Set(company);
        return context;
    }

    private static WorkInboxSourceContextV1 Caller() => new(CompanyA, "SENTETIK-checker", "SENTETIK-correlation", null);

    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => "SENTETIK-checker";
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { CompanyA, CompanyB };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class Authority(ProductWorkInboxSourcesV1Tests test) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            test.asked.Add(permission);
            return Task.FromResult(new PermissionScope(test.granted, true, new HashSet<string>(StringComparer.Ordinal),
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }
}
