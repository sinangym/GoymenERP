using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CounterpartyDbContext))]
[Migration("20260828073000_InitialCounterparty")]
public partial class InitialCounterparty : Migration
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
                Code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
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
                table.CheckConstraint("CK_Parties_Kind_Valid", "[Kind] IN (1, 2)");
                table.CheckConstraint("CK_Parties_Version_Positive", "[Version] > 0");
            });

        migrationBuilder.CreateTable(
            name: "PartyCodeCounters",
            schema: "counterparty",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false),
                LastValue = table.Column<long>(type: "bigint", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PartyCodeCounters", x => x.Id);
                table.CheckConstraint("CK_PartyCodeCounters_LastValue_NonNegative", "[LastValue] >= 0");
                table.CheckConstraint("CK_PartyCodeCounters_Singleton", "[Id] = 1");
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
                table.CheckConstraint(
                    "CK_CompanyPartyAccounts_CompanyId_NotEmpty",
                    "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_CompanyPartyAccounts_Roles_Valid", "[Roles] BETWEEN 1 AND 15");
                table.CheckConstraint("CK_CompanyPartyAccounts_Version_Positive", "[Version] > 0");
                table.ForeignKey(
                    name: "FK_CompanyPartyAccounts_Parties_PartyId",
                    column: x => x.PartyId,
                    principalSchema: "counterparty",
                    principalTable: "Parties",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.InsertData(
            schema: "counterparty",
            table: "PartyCodeCounters",
            columns: ["Id", "LastValue"],
            values: [1, 0L]);

        migrationBuilder.CreateIndex(
            name: "IX_Parties_Code",
            schema: "counterparty",
            table: "Parties",
            column: "Code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Parties_LegalName_Id",
            schema: "counterparty",
            table: "Parties",
            columns: ["LegalName", "Id"]);

        migrationBuilder.CreateIndex(
            name: "IX_CompanyPartyAccounts_PartyId",
            schema: "counterparty",
            table: "CompanyPartyAccounts",
            column: "PartyId");

        migrationBuilder.CreateIndex(
            name: "IX_CompanyPartyAccounts_CompanyId_IsActive_Id",
            schema: "counterparty",
            table: "CompanyPartyAccounts",
            columns: ["CompanyId", "IsActive", "Id"]);

        migrationBuilder.CreateIndex(
            name: "IX_CompanyPartyAccounts_CompanyId_PartyId",
            schema: "counterparty",
            table: "CompanyPartyAccounts",
            columns: ["CompanyId", "PartyId"],
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "CompanyPartyAccounts", schema: "counterparty");
        migrationBuilder.DropTable(name: "PartyCodeCounters", schema: "counterparty");
        migrationBuilder.DropTable(name: "Parties", schema: "counterparty");
    }
}
