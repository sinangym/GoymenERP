using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations;

/// <summary>
/// AT-009: the party's tax/identity number (checksum-valid digits; the check digits are the domain's) and the
/// maker-checker change requests. Every index filter is written out explicitly: the duplicate lookup is NOT unique
/// (a duplicate is a warning), and only one PENDING request may exist per party per company. Rollback is refused with
/// 51059 while any number or request exists, so no entered number is ever dropped silently.
/// </summary>
[DbContext(typeof(CounterpartyDbContext))]
[Migration("20260930150000_AddPartyTaxIdentity")]
public sealed class AddPartyTaxIdentity : Migration
{
    private const string NumberFormat =
        "LEN([{0}]) IN (10, 11) AND DATALENGTH([{0}]) = LEN([{0}]) AND [{0}] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9]%'";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "TaxIdentityNumber", schema: "counterparty", table: "Parties",
            type: "varchar(11)", unicode: false, maxLength: 11, nullable: true);
        migrationBuilder.AddCheckConstraint(
            name: "CK_Parties_TaxIdentityNumber", schema: "counterparty", table: "Parties",
            sql: "[TaxIdentityNumber] IS NULL OR (" + Format("TaxIdentityNumber") + ")");
        migrationBuilder.CreateIndex(
            name: "IX_Parties_TaxIdentityNumber", schema: "counterparty", table: "Parties",
            column: "TaxIdentityNumber",
            filter: "[TaxIdentityNumber] IS NOT NULL");

        migrationBuilder.CreateTable(
            name: "PartyTaxIdentityChangeRequests",
            schema: "counterparty",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CurrentTaxIdentityNumber = table.Column<string>(
                    type: "varchar(11)", unicode: false, maxLength: 11, nullable: false),
                ProposedTaxIdentityNumber = table.Column<string>(
                    type: "varchar(11)", unicode: false, maxLength: 11, nullable: false),
                PartyVersionAtRequest = table.Column<long>(type: "bigint", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                RequestedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                DecidedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                Version = table.Column<long>(type: "bigint", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PartyTaxIdentityChangeRequests", x => x.Id);
                table.CheckConstraint(
                    "CK_PartyTaxIdentityChangeRequests_Current", Format("CurrentTaxIdentityNumber"));
                table.CheckConstraint(
                    "CK_PartyTaxIdentityChangeRequests_Decision",
                    "([Status] = N'Pending' AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionNote] IS NULL)"
                    + " OR ([Status] <> N'Pending' AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL"
                    + " AND [DecidedBy] <> [RequestedBy])");
                table.CheckConstraint(
                    "CK_PartyTaxIdentityChangeRequests_Proposed",
                    Format("ProposedTaxIdentityNumber") + " AND [ProposedTaxIdentityNumber] <> [CurrentTaxIdentityNumber]");
                table.CheckConstraint(
                    "CK_PartyTaxIdentityChangeRequests_Status", "[Status] IN (N'Pending', N'Approved', N'Rejected')");
                table.CheckConstraint(
                    "CK_PartyTaxIdentityChangeRequests_Version", "[Version] >= 1 AND [PartyVersionAtRequest] >= 1");
                table.ForeignKey(
                    name: "FK_PartyTaxIdentityChangeRequests_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PartyTaxIdentityChangeRequests_Parties_PartyId",
                    column: x => x.PartyId,
                    principalSchema: "counterparty",
                    principalTable: "Parties",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PartyTaxIdentityChangeRequests_CompanyId_PartyId_RequestedAtUtc",
            schema: "counterparty", table: "PartyTaxIdentityChangeRequests",
            columns: ["CompanyId", "PartyId", "RequestedAtUtc"]);
        migrationBuilder.CreateIndex(
            name: "IX_PartyTaxIdentityChangeRequests_PartyId",
            schema: "counterparty", table: "PartyTaxIdentityChangeRequests",
            column: "PartyId");
        migrationBuilder.CreateIndex(
            name: "UX_PartyTaxIdentityChangeRequests_Pending",
            schema: "counterparty", table: "PartyTaxIdentityChangeRequests",
            columns: ["CompanyId", "PartyId"],
            unique: true,
            filter: "[Status] = N'Pending'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [counterparty].[PartyTaxIdentityChangeRequests])
                OR EXISTS (SELECT 1 FROM [counterparty].[Parties] WHERE [TaxIdentityNumber] IS NOT NULL)
            BEGIN
                THROW 51059, 'Rollback blocked: counterparty tax identity numbers or change requests exist. Keep the schema and use a compatible forward fix.', 1;
            END;
            """);
        migrationBuilder.DropTable(name: "PartyTaxIdentityChangeRequests", schema: "counterparty");
        migrationBuilder.DropIndex(name: "IX_Parties_TaxIdentityNumber", schema: "counterparty", table: "Parties");
        migrationBuilder.DropCheckConstraint(
            name: "CK_Parties_TaxIdentityNumber", schema: "counterparty", table: "Parties");
        migrationBuilder.DropColumn(name: "TaxIdentityNumber", schema: "counterparty", table: "Parties");
    }

    private static string Format(string column) => string.Format(
        System.Globalization.CultureInfo.InvariantCulture, NumberFormat, column);
}
