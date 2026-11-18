using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RepairPartyIdentityRevisionTaxNumberFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PartyIdentityRevisions_Value",
                schema: "counterparty",
                table: "PartyIdentityRevisions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PartyIdentityRevisions_Value",
                schema: "counterparty",
                table: "PartyIdentityRevisions",
                sql: "LEN([Value]) > 0 AND ([Attribute] <> 1 OR (LEN([Value]) IN (10, 11) AND DATALENGTH([Value]) = 2 * LEN([Value]) AND [Value] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9]%'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [counterparty].[PartyIdentityRevisions] WHERE [Attribute] = 1)
                BEGIN
                    THROW 51799, 'Tax identity revision history exists; RepairPartyIdentityRevisionTaxNumberFormat is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_PartyIdentityRevisions_Value",
                schema: "counterparty",
                table: "PartyIdentityRevisions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PartyIdentityRevisions_Value",
                schema: "counterparty",
                table: "PartyIdentityRevisions",
                sql: "LEN([Value]) > 0 AND ([Attribute] <> 1 OR (LEN([Value]) IN (10, 11) AND DATALENGTH([Value]) = LEN([Value]) AND [Value] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9]%'))");
        }
    }
}
