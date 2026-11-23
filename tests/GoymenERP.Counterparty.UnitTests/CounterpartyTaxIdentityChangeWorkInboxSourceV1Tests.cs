using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Counterparty.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Counterparty.UnitTests;

/// <summary>
/// WP7-2 (AI-D31/AI-D32, AT-009): the party tax identity change Work inbox source lists the caller company's pending requests,
/// oldest first, labelled with the party's legal name (no tax number), the maker as preparer, a stale request flagged; the caller
/// must hold the approve and party read names company-wide. SENTETIK / NOT_ENV.
/// </summary>
public sealed class CounterpartyTaxIdentityChangeWorkInboxSourceV1Tests
{
    private static readonly Guid CompanyA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);
    private readonly string database = "counterparty-work-inbox-" + Guid.NewGuid().ToString("N");
    private readonly HashSet<string> withheld = new(StringComparer.Ordinal);
    private readonly List<string> asked = [];

    [Fact]
    public async Task Pending_requests_of_the_caller_company_are_listed_oldest_first_without_tax_numbers()
    {
        var party = new Party(Guid.NewGuid(), PartyCode.Parse("CAR-000001"), PartyKind.LegalEntity, "SENTETIK Metal A.Ş.");
        await SeedAsync(null, db => db.Parties.Add(party));
        var newer = Request(CompanyA, party.Id, party.Version, Start.AddHours(1));
        var older = Request(CompanyA, party.Id, party.Version + 5, Start); // prepared against another party version: stale
        await SeedAsync(CompanyA, db => db.PartyTaxIdentityChangeRequests.AddRange(newer, older));
        await SeedAsync(CompanyB, db => db.PartyTaxIdentityChangeRequests.Add(Request(CompanyB, party.Id, party.Version, Start.AddDays(-1))));

        var result = await Source().ListPendingAsync(Caller(), 5, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        Assert.Equal([older.Id, newer.Id], result.Items!.Select(item => item.ItemId));
        var first = result.Items![0];
        Assert.Equal(("counterparty", "counterparty.tax-identity-change.v1"), (first.Module, first.Kind));
        Assert.Equal("SENTETIK Metal A.Ş.", first.DocumentLabel);
        Assert.Equal("/cari/" + party.Id.ToString("D"), first.Link);
        Assert.Equal("SENTETIK-maker", first.PreparedBy);
        Assert.Contains(CounterpartyTaxIdentityChangeWorkInboxSourceV1.StaleRule, first.Rules.Select(rule => rule.RuleCode));
        Assert.DoesNotContain(CounterpartyTaxIdentityChangeWorkInboxSourceV1.StaleRule, result.Items[1].Rules.Select(rule => rule.RuleCode));
        Assert.All(result.Items, item => Assert.All(item.Rules, rule => Assert.True(WorkInboxRulesV1.IsValidRule("counterparty", rule))));
        Assert.All(result.Items, item => Assert.DoesNotContain("9876543217", item.DocumentLabel));
        Assert.Equal([CounterpartyTaxIdentityPermissionsV1.Approve, ErpPermissions.CounterpartyPartiesRead], asked.Take(2));
    }

    [Fact]
    public async Task Without_party_read_or_approve_the_source_is_denied()
    {
        withheld.Add(ErpPermissions.CounterpartyPartiesRead);

        var result = await Source().ListPendingAsync(Caller(), 5, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, result.Outcome);
        Assert.Null(result.Items);
    }

    [Fact]
    public async Task A_request_whose_party_is_missing_makes_the_read_unavailable()
    {
        await SeedAsync(CompanyA, db => db.PartyTaxIdentityChangeRequests.Add(Request(CompanyA, Guid.NewGuid(), 1, Start)));

        var result = await Source().ListPendingAsync(Caller(), 5, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, result.Outcome);
    }

    private static PartyTaxIdentityChangeRequest Request(Guid company, Guid partyId, long partyVersion, DateTimeOffset at) =>
        new(Guid.NewGuid(), company, partyId, "1234567890", TaxIdentityNumber.Parse("9876543217"), partyVersion,
            "SENTETIK neden", "SENTETIK-maker", at);

    private async Task SeedAsync(Guid? company, Action<CounterpartyDbContext> seed)
    {
        await using var context = Context(company ?? CompanyA);
        seed(context);
        await context.SaveChangesAsync();
    }

    private CounterpartyTaxIdentityChangeWorkInboxSourceV1 Source() =>
        new(Context(CompanyA), new WorkInboxCompanyWideFenceV1(Resolved(CompanyA), new User(), new Authority(this)));

    private CounterpartyDbContext Context(Guid company) =>
        new(new DbContextOptionsBuilder<CounterpartyDbContext>().UseInMemoryDatabase(database).Options, Resolved(company));

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

    private sealed class Authority(CounterpartyTaxIdentityChangeWorkInboxSourceV1Tests test) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            test.asked.Add(permission);
            return Task.FromResult(new PermissionScope(!test.withheld.Contains(permission), true, new HashSet<string>(StringComparer.Ordinal),
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }
}
