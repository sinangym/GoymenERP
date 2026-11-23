using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Idempotency.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyRetentionPurge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RetentionPurgeRecords",
                schema: "idempotency",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CutoffUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RetentionPeriod = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    PolicyRef = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DeletedCount = table.Column<int>(type: "int", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetentionPurgeRecords", x => x.Id);
                    table.CheckConstraint("CK_RetentionPurgeRecords_CompanyId_NotEmpty", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_RetentionPurgeRecords_DeletedCount_Positive", "[DeletedCount] > 0");
                    table.CheckConstraint("CK_RetentionPurgeRecords_Identifiers_NotEmpty", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [RunId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_RetentionPurgeRecords_PolicyRef_NotBlank", "LEN(LTRIM(RTRIM([PolicyRef]))) > 0");
                    table.CheckConstraint("CK_RetentionPurgeRecords_RetentionPeriod_Format", "[RetentionPeriod] LIKE 'P%' AND LEN([RetentionPeriod]) > 1 AND [RetentionPeriod] NOT LIKE '%[^0-9PYMD]%'");
                    table.CheckConstraint("CK_RetentionPurgeRecords_Utc_Instants", "DATEPART(TZOFFSET, [CutoffUtc]) = 0 AND DATEPART(TZOFFSET, [RecordedAtUtc]) = 0 AND [CutoffUtc] < [RecordedAtUtc]");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RetentionPurgeRecords_CompanyId_RecordedAtUtc_Id",
                schema: "idempotency",
                table: "RetentionPurgeRecords",
                columns: new[] { "CompanyId", "RecordedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_RetentionPurgeRecords_RunId",
                schema: "idempotency",
                table: "RetentionPurgeRecords",
                column: "RunId");

            // The purge evidence is append-only, like the command execution records it reports on.
            migrationBuilder.Sql(
                """
                CREATE TRIGGER [idempotency].[TR_RetentionPurgeRecords_RejectMutation]
                ON [idempotency].[RetentionPurgeRecords]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51561, 'Idempotency retention purge records are immutable.', 1;
                END;
                """);

            // Command execution records stay immutable (51010). The only delete that passes is the
            // retention purge: it opens a session-scoped cutoff window and may delete only rows that
            // completed strictly before that cutoff (51562 otherwise).
            migrationBuilder.Sql(
                """
                CREATE OR ALTER TRIGGER [idempotency].[TR_CommandExecutions_RejectMutation]
                ON [idempotency].[CommandExecutions]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    DECLARE @cutoff datetimeoffset(7) =
                        TRY_CONVERT(datetimeoffset(7), SESSION_CONTEXT(N'goymen.idempotency.retention_cutoff_utc'));
                    IF @cutoff IS NULL OR EXISTS (SELECT 1 FROM inserted)
                    BEGIN
                        THROW 51010, 'Command execution records are immutable.', 1;
                    END;
                    IF EXISTS (SELECT 1 FROM deleted WHERE [CompletedAtUtc] >= @cutoff)
                    BEGIN
                        THROW 51562, 'Command execution records are deleted only by the retention purge for rows older than its cutoff.', 1;
                    END;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [idempotency].[RetentionPurgeRecords])
                BEGIN
                    THROW 51564, 'Idempotency retention purge evidence exists; AddIdempotencyRetentionPurge is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.Sql(
                """
                CREATE OR ALTER TRIGGER [idempotency].[TR_CommandExecutions_RejectMutation]
                ON [idempotency].[CommandExecutions]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51010, 'Command execution records are immutable.', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "RetentionPurgeRecords",
                schema: "idempotency");
        }
    }
}
