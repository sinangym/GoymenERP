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

    [SqlServerFact]
    public async Task Customer_acceptance_is_persisted_replayed_invalidated_by_revision_and_legacy_orders_keep_their_cutover_behavior()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        SalesOrderDraft required;
        SalesOrderDraft legacy;
        Guid requiredLineId;

        await using (var a = run.Store(CompanyA))
        {
            required = (await a.Store.StoreDraftAsync(run.Envelope("k-q9-create"), Hash("q9-create"),
                documentNo => Draft(owners, documentNo, T0, customerAcceptanceRequired: true))).Draft;
            requiredLineId = required.Lines[0].Id;
            await a.Store.UpdateDraftStateAsync(run.Envelope("k-q9-submit"), Hash("q9-submit"), SalesDraftCommands.SubmitV1,
                required.Id, (draft, now) => draft.Submit(2, run.Actor, now), Submitted);
            await a.Store.UpdateDraftStateAsync(run.Envelope("k-q9-approve", actor: "SENTETIK-approver"), Hash("q9-approve"),
                SalesDraftCommands.ApproveV1, required.Id, (draft, now) => draft.Approve(3, "SENTETIK-approver", now), Approved);
            Assert.Equal(SalesOrderLineReferenceOutcomeV1.NotApproved,
                (await a.Port.GetApprovedLineAsync(required.Id, requiredLineId)).Outcome);

            var accepted = (await a.Store.UpdateDraftStateAsync(run.Envelope("k-q9-accept", actor: "SENTETIK-customer"), Hash("q9-accept"),
                SalesDraftCommands.RecordCustomerAcceptanceV1, required.Id,
                (draft, now) => draft.RecordCustomerAcceptance(4, "SENTETIK-customer", now), CustomerAccepted))!;
            Assert.Equal((SalesDraftWriteOutcome.Recorded, 5L, 0, true),
                (accepted.Outcome, accepted.Draft.Version, accepted.Draft.CustomerAcceptance!.Revision, accepted.Draft.IsCustomerAccepted));
            var replay = (await a.Store.UpdateDraftStateAsync(run.Envelope("k-q9-accept", correlation: "SENTETIK-q9-retry", actor: "SENTETIK-customer"),
                Hash("q9-accept"), SalesDraftCommands.RecordCustomerAcceptanceV1, required.Id,
                (_, _) => throw new InvalidOperationException("a replay never mutates"), CustomerAccepted))!;
            Assert.Equal((SalesDraftWriteOutcome.Replayed, run.Correlation, 5L),
                (replay.Outcome, replay.OriginalCorrelationId, replay.Draft.Version));
            Assert.Equal(SalesOrderLineReferenceOutcomeV1.Approved,
                (await a.Port.GetApprovedLineAsync(required.Id, requiredLineId)).Outcome);

            LineCommercialInput[] revisedPrice = [new(requiredLineId, new CommercialDraftFields("13.00", KgUnit, "5"))];
            await a.Store.ReviseDraftCommercialAsync(run.Envelope("k-q9-revise", actor: "SENTETIK-pricer"), Hash("q9-revise"),
                SalesDraftCommands.ReviseCommercialV1, required.Id,
                (draft, now) => draft.ReviseCommercial(5, revisedPrice, "SENTETIK-pricer", now), Revised,
                static _ => Task.CompletedTask);
            await a.Store.UpdateDraftStateAsync(run.Envelope("k-q9-submit-r1"), Hash("q9-submit-r1"), SalesDraftCommands.SubmitV1,
                required.Id, (draft, now) => draft.Submit(6, run.Actor, now), Submitted);
            await a.Store.UpdateDraftStateAsync(run.Envelope("k-q9-approve-r1", actor: "SENTETIK-approver"), Hash("q9-approve-r1"),
                SalesDraftCommands.ApproveV1, required.Id, (draft, now) => draft.Approve(7, "SENTETIK-approver", now), Approved);
            Assert.Equal(SalesOrderLineReferenceOutcomeV1.NotApproved,
                (await a.Port.GetApprovedLineAsync(required.Id, requiredLineId)).Outcome);
            var acceptedR1 = (await a.Store.UpdateDraftStateAsync(run.Envelope("k-q9-accept-r1", actor: "SENTETIK-customer"), Hash("q9-accept-r1"),
                SalesDraftCommands.RecordCustomerAcceptanceV1, required.Id,
                (draft, now) => draft.RecordCustomerAcceptance(8, "SENTETIK-customer", now), CustomerAccepted))!;
            Assert.Equal((9L, 1, true),
                (acceptedR1.Draft.Version, acceptedR1.Draft.CustomerAcceptance!.Revision, acceptedR1.Draft.IsCustomerAccepted));
            Assert.Equal(SalesOrderLineReferenceOutcomeV1.Approved,
                (await a.Port.GetApprovedLineAsync(required.Id, requiredLineId)).Outcome);

            legacy = (await a.Store.StoreDraftAsync(run.Envelope("k-q9-legacy-create"), Hash("q9-legacy-create"),
                documentNo => Draft(owners, documentNo, T0.AddMinutes(1)))).Draft;
            await a.Store.UpdateDraftStateAsync(run.Envelope("k-q9-legacy-submit"), Hash("q9-legacy-submit"), SalesDraftCommands.SubmitV1,
                legacy.Id, (draft, now) => draft.Submit(2, run.Actor, now), Submitted);
            await a.Store.UpdateDraftStateAsync(run.Envelope("k-q9-legacy-approve", actor: "SENTETIK-approver"), Hash("q9-legacy-approve"),
                SalesDraftCommands.ApproveV1, legacy.Id, (draft, now) => draft.Approve(3, "SENTETIK-approver", now), Approved);
            Assert.Equal(SalesOrderLineReferenceOutcomeV1.Approved,
                (await a.Port.GetApprovedLineAsync(legacy.Id, legacy.Lines[0].Id)).Outcome);
        }

        Assert.Equal((true, 1, "SENTETIK-customer", 9L), (
            await run.ScalarAsync<bool>("SELECT [CustomerAcceptanceRequired] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", required.Id)),
            await run.ScalarAsync<int>("SELECT [CustomerAcceptedRevision] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", required.Id)),
            await run.ScalarAsync<string>("SELECT [CustomerAcceptedBy] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", required.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", required.Id))));
        Assert.Equal((false, 0), (
            await run.ScalarAsync<bool>("SELECT [CustomerAcceptanceRequired] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", legacy.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[Drafts] WHERE [Id] = @id AND [CustomerAcceptedRevision] IS NOT NULL", ("@id", legacy.Id))));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [DraftId] = @id AND [CommandName] = @command AND [Outcome] = 1",
            ("@id", required.Id), ("@command", SalesDraftCommands.RecordCustomerAcceptanceV1)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityId] = @id AND [EventType] = N'sales.order-draft.customer-acceptance-recorded.v1'",
            ("@id", required.Id.ToString("D"))));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'SENTETIK-q9-retry'"));

        await using var context = run.Context(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var previous = Assert.Single(applied, migration => migration.EndsWith("_AddDraftRemainderChanges", StringComparison.Ordinal));
        var rollback = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(previous));
        Assert.Equal(51060, rollback.Number);
        Assert.EndsWith("_AddDraftCustomerAcceptance", Assert.Single(await context.Database.GetAppliedMigrationsAsync(),
            migration => migration.EndsWith("_AddDraftCustomerAcceptance", StringComparison.Ordinal)), StringComparison.Ordinal);
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

        Assert.Equal((3, 0, 4L, "12.50"), (await run.ScalarAsync<int>("SELECT [ApprovalState] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [Revision] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [UnitPrice] FROM [sales].[DraftLines] WHERE [DraftId] = @id", ("@id", draft.Id))));

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

        Assert.Equal((0, 4L), (await run.ScalarAsync<int>("SELECT [Revision] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRevisions] WHERE [DraftId] = @id", ("@id", draft.Id)));

        // (0) "One save" is MEASURED, not read from the store: a recorder sees every SavingChanges the store makes (which entries,
        // in which state, with which modified properties) and can refuse the save before any statement reaches SQL Server.
        // First the refused save: the command runs to its save with the WHOLE change set — the archived revision rows, the
        // header with Revision among its modified properties, the line, the attempt and the audit event, together — and that
        // one save is refused. Then SQL Server shows nothing partial: Rev.0, approved, no archived row, no attempt, no audit
        // event. The case cannot pass vacuously: the recorder pins that the save was REACHED with the full set (a command that
        // never ran records no save; a store that saved the header apart from the archive would record two, or leave Rev.1).
        var faulting = new SaveRecorder { Fault = true };
        await using (var a = run.Store(CompanyA, faulting))
        {
            var service = run.Service(a, pricer, new RecordingAuthority(SalesPermissionManifestV1.OrdersPriceWrite));
            await Assert.ThrowsAsync<SaveFault>(() => service.ReviseCommercialAsync(draft.Id, request, run.Metadata("k-revise")));
        }

        var refusedSave = Assert.Single(faulting.Saves);
        AssertRevisionChangeSet(refusedSave);
        Assert.Equal((3, 0, 4L), (await run.ScalarAsync<int>("SELECT [ApprovalState] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [Revision] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));
        // The APPROVED texts survive the refused save untouched (the fixture is priced 12.50 / kg / 5 from creation).
        Assert.Equal(("12.50", "5"), (await run.ScalarAsync<string>("SELECT [UnitPrice] FROM [sales].[DraftLines] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [DiscountPercent] FROM [sales].[DraftLines] WHERE [DraftId] = @id", ("@id", draft.Id))));
        Assert.Equal(KgUnit, await run.ScalarAsync<Guid>("SELECT [PriceUnitId] FROM [sales].[DraftLines] WHERE [DraftId] = @id", ("@id", draft.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRevisions] WHERE [DraftId] = @id", ("@id", draft.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [IdempotencyKeyHash] = @h", ("@h", SalesStore.IdempotencyKeyHash("k-revise"))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Sales.OrderDraft' AND [EventType] = N'sales.order-draft.commercial-revised.v1' AND [EntityId] = @id",
            ("@id", draft.Id.ToString("D"))));

        // (1) The pricer holds ONLY the separate price name: asked with that name in this company, answered yes, then the ONE save —
        // under the SAME key as the refused save: nothing of it was recorded, so this is a fresh write, not a replay (ADR-011 §7:
        // only a completed write or a terminal refusal is an attempt).
        var priced = new RecordingAuthority(SalesPermissionManifestV1.OrdersPriceWrite);
        var recorder = new SaveRecorder();
        await using (var a = run.Store(CompanyA, recorder))
        {
            var service = run.Service(a, pricer, priced);
            var revised = await service.ReviseCommercialAsync(draft.Id, request, run.Metadata("k-revise"));
            Assert.False(revised.IsReplay);
            AssertRevisionChangeSet(Assert.Single(recorder.Saves));
            // K3 P2 / ASSUMPTION-SALESP-6, on real SQL: this pricer holds ONLY the price WRITE name, so its own response is
            // WITHHELD — the texts are null BECAUSE of the marker, not because nothing was written; the columns two blocks
            // below hold 13.00 / kg / 5. (The pre-P2 version of this line asserted the texts here; the read name decides.)
            Assert.Equal(("Rev.1", "Preparing", "5", SalesPriceDisclosure.Withheld, null, null, null), (revised.Detail.Draft.Revision, revised.Detail.Draft.ApprovalState, revised.Detail.Draft.Version,
                revised.Detail.Lines[0].PriceDisclosure, revised.Detail.Lines[0].UnitPrice, revised.Detail.Lines[0].PriceUnitId, revised.Detail.Lines[0].DiscountPercent));
            Assert.Equal(new SalesDraftApprovalV1(null, null, null, null, null), revised.Detail.Approval);
            // Asked and answered are pinned apart from the outcome, by the PROPERTY and not by a count of calls: the authority was asked,
            // every ask was for the separate price name in THIS company, and no other name was ever asked for this write (a count
            // would pin the service's call pattern, which may legitimately gain a fence; the ordering of the fences — before the
            // replay read and again before the save — is the service's own pin in SalesDraftServiceTests).
            Assert.NotEmpty(priced.Measured);
            // K3 P2 lengthens this sequence by ONE legitimate trailing ask — the price READ, for the disclosure — so the pin keeps
            // its meaning and gains the element: every ask for this write is one of the two PRICE names (a draft name can still
            // never appear here), all in THIS company; the write name was asked; the read name is the LAST ask.
            Assert.All(priced.Measured, ask => Assert.Equal(CompanyA, ask.Company));
            Assert.All(priced.Measured, ask => Assert.Contains(ask.Permission, new[] { SalesPermissionManifestV1.OrdersPriceWrite, SalesPermissionManifestV1.OrdersPriceRead }));
            Assert.Contains((SalesPermissionManifestV1.OrdersPriceWrite, CompanyA), priced.Measured);
            Assert.Equal(SalesPermissionManifestV1.OrdersPriceRead, priced.Measured[^1].Permission);

            // The same key again through the service: the stored result, marked as the replay; the authority is asked again first.
            var replay = await service.ReviseCommercialAsync(draft.Id, request, run.Metadata("k-revise", correlation: "SENTETIK-second"));
            Assert.Equal((true, run.Correlation, "Rev.1", "5"), (replay.IsReplay, replay.OriginalCorrelationId, replay.Detail.Draft.Revision, replay.Detail.Draft.Version));
            // The replay saves NOTHING — measured: the recorder still holds exactly the one save.
            Assert.Single(recorder.Saves);

            // K3's separation, the other way round: the price WRITE name grants no read — the pricer holding only it is refused
            // the history (the read is under the draft read name), measured on real SQL; this refusal is what found the case
            // reading the history with the wrong actor.
            var unread = await Assert.ThrowsAsync<SalesDraftServiceException>(() => service.GetDraftRevisionsAsync(draft.Id));
            Assert.Equal((403, "forbidden"), (unread.Status, unread.Code));
        }

        // The history through the READ names — the draft read to reach it, the price read to see the texts (K3 P2; the
        // draft-read-only reader's WITHHELD answer is pinned in the price-read case): Rev.0 with its facts and the OLD texts.
        await using (var a = run.Store(CompanyA))
        {
            var reader = run.Service(a, "SENTETIK-reader", new RecordingAuthority(SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersPriceRead));
            var history = await reader.GetDraftRevisionsAsync(draft.Id);
            var item = Assert.Single(history.Items);
            Assert.Equal(("Rev.0", run.Actor, "SENTETIK-approver", pricer, "12.50", "5", 2), (item.Revision, item.SubmittedBy, item.ApprovedBy, item.ArchivedBy,
                item.Lines[0].UnitPrice, item.Lines[0].DiscountPercent, item.MoneyScale));
        }

        // (2)(3) The rows, read back by SQL as TEXT: the header advanced, the line carries the new texts, Rev.0 keeps the OLD ones.
        Assert.Equal((1, 1, 5L, "<null>"), (await run.ScalarAsync<int>("SELECT [ApprovalState] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [Revision] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT ISNULL([SubmittedBy], N'<null>') FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));
        Assert.Equal(("13.00", "5"), (await run.ScalarAsync<string>("SELECT [UnitPrice] FROM [sales].[DraftLines] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [DiscountPercent] FROM [sales].[DraftLines] WHERE [DraftId] = @id", ("@id", draft.Id))));
        Assert.Equal(KgUnit, await run.ScalarAsync<Guid>("SELECT [PriceUnitId] FROM [sales].[DraftLines] WHERE [DraftId] = @id", ("@id", draft.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftRevisions] WHERE [CompanyId] = @c AND [DraftId] = @id AND [Revision] = 0 AND [SubmittedBy] = @s AND [ApprovedBy] = N'SENTETIK-approver' "
            + "AND [ArchivedBy] = @p AND [MoneyScale] = 2 AND [CorrelationId] = @corr",
            ("@c", CompanyA), ("@id", draft.Id), ("@s", run.Actor), ("@p", pricer), ("@corr", run.Correlation)));
        Assert.Equal("12.50", await run.ScalarAsync<string>(
            "SELECT [UnitPrice] FROM [sales].[DraftRevisionLines] WHERE [CompanyId] = @c AND [DraftId] = @id AND [Revision] = 0 AND [LineNo] = 1 AND [LineId] = @l",
            ("@c", CompanyA), ("@id", draft.Id), ("@l", lineId)));
        Assert.Equal(("5", "1.25"), (await run.ScalarAsync<string>("SELECT [DiscountPercent] FROM [sales].[DraftRevisionLines] WHERE [DraftId] = @id AND [Revision] = 0", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [QuantityText] FROM [sales].[DraftRevisionLines] WHERE [DraftId] = @id AND [Revision] = 0", ("@id", draft.Id))));
        Assert.Equal(KgUnit, await run.ScalarAsync<Guid>("SELECT [PriceUnitId] FROM [sales].[DraftRevisionLines] WHERE [DraftId] = @id AND [Revision] = 0", ("@id", draft.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = @cmd AND [DraftId] = @id AND [Outcome] = 1 AND [ActorId] = @p",
            ("@cmd", SalesDraftCommands.ReviseCommercialV1), ("@id", draft.Id), ("@p", pricer)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Sales.OrderDraft' AND [EventType] = N'sales.order-draft.commercial-revised.v1' "
            + "AND [EntityId] = @id AND [EntityVersionBefore] = 4 AND [EntityVersionAfter] = 5 AND [CorrelationId] = @corr AND [PermissionCode] = @perm",
            ("@id", draft.Id.ToString("D")), ("@corr", run.Correlation), ("@perm", SalesPermissionManifestV1.OrdersPriceWrite)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'SENTETIK-second'"));

        // (4b) A DIFFERENT key carrying the values the draft already has is the refusal (ASSUMPTION-SALESP-5), never the replay above.
        await using (var a = run.Store(CompanyA))
        {
            var service = run.Service(a, pricer, new RecordingAuthority(SalesPermissionManifestV1.OrdersPriceWrite));
            var same = await Assert.ThrowsAsync<SalesDraftServiceException>(() => service.ReviseCommercialAsync(draft.Id,
                request with { ExpectedVersion = "5" }, run.Metadata("k-revise-again")));
            Assert.Equal((422, "validation_failed", "lines", SalesRuleCodes.RevisionNoChange), (same.Status, same.Code, Assert.Single(same.Errors).Field, Assert.Single(same.Errors).Code));
        }

        Assert.Equal(5L, await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRevisions] WHERE [DraftId] = @id", ("@id", draft.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [IdempotencyKeyHash] = @h", ("@h", SalesStore.IdempotencyKeyHash("k-revise-again"))));
    }

    /// <summary>
    /// K3 P2 (Sinan 5748258299, "Sevkiyatçıya fiyat/iskonto/tutar sızmaz" · "ilgili dar fiyat okuması"): the price READ is
    /// its own narrow name, measured on real SQL. NON-VACUOUS by construction: the prices are STORED and read from the
    /// columns FIRST (the current line and the archived Rev.0), so the reader below is withheld a value that exists.
    /// A reader holding <c>sales.drafts.read</c> only gets the WITHHELD marker with the three commercial members null on
    /// the detail and on the archived revisions — not a 403, not a bare null; the same reader holding the price read name
    /// too gets the texts as stored. Layered, stated: which persona holds which name is the persona card's (deny-first);
    /// this case measures that the draft read alone never discloses and that the read name alone is what does.
    /// </summary>
    [SqlServerFact]
    public async Task The_price_read_is_its_own_name_a_draft_reader_gets_the_withheld_marker_never_the_texts_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        SalesOrderDraft draft;
        Guid lineId;
        const string pricer = "SENTETIK-pricer";
        await using (var a = run.Store(CompanyA))
        {
            draft = (await a.Store.StoreDraftAsync(run.Envelope("k-create"), Hash("payload-1"), documentNo => PricedDraft(owners, documentNo, T0))).Draft;
            lineId = draft.Lines[0].Id;
            await a.Store.UpdateDraftStateAsync(run.Envelope("k-submit"), Hash("submit-2"), SalesDraftCommands.SubmitV1, draft.Id,
                (d, now) => d.Submit(2, run.Actor, now), Submitted);
            await a.Store.UpdateDraftStateAsync(run.Envelope("k-approve", actor: "SENTETIK-approver"), Hash("approve-3"),
                SalesDraftCommands.ApproveV1, draft.Id, (d, now) => d.Approve(3, "SENTETIK-approver", now), Approved);
            LineCommercialInput[] priced = [new(lineId, new CommercialDraftFields("13.00", KgUnit, "5"))];
            await a.Store.ReviseDraftCommercialAsync(run.Envelope("k-revise", actor: pricer), Hash("revise-4"),
                SalesDraftCommands.ReviseCommercialV1, draft.Id, (d, now) => d.ReviseCommercial(4, priced, pricer, now), Revised,
                static _ => Task.CompletedTask);
        }

        // Written: the values the reader below must NOT receive are really in the columns (versions 2 → 3 → 4 → 5; Rev.1).
        Assert.Equal(("13.00", "5", 1, 5L), (await run.ScalarAsync<string>("SELECT [UnitPrice] FROM [sales].[DraftLines] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [DiscountPercent] FROM [sales].[DraftLines] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [Revision] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));
        Assert.Equal("12.50", await run.ScalarAsync<string>(
            "SELECT [UnitPrice] FROM [sales].[DraftRevisionLines] WHERE [DraftId] = @id AND [Revision] = 0 AND [LineId] = @l", ("@id", draft.Id), ("@l", lineId)));

        var attemptsBefore = await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CompanyId] = @c", ("@c", CompanyA));

        // (1) The draft read ALONE: the marker, the nulls, and not a 403 — on the detail and on the history.
        var readOnly = new RecordingAuthority(SalesPermissionManifestV1.DraftsRead);
        await using (var a = run.Store(CompanyA))
        {
            var reader = run.Service(a, "SENTETIK-reader", readOnly);
            var detail = await reader.GetDraftDetailAsync(draft.Id);
            var line = Assert.Single(detail.Lines);
            Assert.Equal((SalesPriceDisclosure.Withheld, null, null, null), (line.PriceDisclosure, line.UnitPrice, line.PriceUnitId, line.DiscountPercent));
            Assert.Equal(("Rev.1", "1.25", "Preparing"), (detail.Draft.Revision, line.Quantity, detail.Draft.ApprovalState));
            var history = await reader.GetDraftRevisionsAsync(draft.Id);
            var archived = Assert.Single(Assert.Single(history.Items).Lines);
            Assert.Equal((SalesPriceDisclosure.Withheld, null, null, null), (archived.PriceDisclosure, archived.UnitPrice, archived.PriceUnitId, archived.DiscountPercent));
        }

        // The price read name WAS asked, for this company, and it was the authority's refusal that withheld — pinned by property.
        Assert.Contains((SalesPermissionManifestV1.OrdersPriceRead, CompanyA), readOnly.Measured);
        Assert.All(readOnly.Measured, ask => Assert.Equal(CompanyA, ask.Company));

        // (2) The draft read AND the price read: the texts as stored, current and archived.
        await using (var a = run.Store(CompanyA))
        {
            var reader = run.Service(a, "SENTETIK-reader", new RecordingAuthority(SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersPriceRead));
            var line = Assert.Single((await reader.GetDraftDetailAsync(draft.Id)).Lines);
            Assert.Equal((SalesPriceDisclosure.Visible, "13.00", KgUnit, "5"), (line.PriceDisclosure, line.UnitPrice, line.PriceUnitId, line.DiscountPercent));
            var archived = Assert.Single(Assert.Single((await reader.GetDraftRevisionsAsync(draft.Id)).Items).Lines);
            Assert.Equal((SalesPriceDisclosure.Visible, "12.50", KgUnit, "5"), (archived.PriceDisclosure, archived.UnitPrice, archived.PriceUnitId, archived.DiscountPercent));
        }

        // Nothing was written by reading: the same version, the same number of attempts as before the reads.
        Assert.Equal(5L, await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)));
        Assert.Equal(attemptsBefore, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CompanyId] = @c", ("@c", CompanyA)));
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
        var approval = Array.FindIndex(applied,
            migration => migration.EndsWith("_AddDraftApproval", StringComparison.Ordinal));
        Assert.True(approval > 0);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(applied[approval]);
        Assert.Equal(applied[..(approval + 1)], await context.Database.GetAppliedMigrationsAsync());
        var error = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(applied[approval - 1]));
        Assert.Equal(51045, error.Number);
        Assert.Equal(applied[..(approval + 1)], await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'sales') AND name IN (N'DraftRevisions', N'DraftRevisionLines')"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.check_constraints WHERE [name] = N'CK_Drafts_State' AND schema_id = SCHEMA_ID(N'sales') AND definition LIKE N'%[[]Revision]=(0)%'"));
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

    /// <summary>
    /// D2 (Sinan 5749046401) on REAL SQL, through the REAL service over the real store: the SUBMITTER holding the approve name
    /// cannot decide their own order — priced or unpriced, approve or reject — and the refusal is the API's (403, nothing
    /// recorded: no attempt, no audit event, the columns untouched); a DIFFERENT actor with the same name approves one and
    /// rejects the other. Then the CREATOR withdraws the rejected one: its own command, state 4 → 5 in the header, the
    /// withdrawal's facts in the decision columns, the rejection kept in the audit trail, the lines and the revisions
    /// untouched, the same key replayed; anyone else is refused 403 unrecorded; the approved one is the key's terminal 409.
    /// No money, no stock: the sales schema holds no such table, and every sales table but the header and the attempts has
    /// the row count it had before.
    /// </summary>
    [SqlServerFact]
    public async Task The_submitter_cannot_decide_their_own_order_a_different_approver_can_and_the_creator_withdraws_without_money_or_stock_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        SalesOrderDraft priced;
        SalesOrderDraft unpriced;
        const string approver = "SENTETIK-approver";
        const string other = "SENTETIK-other";
        await using (var a = run.Store(CompanyA))
        {
            priced = (await a.Store.StoreDraftAsync(run.Envelope("k-create-p"), Hash("payload-p"), documentNo => PricedDraft(owners, documentNo, T0))).Draft;
            unpriced = (await a.Store.StoreDraftAsync(run.Envelope("k-create-u"), Hash("payload-u"), documentNo => Draft(owners, documentNo, T0))).Draft;
            foreach (var draft in new[] { priced, unpriced })
            {
                await a.Store.UpdateDraftStateAsync(run.Envelope("k-submit-" + draft.DocumentNo), Hash("submit-2-" + draft.DocumentNo), SalesDraftCommands.SubmitV1,
                    draft.Id, (d, now) => d.Submit(2, run.Actor, now), Submitted);
            }
        }

        // Columns first: both drafts pending (version 3: create + one line = 2, submit = 3), submitted AND created by the preparer.
        foreach (var draft in new[] { priced, unpriced })
        {
            Assert.Equal((2, run.Actor, run.Actor, 3L), (
                await run.ScalarAsync<int>("SELECT [ApprovalState] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
                await run.ScalarAsync<string>("SELECT [SubmittedBy] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
                await run.ScalarAsync<string>("SELECT [CreatedBy] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
                await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));
        }

        // The sales schema's tables, by name: no money, no stock, no shipment table exists here for a withdrawal to touch.
        var tables = await run.ScalarAsync<string>(
            "SELECT STRING_AGG([name], N',') WITHIN GROUP (ORDER BY [name]) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'sales') AND [name] <> N'__EFMigrationsHistory'");
        Assert.Equal("DraftAttempts,DraftCancellations,DraftDeliveryScheduleDates,DraftDeliverySchedules,DraftDeliverySlices,DraftForceClosures,DraftLines,DraftNumbers,DraftRemainderChanges,DraftRemainderDecisions,DraftRevisionLines,DraftRevisions,Drafts", tables);
        async Task<(int Drafts, int Lines, int Slices, int Numbers, int Revisions, int RevisionLines)> QuietCountsAsync() => (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[Drafts] WHERE [CompanyId] = @c", ("@c", CompanyA)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftLines] WHERE [CompanyId] = @c", ("@c", CompanyA)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftDeliverySlices] WHERE [CompanyId] = @c", ("@c", CompanyA)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftNumbers] WHERE [CompanyId] = @c", ("@c", CompanyA)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRevisions] WHERE [CompanyId] = @c", ("@c", CompanyA)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRevisionLines] WHERE [CompanyId] = @c", ("@c", CompanyA)));
        Task<int> AttemptsAsync() => run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CompanyId] = @c", ("@c", CompanyA));
        Task<int> EventsAsync(Guid id) => run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Sales.OrderDraft' AND EntityId = @id", ("@id", id.ToString()));
        var quiet = await QuietCountsAsync();
        var attempts = await AttemptsAsync();
        var events = (await EventsAsync(priced.Id), await EventsAsync(unpriced.Id));

        // (1) The preparer, holding the approve name too (a Satış Destek that WAS granted it): the own decision is refused at the
        //     service — approve on the priced, reject on the unpriced — with nothing recorded and the columns untouched.
        var self = new RecordingAuthority(SalesPermissionManifestV1.DraftsCreate, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersApprove);
        await using (var a = run.Store(CompanyA))
        {
            var preparer = run.Service(a, run.Actor, self, ErpPersonaRoleCodes.SalesSupport, "SALES_MANAGER");
            var approveOwn = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                preparer.ApproveDraftAsync(priced.Id, new SalesDraftStateCommandRequest("3"), run.Metadata("k-self-approve")));
            var rejectOwn = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                preparer.RejectDraftAsync(unpriced.Id, new SalesDraftStateCommandRequest("3", "SENTETIK kendi reddi"), run.Metadata("k-self-reject")));
            Assert.Equal(((403, "forbidden"), (403, "forbidden")), ((approveOwn.Status, approveOwn.Code), (rejectOwn.Status, rejectOwn.Code)));
        }

        Assert.Contains((SalesPermissionManifestV1.OrdersApprove, CompanyA), self.Measured);
        Assert.All(self.Measured, ask => Assert.Equal(CompanyA, ask.Company));
        Assert.Equal((attempts, events, quiet), (await AttemptsAsync(), (await EventsAsync(priced.Id), await EventsAsync(unpriced.Id)), await QuietCountsAsync()));
        foreach (var draft in new[] { priced, unpriced })
        {
            Assert.Equal((2, 3L), (await run.ScalarAsync<int>("SELECT [ApprovalState] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
                await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));
        }

        // (2) A DIFFERENT actor with the approve name: approves the priced, rejects the unpriced; the own name is never asked.
        var manager = new RecordingAuthority(SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersApprove);
        await using (var a = run.Store(CompanyA))
        {
            var service = run.Service(a, approver, manager);
            var approved = await service.ApproveDraftAsync(priced.Id, new SalesDraftStateCommandRequest("3"), run.Metadata("k-approve"));
            var rejected = await service.RejectDraftAsync(unpriced.Id, new SalesDraftStateCommandRequest("3", "SENTETIK fiyat yok"), run.Metadata("k-reject"));
            Assert.Equal(("Approved", approver, "4"), (approved.Detail.Draft.ApprovalState, approved.Detail.Approval.DecidedBy, approved.Detail.Draft.Version));
            Assert.Equal(("Rejected", approver, "SENTETIK fiyat yok", "4"),
                (rejected.Detail.Draft.ApprovalState, rejected.Detail.Approval.DecidedBy, rejected.Detail.Approval.RejectionReason, rejected.Detail.Draft.Version));
        }

        Assert.Equal((attempts + 2, (events.Item1 + 1, events.Item2 + 1), quiet),
            (await AttemptsAsync(), (await EventsAsync(priced.Id), await EventsAsync(unpriced.Id)), await QuietCountsAsync()));
        attempts += 2;

        // (3) Withdrawal. Someone else holding the create name: 403, nothing recorded. The creator: the rejected draft goes
        //     4 → 5 with the withdrawal's facts in the decision columns (the rejection stays in the audit trail), the lines,
        //     slices and revisions untouched; the same key replays; the approved one is the key's terminal 409, recorded.
        await using (var a = run.Store(CompanyA))
        {
            var stranger = run.Service(a, other, new RecordingAuthority(SalesPermissionManifestV1.DraftsCreate, SalesPermissionManifestV1.DraftsRead));
            var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                stranger.WithdrawDraftAsync(unpriced.Id, new SalesDraftStateCommandRequest("4"), run.Metadata("k-withdraw-other")));
            Assert.Equal((403, "forbidden"), (refused.Status, refused.Code));
        }

        Assert.Equal((attempts, events.Item2 + 1), (await AttemptsAsync(), await EventsAsync(unpriced.Id)));
        SalesOrderDraftResponse withdrawn;
        await using (var a = run.Store(CompanyA))
        {
            var creator = run.Service(a, run.Actor, new RecordingAuthority(SalesPermissionManifestV1.DraftsCreate, SalesPermissionManifestV1.DraftsRead));
            withdrawn = await creator.WithdrawDraftAsync(unpriced.Id, new SalesDraftStateCommandRequest("4", "  SENTETIK vazgeçildi  "), run.Metadata("k-withdraw"));
            var replay = await creator.WithdrawDraftAsync(unpriced.Id, new SalesDraftStateCommandRequest("4", "  SENTETIK vazgeçildi  "), run.Metadata("k-withdraw"));
            Assert.Equal((false, true, "Withdrawn", "5"), (withdrawn.IsReplay, replay.IsReplay, replay.Detail.Draft.ApprovalState, replay.Detail.Draft.Version));
            var terminal = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                creator.WithdrawDraftAsync(priced.Id, new SalesDraftStateCommandRequest("4"), run.Metadata("k-withdraw-approved")));
            Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), (terminal.Status, terminal.Code));
        }

        Assert.Equal(("Withdrawn", run.Actor, run.Actor, "SENTETIK vazgeçildi", "Rev.0", true),
            (withdrawn.Detail.Draft.ApprovalState, withdrawn.Detail.Approval.SubmittedBy, withdrawn.Detail.Approval.DecidedBy,
                withdrawn.Detail.Approval.RejectionReason, withdrawn.Detail.Draft.Revision, withdrawn.Detail.IsOwnDraft));
        Assert.Equal((5, run.Actor, "SENTETIK vazgeçildi", 5L, 0), (
            await run.ScalarAsync<int>("SELECT [ApprovalState] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", unpriced.Id)),
            await run.ScalarAsync<string>("SELECT [DecidedBy] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", unpriced.Id)),
            await run.ScalarAsync<string>("SELECT [RejectionReason] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", unpriced.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", unpriced.Id)),
            await run.ScalarAsync<int>("SELECT [Revision] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", unpriced.Id))));
        // The approved one did not move (state 3, version 4); the terminal refusal is under its key (attempt, refused).
        Assert.Equal((3, 4L), (await run.ScalarAsync<int>("SELECT [ApprovalState] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", priced.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", priced.Id))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [DraftId] = @id AND [CommandName] = @c AND [Outcome] = 2", ("@id", priced.Id), ("@c", SalesDraftCommands.WithdrawV1)));
        // Trail: one withdrawal event with the creator as actor; the rejection event is still there; two attempts more
        // (the withdrawal recorded, the approved one's refusal), the replay none; every other sales table unchanged.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'sales.order-draft.withdrawn.v1' AND EntityId = @id AND ActorId = @a AND EntityVersionAfter = 5",
            ("@id", unpriced.Id.ToString()), ("@a", run.Actor)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'sales.order-draft.rejected.v1' AND EntityId = @id", ("@id", unpriced.Id.ToString())));
        Assert.Equal((attempts + 2, (events.Item1 + 1, events.Item2 + 2), quiet),
            (await AttemptsAsync(), (await EventsAsync(priced.Id), await EventsAsync(unpriced.Id)), await QuietCountsAsync()));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [DraftId] = @id AND [CommandName] = @c AND [Outcome] = 1", ("@id", unpriced.Id), ("@c", SalesDraftCommands.WithdrawV1)));

        // (4) An APPROVED HISTORY (Sinan 5750987986 point 3): while the priced draft was approved its line was answered to
        //     the consumers through the port (the read a Logistics/Manufacturing record would link from — Sales holds no
        //     link table and cannot see the link); then a commercial revision brings it back to Preparing at Rev.1. The
        //     easy path is refused (409 recorded), the archived snapshot row and its line stay, nothing else moves.
        await using (var a = run.Store(CompanyA))
        {
            var answered = (await a.Port.ListApprovedLinesAsync(10, null)).Items;
            Assert.Contains(answered, item => item.OrderId == priced.Id);
            var pricer = run.Service(a, run.Actor, new RecordingAuthority(SalesPermissionManifestV1.DraftsCreate, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersPriceWrite));
            var revised = await pricer.ReviseCommercialAsync(priced.Id,
                new SalesDraftCommercialRevisionRequest("4", [new SalesDraftLineCommercialV1(priced.Lines[0].Id, "13.00", KgUnit, "5")]), run.Metadata("k-revise"));
            Assert.Equal(("Preparing", "Rev.1", "5"), (revised.Detail.Draft.ApprovalState, revised.Detail.Draft.Revision, revised.Detail.Draft.Version));
        }

        var snapshots = (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRevisions] WHERE [DraftId] = @id", ("@id", priced.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRevisionLines] WHERE [DraftId] = @id", ("@id", priced.Id)),
            await run.ScalarAsync<string>("SELECT [UnitPrice] FROM [sales].[DraftRevisionLines] WHERE [DraftId] = @id AND [Revision] = 0", ("@id", priced.Id)));
        Assert.Equal((1, 1, "12.50"), snapshots);
        var attemptsBeforeHistory = await AttemptsAsync();
        await using (var a = run.Store(CompanyA))
        {
            var creator = run.Service(a, run.Actor, new RecordingAuthority(SalesPermissionManifestV1.DraftsCreate, SalesPermissionManifestV1.DraftsRead));
            var history = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                creator.WithdrawDraftAsync(priced.Id, new SalesDraftStateCommandRequest("5"), run.Metadata("k-withdraw-history")));
            Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), (history.Status, history.Code));
        }

        Assert.Equal((1, 1, 5L, snapshots, attemptsBeforeHistory + 1), (
            await run.ScalarAsync<int>("SELECT [ApprovalState] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", priced.Id)),
            await run.ScalarAsync<int>("SELECT [Revision] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", priced.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", priced.Id)),
            (await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRevisions] WHERE [DraftId] = @id", ("@id", priced.Id)),
                await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRevisionLines] WHERE [DraftId] = @id", ("@id", priced.Id)),
                await run.ScalarAsync<string>("SELECT [UnitPrice] FROM [sales].[DraftRevisionLines] WHERE [DraftId] = @id AND [Revision] = 0", ("@id", priced.Id))),
            await AttemptsAsync()));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'sales.order-draft.withdrawn.v1' AND EntityId = @id", ("@id", priced.Id.ToString())));

        // (5) Ownership is the PREPARER, not the submitter (Sinan 5750987986 point 2): A prepares, B submits, A as
        //     SALES_SUPPORT (holding the approve name too) is fenced on their own order; B — authorized, not the preparer —
        //     decides it as on main. This is not a universal creator ban: a non-SALES_SUPPORT creator keeps the pre-D2
        //     behavior under the existing approve name, proved separately below without a role/grant/admin exception.
        const string submitter = "SENTETIK-submitter";
        SalesOrderDraft third;
        await using (var a = run.Store(CompanyA))
        {
            third = (await a.Store.StoreDraftAsync(run.Envelope("k-create-3"), Hash("payload-3"), documentNo => Draft(owners, documentNo, T0))).Draft;
            await a.Store.UpdateDraftStateAsync(run.Envelope("k-submit-3", actor: submitter), Hash("submit-3"), SalesDraftCommands.SubmitV1, third.Id,
                (d, now) => d.Submit(2, submitter, now), Submitted);
        }

        Assert.Equal((run.Actor, submitter), (await run.ScalarAsync<string>("SELECT [CreatedBy] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", third.Id)),
            await run.ScalarAsync<string>("SELECT [SubmittedBy] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", third.Id))));
        var preparerAsks = new RecordingAuthority(SalesPermissionManifestV1.DraftsCreate, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersApprove);
        var submitterAsks = new RecordingAuthority(SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersApprove);
        await using (var a = run.Store(CompanyA))
        {
            var fenced = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                run.Service(a, run.Actor, preparerAsks, ErpPersonaRoleCodes.SalesSupport).ApproveDraftAsync(
                    third.Id,
                    new SalesDraftStateCommandRequest("3"),
                    run.Metadata("k-third-self")));
            Assert.Equal((403, "forbidden"), (fenced.Status, fenced.Code));
            var decided = await run.Service(a, submitter, submitterAsks).ApproveDraftAsync(third.Id, new SalesDraftStateCommandRequest("3"), run.Metadata("k-third-submitter"));
            Assert.Equal(("Approved", submitter, submitter, false), (decided.Detail.Draft.ApprovalState, decided.Detail.Approval.SubmittedBy, decided.Detail.Approval.DecidedBy, decided.Detail.IsOwnDraft));
        }

        Assert.All(submitterAsks.Measured, ask => Assert.Contains(ask.Permission, new[] { SalesPermissionManifestV1.OrdersApprove, SalesPermissionManifestV1.OrdersPriceRead }));
        Assert.Equal((3, submitter), (await run.ScalarAsync<int>("SELECT [ApprovalState] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", third.Id)),
            await run.ScalarAsync<string>("SELECT [DecidedBy] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", third.Id))));

        // (6) D2md1 source conformance: CreatedBy still defines `own`, but only the accepted SALES_SUPPORT persona turns
        //     that relationship into the own-decision refusal. A creator with no SALES_SUPPORT role and the already-existing
        //     approve permission may decide their own submitted draft exactly as before D2. No new permission, role code,
        //     grant or admin bypass participates in this case.
        const string nonSalesSupportCreator = "SENTETIK-non-sales-support-creator";
        SalesOrderDraft fourth;
        await using (var a = run.Store(CompanyA))
        {
            fourth = (await a.Store.StoreDraftAsync(run.Envelope("k-create-4", actor: nonSalesSupportCreator), Hash("payload-4"),
                documentNo => Draft(owners, documentNo, T0))).Draft;
            await a.Store.UpdateDraftStateAsync(run.Envelope("k-submit-4", actor: submitter), Hash("submit-4"), SalesDraftCommands.SubmitV1,
                fourth.Id, (d, now) => d.Submit(2, submitter, now), Submitted);
        }

        Assert.Equal((nonSalesSupportCreator, submitter), (
            await run.ScalarAsync<string>("SELECT [CreatedBy] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", fourth.Id)),
            await run.ScalarAsync<string>("SELECT [SubmittedBy] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", fourth.Id))));
        var creatorAsks = new RecordingAuthority(SalesPermissionManifestV1.OrdersApprove);
        await using (var a = run.Store(CompanyA))
        {
            var decidedByCreator = await run.Service(a, nonSalesSupportCreator, creatorAsks).ApproveDraftAsync(
                fourth.Id,
                new SalesDraftStateCommandRequest("3"),
                run.Metadata("k-fourth-creator-approves"));
            Assert.Equal(("Approved", nonSalesSupportCreator, true),
                (decidedByCreator.Detail.Draft.ApprovalState, decidedByCreator.Detail.Approval.DecidedBy, decidedByCreator.Detail.IsOwnDraft));
        }

        Assert.All(creatorAsks.Measured, ask => Assert.Contains(ask.Permission,
            new[] { SalesPermissionManifestV1.OrdersApprove, SalesPermissionManifestV1.OrdersPriceRead }));
        Assert.Equal((3, nonSalesSupportCreator), (
            await run.ScalarAsync<int>("SELECT [ApprovalState] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", fourth.Id)),
            await run.ScalarAsync<string>("SELECT [DecidedBy] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", fourth.Id))));
    }

    /// <summary>
    /// The D2 constraint upgrade preserves a legacy non-create refusal whose draft identity was not
    /// retained, including its replay. New mutation refusals retain the draft identity under the
    /// existing company-plus-draft foreign key, and downgrade refuses rather than discard that link.
    /// </summary>
    [SqlServerFact]
    public async Task The_linked_refusal_migration_preserves_legacy_replay_and_new_refusals_use_the_company_scoped_draft()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        SalesOrderDraft draft;
        await using (var a = run.Store(CompanyA))
        {
            draft = (await a.Store.StoreDraftAsync(run.Envelope("k-create-legacy"), Hash("create-legacy"),
                documentNo => Draft(owners, documentNo, T0))).Draft;
        }

        string[] applied;
        await using (var context = run.Context(CompanyA))
        {
            applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            // Later empty Sales migrations roll back freely; target the withdrawal migration by identity, never by a brittle tail offset.
            var withdrawal = Assert.Single(applied, migration => migration.EndsWith("_AddDraftWithdrawal", StringComparison.Ordinal));
            await context.GetService<IMigrator>().MigrateAsync(withdrawal);
        }

        var refusal = new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.StateConflict,
            [new("approvalState", SalesRuleCodes.StateConflict)]);
        await run.ExecuteAsync("""
            INSERT INTO [sales].[DraftAttempts]
                ([CompanyId], [CommandName], [IdempotencyKeyHash], [RequestHash], [ActorId], [Outcome], [DraftId], [RefusalJson], [CorrelationId], [RecordedAtUtc])
            VALUES
                (@company, @command, @keyHash, @requestHash, @actor, 2, NULL, @refusalJson, @correlation, @recordedAtUtc)
            """,
            ("@company", CompanyA),
            ("@command", SalesDraftCommands.WithdrawV1),
            ("@keyHash", SalesStore.IdempotencyKeyHash("k-legacy-refusal")),
            ("@requestHash", Hash("legacy-refusal")),
            ("@actor", run.Actor),
            ("@refusalJson", JsonSerializer.Serialize(refusal, new JsonSerializerOptions(JsonSerializerDefaults.Web))),
            ("@correlation", run.Correlation),
            ("@recordedAtUtc", T0));

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = @c AND [DraftId] IS NULL AND [Outcome] = 2",
            ("@c", SalesDraftCommands.WithdrawV1)));

        await using (var context = run.Context(CompanyA))
        {
            await context.GetService<IMigrator>().MigrateAsync();
        }

        await using (var a = run.Store(CompanyA))
        {
            var replay = await a.Store.FindReplayAsync(run.Envelope("k-legacy-refusal", correlation: "SENTETIK-retry"),
                Hash("legacy-refusal"), default, SalesDraftCommands.WithdrawV1);
            Assert.Equal((SalesDraftProblemCodesV1.StateConflict, run.Correlation),
                (replay!.Refusal!.Code, replay.OriginalCorrelationId));

            await a.Store.RecordRefusalAsync(run.Envelope("k-linked-refusal"), Hash("linked-refusal"), refusal,
                default, SalesDraftCommands.ApproveV1, draft.Id);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CompanyId] = @company AND [DraftId] = @draft AND [CommandName] = @command AND [Outcome] = 2",
            ("@company", CompanyA), ("@draft", draft.Id), ("@command", SalesDraftCommands.ApproveV1)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.foreign_keys WHERE [name] = N'FK_DraftAttempts_Drafts_CompanyId_DraftId' AND [parent_object_id] = OBJECT_ID(N'[sales].[DraftAttempts]')"));

        await using (var context = run.Context(CompanyA))
        {
            // Every later empty migration rolls back first; the linked refusal then refuses (51046).
            var withdrawal = Assert.Single(applied, migration => migration.EndsWith("_AddDraftWithdrawal", StringComparison.Ordinal));
            var linked = Array.FindIndex(applied, migration => migration.EndsWith("_LinkMutationRefusalsToDrafts", StringComparison.Ordinal));
            Assert.True(linked >= 0);
            var error = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(withdrawal));
            Assert.Equal(51046, error.Number);
            Assert.Equal(applied[..(linked + 1)], await context.Database.GetAppliedMigrationsAsync());
        }
    }

    /// <summary>D2's Down is a forward fix: while any withdrawn draft exists the rollback is refused with 51050 and the five-arm constraint stands.</summary>
    [SqlServerFact]
    public async Task The_withdrawal_migration_refuses_to_roll_back_while_any_draft_is_withdrawn()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        SalesOrderDraft draft;
        await using (var a = run.Store(CompanyA))
        {
            draft = (await a.Store.StoreDraftAsync(run.Envelope("k-create"), Hash("payload-1"), documentNo => Draft(owners, documentNo, T0))).Draft;
            await a.Store.UpdateDraftStateAsync(run.Envelope("k-withdraw"), Hash("withdraw-2"), SalesDraftCommands.WithdrawV1, draft.Id,
                (d, now) => d.Withdraw(2, run.Actor, null, now), Withdrawn);
        }

        // Withdrawn from Preparing: the decision columns carry the withdrawal, the submission columns stay empty (arm 5).
        Assert.Equal((5, run.Actor, (object)DBNull.Value), (
            await run.ScalarAsync<int>("SELECT [ApprovalState] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [DecidedBy] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<object>("SELECT [SubmittedBy] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));

        await using var context = run.Context(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        // Every later empty migration rolls back; D2's guard then refuses (51050).
        var withdrawal = Array.FindIndex(applied, migration => migration.EndsWith("_AddDraftWithdrawal", StringComparison.Ordinal));
        Assert.True(withdrawal > 0);
        var error = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(applied[withdrawal - 1]));
        Assert.Equal(51050, error.Number);
        Assert.Equal(applied[..(withdrawal + 1)], await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.check_constraints WHERE [name] = N'CK_Drafts_Approval' AND schema_id = SCHEMA_ID(N'sales') AND definition LIKE N'%[[]ApprovalState]=(5)%'"));
        Assert.Equal(5, await run.ScalarAsync<int>("SELECT [ApprovalState] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)));
    }

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Withdrawn = new("sales.order-draft.withdrawn.v1", "Withdraw", "SENTETIK geri çekildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");
    private static readonly SalesDraftStateAudit Rejected = new("sales.order-draft.rejected.v1", "Reject", "SENTETIK reddedildi");
    private static readonly SalesDraftStateAudit Revised = new("sales.order-draft.commercial-revised.v1", "ReviseCommercial", "SENTETIK fiyat revize edildi");
    private static readonly SalesDraftStateAudit CustomerAccepted = new("sales.order-draft.customer-acceptance-recorded.v1", "RecordCustomerAcceptance", "SENTETIK müşteri kabulü");

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

    /// <summary>
    /// A draft of the company from SYNTHETIC owner snapshots with <paramref name="lines"/> lines (one by default; version 1 + lines).
    /// Version ladder for a case: 1 + lines, then +1 per RECORDED transition or revision; a REFUSED command (409/422/403)
    /// records at most an attempt and advances nothing — expect the same version after it as before it.
    /// </summary>
    private static SalesOrderDraft Draft(Owners owners, string documentNo, DateTimeOffset at, int lines = 1,
        bool customerAcceptanceRequired = false)
    {
        var header = new SalesDraftHeader(owners.Party, owners.Account, Currency, Day);
        var money = new MoneyProfileSnapshot(owners.Company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), owners.Company, documentNo, header, money, at,
            customerAcceptanceRequired);
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
    /// <summary>
    /// AT-018 / AT-021 on real SQL: a line priced from a list keeps its reference and its visible adjustments (discount amount,
    /// surcharge percent) in <c>sales.DraftLines.PricingJson</c>, written in the commercial revision's ONE save and restored
    /// exactly; the audit event carries them with the calculator's net; the rollback of <c>AddLinePricing</c> refuses with
    /// 51064 while any pricing exists (forward fix). Written in the AT-018 slice; not run there (no SQL slot).
    /// </summary>
    [SqlServerFact]
    public async Task A_line_priced_from_a_list_keeps_its_reference_and_adjustments_in_one_save_and_the_rollback_refuses_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var reference = new PriceReferenceSnapshot(Guid.NewGuid(), "SENTETIK liste", 2, Day, null, "TRY", KgUnit, "12.5", "10", "ExcludingVat",
            "10", "20");
        var fields = new CommercialDraftFields("12.50", KgUnit, null, reference, "0.50", "1.5", null);
        SalesOrderDraft draft;
        await using (var a = run.Store(CompanyA))
        {
            draft = (await a.Store.StoreDraftAsync(run.Envelope("k-pl-create"), Hash("payload-pl"), documentNo => PricedDraft(owners, documentNo, T0))).Draft;
            var lineId = draft.Lines[0].Id;
            var revised = await a.Store.ReviseDraftCommercialAsync(run.Envelope("k-pl-price"), Hash("price-pl"), SalesDraftCommands.ReviseCommercialV1,
                draft.Id, (d, now) => d.ReviseCommercial(d.Version, [new LineCommercialInput(lineId, fields)], run.Actor, now), Revised,
                _ => Task.CompletedTask);
            Assert.NotNull(revised);
        }

        await using (var b = run.Store(CompanyA))
        {
            var reopened = (await b.Store.FindDraftAsync(draft.Id))!;
            var commercial = Assert.IsType<CommercialDraftFields>(reopened.Lines[0].Commercial);
            Assert.Equal(fields, commercial);
            Assert.Equal(("10", "20"), (commercial.Reference!.LowerQuantity,
                commercial.Reference.UpperQuantity));
            Assert.Equal(("Reference", "12.18"), (commercial.PriceSource, SalesLinePriceCalculator.NetUnitPriceText(commercial)));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftLines] WHERE [DraftId] = @id AND ISJSON([PricingJson]) = 1",
            ("@id", draft.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Sales.OrderDraft' "
            + "AND [EventType] = N'sales.order-draft.commercial-revised.v1' AND [EntityId] = @id AND [AfterJson] LIKE N'%netUnitPrice%12.18%'",
            ("@id", draft.Id.ToString("D"))));

        await using var context = run.Context(CompanyA);
        var refusal = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync("20260928090000_AddRevisionReasons"));
        Assert.Equal(51064, refusal.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftLines] WHERE [DraftId] = @id AND [PricingJson] IS NOT NULL",
            ("@id", draft.Id)));
    }

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

    private sealed class FixedActiveRoleCodeReader(params string[] roles) : IActiveRoleCodeReader
    {
        private readonly IReadOnlySet<string> roleCodes = new HashSet<string>(roles, StringComparer.Ordinal);

        public Task<IReadOnlySet<string>> GetActiveRoleCodesAsync(Guid companyId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(companyId == CompanyA
                ? roleCodes
                : (IReadOnlySet<string>)new HashSet<string>(StringComparer.Ordinal));
        }
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
        public SalesDraftService Service(
            StoreScope scope,
            string actor,
            IErpAuthorityRevalidationService authority,
            params string[] activeRoleCodes)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            var owners = new NoOwners();
            return new SalesDraftService(companyContext, new FixedUser(actor, CompanyA), authority,
                new FixedActiveRoleCodeReader(activeRoleCodes), new FixedCorrelation(Correlation), owners, owners, null,
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

    /// <summary>
    /// The change set of a commercial revision's ONE save, asserted TOGETHER (a count alone would accept a save holding only
    /// some of it): the archived revision header and line (Added), the draft header (Modified — exactly the approval facts,
    /// Revision, Version and the timestamp), the draft line (Modified — exactly the commercial column this request changes), the attempt
    /// and the audit event (Added); nothing else.
    /// </summary>
    private static void AssertRevisionChangeSet(IReadOnlyList<SaveRecorder.Entry> save)
    {
        Assert.Equal(
            ["AuditEvent:Added", "DraftAttemptRow:Added", "DraftLineRow:Modified", "DraftRevisionLineRow:Added", "DraftRevisionRow:Added", "DraftRow:Modified"],
            save.Select(entry => $"{entry.Entity}:{entry.State}").Order(StringComparer.Ordinal));
        // The header's modified set, EXACTLY (both directions, as the line below): Approved → Preparing clears the four approval
        // facts, the revision counter and the version advance, the timestamp moves; RejectionReason stays null (not modified)
        // and RowVersion is store-generated. An Assert.Superset was here first; its direction is easy to misread, and an exact
        // set says what a superset cannot — that Revision IS among the modified properties, and nothing else is.
        var header = Assert.Single(save, entry => entry.Entity == nameof(DraftRow));
        Assert.Equal(
            [
                nameof(DraftRow.ApprovalState), nameof(DraftRow.DecidedAtUtc), nameof(DraftRow.DecidedBy), nameof(DraftRow.Revision),
                nameof(DraftRow.SubmittedAtUtc), nameof(DraftRow.SubmittedBy), nameof(DraftRow.UpdatedAtUtc), nameof(DraftRow.Version),
            ],
            header.Modified);
        // The line's modified set is EXACTLY the commercial column whose value CHANGED: this request moves the price
        // (12.50 → 13.00) and re-sends the same unit (kg) and the same discount (5), so EF marks only UnitPrice — the store
        // assigns all three, EF records what differs. What this pins: the changed column is inside the one save, and nothing
        // outside the commercial columns is. The guard's ADMITTED set (all three) is the unit persistence test's pin, not this
        // case's — the first version of this line asserted the admitted set here and was red on real SQL for that reason.
        var line = Assert.Single(save, entry => entry.Entity == nameof(DraftLineRow));
        Assert.Equal([nameof(DraftLineRow.UnitPrice)], line.Modified);
    }

    /// <summary>
    /// Records every SavingChanges the store makes — which entries, in which state, with which modified properties — and, when
    /// <see cref="Fault"/> is set, refuses the save before any statement reaches SQL Server. Registered AFTER the audit
    /// interceptor, so the audit rows it adds are in the recorded set.
    /// </summary>
    private sealed class SaveRecorder : SaveChangesInterceptor
    {
        public sealed record Entry(string Entity, EntityState State, string[] Modified);

        public List<IReadOnlyList<Entry>> Saves { get; } = [];

        public bool Fault { get; init; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Saves.Add(eventData.Context!.ChangeTracker.Entries()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Select(entry => new Entry(entry.Metadata.ClrType.Name, entry.State,
                    entry.Properties.Where(property => property.IsModified).Select(property => property.Metadata.Name).Order(StringComparer.Ordinal).ToArray()))
                .ToArray());
            return Fault ? throw new SaveFault() : ValueTask.FromResult(result);
        }
    }

    private sealed class SaveFault() : Exception("SENTETIK: the one save was refused before any statement reached SQL Server");

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
