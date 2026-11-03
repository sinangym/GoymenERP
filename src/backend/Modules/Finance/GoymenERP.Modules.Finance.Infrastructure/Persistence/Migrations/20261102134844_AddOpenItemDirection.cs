using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// MOD-15 (FNS-D7): every open item gains its direction — 1 receivable (the party owes the company, a sales invoice
    /// installment) or 2 payable (the company owes the party, a purchase invoice installment). Every stored row is an
    /// approved official SALES invoice installment or a synthetic receivable, so Up fills 1 (FNS-D7: "Evet alacak say")
    /// and only then makes the column NOT NULL — no database default is left behind, so a new row must name its
    /// direction. <c>CK_OpenItems_Direction</c> holds the two values and ties each known official kind to its own one.
    /// <see cref="Down"/> refuses with Finance's module refusal (<c>THROW 51051</c>) while any payable is stored: a
    /// rollback would turn a supplier debt into a customer receivable. With none it drops exactly what Up added.
    /// </summary>
    public partial class AddOpenItemDirection : Migration
    {
        // Written out, not read from the model: this migration's text never changes with the model.
        private const string DirectionCheck =
            "[Direction] IN (1, 2) AND ([SourceKind] <> 'official.invoice-installment.v1' OR [Direction] = 1) "
            + "AND ([SourceKind] <> 'official.incoming-invoice-installment.v1' OR [Direction] = 2)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Direction",
                schema: "finance",
                table: "OpenItems",
                type: "int",
                nullable: true);

            migrationBuilder.Sql("UPDATE [finance].[OpenItems] SET [Direction] = 1 WHERE [Direction] IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "Direction",
                schema: "finance",
                table: "OpenItems",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_OpenItems_Direction",
                schema: "finance",
                table: "OpenItems",
                sql: DirectionCheck);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Finance's module refusal (51051): a rollback never relabels a stored supplier debt as a receivable.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[OpenItems] WHERE [Direction] = 2)
                    THROW 51051, 'Finance payable open items exist; AddOpenItemDirection is not rolled back (forward fix).', 1;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_OpenItems_Direction",
                schema: "finance",
                table: "OpenItems");

            migrationBuilder.DropColumn(
                name: "Direction",
                schema: "finance",
                table: "OpenItems");
        }
    }
}
