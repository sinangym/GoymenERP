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
/// WP7-2 (AI-D31/AI-D32): the party legal name change Work inbox source lists the caller company's pending requests, oldest first,
/// labelled with the proposed name, the proposer as maker, a stale request flagged; its approve permission carries an underscore
/// (<c>counterparties.legal_name.approve</c>) and passes the widened contract shape (ASSUMPTION-WP7-PERM-1). SENTETIK / NOT_ENV.
/// </summary>
public sealed class CounterpartyLegalNameChangeWorkInboxSourceV1Tests
{
    private static readonly Guid CompanyA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 11, 0, 0, TimeSpan.Zero);
    private readonly string database = "counterparty-legal-name-inbox-" + Guid.NewGuid().ToString("N");
    private readonly HashSet<string> withheld = new(StringComparer.Ordinal);
    private readonly List<string> asked = [];

    [Fact]
    public async Task Pending_legal_name_requests_of_the_caller_company_are_listed_with_the_proposed_name_and_the_proposer_as_maker()
    {
        var party = new Party(Guid.NewGuid(), PartyCode.Parse("CAR-000002"), PartyKind.LegalEntity, "SENTETIK Eski Unvan A.Ş.");
        await SeedAsync(CompanyA, db => db.Parties.Add(party));
        var stale = Request(CompanyA, party.Id, party.Version + 3, Start);
        var current = Request(CompanyA, party.Id, party.Version, Start.AddMinutes(30));
        await SeedAsync(CompanyA, db => db.PartyLegalNameChangeRequests.AddRange(current, stale));
        await SeedAsync(CompanyB, db => db.PartyLegalNameChangeRequests.Add(Request(CompanyB, party.Id, party.Version, Start.AddDays(-1))));

        var source = Source();
        var result = await source.ListPendingAsync(Caller(), 5, CancellationToken.None);

        Assert.True(WorkInboxRulesV1.IsValidPermission(source.ApproverPermission));
        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        Assert.Equal([stale.Id, current.Id], result.Items!.Select(item => item.ItemId));
        var first = result.Items![0];
        Assert.Equal(("counterparty", "counterparty.legal-name-change.v1"), (first.Module, first.Kind));
        Assert.Equal("SENTETIK Yeni Unvan A.Ş.", first.DocumentLabel);
        Assert.Equal("/cari/" + party.Id.ToString("D"), first.Link);
        Assert.Equal("SENTETIK-maker", first.PreparedBy);
        Assert.Equal(("party", "SENTETIK Eski Unvan A.Ş."), (first.References[0].Role, first.References[0].Label));
        Assert.Contains(CounterpartyLegalNameChangeWorkInboxSourceV1.StaleRule, first.Rules.Select(rule => rule.RuleCode));
        Assert.DoesNotContain(CounterpartyLegalNameChangeWorkInboxSourceV1.StaleRule, result.Items[1].Rules.Select(rule => rule.RuleCode));
        Assert.All(result.Items, item => Assert.All(item.Rules, rule => Assert.True(WorkInboxRulesV1.IsValidRule("counterparty", rule))));
        Assert.Equal([CounterpartyLegalNamePermissionsV1.Approve, ErpPermissions.CounterpartyPartiesRead], asked.Take(2));
    }

    [Fact]
    public async Task Without_the_legal_name_approve_grant_the_source_is_denied()
    {
        withheld.Add(CounterpartyLegalNamePermissionsV1.Approve);

        var result = await Source().ListPendingAsync(Caller(), 5, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, result.Outcome);
        Assert.Null(result.Items);
    }

    private static PartyLegalNameChangeRequest Request(Guid company, Guid partyId, long partyVersion, DateTimeOffset at) =>
        new(Guid.NewGuid(), company, partyId, "SENTETIK Eski Unvan A.Ş.", "SENTETIK Yeni Unvan A.Ş.", partyVersion,
            "SENTETIK neden", "SENTETIK-maker", at);

    private async Task SeedAsync(Guid company, Action<CounterpartyDbContext> seed)
    {
        await using var context = Context(company);
        seed(context);
        await context.SaveChangesAsync();
    }

    private CounterpartyLegalNameChangeWorkInboxSourceV1 Source() =>
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

    private sealed class Authority(CounterpartyLegalNameChangeWorkInboxSourceV1Tests test) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            test.asked.Add(permission);
            return Task.FromResult(new PermissionScope(!test.withheld.Contains(permission), true, new HashSet<string>(StringComparer.Ordinal),
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }
}
