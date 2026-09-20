using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SALES-A1 against a REAL SQL Server: the internal approval axis of a stored draft — the store's
/// one-save transitions with their attempt records and audit events, the ADR-011 §3/§4/§7/§8
/// answers under the real unique keys and concurrency tokens, the check constraint that binds the
/// approval facts to the state, the line reference port (Business 5737989917 = B: only an approved
/// line is a need), and the three routes through the REAL host with fresh synthetic grants. The
/// drafts are built from ISOLATED SYNTHETIC owner snapshots (the store validates nothing about the
/// owners); this proves the approval slice, not a sale. Every identity is SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesDraftApprovalSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 9, 19);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Currency = Guid.Parse("7a1d2c3b-4a59-4687-9a0b-1c2d3e4f5a91");
    private static readonly Guid MoneyProfile = Guid.Parse("7a1d2c3b-4a59-4687-9a0b-1c2d3e4f5a92");
    private static readonly Guid KgUnit = Guid.Parse("7a1d2c3b-4a59-4687-9a0b-1c2d3e4f5a93");
    private static readonly Guid KgProfile = Guid.Parse("7a1d2c3b-4a59-4687-9a0b-1c2d3e4f5a94");
    private static readonly Guid Product = Guid.Parse("7a1d2c3b-4a59-4687-9a0b-1c2d3e4f5a95");
    private static readonly string[] DraftGrants = [SalesPermissionManifestV1.DraftsCreate, SalesPermissionManifestV1.DraftsRead];
    private static readonly string[] ApproverGrants = [SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersApprove];

    [SqlServerFact]
    public async Task A_draft_moves_along_the_approval_axis_in_one_save_each_and_the_port_answers_only_an_approved_line()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        SalesOrderDraft draft;
        Guid lineId;
        string wideCursor;
        await using (var a = run.Store(CompanyA))
        {
            draft = (await a.Store.StoreDraftAsync(run.Envelope("k-create"), Hash("payload-1"), documentNo => Draft(owners, documentNo, T0))).Draft;
            lineId = draft.Lines[0].Id;
            // Business 5737989917 = B: a draft that is being prepared is not a need.
            var answer = await a.Port.GetApprovedLineAsync(draft.Id, lineId);
            Assert.Equal((SalesOrderLineReferenceOutcomeV1.NotApproved, "Preparing"), (answer.Outcome, answer.ApprovalState));
            Assert.Null(answer.Line);
        }

        // Submit: Preparing → PendingApproval in one save (header, attempt, audit); the same key answers again without a second effect.
        SalesDraftStateResult submitted;
        await using (var a = run.Store(CompanyA))
        {
            submitted = (await a.Store.UpdateDraftStateAsync(run.Envelope("k-submit"), Hash("submit-2"), SalesDraftCommands.SubmitV1, draft.Id,
                (d, now) => d.Submit(2, run.Actor, now), Submitted))!;
            Assert.Equal((SalesDraftWriteOutcome.Recorded, InternalApprovalState.PendingApproval, 3L, run.Actor),
                (submitted.Outcome, submitted.Draft.ApprovalState, submitted.Draft.Version, submitted.Draft.Approval.SubmittedBy));
            var replay = (await a.Store.UpdateDraftStateAsync(run.Envelope("k-submit", correlation: "SENTETIK-second"), Hash("submit-2"),
                SalesDraftCommands.SubmitV1, draft.Id, (_, _) => throw new InvalidOperationException("a replay never mutates"), Submitted))!;
            Assert.Equal((SalesDraftWriteOutcome.Replayed, run.Correlation, InternalApprovalState.PendingApproval), (replay.Outcome, replay.OriginalCorrelationId, replay.Draft.ApprovalState));
            var pending = await a.Port.GetApprovedLineAsync(draft.Id, lineId);
            Assert.Equal((SalesOrderLineReferenceOutcomeV1.NotApproved, "PendingApproval"), (pending.Outcome, pending.ApprovalState));
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT ApprovalState FROM sales.Drafts WHERE Id = @id", ("@id", draft.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE CommandName = @c AND DraftId = @id AND Outcome = 1",
            ("@c", SalesDraftCommands.SubmitV1), ("@id", draft.Id)));
        Assert.Equal(run.Actor, await run.ScalarAsync<string>("SELECT SubmittedBy FROM sales.Drafts WHERE Id = @id", ("@id", draft.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Sales.OrderDraft' AND EventType = N'sales.order-draft.submitted.v1' "
            + "AND EntityId = @id AND EntityVersionBefore = 2 AND EntityVersionAfter = 3 AND CorrelationId = @c", ("@id", draft.Id.ToString("D")), ("@c", run.Correlation)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = N'SENTETIK-second'"));

        // Approve: PendingApproval → Approved; the port now answers the line's stored facts; the lines are closed.
        await using (var a = run.Store(CompanyA))
        {
            var approved = (await a.Store.UpdateDraftStateAsync(run.Envelope("k-approve", actor: "SENTETIK-approver"), Hash("approve-3"),
                SalesDraftCommands.ApproveV1, draft.Id, (d, now) => d.Approve(3, "SENTETIK-approver", now), Approved))!;
            Assert.Equal((InternalApprovalState.Approved, 4L, run.Actor, "SENTETIK-approver"),
                (approved.Draft.ApprovalState, approved.Draft.Version, approved.Draft.Approval.SubmittedBy, approved.Draft.Approval.DecidedBy));
            var need = await a.Port.GetApprovedLineAsync(draft.Id, lineId);
            Assert.Equal(SalesOrderLineReferenceOutcomeV1.Approved, need.Outcome);
            Assert.Equal((CompanyA, draft.Id, lineId, 1, owners.Party, owners.Account, Product, "7", KgUnit, "1.25", 0, 4L),
                (need.Line!.CompanyId, need.Line.OrderId, need.Line.LineId, need.Line.LineNo, need.Line.PartyId, need.Line.CompanyAccountId,
                    need.Line.ProductId, need.Line.ProductVersion, need.Line.OrderUnitId, need.Line.QuantityText, need.Line.Revision, need.Line.Version));
            Assert.Equal(SalesOrderLineReferenceOutcomeV1.NotFound, (await a.Port.GetApprovedLineAsync(draft.Id, Guid.NewGuid())).Outcome);
            Assert.Equal(SalesOrderLineReferenceOutcomeV1.NotFound, (await a.Port.GetApprovedLineAsync(Guid.NewGuid(), lineId)).Outcome);
            // DEC-SAL-002: no further A1 transition applies to an approved draft; nothing is stored for the refused attempt.
            var closed = await Assert.ThrowsAsync<SalesRuleException>(() => a.Store.UpdateDraftStateAsync(run.Envelope("k-approve-again"), Hash("approve-4"),
                SalesDraftCommands.ApproveV1, draft.Id, (d, now) => d.Approve(4, "SENTETIK-approver", now), Approved));
            Assert.Equal(SalesRuleCodes.StateConflict, closed.Code);
            Assert.Equal([draft.Id], (await a.Store.ListDraftsAsync(10, null, default, InternalApprovalState.Approved)).Items.Select(item => item.Id));
            Assert.Empty((await a.Store.ListDraftsAsync(10, null, default, InternalApprovalState.PendingApproval)).Items);
        }

        // LOG-4b (FİNANS #358 P2): the approved-lines read is a LINE page with a composite, company-bound cursor. A newer
        // three-line approved draft in front of the one-line draft: pageSize 2 answers the newer draft's lines 1–2 and a cursor
        // INSIDE that draft; the cursor answers line 3, then the older draft's line, then null — nothing skipped, nothing repeated.
        SalesOrderDraft wide;
        await using (var a = run.Store(CompanyA))
        {
            wide = (await a.Store.StoreDraftAsync(run.Envelope("k-create-wide"), Hash("payload-wide"), documentNo => Draft(owners, documentNo, T0.AddMinutes(5), lines: 3))).Draft;
            await a.Store.UpdateDraftStateAsync(run.Envelope("k-submit-wide"), Hash("submit-wide"), SalesDraftCommands.SubmitV1, wide.Id, (d, now) => d.Submit(4, run.Actor, now), Submitted);
            await a.Store.UpdateDraftStateAsync(run.Envelope("k-approve-wide", actor: "SENTETIK-approver"), Hash("approve-wide"), SalesDraftCommands.ApproveV1, wide.Id,
                (d, now) => d.Approve(5, "SENTETIK-approver", now), Approved);
            var pageOne = await a.Port.ListApprovedLinesAsync(2, null);
            Assert.Equal([(wide.Id, 1), (wide.Id, 2)], pageOne.Items.Select(item => (item.OrderId, item.LineNo)));
            Assert.NotNull(pageOne.NextCursor);
            var pageTwo = await a.Port.ListApprovedLinesAsync(2, pageOne.NextCursor);
            Assert.Equal([(wide.Id, 3), (draft.Id, 1)], pageTwo.Items.Select(item => (item.OrderId, item.LineNo)));
            Assert.Null(pageTwo.NextCursor);
            // pageSize 3 stops exactly at the wide draft's boundary: the cursor still resumes at the older draft, once.
            var exact = await a.Port.ListApprovedLinesAsync(3, null);
            Assert.Equal(3, exact.Items.Count);
            Assert.NotNull(exact.NextCursor);
            var rest = await a.Port.ListApprovedLinesAsync(3, exact.NextCursor);
            Assert.Equal([(draft.Id, 1)], rest.Items.Select(item => (item.OrderId, item.LineNo)));
            Assert.Null(rest.NextCursor);
            Assert.Equal(4, (await a.Port.ListApprovedLinesAsync(100, null)).Items.Count);
            Assert.Null((await a.Port.ListApprovedLinesAsync(100, null)).NextCursor);
            Assert.DoesNotContain(pageOne.Items.Concat(pageTwo.Items), item => item.CompanyId != CompanyA);
            await Assert.ThrowsAsync<ArgumentException>(() => a.Port.ListApprovedLinesAsync(2, "SENTETIK-not-a-cursor"));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => a.Port.ListApprovedLinesAsync(101, null));
            wideCursor = pageOne.NextCursor!;
        }

        // Company B holding A's composite cursor gets nothing of A: the store refuses the inner cursor as another company's.
        await using (var b = run.Store(CompanyB))
        {
            await Assert.ThrowsAsync<ArgumentException>(() => b.Port.ListApprovedLinesAsync(2, wideCursor));
            Assert.Empty((await b.Port.ListApprovedLinesAsync(2, null)).Items);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE IdempotencyKeyHash = @h", ("@h", SalesStore.IdempotencyKeyHash("k-approve-again"))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'sales.order-draft.approved.v1' AND EntityId = @id AND ActorId = N'SENTETIK-approver' AND EntityVersionAfter = 4",
            ("@id", draft.Id.ToString("D"))));

        // Company B can neither see the draft, nor its line through the port, nor move it; its same key is its own attempt.
        await using (var b = run.Store(CompanyB))
        {
            Assert.Null(await b.Store.FindDraftAsync(draft.Id));
            Assert.Equal(SalesOrderLineReferenceOutcomeV1.NotFound, (await b.Port.GetApprovedLineAsync(draft.Id, lineId)).Outcome);
            Assert.Null(await b.Store.UpdateDraftStateAsync(run.Envelope("k-submit"), Hash("submit-2"), SalesDraftCommands.SubmitV1, draft.Id,
                (_, _) => throw new InvalidOperationException("never reached"), Submitted));
        }

        // Reject on a second draft: the reason is stored; the rejected draft can be submitted again as Rev.0; a stale version is refused.
        SalesOrderDraft second;
        await using (var a = run.Store(CompanyA))
        {
            second = (await a.Store.StoreDraftAsync(run.Envelope("k-create-2"), Hash("payload-2"), documentNo => Draft(owners, documentNo, T0.AddMinutes(1)))).Draft;
            await a.Store.UpdateDraftStateAsync(run.Envelope("k-submit-2"), Hash("submit-2b"), SalesDraftCommands.SubmitV1, second.Id, (d, now) => d.Submit(2, run.Actor, now), Submitted);
            var rejected = (await a.Store.UpdateDraftStateAsync(run.Envelope("k-reject-2", actor: "SENTETIK-approver"), Hash("reject-3"), SalesDraftCommands.RejectV1, second.Id,
                (d, now) => d.Reject(3, "SENTETIK-approver", "SENTETIK neden", now), Rejected))!;
            Assert.Equal((InternalApprovalState.Rejected, 4L, "SENTETIK neden", 0), (rejected.Draft.ApprovalState, rejected.Draft.Version, rejected.Draft.Approval.RejectionReason, rejected.Draft.Revision));
            var stale = await Assert.ThrowsAsync<SalesVersionConflictException>(() => a.Store.UpdateDraftStateAsync(run.Envelope("k-stale"), Hash("submit-1"),
                SalesDraftCommands.SubmitV1, second.Id, (d, now) => d.Submit(1, run.Actor, now), Submitted));
            Assert.Equal((1L, 4L), (stale.ExpectedVersion, stale.CurrentVersion));
            // ADR-011 §7: the caller records the refusal under its key and the same key replays it.
            await a.Store.RecordRefusalAsync(run.Envelope("k-stale"), Hash("submit-1"),
                new SalesDraftTerminalRefusal(409, "version_conflict", [new("expectedVersion", SalesRuleCodes.VersionConflict)]), default, SalesDraftCommands.SubmitV1);
        }

        await using (var a = run.Store(CompanyA))
        {
            var replayed = await a.Store.FindReplayAsync(run.Envelope("k-stale"), Hash("submit-1"), default, SalesDraftCommands.SubmitV1);
            Assert.Equal((409, "version_conflict", run.Correlation), (replayed!.Refusal!.Status, replayed.Refusal.Code, replayed.OriginalCorrelationId));
            Assert.Null(replayed.Draft);
            // The same key under another command is another attempt.
            Assert.Null(await a.Store.FindReplayAsync(run.Envelope("k-stale"), Hash("submit-1"), default, SalesDraftCommands.ApproveV1));
            var resubmitted = (await a.Store.UpdateDraftStateAsync(run.Envelope("k-submit-2-again"), Hash("submit-4"), SalesDraftCommands.SubmitV1, second.Id,
                (d, now) => d.Submit(4, run.Actor, now), Submitted))!;
            Assert.Equal((InternalApprovalState.PendingApproval, 5L), (resubmitted.Draft.ApprovalState, resubmitted.Draft.Version));
            Assert.Null(resubmitted.Draft.Approval.RejectionReason);
        }

        // The resubmission cleared the decision: the stored reason is gone with it, the audit event keeps it.
        Assert.Equal("<null>", await run.ScalarAsync<string>("SELECT ISNULL(RejectionReason, N'<null>') FROM sales.Drafts WHERE Id = @id", ("@id", second.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'sales.order-draft.rejected.v1' AND EntityId = @id AND AfterJson LIKE N'%SENTETIK neden%'",
            ("@id", second.Id.ToString("D"))));

        // §8: two submitters with the same key and content both pass the attempt lookup; the attempt key lets exactly one through.
        SalesOrderDraft third;
        await using (var a = run.Store(CompanyA))
        {
            third = (await a.Store.StoreDraftAsync(run.Envelope("k-create-3"), Hash("payload-3"), documentNo => Draft(owners, documentNo, T0.AddMinutes(2)))).Draft;
        }

        var barrier = new Barrier(2);
        async Task<object> SubmitAsync()
        {
            await using var writer = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                var result = await writer.Store.UpdateDraftStateAsync(run.Envelope("k-race"), Hash("submit-race"), SalesDraftCommands.SubmitV1, third.Id,
                    (d, now) => d.Submit(2, run.Actor, now), Submitted);
                return result!.Outcome;
            }
            catch (SalesDraftStoreException exception)
            {
                return exception.Code;
            }
        }

        var results = await Task.WhenAll(Task.Run(SubmitAsync), Task.Run(SubmitAsync));
        Assert.Single(results, result => result is SalesDraftWriteOutcome.Recorded);
        Assert.Single(results, result => result is string code && code == SalesDraftStoreCodes.ConcurrentChange);
        await using (var a = run.Store(CompanyA))
        {
            var retry = (await a.Store.UpdateDraftStateAsync(run.Envelope("k-race"), Hash("submit-race"), SalesDraftCommands.SubmitV1, third.Id,
                (_, _) => throw new InvalidOperationException("a replay never mutates"), Submitted))!;
            Assert.Equal((SalesDraftWriteOutcome.Replayed, InternalApprovalState.PendingApproval, 3L), (retry.Outcome, retry.Draft.ApprovalState, retry.Draft.Version));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE IdempotencyKeyHash = @h", ("@h", SalesStore.IdempotencyKeyHash("k-race"))));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'sales.order-draft.submitted.v1' AND EntityId = @id", ("@id", third.Id.ToString("D"))));
        Assert.Equal(3L, await run.ScalarAsync<long>("SELECT Version FROM sales.Drafts WHERE Id = @id", ("@id", third.Id)));

        // The schema binds the facts to the state: "approved" without a decision is refused by SQL Server itself.
        var constraint = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync("UPDATE sales.Drafts SET ApprovalState = 3 WHERE Id = @id", ("@id", third.Id)));
        Assert.Equal(547, constraint.Number);
        Assert.Contains("CK_Drafts_Approval", constraint.Message, StringComparison.Ordinal);
        // Nothing else of the draft moved along the way: lines, slices and the number counter are as stored — the three
        // one-line drafts plus the three-line `wide` draft of the line-page check above (6 lines, 4 drafts).
        Assert.Equal(6, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftLines"));
        Assert.Equal(4, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts WHERE Kind = 1 AND State = 1 AND Revision = 0"));
    }

    [SqlServerFact]
    public async Task The_approval_routes_answer_only_with_fresh_grants_replay_the_persisted_result_and_keep_other_companies_out()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        SalesOrderDraft draft;
        await using (var a = run.Store(CompanyA))
        {
            draft = (await a.Store.StoreDraftAsync(run.Envelope("k-create"), Hash("payload-1"), documentNo => Draft(owners, documentNo, T0))).Draft;
        }

        var preparer = await run.GrantAsync(TestUsers.CompanyA, CompanyA, DraftGrants);
        var approver = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, ApproverGrants);
        using var creator = run.Factory.CreateClientFor(preparer, CompanyA);
        using var decider = run.Factory.CreateClientFor(approver, CompanyA);
        foreach (var client in new[] { creator, decider })
        {
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        }

        // The preparer submits under the create grant; the same key answers the persisted 200 with the replay headers.
        var submitCorrelation = Guid.NewGuid().ToString("N");
        using (var submitted = await creator.SendAsync(Command(draft.Id, "submission", "SENTETIK-submit", "{\"expectedVersion\":\"2\"}", submitCorrelation)))
        {
            Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
            Assert.False(submitted.Headers.Contains("Idempotency-Replayed"));
            var body = await Result(submitted);
            Assert.Equal(("PendingApproval", "3", false), (body.GetProperty("detail").GetProperty("draft").GetProperty("approvalState").GetString(),
                body.GetProperty("detail").GetProperty("draft").GetProperty("version").GetString(), body.GetProperty("isReplay").GetBoolean()));
            Assert.False(string.IsNullOrEmpty(body.GetProperty("detail").GetProperty("approval").GetProperty("submittedBy").GetString()));
            Assert.Equal(JsonValueKind.Null, body.GetProperty("detail").GetProperty("approval").GetProperty("decidedBy").ValueKind);
        }

        using (var replay = await creator.SendAsync(Command(draft.Id, "submission", "SENTETIK-submit", "{\"expectedVersion\":\"2\"}", Guid.NewGuid().ToString("N"))))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(submitCorrelation, Assert.Single(replay.Headers.GetValues("Idempotency-Original-Correlation-Id")));
            Assert.True((await Result(replay)).GetProperty("isReplay").GetBoolean());
        }

        // The preparer holds no approve grant: the decision routes refuse, code-less, and nothing is recorded.
        using (var refused = await creator.SendAsync(Command(draft.Id, "approval", "SENTETIK-creator-approves", "{\"expectedVersion\":\"3\"}", Guid.NewGuid().ToString("N"))))
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE IdempotencyKeyHash = @h", ("@h", SalesStore.IdempotencyKeyHash("SENTETIK-creator-approves"))));

        // The approver approves; a second decision on the approved draft is the recorded 409 (§7), replayed with its first correlation.
        using (var approved = await decider.SendAsync(Command(draft.Id, "approval", "SENTETIK-approve", "{\"expectedVersion\":\"3\"}", Guid.NewGuid().ToString("N"))))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
            var body = await Result(approved);
            Assert.Equal(("Approved", "4"), (body.GetProperty("detail").GetProperty("draft").GetProperty("approvalState").GetString(),
                body.GetProperty("detail").GetProperty("draft").GetProperty("version").GetString()));
            Assert.False(string.IsNullOrEmpty(body.GetProperty("detail").GetProperty("approval").GetProperty("decidedBy").GetString()));
        }

        var conflictCorrelation = Guid.NewGuid().ToString("N");
        using (var closed = await decider.SendAsync(Command(draft.Id, "rejection", "SENTETIK-reject-approved", "{\"expectedVersion\":\"4\",\"reason\":\"SENTETIK\"}", conflictCorrelation)))
        {
            Assert.Equal(HttpStatusCode.Conflict, closed.StatusCode);
            var problem = await Result(closed);
            Assert.Equal((SalesDraftProblemCodesV1.StateConflict, "approvalState"), (problem.GetProperty("code").GetString(), problem.GetProperty("errors")[0].GetProperty("field").GetString()));
            Assert.False(closed.Headers.Contains("Idempotency-Replayed"));
        }

        using (var replayedConflict = await decider.SendAsync(Command(draft.Id, "rejection", "SENTETIK-reject-approved", "{\"expectedVersion\":\"4\",\"reason\":\"SENTETIK\"}", Guid.NewGuid().ToString("N"))))
        {
            Assert.Equal(HttpStatusCode.Conflict, replayedConflict.StatusCode);
            Assert.Equal("true", Assert.Single(replayedConflict.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(conflictCorrelation, Assert.Single(replayedConflict.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        // A stale version is its own 409; a malformed body is 400 before the service; another company's draft is 404.
        using (var stale = await decider.SendAsync(Command(draft.Id, "approval", "SENTETIK-stale", "{\"expectedVersion\":\"1\"}", Guid.NewGuid().ToString("N"))))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.VersionConflict, (await Result(stale)).GetProperty("code").GetString());
        }

        using (var shape = await decider.SendAsync(Command(draft.Id, "approval", "SENTETIK-shape", "{\"expectedVersion\":\"4\",\"reason\":\"x\"}", Guid.NewGuid().ToString("N"))))
        {
            Assert.Equal(HttpStatusCode.BadRequest, shape.StatusCode);
        }

        var readerOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, [.. DraftGrants, .. ApproverGrants]);
        using (var clientOfB = run.Factory.CreateClientFor(readerOfB, CompanyB))
        {
            clientOfB.DefaultRequestHeaders.Remove("Idempotency-Key");
            Assert.Equal(HttpStatusCode.NotFound, (await clientOfB.SendAsync(Command(draft.Id, "submission", "SENTETIK-b", "{\"expectedVersion\":\"4\"}", Guid.NewGuid().ToString("N")))).StatusCode);
        }

        // The list narrows to the axis, the detail carries the facts; an unknown state name is 400.
        var approvedList = await Result(await creator.GetAsync($"{SalesEndpoints.DraftsPath}?approvalState=Approved"));
        Assert.Equal(draft.Id, Assert.Single(approvedList.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Empty((await Result(await creator.GetAsync($"{SalesEndpoints.DraftsPath}?approvalState=PendingApproval"))).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await creator.GetAsync($"{SalesEndpoints.DraftsPath}?approvalState=approved")).StatusCode);
        var detail = await Result(await creator.GetAsync($"{SalesEndpoints.DraftsPath}/{draft.Id:D}"));
        Assert.Equal("Approved", detail.GetProperty("draft").GetProperty("approvalState").GetString());
        Assert.Equal("Rev.0", detail.GetProperty("draft").GetProperty("revision").GetString());

        Assert.Equal(3, await run.ScalarAsync<int>("SELECT ApprovalState FROM sales.Drafts WHERE Id = @id", ("@id", draft.Id)));
        Assert.Equal(4L, await run.ScalarAsync<long>("SELECT Version FROM sales.Drafts WHERE Id = @id", ("@id", draft.Id)));
        // create + submit + approve stored the draft; the closed rejection and the stale approval are the two recorded refusals.
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE DraftId = @id AND Outcome = 1", ("@id", draft.Id)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE Outcome = 2 AND ISJSON(RefusalJson) = 1"));
        // UX_DraftAttempts_DraftId, narrowed to the create command, still guards creation: a second create attempt
        // for the same stored draft is refused by the index itself (2601), while the state attempts above shared the id.
        var duplicateCreate = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "INSERT INTO sales.DraftAttempts (CompanyId, CommandName, IdempotencyKeyHash, RequestHash, ActorId, Outcome, DraftId, CorrelationId, RecordedAtUtc) "
            + "VALUES (@c, @n, @k, @r, N'SENTETIK-actor', 1, @id, N'SENTETIK-correlation', SYSDATETIMEOFFSET()); SELECT 1",
            ("@c", CompanyA), ("@n", SalesDraftCommands.CreateV1), ("@k", new string('a', 64)), ("@r", new string('b', 64)), ("@id", draft.Id)));
        Assert.Equal(2601, duplicateCreate.Number);
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id AND EventType IN (N'sales.order-draft.submitted.v1', N'sales.order-draft.approved.v1')",
            ("@id", draft.Id.ToString("D"))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'sales.order-draft.rejected.v1'"));
    }

    /// <summary>
    /// ÜRÜN #338 P3-2: the approval migration's Down is a forward fix while any draft has left Preparing —
    /// THROW 51045 (the module rule of 51041–51044), the history unchanged, the facts untouched.
    /// </summary>
    /// <summary>
    /// K3 Head B (Sinan 5748258299): the real-SQL success path of an AUTHORISED price write — the item Head A (#370)
    /// carried. Through the SERVICE over the real store, with a recording authority that grants ONLY the separate price
    /// name at runtime to an actor who never held the create name, on a draft created, submitted and approved without
    /// it: (1) the authority is ASKED, with that name and this company, and answered yes — pinned apart from the outcome;
    /// (2) the approved revision's rows are written FIRST and beside the header's advance in ONE save, and the OLD price
    /// stays in <c>sales.DraftRevisionLines</c> as the exact text "12.50" — the decision measured, not a row's presence;
    /// (3) every money text is read back as TEXT (Ö2); (4) the same key replays, a different key with the same values is
    /// the refusal, and an actor holding create/read/approve but NOT the price name is 403 with nothing changed. The same
    /// through the HTTP route waits on the central host adapters (queued behind CARİ's next head).
    /// </summary>
    [SqlServerFact]
    public async Task An_approved_drafts_price_change_archives_the_approved_revision_and_advances_it_in_one_save_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        SalesOrderDraft draft;
        Guid lineId;
        await using (var a = run.Store(CompanyA))
        {
            draft = (await a.Store.StoreDraftAsync(run.Envelope("k-create"), Hash("payload-1"), documentNo => PricedDraft(owners, documentNo, T0))).Draft;
            lineId = draft.Lines[0].Id;
            await a.Store.UpdateDraftStateAsync(run.Envelope("k-submit"), Hash("submit-2"), SalesDraftCommands.SubmitV1, draft.Id,
                (d, now) => d.Submit(2, run.Actor, now), Submitted);
            await a.Store.UpdateDraftStateAsync(run.Envelope("k-approve", actor: "SENTETIK-approver"), Hash("approve-3"),
                SalesDraftCommands.ApproveV1, draft.Id, (d, now) => d.Approve(3, "SENTETIK-approver", now), Approved);
        }

        Assert.Equal((3, 0, 4L, "12.50"), (await run.ScalarAsync<int>("SELECT ApprovalState FROM sales.Drafts WHERE Id = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT Revision FROM sales.Drafts WHERE Id = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT Version FROM sales.Drafts WHERE Id = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT UnitPrice FROM sales.DraftLines WHERE DraftId = @id", ("@id", draft.Id))));

        var request = new SalesDraftCommercialRevisionRequest("4", [new SalesDraftLineCommercialV1(lineId, "13.00", KgUnit, "5")]);
        const string pricer = "SENTETIK-pricer";

        // (4a) An actor with the draft names and the approval name but NOT the price name: asked, answered no, nothing changes.
        await using (var a = run.Store(CompanyA))
        {
            var unpriced = new RecordingAuthority(SalesPermissionManifestV1.DraftsCreate, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersApprove);
            var service = run.Service(a, pricer, unpriced);
            var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => service.ReviseCommercialAsync(draft.Id, request, run.Metadata("k-revise-unpriced")));
            Assert.Equal((403, "forbidden"), (refused.Status, refused.Code));
            Assert.Equal([(SalesPermissionManifestV1.OrdersPriceWrite, CompanyA)], unpriced.Measured);
        }

        Assert.Equal((0, 4L), (await run.ScalarAsync<int>("SELECT Revision FROM sales.Drafts WHERE Id = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT Version FROM sales.Drafts WHERE Id = @id", ("@id", draft.Id))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftRevisions WHERE DraftId = @id", ("@id", draft.Id)));

        // (1) The pricer holds ONLY the separate price name: asked with that name in this company, answered yes, then the ONE save.
        var priced = new RecordingAuthority(SalesPermissionManifestV1.OrdersPriceWrite);
        await using (var a = run.Store(CompanyA))
        {
            var service = run.Service(a, pricer, priced);
            var revised = await service.ReviseCommercialAsync(draft.Id, request, run.Metadata("k-revise"));
            Assert.False(revised.IsReplay);
            Assert.Equal(("Rev.1", "Preparing", "5", "13.00", KgUnit, "5"), (revised.Detail.Draft.Revision, revised.Detail.Draft.ApprovalState, revised.Detail.Draft.Version,
                revised.Detail.Lines[0].UnitPrice, revised.Detail.Lines[0].PriceUnitId, revised.Detail.Lines[0].DiscountPercent));
            Assert.Equal(new SalesDraftApprovalV1(null, null, null, null, null), revised.Detail.Approval);
            // Asked and answered are pinned apart from the outcome, by the PROPERTY and not by a count of calls: the authority was asked,
            // every ask was for the separate price name in THIS company, and no other name was ever asked for this write (a count
            // would pin the service's call pattern, which may legitimately gain a fence; the ordering of the fences — before the
            // replay read and again before the save — is the service's own pin in SalesDraftServiceTests).
            Assert.NotEmpty(priced.Measured);
            Assert.All(priced.Measured, ask => Assert.Equal((SalesPermissionManifestV1.OrdersPriceWrite, CompanyA), ask));

            // The same key again through the service: the stored result, marked as the replay; the authority is asked again first.
            var replay = await service.ReviseCommercialAsync(draft.Id, request, run.Metadata("k-revise", correlation: "SENTETIK-second"));
            Assert.Equal((true, run.Correlation, "Rev.1", "5"), (replay.IsReplay, replay.OriginalCorrelationId, replay.Detail.Draft.Revision, replay.Detail.Draft.Version));

            // The history through the read: Rev.0 with its facts and the OLD texts.
            var history = await service.GetDraftRevisionsAsync(draft.Id);
            var item = Assert.Single(history.Items);
            Assert.Equal(("Rev.0", run.Actor, "SENTETIK-approver", pricer, "12.50", "5", 2), (item.Revision, item.SubmittedBy, item.ApprovedBy, item.ArchivedBy,
                item.Lines[0].UnitPrice, item.Lines[0].DiscountPercent, item.MoneyScale));
        }

        // (2)(3) The rows, read back by SQL as TEXT: the header advanced, the line carries the new texts, Rev.0 keeps the OLD ones.
        Assert.Equal((1, 1, 5L, "<null>"), (await run.ScalarAsync<int>("SELECT ApprovalState FROM sales.Drafts WHERE Id = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT Revision FROM sales.Drafts WHERE Id = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT Version FROM sales.Drafts WHERE Id = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT ISNULL(SubmittedBy, N'<null>') FROM sales.Drafts WHERE Id = @id", ("@id", draft.Id))));
        Assert.Equal(("13.00", "5"), (await run.ScalarAsync<string>("SELECT UnitPrice FROM sales.DraftLines WHERE DraftId = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT DiscountPercent FROM sales.DraftLines WHERE DraftId = @id", ("@id", draft.Id))));
        Assert.Equal(KgUnit, await run.ScalarAsync<Guid>("SELECT PriceUnitId FROM sales.DraftLines WHERE DraftId = @id", ("@id", draft.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.DraftRevisions WHERE CompanyId = @c AND DraftId = @id AND Revision = 0 AND SubmittedBy = @s AND ApprovedBy = N'SENTETIK-approver' "
            + "AND ArchivedBy = @p AND MoneyScale = 2 AND CorrelationId = @corr",
            ("@c", CompanyA), ("@id", draft.Id), ("@s", run.Actor), ("@p", pricer), ("@corr", run.Correlation)));
        Assert.Equal("12.50", await run.ScalarAsync<string>(
            "SELECT UnitPrice FROM sales.DraftRevisionLines WHERE CompanyId = @c AND DraftId = @id AND Revision = 0 AND LineNo = 1 AND LineId = @l",
            ("@c", CompanyA), ("@id", draft.Id), ("@l", lineId)));
        Assert.Equal(("5", "1.25"), (await run.ScalarAsync<string>("SELECT DiscountPercent FROM sales.DraftRevisionLines WHERE DraftId = @id AND Revision = 0", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT QuantityText FROM sales.DraftRevisionLines WHERE DraftId = @id AND Revision = 0", ("@id", draft.Id))));
        Assert.Equal(KgUnit, await run.ScalarAsync<Guid>("SELECT PriceUnitId FROM sales.DraftRevisionLines WHERE DraftId = @id AND Revision = 0", ("@id", draft.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE CommandName = @cmd AND DraftId = @id AND Outcome = 1 AND ActorId = @p",
            ("@cmd", SalesDraftCommands.ReviseCommercialV1), ("@id", draft.Id), ("@p", pricer)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Sales.OrderDraft' AND EventType = N'sales.order-draft.commercial-revised.v1' "
            + "AND EntityId = @id AND EntityVersionBefore = 4 AND EntityVersionAfter = 5 AND CorrelationId = @corr AND PermissionCode = @perm",
            ("@id", draft.Id.ToString("D")), ("@corr", run.Correlation), ("@perm", SalesPermissionManifestV1.OrdersPriceWrite)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = N'SENTETIK-second'"));

        // (4b) A DIFFERENT key carrying the values the draft already has is the refusal (ASSUMPTION-SALESP-5), never the replay above.
        await using (var a = run.Store(CompanyA))
        {
            var service = run.Service(a, pricer, new RecordingAuthority(SalesPermissionManifestV1.OrdersPriceWrite));
            var same = await Assert.ThrowsAsync<SalesDraftServiceException>(() => service.ReviseCommercialAsync(draft.Id,
                request with { ExpectedVersion = "5" }, run.Metadata("k-revise-again")));
            Assert.Equal((422, "validation_failed", "lines", SalesRuleCodes.RevisionNoChange), (same.Status, same.Code, Assert.Single(same.Errors).Field, Assert.Single(same.Errors).Code));
        }

        Assert.Equal(5L, await run.ScalarAsync<long>("SELECT Version FROM sales.Drafts WHERE Id = @id", ("@id", draft.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftRevisions WHERE DraftId = @id", ("@id", draft.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE IdempotencyKeyHash = @h", ("@h", SalesStore.IdempotencyKeyHash("k-revise-again"))));
    }

    [SqlServerFact]
    public async Task The_approval_migration_refuses_to_roll_back_while_any_draft_has_left_preparing()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        SalesOrderDraft draft;
        await using (var a = run.Store(CompanyA))
        {
            draft = (await a.Store.StoreDraftAsync(run.Envelope("k-create"), Hash("payload-1"), documentNo => Draft(owners, documentNo, T0))).Draft;
            await a.Store.UpdateDraftStateAsync(run.Envelope("k-submit"), Hash("submit-2"), SalesDraftCommands.SubmitV1, draft.Id,
                (d, now) => d.Submit(2, run.Actor, now), Submitted);
        }

        await using var context = run.Context(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.EndsWith("_AddDraftApproval", applied[^1], StringComparison.Ordinal);
        var error = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(applied[^2]));
        Assert.Equal(51045, error.Number);
        Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT ApprovalState FROM sales.Drafts WHERE Id = @id", ("@id", draft.Id)));
        Assert.Equal(run.Actor, await run.ScalarAsync<string>("SELECT SubmittedBy FROM sales.Drafts WHERE Id = @id", ("@id", draft.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sys.check_constraints WHERE [name] = N'CK_Drafts_Approval'"));
        // The refusal also covers the attempts: the submit attempt (a non-create attempt with the draft's id) is still
        // there, and the create-only uniqueness the pre-A1 index could not hold it under is still the narrowed one.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.DraftAttempts WHERE DraftId = @id AND CommandName <> @c", ("@id", draft.Id), ("@c", SalesDraftCommands.CreateV1)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.indexes WHERE [name] = N'UX_DraftAttempts_DraftId' AND is_unique = 1 AND has_filter = 1 AND filter_definition LIKE N'%sales.order-draft.create.v1%'"));
    }

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");
    private static readonly SalesDraftStateAudit Rejected = new("sales.order-draft.rejected.v1", "Reject", "SENTETIK reddedildi");
    private static readonly SalesDraftStateAudit Revised = new("sales.order-draft.commercial-revised.v1", "ReviseCommercial", "SENTETIK fiyat revize edildi");

    private static HttpRequestMessage Command(Guid draftId, string segment, string idempotencyKey, string json, string correlationId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{SalesEndpoints.DraftsPath}/{draftId:D}/{segment}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Headers.Add("X-Correlation-Id", correlationId);
        return request;
    }

    private static async Task<JsonElement> Result(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Hash(string seed) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A draft of the company from SYNTHETIC owner snapshots with <paramref name="lines"/> lines (one by default; version 1 + lines).</summary>
    private static SalesOrderDraft Draft(Owners owners, string documentNo, DateTimeOffset at, int lines = 1)
    {
        var header = new SalesDraftHeader(owners.Party, owners.Account, Currency, Day);
        var money = new MoneyProfileSnapshot(owners.Company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), owners.Company, documentNo, header, money, at);
        var unit = new UnitProfileSnapshot(owners.Company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(owners.Company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        for (var index = 0; index < lines; index++)
        {
            draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, "1.25", 1.25m), at);
        }

        return draft;
    }

    /// <summary>The same draft with its one line PRICED (12.50 per kg, 5 %): the values an approved revision must keep verbatim.</summary>
    private static SalesOrderDraft PricedDraft(Owners owners, string documentNo, DateTimeOffset at)
    {
        var header = new SalesDraftHeader(owners.Party, owners.Account, Currency, Day);
        var money = new MoneyProfileSnapshot(owners.Company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), owners.Company, documentNo, header, money, at);
        var unit = new UnitProfileSnapshot(owners.Company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(owners.Company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, "1.25", 1.25m,
            Commercial: new CommercialDraftFields("12.50", KgUnit, "5")), at);
        return draft;
    }

    /// <summary>An authority that answers from a fixed set of granted names and RECORDS every question (name, company).</summary>
    private sealed class RecordingAuthority(params string[] granted) : IErpAuthorityRevalidationService
    {
        public List<(string Permission, Guid Company)> Measured { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Measured.Add((permission, companyId));
            return Task.FromResult(granted.Contains(permission, StringComparer.Ordinal)
                ? new PermissionScope(true, true, new HashSet<string>(["SENTETIK-PRICER"], StringComparer.Ordinal), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
        }
    }

    private sealed class FixedUser(string userId, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([company]);
        public bool CanAccessCompany(Guid companyId) => companyId == company;
    }

    private sealed class FixedCorrelation(string correlationId) : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => correlationId;
        public string? CausationId => null;
        public string SourceChannel => "Web";
        public string? ClientIpAddress => "127.0.0.1";
        public string? UserAgent => "SENTETIK-agent";
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent)
        {
        }
    }

    /// <summary>The owner ports a commercial revision never asks; reaching one is a failure of the case, not a stub answer.</summary>
    private sealed class NoOwners : ISalesPartyAccountPortV1, ISettingsPrerequisiteProvider
    {
        public Task<SalesPartyAccountAnswerV1> GetAuthorizedAsync(Guid partyId, Guid companyAccountId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A commercial revision asks no owner.");
        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A commercial revision asks no owner.");
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A commercial revision asks no owner.");
        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A commercial revision asks no owner.");
    }

    private sealed record Owners(Guid Company, Guid Party, Guid Account)
    {
        public static Owners For(Guid company) => new(company, Guid.NewGuid(), Guid.NewGuid());
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
        public string Actor { get; } = "SENTETIK-preparer-" + Guid.NewGuid().ToString("N");
        public string Correlation { get; } = $"sales-a1-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            await using var context = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .Options, companyContext);
            await context.Database.MigrateAsync();
            return run;
        }

        public SalesDraftWriteEnvelope Envelope(string key, string? correlation = null, string? actor = null) =>
            new(actor ?? Actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], correlation ?? Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        /// <summary>The REAL draft service over this scope's real store, for `actor` in the company, under `authority` (K3 Head B).</summary>
        public SalesDraftService Service(StoreScope scope, string actor, IErpAuthorityRevalidationService authority)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            var owners = new NoOwners();
            return new SalesDraftService(companyContext, new FixedUser(actor, CompanyA), authority, new FixedCorrelation(Correlation), owners, owners, null,
                scope.Store, new SalesDraftServiceBindings(), TimeProvider.System);
        }

        public SalesCommandMetadata Metadata(string key, string? correlation = null) =>
            new(key, correlation ?? Correlation, null, T0);

        /// <summary>A plain Sales context of the company (no audit interceptor): for migration history and rollback checks.</summary>
        public SalesDbContext Context(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .Options, companyContext);
        }

        public StoreScope Store(Guid company, IInterceptor? extra = null)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var builder = new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext));
            if (extra is not null)
            {
                builder.AddInterceptors(extra);
            }

            var dbContext = new SalesDbContext(builder.Options, companyContext);
            var store = new SalesStore(dbContext, companyContext, collector, TimeProvider.System);
            return new StoreScope(dbContext, store, new SalesOrderLineReferencePort(store, companyContext));
        }

        /// <summary>A synthetic role with exactly these permissions in one company, assigned to a configured development user.</summary>
        public async Task<string> GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(
                x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"SALES-A1-{roleId:N}", "Synthetic Sales approval test role", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
            return developmentUser;
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

        public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            await command.ExecuteNonQueryAsync();
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

    private sealed class StoreScope(SalesDbContext dbContext, SalesStore store, SalesOrderLineReferencePort port) : IAsyncDisposable
    {
        public ISalesOrderDraftStore Store => store;

        public ISalesOrderLineReferencePortV1 Port => port;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    /// <summary>Holds each save until both writers have reached it, so both pass the attempt lookup and the draft read first.</summary>
    private sealed class BarrierInterceptor(Barrier barrier) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return ValueTask.FromResult(result);
        }
    }
}
