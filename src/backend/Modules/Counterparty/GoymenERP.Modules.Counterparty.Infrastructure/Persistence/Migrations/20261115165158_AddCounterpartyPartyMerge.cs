using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCounterpartyPartyMerge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MergedIntoPartyId",
                schema: "counterparty",
                table: "Parties",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PartyMerges",
                schema: "counterparty",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovalCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SurvivorPartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SurvivorPartyVersion = table.Column<long>(type: "bigint", nullable: false),
                    MergedPartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MergedPartyVersion = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyMerges", x => x.Id);
                    table.CheckConstraint("CK_PartyMerges_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [ApprovalCompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SurvivorPartyId] <> [MergedPartyId]");
                    table.CheckConstraint("CK_PartyMerges_Text", "LEN([Reason]) > 0 AND LEN([ApprovedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_PartyMerges_Version", "[SurvivorPartyVersion] >= 1 AND [MergedPartyVersion] >= 1");
                    table.ForeignKey(
                        name: "FK_PartyMerges_Companies_ApprovalCompanyId",
                        column: x => x.ApprovalCompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartyMerges_Parties_MergedPartyId",
                        column: x => x.MergedPartyId,
                        principalSchema: "counterparty",
                        principalTable: "Parties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartyMerges_Parties_SurvivorPartyId",
                        column: x => x.SurvivorPartyId,
                        principalSchema: "counterparty",
                        principalTable: "Parties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Parties_MergedIntoPartyId",
                schema: "counterparty",
                table: "Parties",
                column: "MergedIntoPartyId",
                filter: "[MergedIntoPartyId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Parties_MergedInto",
                schema: "counterparty",
                table: "Parties",
                sql: "[MergedIntoPartyId] IS NULL OR ([MergedIntoPartyId] <> [Id] AND [IsActive] = 0)");

            migrationBuilder.CreateIndex(
                name: "IX_PartyMerges_ApprovalCompanyId",
                schema: "counterparty",
                table: "PartyMerges",
                column: "ApprovalCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyMerges_SurvivorPartyId",
                schema: "counterparty",
                table: "PartyMerges",
                column: "SurvivorPartyId");

            migrationBuilder.CreateIndex(
                name: "UX_PartyMerges_MergedParty",
                schema: "counterparty",
                table: "PartyMerges",
                column: "MergedPartyId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Parties_Parties_MergedIntoPartyId",
                schema: "counterparty",
                table: "Parties",
                column: "MergedIntoPartyId",
                principalSchema: "counterparty",
                principalTable: "Parties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix (EK-18 #77, no unmerge): an approved merge is a recorded fact; dropping the record or the pointer
            // while one exists would silently undo it.
            migrationBuilder.Sql("""IF EXISTS (SELECT 1 FROM [counterparty].[PartyMerges]) OR EXISTS (SELECT 1 FROM [counterparty].[Parties] WHERE [MergedIntoPartyId] IS NOT NULL) BEGIN THROW 51453, 'Approved party merges exist; AddCounterpartyPartyMerge is not rolled back (forward fix).', 1; END;""");

            migrationBuilder.DropForeignKey(
                name: "FK_Parties_Parties_MergedIntoPartyId",
                schema: "counterparty",
                table: "Parties");

            migrationBuilder.DropTable(
                name: "PartyMerges",
                schema: "counterparty");

            migrationBuilder.DropIndex(
                name: "IX_Parties_MergedIntoPartyId",
                schema: "counterparty",
                table: "Parties");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Parties_MergedInto",
                schema: "counterparty",
                table: "Parties");

            migrationBuilder.DropColumn(
                name: "MergedIntoPartyId",
                schema: "counterparty",
                table: "Parties");
        }
    }
}
