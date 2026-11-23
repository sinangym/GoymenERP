using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPartyIbans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PartyIbanCommands",
                schema: "counterparty",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CommandName = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyIbanCommands", x => new { x.CompanyId, x.KeyHash });
                    table.CheckConstraint("CK_PartyIbanCommands_Evidence", "LEN([KeyHash]) = 64 AND LEN([PayloadHash]) = 64 AND LEN([CommandName]) > 0 AND LEN([ActorId]) > 0 AND ISJSON([ResultJson]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "PartyIbans",
                schema: "counterparty",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Iban = table.Column<string>(type: "varchar(34)", unicode: false, maxLength: 34, nullable: false, collation: "Latin1_General_100_BIN2"),
                    BankName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    AddedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    AddedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeactivatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    DeactivatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeactivationReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyIbans", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_PartyIbans_Content", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PartyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([Iban]) BETWEEN 15 AND 34 AND LEFT([Iban], 2) NOT LIKE '%[^A-Z]%' AND SUBSTRING([Iban], 3, 2) NOT LIKE '%[^0-9]%' AND [Iban] NOT LIKE '%[^A-Z0-9]%' AND (LEFT([Iban], 2) <> 'TR' OR (LEN([Iban]) = 26 AND SUBSTRING([Iban], 3, 24) NOT LIKE '%[^0-9]%')) AND LEN([BankName]) > 0 AND LEN([AddedBy]) > 0");
                    table.CheckConstraint("CK_PartyIbans_State", "([IsActive] = 1 AND [Version] = 1 AND [DeactivatedBy] IS NULL AND [DeactivatedAtUtc] IS NULL AND [DeactivationReason] IS NULL) OR ([IsActive] = 0 AND [Version] = 2 AND LEN([DeactivatedBy]) > 0 AND [DeactivatedAtUtc] IS NOT NULL AND LEN([DeactivationReason]) > 0)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PartyIbans_ActiveIban",
                schema: "counterparty",
                table: "PartyIbans",
                columns: new[] { "CompanyId", "Iban" },
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_PartyIbans_ActivePartyIban",
                schema: "counterparty",
                table: "PartyIbans",
                columns: new[] { "CompanyId", "PartyId", "Iban" },
                unique: true,
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // FNS-D121 forward fix: the cari IBAN list and its command records are audited owner data; dropping them would lose
            // them. An empty store rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [counterparty].[PartyIbans])
                   OR EXISTS (SELECT 1 FROM [counterparty].[PartyIbanCommands])
                BEGIN
                    THROW 52252, 'Cari IBAN list rows exist; AddPartyIbans is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "PartyIbanCommands",
                schema: "counterparty");

            migrationBuilder.DropTable(
                name: "PartyIbans",
                schema: "counterparty");
        }
    }
}
