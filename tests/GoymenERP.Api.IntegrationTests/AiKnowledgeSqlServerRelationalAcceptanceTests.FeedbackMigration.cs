using GoymenERP.Modules.AiKnowledge.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AiKnowledgeSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Feedback_Down_allows_empty_tables_and_refuses_52313_while_feedback_or_separate_identity_exists()
    {
        await WithDatabaseAsync(async connection =>
        {
            await using var db = Db(connection, TestCompanyIds.CompanyA);
            var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            var feedback = Assert.Single(migrations, id => id.EndsWith("_AddAiAnswerFeedback", StringComparison.Ordinal));
            var index = Array.IndexOf(migrations, feedback); Assert.True(index > 0);
            var previous = migrations[index - 1]; var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(previous); await migrator.MigrateAsync();
            var answer = AnswerReceipt(); await StoreFeedbackAnswer(connection, answer);
            Assert.Equal(AiFeedbackWriteStatusV1.Recorded, (await FeedbackStore(db, answer.CompanyId).AppendAsync(FeedbackCommand(answer), _ => Task.CompletedTask)).Status);
            var failure = Sql(await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync(previous)));
            Assert.Equal(52313, failure.Number);
            Assert.Equal(1, await db.FeedbackEvents.CountAsync()); Assert.Equal(1, await db.FeedbackIdentities.CountAsync());
            Assert.Equal(1, await db.AnswerReceipts.CountAsync());
        });
    }
}
