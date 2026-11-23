using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-018 / plan S1 (FNS-D27(3)) against a REAL SQL Server: a credit limit warning rides on the submit / approve save that produced
/// it — the append-only <c>sales.DraftRiskWarnings</c> row (no amount) and its own audit event on the order, next to the
/// transition's header, attempt and audit event. A replay records nothing twice, a transition without a warning writes no row, a
/// warning on another command is refused before anything is written, another company reads nothing, and the migration's Down
/// passes while no warning exists and refuses (51418) once one is recorded. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class SalesDraftApprovalSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task F018_a_limit_warning_is_one_save_with_its_transition_and_the_migrations_Down_passes_empty_and_refuses_while_a_warning_exists()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var guard = Assert.IsType<SqlOperation>(new AddOrderRiskLimitWarnings().DownOperations[0]).Sql;
        var notSet = new SalesOrderRiskAssessment(SalesOrderRiskWarningOutcome.Unavailable, SalesOrderRiskWarningCategory.CreditLimitNotSet,
            Day, SalesOrderRiskWarnings.PartyRiskContractV1);
        var over = new SalesOrderRiskAssessment(SalesOrderRiskWarningOutcome.Over, SalesOrderRiskWarningCategory.OverLimit, Day,
            SalesOrderRiskWarnings.PartyRiskContractV1);
        SalesOrderDraft draft;
        SalesOrderDraft quiet;
        await using (var a = run.Store(CompanyA))
        {
            draft = (await a.Store.StoreDraftAsync(run.Envelope("k-rw-create"), Hash("rw-payload-1"), documentNo => Draft(owners, documentNo, T0))).Draft;
            quiet = (await a.Store.StoreDraftAsync(run.Envelope("k-rw-create-2"), Hash("rw-payload-2"), documentNo => Draft(owners, documentNo, T0.AddMinutes(1)))).Draft;
        }

        // Drafts exist but no warning: the forward-fix guard lets the rollback through.
        await run.ExecuteAsync(guard);

        // Submit with a warning: the transition and the warning in ONE save; the same key answers again without a second row.
        await using (var a = run.Store(CompanyA))
        {
            var submitted = (await a.Store.UpdateDraftStateAsync(run.Envelope("k-rw-submit"), Hash("rw-submit-2"), SalesDraftCommands.SubmitV1,
                draft.Id, (d, now) => d.Submit(2, run.Actor, now), Submitted, riskWarning: notSet))!;
            Assert.Equal((SalesDraftWriteOutcome.Recorded, 3L), (submitted.Outcome, submitted.Draft.Version));
            var replay = (await a.Store.UpdateDraftStateAsync(run.Envelope("k-rw-submit", correlation: "SENTETIK-rw-retry"), Hash("rw-submit-2"),
                SalesDraftCommands.SubmitV1, draft.Id, (d, now) => d.Submit(2, run.Actor, now), Submitted, riskWarning: notSet))!;
            Assert.Equal(SalesDraftWriteOutcome.Replayed, replay.Outcome);
        }

        Assert.Equal((1, 3L, 0, SalesDraftCommands.SubmitV1, 2, 2), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRiskWarnings] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [DraftVersion] FROM [sales].[DraftRiskWarnings] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [Revision] FROM [sales].[DraftRiskWarnings] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [Command] FROM [sales].[DraftRiskWarnings] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [Outcome] FROM [sales].[DraftRiskWarnings] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [Category] FROM [sales].[DraftRiskWarnings] WHERE [DraftId] = @id", ("@id", draft.Id))));
        Assert.Equal((run.Actor, run.Correlation, SalesOrderRiskWarnings.PartyRiskContractV1), (
            await run.ScalarAsync<string>("SELECT [RecordedBy] FROM [sales].[DraftRiskWarnings] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [CorrelationId] FROM [sales].[DraftRiskWarnings] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [RiskSourceContract] FROM [sales].[DraftRiskWarnings] WHERE [DraftId] = @id", ("@id", draft.Id))));
        Assert.Equal(Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), await run.ScalarAsync<string>(
            "SELECT CONVERT(char(10), [CheckBusinessDay], 23) FROM [sales].[DraftRiskWarnings] WHERE [DraftId] = @id", ("@id", draft.Id)));
        // The order's audit trail: the transition's event and the warning's event under the same correlation and version; the
        // warning's after-image carries no amount.
        Assert.Equal((1, 1), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'sales.order-draft.submitted.v1' "
                + "AND [EntityId] = @id AND [EntityVersionAfter] = 3", ("@id", draft.Id.ToString("D"))),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = @type AND [EntityId] = @id "
                + "AND [EntityVersionAfter] = 3 AND [CorrelationId] = @correlation",
                ("@type", SalesOrderRiskWarnings.RecordedEventType), ("@id", draft.Id.ToString("D")), ("@correlation", run.Correlation))));
        var afterJson = await run.ScalarAsync<string>("SELECT [AfterJson] FROM [audit].[AuditEvents] WHERE [EventType] = @type AND [EntityId] = @id",
            ("@type", SalesOrderRiskWarnings.RecordedEventType), ("@id", draft.Id.ToString("D")));
        Assert.Contains("\"category\":\"CreditLimitNotSet\"", afterJson, StringComparison.Ordinal);
        Assert.DoesNotContain("amount", afterJson, StringComparison.OrdinalIgnoreCase);

        // A transition without a warning writes no row; a warning on a command that cannot carry one is refused before any write.
        await using (var a = run.Store(CompanyA))
        {
            await a.Store.UpdateDraftStateAsync(run.Envelope("k-rw-submit-quiet"), Hash("rw-submit-quiet"), SalesDraftCommands.SubmitV1,
                quiet.Id, (d, now) => d.Submit(2, run.Actor, now), Submitted);
            await Assert.ThrowsAsync<ArgumentException>(() => a.Store.UpdateDraftStateAsync(run.Envelope("k-rw-reject"), Hash("rw-reject-3"),
                SalesDraftCommands.RejectV1, quiet.Id, (d, now) => d.Reject(3, "SENTETIK-approver", "SENTETIK ret", now), Rejected, riskWarning: over));
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRiskWarnings] WHERE [DraftId] = @id", ("@id", quiet.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [IdempotencyKeyHash] = @h",
            ("@h", SalesStore.IdempotencyKeyHash("k-rw-reject"))));

        // Approve with an overrun: still approved (a warning never blocks), the second row is the approval's version.
        await using (var a = run.Store(CompanyA))
        {
            var approved = (await a.Store.UpdateDraftStateAsync(run.Envelope("k-rw-approve", actor: "SENTETIK-approver"), Hash("rw-approve-3"),
                SalesDraftCommands.ApproveV1, draft.Id, (d, now) => d.Approve(3, "SENTETIK-approver", now), Approved, riskWarning: over))!;
            Assert.Equal((InternalApprovalState.Approved, 4L), (approved.Draft.ApprovalState, approved.Draft.Version));
            var listed = await a.Store.ListDraftRiskWarningsAsync(draft.Id);
            Assert.Equal(
                new (long, string, SalesOrderRiskWarningOutcome, SalesOrderRiskWarningCategory, string)[]
                {
                    (4L, SalesDraftCommands.ApproveV1, SalesOrderRiskWarningOutcome.Over, SalesOrderRiskWarningCategory.OverLimit, "SENTETIK-approver"),
                    (3L, SalesDraftCommands.SubmitV1, SalesOrderRiskWarningOutcome.Unavailable, SalesOrderRiskWarningCategory.CreditLimitNotSet, run.Actor),
                },
                listed!.Select(warning => (warning.DraftVersion, warning.Command, warning.Outcome, warning.Category, warning.RecordedBy)));
            Assert.All(listed!, warning => Assert.Equal((Day, SalesOrderRiskWarnings.PartyRiskContractV1), (warning.CheckBusinessDay, warning.RiskSourceContract)));
            Assert.Empty((await a.Store.ListDraftRiskWarningsAsync(quiet.Id))!);
            Assert.Null(await a.Store.ListDraftRiskWarningsAsync(Guid.NewGuid()));
        }

        // Another company sees neither the order nor its warnings.
        await using (var b = run.Store(CompanyB))
        {
            Assert.Null(await b.Store.ListDraftRiskWarningsAsync(draft.Id));
        }

        // A recorded warning: the forward-fix guard refuses and the row stays.
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51418, refused.Number);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRiskWarnings] WHERE [DraftId] = @id", ("@id", draft.Id)));
    }
}
