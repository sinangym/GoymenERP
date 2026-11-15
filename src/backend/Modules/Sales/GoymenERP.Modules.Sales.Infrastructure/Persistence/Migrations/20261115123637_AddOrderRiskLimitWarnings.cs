using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderRiskLimitWarnings : Migration
    {
        // F-018 / plan S1 (FNS-D27(3)): a credit limit overrun at submit or approve is a warning only, appended to the order's own
        // record in the same save as the transition. One new append-only table (sales.DraftRiskWarnings, one row per transition
        // version, no amount); no existing row changes. Down guard THROW 51418.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DraftRiskWarnings",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftVersion = table.Column<long>(type: "bigint", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Command = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CheckBusinessDay = table.Column<DateOnly>(type: "date", nullable: false),
                    RiskSourceContract = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftRiskWarnings", x => new { x.CompanyId, x.DraftId, x.DraftVersion });
                    table.CheckConstraint("CK_DraftRiskWarnings_Facts", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [DraftVersion] >= 2 AND [Revision] >= 0 AND [Command] IN (N'sales.order-draft.submit.v1', N'sales.order-draft.approve.v1') AND (([Outcome] = 1 AND [Category] = 1) OR ([Outcome] = 2 AND [Category] IN (2, 3))) AND LEN([RecordedBy]) > 0 AND LEN([RiskSourceContract]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_DraftRiskWarnings_Drafts_CompanyId_DraftId",
                        columns: x => new { x.CompanyId, x.DraftId },
                        principalSchema: "sales",
                        principalTable: "Drafts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a recorded limit warning is part of the order's audit trail and the previous model cannot hold it. The
            // guard refuses (51418) before anything is dropped; an empty table rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[DraftRiskWarnings])
                BEGIN
                    THROW 51418, 'Recorded order credit limit warnings exist; AddOrderRiskLimitWarnings is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "DraftRiskWarnings",
                schema: "sales");
        }
    }
}
