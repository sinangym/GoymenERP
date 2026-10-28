using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// SAT-D57: each company's customer (price) group list and the company account's group. The account's group is a
    /// composite foreign key (CompanyId, CustomerGroupId) to the group's (CompanyId, Id), so a group of another company
    /// cannot be assigned even by a direct write. No group value is seeded. Rollback is refused with 51091 while any
    /// group exists (an assignment needs a group), so no list or assignment is ever dropped silently; with no group the
    /// rollback drops the empty table and the all-null column.
    /// </summary>
    public partial class AddCounterpartyCustomerGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomerGroupId",
                schema: "counterparty",
                table: "CompanyPartyAccounts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerGroups",
                schema: "counterparty",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerGroups", x => x.Id);
                    table.UniqueConstraint("AK_CustomerGroups_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_CustomerGroups_Name", "LEN([Name]) > 0 AND [Name] = LTRIM(RTRIM([Name]))");
                    table.CheckConstraint("CK_CustomerGroups_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_CustomerGroups_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyPartyAccounts_CompanyId_CustomerGroupId",
                schema: "counterparty",
                table: "CompanyPartyAccounts",
                columns: new[] { "CompanyId", "CustomerGroupId" });

            migrationBuilder.CreateIndex(
                name: "UX_CustomerGroups_CompanyId_Name",
                schema: "counterparty",
                table: "CustomerGroups",
                columns: new[] { "CompanyId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CompanyPartyAccounts_CustomerGroups_CompanyId_CustomerGroupId",
                schema: "counterparty",
                table: "CompanyPartyAccounts",
                columns: new[] { "CompanyId", "CustomerGroupId" },
                principalSchema: "counterparty",
                principalTable: "CustomerGroups",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [counterparty].[CustomerGroups])
                    OR EXISTS (SELECT 1 FROM [counterparty].[CompanyPartyAccounts] WHERE [CustomerGroupId] IS NOT NULL)
                BEGIN
                    THROW 51091, 'Rollback blocked: counterparty customer groups or account group assignments exist. Keep the schema and use a compatible forward fix.', 1;
                END;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_CompanyPartyAccounts_CustomerGroups_CompanyId_CustomerGroupId",
                schema: "counterparty",
                table: "CompanyPartyAccounts");

            migrationBuilder.DropTable(
                name: "CustomerGroups",
                schema: "counterparty");

            migrationBuilder.DropIndex(
                name: "IX_CompanyPartyAccounts_CompanyId_CustomerGroupId",
                schema: "counterparty",
                table: "CompanyPartyAccounts");

            migrationBuilder.DropColumn(
                name: "CustomerGroupId",
                schema: "counterparty",
                table: "CompanyPartyAccounts");
        }
    }
}
