using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;

/// <summary>
/// FIN-T05a (card #66 5743678389): the record of a realized manual bank receipt.
/// <list type="bullet">
/// <item><c>ReceiptDrafts</c> gains its stage (0 draft, 1 recorded) and the record trace (who, when). Every stored
/// row is a draft, so the new stage column is added with 0 for the rows that exist; <c>CK_ReceiptDrafts_Stage</c>
/// then holds "a draft has no record trace, a recorded source has both".</item>
/// <item><c>MoneyEvents</c>: THE money event of a recorded source, one per (company, source, kind) by a unique index,
/// append-only, bound to the company's source by a composite foreign key. Its dates are kept apart as Ö5 keeps
/// them (transaction date, value date, recording instant).</item>
/// </list>
/// <see cref="Down"/> refuses with <c>THROW 51051</c> (Finance's module number, coordinator-assigned) while any money
/// event or any recorded source exists: a rollback would lose money facts. With none it drops exactly what Up added.
/// Forward fix otherwise.
/// </summary>
[DbContext(typeof(FinanceDbContext))]
[Migration("20260920090000_AddMoneyEvents")]
public sealed class AddMoneyEvents : Migration
{
    private const string EmptyGuid = "'00000000-0000-0000-0000-000000000000'";

    // Written out, not read from the model: this migration's text never changes with the model.
    private const string StageCheck =
        "([Stage] = 0 AND [RecordedAtUtc] IS NULL AND [RecordedBy] IS NULL) OR "
        + "([Stage] = 1 AND [RecordedAtUtc] IS NOT NULL AND [RecordedBy] IS NOT NULL AND LEN([RecordedBy]) > 0)";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "Stage",
            schema: "finance",
            table: "ReceiptDrafts",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "RecordedAtUtc",
            schema: "finance",
            table: "ReceiptDrafts",
            type: "datetimeoffset",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RecordedBy",
            schema: "finance",
            table: "ReceiptDrafts",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_ReceiptDrafts_Stage",
            schema: "finance",
            table: "ReceiptDrafts",
            sql: StageCheck);

        migrationBuilder.CreateTable(
            name: "MoneyEvents",
            schema: "finance",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
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
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MoneyEvents", x => x.Id);
                table.CheckConstraint("CK_MoneyEvents_Identity",
                    $"[Id] <> {EmptyGuid} AND [CompanyId] <> {EmptyGuid} AND [SourceId] <> {EmptyGuid} "
                    + $"AND [PartyId] <> {EmptyGuid} AND [CompanyAccountId] <> {EmptyGuid} "
                    + $"AND [TreasuryAccountId] <> {EmptyGuid} AND [CurrencyId] <> {EmptyGuid}");
                table.CheckConstraint("CK_MoneyEvents_Kind", "[Kind] = 1");
                table.CheckConstraint("CK_MoneyEvents_Text",
                    "LEN([AmountText]) > 0 AND LEN([ProfileId]) > 0 AND LEN([ProfileVersion]) > 0 "
                    + "AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
                table.ForeignKey(
                    name: "FK_MoneyEvents_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_MoneyEvents_ReceiptDrafts_CompanyId_SourceId",
                    columns: x => new { x.CompanyId, x.SourceId },
                    principalSchema: "finance",
                    principalTable: "ReceiptDrafts",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MoneyEvents_CompanyId_SourceId_Kind",
            schema: "finance",
            table: "MoneyEvents",
            columns: new[] { "CompanyId", "SourceId", "Kind" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Finance's module refusal (51051): a rollback never drops a money fact or a record trace.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [finance].[MoneyEvents]) OR EXISTS (SELECT 1 FROM [finance].[ReceiptDrafts] WHERE [Stage] <> 0)
                THROW 51051, 'Finance money events or recorded sources exist; AddMoneyEvents is not rolled back (forward fix).', 1;
            """);

        migrationBuilder.DropTable(name: "MoneyEvents", schema: "finance");
        migrationBuilder.DropCheckConstraint(name: "CK_ReceiptDrafts_Stage", schema: "finance", table: "ReceiptDrafts");
        migrationBuilder.DropColumn(name: "RecordedBy", schema: "finance", table: "ReceiptDrafts");
        migrationBuilder.DropColumn(name: "RecordedAtUtc", schema: "finance", table: "ReceiptDrafts");
        migrationBuilder.DropColumn(name: "Stage", schema: "finance", table: "ReceiptDrafts");
    }
}
