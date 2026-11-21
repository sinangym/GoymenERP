using System.Data;
using System.Data.Common;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesDraftApprovalSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Accepted_revision_state_and_enlisted_owner_work_commit_once_and_replay_skips_the_participant()
    {
        await using var run = await Run.StartAsync();
        var (draftId, markerId) = await PrepareFutureDemandAcceptanceAsync(run);
        var called = 0;
        await using (var scope = run.Store(CompanyA))
        {
            var accepted = (await scope.Store.UpdateDraftStateWithTransactionAsync(run.Envelope("future-accept"),
                Hash("future-accept"), SalesDraftCommands.RecordCustomerAcceptanceV1, draftId,
                (draft, now) => draft.RecordCustomerAcceptance(8, run.Actor, now), CustomerAccepted,
                async (draft, oldAccepted, transaction, token) =>
                {
                    Assert.Equal((0, 1, true), (oldAccepted, draft.CustomerAcceptance!.Revision, draft.IsCustomerAccepted));
                    called++;
                    await StageFutureDemandProbeAsync(transaction, markerId, token);
                }))!;
            Assert.Equal((9L, 1), (accepted.Draft.Version, accepted.Draft.CustomerAcceptance!.Revision));
            var replay = (await scope.Store.UpdateDraftStateWithTransactionAsync(run.Envelope("future-accept"),
                Hash("future-accept"), SalesDraftCommands.RecordCustomerAcceptanceV1, draftId,
                (_, _) => throw new InvalidOperationException("SENTETIK replay does not transition"), CustomerAccepted,
                (_, _, _, _) => throw new InvalidOperationException("SENTETIK replay does not release")))!;
            Assert.Equal(SalesDraftWriteOutcome.Replayed, replay.Outcome);
        }
        Assert.Equal(1, called);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts WHERE Id=@id AND UpdatedAtUtc=@at",
            ("@id", markerId), ("@at", T0.AddDays(20))));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE DraftId=@id AND CommandName=@command AND Outcome=1",
            ("@id", draftId), ("@command", SalesDraftCommands.RecordCustomerAcceptanceV1)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=@event AND EntityVersionAfter=9",
            ("@id", draftId.ToString("D")), ("@event", CustomerAccepted.EventType)));
    }

    [SqlServerFact]
    public async Task A_failure_after_the_state_save_rolls_back_enlisted_owner_work_acceptance_attempt_and_audit()
    {
        await using var run = await Run.StartAsync();
        var (draftId, markerId) = await PrepareFutureDemandAcceptanceAsync(run);
        await using (var scope = run.Store(CompanyA, new FailAfterFutureDemandSave()))
        {
            await Assert.ThrowsAsync<FutureDemandSaveFault>(() => scope.Store.UpdateDraftStateWithTransactionAsync(
                run.Envelope("future-accept-fail"), Hash("future-accept-fail"), SalesDraftCommands.RecordCustomerAcceptanceV1,
                draftId, (draft, now) => draft.RecordCustomerAcceptance(8, run.Actor, now), CustomerAccepted,
                (_, _, transaction, token) => StageFutureDemandProbeAsync(transaction, markerId, token)));
        }
        await using var fresh = run.Store(CompanyA);
        var unchanged = (await fresh.Store.FindDraftAsync(draftId))!;
        Assert.Equal((8L, 0), (unchanged.Version, unchanged.CustomerAcceptance!.Revision));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts WHERE Id=@id AND UpdatedAtUtc=@at",
            ("@id", markerId), ("@at", T0.AddDays(20))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE DraftId=@id AND CommandName=@command AND IdempotencyKeyHash=@key",
            ("@id", draftId), ("@command", SalesDraftCommands.RecordCustomerAcceptanceV1), ("@key", Hash("future-accept-fail").ToUpperInvariant())));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=@event AND EntityVersionAfter=9",
            ("@id", draftId.ToString("D")), ("@event", CustomerAccepted.EventType)));
    }

    [SqlServerFact]
    public async Task A_stale_revision_acceptance_never_calls_the_owner_participant_and_does_not_change_the_order()
    {
        await using var run = await Run.StartAsync();
        var (draftId, _) = await PrepareFutureDemandAcceptanceAsync(run);
        var called = false;
        await using var scope = run.Store(CompanyA);
        await Assert.ThrowsAsync<SalesVersionConflictException>(() => scope.Store.UpdateDraftStateWithTransactionAsync(
            run.Envelope("future-stale"), Hash("future-stale"), SalesDraftCommands.RecordCustomerAcceptanceV1, draftId,
            (draft, now) => draft.RecordCustomerAcceptance(7, run.Actor, now), CustomerAccepted,
            (_, _, _, _) => { called = true; return Task.CompletedTask; }));
        Assert.False(called);
        await using var fresh = run.Store(CompanyA);
        Assert.Equal(8L, (await fresh.Store.FindDraftAsync(draftId))!.Version);
    }

    private static async Task<(Guid DraftId, Guid MarkerId)> PrepareFutureDemandAcceptanceAsync(Run run)
    {
        await using var scope = run.Store(CompanyA);
        var owners = Owners.For(CompanyA);
        var draft = (await scope.Store.StoreDraftAsync(run.Envelope("future-create"), Hash("future-create"),
            number => Draft(owners, number, T0, customerAcceptanceRequired: true))).Draft;
        var marker = (await scope.Store.StoreDraftAsync(run.Envelope("future-marker"), Hash("future-marker"),
            number => Draft(owners, number, T0.AddMinutes(1)))).Draft;
        await scope.Store.UpdateDraftStateAsync(run.Envelope("future-submit0"), Hash("future-submit0"), SalesDraftCommands.SubmitV1,
            draft.Id, (d, now) => d.Submit(2, run.Actor, now), Submitted);
        await scope.Store.UpdateDraftStateAsync(run.Envelope("future-approve0"), Hash("future-approve0"), SalesDraftCommands.ApproveV1,
            draft.Id, (d, now) => d.Approve(3, "SENTETIK-approver", now), Approved);
        await scope.Store.UpdateDraftStateAsync(run.Envelope("future-accept0"), Hash("future-accept0"), SalesDraftCommands.RecordCustomerAcceptanceV1,
            draft.Id, (d, now) => d.RecordCustomerAcceptance(4, run.Actor, now), CustomerAccepted);
        await scope.Store.ReviseDraftQuantityAsync(run.Envelope("future-revise"), Hash("future-revise"), SalesDraftCommands.ReviseQuantityV1,
            draft.Id, (d, now) => d.ReviseQuantities(5, [new LineQuantityInput(d.Lines[0].Id, "1", 1m)], run.Actor,
                "SENTETIK talep azaltma", now), new("sales.order-draft.quantity-revised.v1", "ReviseQuantity", "SENTETIK talep azaltma"),
            (_, _) => Task.CompletedTask);
        await scope.Store.UpdateDraftStateAsync(run.Envelope("future-submit1"), Hash("future-submit1"), SalesDraftCommands.SubmitV1,
            draft.Id, (d, now) => d.Submit(6, run.Actor, now), Submitted);
        await scope.Store.UpdateDraftStateAsync(run.Envelope("future-approve1"), Hash("future-approve1"), SalesDraftCommands.ApproveV1,
            draft.Id, (d, now) => d.Approve(7, "SENTETIK-approver", now), Approved);
        return (draft.Id, marker.Id);
    }

    // A synthetic owner-write probe in an existing fixture row: the real outer transaction must commit or roll it back
    // together with Sales. Inventory ReduceDemand acceptance is a separate host-composition fact.
    private static async Task StageFutureDemandProbeAsync(IDbTransaction transaction, Guid markerId, CancellationToken token)
    {
        await using var command = ((DbConnection)transaction.Connection!).CreateCommand();
        command.Transaction = (DbTransaction)transaction;
        command.CommandText = "UPDATE sales.Drafts SET UpdatedAtUtc=@at WHERE CompanyId=@company AND Id=@id";
        command.Parameters.Add(new SqlParameter("@at", T0.AddDays(20)));
        command.Parameters.Add(new SqlParameter("@company", CompanyA));
        command.Parameters.Add(new SqlParameter("@id", markerId));
        Assert.Equal(1, await command.ExecuteNonQueryAsync(token));
    }

    private sealed class FutureDemandSaveFault() : Exception("SENTETIK state save failed before outer commit");
    private sealed class FailAfterFutureDemandSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) => throw new FutureDemandSaveFault();
    }
}
