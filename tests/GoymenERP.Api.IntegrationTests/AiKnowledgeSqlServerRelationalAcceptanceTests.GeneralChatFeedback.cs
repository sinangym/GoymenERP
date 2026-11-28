using GoymenERP.Api.Ai.Answers;
using GoymenERP.Api.Ai.Gateway;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.AiKnowledge.Domain;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AiKnowledgeSqlServerRelationalAcceptanceTests
{
    // Actual receipt/feedback SQL owners; the general-chat provider output is a named synthetic input.
    [SqlServerFact]
    public async Task General_chat_answer_receipt_is_feedback_eligible_and_counted_once_with_unreviewed_and_original_key_replay()
    {
        await WithDatabaseAsync(async connection =>
        {
            var company = TestCompanyIds.CompanyA; Guid id;
            var chat = new AiGatewayAnswerV1("SENTETIK general conversation", false, [], "SENTETIK-model",
                AiModelPlacementV1.OnPremises, "7", 1);
            await using (var first = Db(connection, company))
            {
                var writer = new AiAnswerReceiptWriterV1(AnswerStore(first, company), new AnswerClock(AnswerAt));
                var recorded = await writer.RecordGeneralChatAsync(company, Approver.ToString("D"), "SENTETIK-chat-original",
                    chat, _ => Task.CompletedTask);
                id = Assert.IsType<Guid>(recorded);
            }
            await using var db = Db(connection, company);
            var receipt = await AnswerStore(db, company).GetOwnAsync(company, id, Approver.ToString("D"), _ => Task.CompletedTask);
            Assert.NotNull(receipt); Assert.Equal("Panel", receipt.Feature);
            var now = AnswerAt.AddHours(2);
            var store = FeedbackStore(db, company, now);
            var window = new AiFeedbackCountWindowV1(AnswerAt.AddDays(-1), AnswerAt.AddDays(1), now);
            var unreviewed = Assert.Single(await store.GetMonthCountsAsync(company, Approver, window, _ => Task.CompletedTask));
            Assert.Equal((0, 0, 0, 0, 1), (unreviewed.Accepted, unreviewed.Corrected, unreviewed.Rejected, unreviewed.Reverted, unreviewed.Unreviewed));
            var command = FeedbackCommand(receipt);
            Assert.Equal(AiFeedbackWriteStatusV1.Recorded, (await store.AppendAsync(command, _ => Task.CompletedTask)).Status);
            Assert.Equal(AiFeedbackWriteStatusV1.Recorded, (await store.AppendAsync(FeedbackCommand(receipt, 1, AiFeedbackKindV1.Rejected,
                AiFeedbackReasonsV1.Other, "SENTETIK-chat-reject"), _ => Task.CompletedTask)).Status);
            var replay = await store.AppendAsync(command with { CorrelationId = "SENTETIK-chat-retry" }, _ => Task.CompletedTask);
            Assert.Equal(AiFeedbackWriteStatusV1.Replayed, replay.Status);
            Assert.Equal(1L, replay.Event!.Version); Assert.Equal(command.CorrelationId, replay.OriginalCorrelationId);
            var counted = Assert.Single(await store.GetMonthCountsAsync(company, Approver, window, _ => Task.CompletedTask));
            Assert.Equal((0, 0, 1, 0, 0), (counted.Accepted, counted.Corrected, counted.Rejected, counted.Reverted, counted.Unreviewed));
            Assert.Equal(1, await db.AnswerReceipts.CountAsync()); Assert.Equal(1, await db.AnswerReceiptIdentities.CountAsync());
            Assert.Equal(2, await db.FeedbackEvents.CountAsync()); Assert.Equal(2, await db.FeedbackIdentities.CountAsync());
            var foreign = new AnswerMutableUser { ActorId = Guid.NewGuid() };
            Assert.Null(await new GoymenERP.Modules.AiKnowledge.Infrastructure.AiAnswerReceiptPersistenceV1(db, AnswerCompany(company),
                foreign, new ReceiptAudit(), new AnswerClock(now)).GetOwnAsync(company, id, foreign.ActorId.ToString("D"), _ => Task.CompletedTask));
        });
    }
}
