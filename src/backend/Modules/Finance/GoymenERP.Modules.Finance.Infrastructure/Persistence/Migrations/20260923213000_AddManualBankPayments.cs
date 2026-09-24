using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FinanceDbContext))]
[Migration("20260923213000_AddManualBankPayments")]
public sealed class AddManualBankPayments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ManualBankPayments", schema: "finance",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TreasuryAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                ProfileId = table.Column<string>(type: "varchar(36)", unicode: false, maxLength: 36, nullable: false),
                ProfileVersion = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                ValueOn = table.Column<DateOnly>(type: "date", nullable: true),
                BankReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                Status = table.Column<int>(type: "int", nullable: false),
                PreparedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: FinanceDbContext.IdentityCollation),
                PreparedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ApprovedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: FinanceDbContext.IdentityCollation),
                ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: FinanceDbContext.IdentityCollation),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                BankResultConfirmed = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ManualBankPayments", x => x.Id);
                table.UniqueConstraint("AK_ManualBankPayments_CompanyId_Id", x => new { x.CompanyId, x.Id });
                table.CheckConstraint("CK_ManualBankPayments_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PartyId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000' AND [TreasuryAccountId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_ManualBankPayments_Text", "LEN([AmountText]) > 0 AND LEN([ProfileId]) > 0 AND LEN([ProfileVersion]) > 0 AND LEN([PreparedBy]) > 0");
                table.CheckConstraint("CK_ManualBankPayments_Version", "[Version] >= 1");
                table.CheckConstraint("CK_ManualBankPayments_Status", FinanceDbContext.ManualBankPaymentStatusCheck);
                table.ForeignKey("FK_ManualBankPayments_Companies_CompanyId", x => x.CompanyId,
                    principalSchema: "organization", principalTable: "Companies", principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_ManualBankPayments_CompanyId_PreparedAtUtc_Id", "ManualBankPayments",
            new[] { "CompanyId", "PreparedAtUtc", "Id" }, schema: "finance");

        migrationBuilder.CreateTable(
            name: "OutgoingMoneyEvents", schema: "finance",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TreasuryAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                ProfileId = table.Column<string>(type: "varchar(36)", unicode: false, maxLength: 36, nullable: false),
                ProfileVersion = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                ValueOn = table.Column<DateOnly>(type: "date", nullable: true),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OutgoingMoneyEvents", x => x.Id);
                table.CheckConstraint("CK_OutgoingMoneyEvents_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PaymentId] <> '00000000-0000-0000-0000-000000000000' AND [PartyId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000' AND [TreasuryAccountId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_OutgoingMoneyEvents_Text", "LEN([AmountText]) > 0 AND LEN([ProfileId]) > 0 AND LEN([ProfileVersion]) > 0 AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
                table.ForeignKey("FK_OutgoingMoneyEvents_Companies_CompanyId", x => x.CompanyId,
                    principalSchema: "organization", principalTable: "Companies", principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_OutgoingMoneyEvents_ManualBankPayments_CompanyId_PaymentId",
                    x => new { x.CompanyId, x.PaymentId }, principalSchema: "finance",
                    principalTable: "ManualBankPayments", principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("UX_OutgoingMoneyEvents_CompanyId_PaymentId", "OutgoingMoneyEvents",
            new[] { "CompanyId", "PaymentId" }, schema: "finance", unique: true);

        migrationBuilder.DropCheckConstraint(name: "CK_Commands_Target", schema: "finance", table: "Commands");
        migrationBuilder.AddColumn<Guid>(name: "ManualBankPaymentId", schema: "finance", table: "Commands",
            type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddCheckConstraint(name: "CK_Commands_Target", schema: "finance", table: "Commands",
            sql: FinanceDbContext.TargetCheck);
        migrationBuilder.CreateIndex("IX_Commands_CompanyId_ManualBankPaymentId", "Commands",
            new[] { "CompanyId", "ManualBankPaymentId" }, schema: "finance");
        migrationBuilder.AddForeignKey(
            name: "FK_Commands_ManualBankPayments_CompanyId_ManualBankPaymentId",
            schema: "finance", table: "Commands", columns: new[] { "CompanyId", "ManualBankPaymentId" },
            principalSchema: "finance", principalTable: "ManualBankPayments",
            principalColumns: new[] { "CompanyId", "Id" }, onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Commands_ManualBankPayments_CompanyId_ManualBankPaymentId",
            schema: "finance", table: "Commands");
        migrationBuilder.DropIndex(
            name: "IX_Commands_CompanyId_ManualBankPaymentId", schema: "finance", table: "Commands");
        migrationBuilder.DropCheckConstraint(name: "CK_Commands_Target", schema: "finance", table: "Commands");
        migrationBuilder.DropColumn(name: "ManualBankPaymentId", schema: "finance", table: "Commands");
        migrationBuilder.AddCheckConstraint(
            name: "CK_Commands_Target", schema: "finance", table: "Commands",
            sql: "([DraftId] IS NOT NULL AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL) OR ([BankAccountId] IS NOT NULL AND [BankAccountId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [ReversalProposalId] IS NULL) OR ([ReversalProposalId] IS NOT NULL AND [ReversalProposalId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [BankAccountId] IS NULL) OR ([DraftId] IS NULL AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL AND JSON_VALUE([ResultJson], '$.outcome') = 'ValidationFailed')");
        migrationBuilder.DropTable(name: "OutgoingMoneyEvents", schema: "finance");
        migrationBuilder.DropTable(name: "ManualBankPayments", schema: "finance");
    }
}
