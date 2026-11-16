using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CreditAccounts",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditGroup = table.Column<int>(type: "int", nullable: false),
                    LenderName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContractReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrincipalAmountText = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ProposedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ProposedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditAccounts", x => x.Id);
                    table.UniqueConstraint("AK_CreditAccounts_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_CreditAccounts_Amount", "LEN([PrincipalAmountText]) > 0 AND [PrincipalAmountText] NOT LIKE '%[^0-9.]%' AND [PrincipalAmountText] NOT LIKE '%.%.%' AND [PrincipalAmountText] NOT LIKE '.%' AND [PrincipalAmountText] NOT LIKE '%.' AND [PrincipalAmountText] LIKE '%[1-9]%'");
                    table.CheckConstraint("CK_CreditAccounts_Decision", "([Status] = 1 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL) OR ([Status] = 2 AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [ProposedBy] COLLATE Latin1_General_100_BIN2 AND [RejectionReason] IS NULL) OR ([Status] = 3 AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [ProposedBy] COLLATE Latin1_General_100_BIN2 AND [RejectionReason] IS NOT NULL AND LEN([RejectionReason]) > 0)");
                    table.CheckConstraint("CK_CreditAccounts_Group", "[CreditGroup] IN (1, 2, 3, 4)");
                    table.CheckConstraint("CK_CreditAccounts_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([ProposedBy]) > 0 AND LEN([LenderName]) > 0 AND LEN([ContractReference]) > 0");
                    table.CheckConstraint("CK_CreditAccounts_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_CreditAccounts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CreditAccountCommands",
                schema: "finance",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CreditAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditAccountCommands", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_CreditAccountCommands_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([ActorId]) > 0 AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_CreditAccountCommands_KeyHash", "LEN([IdempotencyKeyHash]) = 64");
                    table.CheckConstraint("CK_CreditAccountCommands_PayloadHash", "LEN([PayloadHash]) = 64");
                    table.CheckConstraint("CK_CreditAccountCommands_ResultJson", "ISJSON([ResultJson]) = 1");
                    table.CheckConstraint("CK_CreditAccountCommands_Target", "([CreditAccountId] IS NOT NULL AND [CreditAccountId] <> '00000000-0000-0000-0000-000000000000') OR ([CreditAccountId] IS NULL AND JSON_VALUE([ResultJson], '$.outcome') = 'ValidationFailed')");
                    table.ForeignKey(
                        name: "FK_CreditAccountCommands_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditAccountCommands_CreditAccounts_CompanyId_CreditAccountId",
                        columns: x => new { x.CompanyId, x.CreditAccountId },
                        principalSchema: "finance",
                        principalTable: "CreditAccounts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CreditAccountInstallments",
                schema: "finance",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    InstallmentText = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PrincipalText = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    InterestText = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    BsmvText = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    KkdfText = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditAccountInstallments", x => new { x.CompanyId, x.CreditAccountId, x.Sequence });
                    table.CheckConstraint("CK_CreditAccountInstallments_Amounts", "LEN([InstallmentText]) > 0 AND [InstallmentText] NOT LIKE '%[^0-9.]%' AND [InstallmentText] NOT LIKE '%.%.%' AND [InstallmentText] NOT LIKE '.%' AND [InstallmentText] NOT LIKE '%.' AND LEN([PrincipalText]) > 0 AND [PrincipalText] NOT LIKE '%[^0-9.]%' AND [PrincipalText] NOT LIKE '%.%.%' AND [PrincipalText] NOT LIKE '.%' AND [PrincipalText] NOT LIKE '%.' AND LEN([InterestText]) > 0 AND [InterestText] NOT LIKE '%[^0-9.]%' AND [InterestText] NOT LIKE '%.%.%' AND [InterestText] NOT LIKE '.%' AND [InterestText] NOT LIKE '%.' AND LEN([BsmvText]) > 0 AND [BsmvText] NOT LIKE '%[^0-9.]%' AND [BsmvText] NOT LIKE '%.%.%' AND [BsmvText] NOT LIKE '.%' AND [BsmvText] NOT LIKE '%.' AND LEN([KkdfText]) > 0 AND [KkdfText] NOT LIKE '%[^0-9.]%' AND [KkdfText] NOT LIKE '%.%.%' AND [KkdfText] NOT LIKE '.%' AND [KkdfText] NOT LIKE '%.'");
                    table.CheckConstraint("CK_CreditAccountInstallments_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [CreditAccountId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_CreditAccountInstallments_Sequence", "[Sequence] >= 1");
                    table.ForeignKey(
                        name: "FK_CreditAccountInstallments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditAccountInstallments_CreditAccounts_CompanyId_CreditAccountId",
                        columns: x => new { x.CompanyId, x.CreditAccountId },
                        principalSchema: "finance",
                        principalTable: "CreditAccounts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreditAccountCommands_CompanyId_CreditAccountId",
                schema: "finance",
                table: "CreditAccountCommands",
                columns: new[] { "CompanyId", "CreditAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditAccounts_Status",
                schema: "finance",
                table: "CreditAccounts",
                columns: new[] { "CompanyId", "Status", "ProposedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_CreditAccounts_Live",
                schema: "finance",
                table: "CreditAccounts",
                columns: new[] { "CompanyId", "LenderName", "ContractReference" },
                unique: true,
                filter: "[Status] IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[CreditAccounts])
                    OR EXISTS (SELECT 1 FROM [finance].[CreditAccountCommands])
                BEGIN
                    THROW 51851, 'Credit accounts or their command records exist; AddCreditAccounts is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "CreditAccountCommands",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "CreditAccountInstallments",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "CreditAccounts",
                schema: "finance");
        }
    }
}
