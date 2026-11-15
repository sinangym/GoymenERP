using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RequirePartyCreditLimitSecondPerson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FNS-D45: every credit limit change is proposed by one person and approved by another; the one-step
            // direct record (FNS-D27) is closed. A stored direct record is real limit history this migration must
            // neither rewrite nor delete, so it refuses instead of picking what such a record becomes (that is a
            // data decision, not this migration's); an empty or proposal-only store moves on.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[PartyCreditLimits] WHERE [IsDirect] = 1)
                BEGIN
                    THROW 51362, 'Direct party credit limit records exist; RequirePartyCreditLimitSecondPerson does not rewrite them.', 1;
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back only widens the check again (every row the stricter check admits, the wider one admits
            // too); no row loses meaning, so no guard is needed.
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
    }
}
