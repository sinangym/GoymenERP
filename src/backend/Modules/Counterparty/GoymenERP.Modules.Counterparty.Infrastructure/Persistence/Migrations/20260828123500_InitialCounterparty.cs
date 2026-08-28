using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CounterpartyDbContext))]
[Migration("20260828123500_InitialCounterparty")]
public sealed class InitialCounterparty : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "counterparty");

        migrationBuilder.CreateTable(
            name: "Parties",
            schema: "counterparty",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                LegalName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                TradeName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Parties", x => x.Id);
                table.CheckConstraint(
                    "CK_Parties_Code_Format",
                    "[Code] LIKE N'CAR-[0-9][0-9][0-9][0-9][0-9][0-9]'");
                table.CheckConstraint(
                    "CK_Parties_Kind",
                    "[Kind] IN (N'LegalEntity', N'NaturalPerson')");
                table.CheckConstraint(
                    "CK_Parties_Version",
                    "[Version] >= 1");
            });

        migrationBuilder.CreateTable(
            name: "CompanyPartyAccounts",
            schema: "counterparty",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Roles = table.Column<int>(type: "int", nullable: false),
                ExternalIntegrationCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CompanyPartyAccounts", x => x.Id);
                table.UniqueConstraint(
                    "AK_CompanyPartyAccounts_CompanyId_Id",
                    x => new { x.CompanyId, x.Id });
                table.CheckConstraint(
                    "CK_CompanyPartyAccounts_Roles",
                    "[Roles] BETWEEN 1 AND 15");
                table.CheckConstraint(
                    "CK_CompanyPartyAccounts_Version",
                    "[Version] >= 1");
                table.ForeignKey(
                    name: "FK_CompanyPartyAccounts_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CompanyPartyAccounts_Parties_PartyId",
                    column: x => x.PartyId,
                    principalSchema: "counterparty",
                    principalTable: "Parties",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Parties_Code",
            schema: "counterparty",
            table: "Parties",
            column: "Code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Parties_IsActive",
            schema: "counterparty",
            table: "Parties",
            column: "IsActive");

        migrationBuilder.CreateIndex(
            name: "IX_CompanyPartyAccounts_CompanyId_IsActive",
            schema: "counterparty",
            table: "CompanyPartyAccounts",
            columns: new[] { "CompanyId", "IsActive" });

        migrationBuilder.CreateIndex(
            name: "IX_CompanyPartyAccounts_CompanyId_PartyId",
            schema: "counterparty",
            table: "CompanyPartyAccounts",
            columns: new[] { "CompanyId", "PartyId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CompanyPartyAccounts_PartyId",
            schema: "counterparty",
            table: "CompanyPartyAccounts",
            column: "PartyId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "CompanyPartyAccounts",
            schema: "counterparty");

        migrationBuilder.DropTable(
            name: "Parties",
            schema: "counterparty");
    }
}
