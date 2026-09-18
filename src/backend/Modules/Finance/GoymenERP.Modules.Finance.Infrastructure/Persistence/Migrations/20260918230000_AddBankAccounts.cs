using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;

/// <summary>
/// FS0 (card #66 5731563923): the Finance bank account master, and the shared command record
/// naming exactly one target (a receipt draft or a bank account). The draft target column becomes
/// nullable; its foreign key and index are dropped around the change and recreated unchanged.
/// <c>Down</c> deletes no data: with any bank-account command stored it fails visibly instead.
/// </summary>
[DbContext(typeof(FinanceDbContext))]
[Migration("20260918230000_AddBankAccounts")]
public sealed class AddBankAccounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BankAccounts",
            schema: "finance",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                ProposedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false,
                    collation: "Latin1_General_100_BIN2"),
                ProposedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ApprovedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true,
                    collation: "Latin1_General_100_BIN2"),
                ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BankAccounts", x => x.Id);
                table.UniqueConstraint("AK_BankAccounts_CompanyId_Id", x => new { x.CompanyId, x.Id });
                table.CheckConstraint("CK_BankAccounts_Identity",
                    "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_BankAccounts_Text", "LEN([DisplayName]) > 0 AND LEN([ProposedBy]) > 0");
                table.CheckConstraint("CK_BankAccounts_Version", "[Version] >= 1");
                table.CheckConstraint("CK_BankAccounts_Approval",
                    "([Status] = 1 AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL) OR "
                    + "([Status] = 2 AND [ApprovedBy] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL AND LEN([ApprovedBy]) > 0 "
                    + "AND [ApprovedBy] <> [ProposedBy])");
                table.ForeignKey(
                    name: "FK_BankAccounts_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BankAccounts_CompanyId_Status",
            schema: "finance",
            table: "BankAccounts",
            columns: new[] { "CompanyId", "Status" });

        // The command record: DraftId becomes optional, BankAccountId is added, exactly one is set.
        migrationBuilder.DropForeignKey(
            name: "FK_Commands_ReceiptDrafts_CompanyId_DraftId",
            schema: "finance",
            table: "Commands");

        migrationBuilder.DropIndex(
            name: "IX_Commands_CompanyId_DraftId",
            schema: "finance",
            table: "Commands");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Commands_Identity",
            schema: "finance",
            table: "Commands");

        migrationBuilder.AlterColumn<Guid>(
            name: "DraftId",
            schema: "finance",
            table: "Commands",
            type: "uniqueidentifier",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uniqueidentifier");

        migrationBuilder.AddColumn<Guid>(
            name: "BankAccountId",
            schema: "finance",
            table: "Commands",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_Commands_Identity",
            schema: "finance",
            table: "Commands",
            sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND LEN([ActorId]) > 0 AND LEN([IdempotencyKey]) > 0 AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Commands_Target",
            schema: "finance",
            table: "Commands",
            sql: "([DraftId] IS NOT NULL AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [BankAccountId] IS NULL) OR "
                + "([BankAccountId] IS NOT NULL AND [BankAccountId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL)");

        migrationBuilder.CreateIndex(
            name: "IX_Commands_CompanyId_DraftId",
            schema: "finance",
            table: "Commands",
            columns: new[] { "CompanyId", "DraftId" });

        migrationBuilder.CreateIndex(
            name: "IX_Commands_CompanyId_BankAccountId",
            schema: "finance",
            table: "Commands",
            columns: new[] { "CompanyId", "BankAccountId" });

        migrationBuilder.AddForeignKey(
            name: "FK_Commands_ReceiptDrafts_CompanyId_DraftId",
            schema: "finance",
            table: "Commands",
            columns: new[] { "CompanyId", "DraftId" },
            principalSchema: "finance",
            principalTable: "ReceiptDrafts",
            principalColumns: new[] { "CompanyId", "Id" },
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Commands_BankAccounts_CompanyId_BankAccountId",
            schema: "finance",
            table: "Commands",
            columns: new[] { "CompanyId", "BankAccountId" },
            principalSchema: "finance",
            principalTable: "BankAccounts",
            principalColumns: new[] { "CompanyId", "Id" },
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Commands_BankAccounts_CompanyId_BankAccountId",
            schema: "finance",
            table: "Commands");

        migrationBuilder.DropForeignKey(
            name: "FK_Commands_ReceiptDrafts_CompanyId_DraftId",
            schema: "finance",
            table: "Commands");

        migrationBuilder.DropIndex(
            name: "IX_Commands_CompanyId_BankAccountId",
            schema: "finance",
            table: "Commands");

        migrationBuilder.DropIndex(
            name: "IX_Commands_CompanyId_DraftId",
            schema: "finance",
            table: "Commands");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Commands_Target",
            schema: "finance",
            table: "Commands");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Commands_Identity",
            schema: "finance",
            table: "Commands");

        migrationBuilder.DropColumn(
            name: "BankAccountId",
            schema: "finance",
            table: "Commands");

        // Fails visibly while a bank-account command row (DraftId NULL) exists; no data is deleted.
        migrationBuilder.AlterColumn<Guid>(
            name: "DraftId",
            schema: "finance",
            table: "Commands",
            type: "uniqueidentifier",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uniqueidentifier",
            oldNullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_Commands_Identity",
            schema: "finance",
            table: "Commands",
            sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND LEN([ActorId]) > 0 AND LEN([IdempotencyKey]) > 0 AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0");

        migrationBuilder.CreateIndex(
            name: "IX_Commands_CompanyId_DraftId",
            schema: "finance",
            table: "Commands",
            columns: new[] { "CompanyId", "DraftId" });

        migrationBuilder.AddForeignKey(
            name: "FK_Commands_ReceiptDrafts_CompanyId_DraftId",
            schema: "finance",
            table: "Commands",
            columns: new[] { "CompanyId", "DraftId" },
            principalSchema: "finance",
            principalTable: "ReceiptDrafts",
            principalColumns: new[] { "CompanyId", "Id" },
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.DropTable(name: "BankAccounts", schema: "finance");
    }
}
