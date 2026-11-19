using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Audit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProtectAuditHistoryRollback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Reapplying the boundary restores appends after an interrupted empty-store rollback.
            // EF Core 10 commits per migration: a transaction-only lock cannot bridge that gap.
            migrationBuilder.Sql(
                """
                EXEC(N'
                    CREATE OR ALTER TRIGGER [audit].[TR_AuditEvents_RejectUpdateDelete]
                    ON [audit].[AuditEvents]
                    INSTEAD OF UPDATE, DELETE
                    AS
                    BEGIN
                        SET NOCOUNT ON;
                        THROW 51000, ''Audit events are append-only and cannot be changed or deleted.'', 1;
                    END;
                ');
                DROP TRIGGER IF EXISTS [audit].[TR_AuditEvents_BlockAppendDuringRollback];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [audit].[AuditEvents] WITH (TABLOCKX, HOLDLOCK))
                BEGIN
                    THROW 51900, 'Audit history exists; ProtectAuditHistoryRollback requires a forward fix.', 1;
                END;
                EXEC(N'
                    CREATE OR ALTER TRIGGER [audit].[TR_AuditEvents_BlockAppendDuringRollback]
                    ON [audit].[AuditEvents]
                    INSTEAD OF INSERT
                    AS
                    BEGIN
                        SET NOCOUNT ON;
                        THROW 51901, ''Audit rollback is active; reapply ProtectAuditHistoryRollback before appending.'', 1;
                    END;
                ');
                -- Historical nullable-to-required Down emits an UPDATE even on this empty table.
                -- Keep rejecting every real row mutation while permitting that zero-row statement.
                -- The append barrier above survives commits between the historical migrations.
                EXEC(N'
                    CREATE OR ALTER TRIGGER [audit].[TR_AuditEvents_RejectUpdateDelete]
                    ON [audit].[AuditEvents]
                    INSTEAD OF UPDATE, DELETE
                    AS
                    BEGIN
                        SET NOCOUNT ON;
                        IF EXISTS (SELECT 1 FROM inserted) OR EXISTS (SELECT 1 FROM deleted)
                            THROW 51000, ''Audit events are append-only and cannot be changed or deleted.'', 1;
                    END;
                ');
                """);
        }
    }
}
