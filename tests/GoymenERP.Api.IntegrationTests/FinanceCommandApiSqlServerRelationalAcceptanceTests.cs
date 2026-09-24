using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// FIN-UI-1 against a REAL SQL Server through the REAL host: the Finance commands' HTTP mapping and
/// ADR-011 §4/§7/§8 as a client sees them. The permission port, the persisted bank-account master and
/// reader, Settings (the approved GDC/TRY money profile and its currency candidates for company A),
/// audit and the command record are real. Callers are the factory's configured development users with
/// synthetic roles on the registered names, created in this test's own database and never seeded.
/// Where a case says so, the Counterparty party port is a SENTETIK stand-in (an active party account,
/// a gate that holds two callers together, an outage). This is API evidence, not the FIN-01 user outcome.
/// </summary>
public sealed class FinanceCommandApiSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Lira = SettingsCurrencyIdentitiesV1.TurkishLira;
    private static readonly Guid Party = Guid.Parse("f1000000-0000-4000-8000-000000000001");
    private static readonly Guid PartyAccount = Guid.Parse("f1000000-0000-4000-8000-000000000002");
    private static readonly Guid GateParty = Guid.Parse("f1000000-0000-4000-8000-000000000003");
    private static readonly Guid UnavailableParty = Guid.Parse("f1000000-0000-4000-8000-000000000004");
    private const string Proposer = TestUsers.CompanyA;
    private const string Approver = TestUsers.CompanyAViewer;
    private static readonly string[] ProposerGrants =
    [
        "finance.bank-account.read", "finance.bank-account.propose", "finance.bank-account.approve",
    ];
    private static readonly string[] ApproverGrants = ["finance.bank-account.read", "finance.bank-account.approve"];
    private static readonly string[] SaverGrants =
    [
        FinancePermissionNames.DraftSave, FinancePermissionNames.DraftRead, FinancePermissionNames.AmountRead,
        "finance.bank-account.read",
    ];
    private static readonly string[] ManualPaymentPreparerGrants =
    [
        FinancePermissionNames.ManualBankPaymentDraftSave,
        FinancePermissionNames.ManualBankPaymentDraftRead,
        FinancePermissionNames.AmountRead,
        "finance.bank-account.read",
    ];
    private static readonly string[] ManualPaymentDeciderGrants =
    [
        FinancePermissionNames.ManualBankPaymentDraftRead,
        FinancePermissionNames.ManualBankPaymentApprove,
        FinancePermissionNames.ManualBankPaymentRecord,
        FinancePermissionNames.AmountRead,
        "finance.bank-account.read",
    ];

    /// <summary>
    /// Ö4 over HTTP: the proposer's candidates are Settings' real ones; a proposal replays; the
    /// proposer's own approval is 403 even with the approve grant; another user's stale approval and an
    /// approval of an unknown account are the key's terminal answers (§7, the latter with no target);
    /// the approval replays; a second approval is refused; company B sees nothing of it.
    /// </summary>
    [SqlServerFact]
    public async Task A_proposal_is_approved_once_by_another_user_and_every_answer_of_a_key_holds()
    {
        await using var run = await Run.StartAsync();
        await run.GrantAsync(Proposer, CompanyA, ProposerGrants);
        await run.GrantAsync(Approver, CompanyA, ApproverGrants);
        using var proposer = run.Client(Proposer, CompanyA);
        using var approver = run.Client(Approver, CompanyA);

        // The currencies are Settings' real candidates, under the propose grant only.
        using (var candidates = await proposer.GetAsync(FinanceEndpoints.BankAccountCurrencyCandidatesPath))
        {
            Assert.Equal(HttpStatusCode.OK, candidates.StatusCode);
            Assert.True(candidates.Headers.CacheControl is { Private: true, NoStore: true });
            Assert.Contains((await Json(candidates)).GetProperty("items").EnumerateArray(),
                item => item.GetProperty("currencyId").GetGuid() == Lira && item.GetProperty("code").GetString() == "TRY");
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await approver.GetAsync(FinanceEndpoints.BankAccountCurrencyCandidatesPath)).StatusCode);

        // Propose, then the same key again: the stored answer with the first correlation.
        var proposeCorrelation = Guid.NewGuid().ToString("N");
        Guid accountId;
        using (var proposed = await proposer.SendAsync(Post(FinanceEndpoints.BankAccountsPath, "SENTETIK-p-1", proposeCorrelation,
                   new BankAccountProposeRequest("SENTETIK ana hesap", Lira, "SENTETIK Banka", "SENTETIK tür", false))))
        {
            Assert.Equal(HttpStatusCode.OK, proposed.StatusCode);
            var result = await Json(proposed);
            Assert.Equal("Succeeded", result.GetProperty("outcome").GetString());
            accountId = result.GetProperty("resource").GetProperty("id").GetGuid();
            Assert.False(result.GetProperty("resource").GetProperty("isActive").GetBoolean());
            Assert.False(proposed.Headers.Contains("Idempotency-Replayed"));
        }

        using (var replayed = await proposer.SendAsync(Post(FinanceEndpoints.BankAccountsPath, "SENTETIK-p-1", Guid.NewGuid().ToString("N"),
                   new BankAccountProposeRequest("SENTETIK ana hesap", Lira, "SENTETIK Banka", "SENTETIK tür", false))))
        {
            Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
            AssertReplayOf(replayed, proposeCorrelation);
            Assert.Equal(accountId, (await Json(replayed)).GetProperty("resource").GetProperty("id").GetGuid());
        }

        // A currency outside Settings' candidates is refused and stores nothing.
        using (var foreign = await proposer.SendAsync(Post(FinanceEndpoints.BankAccountsPath, "SENTETIK-p-2", Guid.NewGuid().ToString("N"),
                   new BankAccountProposeRequest("SENTETIK yabancı", Guid.NewGuid(), "SENTETIK Banka", "SENTETIK tür", false))))
        {
            Assert.Equal((HttpStatusCode)422, foreign.StatusCode);
            Assert.Equal("currency_not_a_candidate", Issue(await Json(foreign)));
        }

        // A pending proposal is the business of those who may propose or approve: both see it, in both lists.
        Assert.Equal(accountId, Single(await Json(await approver.GetAsync(FinanceEndpoints.BankAccountsPath))).GetProperty("id").GetGuid());

        // F-04's list tells each caller whether the proposal is theirs.
        Assert.True(Single(await Json(await proposer.GetAsync(FinanceEndpoints.BankAccountMasterPath))).GetProperty("isOwnProposal").GetBoolean());
        Assert.False(Single(await Json(await approver.GetAsync(FinanceEndpoints.BankAccountMasterPath))).GetProperty("isOwnProposal").GetBoolean());

        // Ö4: the proposer holds the approve grant, and is still refused.
        using (var own = await proposer.SendAsync(Approval(accountId, "SENTETIK-a-own", "1")))
        {
            Assert.Equal(HttpStatusCode.Forbidden, own.StatusCode);
            Assert.Equal("forbidden", (await Json(own)).GetProperty("code").GetString());
        }

        // §7: a stale version is the key's answer for good, even after the account moves on.
        var staleCorrelation = Guid.NewGuid().ToString("N");
        using (var stale = await approver.SendAsync(Approval(accountId, "SENTETIK-a-stale", "7", staleCorrelation)))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal("version_conflict", Issue(await Json(stale)));
        }

        // §7: an account that is not the company's has no target to record; the answer is still kept.
        var unknownCorrelation = Guid.NewGuid().ToString("N");
        var unknownAccount = Guid.NewGuid();
        using (var unknown = await approver.SendAsync(Approval(unknownAccount, "SENTETIK-a-unknown", "1", unknownCorrelation)))
        {
            Assert.Equal((HttpStatusCode)422, unknown.StatusCode);
            Assert.Equal("bank_account_not_found", Issue(await Json(unknown)));
        }

        var approveCorrelation = Guid.NewGuid().ToString("N");
        using (var approved = await approver.SendAsync(Approval(accountId, "SENTETIK-a-1", "1", approveCorrelation)))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
            var resource = (await Json(approved)).GetProperty("resource");
            Assert.True(resource.GetProperty("isActive").GetBoolean());
            Assert.Equal("2", resource.GetProperty("version").GetString());
        }

        // A lost answer retried with the same key is the stored 200, never a 409.
        using (var again = await approver.SendAsync(Approval(accountId, "SENTETIK-a-1", "1")))
        {
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            AssertReplayOf(again, approveCorrelation);
        }

        using (var staleAgain = await approver.SendAsync(Approval(accountId, "SENTETIK-a-stale", "7")))
        {
            Assert.Equal(HttpStatusCode.Conflict, staleAgain.StatusCode);
            AssertReplayOf(staleAgain, staleCorrelation);
            Assert.Equal("version_conflict", Issue(await Json(staleAgain)));
        }

        using (var unknownAgain = await approver.SendAsync(Approval(unknownAccount, "SENTETIK-a-unknown", "1")))
        {
            Assert.Equal((HttpStatusCode)422, unknownAgain.StatusCode);
            AssertReplayOf(unknownAgain, unknownCorrelation);
            Assert.Equal("bank_account_not_found", Issue(await Json(unknownAgain)));
        }

        using (var otherAccount = await approver.SendAsync(Approval(Guid.NewGuid(), "SENTETIK-a-unknown", "1")))
        {
            // Another account id under the same key is another request: a conflict, nothing disclosed.
            Assert.Equal(HttpStatusCode.Conflict, otherAccount.StatusCode);
            var result = await Json(otherAccount);
            Assert.Equal("IdempotencyConflict", result.GetProperty("outcome").GetString());
            Assert.Empty(result.GetProperty("issues").EnumerateArray());
        }

        // A second approval of the approved account, under a new key, is refused.
        using (var second = await approver.SendAsync(Approval(accountId, "SENTETIK-a-2", "2")))
        {
            Assert.Equal((HttpStatusCode)422, second.StatusCode);
            Assert.Equal("bank_account_not_proposed", Issue(await Json(second)));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.BankAccounts"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Commands WHERE DraftId IS NULL AND BankAccountId IS NULL AND CorrelationId = @c",
            ("@c", unknownCorrelation)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Commands WHERE BankAccountId = @id AND CorrelationId = @c",
            ("@id", accountId), ("@c", staleCorrelation)));
        // Each write is audited once, with the permission that granted it (ADR-009).
        Assert.Equal("finance.bank-account.propose", await run.ScalarAsync<string>(
            "SELECT PermissionCode FROM audit.AuditEvents WHERE EventType = N'finance.bank-account.proposed.v1'"));
        Assert.Equal("finance.bank-account.approve", await run.ScalarAsync<string>(
            "SELECT PermissionCode FROM audit.AuditEvents WHERE EventType = N'finance.bank-account.approved.v1'"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.BankAccount'"));

        // Company B: its own grants show nothing of A's account and cannot reach it.
        await run.GrantAsync(TestUsers.CompanyB, CompanyB, ProposerGrants);
        using var clientOfB = run.Client(TestUsers.CompanyB, CompanyB);
        Assert.Empty((await Json(await clientOfB.GetAsync(FinanceEndpoints.BankAccountMasterPath))).GetProperty("items").EnumerateArray());
        using (var foreignApproval = await clientOfB.SendAsync(Approval(accountId, "SENTETIK-b-1", "2")))
        {
            Assert.Equal((HttpStatusCode)422, foreignApproval.StatusCode);
            Assert.Equal("bank_account_not_found", Issue(await Json(foreignApproval)));
        }

        // Without a grant every route refuses.
        using var viewerOfB = run.Client(TestUsers.CompanyAViewer, CompanyB);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewerOfB.GetAsync(FinanceEndpoints.BankAccountMasterPath)).StatusCode);
    }

    /// <summary>
    /// The draft save's mapping with its real owners (the approved account, Settings' GDC/TRY profile)
    /// and a SENTETIK party port: §8 — two callers of one key meet at the party port and race the save:
    /// one draft, one command, one audit event, both 200 and the loser with the replay headers; §4 — the
    /// key replays later; 409 for another payload under the key; §7 — a stale version (409) and an
    /// unknown draft (422) are recorded and replayed; a party outage is the platform's 503 and records
    /// nothing; a caller without grants is 403.
    /// </summary>
    [SqlServerFact]
    public async Task A_same_key_race_on_a_save_stores_one_draft_and_every_answer_of_a_key_holds()
    {
        await using var run = await Run.StartAsync(services =>
        {
            services.RemoveAll<IFinancePartyAccountPort>();
            services.AddSingleton<SyntheticGate>();
            services.AddScoped<IFinancePartyAccountPort, SyntheticParties>();
        });
        var account = await run.ApprovedBankAccountAsync(CompanyA);
        var pending = await run.PendingProposalAsync(CompanyA);
        await run.GrantAsync(Proposer, CompanyA, SaverGrants);
        using var client = run.Client(Proposer, CompanyA);

        // Least privilege: a caller who may read but neither propose nor approve sees approved accounts
        // only — in the picker, in F-04's list and by id (the draft save treats a hidden one as ineligible).
        Assert.Equal(account, Single(await Json(await client.GetAsync(FinanceEndpoints.BankAccountsPath))).GetProperty("id").GetGuid());
        Assert.Equal(account, Single(await Json(await client.GetAsync(FinanceEndpoints.BankAccountMasterPath)))
            .GetProperty("account").GetProperty("id").GetGuid());
        using (var hidden = await client.SendAsync(Post(FinanceEndpoints.DraftsPath, "SENTETIK-pending", Guid.NewGuid().ToString("N"),
                   Save(pending, Party, draftId: null, expectedVersion: null))))
        {
            Assert.Equal((HttpStatusCode)422, hidden.StatusCode);
            Assert.Equal("bank_account_ineligible", Issue(await Json(hidden)));
        }

        // §8: both callers pass the key lookup, meet at the party port, then race the one insert.
        var raced = Save(account, GateParty, draftId: null, expectedVersion: null);
        var correlations = new[] { Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N") };
        var responses = await Task.WhenAll(
            client.SendAsync(Post(FinanceEndpoints.DraftsPath, "SENTETIK-race", correlations[0], raced)),
            client.SendAsync(Post(FinanceEndpoints.DraftsPath, "SENTETIK-race", correlations[1], raced)));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var winner = Assert.Single(responses, response => !response.Headers.Contains("Idempotency-Replayed"));
        var loser = Assert.Single(responses, response => response.Headers.Contains("Idempotency-Replayed"));
        // Each body is read once: the content stream is not rewound for a second read.
        var winnerBody = await Json(winner);
        var loserBody = await Json(loser);
        var winnerCorrelation = winnerBody.GetProperty("originalCorrelationId").GetString()!;
        Assert.Contains(winnerCorrelation, correlations);
        AssertReplayOf(loser, winnerCorrelation);
        Assert.False(winnerBody.GetProperty("isReplay").GetBoolean());
        Assert.True(loserBody.GetProperty("isReplay").GetBoolean());
        var draftId = winnerBody.GetProperty("resource").GetProperty("source").GetProperty("id").GetGuid();
        Assert.Equal(draftId, loserBody.GetProperty("resource").GetProperty("source").GetProperty("id").GetGuid());
        foreach (var response in responses)
        {
            response.Dispose();
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReceiptDrafts"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Commands WHERE DraftId = @id AND CorrelationId = @c", ("@id", draftId), ("@c", winnerCorrelation)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.ReceiptDraft'"));

        // §4: the key later answers the same draft with the first correlation.
        using (var again = await client.SendAsync(Post(FinanceEndpoints.DraftsPath, "SENTETIK-race", Guid.NewGuid().ToString("N"), raced)))
        {
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            AssertReplayOf(again, winnerCorrelation);
        }

        // §3: another payload under the key is a conflict that discloses nothing.
        using (var other = await client.SendAsync(Post(FinanceEndpoints.DraftsPath, "SENTETIK-race", Guid.NewGuid().ToString("N"),
                   Save(account, Party, draftId: null, expectedVersion: null))))
        {
            Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
            var result = await Json(other);
            Assert.Equal("IdempotencyConflict", result.GetProperty("outcome").GetString());
            Assert.Equal(JsonValueKind.Null, result.GetProperty("resource").ValueKind);
        }

        // §7: a stale version is recorded against the draft and replayed after the draft moves on.
        var staleCorrelation = Guid.NewGuid().ToString("N");
        var stale = Save(account, Party, draftId, expectedVersion: "7");
        using (var first = await client.SendAsync(Post(FinanceEndpoints.DraftsPath, "SENTETIK-stale", staleCorrelation, stale)))
        {
            Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
            Assert.Equal("version_conflict", Issue(await Json(first)));
        }

        using (var edit = await client.SendAsync(Post(FinanceEndpoints.DraftsPath, "SENTETIK-edit", Guid.NewGuid().ToString("N"),
                   Save(account, Party, draftId, expectedVersion: "1"))))
        {
            Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
            Assert.Equal("2", (await Json(edit)).GetProperty("resource").GetProperty("source").GetProperty("version").GetString());
        }

        using (var replayed = await client.SendAsync(Post(FinanceEndpoints.DraftsPath, "SENTETIK-stale", Guid.NewGuid().ToString("N"), stale)))
        {
            Assert.Equal(HttpStatusCode.Conflict, replayed.StatusCode);
            AssertReplayOf(replayed, staleCorrelation);
            Assert.Equal("version_conflict", Issue(await Json(replayed)));
        }

        // §7: a draft that is not the company's is recorded with no target and replayed.
        var unknownCorrelation = Guid.NewGuid().ToString("N");
        var unknown = Save(account, Party, Guid.NewGuid(), expectedVersion: "1");
        using (var first = await client.SendAsync(Post(FinanceEndpoints.DraftsPath, "SENTETIK-unknown", unknownCorrelation, unknown)))
        {
            Assert.Equal((HttpStatusCode)422, first.StatusCode);
            Assert.Equal("draft_not_found", Issue(await Json(first)));
        }

        using (var replayed = await client.SendAsync(Post(FinanceEndpoints.DraftsPath, "SENTETIK-unknown", Guid.NewGuid().ToString("N"), unknown)))
        {
            Assert.Equal((HttpStatusCode)422, replayed.StatusCode);
            AssertReplayOf(replayed, unknownCorrelation);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Commands WHERE DraftId IS NULL AND BankAccountId IS NULL AND CorrelationId = @c",
            ("@c", unknownCorrelation)));

        // A refusal on the merits is not terminal: nothing is recorded, a retry asks the owners again.
        using (var ineligible = await client.SendAsync(Post(FinanceEndpoints.DraftsPath, "SENTETIK-bank", Guid.NewGuid().ToString("N"),
                   Save(Guid.NewGuid(), Party, draftId: null, expectedVersion: null))))
        {
            Assert.Equal((HttpStatusCode)422, ineligible.StatusCode);
            Assert.Equal("bank_account_ineligible", Issue(await Json(ineligible)));
        }

        // An owner outage is the platform's 503, nothing is recorded.
        using (var outage = await client.SendAsync(Post(FinanceEndpoints.DraftsPath, "SENTETIK-outage", Guid.NewGuid().ToString("N"),
                   Save(account, UnavailableParty, draftId: null, expectedVersion: null))))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, outage.StatusCode);
            Assert.Equal("service_unavailable", (await Json(outage)).GetProperty("code").GetString());
        }

        // 4 commands: the raced create, the edit, the stale and the unknown refusals.
        Assert.Equal(4, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReceiptDrafts"));

        // Without a grant the save is the platform's 403 and reads nothing.
        using var viewer = run.Client(Approver, CompanyA);
        using (var denied = await viewer.SendAsync(Post(FinanceEndpoints.DraftsPath, "SENTETIK-denied", Guid.NewGuid().ToString("N"), raced)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal("forbidden", (await Json(denied)).GetProperty("code").GetString());
        }
    }

    /// <summary>
    /// FIN-T06 through the real HTTP host and real Identity/Finance SQL. The owner answers for party and
    /// period are explicit synthetic test inputs; grants are disposable rows in this test database. A maker
    /// cannot approve its own proposal even when it also holds the checker permission. A different checker
    /// sees the pending item, creates one separate reversal, and the approval key replays exactly.
    /// </summary>
    [SqlServerFact]
    public async Task A_reversal_proposal_requires_another_HTTP_user_and_preserves_both_history_links()
    {
        await using var run = await Run.StartAsync(services =>
        {
            services.RemoveAll<IFinancePartyAccountPort>();
            services.AddSingleton<SyntheticGate>();
            services.AddScoped<IFinancePartyAccountPort, SyntheticParties>();
            services.RemoveAll<IFinanceRecordPeriodRule>();
            services.AddScoped<IFinanceRecordPeriodRule, SyntheticOpenPeriodRule>();
        });
        var account = await run.ApprovedBankAccountAsync(CompanyA);
        await run.GrantAsync(Proposer, CompanyA,
            FinancePermissionNames.DraftSave,
            FinancePermissionNames.DraftRead,
            FinancePermissionNames.AmountRead,
            FinancePermissionNames.Record,
            FinancePermissionNames.ReversePropose,
            FinancePermissionNames.ReverseApprove,
            "finance.bank-account.read");
        await run.GrantAsync(Approver, CompanyA,
            FinancePermissionNames.DraftRead,
            FinancePermissionNames.AmountRead,
            FinancePermissionNames.ReverseApprove);
        await run.GrantAsync(Approver, CompanyB,
            FinancePermissionNames.DraftRead,
            FinancePermissionNames.AmountRead,
            FinancePermissionNames.ReverseApprove);
        using var maker = run.Client(Proposer, CompanyA);
        using var checker = run.Client(Approver, CompanyA);
        using var foreignCompany = run.Client(Approver, CompanyB);

        Guid sourceId;
        string sourceVersion;
        using (var saved = await maker.SendAsync(Post(FinanceEndpoints.DraftsPath, "SENTETIK-reversal-save",
                   Guid.NewGuid().ToString("N"), Save(account, Party, draftId: null, expectedVersion: null))))
        {
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var source = (await Json(saved)).GetProperty("resource").GetProperty("source");
            sourceId = source.GetProperty("id").GetGuid();
            sourceVersion = source.GetProperty("version").GetString()!;
        }

        using (var recorded = await maker.SendAsync(Post(
                   $"{FinanceEndpoints.ManualReceiptsPath}/{sourceId:D}/record", "SENTETIK-reversal-record",
                   Guid.NewGuid().ToString("N"), new RecordRequest(sourceId, sourceVersion))))
        {
            Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);
            sourceVersion = (await Json(recorded)).GetProperty("resource").GetProperty("source")
                .GetProperty("version").GetString()!;
        }

        var reversalOccurredOn = new DateOnly(2026, 9, 19);
        Guid proposalId;
        string proposalVersion;
        using (var proposed = await maker.SendAsync(Post(
                   $"{FinanceEndpoints.ManualReceiptsPath}/{sourceId:D}/reversal-proposals",
                   "SENTETIK-reversal-propose", Guid.NewGuid().ToString("N"),
                   new ProposeReversalRequest(sourceId, sourceVersion, reversalOccurredOn, "SENTETIK-yanlis-tahsilat"))))
        {
            Assert.Equal(HttpStatusCode.OK, proposed.StatusCode);
            var resource = (await Json(proposed)).GetProperty("resource");
            proposalId = resource.GetProperty("id").GetGuid();
            proposalVersion = resource.GetProperty("version").GetString()!;
            Assert.Equal("Pending", resource.GetProperty("status").GetString());
        }

        using (var ownApproval = await maker.SendAsync(Post(
                   $"{FinanceEndpoints.ReversalProposalsPath}/{proposalId:D}/approval",
                   "SENTETIK-reversal-self-approval", Guid.NewGuid().ToString("N"),
                   new ApproveReversalRequest(proposalId, proposalVersion))))
        {
            Assert.Equal(HttpStatusCode.Forbidden, ownApproval.StatusCode);
            var problem = await Json(ownApproval);
            Assert.Equal("forbidden", problem.GetProperty("code").GetString());
            Assert.False(problem.TryGetProperty("issues", out _));
        }

        using (var foreignPending = await foreignCompany.GetAsync(FinanceEndpoints.ReversalProposalsPath + "/pending"))
        {
            Assert.Equal(HttpStatusCode.OK, foreignPending.StatusCode);
            Assert.Empty((await Json(foreignPending)).GetProperty("items").EnumerateArray());
        }

        using (var foreignDetail = await foreignCompany.GetAsync(
                   $"{FinanceEndpoints.ReversalProposalsPath}/{proposalId:D}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreignDetail.StatusCode);
        }

        using (var foreignApproval = await foreignCompany.SendAsync(Post(
                   $"{FinanceEndpoints.ReversalProposalsPath}/{proposalId:D}/approval",
                   "SENTETIK-reversal-foreign-approval", Guid.NewGuid().ToString("N"),
                   new ApproveReversalRequest(proposalId, proposalVersion))))
        {
            Assert.Equal((HttpStatusCode)422, foreignApproval.StatusCode);
            Assert.Equal("finance_reversal_proposal_not_found", Issue(await Json(foreignApproval)));
        }

        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.ReversalProposals WHERE Id = @p AND CompanyId = @c AND Status <> 0",
            ("@p", proposalId), ("@c", CompanyA)));

        using (var pending = await checker.GetAsync(FinanceEndpoints.ReversalProposalsPath + "/pending"))
        {
            Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
            Assert.Equal(proposalId, Single(await Json(pending)).GetProperty("id").GetGuid());
        }

        using (var detail = await checker.GetAsync($"{FinanceEndpoints.ReversalProposalsPath}/{proposalId:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            Assert.Equal(sourceId, (await Json(detail)).GetProperty("originalSourceId").GetGuid());
        }

        var approvalCorrelation = Guid.NewGuid().ToString("N");
        Guid reversalSourceId;
        var approvalBody = new ApproveReversalRequest(proposalId, proposalVersion);
        using (var approved = await checker.SendAsync(Post(
                   $"{FinanceEndpoints.ReversalProposalsPath}/{proposalId:D}/approval",
                   "SENTETIK-reversal-approval", approvalCorrelation, approvalBody)))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
            var resource = (await Json(approved)).GetProperty("resource");
            Assert.Equal("Approved", resource.GetProperty("status").GetString());
            reversalSourceId = resource.GetProperty("reversalSourceId").GetGuid();
            Assert.NotEqual(sourceId, reversalSourceId);
        }

        using (var replayed = await checker.SendAsync(Post(
                   $"{FinanceEndpoints.ReversalProposalsPath}/{proposalId:D}/approval",
                   "SENTETIK-reversal-approval", Guid.NewGuid().ToString("N"), approvalBody)))
        {
            Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
            AssertReplayOf(replayed, approvalCorrelation);
            Assert.Equal(reversalSourceId, (await Json(replayed)).GetProperty("resource")
                .GetProperty("reversalSourceId").GetGuid());
        }

        using (var original = await maker.GetAsync($"{FinanceEndpoints.ManualReceiptsPath}/{sourceId:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, original.StatusCode);
            var body = await Json(original);
            Assert.Equal(reversalSourceId, body.GetProperty("reversalSourceId").GetGuid());
            Assert.Equal(JsonValueKind.Null, body.GetProperty("reversesSourceId").ValueKind);
        }

        using (var reversal = await maker.GetAsync($"{FinanceEndpoints.ManualReceiptsPath}/{reversalSourceId:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, reversal.StatusCode);
            var body = await Json(reversal);
            Assert.Equal(JsonValueKind.Null, body.GetProperty("reversalSourceId").ValueKind);
            Assert.Equal(sourceId, body.GetProperty("reversesSourceId").GetGuid());
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReceiptDrafts"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.ReversalProposals WHERE Id = @p AND OriginalSourceId = @o "
            + "AND ReversalSourceId = @r AND Status = 1 AND ProposedBy <> DecidedBy",
            ("@p", proposalId), ("@o", sourceId), ("@r", reversalSourceId)));

        // B2 rejection is a distinct terminal decision. An empty reason changes nothing; a valid reason
        // closes the proposal without creating a reversing source or money event.
        Guid rejectedSourceId;
        string rejectedSourceVersion;
        using (var saved = await maker.SendAsync(Post(FinanceEndpoints.DraftsPath, "SENTETIK-rejection-save",
                   Guid.NewGuid().ToString("N"), Save(account, Party, draftId: null, expectedVersion: null))))
        {
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var source = (await Json(saved)).GetProperty("resource").GetProperty("source");
            rejectedSourceId = source.GetProperty("id").GetGuid();
            rejectedSourceVersion = source.GetProperty("version").GetString()!;
        }

        using (var recorded = await maker.SendAsync(Post(
                   $"{FinanceEndpoints.ManualReceiptsPath}/{rejectedSourceId:D}/record", "SENTETIK-rejection-record",
                   Guid.NewGuid().ToString("N"), new RecordRequest(rejectedSourceId, rejectedSourceVersion))))
        {
            Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);
            rejectedSourceVersion = (await Json(recorded)).GetProperty("resource").GetProperty("source")
                .GetProperty("version").GetString()!;
        }

        Guid rejectedProposalId;
        string rejectedProposalVersion;
        using (var proposed = await maker.SendAsync(Post(
                   $"{FinanceEndpoints.ManualReceiptsPath}/{rejectedSourceId:D}/reversal-proposals",
                   "SENTETIK-rejection-propose", Guid.NewGuid().ToString("N"),
                   new ProposeReversalRequest(rejectedSourceId, rejectedSourceVersion, reversalOccurredOn,
                       "SENTETIK-ikinci-yanlis-tahsilat"))))
        {
            Assert.Equal(HttpStatusCode.OK, proposed.StatusCode);
            var resource = (await Json(proposed)).GetProperty("resource");
            rejectedProposalId = resource.GetProperty("id").GetGuid();
            rejectedProposalVersion = resource.GetProperty("version").GetString()!;
        }

        using (var reasonRequired = await checker.SendAsync(Post(
                   $"{FinanceEndpoints.ReversalProposalsPath}/{rejectedProposalId:D}/rejection",
                   "SENTETIK-rejection-empty", Guid.NewGuid().ToString("N"),
                   new RejectReversalRequest(rejectedProposalId, rejectedProposalVersion, " "))))
        {
            Assert.Equal((HttpStatusCode)422, reasonRequired.StatusCode);
            Assert.Equal("invalid_request", Issue(await Json(reasonRequired)));
        }

        using (var rejected = await checker.SendAsync(Post(
                   $"{FinanceEndpoints.ReversalProposalsPath}/{rejectedProposalId:D}/rejection",
                   "SENTETIK-rejection-valid", Guid.NewGuid().ToString("N"),
                   new RejectReversalRequest(rejectedProposalId, rejectedProposalVersion,
                       "SENTETIK-belge-duzeltilecek"))))
        {
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
            var resource = (await Json(rejected)).GetProperty("resource");
            Assert.Equal("Rejected", resource.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, resource.GetProperty("reversalSourceId").ValueKind);
            Assert.Equal("SENTETIK-belge-duzeltilecek", resource.GetProperty("decisionReason").GetString());
        }

        using (var pending = await checker.GetAsync(FinanceEndpoints.ReversalProposalsPath + "/pending"))
        {
            Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
            Assert.Empty((await Json(pending)).GetProperty("items").EnumerateArray());
        }

        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReceiptDrafts"));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.ReversalProposals WHERE Id = @p AND OriginalSourceId = @o "
            + "AND Status = 2 AND ReversalSourceId IS NULL AND ProposedBy <> DecidedBy",
            ("@p", rejectedProposalId), ("@o", rejectedSourceId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.receipt-reversal.rejected.v1' "
            + "AND EntityId = @p", ("@p", rejectedProposalId.ToString("D"))));
    }

    /// <summary>
    /// R47 A-now over the real host and real SQL store: a null-document draft is prepared, approved by
    /// another user and recorded exactly once. Stale versions are terminal and replayable, company B
    /// cannot observe company A, missing authority is 403, and a non-null document fails closed through
    /// the registered unavailable owner without persisting a payment, command, audit or money event.
    /// The period and party owner answers are explicit SENTETIK test ports; no production grant is made.
    /// </summary>
    [SqlServerFact]
    public async Task Manual_bank_payment_is_company_scoped_replayable_and_fails_closed_for_an_unowned_document()
    {
        await using var run = await Run.StartAsync(services =>
        {
            services.RemoveAll<IFinancePartyAccountPort>();
            services.AddSingleton<SyntheticGate>();
            services.AddScoped<IFinancePartyAccountPort, SyntheticParties>();
            services.RemoveAll<IFinanceRecordPeriodRule>();
            services.AddScoped<IFinanceRecordPeriodRule, SyntheticOpenPeriodRule>();
        });
        const string beforeManualBankPayments = "20260921120000_AddReversalProposals";
        await using (var context = run.Context(CompanyA))
        {
            await context.GetService<IMigrator>().MigrateAsync(beforeManualBankPayments);
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'finance') "
                + "AND name IN (N'ManualBankPayments', N'OutgoingMoneyEvents')"));
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'finance.Commands') "
                + "AND name = N'ManualBankPaymentId'"));
            Assert.EndsWith("_AddReversalProposals",
                (await context.Database.GetAppliedMigrationsAsync()).Last(), StringComparison.Ordinal);

            await context.Database.MigrateAsync();
            Assert.Equal(2, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'finance') "
                + "AND name IN (N'ManualBankPayments', N'OutgoingMoneyEvents')"));
            Assert.Equal(1, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'finance.Commands') "
                + "AND name = N'ManualBankPaymentId'"));
            Assert.EndsWith("_AddFinanceAllocations",
                (await context.Database.GetAppliedMigrationsAsync()).Last(), StringComparison.Ordinal);
        }
        var account = await run.ApprovedBankAccountAsync(CompanyA);
        await run.GrantAsync(Proposer, CompanyA, ManualPaymentPreparerGrants);
        await run.GrantAsync(Approver, CompanyA, ManualPaymentDeciderGrants);
        using var preparer = run.Client(Proposer, CompanyA);
        using var decider = run.Client(Approver, CompanyA);

        var draftRequest = PaymentSave(account, linkedDocument: null);
        Guid paymentId;
        using (var saved = await preparer.SendAsync(Post(FinanceEndpoints.ManualBankPaymentsPath,
                   "SENTETIK-payment-save", Guid.NewGuid().ToString("N"), draftRequest)))
        {
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.True(saved.Headers.CacheControl is { Private: true, NoStore: true });
            var result = await Json(saved);
            Assert.Equal("Succeeded", result.GetProperty("outcome").GetString());
            paymentId = result.GetProperty("resource").GetProperty("id").GetGuid();
            Assert.Equal("Draft", result.GetProperty("resource").GetProperty("status").GetString());
            Assert.Equal("1", result.GetProperty("resource").GetProperty("version").GetString());
            Assert.Equal(JsonValueKind.Null, result.GetProperty("resource").GetProperty("linkedDocument").ValueKind);
        }

        const string downPositiveCommand = "SqlManualTargetDownPositive";
        const string downNegativeCommand = "SqlManualTargetDownNegative";
        const string upPositiveCommand = "SqlManualTargetUpPositive";
        const string upNegativeCommand = "SqlManualTargetUpNegative";
        await using (var context = run.Context(CompanyA))
        {
            var migrations = context.Database.GetMigrations().ToArray();
            var allocationMigration = Array.FindIndex(migrations,
                migration => migration.EndsWith("_AddFinanceAllocations", StringComparison.Ordinal));
            Assert.True(allocationMigration > 0);
            await context.GetService<IMigrator>().MigrateAsync(migrations[allocationMigration - 1]);

            Assert.Equal(1, await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO finance.Commands
                    (CompanyId, CommandName, IdempotencyKeyHash, ActorId, PayloadHash,
                     ManualBankPaymentId, ResultJson, CorrelationId, RecordedAtUtc)
                VALUES
                    ({CompanyA}, {downPositiveCommand}, {new string('A', 64)}, {Proposer}, {new string('B', 64)},
                     {paymentId}, {"{\"outcome\":\"Succeeded\"}"}, {Guid.NewGuid().ToString("N")}, {DateTimeOffset.UtcNow})
                """));
            var downFailure = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
                context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO finance.Commands
                        (CompanyId, CommandName, IdempotencyKeyHash, ActorId, PayloadHash,
                         BankAccountId, ManualBankPaymentId, ResultJson, CorrelationId, RecordedAtUtc)
                    VALUES
                        ({CompanyA}, {downNegativeCommand}, {new string('C', 64)}, {Proposer}, {new string('D', 64)},
                         {account}, {paymentId}, {"{\"outcome\":\"Succeeded\"}"}, {Guid.NewGuid().ToString("N")}, {DateTimeOffset.UtcNow})
                    """));
            Assert.Contains("CK_Commands_Target", downFailure.Message, StringComparison.Ordinal);
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM finance.Commands WHERE CompanyId = @companyId AND CommandName = @command",
                ("@companyId", CompanyA), ("@command", downNegativeCommand)));

            await context.Database.MigrateAsync();
            Assert.Equal(1, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM finance.Commands WHERE CompanyId = @companyId AND CommandName = @command AND ManualBankPaymentId = @paymentId",
                ("@companyId", CompanyA), ("@command", downPositiveCommand), ("@paymentId", paymentId)));
            Assert.Equal(1, await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO finance.Commands
                    (CompanyId, CommandName, IdempotencyKeyHash, ActorId, PayloadHash,
                     ManualBankPaymentId, ResultJson, CorrelationId, RecordedAtUtc)
                VALUES
                    ({CompanyA}, {upPositiveCommand}, {new string('E', 64)}, {Proposer}, {new string('F', 64)},
                     {paymentId}, {"{\"outcome\":\"Succeeded\"}"}, {Guid.NewGuid().ToString("N")}, {DateTimeOffset.UtcNow})
                """));
            var upFailure = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
                context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO finance.Commands
                        (CompanyId, CommandName, IdempotencyKeyHash, ActorId, PayloadHash,
                         BankAccountId, ManualBankPaymentId, ResultJson, CorrelationId, RecordedAtUtc)
                    VALUES
                        ({CompanyA}, {upNegativeCommand}, {new string('0', 64)}, {Proposer}, {new string('1', 64)},
                         {account}, {paymentId}, {"{\"outcome\":\"Succeeded\"}"}, {Guid.NewGuid().ToString("N")}, {DateTimeOffset.UtcNow})
                    """));
            Assert.Contains("CK_Commands_Target", upFailure.Message, StringComparison.Ordinal);
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM finance.Commands WHERE CompanyId = @companyId AND CommandName = @command",
                ("@companyId", CompanyA), ("@command", upNegativeCommand)));
        }

        using (var listed = await preparer.GetAsync(FinanceEndpoints.ManualBankPaymentsPath))
        {
            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
            Assert.Equal(paymentId, Single(await Json(listed)).GetProperty("id").GetGuid());
        }
        using (var detailed = await decider.GetAsync($"{FinanceEndpoints.ManualBankPaymentsPath}/{paymentId:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, detailed.StatusCode);
            Assert.Equal(paymentId, (await Json(detailed)).GetProperty("id").GetGuid());
        }

        var staleCorrelation = Guid.NewGuid().ToString("N");
        using (var stale = await decider.SendAsync(Post(
                   $"{FinanceEndpoints.ManualBankPaymentsPath}/{paymentId:D}/approval",
                   "SENTETIK-payment-stale", staleCorrelation,
                   new ApproveManualBankPaymentRequest(paymentId, "7"))))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal("version_conflict", Issue(await Json(stale)));
        }
        using (var staleReplay = await decider.SendAsync(Post(
                   $"{FinanceEndpoints.ManualBankPaymentsPath}/{paymentId:D}/approval",
                   "SENTETIK-payment-stale", Guid.NewGuid().ToString("N"),
                   new ApproveManualBankPaymentRequest(paymentId, "7"))))
        {
            Assert.Equal(HttpStatusCode.Conflict, staleReplay.StatusCode);
            AssertReplayOf(staleReplay, staleCorrelation);
        }

        using (var approved = await decider.SendAsync(Post(
                   $"{FinanceEndpoints.ManualBankPaymentsPath}/{paymentId:D}/approval",
                   "SENTETIK-payment-approve", Guid.NewGuid().ToString("N"),
                   new ApproveManualBankPaymentRequest(paymentId, "1"))))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
            var resource = (await Json(approved)).GetProperty("resource");
            Assert.Equal("Approved", resource.GetProperty("status").GetString());
            Assert.Equal("2", resource.GetProperty("version").GetString());
        }

        var recordCorrelation = Guid.NewGuid().ToString("N");
        using (var recorded = await decider.SendAsync(Post(
                   $"{FinanceEndpoints.ManualBankPaymentsPath}/{paymentId:D}/record",
                   "SENTETIK-payment-record", recordCorrelation,
                   new RecordManualBankPaymentRequest(paymentId, "2", BankResultConfirmed: true))))
        {
            Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);
            var resource = (await Json(recorded)).GetProperty("resource");
            Assert.Equal("Recorded", resource.GetProperty("status").GetString());
            Assert.Equal("3", resource.GetProperty("version").GetString());
            Assert.True(resource.GetProperty("bankResultConfirmed").GetBoolean());
        }
        using (var replayed = await decider.SendAsync(Post(
                   $"{FinanceEndpoints.ManualBankPaymentsPath}/{paymentId:D}/record",
                   "SENTETIK-payment-record", Guid.NewGuid().ToString("N"),
                   new RecordManualBankPaymentRequest(paymentId, "2", BankResultConfirmed: true))))
        {
            Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
            AssertReplayOf(replayed, recordCorrelation);
        }

        using var companyBWithoutGrant = run.Client(Approver, CompanyB);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await companyBWithoutGrant.GetAsync(FinanceEndpoints.ManualBankPaymentsPath)).StatusCode);
        await run.GrantAsync(TestUsers.CompanyB, CompanyB, FinancePermissionNames.ManualBankPaymentDraftRead);
        using var companyB = run.Client(TestUsers.CompanyB, CompanyB);
        Assert.Empty((await Json(await companyB.GetAsync(FinanceEndpoints.ManualBankPaymentsPath)))
            .GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound,
            (await companyB.GetAsync($"{FinanceEndpoints.ManualBankPaymentsPath}/{paymentId:D}")).StatusCode);

        var documentRequest = PaymentSave(account,
            new ManualBankPaymentLinkedDocumentRequest(Guid.NewGuid(), "document-v1"));
        using (var unavailable = await preparer.SendAsync(Post(FinanceEndpoints.ManualBankPaymentsPath,
                   "SENTETIK-payment-document", Guid.NewGuid().ToString("N"), documentRequest)))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.Equal("application/problem+json", unavailable.Content.Headers.ContentType?.MediaType);
            var problem = await Json(unavailable);
            Assert.Equal(503, problem.GetProperty("status").GetInt32());
            Assert.False(problem.TryGetProperty("outcome", out _));
            Assert.False(problem.TryGetProperty("issues", out _));
        }

        await using (var context = run.Context(CompanyA))
        {
            var error = await Assert.ThrowsAsync<SqlException>(() =>
                context.GetService<IMigrator>().MigrateAsync(beforeManualBankPayments));
            Assert.Equal(51053, error.Number);
            Assert.EndsWith("_AddManualBankPayments",
                (await context.Database.GetAppliedMigrationsAsync()).Last(), StringComparison.Ordinal);
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ManualBankPayments"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutgoingMoneyEvents"));
        Assert.Equal(4, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Commands WHERE ManualBankPaymentId = @id", ("@id", paymentId)));
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.ManualBankPayment'"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'finance') "
            + "AND name IN (N'ManualBankPayments', N'OutgoingMoneyEvents')"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'finance.Commands') "
            + "AND name = N'ManualBankPaymentId'"));
    }

    private static ManualBankPaymentDraftSaveRequest PaymentSave(
        Guid bankAccount,
        ManualBankPaymentLinkedDocumentRequest? linkedDocument) =>
        new(null, null, new ManualBankPaymentPreparation(Party, PartyAccount, bankAccount,
            new MoneyText("1000.00", Lira),
            new ProfileReference(ApprovedMoneyProfileCatalog.GdcTurkishLiraV1.ProfileId.ToString("D"),
                ApprovedMoneyProfileCatalog.GdcTurkishLiraV1.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new DateOnly(2026, 9, 18), null, null, null), linkedDocument);

    private static DraftSaveRequest Save(Guid bankAccount, Guid party, Guid? draftId, string? expectedVersion) =>
        new DraftSaveRequest(draftId, expectedVersion, new ReceiptPreparation(party, PartyAccount, bankAccount,
            new MoneyText("1000.00", Lira),
            new ProfileReference(ApprovedMoneyProfileCatalog.GdcTurkishLiraV1.ProfileId.ToString("D"),
                ApprovedMoneyProfileCatalog.GdcTurkishLiraV1.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new DateOnly(2026, 9, 18), null, null, null));

    private static HttpRequestMessage Post<T>(string path, string key, string correlation, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, FinanceJson.CreateOptions()), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", correlation);
        return request;
    }

    private static HttpRequestMessage Approval(Guid accountId, string key, string expectedVersion, string? correlation = null) =>
        Post($"{FinanceEndpoints.BankAccountsPath}/{accountId:D}/approval", key, correlation ?? Guid.NewGuid().ToString("N"),
            new BankAccountApprovalBody(expectedVersion));

    private static void AssertReplayOf(HttpResponseMessage response, string originalCorrelation)
    {
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal(originalCorrelation, Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
    }

    private static string? Issue(JsonElement result) =>
        Assert.Single(result.GetProperty("issues").EnumerateArray()).GetProperty("code").GetString();

    private static JsonElement Single(JsonElement page) => Assert.Single(page.GetProperty("items").EnumerateArray());

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>Holds the first two callers of the gate party until both arrived (at most 30 s).</summary>
    private sealed class SyntheticGate
    {
        private readonly TaskCompletionSource both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrived;

        public Task ArriveAsync()
        {
            if (Interlocked.Increment(ref arrived) >= 2)
            {
                both.TrySetResult();
            }

            return both.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    /// <summary>SENTETIK: one active party account of company A; the gate party waits for its twin; one party is out.</summary>
    private sealed class SyntheticParties(SyntheticGate gate) : IFinancePartyAccountPort
    {
        public async Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId,
            Guid companyAccountId, CancellationToken cancellationToken)
        {
            if (partyId == UnavailableParty)
            {
                return new ReadResult<PartyAccountEligibility>(ReadOutcome.Unavailable, null);
            }

            if (partyId == GateParty)
            {
                await gate.ArriveAsync();
            }

            return (partyId == Party || partyId == GateParty) && companyAccountId == PartyAccount && context.CompanyId == CompanyA
                ? new ReadResult<PartyAccountEligibility>(ReadOutcome.Found,
                    new PartyAccountEligibility(partyId, companyAccountId, CompanyA, IsActive: true, IsPartyActive: true))
                : new ReadResult<PartyAccountEligibility>(ReadOutcome.NotFound, null);
        }
    }

    /// <summary>Explicit HTTP-mapping test input; real period policy is covered by the relational FIN-T05/T06 cases.</summary>
    private sealed class SyntheticOpenPeriodRule : IFinanceRecordPeriodRule
    {
        public Task<RecordPeriodAnswer> CheckAsync(FinanceContext context, DateOnly occurredOn, DateOnly? valueOn,
            DateTimeOffset decidedAt, CancellationToken cancellationToken) =>
            Task.FromResult(new RecordPeriodAnswer(RecordPeriodOutcome.Open, null));
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }

        public static async Task<Run> StartAsync(Action<IServiceCollection>? configureServices = null)
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString, configureServices);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            // Idempotent if the host already applied Finance's migrations.
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            await using var context = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .Options, companyContext);
            await context.Database.MigrateAsync();
            return run;
        }

        /// <summary>A client whose key and correlation are set per request, never by default.</summary>
        public HttpClient Client(string developmentUser, Guid company)
        {
            var client = Factory.CreateClientFor(developmentUser, company);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            return client;
        }

        public FinanceDbContext Context(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .Options, companyContext);
        }

        /// <summary>A SENTETIK approved TRY account row of the company (two synthetic actors, Ö4 holds in the row).</summary>
        public async Task<Guid> ApprovedBankAccountAsync(Guid company)
        {
            var id = Guid.NewGuid();
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                "INSERT INTO finance.BankAccounts (Id, CompanyId, CurrencyId, DisplayName, MoneyMovementEligible, Iban, "
                + "Status, Version, ProposedBy, ProposedAtUtc, ApprovedBy, ApprovedAtUtc, UpdatedAtUtc) VALUES "
                + "(@id, @c, @cur, N'SENTETIK banka', 1, N'TR330006100519786457841326', 2, 2, N'SENTETIK-proposer', "
                + "SYSDATETIMEOFFSET(), N'SENTETIK-approver', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())",
                connection);
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@c", company);
            command.Parameters.AddWithValue("@cur", Lira);
            await command.ExecuteNonQueryAsync();
            return id;
        }

        /// <summary>A SENTETIK pending proposal row of the company (proposed, not approved).</summary>
        public async Task<Guid> PendingProposalAsync(Guid company)
        {
            var id = Guid.NewGuid();
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                "INSERT INTO finance.BankAccounts (Id, CompanyId, CurrencyId, DisplayName, Status, Version, ProposedBy, "
                + "ProposedAtUtc, UpdatedAtUtc) VALUES (@id, @c, @cur, N'SENTETIK bekleyen', 1, 1, N'SENTETIK-proposer', "
                + "SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())",
                connection);
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@c", company);
            command.Parameters.AddWithValue("@cur", Lira);
            await command.ExecuteNonQueryAsync();
            return id;
        }

        /// <summary>
        /// A synthetic role with exactly these permissions in one company, assigned to a configured
        /// development user (linked to a new ERP account first if it has none).
        /// </summary>
        public async Task GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(
                x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Finance user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor,
                    BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"FIN-UI1-{roleId:N}", "Synthetic Finance test role", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company,
                DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
        }

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }
    }
}
