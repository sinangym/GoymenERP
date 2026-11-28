using System.Net;
using System.Net.Http.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class CounterpartyPartyMergeSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Seen_merge_HTTP_preserves_native_body_original_receipt_and_company_accounts_with_impact_change_refusal()
    {
        await using var test = new MergeTest(true);
        using var a = test.Client(); using var b = test.Client(TestCompanyIds.CompanyB);
        var survivor = await CreatePartyAsync(a, "SENTETIK HTTP survivor");
        var merged = await CreatePartyAsync(a, "SENTETIK HTTP duplicate");
        var selection = new { mergedPartyId = merged, expectedVersion = await VersionAsync(a, survivor), mergedPartyExpectedVersion = await VersionAsync(a, merged) };
        using var first = await a.PostAsJsonAsync(PathFor(survivor) + "/purpose-preview", selection);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Contains("no-store", first.Headers.CacheControl!.ToString());
        var human = await JsonAsync(first);
        Assert.Equal(new[] { "impact", "seenImpactDigest" }, human.EnumerateObject().Select(x => x.Name).Order());
        Assert.Equal(5, human.GetProperty("impact").EnumerateObject().Count());
        var oldToken = human.GetProperty("seenImpactDigest").GetString()!;
        await LinkAsync(b, merged); // Actual account writer; neither PARTY version changes.
        var body = Body(new { selection.mergedPartyId, selection.expectedVersion, selection.mergedPartyExpectedVersion, reason = "SENTETIK HTTP merge" });
        NewKey(a); a.DefaultRequestHeaders.Add("X-Counterparty-Merge-Seen-Impact", oldToken);
        using var changed = await PutAsync(a, PathFor(survivor), body);
        await AssertProblemAsync(changed, HttpStatusCode.Conflict, CounterpartyProblemCodesV1.PartyMergeImpactChanged);
        Assert.False(changed.Headers.Contains("Idempotency-Replayed"));
        Assert.False(changed.Headers.Contains("Idempotency-Original-Correlation-Id"));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[PartyMerges];", merged));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]='counterparty.party.merge.approve.v1';", merged));
        using var refreshed = await a.PostAsJsonAsync(PathFor(survivor) + "/purpose-preview", selection);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var currentToken = (await JsonAsync(refreshed)).GetProperty("seenImpactDigest").GetString()!;
        Assert.NotEqual(oldToken, currentToken);
        a.DefaultRequestHeaders.Remove("X-Counterparty-Merge-Seen-Impact");
        a.DefaultRequestHeaders.Add("X-Counterparty-Merge-Seen-Impact", currentToken);
        using var applied = await PutAsync(a, PathFor(survivor), body);
        Assert.Equal(HttpStatusCode.NoContent, applied.StatusCode);
        using var replay = await PutAsync(a, PathFor(survivor), body);
        Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode); Assert.True(replay.Headers.Contains("Idempotency-Replayed"));
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[PartyMerges];", merged));
        Assert.Equal(3, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts] WHERE [PartyId]=@Party OR [PartyId]=@Other;", merged, survivor));
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [Operation]='ApproveMerge';", merged));
        Assert.Equal(2, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[OutboxMessages] WHERE [EventType]=@Event;", merged));
        // Same original native key/body can still be read through the unchanged legacy protocol.
        a.DefaultRequestHeaders.Remove("X-Counterparty-Merge-Seen-Impact");
        using var nativeReplay = await PutAsync(a, PathFor(survivor), body);
        Assert.Equal(HttpStatusCode.NoContent, nativeReplay.StatusCode); Assert.True(nativeReplay.Headers.Contains("Idempotency-Replayed"));
        await using var scope = test.Factory.Services.CreateAsyncScope();
        Assert.Same(scope.ServiceProvider.GetRequiredService<ICounterpartyPartyMergeService>(), scope.ServiceProvider.GetRequiredService<ICounterpartyPartyMergePurposeV1>());
    }

    [SqlServerFact]
    public async Task Seen_merge_HTTP_denies_unproven_legacy_success_and_rejects_malformed_or_duplicate_header_without_writes()
    {
        await using var test = new MergeTest(true); using var a = test.Client();
        var survivor = await CreatePartyAsync(a, "SENTETIK HTTP legacy survivor"); var merged = await CreatePartyAsync(a, "SENTETIK HTTP legacy duplicate");
        var selection = new { mergedPartyId = merged, expectedVersion = await VersionAsync(a, survivor), mergedPartyExpectedVersion = await VersionAsync(a, merged) };
        using var preflight = await a.PostAsJsonAsync(PathFor(survivor) + "/purpose-preview", selection);
        Assert.Equal(HttpStatusCode.OK, preflight.StatusCode);
        var token = (await JsonAsync(preflight)).GetProperty("seenImpactDigest").GetString()!;
        var body = Body(new { selection.mergedPartyId, selection.expectedVersion, selection.mergedPartyExpectedVersion, reason = "SENTETIK HTTP legacy" }); NewKey(a);
        a.DefaultRequestHeaders.Add("X-Counterparty-Merge-Seen-Impact", new string('a', 64));
        using var bad = await PutAsync(a, PathFor(survivor), body); Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        a.DefaultRequestHeaders.Remove("X-Counterparty-Merge-Seen-Impact");
        a.DefaultRequestHeaders.Add("X-Counterparty-Merge-Seen-Impact", new[] { token, token });
        using var duplicate = await PutAsync(a, PathFor(survivor), body); Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[PartyMerges];", merged));
        a.DefaultRequestHeaders.Remove("X-Counterparty-Merge-Seen-Impact");
        using var legacy = await PutAsync(a, PathFor(survivor), body); Assert.Equal(HttpStatusCode.NoContent, legacy.StatusCode);
        a.DefaultRequestHeaders.Add("X-Counterparty-Merge-Seen-Impact", token);
        using var unproven = await PutAsync(a, PathFor(survivor), body); Assert.Equal(HttpStatusCode.ServiceUnavailable, unproven.StatusCode);
        Assert.False(unproven.Headers.Contains("Idempotency-Replayed")); Assert.False(unproven.Headers.Contains("Idempotency-Original-Correlation-Id"));
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[PartyMerges];", merged));
        a.DefaultRequestHeaders.Remove("X-Counterparty-Merge-Seen-Impact");
        using var original = await PutAsync(a, PathFor(survivor), body); Assert.Equal(HttpStatusCode.NoContent, original.StatusCode);
        Assert.True(original.Headers.Contains("Idempotency-Replayed"));
    }
}
