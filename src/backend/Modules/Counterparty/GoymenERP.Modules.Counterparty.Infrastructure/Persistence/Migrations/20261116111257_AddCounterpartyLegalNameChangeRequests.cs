using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCounterpartyLegalNameChangeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PartyLegalNameChangeRequests",
                schema: "counterparty",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrentLegalName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ProposedLegalName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    PartyVersionAtRequest = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyLegalNameChangeRequests", x => x.Id);
                    table.CheckConstraint("CK_PartyLegalNameChangeRequests_Decision", "([Status] = N'Pending' AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionNote] IS NULL) OR ([Status] <> N'Pending' AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [RequestedBy] COLLATE Latin1_General_100_BIN2)");
                    table.CheckConstraint("CK_PartyLegalNameChangeRequests_Names", "LEN([CurrentLegalName]) > 0 AND LEN([ProposedLegalName]) > 0 AND [ProposedLegalName] COLLATE Latin1_General_100_BIN2 <> [CurrentLegalName] COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_PartyLegalNameChangeRequests_Status", "[Status] IN (N'Pending', N'Approved', N'Rejected')");
                    table.CheckConstraint("CK_PartyLegalNameChangeRequests_Text", "LEN([Reason]) > 0 AND LEN([RequestedBy]) > 0 AND [Version] >= 1 AND [PartyVersionAtRequest] >= 1");
                    table.ForeignKey(
                        name: "FK_PartyLegalNameChangeRequests_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartyLegalNameChangeRequests_Parties_PartyId",
                        column: x => x.PartyId,
                        principalSchema: "counterparty",
                        principalTable: "Parties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PartyLegalNameChangeRequests_CompanyId_PartyId_RequestedAtUtc",
                schema: "counterparty",
                table: "PartyLegalNameChangeRequests",
                columns: new[] { "CompanyId", "PartyId", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PartyLegalNameChangeRequests_PartyId",
                schema: "counterparty",
                table: "PartyLegalNameChangeRequests",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "UX_PartyLegalNameChangeRequests_Pending",
                schema: "counterparty",
                table: "PartyLegalNameChangeRequests",
                columns: new[] { "CompanyId", "PartyId" },
                unique: true,
                filter: "[Status] = N'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix (ALS-D111/D113): a proposed or decided legal-name change is the maker-checker record of who asked
            // and who approved; dropping it while one exists would silently erase that history.
            migrationBuilder.Sql("""IF EXISTS (SELECT 1 FROM [counterparty].[PartyLegalNameChangeRequests]) BEGIN THROW 51847, 'Legal-name change requests exist; AddCounterpartyLegalNameChangeRequests is not rolled back (forward fix).', 1; END;""");

            migrationBuilder.DropTable(
                name: "PartyLegalNameChangeRequests",
                schema: "counterparty");
        }
    }
}
