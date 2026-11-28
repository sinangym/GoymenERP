using System.Net;
using System.Net.Http.Json;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class CounterpartyPartyMergeSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Merge_preview_reads_all_holding_accounts_without_creating_a_merge_or_changing_accounts()
    {
        await using var test = new MergeTest(grantMerge: true);
        using var a = test.Client(); using var b = test.Client(TestCompanyIds.CompanyB);
        var survivor = await CreatePartyAsync(a, "SENTETIK etki ana");
        var merged = await CreatePartyAsync(a, "SENTETIK etki mükerrer");
        await LinkAsync(b, merged);
        var survivorVersion = await VersionAsync(a, survivor); var mergedVersion = await VersionAsync(a, merged);
        using var response = await a.PostAsJsonAsync(PathFor(survivor) + "/preview",
            new { mergedPartyId = merged, expectedVersion = survivorVersion, mergedPartyExpectedVersion = mergedVersion });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var body = await JsonAsync(response);
        Assert.Equal(new[] { "changes", "companyId", "holdingAccounts", "merged", "survivor" }, body.EnumerateObject().Select(x => x.Name).Order());
        Assert.Equal(TestCompanyIds.CompanyA, body.GetProperty("companyId").GetGuid());
        Assert.Equal(survivor, body.GetProperty("survivor").GetProperty("partyId").GetGuid());
        Assert.Equal(merged, body.GetProperty("changes").GetProperty("partyId").GetGuid());
        Assert.Equal(survivor, body.GetProperty("changes").GetProperty("mergedIntoPartyId").GetGuid());
        Assert.False(body.GetProperty("changes").GetProperty("isActive").GetBoolean());
        var accounts = body.GetProperty("holdingAccounts").EnumerateArray().ToArray();
        Assert.Equal(3, accounts.Length);
        Assert.Equal(new[] { TestCompanyIds.CompanyA, TestCompanyIds.CompanyB }.Order(), accounts.Select(x => x.GetProperty("companyId").GetGuid()).Distinct().Order());
        foreach (var account in accounts)
        {
            Assert.Equal(new[] { "accountId", "companyId", "isActive", "partyId", "version" }, account.EnumerateObject().Select(x => x.Name).Order());
            Assert.True(account.GetProperty("isActive").GetBoolean()); Assert.Equal("1", account.GetProperty("version").GetString());
        }
        Assert.Equal(survivorVersion, await VersionAsync(a, survivor)); Assert.Equal(mergedVersion, await VersionAsync(a, merged));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[PartyMerges];", survivor));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]='counterparty.party.merge.approve.v1';", survivor));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[OutboxMessages] WHERE [EventType]=@Event;", survivor));
        // This read does not bind the later PUT to account membership; no atomic preflight claim is made here.
    }
    [SqlServerFact]
    public async Task Merge_preview_denies_a_reader_without_the_other_holding_company_authority_and_never_returns_partial_facts()
    {
        await using var test = new MergeTest(grantMerge: true);
        using var admin = test.Client(); using var b = test.Client(TestCompanyIds.CompanyB);
        using var local = test.Client(TestCompanyIds.CompanyA, TestUsers.CompanyA);
        var survivor = await CreatePartyAsync(admin, "SENTETIK kapsam ana"); var merged = await CreatePartyAsync(admin, "SENTETIK kapsam diğer");
        await LinkAsync(b, merged);
        using var denied = await local.PostAsJsonAsync(PathFor(survivor) + "/preview", new {
            mergedPartyId = merged, expectedVersion = await VersionAsync(admin, survivor), mergedPartyExpectedVersion = await VersionAsync(admin, merged) });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var body = await JsonAsync(denied); Assert.False(body.TryGetProperty("holdingAccounts", out _)); Assert.False(body.TryGetProperty("survivor", out _));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[PartyMerges];", survivor));
    }
    [SqlServerFact]
    public async Task Merge_preview_stale_pair_or_query_override_cannot_release_impact()
    {
        await using var test = new MergeTest(grantMerge: true); using var client = test.Client();
        var survivor = await CreatePartyAsync(client, "SENTETIK eski ana"); var merged = await CreatePartyAsync(client, "SENTETIK eski diğer");
        var request = new { mergedPartyId = merged, expectedVersion = "999", mergedPartyExpectedVersion = await VersionAsync(client, merged) };
        using var stale = await client.PostAsJsonAsync(PathFor(survivor) + "/preview", request);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode); Assert.False((await JsonAsync(stale)).TryGetProperty("holdingAccounts", out _));
        using var query = await client.PostAsJsonAsync(PathFor(survivor) + "/preview?companyId=" + TestCompanyIds.CompanyB, request);
        Assert.Equal(HttpStatusCode.BadRequest, query.StatusCode);
    }
}
