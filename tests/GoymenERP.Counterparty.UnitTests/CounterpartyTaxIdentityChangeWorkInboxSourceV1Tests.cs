using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
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
    private Func<Task>? beforeFresh;

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
        Assert.Equal("/onaylarim/counterparty.tax-identity-change.v1/" + older.Id.ToString("D"), first.Link);
        Assert.Equal("/cari/" + party.Id.ToString("D"), Assert.Single(first.References).Link);
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

    [Theory]
    [InlineData("Pending")] [InlineData("Approved")] [InlineData("Rejected")]
    public async Task Decision_purpose_keeps_native_company_account_and_terminal_maker_metadata_without_tax_data(string state)
    {
        var party = new Party(Guid.NewGuid(), PartyCode.Parse("CAR-000001"), PartyKind.LegalEntity, "SENTETIK Metal");
        await SeedAsync(CompanyA, db => { db.Parties.Add(party); db.CompanyPartyAccounts.Add(new CompanyPartyAccount(Guid.NewGuid(), CompanyA, party.Id, PartyRole.Customer)); });
        var row = Request(CompanyA, party.Id, party.Version, Start);
        if (state == "Approved") row.Approve("SENTETIK-checker", null, Start.AddHours(1));
        if (state == "Rejected") row.Reject("SENTETIK-checker", null, Start.AddHours(1));
        await SeedAsync(CompanyA, db => db.PartyTaxIdentityChangeRequests.Add(row));
        // The native global-party fence needs an account's existence, not an invented active-account rule.
        await using (var db = Context(CompanyA))
        {
            var account = await db.CompanyPartyAccounts.SingleAsync();
            account.Deactivate();
            await db.SaveChangesAsync();
        }
        var value = await Source().ReadDecisionAsync(Caller(), row.Id, default);
        Assert.NotNull(value); Assert.Equal("1", value.SourceVersion); Assert.Equal(state == "Pending", value.Pending);
        Assert.Equal("SENTETIK-maker", value.Item.PreparedBy);
        Assert.Equal(row.Id, value.Item.ItemId); Assert.Equal(CompanyA, value.CompanyId);
        Assert.Equal(("party", (Guid?)party.Id), (Assert.Single(value.Item.References).Role, Assert.Single(value.Item.References).RecordId));
        Assert.Equal("/onaylarim/counterparty.tax-identity-change.v1/" + row.Id.ToString("D"), value.Item.Link);
        Assert.Equal("/cari/" + party.Id.ToString("D"), Assert.Single(value.Item.References).Link);
        var wire = System.Text.Json.JsonSerializer.Serialize(value);
        Assert.DoesNotContain("1234567890", wire); Assert.DoesNotContain("9876543217", wire);
    }

    [Fact]
    public async Task Decision_purpose_never_discloses_a_request_without_the_native_company_account_or_both_permissions()
    {
        var party = new Party(Guid.NewGuid(), PartyCode.Parse("CAR-000001"), PartyKind.LegalEntity, "SENTETIK Metal");
        await SeedAsync(CompanyA, db => db.Parties.Add(party));
        var row = Request(CompanyA, party.Id, party.Version, Start);
        await SeedAsync(CompanyA, db => db.PartyTaxIdentityChangeRequests.Add(row));
        var foreign = Request(CompanyB, party.Id, party.Version, Start);
        await SeedAsync(CompanyB, db => db.PartyTaxIdentityChangeRequests.Add(foreign));
        Assert.Null(await Source().ReadDecisionAsync(Caller(), row.Id, default));
        Assert.Null(await Source().ReadDecisionAsync(Caller(), foreign.Id, default));
        await SeedAsync(CompanyA, db => db.CompanyPartyAccounts.Add(new CompanyPartyAccount(Guid.NewGuid(), CompanyA, party.Id, PartyRole.Customer)));
        foreach (var permission in new[] { ErpPermissions.CounterpartyPartiesRead, CounterpartyTaxIdentityPermissionsV1.Approve })
        { withheld.Clear(); withheld.Add(permission); Assert.Null(await Source().ReadDecisionAsync(Caller(), row.Id, default)); }
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Decision_purpose_refuses_a_request_or_party_changed_during_the_bounded_projection_instead_of_mixing_status_and_version(bool requestChanged)
    {
        var party = new Party(Guid.NewGuid(), PartyCode.Parse("CAR-000001"), PartyKind.LegalEntity, "SENTETIK Metal");
        await SeedAsync(CompanyA, db => { db.Parties.Add(party); db.CompanyPartyAccounts.Add(new CompanyPartyAccount(Guid.NewGuid(), CompanyA, party.Id, PartyRole.Customer)); });
        var row = Request(CompanyA, party.Id, party.Version, Start);
        await SeedAsync(CompanyA, db => db.PartyTaxIdentityChangeRequests.Add(row));
        beforeFresh = async () =>
        {
            if (asked.Count != 3) return;
            await using var db = Context(CompanyA);
            if (requestChanged)
            {
                var current = await db.PartyTaxIdentityChangeRequests.SingleAsync();
                current.Reject("SENTETIK-other-checker", null, Start.AddHours(1));
            }
            else (await db.Parties.SingleAsync()).ChangeTradeName("SENTETIK later trade name", Start.AddHours(1));
            await db.SaveChangesAsync();
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Source().ReadDecisionAsync(Caller(), row.Id, default));
    }

    [Fact]
    public async Task Native_250_character_names_keep_exact_human_proof_with_code_captions_without_widening_Work_or_serializing_sensitive_values()
    {
        var currentName = new string('A', 250);
        var party = new Party(Guid.NewGuid(), PartyCode.Parse("CAR-000001"), PartyKind.LegalEntity, currentName);
        await SeedAsync(CompanyA, db => { db.Parties.Add(party); db.CompanyPartyAccounts.Add(new CompanyPartyAccount(Guid.NewGuid(), CompanyA, party.Id, PartyRole.Customer)); });
        var row = Request(CompanyA, party.Id, party.Version, Start);
        await SeedAsync(CompanyA, db => db.PartyTaxIdentityChangeRequests.Add(row));
        var value = await Source().ReadDecisionAsync(Caller(), row.Id, default); Assert.NotNull(value);
        Assert.Equal("CAR-000001", value.Item.DocumentLabel); Assert.Equal("CAR-000001", Assert.Single(value.Item.References).Label);
        var proof = Assert.IsType<WorkCounterpartyTaxIdentityChangePreviewV1>(value.CounterpartyTaxIdentityChange);
        Assert.Equal((party.Id, row.Id, "1234567890", "9876543217", "vkn", row.Reason),
            (proof.PartyId, proof.ChangeRequestId, proof.CurrentTaxIdentityNumber, proof.ProposedTaxIdentityNumber, proof.ProposedKind, proof.Reason));
        var snapshotWire = System.Text.Json.JsonSerializer.Serialize(value);
        Assert.DoesNotContain("1234567890", snapshotWire); Assert.DoesNotContain("9876543217", snapshotWire);
        Assert.DoesNotContain(new string('B', 250), snapshotWire);
        var inbox = await Source().ListPendingAsync(Caller(), 5, default);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, inbox.Outcome); var item = Assert.Single(inbox.Items!);
        Assert.Equal("CAR-000001", item.DocumentLabel); Assert.Equal("CAR-000001", Assert.Single(item.References).Label);
        var inboxWire = System.Text.Json.JsonSerializer.Serialize(inbox);
        Assert.DoesNotContain("1234567890", inboxWire); Assert.DoesNotContain("9876543217", inboxWire);
        Assert.DoesNotContain(new string('B', 250), inboxWire);
        Assert.Equal(200, WorkInboxRulesV1.MaxDocumentLabelLength); Assert.Equal(200, WorkInboxRulesV1.MaxReferenceLabelLength);
    }

    [Fact]
    public async Task Historical_current_tax_value_is_displayed_raw_under_the_native_stored_bounds_without_a_new_checksum_gate()
    {
        const string historical = "10000000147"; // Native TCKN checksum fails; the historical SQL shape is still 11 ASCII digits.
        Assert.False(TaxIdentityNumber.TryParse(historical, out _, out _));
        var party = new Party(Guid.NewGuid(), PartyCode.Parse("CAR-000001"), PartyKind.LegalEntity, "SENTETIK");
        await SeedAsync(CompanyA, db => { db.Parties.Add(party); db.CompanyPartyAccounts.Add(new CompanyPartyAccount(Guid.NewGuid(), CompanyA, party.Id, PartyRole.Customer)); });
        var row = new PartyTaxIdentityChangeRequest(Guid.NewGuid(), CompanyA, party.Id, historical,
            TaxIdentityNumber.Parse("9876543217"), party.Version, "SENTETIK legacy correction", "SENTETIK-maker", Start);
        await SeedAsync(CompanyA, db => db.PartyTaxIdentityChangeRequests.Add(row));
        var value = await Source().ReadDecisionAsync(Caller(), row.Id, default); Assert.NotNull(value);
        Assert.Equal(historical, value.CounterpartyTaxIdentityChange!.CurrentTaxIdentityNumber);
        Assert.DoesNotContain(historical, System.Text.Json.JsonSerializer.Serialize(value));
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
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            test.asked.Add(permission);
            if (test.beforeFresh is not null) await test.beforeFresh();
            return new PermissionScope(!test.withheld.Contains(permission), true, new HashSet<string>(StringComparer.Ordinal),
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        }
    }
}
