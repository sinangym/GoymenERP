using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPartyCollaterals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PartyCollaterals",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollateralId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    SourceKind = table.Column<int>(type: "int", nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    CollateralTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollateralTypeVersion = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AmountText = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    DocumentReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReleaseReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SupersededAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyCollaterals", x => x.Id);
                    table.UniqueConstraint("AK_PartyCollaterals_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_PartyCollaterals_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [CollateralId] <> '00000000-0000-0000-0000-000000000000' AND [PartyId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000' AND [CollateralTypeId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CollateralTypeVersion]) > 0 AND LEN([RecordedBy]) > 0");
                    table.CheckConstraint("CK_PartyCollaterals_Kind", "([Kind] = 1 AND [Revision] = 1 AND [ReleaseReason] IS NULL) OR ([Kind] = 2 AND [Revision] > 1 AND [ReleaseReason] IS NULL) OR ([Kind] = 3 AND [Revision] > 1 AND [ReleaseReason] IS NOT NULL AND LEN([ReleaseReason]) > 0)");
                    table.CheckConstraint("CK_PartyCollaterals_Payload", "[Direction] IN (1, 2) AND [SourceKind] = 1 AND [Version] >= 1 AND [Revision] >= 1 AND LEN([AmountText]) > 0 AND [AmountText] NOT LIKE '%[^0-9.]%' AND [AmountText] NOT LIKE '%.%.%' AND [AmountText] NOT LIKE '.%' AND [AmountText] NOT LIKE '%.' AND [AmountText] LIKE '%[1-9]%' AND ([ValidUntil] IS NULL OR [ValidUntil] >= [ValidFrom]) AND ([DocumentReference] IS NULL OR LEN([DocumentReference]) > 0)");
                    table.CheckConstraint("CK_PartyCollaterals_Status", "([Status] = 1 AND [SupersededAtUtc] IS NULL) OR ([Status] = 2 AND [SupersededAtUtc] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_PartyCollaterals_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PartyCollateralCommands",
                schema: "finance",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    PartyCollateralRowId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyCollateralCommands", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_PartyCollateralCommands_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([ActorId]) > 0 AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_PartyCollateralCommands_KeyHash", "LEN([IdempotencyKeyHash]) = 64");
                    table.CheckConstraint("CK_PartyCollateralCommands_PayloadHash", "LEN([PayloadHash]) = 64");
                    table.CheckConstraint("CK_PartyCollateralCommands_ResultJson", "ISJSON([ResultJson]) = 1");
                    table.CheckConstraint("CK_PartyCollateralCommands_Target", "([PartyCollateralRowId] IS NOT NULL AND [PartyCollateralRowId] <> '00000000-0000-0000-0000-000000000000') OR ([PartyCollateralRowId] IS NULL AND JSON_VALUE([ResultJson], '$.outcome') IN ('ValidationFailed', 'VersionConflict'))");
                    table.ForeignKey(
                        name: "FK_PartyCollateralCommands_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartyCollateralCommands_PartyCollaterals_CompanyId_PartyCollateralRowId",
                        columns: x => new { x.CompanyId, x.PartyCollateralRowId },
                        principalSchema: "finance",
                        principalTable: "PartyCollaterals",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PartyCollateralCommands_CompanyId_PartyCollateralRowId",
                schema: "finance",
                table: "PartyCollateralCommands",
                columns: new[] { "CompanyId", "PartyCollateralRowId" });

            migrationBuilder.CreateIndex(
                name: "IX_PartyCollaterals_Account",
                schema: "finance",
                table: "PartyCollaterals",
                columns: new[] { "CompanyId", "CompanyAccountId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PartyCollaterals_History",
                schema: "finance",
                table: "PartyCollaterals",
                columns: new[] { "CompanyId", "CompanyAccountId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_PartyCollaterals_Live",
                schema: "finance",
                table: "PartyCollaterals",
                columns: new[] { "CompanyId", "CollateralId", "Status" },
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_PartyCollaterals_Revision",
                schema: "finance",
                table: "PartyCollaterals",
                columns: new[] { "CompanyId", "CollateralId", "Revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a stored collateral revision or recorded attempt is the audited collateral history (FNS-D57);
            // dropping it would silently erase who recorded, changed or released which collateral. An empty store rolls
            // back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[PartyCollaterals])
                    OR EXISTS (SELECT 1 FROM [finance].[PartyCollateralCommands])
                BEGIN
                    THROW 51585, 'Party collateral records exist; AddPartyCollaterals is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "PartyCollateralCommands",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "PartyCollaterals",
                schema: "finance");
        }
    }
}
