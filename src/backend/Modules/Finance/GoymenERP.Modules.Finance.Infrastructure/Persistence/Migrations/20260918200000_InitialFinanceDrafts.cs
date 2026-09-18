using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;

/// <summary>
/// Finance's first schema (FIN-T04a): manual bank receipt drafts and the command (idempotency)
/// record. Both are tenant tables; companies are Organization's, referenced by key only, so this
/// runs after Organization's migrations. No money, source or balance table exists here.
/// </summary>
[DbContext(typeof(FinanceDbContext))]
[Migration("20260918200000_InitialFinanceDrafts")]
public sealed class InitialFinanceDrafts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "finance");

        migrationBuilder.CreateTable(
            name: "ReceiptDrafts",
            schema: "finance",
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
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReceiptDrafts", x => x.Id);
                table.UniqueConstraint("AK_ReceiptDrafts_CompanyId_Id", x => new { x.CompanyId, x.Id });
                table.CheckConstraint("CK_ReceiptDrafts_Identity",
                    "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND [PartyId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND [TreasuryAccountId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_ReceiptDrafts_Text",
                    "LEN([AmountText]) > 0 AND LEN([ProfileId]) > 0 AND LEN([ProfileVersion]) > 0 "
                    + "AND LEN([CreatedBy]) > 0 AND LEN([UpdatedBy]) > 0");
                table.CheckConstraint("CK_ReceiptDrafts_Version", "[Version] >= 1");
                table.ForeignKey(
                    name: "FK_ReceiptDrafts_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Commands",
            schema: "finance",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false,
                    collation: "Latin1_General_100_BIN2"),
                IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false,
                    collation: "Latin1_General_100_BIN2"),
                CommandName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Commands", x => new { x.CompanyId, x.ActorId, x.IdempotencyKey });
                table.CheckConstraint("CK_Commands_Identity",
                    "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND LEN([ActorId]) > 0 AND LEN([IdempotencyKey]) > 0 AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0");
                table.CheckConstraint("CK_Commands_PayloadHash", "LEN([PayloadHash]) = 64");
                table.CheckConstraint("CK_Commands_ResultJson", "ISJSON([ResultJson]) = 1");
                table.ForeignKey(
                    name: "FK_Commands_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Commands_ReceiptDrafts_CompanyId_DraftId",
                    columns: x => new { x.CompanyId, x.DraftId },
                    principalSchema: "finance",
                    principalTable: "ReceiptDrafts",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ReceiptDrafts_CompanyId_UpdatedAtUtc_Id",
            schema: "finance",
            table: "ReceiptDrafts",
            columns: new[] { "CompanyId", "UpdatedAtUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_Commands_CompanyId_DraftId",
            schema: "finance",
            table: "Commands",
            columns: new[] { "CompanyId", "DraftId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Commands", schema: "finance");
        migrationBuilder.DropTable(name: "ReceiptDrafts", schema: "finance");
    }
}
