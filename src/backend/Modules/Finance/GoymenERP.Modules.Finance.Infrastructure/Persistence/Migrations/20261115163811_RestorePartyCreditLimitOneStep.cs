using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RestorePartyCreditLimitOneStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FNS-D56 / FNS-D57(3): a credit limit change is again one step by a holder of the approve authority
            // (FNS-D27), beside the proposal path (FNS-D27a); the second-person rule of FNS-D45 is not applied. This
            // puts back the decision check AddPartyCreditLimits created, which admits every row the second-person
            // check of RequirePartyCreditLimitSecondPerson admits (that migration stays in the chain unchanged), so
            // no stored row loses meaning and no guard is needed.
            migrationBuilder.DropCheckConstraint(
                name: "CK_PartyCreditLimits_Decision",
                schema: "finance",
                table: "PartyCreditLimits");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PartyCreditLimits_Decision",
                schema: "finance",
                table: "PartyCreditLimits",
                sql: "([Status] = 1 AND [IsDirect] = 0 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL AND [AccountRevision] IS NULL AND [SupersededAtUtc] IS NULL) OR ([Status] IN (2, 4) AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [AccountRevision] IS NOT NULL AND [RejectionReason] IS NULL AND (([Status] = 2 AND [SupersededAtUtc] IS NULL) OR ([Status] = 4 AND [SupersededAtUtc] IS NOT NULL)) AND (([IsDirect] = 1 AND [DecidedBy] COLLATE Latin1_General_100_BIN2 = [ProposedBy] COLLATE Latin1_General_100_BIN2) OR ([IsDirect] = 0 AND [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [ProposedBy] COLLATE Latin1_General_100_BIN2))) OR ([Status] = 3 AND [IsDirect] = 0 AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [ProposedBy] COLLATE Latin1_General_100_BIN2 AND [RejectionReason] IS NOT NULL AND LEN([RejectionReason]) > 0 AND [AccountRevision] IS NULL AND [SupersededAtUtc] IS NULL) OR ([Status] = 4 AND [IsDirect] = 0 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL AND [AccountRevision] IS NULL AND [SupersededAtUtc] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back narrows the check to the second-person rule again, which no direct record (decided by its
            // own author) satisfies. A stored direct record is real limit history the rollback must neither rewrite
            // nor delete, so it refuses instead (forward fix); an empty or proposal-only store rolls back.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[PartyCreditLimits] WHERE [IsDirect] = 1)
                BEGIN
                    THROW 51563, 'Direct party credit limit records exist; RestorePartyCreditLimitOneStep is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_PartyCreditLimits_Decision",
                schema: "finance",
                table: "PartyCreditLimits");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PartyCreditLimits_Decision",
                schema: "finance",
                table: "PartyCreditLimits",
                sql: "([Status] = 1 AND [IsDirect] = 0 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL AND [AccountRevision] IS NULL AND [SupersededAtUtc] IS NULL) OR ([Status] IN (2, 4) AND [IsDirect] = 0 AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [AccountRevision] IS NOT NULL AND [RejectionReason] IS NULL AND (([Status] = 2 AND [SupersededAtUtc] IS NULL) OR ([Status] = 4 AND [SupersededAtUtc] IS NOT NULL)) AND [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [ProposedBy] COLLATE Latin1_General_100_BIN2) OR ([Status] = 3 AND [IsDirect] = 0 AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [ProposedBy] COLLATE Latin1_General_100_BIN2 AND [RejectionReason] IS NOT NULL AND LEN([RejectionReason]) > 0 AND [AccountRevision] IS NULL AND [SupersededAtUtc] IS NULL) OR ([Status] = 4 AND [IsDirect] = 0 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL AND [AccountRevision] IS NULL AND [SupersededAtUtc] IS NOT NULL)");
        }
    }
}
