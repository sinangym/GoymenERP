using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodClosureCarryAndReopening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PeriodClosures",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClosedPeriodVersion = table.Column<long>(type: "bigint", nullable: false),
                    TargetPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CarryVoucherId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    LineCount = table.Column<int>(type: "int", nullable: false),
                    ClosedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReopeningId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReopenedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReopenedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeriodClosures", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_PeriodClosures_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CarryVoucherId] <> '00000000-0000-0000-0000-000000000000' AND [PeriodId] <> [TargetPeriodId] AND [ClosedBy] <> '00000000-0000-0000-0000-000000000000' AND [ClosedPeriodVersion] > 1 AND LEN([SourceHash]) = 64 AND [LineCount] >= 0");
                    table.CheckConstraint("CK_PeriodClosures_Reopening", "([Version] = 1 AND [ReopeningId] IS NULL AND [ReopenedBy] IS NULL AND [ReopenedAtUtc] IS NULL) OR ([Version] = 2 AND [ReopeningId] IS NOT NULL AND [ReopeningId] <> '00000000-0000-0000-0000-000000000000' AND [ReopenedBy] IS NOT NULL AND [ReopenedBy] <> '00000000-0000-0000-0000-000000000000' AND [ReopenedAtUtc] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_PeriodClosures_Periods_CompanyId_PeriodId",
                        columns: x => new { x.CompanyId, x.PeriodId },
                        principalSchema: "accounting",
                        principalTable: "Periods",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PeriodClosures_Periods_CompanyId_TargetPeriodId",
                        columns: x => new { x.CompanyId, x.TargetPeriodId },
                        principalSchema: "accounting",
                        principalTable: "Periods",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PeriodReopenings",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClosureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClosedPeriodVersion = table.Column<long>(type: "bigint", nullable: false),
                    ProposedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProposedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeriodReopenings", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_PeriodReopenings_Approval", "([Version] = 1 AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL) OR ([Version] = 2 AND [ApprovedBy] IS NOT NULL AND [ApprovedBy] <> '00000000-0000-0000-0000-000000000000' AND [ApprovedBy] <> [ProposedBy] AND [ApprovedAtUtc] IS NOT NULL AND [ApprovedAtUtc] >= [ProposedAtUtc])");
                    table.CheckConstraint("CK_PeriodReopenings_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [ProposedBy] <> '00000000-0000-0000-0000-000000000000' AND [ClosedPeriodVersion] > 1 AND LEN([Reason]) > 0");
                    table.ForeignKey(
                        name: "FK_PeriodReopenings_PeriodClosures_CompanyId_ClosureId",
                        columns: x => new { x.CompanyId, x.ClosureId },
                        principalSchema: "accounting",
                        principalTable: "PeriodClosures",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PeriodReopenings_Periods_CompanyId_PeriodId",
                        columns: x => new { x.CompanyId, x.PeriodId },
                        principalSchema: "accounting",
                        principalTable: "Periods",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PeriodClosures_CompanyId_CarryVoucherId",
                schema: "accounting",
                table: "PeriodClosures",
                columns: new[] { "CompanyId", "CarryVoucherId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PeriodClosures_CompanyId_PeriodId_ClosedPeriodVersion",
                schema: "accounting",
                table: "PeriodClosures",
                columns: new[] { "CompanyId", "PeriodId", "ClosedPeriodVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PeriodClosures_CompanyId_TargetPeriodId",
                schema: "accounting",
                table: "PeriodClosures",
                columns: new[] { "CompanyId", "TargetPeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_PeriodReopenings_CompanyId_ClosureId",
                schema: "accounting",
                table: "PeriodReopenings",
                columns: new[] { "CompanyId", "ClosureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PeriodReopenings_CompanyId_PeriodId",
                schema: "accounting",
                table: "PeriodReopenings",
                columns: new[] { "CompanyId", "PeriodId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [accounting].[PeriodClosures])
                   OR EXISTS (SELECT 1 FROM [accounting].[PeriodReopenings])
                BEGIN
                    THROW 51920, 'Period closure and reopening history exists; AddPeriodClosureCarryAndReopening is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "PeriodReopenings",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "PeriodClosures",
                schema: "accounting");
        }
    }
}
