using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLogisticsShipmentPlanExcess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LoadingInstructionLines_Shares",
                schema: "logistics",
                table: "LoadingInstructionLines");

            migrationBuilder.AddColumn<decimal>(
                name: "ApprovedPlanExcess",
                schema: "logistics",
                table: "LoadingInstructionLines",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_LoadingShares_CompanyId_InstructionId_ShareId",
                schema: "logistics",
                table: "LoadingShares",
                columns: new[] { "CompanyId", "InstructionId", "ShareId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_LoadingInstructionLines_CompanyId_InstructionId_LineId",
                schema: "logistics",
                table: "LoadingInstructionLines",
                columns: new[] { "CompanyId", "InstructionId", "LineId" });

            migrationBuilder.CreateTable(
                name: "PlanExcessExceptions",
                schema: "logistics",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExceptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstructionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ProposedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProposedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanExcessExceptions", x => new { x.CompanyId, x.ExceptionId });
                    table.UniqueConstraint("AK_PlanExcessExceptions_CompanyId_ExceptionId_InstructionId_LineId", x => new { x.CompanyId, x.ExceptionId, x.InstructionId, x.LineId });
                    table.CheckConstraint("CK_PlanExcessExceptions_Quantity", "[Quantity] > 0");
                    table.CheckConstraint("CK_PlanExcessExceptions_State", "([Status]=1 AND [Version]=1 AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL) OR ([Status]=2 AND [Version]=2 AND [ApprovedBy] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_PlanExcessExceptions_LoadingInstructionLines_CompanyId_InstructionId_LineId",
                        columns: x => new { x.CompanyId, x.InstructionId, x.LineId },
                        principalSchema: "logistics",
                        principalTable: "LoadingInstructionLines",
                        principalColumns: new[] { "CompanyId", "InstructionId", "LineId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlanExcessReservations",
                schema: "logistics",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AllocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExceptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstructionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    OriginalAllocationVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OriginalLabelVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanExcessReservations", x => new { x.CompanyId, x.AllocationId });
                    table.CheckConstraint("CK_PlanExcessReservations_Quantity", "[Quantity]>0");
                    table.ForeignKey(
                        name: "FK_PlanExcessReservations_PlanExcessExceptions_CompanyId_ExceptionId_InstructionId_LineId",
                        columns: x => new { x.CompanyId, x.ExceptionId, x.InstructionId, x.LineId },
                        principalSchema: "logistics",
                        principalTable: "PlanExcessExceptions",
                        principalColumns: new[] { "CompanyId", "ExceptionId", "InstructionId", "LineId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlanExcessUsages",
                schema: "logistics",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExceptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShareId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstructionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanExcessUsages", x => new { x.CompanyId, x.ExceptionId, x.ShareId });
                    table.CheckConstraint("CK_PlanExcessUsages_Shape", "[Quantity]>0 AND [Kind] IN (1,2)");
                    table.ForeignKey(
                        name: "FK_PlanExcessUsages_LoadingShares_CompanyId_InstructionId_ShareId",
                        columns: x => new { x.CompanyId, x.InstructionId, x.ShareId },
                        principalSchema: "logistics",
                        principalTable: "LoadingShares",
                        principalColumns: new[] { "CompanyId", "InstructionId", "ShareId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlanExcessUsages_PlanExcessExceptions_CompanyId_ExceptionId_InstructionId_LineId",
                        columns: x => new { x.CompanyId, x.ExceptionId, x.InstructionId, x.LineId },
                        principalSchema: "logistics",
                        principalTable: "PlanExcessExceptions",
                        principalColumns: new[] { "CompanyId", "ExceptionId", "InstructionId", "LineId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_LoadingInstructionLines_Shares",
                schema: "logistics",
                table: "LoadingInstructionLines",
                sql: "[Instructed] > 0 AND [Loaded] >= 0 AND [Shipped] >= 0 AND [Delivered] >= 0 AND [ApprovedPlanExcess]>=0 AND [Loaded] <= [Instructed]+[ApprovedPlanExcess] AND [Shipped] <= [Loaded] AND [Delivered] <= [Shipped]");

            migrationBuilder.CreateIndex(
                name: "IX_PlanExcessExceptions_CompanyId_InstructionId_LineId_ExceptionId",
                schema: "logistics",
                table: "PlanExcessExceptions",
                columns: new[] { "CompanyId", "InstructionId", "LineId", "ExceptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlanExcessReservations_CompanyId_ExceptionId_InstructionId_LineId",
                schema: "logistics",
                table: "PlanExcessReservations",
                columns: new[] { "CompanyId", "ExceptionId", "InstructionId", "LineId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlanExcessUsages_CompanyId_ExceptionId_InstructionId_LineId",
                schema: "logistics",
                table: "PlanExcessUsages",
                columns: new[] { "CompanyId", "ExceptionId", "InstructionId", "LineId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlanExcessUsages_CompanyId_InstructionId_ShareId",
                schema: "logistics",
                table: "PlanExcessUsages",
                columns: new[] { "CompanyId", "InstructionId", "ShareId" });
            migrationBuilder.Sql("""
                CREATE TRIGGER [logistics].[TR_PlanExcessExceptions_ForwardOnly]
                ON [logistics].[PlanExcessExceptions] AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1 FROM deleted d LEFT JOIN inserted i
                            ON i.[CompanyId]=d.[CompanyId] AND i.[ExceptionId]=d.[ExceptionId]
                        WHERE i.[ExceptionId] IS NULL OR d.[Status]<>1 OR d.[Version]<>1
                            OR d.[ApprovedBy] IS NOT NULL OR d.[ApprovedAtUtc] IS NOT NULL
                            OR i.[Status]<>2 OR i.[Version]<>2 OR i.[ApprovedBy] IS NULL OR LEN(i.[ApprovedBy])=0 OR i.[ApprovedAtUtc] IS NULL
                            OR i.[InstructionId]<>d.[InstructionId] OR i.[LineId]<>d.[LineId] OR i.[Quantity]<>d.[Quantity]
                            OR CONVERT(varbinary(max),i.[Reason])<>CONVERT(varbinary(max),d.[Reason])
                            OR CONVERT(varbinary(max),i.[ProposedBy])<>CONVERT(varbinary(max),d.[ProposedBy])
                            OR i.[ProposedAtUtc]<>d.[ProposedAtUtc])
                        THROW 51818, 'Plan excess evidence permits only its original pending-to-approved transition.', 1;
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER [logistics].[TR_PlanExcessUsages_Immutable]
                ON [logistics].[PlanExcessUsages] AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51819, 'Actual plan excess usage is immutable.', 1;
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER [logistics].[TR_PlanExcessReservations_Immutable]
                ON [logistics].[PlanExcessReservations] AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51820, 'Actual plan excess reservation evidence is immutable.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [logistics].[PlanExcessExceptions])
                    OR EXISTS (SELECT 1 FROM [logistics].[PlanExcessUsages])
                    OR EXISTS (SELECT 1 FROM [logistics].[PlanExcessReservations])
                    OR EXISTS (SELECT 1 FROM [logistics].[LoadingInstructionLines] WHERE [ApprovedPlanExcess]<>0)
                BEGIN
                    THROW 51817, 'Plan excess approval, actual usage or reservation evidence exists; AddLogisticsShipmentPlanExcess is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "PlanExcessReservations",
                schema: "logistics");

            migrationBuilder.DropTable(
                name: "PlanExcessUsages",
                schema: "logistics");

            migrationBuilder.DropTable(
                name: "PlanExcessExceptions",
                schema: "logistics");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_LoadingShares_CompanyId_InstructionId_ShareId",
                schema: "logistics",
                table: "LoadingShares");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_LoadingInstructionLines_CompanyId_InstructionId_LineId",
                schema: "logistics",
                table: "LoadingInstructionLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LoadingInstructionLines_Shares",
                schema: "logistics",
                table: "LoadingInstructionLines");

            migrationBuilder.DropColumn(
                name: "ApprovedPlanExcess",
                schema: "logistics",
                table: "LoadingInstructionLines");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LoadingInstructionLines_Shares",
                schema: "logistics",
                table: "LoadingInstructionLines",
                sql: "[Instructed] > 0 AND [Loaded] >= 0 AND [Shipped] >= 0 AND [Delivered] >= 0 AND [Loaded] <= [Instructed] AND [Shipped] <= [Loaded] AND [Delivered] <= [Shipped]");
        }
    }
}
