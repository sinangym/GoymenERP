using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpensePayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExpensePayments",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    SourceKind = table.Column<int>(type: "int", nullable: false),
                    TreasuryAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpenseItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    ProfileId = table.Column<string>(type: "varchar(36)", unicode: false, maxLength: 36, nullable: false),
                    ProfileVersion = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ValueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PreparedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PaymentConfirmed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpensePayments", x => x.Id);
                    table.UniqueConstraint("AK_ExpensePayments_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_ExpensePayments_Dates", "[ValueOn] IS NULL OR [ValueOn] >= [OccurredOn]");
                    table.CheckConstraint("CK_ExpensePayments_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [TreasuryAccountId] <> '00000000-0000-0000-0000-000000000000' AND [ExpenseItemId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_ExpensePayments_SourceKind", "[SourceKind] IN (0, 1)");
                    table.CheckConstraint("CK_ExpensePayments_Status", "([Status] = 0 AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [RecordedBy] IS NULL AND [RecordedAtUtc] IS NULL AND [PaymentConfirmed] = 0) OR ([Status] = 1 AND [ApprovedBy] IS NOT NULL AND LEN([ApprovedBy]) > 0 AND [ApprovedAtUtc] IS NOT NULL AND [ApprovedBy] <> [PreparedBy] AND [RecordedBy] IS NULL AND [RecordedAtUtc] IS NULL AND [PaymentConfirmed] = 0) OR ([Status] = 2 AND [ApprovedBy] IS NOT NULL AND LEN([ApprovedBy]) > 0 AND [ApprovedAtUtc] IS NOT NULL AND [ApprovedBy] <> [PreparedBy] AND [RecordedBy] IS NOT NULL AND LEN([RecordedBy]) > 0 AND [RecordedAtUtc] IS NOT NULL AND [PaymentConfirmed] = 1)");
                    table.CheckConstraint("CK_ExpensePayments_Text", "LEN([AmountText]) > 0 AND LEN([ProfileId]) > 0 AND LEN([ProfileVersion]) > 0 AND LEN([PreparedBy]) > 0 AND LEN(LTRIM(RTRIM([Description]))) > 0 AND ([Reference] IS NULL OR LEN(LTRIM(RTRIM([Reference]))) > 0)");
                    table.CheckConstraint("CK_ExpensePayments_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_ExpensePayments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExpensePaymentCommands",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    IdempotencyKeyHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PayloadHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpensePaymentCommands", x => x.Id);
                    table.CheckConstraint("CK_ExpensePaymentCommands_Content", "LEN([CommandName]) > 0 AND LEN([IdempotencyKeyHash]) = 64 AND LEN([ActorId]) > 0 AND LEN([PayloadHash]) = 64 AND ISJSON([ResultJson]) = 1 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_ExpensePaymentCommands_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND ([PaymentId] IS NULL OR [PaymentId] <> '00000000-0000-0000-0000-000000000000')");
                    table.ForeignKey(
                        name: "FK_ExpensePaymentCommands_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpensePaymentCommands_ExpensePayments_CompanyId_PaymentId",
                        columns: x => new { x.CompanyId, x.PaymentId },
                        principalSchema: "finance",
                        principalTable: "ExpensePayments",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePaymentCommands_CompanyId_CommandName_IdempotencyKeyHash",
                schema: "finance",
                table: "ExpensePaymentCommands",
                columns: new[] { "CompanyId", "CommandName", "IdempotencyKeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePaymentCommands_CompanyId_PaymentId",
                schema: "finance",
                table: "ExpensePaymentCommands",
                columns: new[] { "CompanyId", "PaymentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePayments_CompanyId_PreparedAtUtc_Id",
                schema: "finance",
                table: "ExpensePayments",
                columns: new[] { "CompanyId", "PreparedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePayments_CompanyId_SourceKind_TreasuryAccountId_Status",
                schema: "finance",
                table: "ExpensePayments",
                columns: new[] { "CompanyId", "SourceKind", "TreasuryAccountId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix (refusal 51296): dropping the tables would lose expense payments (a
            // recorded one is a money fact) and their accepted command attempts. An empty store rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[ExpensePayments]) OR EXISTS (SELECT 1 FROM [finance].[ExpensePaymentCommands])
                BEGIN
                    THROW 51296, 'Finance expense payments exist; AddExpensePayments is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "ExpensePaymentCommands",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "ExpensePayments",
                schema: "finance");
        }
    }
}
