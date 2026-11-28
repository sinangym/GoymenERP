using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiScheduledTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduledTaskCommands",
                schema: "ai",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    KeyHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Command = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PayloadHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledTaskCommands", x => new { x.CompanyId, x.ActorId, x.KeyHash });
                    table.CheckConstraint("CK_AiScheduledTaskCommands_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [TaskId] <> '00000000-0000-0000-0000-000000000000' AND LEN([ActorId]) > 0 AND LEN([Command]) > 0 AND LEN([CorrelationId]) > 0 AND ISJSON([ResponseJson]) = 1 AND DATALENGTH([KeyHash]) = 64 AND [KeyHash] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND DATALENGTH([PayloadHash]) = 64 AND [PayloadHash] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'");
                    table.ForeignKey(
                        name: "FK_ScheduledTaskCommands_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledTaskEvents",
                schema: "ai",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledTaskEvents", x => new { x.CompanyId, x.EventId });
                    table.CheckConstraint("CK_AiScheduledTaskEvents_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [EventId] <> '00000000-0000-0000-0000-000000000000' AND [TaskId] <> '00000000-0000-0000-0000-000000000000' AND LEN([ActorId]) > 0 AND LEN([Kind]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_ScheduledTaskEvents_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledTasks",
                schema: "ai",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Effect = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ScheduleVersion = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledTasks", x => new { x.CompanyId, x.TaskId });
                    table.CheckConstraint("CK_AiScheduledTasks_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [TaskId] <> '00000000-0000-0000-0000-000000000000' AND LEN([OwnerActorId]) > 0 AND [Purpose] = 'work.morning-summary.v1' AND [Effect] = 1 AND [State] BETWEEN 1 AND 4 AND [Version] > 0 AND LEN([ScheduleVersion]) > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]");
                    table.ForeignKey(
                        name: "FK_ScheduledTasks_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledTaskRuns",
                schema: "ai",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SlotUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReceiptJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledTaskRuns", x => new { x.CompanyId, x.TaskId, x.SlotUtc });
                    table.CheckConstraint("CK_AiScheduledTaskRuns_Receipt", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [TaskId] <> '00000000-0000-0000-0000-000000000000' AND ISJSON([ReceiptJson]) = 1");
                    table.ForeignKey(
                        name: "FK_ScheduledTaskRuns_ScheduledTasks_CompanyId_TaskId",
                        columns: x => new { x.CompanyId, x.TaskId },
                        principalSchema: "ai",
                        principalTable: "ScheduledTasks",
                        principalColumns: new[] { "CompanyId", "TaskId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTaskEvents_CompanyId_TaskId_RecordedAtUtc_EventId",
                schema: "ai",
                table: "ScheduledTaskEvents",
                columns: new[] { "CompanyId", "TaskId", "RecordedAtUtc", "EventId" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTasks_CompanyId_OwnerActorId_State_TaskId",
                schema: "ai",
                table: "ScheduledTasks",
                columns: new[] { "CompanyId", "OwnerActorId", "State", "TaskId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [ai].[ScheduledTasks] WITH (UPDLOCK, HOLDLOCK))
                    OR EXISTS (SELECT 1 FROM [ai].[ScheduledTaskCommands] WITH (UPDLOCK, HOLDLOCK))
                    OR EXISTS (SELECT 1 FROM [ai].[ScheduledTaskRuns] WITH (UPDLOCK, HOLDLOCK))
                    OR EXISTS (SELECT 1 FROM [ai].[ScheduledTaskEvents] WITH (UPDLOCK, HOLDLOCK))
                BEGIN
                    THROW 52354, 'Scheduled task state or retained command, run or audit evidence exists; AddAiScheduledTasks is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "ScheduledTaskCommands",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "ScheduledTaskEvents",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "ScheduledTaskRuns",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "ScheduledTasks",
                schema: "ai");
        }
    }
}
