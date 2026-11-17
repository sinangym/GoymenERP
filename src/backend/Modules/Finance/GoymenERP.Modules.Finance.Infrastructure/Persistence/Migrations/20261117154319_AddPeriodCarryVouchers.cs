using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodCarryVouchers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PeriodCarryVouchers",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClosureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourcePeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClosedPeriodVersion = table.Column<long>(type: "bigint", nullable: false),
                    SourceEndsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    TargetPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetStartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    LinesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeriodCarryVouchers", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_PeriodCarryVouchers_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [ClosureId] <> '00000000-0000-0000-0000-000000000000' AND [SourcePeriodId] <> [TargetPeriodId] AND [RecordedBy] <> '00000000-0000-0000-0000-000000000000' AND [ClosedPeriodVersion] > 1 AND [TargetStartsOn] > [SourceEndsOn]");
                    table.CheckConstraint("CK_PeriodCarryVouchers_Source", "ISJSON([SourceJson]) = 1 AND ISJSON([LinesJson]) = 1 AND LEN([SourceHash]) = 64");
                    table.ForeignKey(
                        name: "FK_PeriodCarryVouchers_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PeriodCarryReversals",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClosureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoucherId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    SourceHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeriodCarryReversals", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_PeriodCarryReversals_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [RecordedBy] <> '00000000-0000-0000-0000-000000000000' AND LEN([Reason]) > 0 AND LEN([SourceHash]) = 64");
                    table.ForeignKey(
                        name: "FK_PeriodCarryReversals_PeriodCarryVouchers_CompanyId_VoucherId",
                        columns: x => new { x.CompanyId, x.VoucherId },
                        principalSchema: "finance",
                        principalTable: "PeriodCarryVouchers",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PeriodCarryReversals_CompanyId_ClosureId",
                schema: "finance",
                table: "PeriodCarryReversals",
                columns: new[] { "CompanyId", "ClosureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PeriodCarryReversals_CompanyId_VoucherId",
                schema: "finance",
                table: "PeriodCarryReversals",
                columns: new[] { "CompanyId", "VoucherId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PeriodCarryVouchers_CompanyId_ClosureId",
                schema: "finance",
                table: "PeriodCarryVouchers",
                columns: new[] { "CompanyId", "ClosureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PeriodCarryVouchers_CompanyId_SourcePeriodId_ClosedPeriodVersion",
                schema: "finance",
                table: "PeriodCarryVouchers",
                columns: new[] { "CompanyId", "SourcePeriodId", "ClosedPeriodVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PeriodCarryVouchers_CompanyId_TargetStartsOn",
                schema: "finance",
                table: "PeriodCarryVouchers",
                columns: new[] { "CompanyId", "TargetStartsOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[PeriodCarryVouchers])
                   OR EXISTS (SELECT 1 FROM [finance].[PeriodCarryReversals])
                BEGIN
                    THROW 51921, 'Period carry source or reversal proof exists; AddPeriodCarryVouchers is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "PeriodCarryReversals",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "PeriodCarryVouchers",
                schema: "finance");
        }
    }
}
