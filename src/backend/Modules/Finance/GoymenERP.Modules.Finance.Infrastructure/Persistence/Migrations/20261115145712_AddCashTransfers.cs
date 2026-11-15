using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCashTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CashTransfers",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    SourceKind = table.Column<int>(type: "int", nullable: false),
                    SourceAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceCurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceAmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    SourceProfileId = table.Column<string>(type: "varchar(36)", unicode: false, maxLength: 36, nullable: false),
                    SourceProfileVersion = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    TargetKind = table.Column<int>(type: "int", nullable: false),
                    TargetAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetCurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetAmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    TargetProfileId = table.Column<string>(type: "varchar(36)", unicode: false, maxLength: 36, nullable: false),
                    TargetProfileVersion = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    RateText = table.Column<string>(type: "varchar(29)", unicode: false, maxLength: 29, nullable: true),
                    OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ValueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TransferConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashTransfers", x => x.Id);
                    table.UniqueConstraint("AK_CashTransfers_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_CashTransfers_AccountKinds", "[SourceKind] IN (0, 1) AND [TargetKind] IN (0, 1)");
                    table.CheckConstraint("CK_CashTransfers_Confirmed", "[TransferConfirmed] = 1");
                    table.CheckConstraint("CK_CashTransfers_Dates", "[ValueOn] IS NULL OR [ValueOn] >= [OccurredOn]");
                    table.CheckConstraint("CK_CashTransfers_DistinctAccounts", "NOT ([SourceKind] = [TargetKind] AND [SourceAccountId] = [TargetAccountId])");
                    table.CheckConstraint("CK_CashTransfers_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SourceAccountId] <> '00000000-0000-0000-0000-000000000000' AND [TargetAccountId] <> '00000000-0000-0000-0000-000000000000' AND [SourceCurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [TargetCurrencyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_CashTransfers_Kind", "([Kind] = 0 AND [SourceCurrencyId] = [TargetCurrencyId] AND [SourceAmountText] = [TargetAmountText] AND [RateText] IS NULL) OR ([Kind] = 1 AND [SourceCurrencyId] <> [TargetCurrencyId] AND [RateText] IS NOT NULL AND LEN([RateText]) > 0)");
                    table.CheckConstraint("CK_CashTransfers_Text", "LEN([SourceAmountText]) > 0 AND LEN([TargetAmountText]) > 0 AND LEN([SourceProfileId]) > 0 AND LEN([SourceProfileVersion]) > 0 AND LEN([TargetProfileId]) > 0 AND LEN([TargetProfileVersion]) > 0 AND LEN([RecordedBy]) > 0 AND ([Reference] IS NULL OR LEN(LTRIM(RTRIM([Reference]))) > 0) AND ([Description] IS NULL OR LEN(LTRIM(RTRIM([Description]))) > 0)");
                    table.CheckConstraint("CK_CashTransfers_Version", "[Version] = 1");
                    table.ForeignKey(
                        name: "FK_CashTransfers_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashTransferCommands",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    IdempotencyKeyHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PayloadHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashTransferCommands", x => x.Id);
                    table.CheckConstraint("CK_CashTransferCommands_Content", "LEN([CommandName]) > 0 AND LEN([IdempotencyKeyHash]) = 64 AND LEN([ActorId]) > 0 AND LEN([PayloadHash]) = 64 AND ISJSON([ResultJson]) = 1 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_CashTransferCommands_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [TransferId] <> '00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "FK_CashTransferCommands_CashTransfers_CompanyId_TransferId",
                        columns: x => new { x.CompanyId, x.TransferId },
                        principalSchema: "finance",
                        principalTable: "CashTransfers",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashTransferCommands_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashTransferCommands_CompanyId_CommandName_IdempotencyKeyHash",
                schema: "finance",
                table: "CashTransferCommands",
                columns: new[] { "CompanyId", "CommandName", "IdempotencyKeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashTransferCommands_CompanyId_TransferId",
                schema: "finance",
                table: "CashTransferCommands",
                columns: new[] { "CompanyId", "TransferId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashTransfers_CompanyId_RecordedAtUtc_Id",
                schema: "finance",
                table: "CashTransfers",
                columns: new[] { "CompanyId", "RecordedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CashTransfers_CompanyId_SourceKind_SourceAccountId",
                schema: "finance",
                table: "CashTransfers",
                columns: new[] { "CompanyId", "SourceKind", "SourceAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashTransfers_CompanyId_TargetKind_TargetAccountId",
                schema: "finance",
                table: "CashTransfers",
                columns: new[] { "CompanyId", "TargetKind", "TargetAccountId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix (refusal 51458): dropping the tables would lose committed transfers and currency exchanges
            // (FNS-D24: each is the money fact of both legs) and their accepted command attempts. An empty store
            // rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[CashTransfers]) OR EXISTS (SELECT 1 FROM [finance].[CashTransferCommands])
                BEGIN
                    THROW 51458, 'Finance cash transfers exist; AddCashTransfers is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "CashTransferCommands",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "CashTransfers",
                schema: "finance");
        }
    }
}
